// Myth's visual asset pack (Myth/Assets/Packed/Visuals): the C# baker's output, read rather
// than re-baked here. It holds the generated tile kits; rebuild it with
// `dotnet run -- --build-asset-pack` in Myth/ after changing tilesets.
// Format: Myth/Source/Assets/Assets.cs, WriteVisualAssetPackManifest (a .NET BinaryWriter stream).

// A trimmed image in the atlas: `source` is where its pixels are, `frame` places them in the
// untrimmed image (x/y are minus the trim), like Foster's Subtexture.
Subtex :: struct { source: RectInt; frame: RectInt; }
Segment :: struct { a, b: Vector2; }

Packed_Tileset :: struct {
    name: string;
    columns, rows: s32;
    fill_tiles: s32;
    tiles: [] Subtex;
    variants: [] [] Subtex;
    normal_tiles: [] Subtex;      // tiles' baked normals, same layout
    normal_variants: [] [] Subtex;
    occluders: [] [] Segment;     // per tile, the edges that cast shadows, in tile pixels
    occluder_variants: [] [] [] Segment;
}

Pack :: struct {
    atlas: Texture;
    lighting_noise: Texture;
    tilesets: [] Packed_Tileset;
}

PACK_DIR :: "Packed/Visuals";
PACK_VERSION :: 16;

load_pack :: () -> Pack, bool {
    file, ok := read_entire_file(tprint("%/%/manifest.bin", ASSETS, PACK_DIR), log_errors = false);
    if !ok return .{}, false;
    r := Reader.{data = xx file};
    if read(*r, u32) != 0x5356494D || read(*r, s32) != PACK_VERSION { log_error("visual pack is stale; rebuild it with --build-asset-pack"); return .{}, false; }

    pack: Pack;
    atlas_name := read_string(*r);
    noise_name := ifx read(*r, bool) then read_string(*r) else "";

    for 1..read(*r, u16) { read_string(*r); skip_subtex(*r); }  // named subtextures
    for 1..read(*r, u16) {  // sprites: we load these from their .ase instead
        read_string(*r);
        r.pos += 8;
        for 1..read(*r, u16) { for 1..4 skip_subtex(*r); r.pos += 4; read_string(*r); r.pos += 8 + 4 + 4 + 4; }
        for 1..read(*r, u16) { read_string(*r); r.pos += 12; }
        for 1..read(*r, u16) { read_string(*r); r.pos += 16; }
        for 1..read(*r, u16) { read_string(*r); poses := read(*r, u16); r.pos += poses * 17; }
    }

    tilesets: [..] Packed_Tileset;
    for 1..read(*r, u16) {
        t: Packed_Tileset;
        t.name = read_string(*r);
        t.columns = read(*r, s32);
        t.rows = read(*r, s32);
        t.fill_tiles = read(*r, s32);
        t.tiles = read_subtexes(*r);
        variants := NewArray(read(*r, u16), [] Subtex);
        for * variants it.* = read_subtexes(*r);
        t.variants = variants;
        t.normal_tiles = read_subtexes(*r);
        normal_variants := NewArray(read(*r, u16), [] Subtex);
        for * normal_variants it.* = read_subtexes(*r);
        t.normal_variants = normal_variants;
        t.occluders = read_occluders(*r);
        occluder_variants := NewArray(read(*r, u16), [] [] Segment);
        for * occluder_variants it.* = read_occluders(*r);
        t.occluder_variants = occluder_variants;
        array_add(*tilesets, t);
    }
    pack.tilesets = tilesets;
    if r.bad { log_error("couldn't read the visual pack"); return .{}, false; }

    pack.atlas, ok = load_qoi(atlas_name);
    if !ok return .{}, false;
    if noise_name pack.lighting_noise = load_qoi(noise_name);
    return pack, true;
}

