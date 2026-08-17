#!/usr/bin/env python3

import argparse
import datetime as dt
import json
import math
import re
import subprocess
from collections import Counter
from itertools import combinations
from pathlib import Path


ROOT = Path(__file__).resolve().parent.parent
OUTPUT = ROOT / "Platforms/Web/dashboard/data.json"
HISTORY_PATHS = ["Assets/Rooms", "Assets/Actors", "Assets/Sources/Tilesets", "Source", "tools/Myth.SourceGen", "Platforms/iOS"]
PROJECT_NAMES = ("Myth", "TinyLink")
SOLID_TILE_NAMES = {"castle", "grass", "metal", "stone", "crosshatch", "dirt"}
SOLID_TILE_CHARS = set("1GSMTP%2SY$J")
CODE_PREFIXES = ("Source/", "tools/Myth.SourceGen/", "Platforms/iOS/")
CODE_EXCLUDES = ("Source/Audio/FMOD/", "Source/External/", "/bin/", "/obj/")
INFRASTRUCTURE_ACTOR_TYPES = {
    "BudStoryBark", "CameraBlocker", "Cutscene", "Goal", "Light", "ParticleEmitter",
    "PlayerStart", "Prop", "SavePoint", "TitleText", "Unknown", "Wildlife",
}


def git(*args, text=True):
    return subprocess.check_output(["git", *args], cwd=ROOT, text=text)


def selected_commits():
    paths = [f":(top){project}/{path}" for project in PROJECT_NAMES for path in HISTORY_PATHS]
    rows = git("log", "--format=%H\t%cI", "--", *paths).splitlines()
    selected = {}
    for row in rows:
        commit, timestamp = row.split("\t", 1)
        date = dt.datetime.fromisoformat(timestamp).date()
        key = date.isoformat()
        selected.setdefault(key, (commit, date))
    return list(reversed(selected.values()))


def relevant(path):
    return (
        path.startswith("Assets/Rooms/") and path.endswith(".json")
        or path.startswith("Assets/Actors/") and path.endswith(".json")
        or path.startswith("Assets/Sources/Tilesets/") and path.endswith((".kit.json", ".nct.json"))
        or code_path(path)
    )


def code_path(path):
    return path.endswith(".cs") and path.startswith(CODE_PREFIXES) and not any(part in path for part in CODE_EXCLUDES)


class GitObjects:
    def __init__(self):
        self.process = subprocess.Popen(
            ["git", "cat-file", "--batch"], cwd=ROOT, stdin=subprocess.PIPE, stdout=subprocess.PIPE
        )

    def read(self, object_name):
        self.process.stdin.write(f"{object_name}\n".encode())
        self.process.stdin.flush()
        header = self.process.stdout.readline().decode().rstrip().split()
        if len(header) < 3 or header[1] == "missing":
            return None
        size = int(header[2])
        data = self.process.stdout.read(size)
        self.process.stdout.read(1)
        return data

    def close(self):
        self.process.stdin.close()
        self.process.wait()


def files_at(commit, objects):
    entries = git("ls-tree", "-r", "-z", "--full-tree", commit, *PROJECT_NAMES).split("\0")
    files = {}
    for entry in entries:
        if not entry:
            continue
        meta, full_path = entry.split("\t", 1)
        project, separator, path = full_path.partition("/")
        if separator and project in PROJECT_NAMES and relevant(path):
            files[path] = objects.read(meta.split()[2])
    return files


def working_files():
    files = {}
    for base in HISTORY_PATHS:
        root = ROOT / base
        if not root.exists():
            continue
        for path in root.rglob("*"):
            relative = path.relative_to(ROOT).as_posix()
            if path.is_file() and relevant(relative):
                files[relative] = path.read_bytes()
    return files


def parse_json(raw):
    try:
        return json.loads(raw)
    except (json.JSONDecodeError, UnicodeDecodeError):
        return None


def get_ci(value, key):
    if not isinstance(value, dict):
        return None
    key = key.lower()
    return next((item for name, item in value.items() if name.lower() == key), None)


def actor_types(files):
    specs = {}
    for path, raw in files.items():
        if not path.startswith("Assets/Actors/"):
            continue
        value = parse_json(raw)
        if isinstance(value, dict):
            name = get_ci(value, "Name") or get_ci(value, "Id") or Path(path).stem
            specs[name.lower()] = value

    def resolve(value):
        seen = set()
        while isinstance(value, dict):
            if get_ci(value, "Type"):
                return get_ci(value, "Type")
            parent = get_ci(value, "From")
            if not isinstance(parent, str) or parent.lower() in seen:
                break
            seen.add(parent.lower())
            value = specs.get(parent.lower())
        return "Unknown"

    return resolve


