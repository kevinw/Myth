#!/usr/bin/env python3

# Computes map-structure metrics over reference metroidvania maps (Notes/MapStudy/*.mvmap.json)
# and over our own rooms, so "is our map interesting yet" is a reading and not a taste.

import argparse
import json
import math
import re
from collections import Counter, defaultdict, deque
from itertools import combinations
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
STUDY = ROOT / "Notes/MapStudy"
PASSABLE = {"open", "door", "hidden", "trick"}


def load_map(path):
    game = json.loads(path.read_text())
    game.setdefault("source", str(path.relative_to(ROOT)))
    return game


def our_map(world):
    rooms, edges = [], []
    for path in sorted((ROOT / "Assets/Rooms" / world).glob("*.json")):
        room = json.loads(path.read_text())
        (x, y), (w, h) = room["position"], room["size"]
        types = {actor.get("Type") or actor.get("From") for actor in room.get("actors", [])}
        rooms.append({"id": path.stem, "rect": [x, y, w, h], "region": path.stem.split("_")[0],
                      "tags": sorted(tag for tag in ("SavePoint", "Goal") if tag in types)})
    for a, b in combinations(rooms, 2):
        if touching(a["rect"], b["rect"]):
            edges.append({"a": a["id"], "b": b["id"], "kind": "open"})
    return {"game": f"{world} (ours)", "source": f"Assets/Rooms/{world}", "rooms": rooms,
            "edges": edges, "items": [], "start": rooms[0]["id"] if rooms else None}


def touching(a, b):
    (ax, ay, aw, ah), (bx, by, bw, bh) = a, b
    shares_x = ax < bx + bw and bx < ax + aw
    shares_y = ay < by + bh and by < ay + ah
    return (shares_x and (ay + ah == by or by + bh == ay)) or (shares_y and (ax + aw == bx or bx + bw == ax))


def adjacency(game, held=None):
    # held=None means ignore gates entirely (the pure map graph).
    graph = defaultdict(set)
    for room in game["rooms"]:
        graph[room["id"]]
    for edge in game["edges"]:
        kind = edge["kind"]
        ability = kind[5:] if kind.startswith("gate:") else None
        if held is not None and ability and ability not in held:
            continue
        graph[edge["a"]].add(edge["b"])
        if kind != "one-way":
            graph[edge["b"]].add(edge["a"])
    return graph


def components(graph):
    seen, found = set(), 0
    for node in graph:
        if node in seen:
            continue
        found += 1
        stack = [node]
        while stack:
            current = stack.pop()
            if current in seen:
                continue
            seen.add(current)
            stack.extend(graph[current] - seen)
    return found


def loop_spectrum(graph):
    # Fundamental cycle basis from a BFS spanning forest: each non-tree edge closes one loop.
    parent, depth, seen = {}, {}, set()
    for root in graph:
        if root in seen:
            continue
        parent[root], depth[root], seen = None, 0, seen | {root}
        queue = deque([root])
        while queue:
            node = queue.popleft()
            for other in graph[node]:
                if other not in seen:
                    seen.add(other)
                    parent[other], depth[other] = node, depth[node] + 1
                    queue.append(other)
    tree = {frozenset((node, parent[node])) for node in parent if parent[node]}
    lengths = []
    for a, others in graph.items():
        for b in others:
            if a < b and frozenset((a, b)) not in tree:
                lengths.append(tree_distance(a, b, parent, depth) + 1)
    return sorted(lengths)


def tree_distance(a, b, parent, depth):
    steps = 0
    while depth.get(a, 0) > depth.get(b, 0):
        a, steps = parent[a], steps + 1
    while depth.get(b, 0) > depth.get(a, 0):
        b, steps = parent[b], steps + 1
    while a != b and a is not None and b is not None:
        a, b, steps = parent[a], parent[b], steps + 2
    return steps


def betweenness(graph):
    # Brandes, unweighted.
    score = dict.fromkeys(graph, 0.0)
    for source in graph:
        stack, paths, dist = [], dict.fromkeys(graph, 0), dict.fromkeys(graph, -1)
        predecessors = defaultdict(list)
        paths[source], dist[source] = 1, 0
        queue = deque([source])
        while queue:
            node = queue.popleft()
            stack.append(node)
            for other in graph[node]:
                if dist[other] < 0:
                    dist[other] = dist[node] + 1
                    queue.append(other)
                if dist[other] == dist[node] + 1:
                    paths[other] += paths[node]
                    predecessors[other].append(node)
        delta = dict.fromkeys(graph, 0.0)
        while stack:
            node = stack.pop()
            for previous in predecessors[node]:
                delta[previous] += paths[previous] / paths[node] * (1 + delta[node])
            if node != source:
                score[node] += delta[node]
    return score


