import json, os

OUT = "/Users/kev/src/FosterGames/Myth/Assets/Rooms/Myth"
W = 40
FG_CHARS = set("0TYGSPJRZIXEF12%$Mgirw")
BG_CHARS = set("0#-()muv")

def grid(h):
    return [["0"] * W for _ in range(h)]

def fill(g, x0, y0, x1, y1, c):
    for y in range(y0, y1 + 1):
        for x in range(x0, x1 + 1):
            g[y][x] = c

def put(g, c, *cells):
    for (x, y) in cells:
        g[y][x] = c

def rows(g):
    return ["".join(r) for r in g]

def validate(name, fg, bg, h, fungal):
    assert len(fg) == h and len(bg) == h, name
    for r in rows(fg):
        assert len(r) == W and set(r) <= FG_CHARS, (name, r)
    for r in rows(bg):
        assert len(r) == W and set(r) <= BG_CHARS, (name, r)
    for (x, y) in fungal:
        assert 0 <= x < W and 0 <= y < h, (name, x, y)
        assert fg[y][x] in "TGPYSJRZ", (name, "fungal not on solid", x, y, fg[y][x])

def show(name, fg, bg):
    print(f"=== {name} : tiles ===")
    for i, r in enumerate(rows(fg)):
        print(f"{i:2d} {r}")
    print(f"--- {name} : background ---")
    for i, r in enumerate(rows(bg)):
        print(f"{i:2d} {r}")

def jval(v):
    if isinstance(v, bool):
        return "true" if v else "false"
    if isinstance(v, list):
        return "[" + ", ".join(jval(x) for x in v) + "]"
    return json.dumps(v)

def write(name, data):
    lines = ["{"]
    keys = list(data.keys())
    for i, k in enumerate(keys):
        v = data[k]
        comma = "," if i < len(keys) - 1 else ""
        if k in ("tiles", "backgroundTiles"):
            lines.append(f'  "{k}": [')
            for j, row in enumerate(v):
                lines.append(f'    "{row}"' + ("," if j < len(v) - 1 else ""))
            lines.append(f"  ]{comma}")
        elif k == "fungal":
            pairs = ",".join(f"[{x}, {y}]" for x, y in v)
            lines.append(f'  "{k}": [{pairs}')
            lines.append(f"  ]{comma}")
        elif k == "actors":
            lines.append(f'  "{k}": [')
            for j, actor in enumerate(v):
                lines.append("    {")
                akeys = list(actor.keys())
                for m, ak in enumerate(akeys):
                    acomma = "," if m < len(akeys) - 1 else ""
                    lines.append(f'      "{ak}": {jval(actor[ak])}{acomma}')
                lines.append("    }" + ("," if j < len(v) - 1 else ""))
            lines.append(f"  ]{comma}")
        else:
            lines.append(f'  "{k}": {jval(v)}{comma}')
    lines.append("}")
    path = os.path.join(OUT, name)
    with open(path, "w") as f:
        f.write("\n".join(lines))
    # round-trip check
    with open(path) as f:
        assert json.load(f) == data, name
    print("wrote", path)

# ---------------------------------------------------------------- learn_jump
H1 = 23
fg = grid(H1)
bg = grid(H1)

fill(fg, 0, 0, 0, 22, "T")                    # left wall (dead end for now)
fill(fg, 1, 17, 10, 17, "G")                  # starting meadow, floor top 17
fill(fg, 1, 18, 10, 22, "P")
put(fg, "S", (5, 16), (8, 16)); put(fg, "G", (6, 16), (7, 16))   # first hop: a mound
fill(fg, 11, 15, 14, 15, "G"); fill(fg, 11, 16, 14, 22, "P")     # block 1 (jump 2 up)
fill(fg, 15, 16, 17, 18, "w"); fill(fg, 15, 19, 17, 22, "P")     # pool gap, 3 wide
fill(fg, 18, 15, 21, 15, "G"); fill(fg, 18, 16, 21, 22, "P")     # block 2
fill(fg, 22, 16, 25, 18, "w"); fill(fg, 22, 19, 25, 22, "P")     # pool gap, 4 wide
fill(fg, 26, 15, 29, 15, "G"); fill(fg, 26, 16, 29, 22, "P")     # block 3
fill(fg, 30, 16, 33, 18, "w"); fill(fg, 30, 19, 33, 22, "P")     # pool gap, 4 wide
fill(fg, 34, 13, 39, 13, "G"); fill(fg, 34, 14, 39, 22, "P")     # exit shelf, floor top 13