def solid_tile_names(files):
    names = set(SOLID_TILE_NAMES)
    for path, raw in files.items():
        if not path.startswith("Assets/Sources/Tilesets/"):
            continue
        value = parse_json(raw)
        if not isinstance(value, dict):
            continue
        name = Path(path).name.removesuffix(".kit.json").removesuffix(".nct.json")
        if path.endswith(".kit.json") and value.get("layer", "Foreground").lower() == "foreground":
            names.add(name.lower())
        elif path.endswith(".nct.json") and value.get("solid") is True:
            names.add(name.lower())
    return names


def room_distance(a, b):
    union = a["actors"] | b["actors"]
    actor_distance = 1 - len(a["actors"] & b["actors"]) / len(union) if union else 0
    pressure_distance = abs(a["pressure"] - b["pressure"])
    area_distance = min(1, abs(math.log(max(1, a["area"]) / max(1, b["area"]))) / math.log(8))
    aspect_distance = min(1, abs(math.log(max(.01, a["aspect"]) / max(.01, b["aspect"]))) / math.log(4))
    return actor_distance * .55 + pressure_distance * .2 + area_distance * .15 + aspect_distance * .1


def design_distance(rooms):
    if len(rooms) < 2:
        return 0
    nearest = [min(room_distance(room, other) for other in rooms if other is not room) for room in rooms]
    return round(sum(nearest) / len(nearest) * 100, 2)


def world_metrics(files):
    result = {}
    resolve_type = actor_types(files)
    solid_names = solid_tile_names(files)
    for path, raw in files.items():
        match = re.fullmatch(r"Assets/Rooms/(?:(?P<world>[^/]+)/)?[^/]+\.json", path)
        if not match:
            continue
        room = parse_json(raw)
        tiles = get_ci(room, "tiles")
        if not isinstance(room, dict) or not isinstance(tiles, list):
            continue
        world_name = match.group("world") or "FrogWorld"
        world = result.setdefault(world_name, {
            "rooms": 0, "tileCapacity": 0, "solidTiles": 0, "actors": 0,
            "actorTypes": Counter(), "_interactionPairs": set(), "_rooms": [],
        })
        world["rooms"] += 1
        size = get_ci(room, "size")
        if isinstance(size, list) and len(size) >= 2 and all(isinstance(value, int) for value in size[:2]):
            width, height = size[:2]
        else:
            width, height = max((len(row) for row in tiles if isinstance(row, str)), default=0), len(tiles)
        capacity = width * height
        world["tileCapacity"] += capacity
        legend_value = get_ci(room, "legend") or {}
        legend = {key: str(value).lower() for key, value in legend_value.items()}
        room_solids = 0
        for row in tiles:
            if not isinstance(row, str):
                continue
            room_solids += sum(
                tile in SOLID_TILE_CHARS or legend.get(tile) in solid_names or legend.get(tile, "").endswith(" slope")
                for tile in row
            )
        world["solidTiles"] += room_solids
        actors = get_ci(room, "actors") or []
        if not isinstance(actors, list):
            continue
        room_actor_types = set()
        for actor in actors:
            if not isinstance(actor, dict):
                continue
            actor_type = resolve_type(actor)
            world["actors"] += 1
            world["actorTypes"][actor_type] += 1
            if actor_type not in INFRASTRUCTURE_ACTOR_TYPES:
                room_actor_types.add(actor_type)
        world["_interactionPairs"].update(combinations(sorted(room_actor_types), 2))
        world["_rooms"].append({
            "actors": room_actor_types,
            "pressure": room_solids / capacity if capacity else 0,
            "area": capacity,
            "aspect": width / height if height else 1,
        })
    for world in result.values():
        counts = world["actorTypes"].values()
        entropy = -sum((count / world["actors"]) * math.log(count / world["actors"]) for count in counts) if world["actors"] else 0
        world["terrainPressure"] = round(world["solidTiles"] / world["tileCapacity"] * 100, 2) if world["tileCapacity"] else 0
        world["castDiversity"] = round(math.exp(entropy), 2) if world["actors"] else 0
        rooms = world.pop("_rooms")
        world["designDistance"] = design_distance(rooms)
        world["interactionTriangles"] = sum(1 for room in rooms if len(room["actors"]) >= 3)
        world["_interactionPairs"] = [list(pair) for pair in sorted(world["_interactionPairs"])]
        world["actorTypes"] = dict(sorted(world["actorTypes"].items(), key=lambda item: (-item[1], item[0].lower())))
    return dict(sorted(result.items()))


