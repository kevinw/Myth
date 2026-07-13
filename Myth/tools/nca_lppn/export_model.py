"""Exports a Cells2Pixels PENCA checkpoint for NeuralTextureActor."""

import argparse
from pathlib import Path

import numpy as np
import torch

from import_model import TENSORS


def require_tensor(state, name, shape):
    if name not in state:
        raise ValueError(f"checkpoint is missing {name}")
    value = state[name].detach().cpu().to(torch.float32).numpy()
    if list(value.shape) != shape:
        raise ValueError(f"{name}: expected {shape}, got {list(value.shape)}")
    return value


def output_channel_count(lppn):
    if "net.3.weight" not in lppn:
        raise ValueError("checkpoint is missing net.3.weight")
    channels = lppn["net.3.weight"].shape[0]
    if channels not in (3, 6):
        raise ValueError(f"net.3.weight has {channels} output channels; expected 3 (color) or 6 (color+normal)")
    return channels


def checkpoint_tensors(model, lppn, channels):
    # The final bias is padded so every packed tensor is float4-aligned.
    padded_bias = (channels + 3) // 4 * 4
    w1 = require_tensor(model, "w1.weight", [128, 66, 1, 1])[:, :, 0, 0]
    perception = w1[:, :-2].reshape(128, 16, 4).swapaxes(1, 2).reshape(128, 64)
    w1 = np.pad(np.concatenate([perception, w1[:, -2:]], axis=1), ((0, 0), (0, 2)))
    tensors = {
        "nca.w1.weight": w1,
        "nca.w1.bias": require_tensor(model, "w1.bias", [128]),
        "nca.w2.weight.T": require_tensor(model, "w2.weight", [16, 128, 1, 1])[:, :, 0, 0].T,
        "lppn.net.0.linear.weight": require_tensor(lppn, "net.0.linear.weight", [32, 20]),
        "lppn.net.0.linear.bias": require_tensor(lppn, "net.0.linear.bias", [32]),
        "lppn.net.1.linear.weight": require_tensor(lppn, "net.1.linear.weight", [32, 32]),
        "lppn.net.1.linear.bias": require_tensor(lppn, "net.1.linear.bias", [32]),
        "lppn.net.2.linear.weight": require_tensor(lppn, "net.2.linear.weight", [32, 32]),
        "lppn.net.2.linear.bias": require_tensor(lppn, "net.2.linear.bias", [32]),
        "lppn.net.3.weight": require_tensor(lppn, "net.3.weight", [channels, 32]),
    }
    final_bias = require_tensor(lppn, "net.3.bias", [channels])
    tensors["lppn.net.3.bias"] = np.pad(final_bias, (0, padded_bias - channels))
    return tensors, padded_bias


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("checkpoint", type=Path, help="Experiment directory containing model.pth and siren.pth")
    parser.add_argument("output", type=Path)
    args = parser.parse_args()

    model = torch.load(args.checkpoint / "model.pth", map_location="cpu", weights_only=True)
    lppn = torch.load(args.checkpoint / "siren.pth", map_location="cpu", weights_only=True)
    channels = output_channel_count(lppn)
    tensors, padded_bias = checkpoint_tensors(model, lppn, channels)

    layout = [
        (name, shape) for name, shape in TENSORS
        if name not in ("lppn.net.3.weight", "lppn.net.3.bias")
    ] + [
        ("lppn.net.3.weight", [channels, 32]),
        ("lppn.net.3.bias", [padded_bias]),
    ]

    packed = bytearray()
    for name, shape in layout:
        value = tensors[name]
        if list(value.shape) != shape:
            raise ValueError(f"{name}: runtime expects {shape}, got {list(value.shape)}")
        packed.extend(np.asarray(value, dtype="<f4").tobytes(order="C"))

    expected_bytes = 4 * sum(np.prod(shape) for _, shape in layout)
    if len(packed) != expected_bytes:
        raise ValueError(f"expected {expected_bytes} packed bytes, got {len(packed)}")

    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_bytes(packed)
    print(f"Wrote {args.output} ({len(packed) // 4} float32 weights, {channels} output channels)")


if __name__ == "__main__":
    main()
