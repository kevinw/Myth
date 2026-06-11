"""Trains a Growing Neural Cellular Automata model
(https://distill.pub/2020/growing-ca/) to match TinyLink's NCAUpdate.hlsl /
NCAUpdateDirectional.hlsl exactly, so the exported weights can be dropped
straight into Assets/NCA/*.bin and run by NeuralCellularActor.

Usage:
    .venv/bin/python train.py targets/heart1.png Assets/NCA/heart_weights.bin --pad 8 --steps 4000

    # "Look at player" variant (NCAUpdateDirectional.hlsl):
    .venv/bin/python train.py targets/blob1.png Assets/NCA/blob_weights.bin \
        --pad 8 --steps 4000 --direction-aware
"""

import argparse
from pathlib import Path

import numpy as np
import torch
import torch.nn as nn
import torch.nn.functional as F
from PIL import Image

CHANNEL_N = 16
HIDDEN_N = 128
PERCEPT_N = CHANNEL_N * 3
DIR_N = 2
FIRE_RATE = 0.5
DIR_SHIFT_AMOUNT = 0.12  # fraction of grid half-size the target shifts by

# Matches sobel_x / sobel_y in NCAUpdate.hlsl.
SOBEL_X = torch.tensor([
    [-1.0, 0.0, 1.0],
    [-2.0, 0.0, 2.0],
    [-1.0, 0.0, 1.0],
]) / 8.0
SOBEL_Y = SOBEL_X.T
IDENTITY = torch.tensor([
    [0.0, 0.0, 0.0],
    [0.0, 1.0, 0.0],
    [0.0, 0.0, 0.0],
])


class CAModel(nn.Module):
    def __init__(self, channel_n=CHANNEL_N, hidden_n=HIDDEN_N, extra_inputs=0, device="cpu"):
        super().__init__()
        self.channel_n = channel_n
        self.extra_inputs = extra_inputs

        kernels = torch.stack([IDENTITY, SOBEL_X, SOBEL_Y])  # (3,3,3)
        # depthwise conv weight: (out=channel_n*3, in/groups=1, kh, kw), with
        # output channels grouped per input channel as [id, sx, sy] triples
        # so the flattened result is [c0_id, c0_sx, c0_sy, c1_id, ...]
        weight = kernels.unsqueeze(1).repeat(channel_n, 1, 1, 1)  # (channel_n*3, 1, 3, 3)
        self.register_buffer("perception_weight", weight.to(device))

        self.conv1 = nn.Conv2d(PERCEPT_N + extra_inputs, hidden_n, 1)
        self.conv2 = nn.Conv2d(hidden_n, channel_n, 1)
        nn.init.zeros_(self.conv2.weight)
        nn.init.zeros_(self.conv2.bias)

    def perceive(self, x):
        # x: (B, C, H, W) -> depthwise conv with groups=C, 3 filters per channel
        # produces (B, C*3, H, W) ordered [c0_id, c0_sx, c0_sy, c1_id, ...]
        C = x.shape[1]
        return F.conv2d(x, self.perception_weight, padding=1, groups=C)

    def forward(self, x, fire_rate=FIRE_RATE, extra=None):
        pre_alive = (F.max_pool2d(x[:, 3:4], 3, stride=1, padding=1) > 0.1).float()

        y = self.perceive(x)
        if extra is not None:
            y = torch.cat([y, extra], dim=1)
        y = F.relu(self.conv1(y))
        dx = self.conv2(y)

        update_mask = (torch.rand_like(x[:, :1]) <= fire_rate).float()
        x = x + dx * update_mask
        x = x * pre_alive
        return x


def make_seed(grid_size, channel_n=CHANNEL_N, device="cpu"):
    x = torch.zeros(1, channel_n, grid_size, grid_size, device=device)
    cx = cy = grid_size // 2
    x[:, 3:, cy, cx] = 1.0
    return x