def strip_csharp(source):
    output = []
    i = 0
    state = "code"
    raw_quotes = 0
    while i < len(source):
        char = source[i]
        next_char = source[i + 1] if i + 1 < len(source) else ""
        if state == "code":
            if char == "/" and next_char == "/":
                output.extend("  ")
                i += 2
                state = "line"
                continue
            if char == "/" and next_char == "*":
                output.extend("  ")
                i += 2
                state = "block"
                continue
            if char == '"':
                raw_quotes = len(source[i:]) - len(source[i:].lstrip('"'))
                if raw_quotes >= 3:
                    output.extend(" " * raw_quotes)
                    i += raw_quotes
                    state = "raw"
                    continue
                output.append(" ")
                i += 1
                state = "verbatim" if i >= 2 and source[i - 2] == "@" else "string"
                continue
            if char == "'":
                output.append(" ")
                i += 1
                state = "char"
                continue
            output.append(char)
        elif state == "line":
            output.append("\n" if char == "\n" else " ")
            if char == "\n":
                state = "code"
        elif state == "block":
            output.append("\n" if char == "\n" else " ")
            if char == "*" and next_char == "/":
                output.append(" ")
                i += 1
                state = "code"
        elif state == "raw":
            output.append("\n" if char == "\n" else " ")
            if char == '"' and source.startswith('"' * raw_quotes, i):
                output.extend(" " * (raw_quotes - 1))
                i += raw_quotes - 1
                state = "code"
        else:
            output.append("\n" if char == "\n" else " ")
            if state == "string" and char == "\\":
                if i + 1 < len(source):
                    output.append("\n" if source[i + 1] == "\n" else " ")
                    i += 1
            elif state == "verbatim" and char == '"' and next_char == '"':
                output.append(" ")
                i += 1
            elif state in {"string", "verbatim"} and char == '"' or state == "char" and char == "'":
                state = "code"
        i += 1
    return "".join(output)


TOKEN = re.compile(r"[A-Za-z_]\w*|=>|&&|\|\||\?\?|\?\.|::|[{}()<>;?,.:=\[\]]")
CONTROL = {"if", "for", "foreach", "while", "switch", "catch", "using", "lock", "fixed", "nameof", "typeof", "sizeof"}
TYPE_DECLARATIONS = {"class", "struct", "record", "interface", "enum", "delegate"}
BRANCH = {"if", "for", "foreach", "while", "catch", "case", "&&", "||", "?"}


def file_complexity(source):
    clean = strip_csharp(source)
    tokens = TOKEN.findall(clean)
    pairs = {}
    stack = []
    for index, token in enumerate(tokens):
        if token in "({":
            stack.append((token, index))
        elif token in ")}":
            expected = "(" if token == ")" else "{"
            for pos in range(len(stack) - 1, -1, -1):
                if stack[pos][0] == expected:
                    _, start = stack.pop(pos)
                    pairs[start] = index
                    break

    methods = []
    for open_paren, close_paren in pairs.items():
        if tokens[open_paren] != "(" or open_paren == 0:
            continue
        name = tokens[open_paren - 1]
        if not re.fullmatch(r"[A-Za-z_]\w*", name) or name in CONTROL:
            continue
        if open_paren > 1 and tokens[open_paren - 2] in {".", "new", "return", "=", "=>", *TYPE_DECLARATIONS}:
            continue
        next_index = close_paren + 1
        while next_index < len(tokens) and tokens[next_index] in {"?", "[", "]"}:
            next_index += 1
        if next_index >= len(tokens) or tokens[next_index] not in {"{", "=>"}:
            continue
        if tokens[next_index] == "{":
            end = pairs.get(next_index)
            if end is None:
                continue
        else:
            end = next((index for index in range(next_index + 1, len(tokens)) if tokens[index] == ";"), next_index)
        methods.append((next_index, end))

    complexities = []
    for start, end in methods:
        branches = 0
        for index in range(start + 1, end):
            token = tokens[index]
            if token not in BRANCH:
                continue
            if token == "?" and index + 1 < len(tokens) and tokens[index + 1] in {"?", "."}:
                continue
            branches += 1
        complexities.append(1 + branches)
    lines = sum(bool(line.strip()) for line in clean.splitlines())
    return lines, complexities


