"""Trains a "self-organizing texture" Neural Cellular Automata model that
grows an organic texture (e.g. fungus) outward from scattered seed cells,
matching NCAUpdate.hlsl exactly so it can be dropped into Assets/NCA/*.bin
and run by NCALayer.

Unlike train.py (which grows a single fixed-shape creature from one center
seed), this trains from many scattered seeds toward a *texture* loss: the
Gram matrix of perception features at several scales must match the target
image's. This lets the same local rule fill an arbitrarily large/irregular
region with a fungus-like pattern.

Two properties tuned for the in-game "fungus on a wall" look:

  * Seamless (--seamless, default on): perception uses CIRCULAR padding, so
    the model never sees a hard grid boundary during training and never
    learns the bright beveled rim that otherwise rings the painted region.

  * Patchy (--coverage < 1): the target image is masked by an organic noise
    field with empty gaps and trained as premultiplied RGBA, so the texture
    grows in islands/tendrils instead of saturating the whole region. The
    alive-fraction loss targets `--coverage`, not 100%.

Usage:
    .venv/bin/python train_texture.py \
        "../../Production/Neural Texture Inputs/Fungus1.jpg" \
        ../../Assets/NCA/fungus_weights.bin --size 48 --steps 4000 \
        --coverage 0.55
"""

import argparse
from pathlib import Path

import numpy as np
import torch
import torch.nn as nn
import torch.nn.functional as F
from PIL import Image

from train import CAModel, CHANNEL_N, HIDDEN_N, export_weights, random_damage_, SOBEL_X, SOBEL_Y, IDENTITY

RGBA_KERNELS = torch.stack([IDENTITY, SOBEL_X, SOBEL_Y])  # (3,3,3)

# Toggled by main(); when True, all perception convs wrap around the grid
# (circular padding) so trained textures are seamless and edge-artifact free.
SEAMLESS = True


def _pad(x):
    return F.pad(x, (1, 1, 1, 1), mode="circular") if SEAMLESS else F.pad(x, (1, 1, 1, 1))


def perceive_rgba(x, weight):
    # x: (B,4,H,W) -> (B,12,H,W), [c0_id, c0_sx, c0_sy, c1_id, ...]
    return F.conv2d(_pad(x), weight, padding=0, groups=4)


def circular_perceive(self, x):
    # Drop-in replacement for CAModel.perceive that wraps the grid instead of
    # zero-padding, so the model is trained on a boundary-free torus.
    C = x.shape[1]
    return F.conv2d(_pad(x), self.perception_weight, padding=0, groups=C)


