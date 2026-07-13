"""Packs a Cells2Pixels dynamic-texture JSON model for NeuralTextureActor.

Usage:
    python3 import_model.py chequered_0050-circular.json \
        ../../Assets/NCA/chequered_0050_circular.bin
"""

import argparse
import base64
import json
import struct
from pathlib import Path


TENSORS = [
    ("nca.w1.weight", [128, 68]),
    ("nca.w1.bias", [128]),
    ("nca.w2.weight.T", [128, 16]),
    ("lppn.net.0.linear.weight", [32, 20]),
    ("lppn.net.0.linear.bias", [32]),
    ("lppn.net.1.linear.weight", [32, 32]),
    ("lppn.net.1.linear.bias", [32]),
    ("lppn.net.2.linear.weight", [32, 32]),
    ("lppn.net.2.linear.bias", [32]),
    ("lppn.net.3.weight", [3, 32]),
    ("lppn.net.3.bias", [4]),
]


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("source", type=Path)
    parser.add_argument("output", type=Path)
    args = parser.parse_args()

    model = json.loads(args.source.read_text())
    if set(model) != {name for name, _ in TENSORS}:
        raise ValueError("model tensor set does not match the supported dynamic-texture architecture")

    packed = bytearray()
    for name, shape in TENSORS:
        tensor = model[name]
        if tensor.get("shape") != shape or tensor.get("dtype") != "float32":
            raise ValueError(f"{name}: expected float32 {shape}, got {tensor.get('dtype')} {tensor.get('shape')}")

        data = base64.b64decode(tensor["data64"], validate=True)
        expected = 4
        for dimension in shape:
            expected *= dimension
        if len(data) != expected:
            raise ValueError(f"{name}: expected {expected} bytes, got {len(data)}")

        # Fail early on a checkpoint written with unexpected byte order.
        struct.unpack(f"<{len(data) // 4}f", data)
        packed.extend(data)

    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_bytes(packed)
    print(f"wrote {args.output} ({len(packed) // 4} float32 weights)")


if __name__ == "__main__":
    main()