def code_metrics(files):
    lines = 0
    source_file_lines = []
    complexities = []
    for path, raw in files.items():
        if not code_path(path):
            continue
        try:
            source_lines, file_complexities = file_complexity(raw.decode("utf-8"))
        except UnicodeDecodeError:
            continue
        lines += source_lines
        source_file_lines.append(source_lines)
        complexities.extend(file_complexities)
    total = sum(complexities)
    top_ten_lines = sum(sorted(source_file_lines, reverse=True)[:10])
    return {
        "lines": lines,
        "complexity": total,
        "decisionDensity": round(total / lines * 1000, 2) if lines else 0,
        "topTenGravity": round(top_ten_lines / lines * 100, 2) if lines else 0,
        "methods": len(complexities),
        "averageComplexity": round(total / len(complexities), 2) if complexities else 0,
        "maxComplexity": max(complexities, default=0),
    }


def snapshot(date, commit, files, current=False):
    return {
        "date": date.isoformat(),
        "commit": commit[:8],
        "current": current,
        "code": code_metrics(files),
        "worlds": world_metrics(files),
    }


def annotate_design_practice(snapshots):
    history = {}
    for item in snapshots:
        for name, world in item["worlds"].items():
            pairs = {tuple(pair) for pair in world.pop("_interactionPairs")}
            actor_types = set(world["actorTypes"]) - INFRASTRUCTURE_ACTOR_TYPES
            if name not in history:
                history[name] = {"pairs": set(pairs), "actors": set(actor_types), "reuse": 0}
                world["newNovelCombinations"] = 0
                world["newReuseCombinations"] = 0
            else:
                seen = history[name]
                novel = pairs - seen["pairs"]
                reused = {pair for pair in novel if pair[0] in seen["actors"] and pair[1] in seen["actors"]}
                world["newNovelCombinations"] = len(novel)
                world["newReuseCombinations"] = len(reused)
                seen["pairs"].update(novel)
                seen["actors"].update(actor_types)
                seen["reuse"] += len(reused)
            world["novelCombinations"] = len(history[name]["pairs"])
            world["reuseCombinations"] = history[name]["reuse"]


def main():
    parser = argparse.ArgumentParser(description="Generate Human Questions's static development dashboard data.")
    parser.add_argument("--output", type=Path, default=OUTPUT)
    parser.add_argument("--current-only", action="store_true", help="Skip Git history (useful for quick iteration).")
    args = parser.parse_args()

    head = git("rev-parse", "HEAD").strip()
    snapshots = []
    if not args.current_only:
        objects = GitObjects()
        try:
            for commit, date in selected_commits():
                snapshots.append(snapshot(date, commit, files_at(commit, objects)))
        finally:
            objects.close()

    current_date = dt.datetime.now(dt.timezone.utc).date()
    current = snapshot(current_date, head, working_files(), current=True)
    if snapshots and snapshots[-1]["commit"] == current["commit"] and snapshots[-1]["worlds"] == current["worlds"] and snapshots[-1]["code"] == current["code"]:
        snapshots[-1] = current
    else:
        snapshots.append(current)
    annotate_design_practice(snapshots)

    payload = {
        "generatedAt": dt.datetime.now(dt.timezone.utc).replace(microsecond=0).isoformat().replace("+00:00", "Z"),
        "sample": "Latest relevant commit on each active calendar day, plus the current worktree.",
        "methodology": {
            "solidTiles": "Foreground cells whose resolved tile registration blocks movement; slopes are included.",
            "lines": "Non-blank first-party C# source lines after comments and literals are removed; generated FMOD bindings and external code are excluded.",
            "complexity": "Aggregate McCabe complexity across C# methods: one per method plus branches, cases, catches, ternaries, and short-circuit operators.",
            "decisionDensity": "Aggregate cyclomatic complexity per 1,000 first-party C# source lines.",
            "topTenGravity": "Percentage of first-party C# source lines contained in the ten largest source files.",
            "terrainPressure": "Movement-blocking foreground cells as a percentage of all room tile-space in the selected world.",
            "castDiversity": "The exponential of Shannon entropy across placed actor types: the equivalent number of equally common types.",
            "novelCombinations": "Previously unseen pairs of gameplay actor types placed in the same room during a historical interval; infrastructure and decorative actors are excluded.",
            "reuseCombinations": "Novel co-locations made entirely from actor types already placed in an earlier interval.",
            "designDistance": "Average nearest-room distance on a 0–100 scale, weighted by gameplay-actor mix, terrain pressure, room area, and aspect ratio.",
            "interactionTriangles": "Rooms containing at least three distinct gameplay actor types after infrastructure and decorative actors are excluded.",
        },
        "snapshots": snapshots,
    }
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(payload, separators=(",", ":")) + "\n")
    latest = snapshots[-1]
    print(f"Dashboard: {len(snapshots)} snapshots, {len(latest['worlds'])} worlds, {latest['code']['lines']:,} lines -> {args.output}")


if __name__ == "__main__":
    main()
