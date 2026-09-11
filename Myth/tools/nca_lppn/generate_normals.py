#!/usr/bin/env python3
"""Generate paired color and normal targets from photos using local depth estimation."""

import argparse
import hashlib
import logging
import os
import shutil
import sys
import tempfile
import urllib.request
import zipfile
from pathlib import Path

from train import ROOT, VENV, resolve_target


MODEL_URL = "https://ml-assets.apple.com/coreml/models/Image/DepthEstimation/DepthAnything/DepthAnythingV2SmallF16.mlpackage.zip"
MODEL_SHA256 = "8e875979ec82fa46f292468a7567a520b2aa07c41008c32141f3396da05a4067"
MODEL_PATH = ROOT / "data" / "depth-anything-v2-small" / "DepthAnythingV2SmallF16.mlpackage"
MODEL_SIZE = (518, 392)
DEFAULT_OUTPUT = ROOT / "targets" / "generated"


def prepare_model():
    if MODEL_PATH.is_dir():
        return
    MODEL_PATH.parent.mkdir(parents=True, exist_ok=True)
    print("Downloading Apple's Depth Anything V2 Small model")
    with tempfile.TemporaryDirectory() as temporary:
        temporary = Path(temporary)
        archive = temporary / "model.zip"
        urllib.request.urlretrieve(MODEL_URL, archive)
        with archive.open("rb") as file:
            checksum = hashlib.file_digest(file, "sha256").hexdigest()
        if checksum != MODEL_SHA256:
            raise RuntimeError("Downloaded Depth Anything model checksum does not match")
        with zipfile.ZipFile(archive) as package:
            package.extractall(temporary)
        extracted = temporary / MODEL_PATH.name
        if not extracted.is_dir():
            raise RuntimeError(f"Downloaded archive does not contain {MODEL_PATH.name}")
        shutil.move(extracted, MODEL_PATH)


def gaussian_blur(image, sigma, seamless):
    if sigma <= 0:
        return image
    radius = max(1, int(np.ceil(sigma * 3)))
    offsets = np.arange(-radius, radius + 1)
    kernel = np.exp(-0.5 * np.square(offsets / sigma))
    kernel /= kernel.sum()
    mode = "wrap" if seamless else "edge"
    blurred = np.apply_along_axis(
        lambda row: np.convolve(row, kernel, mode="valid"),
        1,
        np.pad(image, ((0, 0), (radius, radius)), mode=mode),
    )
    return np.apply_along_axis(
        lambda column: np.convolve(column, kernel, mode="valid"),
        0,
        np.pad(blurred, ((radius, radius), (0, 0)), mode=mode),
    ).astype(np.float32)


def normalize_depth(depth):
    low, high = np.percentile(depth, (1, 99))
    if high - low < 1e-8:
        return np.zeros_like(depth)
    return np.clip((depth - low) / (high - low), 0, 1)


def depth_to_normal(depth, color, args):
    depth = gaussian_blur(depth, args.smooth, args.seamless)
    scene = gaussian_blur(depth, args.detail_radius, args.seamless)
    if args.detail_source == "luminance":
        luminance = gaussian_blur(np.asarray(color.convert("L"), dtype=np.float32) / 255, args.smooth, args.seamless)
        detail = luminance - gaussian_blur(luminance, args.detail_radius, args.seamless)
    else:
        detail = depth - scene
    height = args.scene_weight * scene + args.detail_weight * detail
    if args.seamless:
        dx = (np.roll(height, -1, axis=1) - np.roll(height, 1, axis=1)) * 0.5
        dy = (np.roll(height, -1, axis=0) - np.roll(height, 1, axis=0)) * 0.5
    else:
        dy, dx = np.gradient(height)
    strength = args.strength * height.shape[1] / 512
    normal = np.stack((-dx * strength, dy * strength, np.ones_like(height)), axis=2)
    normal /= np.linalg.norm(normal, axis=2, keepdims=True)
    return np.clip((normal + 1) * 127.5, 0, 255).round().astype(np.uint8)


