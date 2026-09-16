#!/usr/bin/env python3

import json
import math
import re
import shutil
from pathlib import Path

try:
    import pyvips
except ImportError as error:
    raise SystemExit('pyvips is required: python3 -m pip install "pyvips[binary]"') from error

ROOT = Path(__file__).resolve().parents[2]
SOURCE = ROOT / "Production" / "Room Screenshots"
OUTPUT = ROOT / "Production" / "World Map"
TILE_SIZE = 256


def slug(value):
    return re.sub(r"[^A-Za-z0-9_.-]", "_", value)


def rgba(image):
    if image.bands == 4:
        return image
    if image.bands == 3:
        return image.bandjoin(255)
    raise ValueError(f"expected an RGB or RGBA room image, got {image.bands} bands")


def build_world(world, output):
    rooms = world["rooms"]
    left = min(room["x"] for room in rooms)
    top = min(room["y"] for room in rooms)
    right = max(room["x"] + room["width"] for room in rooms)
    bottom = max(room["y"] + room["height"] for room in rooms)
    width, height = right - left, bottom - top
    extent = 1 << math.ceil(math.log2(max(TILE_SIZE, width, height)))
    max_zoom = int(math.log2(extent // TILE_SIZE))
    world_slug = slug(world["id"])
    layers = []

    for sublayer in sorted({room["sublayer"] for room in rooms}):
        layer_rooms = [room for room in rooms if room["sublayer"] == sublayer]
        images = [rgba(pyvips.Image.new_from_file(str(SOURCE / room["file"]), access="sequential")) for room in layer_rooms]
        canvas = pyvips.Image.black(extent, extent, bands=4).copy(interpretation="srgb")
        canvas = canvas.composite(
            images,
            ["over"] * len(images),
            x=[room["x"] - left for room in layer_rooms],
            y=[room["y"] - top for room in layer_rooms],
        )
        layer_slug = f"layer-{sublayer}"
        tiles = output / "worlds" / world_slug / layer_slug
        tiles.parent.mkdir(parents=True, exist_ok=True)
        canvas.dzsave(
            str(tiles),
            layout="google",
            suffix=".png",
            tile_size=TILE_SIZE,
            overlap=0,
            depth="onetile",
            skip_blanks=-1,
            background=[0, 0, 0, 0],
        )
        layers.append({
            "id": sublayer,
            "label": "Main" if sublayer == 0 else f"Sublayer {sublayer}",
            "tiles": f"worlds/{world_slug}/{layer_slug}/{{z}}/{{y}}/{{x}}.png",
        })

    return {
        "id": world["id"],
        "width": width,
        "height": height,
        "extent": extent,
        "maxZoom": max_zoom,
        "origin": {"x": left, "y": top},
        "layers": layers,
    }


def main():
    manifest_path = SOURCE / "rooms.json"
    if not manifest_path.exists():
        raise SystemExit("room export missing; run: dotnet run --project Myth.csproj -- --write-all-rooms")
    source = json.loads(manifest_path.read_text())
    temporary = OUTPUT.with_name(OUTPUT.name + ".tmp")
    shutil.rmtree(temporary, ignore_errors=True)
    temporary.mkdir(parents=True)
    worlds = [build_world(world, temporary) for world in source["worlds"] if world["rooms"]]
    (temporary / "map.json").write_text(json.dumps({"version": 1, "worlds": worlds}, indent=2) + "\n")
    shutil.rmtree(OUTPUT, ignore_errors=True)
    temporary.rename(OUTPUT)
    print(f"Built {len(worlds)} worlds in {OUTPUT}")


if __name__ == "__main__":
    main()
