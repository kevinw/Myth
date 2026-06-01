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


def rename_actor_object(obj: dict, existing_actor_ids: set[str], legacy_actor_ids: dict[int, str]) -> tuple[dict, bool]:
    changed = False
    renamed = {}
    for key, value in obj.items():
        next_key = actor_key(key)
        changed |= next_key != key
        renamed[next_key] = value

    legacy_id = renamed.get("Id")
    if isinstance(legacy_id, str) and legacy_id.strip():
        legacy_actor_ids[id(renamed)] = legacy_id.strip()

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


def migrate_room(path: Path, existing_actor_ids: set[str], legacy_actor_ids: dict[int, str]) -> tuple[dict | None, bool]:
    data = json.loads(path.read_text())
    if not isinstance(data, dict):
        return None, False

    actors = data.get("actors")
    if not isinstance(actors, list):
        return data, False

    changed = False
    next_actors = []
    for actor in actors:
        if isinstance(actor, dict):
            migrated, actor_changed = rename_actor_object(actor, existing_actor_ids, legacy_actor_ids)
            next_actors.append(migrated)
            changed |= actor_changed
        else:
            next_actors.append(actor)

    if changed:
        data["actors"] = next_actors
    return data, changed


def actor_type(actor: dict, specs: dict[str, dict]) -> str | None:
    if isinstance(actor.get("Type"), str):
        return actor["Type"]
    from_name = actor.get("From")
    if isinstance(from_name, str) and isinstance(specs.get(from_name), dict):
        return specs[from_name].get("Type")
    return None


def is_linked_door(actor: dict, specs: dict[str, dict]) -> bool:
    return actor_type(actor, specs) == "LinkedDoor"


def convert_linked_door_targets(rooms: dict[str, dict], specs: dict[str, dict], legacy_actor_ids: dict[int, str]) -> set[str]:
    changed_rooms: set[str] = set()

    for room_id, room in rooms.items():
        actors = room.get("actors")
        if not isinstance(actors, list):
            continue

        for actor in actors:
            if not isinstance(actor, dict) or not is_linked_door(actor, specs):
                continue

            target_actor_id = actor.get("TargetActorId")
            if not isinstance(target_actor_id, str) or not target_actor_id.strip():
                target_room_id = actor.get("TargetRoomId")
                target_room = rooms.get(target_room_id) if isinstance(target_room_id, str) else None
                target_actor = None
                if target_room is not None and isinstance(target_room.get("actors"), list):
                    target_link_id = actor.get("TargetLinkId") or actor.get("LinkId")
                    target_door_id = actor.get("TargetDoorId")
                    candidates = [
                        candidate for candidate in target_room["actors"]
                        if isinstance(candidate, dict) and is_linked_door(candidate, specs)
                    ]

                    if isinstance(target_link_id, str) and target_link_id.strip():
                        target_actor = next((candidate for candidate in candidates if candidate.get("LinkId") == target_link_id), None)
                    if target_actor is None and isinstance(target_door_id, str) and target_door_id.strip():
                        target_actor = next((
                            candidate for candidate in candidates
                            if candidate.get("Id") == target_door_id or legacy_actor_ids.get(id(candidate)) == target_door_id
                        ), None)
                    if target_actor is None and candidates:
                        target_actor = candidates[0]

                if target_actor is not None:
                    actor["TargetActorId"] = target_actor["Id"]
                    changed_rooms.add(room_id)

            for old_key in ("TargetRoomId", "LinkId", "TargetLinkId", "TargetDoorId"):
                if old_key in actor:
                    del actor[old_key]
                    changed_rooms.add(room_id)

    return changed_rooms


def main() -> int:
    parser = argparse.ArgumentParser(description="Rename actor JSON keys from camelCase to C# member names.")
    parser.add_argument("--write", action="store_true", help="Rewrite files. Without this, only print files that would change.")
    parser.add_argument("--root", type=Path, default=Path.cwd(), help="Repository root. Defaults to current directory.")
    args = parser.parse_args()

    root = args.root
    changed = []
    specs: dict[str, dict] = {}
    legacy_actor_ids: dict[int, str] = {}
    existing_actor_ids: set[str] = set()

    actors_dir = root / "Assets" / "Actors"
    if actors_dir.exists():
        for path in sorted(actors_dir.rglob("*.json")):
            data = json.loads(path.read_text())
            migrated = data
            spec_changed = False
            if isinstance(data, dict):
                migrated, spec_changed = rename_spec_object(data)
                name = migrated.get("Name")
                if isinstance(name, str):
                    specs[name] = migrated
            if spec_changed:
                if args.write:
                    path.write_text(format_json(migrated))
                changed.append(path)

    rooms_dir = root / "Assets" / "Rooms"
    rooms: dict[str, dict] = {}
    room_paths: dict[str, Path] = {}
    room_changed: dict[str, bool] = {}
    if rooms_dir.exists():
        for path in sorted(rooms_dir.rglob("*.json")):
            data, changed_room = migrate_room(path, existing_actor_ids, legacy_actor_ids)
            if data is None:
                continue
            room_id = path.stem
            rooms[room_id] = data
            room_paths[room_id] = path
            room_changed[room_id] = changed_room

    convert_changed = convert_linked_door_targets(rooms, specs, legacy_actor_ids)
    for room_id in convert_changed:
        room_changed[room_id] = True

    for room_id, was_changed in room_changed.items():
        if not was_changed:
            continue
        path = room_paths[room_id]
        if args.write:
            path.write_text(format_json(rooms[room_id]))
        changed.append(path)

    action = "updated" if args.write else "would update"
    for path in changed:
        print(f"{action}: {path.relative_to(root)}")
    print(f"{len(changed)} file(s) {action}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