def make_patchy_alpha(size, coverage, feature_scale=6, sharpness=10.0, device="cpu"):
    """Organic alpha mask: low-frequency value noise thresholded so roughly
    `coverage` fraction of cells are opaque, with soft (smoothstepped) edges.
    coverage >= 1 returns a fully-opaque mask (the old dense behavior)."""
    if coverage >= 0.999:
        return torch.ones(1, 1, size, size, device=device)

    low = max(2, size // feature_scale)
    noise = torch.rand(1, 1, low, low, device=device)
    noise = F.interpolate(noise, size=(size, size), mode="bicubic", align_corners=False)
    noise = noise.clamp(0.0, 1.0)

    flat = noise.flatten()
    k = max(1, int((1.0 - coverage) * flat.numel()))
    thresh = flat.kthvalue(k).values
    return torch.sigmoid((noise - thresh) * sharpness)


def load_target(path, size, coverage, device):
    img = Image.open(path).convert("RGB")
    img = img.resize((size, size), Image.LANCZOS)
    arr = np.asarray(img, dtype=np.float32) / 255.0  # (H,W,3)
    rgb = torch.from_numpy(arr).permute(2, 0, 1).unsqueeze(0).to(device)  # (1,3,H,W)

    alpha = make_patchy_alpha(size, coverage, device=device)  # (1,1,H,W)
    rgb = rgb * alpha  # premultiplied: holes are truly empty
    return torch.cat([rgb, alpha], dim=1)  # (1,4,H,W)


def make_seed_scatter(size, batch, channel_n=CHANNEL_N, min_seeds=3, max_seeds=8, device="cpu"):
    x = torch.zeros(batch, channel_n, size, size, device=device)
    for b in range(batch):
        n = np.random.randint(min_seeds, max_seeds + 1)
        ys = np.random.randint(0, size, size=n)
        xs = np.random.randint(0, size, size=n)
        x[b, 3:, ys, xs] = 1.0
    return x


def gram_matrix(feat):
    # feat: (B,C,H,W) -> (B,C,C), normalized by spatial size
    b, c, h, w = feat.shape
    f = feat.reshape(b, c, h * w)
    return torch.bmm(f, f.transpose(1, 2)) / (h * w)


def multiscale_grams(rgba, weight, scales=(1, 2, 4)):
    grams = []
    for s in scales:
        t = rgba if s == 1 else F.avg_pool2d(rgba, s)
        feat = perceive_rgba(t, weight)
        grams.append(gram_matrix(feat))
    return grams


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("target", type=Path)
    parser.add_argument("output", type=Path)
    parser.add_argument("--size", type=int, default=48)
    parser.add_argument("--steps", type=int, default=4000)
    parser.add_argument("--pool-size", type=int, default=32)
    parser.add_argument("--batch-size", type=int, default=4)
    parser.add_argument("--damage-after", type=int, default=1000)
    parser.add_argument("--coverage", type=float, default=0.55,
                        help="target opaque fraction (1.0 = dense mat, lower = patchier)")
    parser.add_argument("--no-seamless", action="store_true",
                        help="use zero padding instead of circular (keeps the boundary rim)")
    parser.add_argument("--lr", type=float, default=2e-3)
    parser.add_argument("--log-every", type=int, default=100)
    args = parser.parse_args()

    global SEAMLESS
    SEAMLESS = not args.no_seamless

    device = "mps" if torch.backends.mps.is_available() else "cpu"
    print(f"device: {device}  seamless: {SEAMLESS}  coverage: {args.coverage}")

    target = load_target(args.target, args.size, args.coverage, device=device)  # (1,4,S,S)
    print(f"target mean alpha: {target[:, 3].mean().item():.3f}")
    rgba_weight = RGBA_KERNELS.unsqueeze(1).repeat(4, 1, 1, 1).to(device)  # (12,1,3,3)

    with torch.no_grad():
        target_grams = multiscale_grams(target, rgba_weight)

    model = CAModel(channel_n=CHANNEL_N, hidden_n=HIDDEN_N, extra_inputs=0, device=device).to(device)
    if SEAMLESS:
        import types
        model.perceive = types.MethodType(circular_perceive, model)
    optimizer = torch.optim.Adam(model.parameters(), lr=args.lr)
    scheduler = torch.optim.lr_scheduler.MultiStepLR(optimizer, milestones=[args.steps // 2], gamma=0.1)

    pool = make_seed_scatter(args.size, args.pool_size, device=device)
    target_alive = target[:, 3].mean()

    for i in range(args.steps):
        idx = np.random.choice(args.pool_size, args.batch_size, replace=False)
        x = pool[idx].clone()

        with torch.no_grad():
            # Reset the worst sample in the batch back to a fresh scatter so
            # the model keeps practicing growth from sparse seeds, not just
            # refining an already-grown texture.
            losses = x[:, 3].mean(dim=[1, 2])
            worst = torch.argmin(losses)
            x[worst] = make_seed_scatter(args.size, 1, device=device)[0]

            if i >= args.damage_after:
                for b in range(args.batch_size // 2, args.batch_size):
                    random_damage_(x[b:b + 1], max_frac=0.3)

        n_steps = np.random.randint(48, 80)
        for _ in range(n_steps):
            x = model(x)

        rgba = x[:, :4]
        grams = multiscale_grams(rgba, rgba_weight)
        loss = sum(((g - tg.expand_as(g)) ** 2).mean() for g, tg in zip(grams, target_grams))

        # Drive overall coverage toward the (patchy) target's alive fraction
        # rather than fully saturating the region.
        alive_loss = (rgba[:, 3].mean() - target_alive) ** 2
        loss = loss + alive_loss

        # Keep channel values bounded so the simulation stays stable.
        overflow_loss = (x - x.clamp(-2.0, 2.0)).abs().mean()
        loss = loss + overflow_loss

        optimizer.zero_grad()
        loss.backward()
        for p in model.parameters():
            if p.grad is not None:
                p.grad = p.grad / (p.grad.norm() + 1e-8)
        optimizer.step()
        scheduler.step()

        pool[idx] = x.detach()

        if i % args.log_every == 0 or i == args.steps - 1:
            print(f"step {i:5d}  loss {loss.item():.6f}  alive {rgba[:, 3].mean().item():.3f}")

    export_weights(model, args.output)
    print(f"wrote {args.output}")


if __name__ == "__main__":
    main()
