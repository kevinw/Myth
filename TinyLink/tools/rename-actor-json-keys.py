#!/usr/bin/env python3
import argparse
import json
import re
import secrets
from pathlib import Path


def pascal_key(key: str) -> str:
    return key[:1].upper() + key[1:] if key else key


def actor_key(key: str) -> str:
    return pascal_key(key)


def spec_key(key: str) -> str:
    if key.lower() == "id":
        return "Name"
    return pascal_key(key)


def new_actor_id(existing: set[str]) -> str:
    while True:
        value = f"{secrets.randbits(64):016x}"
        if value not in existing:
            existing.add(value)
            return value


def normalize_actor_id(value, existing: set[str]) -> tuple[str, bool]:
    if isinstance(value, str):
        text = value.strip().lower()
        if re.fullmatch(r"[0-9a-f]{16}", text) and text not in existing:
            existing.add(text)
            return text, text != value

    return new_actor_id(existing), True


def rename_actor_object(obj: dict, existing_actor_ids: set[str]) -> tuple[dict, bool]:
    changed = False
    renamed = {}
    for key, value in obj.items():
        next_key = actor_key(key)
        changed |= next_key != key
        renamed[next_key] = value

    actor_id, id_changed = normalize_actor_id(renamed.get("Id"), existing_actor_ids)
    renamed["Id"] = actor_id
    changed |= id_changed or "Id" not in obj
    return renamed, changed


def rename_spec_object(obj: dict) -> tuple[dict, bool]:
    changed = False
    renamed = {}
    for key, value in obj.items():
        next_key = spec_key(key)
        changed |= next_key != key
        renamed[next_key] = value
    return renamed, changed


def compact_point_arrays(text: str) -> str:
    return re.sub(
        r"\[\s*(-?\d+(?:\.\d+)?),\s*(-?\d+(?:\.\d+)?)\s*\]",
        r"[\1, \2]",
        text,
    )


def format_json(data) -> str:
    return compact_point_arrays(json.dumps(data, indent=2) + "\n")


def migrate_actor_spec(path: Path, write: bool) -> bool:
    data = json.loads(path.read_text())
    if not isinstance(data, dict):
        return False

    migrated, changed = rename_spec_object(data)
    if changed and write:
        path.write_text(format_json(migrated))
    return changed


def migrate_room(path: Path, write: bool, existing_actor_ids: set[str]) -> bool:
    data = json.loads(path.read_text())
    if not isinstance(data, dict):
        return False

    actors = data.get("actors")
    if not isinstance(actors, list):
        return False

    changed = False
    next_actors = []
    for actor in actors:
        if isinstance(actor, dict):
            migrated, actor_changed = rename_actor_object(actor, existing_actor_ids)
            next_actors.append(migrated)
            changed |= actor_changed
        else:
            next_actors.append(actor)

    if changed:
        data["actors"] = next_actors
        if write:
            path.write_text(format_json(data))
    return changed


def main() -> int:
    parser = argparse.ArgumentParser(description="Rename actor JSON keys from camelCase to C# member names.")
    parser.add_argument("--write", action="store_true", help="Rewrite files. Without this, only print files that would change.")
    parser.add_argument("--root", type=Path, default=Path.cwd(), help="Repository root. Defaults to current directory.")
    args = parser.parse_args()

    root = args.root
    changed = []
    existing_actor_ids: set[str] = set()

    actors_dir = root / "Assets" / "Actors"
    if actors_dir.exists():
        for path in sorted(actors_dir.rglob("*.json")):
            if migrate_actor_spec(path, args.write):
                changed.append(path)

    rooms_dir = root / "Assets" / "Rooms"
    if rooms_dir.exists():
        for path in sorted(rooms_dir.rglob("*.json")):
            if migrate_room(path, args.write, existing_actor_ids):
                changed.append(path)

    action = "updated" if args.write else "would update"
    for path in changed:
        print(f"{action}: {path.relative_to(root)}")
    print(f"{len(changed)} file(s) {action}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