def infer_depth(model, color, size, invert):
    depth = model.predict({"image": color.resize(MODEL_SIZE, Image.Resampling.BICUBIC)})["depth"]
    depth = np.asarray(depth.resize((size, size), Image.Resampling.BICUBIC), dtype=np.float32)
    depth = normalize_depth(depth)
    return 1 - depth if invert else depth


def main():
    venv_python = VENV / "bin" / "python"
    if venv_python.is_file() and Path(sys.prefix).resolve() != VENV.resolve():
        os.execv(venv_python, [str(venv_python), *sys.argv])
    if sys.platform != "darwin":
        raise SystemExit("Apple's Core ML depth model requires macOS")

    global np, Image, ImageOps
    import numpy as np
    from PIL import Image, ImageOps

    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("targets", nargs="+", help="Image paths, or names under targets/")
    parser.add_argument("--output", type=Path, default=DEFAULT_OUTPUT)
    parser.add_argument("--size", type=int, default=512)
    parser.add_argument("--strength", type=float, default=64, help="Overall normal slope strength (default: 64)")
    parser.add_argument("--scene-weight", type=float, default=1, help="Large-scale scene depth contribution (default: 1)")
    parser.add_argument("--detail-weight", type=float, default=1, help="Local surface relief contribution (default: 1)")
    parser.add_argument("--detail-radius", type=float, default=24, help="Scene/detail split radius in output pixels (default: 24)")
    parser.add_argument(
        "--detail-source",
        choices=["depth", "luminance"],
        default="depth",
        help="Local relief from estimated depth, or from photo luminance for fine detail the depth model misses (default: depth)",
    )
    parser.add_argument("--smooth", type=float, default=1, help="Depth denoising radius in output pixels (default: 1)")
    parser.add_argument("--invert", action="store_true", help="Reverse near and far before generating normals")
    parser.add_argument("--seamless", action="store_true", help="Wrap filtering and gradients across image edges")
    parser.add_argument("--save-depth", action="store_true", help="Also save the normalized depth estimate")
    parser.add_argument("--overwrite", action="store_true")
    args = parser.parse_args()

    if args.size <= 0 or args.strength < 0 or args.detail_radius < 0 or args.smooth < 0:
        parser.error("size must be positive; strength and radii must be non-negative")
    targets = [resolve_target(value) for value in args.targets]
    outputs = [(args.output / f"{target.stem}_color.png", args.output / f"{target.stem}_normal.png") for target in targets]
    if len({color for color, _ in outputs}) != len(outputs):
        parser.error("target file names must be unique")
    pending = [
        (target, color, normal)
        for target, (color, normal) in zip(targets, outputs)
        if args.overwrite or not (color.exists() or normal.exists())
    ]
    for target, (color, normal) in zip(targets, outputs):
        if (target, color, normal) not in pending:
            print(f"Skipping {target.name}: output exists (use --overwrite)")
    if not pending:
        return

    try:
        logging.getLogger("coremltools").setLevel(logging.ERROR)
        import coremltools as ct
    except ImportError as error:
        raise SystemExit("Install requirements with: .venv/bin/pip install -r requirements.txt") from error

    prepare_model()
    args.output.mkdir(parents=True, exist_ok=True)
    model = ct.models.MLModel(str(MODEL_PATH), compute_units=ct.ComputeUnit.ALL)
    for position, (target, color_output, normal_output) in enumerate(pending, 1):
        with Image.open(target) as source:
            color = ImageOps.fit(ImageOps.exif_transpose(source).convert("RGB"), (args.size, args.size), Image.Resampling.LANCZOS)
        depth = infer_depth(model, color, args.size, args.invert)
        color.save(color_output)
        Image.fromarray(depth_to_normal(depth, color, args)).save(normal_output)
        if args.save_depth:
            Image.fromarray((depth * 255).round().astype(np.uint8)).save(args.output / f"{target.stem}_depth.png")
        print(f"[{position}/{len(pending)}] {target.name} -> {color_output.name} + {normal_output.name}")


if __name__ == "__main__":
    main()
