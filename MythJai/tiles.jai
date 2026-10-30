// The room's tile art, from the visual pack, chosen the way Myth does it so the same room
// looks the same:
//  - a plain tileset (Grid) picks a tile per cell with Foster's Rng seeded by the cell;
//  - a kit (DualGrid) draws on the dual grid, one tile per cell corner: the shape comes from
//    which of the four cells around the corner are solid, the variant from a hash of the world
//    tile, and where two kits meet each draws only its own quadrants.
// Also Myth's tile field, which picks the lighting dither per tile: every cell gets a variant
// from a hash of its world tile, and the foreground tiles stamp their own over it, family and all.
// And the edges that cast shadows: the room's bounds and the foreground kits' occluders.
// Not ported yet: slopes, fill patterns spanning tiles.

Tile_Draw :: struct { tile: Subtex; normal: Subtex; pos: Vector2; clip: RectInt; field: Color; }

tile_draws: struct {
    background: [..] Tile_Draw;
    foreground: [..] Tile_Draw;
};
occluders: [..] Segment;  // room pixels

build_tile_draws :: () {
    tile_draws.background.count = 0;
    tile_draws.foreground.count = 0;
    occluders.count = 0;
    size := Vector2.{xx (room.size.x * TILE_SIZE), xx (room.size.y * TILE_SIZE)};
    corners := Vector2.[.{}, .{size.x, 0}, size, .{0, size.y}];
    for corners array_add(*occluders, .{it, corners[(it_index + 1) % 4]});
    build_layer(room.background, *tile_draws.background, foreground = false);
    build_layer(room.tiles, *tile_draws.foreground, foreground = true);
    merge_occluders();
}

// DualGrid.MergeAxisAlignedShadowSegments: tile edges that line up become one run, so a light
// tests a handful of long edges instead of every tile's.
merge_occluders :: () {
    Run :: struct { horizontal: bool; axis, start, end: float; };
    runs: [..] Run;
    runs.allocator = temp;
    kept := 0;
    for occluders {
        if abs(it.a.y - it.b.y) < 0.01 array_add(*runs, .{true, it.a.y, min(it.a.x, it.b.x), max(it.a.x, it.b.x)});
        else if abs(it.a.x - it.b.x) < 0.01 array_add(*runs, .{false, it.a.x, min(it.a.y, it.b.y), max(it.a.y, it.b.y)});
        else { occluders[kept] = it; kept += 1; }
    }
    occluders.count = kept;
    quick_sort(runs, (a: Run, b: Run) -> s64 {
        if a.horizontal != b.horizontal return ifx a.horizontal then -1 else 1;
        if abs(a.axis - b.axis) >= 0.01 return ifx a.axis < b.axis then -1 else 1;
        return ifx a.start < b.start then -1 else ifx a.start > b.start then 1 else 0;
    });
    add_run :: (r: Run) {
        if r.end <= r.start return;
        if r.horizontal array_add(*occluders, .{.{r.start, r.axis}, .{r.end, r.axis}});
        else array_add(*occluders, .{.{r.axis, r.start}, .{r.axis, r.end}});
    }
    if !runs.count return;
    current := runs[0];
    for 1..runs.count-1 {
        next := runs[it];
        if next.horizontal == current.horizontal && abs(next.axis - current.axis) < 0.01 && next.start <= current.end + 0.001 {
            current.end = max(current.end, next.end);
            continue;
        }
        add_run(current);
        current = next;
    }
    add_run(current);
}

draw_tiles :: (draws: [] Tile_Draw, normals := false) {
    for draws {
        tile := ifx normals then it.normal else it.tile;
        if tile.source.w draw_subtex(*pack, tile, it.pos, it.clip);
    }
}