load_qoi :: (name: string) -> Texture, bool {
    file, ok := read_entire_file(tprint("%/%/%", ASSETS, PACK_DIR, name), log_errors = false);
    if !ok return .{}, false;
    defer free(file);
    pixels, w, h, decoded := decode_qoi(xx file);
    if !decoded { log_error("couldn't decode %", name); return .{}, false; }
    defer array_free(pixels);
    premultiply(pixels);
    return make_texture(pixels.data, w, h), true;
}

find_tileset :: (pack: *Pack, name: string) -> *Packed_Tileset {
    for * pack.tilesets if it.name == name return it;
    return null;
}

// Draws `t` with its untrimmed top-left at `pos`, showing only the part inside `clip`
// (untrimmed-image space) when one is given.
draw_subtex :: (pack: *Pack, t: Subtex, pos: Vector2, clip := RectInt.{}, color := WHITE, wash := false) {
    // Where the source pixels sit within the untrimmed image.
    x0, y0 := -t.frame.x, -t.frame.y;
    x1, y1 := x0 + t.source.w, y0 + t.source.h;
    if clip.w { x0, y0, x1, y1 = max(x0, clip.x), max(y0, clip.y), min(x1, clip.x + clip.w), min(y1, clip.y + clip.h); }
    if x1 <= x0 || y1 <= y0 return;
    source := Rect.{.{xx (t.source.x + x0 + t.frame.x), xx (t.source.y + y0 + t.frame.y)}, .{xx (x1 - x0), xx (y1 - y0)}};
    image(pack.atlas, source, pos + Vector2.{xx x0, xx y0}, color, wash);
}

#scope_file

Reader :: struct { data: [] u8; pos: s64; bad: bool; }

read :: (r: *Reader, $T: Type) -> T {
    if r.pos < 0 || r.pos + size_of(T) > r.data.count { r.bad = true; return xx 0; }
    defer r.pos += size_of(T);
    return (cast(*T) *r.data[r.pos]).*;
}

// .NET BinaryWriter strings: a 7-bit-encoded length, then UTF-8.
read_string :: (r: *Reader) -> string {
    count, shift := 0, 0;
    while !r.bad {
        b := read(r, u8);
        count |= cast(int) (b & 0x7f) << shift;
        shift += 7;
        if !(b & 0x80) break;
    }
    if r.pos + count > r.data.count { r.bad = true; return ""; }
    defer r.pos += count;
    return copy_string(.{count, *r.data[r.pos]});
}

// One field at a time: the reads advance r.pos, so their order matters.
read_subtex :: (r: *Reader) -> Subtex {
    s: Subtex;
    s.source.x = read(r, u16); s.source.y = read(r, u16); s.source.w = read(r, u16); s.source.h = read(r, u16);
    s.frame.x = read(r, s16);  s.frame.y = read(r, s16);  s.frame.w = read(r, u16);  s.frame.h = read(r, u16);
    return s;
}

skip_subtex :: (r: *Reader) { r.pos += 16; }

read_subtexes :: (r: *Reader) -> [] Subtex {
    result := NewArray(read(r, u16), Subtex);
    for * result it.* = read_subtex(r);
    return result;
}

read_occluders :: (r: *Reader) -> [] [] Segment {
    result := NewArray(read(r, u16), [] Segment);
    for * tile: result {
        tile.* = NewArray(read(r, u16), Segment);
        for * tile.* {
            // One read per statement: the order a struct literal's fields are evaluated in isn't promised.
            it.a.x = read(r, float); it.a.y = read(r, float);
            it.b.x = read(r, float); it.b.y = read(r, float);
        }
    }
    return result;
}

premultiply :: (pixels: [] u8) {
    for i: 0..pixels.count / 4 - 1 {
        p := pixels.data + i * 4;
        for 0..2 p[it] = xx ((cast(u32) p[it] * p[3] + 127) / 255);
    }
}

#import "Basic";
#import "File";
#import "Math";
#import "Kit";