def load_target(path, pad, device="cpu"):
    img = Image.open(path).convert("RGBA")
    arr = np.asarray(img, dtype=np.float32) / 255.0  # (H, W, 4)
    rgb = arr[..., :3]
    a = arr[..., 3:4]
    premul = np.concatenate([rgb * a, a], axis=-1)  # premultiplied RGBA

    h, w, _ = premul.shape
    grid_h, grid_w = h + 2 * pad, w + 2 * pad
    canvas = np.zeros((grid_h, grid_w, 4), dtype=np.float32)
    canvas[pad:pad + h, pad:pad + w, :] = premul

    t = torch.from_numpy(canvas).permute(2, 0, 1).unsqueeze(0).to(device)  # (1,4,H,W)
    return t, grid_h, grid_w


def random_damage_(x, max_frac=0.4):
    """Zero out a random rectangle in-place, mimicking Cut()."""
    _, c, h, w = x.shape
    rw = max(2, int(w * np.random.uniform(0.1, max_frac)))
    rh = max(2, int(h * np.random.uniform(0.1, max_frac)))
    x0 = np.random.randint(0, max(1, w - rw))
    y0 = np.random.randint(0, max(1, h - rh))
    x[:, :, y0:y0 + rh, x0:x0 + rw] = 0.0


def sample_dirs(n, device="cpu"):
    """Random unit direction vectors; ~30% are zero (no target nearby)."""
    angles = torch.rand(n, device=device) * 2 * np.pi
    dirs = torch.stack([torch.cos(angles), torch.sin(angles)], dim=-1)
    zero_mask = torch.rand(n, device=device) < 0.3
    dirs[zero_mask] = 0.0
    return dirs


def shift_targets(target, dirs, shift_amount=DIR_SHIFT_AMOUNT):
    """Shifts `target` (1,4,H,W) toward each direction in `dirs` (B,2),
    producing a per-sample target (B,4,H,W). A creature trained against
    these learns to lean its growth toward the input direction vector."""
    B = dirs.shape[0]
    H, W = target.shape[2], target.shape[3]
    theta = torch.zeros(B, 2, 3, device=dirs.device, dtype=dirs.dtype)
    theta[:, 0, 0] = 1.0
    theta[:, 1, 1] = 1.0
    theta[:, 0, 2] = -dirs[:, 0] * shift_amount
    theta[:, 1, 2] = -dirs[:, 1] * shift_amount
    grid = F.affine_grid(theta, [B, 4, H, W], align_corners=False)
    base = target.expand(B, -1, -1, -1)
    return F.grid_sample(base, grid, align_corners=False, padding_mode="zeros")