def reachable(graph, start):
    seen, stack = set(), [start]
    while stack:
        node = stack.pop()
        if node in seen or node not in graph:
            continue
        seen.add(node)
        stack.extend(graph[node])
    return seen


def progression(game):
    # BFS over ability sets. Yields the acquisition DAG, which gives frontier width,
    # ordering count, and gate spans all at once.
    items = {item["ability"]: item["room"] for item in game.get("items", [])}
    gated = {edge["kind"][5:] for edge in game["edges"] if edge["kind"].startswith("gate:")}
    abilities = set(items) | gated
    innate = frozenset(abilities - set(items))
    if not game.get("start") or len(abilities) > 22:
        return None
    transitions, frontier = defaultdict(set), {}
    queue, seen = deque([innate]), {innate}
    while queue:
        held = queue.popleft()
        rooms = reachable(adjacency(game, held), game["start"])
        frontier[held] = sum(
            1 for edge in game["edges"]
            if edge["kind"].startswith("gate:") and edge["kind"][5:] not in held
            and (edge["a"] in rooms or edge["b"] in rooms)
        )
        for ability, room in items.items():
            if ability not in held and room in rooms:
                nxt = held | {ability}
                transitions[held].add((ability, nxt))
                if nxt not in seen:
                    seen.add(nxt)
                    queue.append(nxt)
    full = max(seen, key=len)
    return {"innate": innate, "items": items, "abilities": abilities, "transitions": transitions,
            "frontier": frontier, "states": seen, "full": full,
            "complete": len(full) == len(abilities)}


def orderings(run, cap=10 ** 7):
    memo = {}

    def count(state):
        if state == run["full"]:
            return 1
        if state not in memo:
            memo[state] = min(cap, sum(count(nxt) for _, nxt in run["transitions"][state]))
        return memo[state]

    return count(run["innate"])


def gate_spans(game, run):
    # Rooms travelled from an ability's pickup to the nearest gate it opens.
    spans = {}
    for ability, room in run["items"].items():
        locks = [edge for edge in game["edges"] if edge["kind"] == f"gate:{ability}"]
        if not locks:
            continue
        state = next((held for held in run["states"] if ability in held), None)
        graph = adjacency(game, state)
        distance = hops(graph, room)
        reach = [min(distance.get(edge["a"], math.inf), distance.get(edge["b"], math.inf)) for edge in locks]
        reach = [value for value in reach if value < math.inf]
        spans[ability] = {"locks": len(locks), "firstUse": min(reach) if reach else None,
                          "medianUse": median(reach) if reach else None}
    return spans


def hops(graph, start):
    distance, queue = {start: 0}, deque([start])
    while queue:
        node = queue.popleft()
        for other in graph.get(node, ()):
            if other not in distance:
                distance[other] = distance[node] + 1
                queue.append(other)
    return distance


def median(values):
    values = sorted(values)
    if not values:
        return None
    middle = len(values) // 2
    return values[middle] if len(values) % 2 else (values[middle - 1] + values[middle]) / 2


def interdigitation(rooms):
    # Do regions interleave in world space, or tile neatly? Overlapping bounding boxes
    # are what make a discovered shortcut feel physically true.
    boxes = {}
    for room in rooms:
        x, y, w, h = room["rect"]
        region = room.get("region") or "-"
        box = boxes.get(region)
        boxes[region] = [min(box[0], x), min(box[1], y), max(box[2], x + w), max(box[3], y + h)] if box else [x, y, x + w, y + h]
    if len(boxes) < 2:
        return 0.0
    overlapping = sum(
        max(0, min(a[2], b[2]) - max(a[0], b[0])) * max(0, min(a[3], b[3]) - max(a[1], b[1])) > 0
        for a, b in combinations(boxes.values(), 2)
    )
    return round(overlapping / math.comb(len(boxes), 2) * 100, 1)


def silhouettes(rooms):
    shapes = Counter(tuple(room["rect"][2:]) for room in rooms)
    return round(sum(count - 1 for count in shapes.values()) / max(1, len(rooms)) * 100, 1)


