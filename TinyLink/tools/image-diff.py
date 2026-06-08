#!/usr/bin/env python3
import argparse
import math
import sys
from pathlib import Path

import numpy as np
from PIL import Image


def main() -> int:
	parser = argparse.ArgumentParser(description="Compare two screenshots with tolerant pixel metrics.")
	parser.add_argument("reference", type=Path)
	parser.add_argument("actual", type=Path)
	parser.add_argument("--diff", type=Path, default=None, help="Optional visual diff output path.")
	parser.add_argument("--pixel-threshold", type=int, default=8, help="Per-channel delta before a pixel counts as changed.")
	parser.add_argument("--max-mean", type=float, default=8.0, help="Maximum allowed mean absolute channel delta.")
	parser.add_argument("--max-changed-percent", type=float, default=25.0, help="Maximum allowed changed pixel percentage.")
	args = parser.parse_args()

	reference = Image.open(args.reference).convert("RGBA")
	actual = Image.open(args.actual).convert("RGBA")
	if reference.size != actual.size:
		print(f"Image size mismatch: reference={reference.size}, actual={actual.size}", file=sys.stderr)
		return 1

	width, height = reference.size
	total_pixels = width * height

	ref = np.asarray(reference, dtype=np.int32)
	got = np.asarray(actual, dtype=np.int32)
	delta = np.abs(ref - got)
	pixel_max = delta.max(axis=2)
	mean_abs = float(delta.mean())
	rms = math.sqrt(float((delta * delta).mean()))
	changed_percent = float((pixel_max > args.pixel_threshold).sum()) * 100.0 / total_pixels
	max_delta = int(delta.max())

	if args.diff:
		diff = np.zeros_like(delta, dtype=np.uint8)
		diff[:, :, :3] = np.minimum(delta[:, :, :3] * 4, 255).astype(np.uint8)
		diff[:, :, 3] = 255
		args.diff.parent.mkdir(parents=True, exist_ok=True)
		Image.fromarray(diff, "RGBA").save(args.diff)

	print(
		f"mean_abs={mean_abs:.3f} rms={rms:.3f} changed={changed_percent:.2f}% "
		f"max_delta={max_delta} size={width}x{height}")

	if mean_abs > args.max_mean or changed_percent > args.max_changed_percent:
		print(
			f"Image diff exceeded threshold: mean_abs <= {args.max_mean}, "
			f"changed <= {args.max_changed_percent}%",
			file=sys.stderr)
		return 1

	return 0


if __name__ == "__main__":
	raise SystemExit(main())
