// The room's tile art, from the visual pack, chosen the way Myth does it so the same room
// looks the same:
//  - a plain tileset (Grid) picks a tile per cell with Foster's Rng seeded by the cell;
//  - a kit (DualGrid) draws on the dual grid, one tile per cell corner: the shape comes from
//    which of the four cells around the corner are solid, the variant from a hash of the world
//    tile, and where two kits meet each draws only its own quadrants.
// Not ported yet: slopes, normals, occluders (for shadows), fill patterns spanning tiles.

Tile_Draw :: struct { tile: Subtex; pos: Vector2; clip: RectInt; }

tile_draws: struct {
    background: [..] Tile_Draw;
    foreground: [..] Tile_Draw;
};

build_tile_draws :: () {
    tile_draws.background.count = 0;
    tile_draws.foreground.count = 0;
    build_layer(room.background, *tile_draws.background, foreground = false);
    build_layer(room.tiles, *tile_draws.foreground, foreground = true);
}

draw_tiles :: (draws: [] Tile_Draw) {
    for draws draw_subtex(*pack, it.tile, it.pos, it.clip);
}

#scope_file

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
    array_add(draws, .{t.tiles[splitmix(*rng) % cast(u32) t.tiles.count], pos, .{}});
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
        tile, ok := variant_tile(kits[c], MASK_TO_TILE[join], tile_variant_seed(world, c));
        if !ok continue;
        pos := Vector2.{(vx - 0.5) * TILE_SIZE, (vy - 0.5) * TILE_SIZE};
        if material == ALL array_add(draws, .{tile, pos, .{}});
        else for QUADRANTS if material & it.bit array_add(draws, .{tile, pos, it.clip});
    }
}

// Cells off the room clamp to its edge, so terrain runs on past it.
cell :: (layer: [] u8, x: int, y: int) -> u8 {
    return layer[clamp(y, 0, room.size.y - 1) * room.size.x + clamp(x, 0, room.size.x - 1)];
}

// Tileset.GetVariantTile with fill patterns one tile wide.
variant_tile :: (t: *Packed_Tileset, at: Point2, seed: s32) -> Subtex, bool {
    if !t.variants {
        index := at.x * t.rows + at.y;
        if index >= t.tiles.count return .{}, false;
        return t.tiles[index], true;
    }
    variant := t.variants[(seed & 0x7fff_ffff) % t.variants.count];
    index := at.x * t.rows + at.y;
    if index >= variant.count return .{}, false;
    return variant[index], true;
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