put(fg, "g", (2, 16), (3, 16), (12, 14), (19, 14), (20, 14), (36, 12), (37, 12))
put(fg, "r", (28, 14), (29, 14))

# ruined tower behind the meadow
put(bg, "(", (2, 9)); put(bg, ")", (5, 9)); put(bg, "#", (3, 9), (4, 9))
fill(bg, 2, 10, 5, 16, "#")
fill(bg, 6, 10, 6, 16, "-")
# myth panel with runes behind block 1
fill(bg, 12, 8, 13, 12, "m")
fill(bg, 12, 13, 13, 14, "u")
# tall tower behind block 3
put(bg, "(", (27, 5)); put(bg, ")", (30, 5)); put(bg, "#", (28, 5), (29, 5))
fill(bg, 27, 6, 30, 14, "#")
# vines by the exit shelf
fill(bg, 35, 9, 35, 12, "v")
fill(bg, 37, 8, 37, 11, "v")
fill(bg, 38, 10, 38, 12, "v")

fungal1 = [[8, 17], [9, 17], [10, 17], [11, 15], [14, 15], [15, 19], [16, 19],
           [21, 15], [22, 19], [23, 19], [26, 15], [29, 15], [33, 19], [34, 13], [35, 13]]

room1 = {
    "position": [-120, 0],
    "size": [W, H1],
    "ambientColor": "979797",
    "lightNoise": 0.3,
    "tiles": rows(fg),
    "backgroundTiles": rows(bg),
    "fungal": fungal1,
    "actors": [
        {"Id": "a1f30c88d21e4b01", "Type": "PlayerStart", "Position": [24, 136]},
        {"Id": "a1f30c88d21e4b02", "Type": "Light", "Position": [40, 116],
         "Intensity": 0.9, "Radius": 70.0, "FlickerFrequency": 0.4},
        {"Id": "a1f30c88d21e4b03", "Type": "Jumpthru", "Position": [144, 88], "Size": [32, 8]},
        {"Id": "a1f30c88d21e4b04", "From": "BlueMemoryLight", "Position": [190, 98],
         "Radius": 95.0, "Intensity": 0.75},
        {"Id": "a1f30c88d21e4b05", "Type": "Light", "Position": [296, 92], "Radius": 60.0},
        {"Id": "a1f30c88d21e4b06", "Type": "Goal", "Position": [304, 104], "Name": "learn_jump"},
    ],
}
validate("learn_jump", fg, bg, H1, fungal1)
show("learn_jump", fg, bg)

# ---------------------------------------------------------------- learn_roll
H2 = 23
fg = grid(H2)
bg = grid(H2)

fill(fg, 0, 13, 10, 13, "G")                  # entrance floor, top 13 (matches learn_jump exit)
fill(fg, 0, 14, 10, 22, "P")
fill(fg, 6, 4, 10, 11, "T")                   # hanging shelf; slot row 12 is roll/crawl only
put(fg, "r", (7, 3), (9, 3))
fill(fg, 11, 13, 15, 13, "G"); fill(fg, 11, 14, 15, 22, "P")   # pocket after the slot
put(fg, "g", (13, 12), (14, 12))
# stepped downhill, drop 1 per 2 columns: 13 -> 18
tops = [(16, 14), (17, 14), (18, 15), (19, 15), (20, 16), (21, 16),
        (22, 17), (23, 17), (24, 18), (25, 18)]
for i, (x, t) in enumerate(tops):
    fg[t][x] = "S" if i % 2 == 0 else "G"
    fill(fg, x, t + 1, x, 22, "P")
fill(fg, 26, 18, 27, 18, "G"); fill(fg, 26, 19, 27, 22, "P")   # launch lip
fill(fg, 28, 19, 34, 21, "w"); fill(fg, 28, 22, 34, 22, "P")   # the long gap (roll-jump it)
fill(fg, 35, 18, 39, 18, "G"); fill(fg, 35, 19, 39, 22, "P")   # landing
fill(fg, 37, 12, 39, 16, "T")                                  # exit overhang; slot row 17
put(fg, "g", (35, 17), (36, 17))

fill(bg, 6, 12, 10, 12, "#")                  # darkness inside the slot
put(bg, "(", (11, 6)); put(bg, ")", (14, 6)); put(bg, "#", (12, 6), (13, 6))
fill(bg, 11, 7, 14, 12, "#")                  # tower behind the pocket
put(bg, "(", (18, 9)); put(bg, ")", (22, 9)); put(bg, "#", (19, 9), (20, 9), (21, 9))
fill(bg, 18, 10, 22, 13, "#")                 # arch behind the slope
fill(bg, 29, 13, 29, 15, "v")                 # vines over the water
fill(bg, 31, 13, 31, 16, "v")
fill(bg, 33, 13, 33, 14, "v")
fill(bg, 37, 8, 39, 11, "m")                  # myth panel above the exit overhang
fill(bg, 38, 10, 39, 11, "u")

