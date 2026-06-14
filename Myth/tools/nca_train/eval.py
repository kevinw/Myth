"""Loads an exported NCA weights .bin and renders a growth + cut/regrow
sequence as PNG frames, to sanity-check a trained model before dropping it
into the game.

Usage:
    .venv/bin/python eval.py Assets/NCA/heart_weights.bin --grid 32 --out /tmp/heart_preview
"""

import argparse
from pathlib import Path

import numpy as np
import torch
import torch.nn.functional as F
from PIL import Image

from train import CAModel, make_seed, HIDDEN_N, PERCEPT_N, CHANNEL_N, DIR_N


def load_weights(model: CAModel, path: Path):
    percept_n = model.conv1.in_channels
    blob = np.fromfile(path, dtype=np.float32)
    expected = percept_n * 128 + 128 + 128 * 16 + 16
    assert blob.size == expected, f"{blob.size} != {expected}"

    off = 0
    w0 = blob[off:off + percept_n * HIDDEN_N].reshape(percept_n, HIDDEN_N)  # (i,j)
    off += percept_n * HIDDEN_N
    b0 = blob[off:off + HIDDEN_N]
    off += HIDDEN_N
    w1 = blob[off:off + HIDDEN_N * CHANNEL_N].reshape(HIDDEN_N, CHANNEL_N)  # (j,k)
    off += HIDDEN_N * CHANNEL_N
    b1 = blob[off:off + CHANNEL_N]

    with torch.no_grad():
        model.conv1.weight.copy_(torch.from_numpy(w0.T.reshape(HIDDEN_N, percept_n, 1, 1)))
        model.conv1.bias.copy_(torch.from_numpy(b0))
        model.conv2.weight.copy_(torch.from_numpy(w1.T.reshape(CHANNEL_N, HIDDEN_N, 1, 1)))
        model.conv2.bias.copy_(torch.from_numpy(b1))


def to_image(x):
    # x: (1, 16, H, W) -> premultiplied RGBA -> straight RGBA uint8
    rgba = x[0, :4].clamp(0, 1).cpu().numpy()
    rgb, a = rgba[:3], rgba[3:4]
    a_safe = np.clip(a, 1e-4, 1.0)
    straight_rgb = np.clip(rgb / a_safe, 0, 1)
    out = np.concatenate([straight_rgb, a], axis=0)
    out = (out.transpose(1, 2, 0) * 255).astype(np.uint8)
    return Image.fromarray(out, "RGBA")


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("weights", type=Path)
    parser.add_argument("--grid", type=int, default=32)
    parser.add_argument("--out", type=Path, default=Path("/tmp/nca_preview"))
    parser.add_argument("--grow-steps", type=int, default=80)
    parser.add_argument("--regrow-steps", type=int, default=80)
    parser.add_argument("--scale", type=int, default=8)
    parser.add_argument("--direction-aware", action="store_true")
    parser.add_argument("--dir", type=float, nargs=2, default=[1.0, 0.0],
                         help="direction-to-player vector (x y) for direction-aware models")
    args = parser.parse_args()

    device = "mps" if torch.backends.mps.is_available() else "cpu"
    extra_inputs = DIR_N if args.direction_aware else 0
    model = CAModel(extra_inputs=extra_inputs, device=device).to(device)
    load_weights(model, args.weights)

    args.out.mkdir(parents=True, exist_ok=True)

    def make_extra(batch=1, h=args.grid, w=args.grid):
        if not args.direction_aware:
            return None
        d = torch.tensor(args.dir, device=device, dtype=torch.float32)
        return d.view(1, DIR_N, 1, 1).expand(batch, -1, h, w)

    x = make_seed(args.grid, device=device)
    with torch.no_grad():
        for i in range(args.grow_steps):
            x = model(x, extra=make_extra())
            if i % 10 == 0 or i == args.grow_steps - 1:
                img = to_image(x).resize(
                    (args.grid * args.scale, args.grid * args.scale), Image.NEAREST)
                img.save(args.out / f"grow_{i:03d}.png")

        # Cut a chunk out of the right half, then watch it regrow.
        h = w = args.grid
        x[:, :, h // 4: h // 2, w // 2:] = 0.0
        img = to_image(x).resize((w * args.scale, h * args.scale), Image.NEAREST)
        img.save(args.out / "cut_000.png")

        for i in range(args.regrow_steps):
            x = model(x, extra=make_extra())
            if i % 10 == 0 or i == args.regrow_steps - 1:
                img = to_image(x).resize((w * args.scale, h * args.scale), Image.NEAREST)
                img.save(args.out / f"regrow_{i:03d}.png")

    print(f"wrote frames to {args.out}")


if __name__ == "__main__":
    main()
