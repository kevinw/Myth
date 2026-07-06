#!/usr/bin/env python3
"""Export a tiny white-silhouette WebP of the player (e.g. the run cycle) for the
browser loading screen. The output is white-on-transparent so it can be shown as
a white glyph directly, or — better — used as a CSS `mask-image` on a tinted box
to recolor it to anything (see the snippet printed at the end).

The player sprite (Assets/Sources/Sprites/player.ase) has no animation tags, so the run
frames are selected by index here. Tweak the CONFIG block below — run with
`--contact-sheet` first to see numbered frames and pick the right ones.

Requires: Aseprite (CLI) + Pillow.  Run from anywhere:
    python3 Myth/tools/export-loading-silhouette.py
    python3 Myth/tools/export-loading-silhouette.py --contact-sheet
"""
from __future__ import annotations
import argparse, json, os, subprocess, sys, tempfile
from pathlib import Path

# ---------------------------------------------------------------- CONFIG ----
# Which animation to export. player.ase is tagged (run, run_start, idle, jump,
# dive, ...) — pick a tag and the frames resolve automatically. Set FRAMES to an
# explicit 0-based index list to override the tag (see --contact-sheet / --tags).
TAG = "run"
FRAMES = None

SILHOUETTE_COLOR = (255, 255, 255)  # white; tint at runtime via CSS
ALPHA_THRESHOLD = 0                 # alpha > this is kept (pixel art is 0/255)
SCALE = 1                           # export scale; keep native + CSS-upscale (pixelated)
FRAME_DURATION_MS = 90              # per-frame; None = use the .ase's own durations
TRIM = True                         # crop to the union bbox of all frames (tighter/smaller)
LOSSLESS = True                     # pixel art → lossless keeps edges crisp

# --sequence mode: stitch several tags into one loop, flipping horizontally to
# face the other way. Each step: tag (or frames), flip, repeat (times to repeat
# the step's frames), ms (per-frame duration; None = the .ase's own).
SEQUENCE = [
    {"tag": "run",  "flip": False, "repeat": 3, "ms": 90},   # run right
    {"tag": "skid", "flip": False, "repeat": 1, "ms": 220},  # skid to turn
    {"tag": "run",  "flip": True,  "repeat": 3, "ms": 90},   # run left
    {"tag": "skid", "flip": True,  "repeat": 1, "ms": 220},  # skid to turn back
]

PROJECT = Path(__file__).resolve().parent.parent          # Myth/
SRC_ASE = PROJECT / "Assets/Sources/Sprites/player.ase"
OUTPUT = PROJECT / "Platforms/Web/wwwroot/loading-player.webp"
SEQUENCE_OUTPUT = PROJECT / "Platforms/Web/wwwroot/loading-player-skid.webp"
ASEPRITE = os.environ.get(
    "ASEPRITE", "/Applications/Aseprite.app/Contents/MacOS/aseprite")
# ----------------------------------------------------------------------------

try:
    from PIL import Image, ImageDraw
except ImportError:
    sys.exit("Pillow is required: pip install pillow")


def render_strip(scale: int):
    """Render every frame to a strip and return (frames, tags).

    frames: list of (RGBA image, duration_ms) in document order.
    tags:   {name: (from, to)} from the .ase, 0-based inclusive ranges.
    """
    if not Path(ASEPRITE).exists():
        sys.exit(f"Aseprite CLI not found at {ASEPRITE} (set $ASEPRITE).")
    if not SRC_ASE.exists():
        sys.exit(f"Source sprite not found: {SRC_ASE}")
    tmp = Path(tempfile.mkdtemp(prefix="loading-sil-"))
    strip, data = tmp / "strip.png", tmp / "strip.json"
    subprocess.run([
        ASEPRITE, "-b", str(SRC_ASE), "--list-tags",  # --list-tags → meta.frameTags
        "--sheet", str(strip), "--sheet-columns", "9999",
        "--data", str(data), "--format", "json-array",
        "--scale", str(scale),
    ], check=True, stdout=subprocess.DEVNULL)
    meta = json.loads(data.read_text())
    sheet = Image.open(strip).convert("RGBA")
    frames = []
    for fr in meta["frames"]:
        r = fr["frame"]
        frames.append((sheet.crop((r["x"], r["y"], r["x"] + r["w"], r["y"] + r["h"])),
                       fr["duration"]))
    tags = {t["name"]: (t["from"], t["to"]) for t in meta["meta"].get("frameTags", [])}
    return frames, tags


def step_indices(step: dict, tags: dict) -> list[int]:
    """Frame indices for one step: explicit 'frames', else the named 'tag' range,
    repeated 'repeat' times."""
    if step.get("frames") is not None:
        idx = list(step["frames"])
    else:
        t = step["tag"]
        if t not in tags:
            sys.exit(f"Tag {t!r} not found. Available: {', '.join(tags) or '(none)'}")
        lo, hi = tags[t]
        idx = list(range(lo, hi + 1))
    return idx * step.get("repeat", 1)