def to_rgba(x):
    return x[:, :4]


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("target", type=Path)
    parser.add_argument("output", type=Path)
    parser.add_argument("--pad", type=int, default=8)
    parser.add_argument("--steps", type=int, default=2000)
    parser.add_argument("--pool-size", type=int, default=256)
    parser.add_argument("--batch-size", type=int, default=8)
    parser.add_argument("--damage-after", type=int, default=500,
                         help="start applying damage augmentation after this many steps")
    parser.add_argument("--direction-aware", action="store_true",
                         help="train with broadcast direction-to-player perception inputs "
                              "(NCAUpdateDirectional.hlsl)")
    parser.add_argument("--lr", type=float, default=2e-3)
    parser.add_argument("--log-every", type=int, default=100)
    args = parser.parse_args()

    device = "mps" if torch.backends.mps.is_available() else "cpu"
    print(f"device: {device}")

    target, grid_h, grid_w = load_target(args.target, args.pad, device=device)
    print(f"grid: {grid_w}x{grid_h}  direction_aware={args.direction_aware}")

    extra_inputs = DIR_N if args.direction_aware else 0
    model = CAModel(extra_inputs=extra_inputs, device=device).to(device)
    optimizer = torch.optim.Adam(model.parameters(), lr=args.lr)
    scheduler = torch.optim.lr_scheduler.MultiStepLR(
        optimizer, milestones=[args.steps // 2], gamma=0.1)

    seed = make_seed(grid_w, device=device)  # assumes square grid
    pool = seed.repeat(args.pool_size, 1, 1, 1).clone()
    pool_dirs = sample_dirs(args.pool_size, device=device)

    for i in range(args.steps):
        idx = np.random.choice(args.pool_size, args.batch_size, replace=False)
        x = pool[idx].clone()
        dirs_a = pool_dirs[idx].clone()
        dirs_b = dirs_a.clone()

        with torch.no_grad():
            ref_target = shift_targets(target, dirs_a) if args.direction_aware else target
            losses = ((to_rgba(x) - ref_target) ** 2).mean(dim=[1, 2, 3])
            order = torch.argsort(losses, descending=True)
            x = x[order]
            idx = idx[order.cpu().numpy()]
            dirs_a = dirs_a[order]
            dirs_b = dirs_b[order]

            x[0] = seed[0]
            if args.direction_aware:
                dirs_a[0] = sample_dirs(1, device=device)[0]
                dirs_b[0] = dirs_a[0]

            if i >= args.damage_after:
                for b in range(args.batch_size // 2, args.batch_size):
                    random_damage_(x[b:b + 1])

            # Mid-rollout direction switches teach the model to re-orient an
            # already-grown body toward a new target, not just grow that way.
            switch_mask = torch.zeros(args.batch_size, dtype=torch.bool, device=device)
            if args.direction_aware:
                switch_mask = torch.rand(args.batch_size, device=device) < 0.5
                dirs_b[switch_mask] = sample_dirs(int(switch_mask.sum().item()), device=device)

        n_steps = np.random.randint(64, 96)
        switch_step = n_steps // 2
        cur_dirs = dirs_a

        for step in range(n_steps):
            if step == switch_step:
                cur_dirs = dirs_b

            extra = None
            if args.direction_aware:
                extra = cur_dirs.view(args.batch_size, DIR_N, 1, 1).expand(-1, -1, grid_h, grid_w)

            x = model(x, extra=extra)

        target_b = shift_targets(target, dirs_b) if args.direction_aware else target
        loss = ((to_rgba(x) - target_b) ** 2).mean()

        optimizer.zero_grad()
        loss.backward()
        for p in model.parameters():
            if p.grad is not None:
                p.grad = p.grad / (p.grad.norm() + 1e-8)
        optimizer.step()
        scheduler.step()

        pool[idx] = x.detach()
        pool_dirs[idx] = dirs_b

        if i % args.log_every == 0 or i == args.steps - 1:
            print(f"step {i:5d}  loss {loss.item():.6f}")

    export_weights(model, args.output)
    print(f"wrote {args.output}")


def export_weights(model: CAModel, output: Path):
    # Layout matches NCAUpdate.hlsl / NCAUpdateDirectional.hlsl:
    #   w0: PERCEPT_N x 128, offset 0          (index = i*128+j)
    #   b0: 128,             offset PERCEPT_N*128
    #   w1: 128 x 16,        offset b0_off+128 (index = j*16+k)
    #   b1: 16,              offset w1_off+128*16
    percept_n = model.conv1.in_channels

    w0 = model.conv1.weight.detach().cpu().numpy().reshape(HIDDEN_N, percept_n)  # (j,i)
    b0 = model.conv1.bias.detach().cpu().numpy()  # (j,)
    w1 = model.conv2.weight.detach().cpu().numpy().reshape(CHANNEL_N, HIDDEN_N)  # (k,j)
    b1 = model.conv2.bias.detach().cpu().numpy()  # (k,)

    w0_flat = w0.T.reshape(-1)  # (i,j) -> i*128+j
    w1_flat = w1.T.reshape(-1)  # (j,k) -> j*16+k

    blob = np.concatenate([
        w0_flat.astype(np.float32),
        b0.astype(np.float32),
        w1_flat.astype(np.float32),
        b1.astype(np.float32),
    ])

    expected = percept_n * 128 + 128 + 128 * 16 + 16
    assert blob.size == expected, f"{blob.size} != {expected}"

    output.parent.mkdir(parents=True, exist_ok=True)
    blob.tofile(output)


if __name__ == "__main__":
    main()
