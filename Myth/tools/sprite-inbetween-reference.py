#!/usr/bin/env python3
from __future__ import annotations

import argparse
import html
import json
import math
import random
import subprocess
import sys
from dataclasses import dataclass
from pathlib import Path
from typing import Callable

from PIL import Image, ImageChops, ImageDraw, ImageOps, ImageSequence
import numpy as np


ALPHA_THRESHOLD = 30
BLOCK_RADIUS = 2
SEARCH_RADIUS = 1
SPARSE_CHANGE_RATIO = 0.50
RIFE_REPO = Path("tmp/neural-vfi/Practical-RIFE")
RIFE_MODEL = Path("tmp/neural-vfi/models/rife-4.25-lite/train_log")
FILM_REPO = Path("tmp/neural-vfi/frame-interpolation")
FILM_MODEL = Path("tmp/neural-vfi/models/film/film_net/Style/saved_model")
_RIFE_RUNNER = None
_FILM_RUNNER = None


@dataclass(frozen=True)
class Case:
    source_name: str
    anchor_index: int
    index: int
    next_index: int
    t: float
    duration_a: int
    duration_target: int
    frame_a: Image.Image
    target: Image.Image
    frame_b: Image.Image


@dataclass(frozen=True)
class SourceAnimation:
    name: str
    frames: list[Image.Image]
    durations: list[int]
    sparse: bool


@dataclass(frozen=True)
class Metric:
    source_name: str
    index: int
    t: float
    algorithm: str
    rmse: float
    mae: float
    max_diff: int


def decode_frames(path: Path) -> tuple[list[Image.Image], list[int]]:
    image = Image.open(path)
    frames: list[Image.Image] = []
    durations: list[int] = []

    for frame in ImageSequence.Iterator(image):
        frames.append(normalize_transparent(frame.convert("RGBA")))
        durations.append(int(frame.info.get("duration") or image.info.get("duration") or 100))

    return frames, durations


def normalize_transparent(image: Image.Image) -> Image.Image:
    rgba = np.asarray(image, dtype=np.uint8).copy()
    rgba[rgba[:, :, 3] < ALPHA_THRESHOLD, :3] = 0
    return Image.fromarray(rgba, "RGBA")


def common_bbox(frames: list[Image.Image], padding: int) -> tuple[int, int, int, int]:
    boxes = [frame.getbbox() for frame in frames]
    boxes = [box for box in boxes if box is not None]
    if not boxes:
        w, h = frames[0].size
        return (0, 0, w, h)

    left = min(box[0] for box in boxes)
    top = min(box[1] for box in boxes)
    right = max(box[2] for box in boxes)
    bottom = max(box[3] for box in boxes)
    w, h = frames[0].size
    return (
        max(0, left - padding),
        max(0, top - padding),
        min(w, right + padding),
        min(h, bottom + padding),
    )


def crop_frames(frames: list[Image.Image], mode: str, padding: int) -> list[Image.Image]:
    if mode == "full":
        return [frame.copy() for frame in frames]
    if mode != "common":
        raise ValueError(f"unsupported crop mode: {mode}")

    box = common_bbox(frames, padding)
    return [frame.crop(box) for frame in frames]


def find_aseprite_bin() -> str | None:
    candidates = [
        "aseprite",
        "/Applications/Aseprite.app/Contents/MacOS/aseprite",
        str(Path.home() / "Library/Application Support/Steam/steamapps/common/Aseprite/Aseprite.app/Contents/MacOS/aseprite"),
    ]
    for candidate in candidates:
        try:
            result = subprocess.run(
                [candidate, "--version"],
                stdout=subprocess.DEVNULL,
                stderr=subprocess.DEVNULL,
                check=False,
            )
            if result.returncode == 0:
                return candidate
        except FileNotFoundError:
            continue
    return None


def decode_aseprite(path: Path, converted_dir: Path, aseprite_bin: str) -> tuple[list[Image.Image], list[int]]:
    converted_dir.mkdir(parents=True, exist_ok=True)
    output = converted_dir / f"{path.stem}.webp"
    if not output.exists() or output.stat().st_mtime < path.stat().st_mtime:
        result = subprocess.run(
            [aseprite_bin, "-b", str(path), "--save-as", str(output)],
            stdout=subprocess.PIPE,
            stderr=subprocess.PIPE,
            text=True,
            check=False,
        )
        if result.returncode != 0 or not output.exists():
            message = (result.stderr or result.stdout or "no output").strip()
            raise RuntimeError(f"Aseprite export failed: {message}")
    return decode_frames(output)


