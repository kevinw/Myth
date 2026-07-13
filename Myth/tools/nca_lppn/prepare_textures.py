"""Prepare downloaded PBR texture sets for NCA training."""

import argparse
import re
from pathlib import Path

import numpy as np
from PIL import Image, ImageOps


ROOT = Path(__file__).resolve().parent
DEFAULT_SOURCE = Path.home() / "Downloads" / "NCA_TEXTURES"
DEFAULT_OUTPUT = ROOT / "targets" / "prepared"
IMAGE_EXTENSIONS = {".jpg", ".jpeg", ".png", ".tif", ".tiff", ".webp"}

DIFFUSE_NAMES = ("basecolor", "base_color", "base-color", "albedo", "diffuse", "colour", "color", "diff")
NORMAL_GL_NAMES = ("normalgl", "normal_gl", "normal-gl", "nrmgl", "nrm_gl", "nrm-gl")
NORMAL_DX_NAMES = ("normaldx", "normal_dx", "normal-dx", "nrmdx", "nrm_dx", "nrm-dx")
NORMAL_NAMES = ("normal", "norm", "nrm")


def map_kind(path):
	name = path.stem.lower()
	if any(value in name for value in NORMAL_GL_NAMES):
		return "normal_gl"
	if any(value in name for value in NORMAL_DX_NAMES):
		return "normal_dx"
	if any(re.search(rf"(?:^|[_ .-]){value}(?:$|[_ .-])", name) for value in NORMAL_NAMES):
		return "normal"
	if any(value in name for value in DIFFUSE_NAMES):
		return "diffuse"
	return None


def texture_name(directory):
	name = re.sub(r"(?:[_ .-](?:1|2|4|8|16)k)?[_ .-]?(?:jpg|png)$", "", directory.name, flags=re.IGNORECASE)
	return re.sub(r"[^a-z0-9]+", "_", name.lower()).strip("_")


def choose(files, kinds):
	for kind in kinds:
		matches = sorted(path for path in files if map_kind(path) == kind)
		if matches:
			return matches[0], kind
	return None, None


def fit(image, size):
	return ImageOps.fit(image, (size, size), method=Image.Resampling.LANCZOS)


def prepare_diffuse(source, destination, size):
	with Image.open(source) as image:
		fit(image.convert("RGB"), size).save(destination)


def prepare_normal(source, destination, size, directx):
	with Image.open(source) as image:
		pixels = np.asarray(fit(image.convert("RGB"), size), dtype=np.float32) / 127.5 - 1.0
	if directx:
		pixels[:, :, 1] *= -1
	length = np.linalg.norm(pixels, axis=2, keepdims=True)
	pixels /= np.maximum(length, 1e-8)
	pixels = np.clip((pixels + 1.0) * 127.5, 0, 255).round().astype(np.uint8)
	Image.fromarray(pixels, "RGB").save(destination)


def main():
	parser = argparse.ArgumentParser(description=__doc__)
	parser.add_argument("source", nargs="?", type=Path, default=DEFAULT_SOURCE)
	parser.add_argument("--output", type=Path, default=DEFAULT_OUTPUT)
	parser.add_argument("--size", type=int, default=512)
	parser.add_argument("--overwrite", action="store_true")
	args = parser.parse_args()

	if not args.source.is_dir():
		raise SystemExit(f"Texture directory does not exist: {args.source}")
	if args.size <= 0:
		raise SystemExit("--size must be positive")

	args.output.mkdir(parents=True, exist_ok=True)
	prepared = 0
	for directory in sorted({path.parent for path in args.source.rglob("*") if path.suffix.lower() in IMAGE_EXTENSIONS}):
		files = [path for path in directory.iterdir() if path.is_file() and path.suffix.lower() in IMAGE_EXTENSIONS]
		diffuse, _ = choose(files, ("diffuse",))
		normal, normal_kind = choose(files, ("normal_gl", "normal", "normal_dx"))
		if not diffuse or not normal:
			continue

		name = texture_name(directory)
		diffuse_output = args.output / f"{name}_color.png"
		normal_output = args.output / f"{name}_normal.png"
		if not args.overwrite and (diffuse_output.exists() or normal_output.exists()):
			print(f"Skipping {name}: output exists (use --overwrite)")
			continue

		prepare_diffuse(diffuse, diffuse_output, args.size)
		prepare_normal(normal, normal_output, args.size, normal_kind == "normal_dx")
		print(f"{name}: {diffuse.name} + {normal.name}")
		prepared += 1

	print(f"Prepared {prepared} texture set(s) in {args.output}")


if __name__ == "__main__":
	main()