fungal2 = [[6, 11], [7, 11], [9, 11], [7, 13], [8, 13], [25, 18], [26, 18], [27, 18],
           [30, 22], [31, 22], [32, 22], [35, 18], [36, 18], [37, 16], [38, 16]]

room2 = {
    "position": [-80, 0],
    "size": [W, H2],
    "ambientColor": "8F8F8F",
    "lightNoise": 0.3,
    "reverb": 0.15,
    "tiles": rows(fg),
    "backgroundTiles": rows(bg),
    "fungal": fungal2,
    "actors": [
        {"Id": "b2e41d99e32f5c01", "Type": "PlayerStart", "Position": [16, 104]},
        {"Id": "b2e41d99e32f5c02", "Type": "Light", "Position": [44, 80],
         "Intensity": 0.85, "Radius": 65.0, "FlickerFrequency": 0.7},
        {"Id": "b2e41d99e32f5c03", "Type": "Light", "Position": [104, 72], "Radius": 80.0},
        {"Id": "b2e41d99e32f5c04", "From": "BlueMemoryLight", "Position": [252, 124],
         "Radius": 100.0, "Intensity": 0.7},
        {"Id": "b2e41d99e32f5c05", "Type": "Goal", "Position": [312, 144], "Name": "learn_roll"},
    ],
}
validate("learn_roll", fg, bg, H2, fungal2)
show("learn_roll", fg, bg)

# -------------------------------------------------------------- learn_burrow
H3 = 45
fg = grid(H3)
bg = grid(H3)

fill(fg, 0, 0, 39, 1, "T")                    # stone cap
for x in (6, 7, 14, 15, 22, 23, 30, 31):      # stalactite fringe
    fg[2][x] = "T"
fill(fg, 0, 2, 0, 16, "T")                    # left wall above the entrance slot (row 17 open)
fill(fg, 0, 18, 8, 18, "G")                   # entrance ledge, floor top 18
fill(fg, 0, 19, 8, 44, "P")
fill(fg, 5, 27, 8, 31, "0")                   # alcove carved into the ledge block
fill(fg, 5, 32, 8, 32, "G")
fill(fg, 9, 39, 39, 44, "P")                  # well bottom slab
fill(fg, 9, 39, 28, 39, "G")
fill(fg, 10, 39, 13, 39, "0")                 # sunken pool under the entrance drop
fill(fg, 10, 40, 13, 42, "w")
put(fg, "S", (15, 38), (18, 38)); put(fg, "G", (16, 38), (17, 38))  # practice mound
fill(fg, 20, 32, 24, 32, "G"); fill(fg, 20, 33, 24, 33, "R")        # floating island (pop target)
fill(fg, 29, 31, 39, 38, "R"); fill(fg, 29, 31, 32, 31, "G")        # terrace 1: climb face col 29 (8 tall)
fill(fg, 33, 22, 39, 30, "R"); fill(fg, 33, 22, 36, 22, "G")        # terrace 2: climb face col 33 (9 tall)
fill(fg, 37, 14, 39, 21, "R"); fill(fg, 37, 14, 39, 14, "G")        # terrace 3 / exit floor, face col 37 (8 tall)
fill(fg, 39, 2, 39, 9, "T")                   # frame above the exit opening (rows 10-13 open)

put(fg, "g", (3, 17), (6, 17), (26, 38), (27, 38), (22, 31), (30, 30), (35, 21), (38, 13))
put(fg, "r", (9, 38), (28, 38))

fill(bg, 10, 31, 27, 38, "#")                 # dark cavern backing at the bottom
fill(bg, 15, 20, 16, 38, "m")                 # buried myth pillar
put(bg, "u", (15, 25), (16, 26), (15, 34), (16, 35))
fill(bg, 25, 24, 26, 38, "m")                 # second pillar
put(bg, "u", (25, 28), (26, 29), (25, 36))
fill(bg, 5, 27, 8, 31, "m")                   # alcove backing + runes
put(bg, "u", (6, 29), (7, 29), (6, 30), (7, 30))
fill(bg, 30, 24, 32, 26, "u")                 # runes marking the terrace-2 climb
put(bg, "(", (12, 10)); put(bg, ")", (16, 10)); put(bg, "#", (13, 10), (14, 10), (15, 10))
fill(bg, 12, 11, 16, 20, "#")                 # ruin tower in the upper cavern
fill(bg, 17, 11, 17, 20, "-")
for x, y1 in ((11, 5), (18, 4), (21, 3), (26, 6), (34, 3)):   # hanging vines from the cap
    fill(bg, x, 2, x, y1, "v")