def build_silhouettes(rendered, tags, steps):
    """Turn a list of steps into (silhouette frames, per-frame durations)."""
    n = len(rendered)
    sils, durs = [], []
    for step in steps:
        ms = step.get("ms", FRAME_DURATION_MS)
        for i in step_indices(step, tags):
            if i < 0 or i >= n:
                sys.exit(f"Frame index {i} out of range (sprite has {n} frames).")
            img = rendered[i][0]
            if step.get("flip"):
                img = img.transpose(Image.FLIP_LEFT_RIGHT)
            sils.append(to_silhouette(img))
            durs.append(ms if ms else rendered[i][1])
    return sils, durs


def to_silhouette(img: Image.Image) -> Image.Image:
    """Recolor every visible pixel to SILHOUETTE_COLOR, preserving the alpha shape."""
    alpha = img.convert("RGBA").getchannel("A")
    if ALPHA_THRESHOLD > 0:
        alpha = alpha.point(lambda a: 255 if a > ALPHA_THRESHOLD else 0)
    out = Image.new("RGBA", img.size, SILHOUETTE_COLOR + (0,))
    out.putalpha(alpha)
    return out


def union_bbox(frames):
    box = None
    for f in frames:
        b = f.getchannel("A").getbbox()
        if b is None:
            continue
        box = b if box is None else (min(box[0], b[0]), min(box[1], b[1]),
                                     max(box[2], b[2]), max(box[3], b[3]))
    return box


def contact_sheet():
    """Write a numbered grid of all frames so you can pick FRAMES indices."""
    rendered, tags = render_strip(3)
    frames = [f for f, _ in rendered]
    if tags:
        print("Tags:", ", ".join(f"{n}={lo}-{hi}" for n, (lo, hi) in tags.items()))
    cols, n = 9, len(frames)
    fw, fh = frames[0].size
    rows = (n + cols - 1) // cols
    pad = 16
    sheet = Image.new("RGBA", (cols * (fw + pad), rows * (fh + pad)), (24, 14, 34, 255))
    draw = ImageDraw.Draw(sheet)
    for i, f in enumerate(frames):
        x, y = (i % cols) * (fw + pad), (i // cols) * (fh + pad)
        sheet.alpha_composite(f, (x, y))
        draw.text((x + 2, y + 2), str(i), fill=(255, 255, 0, 255))
    out = PROJECT / "tmp" / "player-contact-sheet.png"
    out.parent.mkdir(exist_ok=True)
    sheet.save(out)
    print(f"Wrote numbered contact sheet: {out}  ({n} frames)")


def write_webp(sils, durs, output: Path):
    """Trim, encode an animated (or single-frame) transparent WebP, report stats."""
    if TRIM:
        box = union_bbox(sils)
        if box:
            sils = [s.crop(box) for s in sils]
    output.parent.mkdir(parents=True, exist_ok=True)
    save_kw = dict(format="WEBP", lossless=LOSSLESS, quality=100, method=6, exact=True)
    if len(sils) > 1:
        save_kw.update(save_all=True, append_images=sils[1:], duration=durs, loop=0)
    sils[0].save(output, **save_kw)
    w, h = sils[0].size
    print(f"Wrote {output.relative_to(PROJECT.parent)}  "
          f"{w}x{h}px, {len(sils)} frame(s), {output.stat().st_size} bytes")
    return w, h


def print_css(filename: str, w: int, h: int):
    print("\nCSS tint (recolor the white silhouette to any color):")
    print(f"""  .loading-bud {{
    width: {w}px; height: {h}px;
    background: currentColor;            /* the tint */
    -webkit-mask: url({filename}) center / contain no-repeat;
            mask: url({filename}) center / contain no-repeat;
    image-rendering: pixelated;          /* crisp upscaling */
  }}""")


def export():
    rendered, tags = render_strip(SCALE)
    print(f"Exporting {TAG if FRAMES is None else f'frames {list(FRAMES)}'}")
    steps = [{"tag": TAG, "frames": FRAMES, "ms": FRAME_DURATION_MS}]
    w, h = write_webp(*build_silhouettes(rendered, tags, steps), OUTPUT)
    print_css(OUTPUT.name, w, h)


def export_sequence():
    rendered, tags = render_strip(SCALE)
    desc = " -> ".join(f"{s.get('tag') or s.get('frames')}"
                       f"{'(flip)' if s.get('flip') else ''}x{s.get('repeat', 1)}"
                       for s in SEQUENCE)
    print(f"Exporting sequence: {desc}")
    w, h = write_webp(*build_silhouettes(rendered, tags, SEQUENCE), SEQUENCE_OUTPUT)
    print_css(SEQUENCE_OUTPUT.name, w, h)


if __name__ == "__main__":
    ap = argparse.ArgumentParser(description=__doc__)
    ap.add_argument("--contact-sheet", action="store_true",
                    help="write a numbered grid of all frames and exit")
    ap.add_argument("--tags", action="store_true",
                    help="list the sprite's animation tags and their frame ranges")
    ap.add_argument("--sequence", action="store_true",
                    help="export the multi-tag SEQUENCE (e.g. run/skid back-and-forth)")
    args = ap.parse_args()
    if args.tags:
        _, tags = render_strip(1)
        for name, (lo, hi) in tags.items():
            print(f"{name:20s} {lo}-{hi}")
    elif args.contact_sheet:
        contact_sheet()
    elif args.sequence:
        export_sequence()
    else:
        export()
