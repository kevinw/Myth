#!/usr/bin/env python3

# Builds Notes/MapStudy/super-metroid.mvmap.json from the Map Randomizer's vanilla data:
#   room_geometry.json  -- 253 rooms, map-tile masks, door pointers, item locations
#   maps/vanilla/vanilla_map.json -- each room's map position, and every door pairing
# Both CC BY 4.0, github.com/blkerby/MapRandomizer. No ROM is read; nothing here is game asset data.

import json
import urllib.request
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
OUTPUT = ROOT / "Notes/MapStudy/super-metroid.mvmap.json"
BASE = "https://raw.githubusercontent.com/blkerby/MapRandomizer/main"
AREAS = ["Crateria", "Brinstar", "Norfair", "Wrecked Ship", "Maridia", "Tourian", "Ceres"]
BOSSES = {"Kraid Room", "Phantoon's Room", "Draygon's Room", "Ridley's Room", "Mother Brain Room"}


def fetch(path):
    with urllib.request.urlopen(f"{BASE}/{path}") as response:
        return json.load(response)


def tags(room):
    found = []
    if "Save Room" in room["name"]:
        found.append("save")
    if room["name"] in BOSSES:
        found.append("boss")
    if room["items"]:
        found.append("upgrade")
    if "Elevator" in room["name"]:
        found.append("transit")
    return found


def main():
    geometry, vanilla = fetch("room_geometry.json"), fetch("maps/vanilla/vanilla_map.json")
    positions = vanilla["rooms"]
    assert len(geometry) == len(positions), f"{len(geometry)} rooms but {len(positions)} positions"

    rooms, owner = [], {}
    for room, (x, y) in zip(geometry, positions):
        identifier = room["name"]
        rooms.append({
            "id": identifier,
            "rect": [x, y, len(room["map"][0]), len(room["map"])],
            "region": AREAS[room["area"]],
            "tags": tags(room),
        })
        for door in room["doors"]:
            # Only exit_ptr identifies the owning room; entrance_ptr points at the far side's door.
            if door.get("exit_ptr") is not None:
                owner[door["exit_ptr"]] = identifier

    edges, seen = [], set()
    for (a_exit, a_entrance), (b_exit, b_entrance), bidirectional in vanilla["doors"]:
        a, b = owner.get(a_exit), owner.get(b_exit)
        if not a or not b or a == b:
            continue
        key = frozenset((a, b)) if bidirectional else (a, b)
        if key in seen:
            continue
        seen.add(key)
        edges.append({"a": a, "b": b, "kind": "door" if bidirectional else "one-way"})

    payload = {
        "game": "Super Metroid",
        "source": "blkerby/MapRandomizer room_geometry.json + maps/vanilla/vanilla_map.json (CC BY 4.0). "
                  "Vanilla layout; gates not yet imported -- see sm-json-data for per-strat requirements.",
        "start": "Landing Site",
        "rooms": rooms,
        "edges": edges,
        "items": [],
    }
    OUTPUT.write_text(json.dumps(payload, indent=1) + "\n")
    print(f"Super Metroid: {len(rooms)} rooms, {len(edges)} edges -> {OUTPUT}")


if __name__ == "__main__":
    main()