fungal3 = [[14, 39], [19, 39], [20, 39], [21, 39], [26, 39], [27, 39], [28, 39],
           [8, 34], [8, 35], [8, 36], [8, 37], [8, 38],
           [5, 26], [6, 26], [7, 26], [5, 32], [6, 32],
           [29, 32], [29, 33], [29, 34], [29, 36], [30, 31], [31, 31],
           [21, 32], [23, 32], [22, 33],
           [33, 24], [33, 25], [33, 27], [33, 29], [34, 22], [35, 22],
           [37, 16], [37, 17], [37, 19],
           [15, 38], [16, 38], [4, 18], [5, 18], [7, 18]]

room3 = {
    "position": [-40, 0],
    "size": [W, H3],
    "ambientColor": "606060",
    "lightNoise": 0.3,
    "reverb": 0.45,
    "tiles": rows(fg),
    "backgroundTiles": rows(bg),
    "fungal": fungal3,
    "actors": [
        {"Id": "c3d52eaaf4406d01", "Type": "PlayerStart", "Position": [116, 312]},
        {"Id": "c3d52eaaf4406d02", "Type": "Light", "Position": [40, 120],
         "Intensity": 0.85, "Radius": 60.0, "FlickerFrequency": 0.8},
        {"Id": "c3d52eaaf4406d03", "Type": "Light", "Position": [92, 300],
         "Intensity": 0.85, "Radius": 70.0, "FlickerFrequency": 1.2},
        {"Id": "c3d52eaaf4406d04", "From": "BlueMemoryLight", "Position": [52, 244],
         "Radius": 90.0, "Intensity": 0.75},
        {"Id": "c3d52eaaf4406d05", "Type": "Light", "Position": [176, 244], "Radius": 65.0},
        {"Id": "c3d52eaaf4406d06", "From": "BlueMemoryLight", "Position": [230, 120],
         "Radius": 120.0, "Intensity": 0.6},
        {"Id": "c3d52eaaf4406d07", "Type": "Light", "Position": [308, 100], "Radius": 55.0},
        {"Id": "c3d52eaaf4406d08", "Type": "ParticleEmitter", "Position": [180, 60],
         "EffectName": "snow"},
        {"Id": "c3d52eaaf4406d09", "Type": "Goal", "Position": [310, 112], "Name": "learn_burrow"},
    ],
}
validate("learn_burrow", fg, bg, H3, fungal3)
show("learn_burrow", fg, bg)

# ------------------------------------------------------------ tileset_gallery
# Reference room: one tower per registered dual/solid tileset, lit to show the
# generated normal maps. Not part of any gameplay chain.
H4 = 23
fg = grid(H4)
bg = grid(H4)

fill(fg, 0, 0, 0, 22, "T")                    # side walls
fill(fg, 39, 0, 39, 22, "T")
fill(fg, 0, 18, 39, 22, "T")                  # floor
towers = "1T%MGPRIE"                          # castle stone crosshatch metal grass dirt ruin ice ember
for i, ch in enumerate(towers):
    x = 2 + i * 4
    fill(fg, x, 14, x + 2, 17, ch)

room4 = {
    "position": [-160, 0],
    "size": [W, H4],
    "ambientColor": "909090",
    "lightNoise": 0.2,
    "tiles": rows(fg),
    "backgroundTiles": rows(bg),
    "fungal": [],
    "actors": [
        {"Id": "d4e63fbb05517e01", "Type": "PlayerStart", "Position": [120, 112]},
        {"Id": "d4e63fbb05517e02", "Type": "Light", "Position": [60, 96],
         "Intensity": 0.9, "Radius": 90.0},
        {"Id": "d4e63fbb05517e03", "Type": "Light", "Position": [156, 96],
         "Intensity": 0.9, "Radius": 90.0},
        {"Id": "d4e63fbb05517e04", "Type": "Light", "Position": [252, 96],
         "Intensity": 0.9, "Radius": 90.0},
    ],
}
validate("tileset_gallery", fg, bg, H4, [])
show("tileset_gallery", fg, bg)

write("tileset_gallery.json", room4)

write("learn_jump.json", room1)
write("learn_roll.json", room2)
write("learn_burrow.json", room3)