def aseprite_metadata(path: Path, converted_dir: Path, aseprite_bin: str) -> tuple[list[dict], list[int]]:
    converted_dir.mkdir(parents=True, exist_ok=True)
    meta_path = converted_dir / f"{path.stem}.json"
    sheet_path = converted_dir / f"{path.stem}.png"
    if not meta_path.exists() or meta_path.stat().st_mtime < path.stat().st_mtime:
        result = subprocess.run(
            [
                aseprite_bin,
                "-b",
                "--list-tags",
                "--data", str(meta_path),
                "--format", "json-array",
                "--sheet", str(sheet_path),
                str(path),
            ],
            stdout=subprocess.PIPE,
            stderr=subprocess.PIPE,
            text=True,
            check=False,
        )
        if result.returncode != 0 or not meta_path.exists():
            message = (result.stderr or result.stdout or "no metadata").strip()
            raise RuntimeError(f"Aseprite metadata export failed: {message}")

    data = json.loads(meta_path.read_text(encoding="utf-8"))
    tags = data.get("meta", {}).get("frameTags", [])
    durations = [int(frame.get("duration") or 100) for frame in data.get("frames", [])]
    return tags, durations


def decode_aseprite_tag(path: Path, tag: dict, durations: list[int], converted_dir: Path, aseprite_bin: str) -> SourceAnimation:
    tag_name = str(tag["name"])
    output = converted_dir / f"{path.stem}-{tag_name}.webp"
    if not output.exists() or output.stat().st_mtime < path.stat().st_mtime:
        result = subprocess.run(
            [aseprite_bin, "-b", str(path), "--tag", tag_name, "--save-as", str(output)],
            stdout=subprocess.PIPE,
            stderr=subprocess.PIPE,
            text=True,
            check=False,
        )
        if result.returncode != 0 or not output.exists():
            message = (result.stderr or result.stdout or "no output").strip()
            raise RuntimeError(f"Aseprite tag export failed for {tag_name}: {message}")

    frames, webp_durations = decode_frames(output)
    start = int(tag["from"])
    end = int(tag["to"])
    tag_durations = durations[start:end + 1]
    if len(tag_durations) != len(frames):
        fallback = [duration for duration in webp_durations if duration > 0] or tag_durations or [100]
        tag_durations = (fallback * ((len(frames) + len(fallback) - 1) // len(fallback)))[:len(frames)]
    return SourceAnimation(f"{path.stem}/{tag_name}", frames, tag_durations, True)


def load_source_animations(path: Path, converted_dir: Path, aseprite_bin: str | None) -> list[SourceAnimation]:
    if path.suffix.lower() in {".ase", ".aseprite"}:
        if not aseprite_bin:
            raise RuntimeError("Aseprite CLI not found; pass --aseprite-bin or omit --include-aseprites")
        tags, durations = aseprite_metadata(path, converted_dir, aseprite_bin)
        if not tags:
            frames, frame_durations = decode_aseprite(path, converted_dir, aseprite_bin)
            return [SourceAnimation(path.stem, frames, frame_durations, True)]

        animations = []
        for tag in tags:
            animations.append(decode_aseprite_tag(path, tag, durations, converted_dir, aseprite_bin))
        return animations

    frames, durations = decode_frames(path)
    return [SourceAnimation(path.stem, frames, durations, False)]


def build_cases_from_frames(name: str, frames: list[Image.Image], durations: list[int], crop: str, padding: int, loop: bool, stride: int) -> tuple[list[Case], list[Image.Image], list[int]]:
    frames = crop_frames(frames, crop, padding)
    if len(frames) < 2:
        return [], frames, durations

    anchors = list(range(0, len(frames), stride))
    if len(anchors) < 2 and not loop:
        return [], frames, durations

    cases: list[Case] = []
    anchor_pairs = list(zip(anchors, anchors[1:]))
    if loop:
        anchor_pairs.append((anchors[-1], anchors[0] + len(frames)))

    for a, b_abs in anchor_pairs:
        gap_duration = sum(durations[i % len(frames)] for i in range(a, b_abs))
        elapsed = 0
        for mid_abs in range(a + 1, b_abs):
            mid = mid_abs % len(frames)
            elapsed += durations[(mid_abs - 1) % len(frames)]
            t = elapsed / max(1, gap_duration)
            cases.append(Case(
                name,
                a % len(frames),
                mid,
                b_abs % len(frames),
                t,
                durations[a % len(frames)],
                durations[mid],
                frames[a % len(frames)],
                frames[mid],
                frames[b_abs % len(frames)],
            ))
    return cases, frames, durations


def build_sparse_generation_cases(name: str, frames: list[Image.Image], durations: list[int], loop: bool, stride: int) -> list[Case]:
    if len(frames) < 2:
        return []

    cases: list[Case] = []
    pair_count = len(frames) if loop else len(frames) - 1
    for anchor in range(pair_count):
        next_index = (anchor + 1) % len(frames)
        step_duration = max(20, int(round(durations[anchor] / stride)))
        for step in range(1, stride):
            synthetic_index = anchor * stride + step
            cases.append(Case(
                name,
                anchor,
                synthetic_index,
                next_index,
                step / stride,
                step_duration,
                step_duration,
                frames[anchor],
                frames[anchor],
                frames[next_index],
            ))
    return cases


def color_at(img: Image.Image, x: int, y: int) -> tuple[int, int, int, int]:
    r, g, b, a = img.getpixel((x, y))
    if a < ALPHA_THRESHOLD:
        return (0, 0, 0, 0)
    return (r, g, b, a)


def sad_rgba(src: Image.Image, dst: Image.Image, sx: int, sy: int, dx: int, dy: int) -> int:
    w, h = src.size
    total = 0

    for by in range(-BLOCK_RADIUS, BLOCK_RADIUS + 1):
        for bx in range(-BLOCK_RADIUS, BLOCK_RADIUS + 1):
            sx2 = sx + bx
            sy2 = sy + by
            dx2 = dx + bx
            dy2 = dy + by
            if sx2 < 0 or sx2 >= w or sy2 < 0 or sy2 >= h or dx2 < 0 or dx2 >= w or dy2 < 0 or dy2 >= h:
                total += 255
                continue

            a = color_at(src, sx2, sy2)
            b = color_at(dst, dx2, dy2)
            if a[3] < ALPHA_THRESHOLD and b[3] < ALPHA_THRESHOLD:
                continue

            total += max(abs(a[0] - b[0]), abs(a[1] - b[1]), abs(a[2] - b[2]))

    return total


def median_filter_flow(flow: list[list[tuple[int, int]]]) -> list[list[tuple[int, int]]]:
    h = len(flow)
    w = len(flow[0])
    result = [[(0, 0) for _ in range(w)] for _ in range(h)]

    for y in range(h):
        for x in range(w):
            xs: list[int] = []
            ys: list[int] = []
            for dy in (-1, 0, 1):
                ny = y + dy
                if ny < 0 or ny >= h:
                    continue
                for dx in (-1, 0, 1):
                    nx = x + dx
                    if nx < 0 or nx >= w:
                        continue
                    fx, fy = flow[ny][nx]
                    xs.append(fx)
                    ys.append(fy)
            xs.sort()
            ys.sort()
            result[y][x] = (xs[len(xs) // 2], ys[len(ys) // 2])

    return result


def sparse_pose_change(a: Image.Image, b: Image.Image) -> bool:
    aa = np.asarray(a, dtype=np.uint8)
    bb = np.asarray(b, dtype=np.uint8)
    opaque_mask = (aa[:, :, 3] >= ALPHA_THRESHOLD) | (bb[:, :, 3] >= ALPHA_THRESHOLD)
    opaque = int(opaque_mask.sum())
    changed = int((np.any(aa != bb, axis=2) & opaque_mask).sum())

    return opaque > 0 and changed / opaque <= SPARSE_CHANGE_RATIO


def blockmatch_flow(a: Image.Image, b: Image.Image) -> list[list[tuple[int, int]]]:
    w, h = a.size
    flow = [[(0, 0) for _ in range(w)] for _ in range(h)]
    for y in range(h):
        for x in range(w):
            best_cost = 1 << 60
            best = (0, 0)
            for dy in range(-SEARCH_RADIUS, SEARCH_RADIUS + 1):
                for dx in range(-SEARCH_RADIUS, SEARCH_RADIUS + 1):
                    cost = sad_rgba(a, b, x, y, x + dx, y + dy)
                    if cost < best_cost:
                        best_cost = cost
                        best = (dx, dy)
            flow[y][x] = best
    return median_filter_flow(flow)


def predict_static(case: Case) -> Image.Image:
    return case.frame_a.copy()


def predict_blend(case: Case) -> Image.Image:
    return Image.blend(case.frame_a, case.frame_b, case.t)


def predict_myth(case: Case) -> Image.Image:
    if sparse_pose_change(case.frame_a, case.frame_b):
        return predict_static(case)

    flow = blockmatch_flow(case.frame_a, case.frame_b)
    w, h = case.frame_a.size
    out = Image.new("RGBA", (w, h))
    src = case.frame_a.load()
    dst = out.load()

    for y in range(h):
        for x in range(w):
            fx, fy = flow[y][x]
            sx = min(w - 1, max(0, round(x - fx * case.t)))
            sy = min(h - 1, max(0, round(y - fy * case.t)))
            dst[x, y] = src[sx, sy]

    return out


class RifeRunner:
    def __init__(self, repo: Path, model_dir: Path):
        import torch
        from torch.nn import functional as F

        repo = repo.resolve()
        model_dir = model_dir.resolve()
        sys.path.insert(0, str(repo))
        sys.path.insert(0, str(model_dir.parent))

        from train_log.RIFE_HDv3 import Model

        self.torch = torch
        self.F = F
        self.device = torch.device("cuda" if torch.cuda.is_available() else "cpu")
        torch.set_grad_enabled(False)
        self.model = Model()
        self.model.load_model(str(model_dir), -1)
        self.model.eval()
        self.model.device()

    def _tensor_rgb(self, image: Image.Image) -> object:
        rgba = np.asarray(image, dtype=np.float32) / 255.0
        rgb = rgba[:, :, :3].copy()
        rgb[rgba[:, :, 3] < (ALPHA_THRESHOLD / 255.0)] = 0.0
        tensor = self.torch.from_numpy(rgb.transpose(2, 0, 1)).unsqueeze(0)
        return tensor.to(self.device)

    def _pad(self, tensor: object, w: int, h: int) -> object:
        size = 128
        while size < max(w, h):
            size *= 2
        return self.F.pad(tensor, (0, size - w, 0, size - h))

    def _infer(self, a: object, b: object, t: float, w: int, h: int) -> np.ndarray:
        a = self._pad(a, w, h)
        b = self._pad(b, w, h)
        out = self.model.inference(a, b, timestep=t)
        out = out[0].detach().cpu().clamp(0, 1).numpy().transpose(1, 2, 0)
        return out[:h, :w]

    def predict(self, case: Case) -> Image.Image:
        w, h = case.frame_a.size
        rgb = self._infer(self._tensor_rgb(case.frame_a), self._tensor_rgb(case.frame_b), case.t, w, h)
        alpha_a = np.asarray(case.frame_a, dtype=np.float32)[:, :, 3:4] / 255.0
        alpha_b = np.asarray(case.frame_b, dtype=np.float32)[:, :, 3:4] / 255.0
        if np.array_equal(alpha_a, alpha_b):
            alpha = alpha_a
        else:
            alpha = alpha_a * (1.0 - case.t) + alpha_b * case.t
        rgba = np.concatenate((rgb, alpha), axis=2)
        rgba = np.clip(np.rint(rgba * 255), 0, 255).astype(np.uint8)
        rgba[rgba[:, :, 3] < ALPHA_THRESHOLD, :3] = 0
        return Image.fromarray(rgba, "RGBA")


def predict_rife(case: Case) -> Image.Image:
    global _RIFE_RUNNER
    if _RIFE_RUNNER is None:
        _RIFE_RUNNER = RifeRunner(RIFE_REPO, RIFE_MODEL)
    return _RIFE_RUNNER.predict(case)


def snap_to_input_palette(image: Image.Image, case: Case) -> Image.Image:
    out = np.asarray(image, dtype=np.uint8).copy()
    palettes = []
    for frame in (case.frame_a, case.frame_b):
        rgba = np.asarray(frame, dtype=np.uint8)
        mask = rgba[:, :, 3] >= ALPHA_THRESHOLD
        if mask.any():
            palettes.append(rgba[mask, :3])
    if not palettes:
        return image

    palette = np.unique(np.concatenate(palettes, axis=0), axis=0).astype(np.int32)
    mask = out[:, :, 3] >= ALPHA_THRESHOLD
    pixels = out[mask, :3].astype(np.int32)
    if len(pixels) == 0:
        return image

    # Palette sizes are small for sprites; chunking keeps memory predictable for larger refs.
    snapped = np.empty_like(pixels, dtype=np.uint8)
    chunk_size = 4096
    for start in range(0, len(pixels), chunk_size):
        chunk = pixels[start:start + chunk_size]
        diff = chunk[:, None, :] - palette[None, :, :]
        dist = np.sum(diff * diff, axis=2)
        snapped[start:start + chunk_size] = palette[np.argmin(dist, axis=1)].astype(np.uint8)

    out[mask, :3] = snapped
    out[out[:, :, 3] < ALPHA_THRESHOLD, :3] = 0
    return Image.fromarray(out, "RGBA")


def predict_rife_snap(case: Case) -> Image.Image:
    return snap_to_input_palette(predict_rife(case), case)


class FilmRunner:
    def __init__(self, repo: Path, model_dir: Path):
        repo = repo.resolve()
        sys.path.insert(0, str(repo))
        from eval import interpolator as interpolator_lib

        self.interpolator = interpolator_lib.Interpolator(str(model_dir.resolve()), align=64)

    def predict(self, case: Case) -> Image.Image:
        rgba_a = np.asarray(case.frame_a, dtype=np.float32) / 255.0
        rgba_b = np.asarray(case.frame_b, dtype=np.float32) / 255.0

        rgb_a = rgba_a[:, :, :3].copy()
        rgb_b = rgba_b[:, :, :3].copy()
        rgb_a[rgba_a[:, :, 3] < (ALPHA_THRESHOLD / 255.0)] = 0.0
        rgb_b[rgba_b[:, :, 3] < (ALPHA_THRESHOLD / 255.0)] = 0.0

        rgb = self.interpolator(
            rgb_a[np.newaxis, ...].astype(np.float32),
            rgb_b[np.newaxis, ...].astype(np.float32),
            np.asarray([case.t], dtype=np.float32),
        )[0]

        alpha_a = rgba_a[:, :, 3:4]
        alpha_b = rgba_b[:, :, 3:4]
        if np.array_equal(alpha_a, alpha_b):
            alpha = alpha_a
        else:
            alpha = alpha_a * (1.0 - case.t) + alpha_b * case.t

        rgba = np.concatenate((rgb, alpha), axis=2)
        rgba = np.clip(np.rint(rgba * 255), 0, 255).astype(np.uint8)
        rgba[rgba[:, :, 3] < ALPHA_THRESHOLD, :3] = 0
        return Image.fromarray(rgba, "RGBA")


def predict_film(case: Case) -> Image.Image:
    global _FILM_RUNNER
    if _FILM_RUNNER is None:
        _FILM_RUNNER = FilmRunner(FILM_REPO, FILM_MODEL)
    return _FILM_RUNNER.predict(case)


ALGORITHMS: dict[str, Callable[[Case], Image.Image]] = {
    "static": predict_static,
    "blend": predict_blend,
    "myth": predict_myth,
    "rife": predict_rife,
    "rife-snap": predict_rife_snap,
    "film": predict_film,
}


def compare(source_name: str, index: int, t: float, algorithm: str, actual: Image.Image, expected: Image.Image) -> Metric:
    diff = np.asarray(actual, dtype=np.int32) - np.asarray(expected, dtype=np.int32)
    abs_diff = np.abs(diff)
    return Metric(
        source_name=source_name,
        index=index,
        t=t,
        algorithm=algorithm,
        rmse=float(np.sqrt(np.mean(diff * diff))),
        mae=float(np.mean(abs_diff)),
        max_diff=int(abs_diff.max()),
    )


def diff_image(actual: Image.Image, expected: Image.Image) -> Image.Image:
    diff = ImageChops.difference(actual, expected).convert("RGB")
    return ImageOps.autocontrast(diff).convert("RGBA")


def label_image(img: Image.Image, text: str, scale: int) -> Image.Image:
    body = img.resize((img.width * scale, img.height * scale), Image.Resampling.NEAREST)
    out = Image.new("RGBA", (body.width, body.height + 16), (18, 18, 18, 255))
    out.paste(body, (0, 16))
    draw = ImageDraw.Draw(out)
    draw.text((2, 1), text, fill=(255, 255, 255, 255))
    return out


def write_worst_sheet(out_dir: Path, algorithm: str, rows: list[tuple[Metric, Case, Image.Image]], limit: int, scale: int) -> None:
    rows = sorted(rows, key=lambda row: row[0].rmse, reverse=True)[:limit]
    if not rows:
        return

    sheet_rows: list[list[Image.Image]] = []
    for metric, case, prediction in rows:
        sheet_rows.append([
            label_image(case.frame_a, f"{case.source_name} {case.index} A", scale),
            label_image(case.target, "target", scale),
            label_image(case.frame_b, f"B t={case.t:.2f}", scale),
            label_image(prediction, f"{algorithm} {metric.rmse:.1f}", scale),
            label_image(diff_image(prediction, case.target), "diff", scale),
        ])

    width = max(sum(cell.width for cell in row) for row in sheet_rows)
    height = sum(max(cell.height for cell in row) for row in sheet_rows)
    sheet = Image.new("RGBA", (width, height), (0, 0, 0, 255))

    y = 0
    for row in sheet_rows:
        x = 0
        row_h = max(cell.height for cell in row)
        for cell in row:
            sheet.paste(cell, (x, y))
            x += cell.width
        y += row_h

    sheet.save(out_dir / f"worst-{algorithm}.png")


def save_animation_webp(path: Path, frames: list[Image.Image], durations: list[int], scale: int) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    if not frames:
        return

    scaled = [
        normalize_transparent(frame).resize((frame.width * scale, frame.height * scale), Image.Resampling.NEAREST)
        for frame in frames
    ]
    duration = [max(20, int(d)) for d in durations]
    if len(duration) < len(scaled):
        duration.extend([duration[-1] if duration else 100] * (len(scaled) - len(duration)))

    scaled[0].save(
        path,
        save_all=True,
        append_images=scaled[1:],
        duration=duration[:len(scaled)],
        loop=0,
        lossless=True,
        quality=100,
        method=0,
    )


def html_rel(path: Path, base: Path) -> str:
    return html.escape(path.relative_to(base).as_posix(), quote=True)


def removed_sequence(frames: list[Image.Image], durations: list[int], stride: int) -> tuple[list[Image.Image], list[int]]:
    anchors = list(range(0, len(frames), stride))
    if not anchors:
        return [], []

    output_frames = [frames[i] for i in anchors]
    output_durations: list[int] = []
    for idx, anchor in enumerate(anchors):
        next_anchor = anchors[idx + 1] if idx + 1 < len(anchors) else len(frames)
        output_durations.append(sum(durations[anchor:next_anchor]))
    return output_frames, output_durations


def predicted_sequence(
    frames: list[Image.Image],
    durations: list[int],
    cases: list[Case],
    predictions: dict[tuple[str, int, str], Image.Image],
    algorithm: str,
) -> tuple[list[Image.Image], list[int]]:
    output_frames: list[Image.Image] = []
    output_durations: list[int] = []
    last_index: int | None = None

    groups: dict[tuple[int, int], list[Case]] = {}
    for case in cases:
        groups.setdefault((case.anchor_index, case.next_index), []).append(case)

    ordered_groups = sorted(groups.items(), key=lambda item: (item[0][0], item[1][0].t))
    for (anchor, next_index), group_cases in ordered_groups:
        if last_index != anchor:
            output_frames.append(frames[anchor])
            output_durations.append(durations[anchor])
            last_index = anchor

        for case in sorted(group_cases, key=lambda it: it.t):
            prediction = predictions.get((case.source_name, case.index, algorithm))
            if prediction is None:
                continue
            output_frames.append(prediction)
            output_durations.append(case.duration_target)
            last_index = case.index

        if last_index != next_index:
            output_frames.append(frames[next_index])
            output_durations.append(durations[next_index])
            last_index = next_index

    return output_frames, output_durations


def sparse_predicted_sequence(
    frames: list[Image.Image],
    durations: list[int],
    cases: list[Case],
    predictions: dict[tuple[str, int, str], Image.Image],
    algorithm: str,
    stride: int,
    loop: bool,
) -> tuple[list[Image.Image], list[int]]:
    output_frames: list[Image.Image] = []
    output_durations: list[int] = []
    cases_by_anchor: dict[int, list[Case]] = {}
    for case in cases:
        cases_by_anchor.setdefault(case.anchor_index, []).append(case)

    pair_count = len(frames) if loop else max(0, len(frames) - 1)
    for anchor in range(pair_count):
        step_duration = max(20, int(round(durations[anchor] / stride)))
        output_frames.append(frames[anchor])
        output_durations.append(step_duration)
        for case in sorted(cases_by_anchor.get(anchor, []), key=lambda it: it.t):
            prediction = predictions.get((case.source_name, case.index, algorithm))
            if prediction is None:
                continue
            output_frames.append(prediction)
            output_durations.append(step_duration)

    if not loop and frames:
        output_frames.append(frames[-1])
        output_durations.append(durations[-1])

    return output_frames, output_durations


def write_html_report(
    html_path: Path,
    cases: list[Case],
    generation_cases: list[Case],
    source_frames: dict[str, tuple[list[Image.Image], list[int]]],
    source_sparse: dict[str, bool],
    metrics_by_key: dict[tuple[str, int, str], Metric],
    predictions: dict[tuple[str, int, str], Image.Image],
    algorithms: list[str],
    scale: int,
    stride: int,
    loop: bool,
) -> None:
    html_path.parent.mkdir(parents=True, exist_ok=True)
    asset_dir = html_path.parent / f"{html_path.stem}-assets"
    asset_dir.mkdir(parents=True, exist_ok=True)

    rows: list[str] = []
    cases_by_source: dict[str, list[Case]] = {}
    for case in cases:
        cases_by_source.setdefault(case.source_name, []).append(case)
    generation_cases_by_source: dict[str, list[Case]] = {}
    for case in generation_cases:
        generation_cases_by_source.setdefault(case.source_name, []).append(case)

    original_paths: dict[str, Path] = {}
    for source_name, (frames, durations) in source_frames.items():
        original_path = asset_dir / f"source-{source_name}.webp"
        save_animation_webp(original_path, frames, durations, scale)
        original_paths[source_name] = original_path

    for row_index, source_name in enumerate(sorted(source_frames)):
        frames, durations = source_frames[source_name]
        source_cases = cases_by_source.get(source_name, [])
        source_generation_cases = generation_cases_by_source.get(source_name, [])
        sparse = source_sparse.get(source_name, False)
        prefix = f"{row_index:04d}-{source_name}"
        removed_path = asset_dir / f"{prefix}-removed.webp"
        if sparse:
            removed_frames, removed_durations = frames, durations
        else:
            removed_frames, removed_durations = removed_sequence(frames, durations, stride)
        save_animation_webp(removed_path, removed_frames, removed_durations, scale)

        original_path = original_paths.get(source_name)
        original_cell = (
            f"<img src=\"{html_rel(original_path, html_path.parent)}\" alt=\"original\">"
            if original_path is not None
            else "n/a"
        )
        cells = [
            f"<td class=\"meta\"><strong>{html.escape(source_name)}</strong><br>{len(frames)} frames<br>stride {stride}</td>",
            f"<td>{original_cell}</td>",
            f"<td><img src=\"{html_rel(removed_path, html_path.parent)}\" alt=\"removed\"></td>",
        ]

        for algorithm in algorithms:
            if sparse:
                if not source_generation_cases:
                    cells.append("<td class=\"missing\">n/a</td>")
                    continue
                pred_path = asset_dir / f"{prefix}-{algorithm}.webp"
                pred_frames, pred_durations = sparse_predicted_sequence(
                    frames,
                    durations,
                    source_generation_cases,
                    predictions,
                    algorithm,
                    stride,
                    loop,
                )
                save_animation_webp(pred_path, pred_frames, pred_durations, scale)
                cells.append(
                    f"<td><img src=\"{html_rel(pred_path, html_path.parent)}\" alt=\"{html.escape(algorithm)}\">"
                    f"<div class=\"metric\">generated<br>no target</div></td>"
                )
                continue

            algorithm_metrics = [
                metric for case in source_cases
                if (metric := metrics_by_key.get((case.source_name, case.index, algorithm))) is not None
            ]
            if not algorithm_metrics:
                cells.append("<td class=\"missing\">n/a</td>")
                continue

            pred_path = asset_dir / f"{prefix}-{algorithm}.webp"
            pred_frames, pred_durations = predicted_sequence(frames, durations, source_cases, predictions, algorithm)
            save_animation_webp(pred_path, pred_frames, pred_durations, scale)
            rmse = math.sqrt(sum(metric.rmse * metric.rmse for metric in algorithm_metrics) / len(algorithm_metrics))
            mae = sum(metric.mae for metric in algorithm_metrics) / len(algorithm_metrics)
            cells.append(
                f"<td><img src=\"{html_rel(pred_path, html_path.parent)}\" alt=\"{html.escape(algorithm)}\">"
                f"<div class=\"metric\">RMSE {rmse:.2f}<br>MAE {mae:.2f}</div></td>"
            )

        rows.append("<tr>" + "".join(cells) + "</tr>")

    algorithm_headers = "".join(f"<th>{html.escape(algorithm)}</th>" for algorithm in algorithms)
    document = f"""<!doctype html>
<html lang=\"en\">
<head>
<meta charset=\"utf-8\">
<title>Sprite Inbetween Reference Report</title>
<style>
body {{ margin: 24px; font: 14px/1.4 -apple-system, BlinkMacSystemFont, Segoe UI, sans-serif; background: #181818; color: #eee; }}
table {{ border-collapse: collapse; width: 100%; }}
th {{ position: sticky; top: 0; background: #242424; z-index: 1; }}
th, td {{ border: 1px solid #333; padding: 8px; vertical-align: top; text-align: center; }}
td.meta {{ min-width: 130px; text-align: left; color: #ccc; }}
img {{ image-rendering: pixelated; background: #000; max-width: 260px; max-height: 260px; }}
.metric {{ margin-top: 6px; color: #bbb; font-size: 12px; }}
.missing {{ color: #777; }}
</style>
</head>
<body>
<h1>Sprite Inbetween Reference Report</h1>
<p>Original shows the full source animation. Reference rows keep one frame out of every {stride}, then score each method against the omitted authored frames. Aseprite rows use their tag frames as already-sparse keyframes, so Removed matches Original and method cells generate unscored inbetweens.</p>
<table>
<thead><tr><th>Source</th><th>Original</th><th>Removed</th>{algorithm_headers}</tr></thead>
<tbody>
{''.join(rows)}
</tbody>
</table>
</body>
</html>
"""
    html_path.write_text(document, encoding="utf-8")


def write_report(out_dir: Path, metrics: list[Metric], cases_by_key: dict[tuple[str, int], Case], predictions: dict[tuple[str, int, str], Image.Image], worst_count: int, scale: int) -> None:
    out_dir.mkdir(parents=True, exist_ok=True)
    metric_rows = [metric.__dict__ for metric in metrics]
    (out_dir / "metrics.json").write_text(json.dumps(metric_rows, indent=2), encoding="utf-8")

    by_algorithm: dict[str, list[Metric]] = {}
    by_source_algorithm: dict[tuple[str, str], list[Metric]] = {}
    for metric in metrics:
        by_algorithm.setdefault(metric.algorithm, []).append(metric)
        by_source_algorithm.setdefault((metric.source_name, metric.algorithm), []).append(metric)

    lines = [
        "# Sprite Inbetween Reference Metrics",
        "",
        "| Algorithm | Cases | RMSE | MAE | Max diff |",
        "| --- | ---: | ---: | ---: | ---: |",
    ]
    for algorithm, values in sorted(by_algorithm.items()):
        rmse = math.sqrt(sum(v.rmse * v.rmse for v in values) / len(values))
        mae = sum(v.mae for v in values) / len(values)
        max_diff = max(v.max_diff for v in values)
        lines.append(f"| {algorithm} | {len(values)} | {rmse:.3f} | {mae:.3f} | {max_diff} |")

    lines.extend([
        "",
        "## By Source",
        "",
        "| Source | Algorithm | Cases | RMSE | MAE | Max diff |",
        "| --- | --- | ---: | ---: | ---: | ---: |",
    ])
    for (source, algorithm), values in sorted(by_source_algorithm.items()):
        rmse = math.sqrt(sum(v.rmse * v.rmse for v in values) / len(values))
        mae = sum(v.mae for v in values) / len(values)
        max_diff = max(v.max_diff for v in values)
        lines.append(f"| {source} | {algorithm} | {len(values)} | {rmse:.3f} | {mae:.3f} | {max_diff} |")

    (out_dir / "summary.md").write_text("\n".join(lines) + "\n", encoding="utf-8")

    for algorithm in by_algorithm:
        rows = []
        for metric in by_algorithm[algorithm]:
            case = cases_by_key[(metric.source_name, metric.index)]
            prediction = predictions[(metric.source_name, metric.index, algorithm)]
            rows.append((metric, case, prediction))
        write_worst_sheet(out_dir, algorithm, rows, worst_count, scale)


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="Evaluate inbetween algorithms against hand-authored animated sprite frames.")
    parser.add_argument("--refs", type=Path, default=Path("Production/Reference Sprites"))
    parser.add_argument("--include-aseprites", action="store_true", help="also include .ase/.aseprite files from --aseprite-dir")
    parser.add_argument("--aseprite-dir", type=Path, default=Path("Assets/Sprites"))
    parser.add_argument("--aseprite-bin", type=str, default=None)
    parser.add_argument("--out", type=Path, default=Path("tmp/reference-inbetweens"))
    parser.add_argument("--html", type=Path, default=None, help="optional HTML table report path")
    parser.add_argument("--crop", choices=("common", "full"), default="common")
    parser.add_argument("--padding", type=int, default=2)
    parser.add_argument("--loop", action="store_true", help="include wraparound triplets")
    parser.add_argument("--stride", type=int, default=2, help="keep every Nth frame and synthesize omitted frames between kept anchors")
    parser.add_argument("--algorithms", nargs="+", choices=sorted(ALGORITHMS), default=["static", "blend"])
    parser.add_argument("--rife-repo", type=Path, default=RIFE_REPO)
    parser.add_argument("--rife-model", type=Path, default=RIFE_MODEL)
    parser.add_argument("--film-repo", type=Path, default=FILM_REPO)
    parser.add_argument("--film-model", type=Path, default=FILM_MODEL)
    parser.add_argument("--max-cases-per-source", type=int, default=8, help="0 means exhaustive")
    parser.add_argument("--seed", type=int, default=1)
    parser.add_argument("--worst-count", type=int, default=12)
    parser.add_argument("--sheet-scale", type=int, default=2)
    parser.add_argument("--html-scale", type=int, default=2)
    return parser.parse_args()


def main() -> int:
    global RIFE_REPO, RIFE_MODEL, FILM_REPO, FILM_MODEL
    args = parse_args()
    if args.stride < 2:
        raise ValueError("--stride must be at least 2")
    RIFE_REPO = args.rife_repo
    RIFE_MODEL = args.rife_model
    FILM_REPO = args.film_repo
    FILM_MODEL = args.film_model
    rng = random.Random(args.seed)
    aseprite_bin = args.aseprite_bin or find_aseprite_bin()
    converted_dir = args.out / "converted"

    files = sorted(path for path in args.refs.iterdir() if path.suffix.lower() in {".gif", ".webp"})
    if args.include_aseprites:
        files.extend(sorted(path for path in args.aseprite_dir.iterdir() if path.suffix.lower() in {".ase", ".aseprite"}))

    cases: list[Case] = []
    generation_cases: list[Case] = []
    source_frames: dict[str, tuple[list[Image.Image], list[int]]] = {}
    source_sparse: dict[str, bool] = {}
    for path in files:
        try:
            animations = load_source_animations(path, converted_dir, aseprite_bin)
        except Exception as ex:
            print(f"skipping {path}: {ex}", file=sys.stderr)
            continue

        for animation in animations:
            cropped_frames = crop_frames(animation.frames, args.crop, args.padding)
            cropped_durations = animation.durations
            if len(cropped_frames) < 2:
                continue

            source_frames[animation.name] = (cropped_frames, cropped_durations)
            source_sparse[animation.name] = animation.sparse
            if animation.sparse:
                generation_cases.extend(build_sparse_generation_cases(animation.name, cropped_frames, cropped_durations, args.loop, args.stride))
                continue

            source_cases, _, _ = build_cases_from_frames(animation.name, cropped_frames, cropped_durations, "full", args.padding, args.loop, args.stride)
            if not source_cases:
                continue

            if args.max_cases_per_source > 0 and len(source_cases) > args.max_cases_per_source:
                source_cases = sorted(rng.sample(source_cases, args.max_cases_per_source), key=lambda case: case.index)
            cases.extend(source_cases)

    metrics: list[Metric] = []
    cases_by_key: dict[tuple[str, int], Case] = {}
    predictions: dict[tuple[str, int, str], Image.Image] = {}

    for case in cases:
        cases_by_key[(case.source_name, case.index)] = case
        for algorithm in args.algorithms:
            prediction = ALGORITHMS[algorithm](case)
            predictions[(case.source_name, case.index, algorithm)] = prediction
            metrics.append(compare(case.source_name, case.index, case.t, algorithm, prediction, case.target))

    for case in generation_cases:
        for algorithm in args.algorithms:
            predictions[(case.source_name, case.index, algorithm)] = ALGORITHMS[algorithm](case)

    write_report(args.out, metrics, cases_by_key, predictions, args.worst_count, args.sheet_scale)
    if args.html:
        metrics_by_key = {(metric.source_name, metric.index, metric.algorithm): metric for metric in metrics}
        write_html_report(
            args.html,
            cases,
            generation_cases,
            source_frames,
            source_sparse,
            metrics_by_key,
            predictions,
            args.algorithms,
            args.html_scale,
            args.stride,
            args.loop,
        )
    print(f"evaluated {len(cases)} cases across {len(args.algorithms)} algorithms")
    if generation_cases:
        print(f"generated {len(generation_cases)} unscored aseprite inbetween slots")
    print(args.out / "summary.md")
    if args.html:
        print(args.html)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