def analyse(game):
    rooms, edges = game["rooms"], game["edges"]
    graph = adjacency(game)
    degree = {node: len(others) for node, others in graph.items()}
    loops = loop_spectrum(graph)
    central = betweenness(graph)
    ranked = sorted(central, key=lambda node: -central[node])
    percentile = {node: 1 - index / max(1, len(ranked) - 1) for index, node in enumerate(ranked)}
    saves = [room["id"] for room in rooms if "save" in {tag.lower() for tag in room.get("tags", [])}]
    areas = [room["rect"][2] * room["rect"][3] for room in rooms]
    kinds = Counter(edge["kind"].split(":")[0] for edge in edges)

    report = {
        "game": game["game"],
        "source": game["source"],
        "rooms": len(rooms),
        "edges": len(edges),
        "cycleRank": len(edges) - len(rooms) + components(graph),
        "cycleRankPer100": round((len(edges) - len(rooms) + components(graph)) / max(1, len(rooms)) * 100, 1),
        "shortLoopShare": round(sum(length <= 4 for length in loops) / len(loops) * 100, 1) if loops else 0,
        "medianLoop": median(loops),
        "branchingShare": round(sum(value >= 3 for value in degree.values()) / max(1, len(rooms)) * 100, 1),
        "deadEndShare": round(sum(value <= 1 for value in degree.values()) / max(1, len(rooms)) * 100, 1),
        "saveCentrality": round(sum(percentile[node] for node in saves) / len(saves) * 100, 1) if saves else None,
        # Saves are cul-de-sacs hung off hubs, so the room's own centrality understates the placement:
        # what matters is the corridor it attaches to.
        "saveAnchorCentrality": round(sum(max(percentile[other] for other in graph[node])
                                          for node in saves if graph[node]) / len(saves) * 100, 1) if saves else None,
        "areaSpread": round(max(areas) / max(1, min(areas)), 1) if areas else 0,
        "interdigitation": interdigitation(rooms),
        "silhouetteCollisions": silhouettes(rooms),
        "edgeKinds": dict(kinds.most_common()),
    }

    run = progression(game)
    if run and run["items"]:
        widths = [run["frontier"][state] for state in sorted(run["states"], key=len)]
        spans = gate_spans(game, run)
        locks = [span["locks"] for span in spans.values()]
        report |= {
            "frontierMedian": median(widths),
            "frontierMin": min(widths),
            "frontierCurve": widths,
            "orderings": orderings(run) if run["complete"] else None,
            "softlocked": None if run["complete"] else sorted(run["abilities"] - run["full"]),
            "verbScore": round(sum(locks) / len(locks), 1) if locks else None,
            "keycards": sum(1 for count in locks if count == 1),
            "gateSpans": spans,
        }

    notes = game.get("notes") or []
    if notes:
        latencies = [note["minutes"] for note in notes if "minutes" in note]
        report |= {
            "promissoryNotes": len(notes),
            "noteLatencyMedian": median(latencies),
            "noteLatencySpread": [min(latencies), max(latencies)] if latencies else None,
        }
    return report


COLUMNS = [
    ("rooms", "rooms", "{}"), ("cycleRank", "loops", "{}"), ("cycleRankPer100", "loops/100rm", "{}"),
    ("shortLoopShare", "short loop %", "{}"), ("branchingShare", "branch %", "{}"),
    ("deadEndShare", "dead end %", "{}"), ("saveCentrality", "save centr", "{}"), ("saveAnchorCentrality", "save anchor", "{}"),
    ("interdigitation", "interdig %", "{}"), ("silhouetteCollisions", "same shape %", "{}"),
    ("frontierMedian", "frontier", "{}"), ("verbScore", "locks/verb", "{}"),
    ("orderings", "orderings", "{}"), ("noteLatencyMedian", "note mins", "{}"),
]


def table(reports):
    header = ["game"] + [label for _, label, _ in COLUMNS]
    rows = [[report["game"]] + [
        "-" if report.get(key) is None else fmt.format(report[key]) for key, _, fmt in COLUMNS
    ] for report in reports]
    widths = [max(len(row[i]) for row in [header] + rows) for i in range(len(header))]
    lines = ["  ".join(cell.ljust(widths[i]) for i, cell in enumerate(header)),
             "  ".join("-" * width for width in widths)]
    lines += ["  ".join(cell.ljust(widths[i]) for i, cell in enumerate(row)) for row in rows]
    return "\n".join(lines)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--worlds", nargs="*", default=["Myth"], help="our worlds to include")
    parser.add_argument("--output", type=Path, default=ROOT / "Platforms/Web/dashboard/mapstudy.json")
    args = parser.parse_args()

    reports = [analyse(load_map(path)) for path in sorted(STUDY.glob("*.mvmap.json"))]
    reports += [analyse(our_map(world)) for world in args.worlds]

    print(table(reports))
    for report in reports:
        if report.get("softlocked"):
            print(f"\n{report['game']}: unreachable abilities {', '.join(report['softlocked'])}")
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps({"games": reports}, indent=1) + "\n")
    print(f"\n{len(reports)} maps -> {args.output}")


if __name__ == "__main__":
    main()