draw_tile_field :: () {
    for y: 0..room.size.y-1 for x: 0..room.size.x-1
        rect(.{.{xx (x * TILE_SIZE), xx (y * TILE_SIZE)}, .{xx TILE_SIZE, xx TILE_SIZE}}, tile_field_color(#char "0", room.position + Point2.{xx x, xx y}));
    for tile_draws.foreground if it.tile.source.w draw_subtex(*pack, it.tile, it.pos, it.clip, it.field, wash = true);
}

#scope_file

// Game.TileFieldColor: r = variant, g = dither family << 5 (the low bits are the anchor, 0 for rooms).
tile_field_color :: (c: u8, world: Point2) -> Color {
    hash: u32 = 2166136261;
    hash = (hash ^ cast,no_check(u32) world.x) * 16777619;
    hash = (hash ^ cast,no_check(u32) world.y) * 16777619;
    hash = (hash ^ c) * 16777619;
    family: u8 = 4;
    if c == {
        case #char "0"; family = 0;
        case #char "1"; #through; case #char "2"; #through; case #char "x"; family = 1;
        case #char "G"; #through; case #char "S"; #through; case #char "X"; #through; case #char "g"; family = 2;
        case #char "w"; family = 3;
    }
    return .{xx (hash & 255), family << 5, 0, 255};
}

// DualGrid.MaskToTile: corner mask (bit per quadrant) -> tile in the kit's 4x4 layout.
MASK_TO_TILE :: Point2.[
    .{0, 3}, .{3, 3}, .{0, 0}, .{3, 2}, .{0, 2}, .{1, 2}, .{2, 3}, .{3, 1},
    .{1, 3}, .{0, 1}, .{3, 0}, .{2, 0}, .{1, 0}, .{2, 2}, .{1, 1}, .{2, 1},
];
TOP_LEFT :: 1; BOTTOM_LEFT :: 2; TOP_RIGHT :: 4; BOTTOM_RIGHT :: 8; ALL :: 15;
QUADRANTS :: struct { bit: s32; clip: RectInt; }.[
    .{TOP_LEFT,     .{0, 0, 4, 4}},
    .{BOTTOM_LEFT,  .{0, 4, 4, 4}},
    .{TOP_RIGHT,    .{4, 0, 4, 4}},
    .{BOTTOM_RIGHT, .{4, 4, 4, 4}},
];

build_layer :: (layer: [] u8, draws: *[..] Tile_Draw, foreground: bool) {
    kits: [128] *Packed_Tileset;
    plain: [128] *Packed_Tileset;
    for c: 1..127 if room.legend[c] {
        name := tileset_name(room.legend[c]);
        kits[c] = find_tileset(*pack, tprint("%-dual", name));
        if !kits[c] plain[c] = find_tileset(*pack, name);
        if !kits[c] && !plain[c] log_error("no tileset for %", room.legend[c]);
    }

    for y: 0..room.size.y-1 for x: 0..room.size.x-1 {
        c := layer[y * room.size.x + x];
        t := plain[c];
        if !t || !t.tiles continue;
        rng := cast(u64) (x + y * room.size.x);
        pos := Vector2.{xx (x * TILE_SIZE), xx (y * TILE_SIZE)};
        index := splitmix(*rng) % cast(u32) t.tiles.count;
        field := tile_field_color(c, room.position + Point2.{xx x, xx y});
        array_add(draws, .{t.tiles[index], ifx index < t.normal_tiles.count then t.normal_tiles[index] else .{}, pos, .{}, field});
    }

    for c: 1..127 if kits[c] for vy: 0..room.size.y for vx: 0..room.size.x {
        material, join: s32;
        for QUADRANTS {
            x, y := vx - 1 + it.clip.x / 4, vy - 1 + it.clip.y / 4;
            here := cell(layer, x, y);
            if here == c material |= it.bit;
            // The shape joins the solid cells (foreground) or the kit cells (background) around the corner.
            if ifx foreground then here != #char "0" else kits[here] != null join |= it.bit;
        }
        if !material || !join continue;
        world := Point2.{room.position.x + xx vx, room.position.y + xx vy};
        seed := tile_variant_seed(world, c);
        tile, ok := variant_tile(kits[c].tiles, kits[c].variants, kits[c].rows, MASK_TO_TILE[join], seed);
        if !ok continue;
        normal := variant_tile(kits[c].normal_tiles, kits[c].normal_variants, kits[c].rows, MASK_TO_TILE[join], seed);
        pos := Vector2.{(vx - 0.5) * TILE_SIZE, (vy - 0.5) * TILE_SIZE};
        field := tile_field_color(xx c, world);
        if foreground for variant_occluders(kits[c], MASK_TO_TILE[join], seed) {
            mid := (it.a + it.b) * 0.5;
            quadrant := ifx mid.x < TILE_SIZE / 2 then ifx mid.y < TILE_SIZE / 2 then TOP_LEFT else BOTTOM_LEFT
                        else ifx mid.y < TILE_SIZE / 2 then TOP_RIGHT else BOTTOM_RIGHT;
            if material & quadrant array_add(*occluders, .{pos + it.a, pos + it.b});
        }
        if material == ALL array_add(draws, .{tile, normal, pos, .{}, field});
        else for QUADRANTS if material & it.bit array_add(draws, .{tile, normal, pos, it.clip, field});
    }
}

// Cells off the room clamp to its edge, so terrain runs on past it.
cell :: (layer: [] u8, x: int, y: int) -> u8 {
    return layer[clamp(y, 0, room.size.y - 1) * room.size.x + clamp(x, 0, room.size.x - 1)];
}

// Tileset.GetVariantTile with fill patterns one tile wide.
variant_tile :: (tiles: [] Subtex, variants: [] [] Subtex, rows: s32, at: Point2, seed: s32) -> Subtex, bool {
    if !variants {
        index := at.x * rows + at.y;
        if index >= tiles.count return .{}, false;
        return tiles[index], true;
    }
    variant := variants[(seed & 0x7fff_ffff) % variants.count];
    index := at.x * rows + at.y;
    if index >= variant.count return .{}, false;
    return variant[index], true;
}

// Tileset.GetVariantOccluders, picked like variant_tile.
variant_occluders :: (t: *Packed_Tileset, at: Point2, seed: s32) -> [] Segment {
    tiles := ifx t.occluder_variants then t.occluder_variants[(seed & 0x7fff_ffff) % t.occluder_variants.count] else t.occluders;
    index := at.x * t.rows + at.y;
    return ifx index < tiles.count then tiles[index] else .[];
}

// Kit's Hash32.TileVariantSeed.
tile_variant_seed :: (world: Point2, c: int) -> s32 {
    hash := cast(u32) world.x * 0x8da6b343;
    hash ^= cast(u32) world.y * 0xd8163841;
    hash ^= cast(u32) c * 0xcb1ab31f;
    hash ^= hash >> 16;
    hash *= 0x7feb352d;
    hash ^= hash >> 15;
    hash *= 0x846ca68b;
    return cast,no_check(s32) (hash ^ (hash >> 16));
}

// Foster's Rng.U32 (SplitMix64).
splitmix :: (state: *u64) -> u32 {
    state.* += 0x9e3779b97f4a7c15;
    n := state.*;
    n = (n ^ (n >> 30)) * 0xbf58476d1ce4e5b;
    n = (n ^ (n >> 27)) * 0x94d049bb133111eb;
    return cast,no_check(u32) (n ^ (n >> 31));
}

// A legend's tile type name to its tileset: "Myth Back" -> "myth-back".
tileset_name :: (entry: string) -> string {
    name := to_lower_copy(entry,, temp);
    for 0..name.count-1 if name[it] == #char " " name[it] = #char "-";
    return name;
}

#import "Basic";
#import "Math";
#import "String";
#import "Kit";
