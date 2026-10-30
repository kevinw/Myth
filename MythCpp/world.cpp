// The room: its file, the visual pack's tile art and normals, the tiles drawn the way Myth picks
// them, sprites by name, and actors moving against the solid tiles.
#include "game.h"
#include <algorithm>

// --- A Myth room file (Myth/Assets/Rooms/**.json): two layers of tile chars, a legend naming each
// char's tile type, lighting settings, and the actors. ---

static std::span<u8> read_layer(Json rows, Point2 size) {
    auto layer = persist.array<u8>(size_t(size.x) * size.y);
    memset(layer.data(), '0', layer.size());
    s32 y = 0;
    for (Json& row : rows) {
        if (y >= size.y) break;
        string_view text = row.as_string();
        memcpy(&layer[size_t(y++) * size.x], text.data(), min((s32)text.size(), size.x));
    }
    return layer;
}

std::optional<Room> load_room(const char* path) {
    auto text = read_file(path, persist);  // the json's strings point into it
    if (text.empty()) return {};
    auto json = parse_json({(const char*)text.data(), text.size()}, persist);
    if (!json) { log("%s isn't valid JSON", path); return {}; }

    Room r;
    Vector2 size = (*json)["size"].as_vector2(), position = (*json)["position"].as_vector2();
    r.size = {(s32)size.x, (s32)size.y};
    r.position = {(s32)position.x, (s32)position.y};
    r.backdrop = rgb((*json)["backdropColor"].as_string(), Color::rgb(0x1a0a2e));
    r.ambient = rgb((*json)["ambientColor"].as_string(), Color::rgb(0x525252));
    r.ambient_intensity = (*json)["ambientIntensity"].as_float(0.8f);
    r.reverb = (*json)["reverb"].as_float();
    Json legend = (*json)["legend"];
    for (size_t i = 0; i < legend.keys.size(); i++)
        if (legend.keys[i].size() == 1 && (u8)legend.keys[i][0] < 128) r.legend[(u8)legend.keys[i][0]] = legend.items[i].as_string();
    r.tiles = read_layer((*json)["tiles"], r.size);
    r.background = read_layer((*json)["backgroundTiles"], r.size);
    r.actors = (*json)["actors"];
    return r;
}

// --- Myth's visual asset pack (Myth/Assets/Packed/Visuals): the C# baker's output, read rather than
// re-baked here. It holds the generated tile kits; rebuild it with `dotnet run -- --build-asset-pack`
// in Myth/ after changing tilesets. Format: Myth/Source/Assets/Assets.cs,
// WriteVisualAssetPackManifest (a .NET BinaryWriter stream). ---

namespace {
struct Reader {
    std::span<u8> data;
    size_t pos = 0;
    bool bad = false;

    template <typename T> T read() {
        if (pos + sizeof(T) > data.size()) { bad = true; return T{}; }
        T v;
        memcpy(&v, &data[pos], sizeof(T));
        pos += sizeof(T);
        return v;
    }
    // .NET BinaryWriter strings: a 7-bit-encoded length, then UTF-8. Points into the file.
    string_view string() {
        size_t count = 0;
        for (s32 shift = 0; !bad; shift += 7) {
            u8 b = read<u8>();
            count |= size_t(b & 0x7f) << shift;
            if (!(b & 0x80)) break;
        }
        if (pos + count > data.size()) { bad = true; return {}; }
        string_view s{(const char*)&data[pos], count};
        pos += count;
        return s;
    }
    // One field at a time: the reads advance pos, so their order matters.
    Subtex subtex() {
        Subtex s;
        s.source.x = read<u16>(); s.source.y = read<u16>(); s.source.w = read<u16>(); s.source.h = read<u16>();
        s.frame.x = read<s16>(); s.frame.y = read<s16>(); s.frame.w = read<u16>(); s.frame.h = read<u16>();
        return s;
    }
    void skip_subtex() { pos += 16; }
    std::span<Subtex> subtexes() {
        auto out = persist.array<Subtex>(read<u16>());
        for (Subtex& s : out) s = subtex();
        return out;
    }
    std::span<std::span<Segment>> occluders() {
        auto out = persist.array<std::span<Segment>>(read<u16>());
        for (auto& tile : out) {
            tile = persist.array<Segment>(read<u16>());
            for (Segment& s : tile) { s.a.x = read<float>(); s.a.y = read<float>(); s.b.x = read<float>(); s.b.y = read<float>(); }
        }
        return out;
    }
};

std::optional<Texture> load_pack_qoi(string_view name) {
    return load_qoi(asset("Packed/Visuals/%.*s", (int)name.size(), name.data()));
}
}

std::optional<Pack> load_pack() {
    const s32 PACK_VERSION = 16;
    auto file = read_file(asset("Packed/Visuals/manifest.bin"), persist);
    if (file.empty()) return {};
    Reader r{file};
    if (r.read<u32>() != 0x5356494D || r.read<s32>() != PACK_VERSION) { log("visual pack is stale; rebuild it with --build-asset-pack"); return {}; }

    Pack pack;
    string_view atlas_name = r.string();
    string_view noise_name = r.read<bool>() ? r.string() : string_view{};

    for (u16 n = r.read<u16>(); n > 0; n--) { r.string(); r.skip_subtex(); }  // named subtextures
    pack.sprites = persist.array<PackedSprite>(r.read<u16>());
    for (PackedSprite& sprite : pack.sprites) {  // just their normals; the rest we load from their .ase
        sprite.name = r.string();
        sprite.origin.x = r.read<float>();
        sprite.origin.y = r.read<float>();
        sprite.normals = persist.array<Subtex>(r.read<u16>());
        for (Subtex& normal : sprite.normals) {
            r.skip_subtex(); r.skip_subtex();  // color, visible
            normal = r.subtex();
            r.skip_subtex();                   // emissive
            r.pos += 4; r.string(); r.pos += 8 + 4 + 4 + 4;
        }
        for (u16 n = r.read<u16>(); n > 0; n--) { r.string(); r.pos += 12; }
        for (u16 n = r.read<u16>(); n > 0; n--) { r.string(); r.pos += 16; }
        for (u16 n = r.read<u16>(); n > 0; n--) { r.string(); r.pos += r.read<u16>() * 17; }
    }

    pack.tilesets = persist.array<PackedTileset>(r.read<u16>());
    for (PackedTileset& t : pack.tilesets) {
        t = {};
        t.name = r.string();
        t.columns = r.read<s32>();
        t.rows = r.read<s32>();
        t.fill_tiles = r.read<s32>();
        t.tiles = r.subtexes();
        t.variants = persist.array<std::span<Subtex>>(r.read<u16>());
        for (auto& v : t.variants) v = r.subtexes();
        t.normal_tiles = r.subtexes();
        t.normal_variants = persist.array<std::span<Subtex>>(r.read<u16>());
        for (auto& v : t.normal_variants) v = r.subtexes();
        t.occluders = r.occluders();
        t.occluder_variants = persist.array<std::span<std::span<Segment>>>(r.read<u16>());
        for (auto& v : t.occluder_variants) v = r.occluders();
    }
    if (r.bad) { log("couldn't read the visual pack"); return {}; }

    auto atlas = load_pack_qoi(atlas_name);
    if (!atlas) return {};
    pack.atlas = *atlas;
    if (!noise_name.empty()) pack.lighting_noise = load_pack_qoi(noise_name).value_or(Texture{});
    return pack;
}

PackedSprite* Pack::find_sprite(string_view name) {
    for (PackedSprite& s : sprites) if (s.name == name) return &s;
    return nullptr;
}

PackedTileset* Pack::find_tileset(string_view name) {
    for (PackedTileset& t : tilesets) if (t.name == name) return &t;
    return nullptr;
}

void draw_subtex(const Subtex& t, Vector2 pos, RectInt clip, Color color, bool wash) {
    // Where the source pixels sit within the untrimmed image.
    s32 x0 = -t.frame.x, y0 = -t.frame.y, x1 = x0 + t.source.w, y1 = y0 + t.source.h;
    if (clip.w) { x0 = max(x0, clip.x); y0 = max(y0, clip.y); x1 = min(x1, clip.x + clip.w); y1 = min(y1, clip.y + clip.h); }
    if (x1 <= x0 || y1 <= y0) return;
    Rect source{{(float)(t.source.x + x0 + t.frame.x), (float)(t.source.y + y0 + t.frame.y)}, {(float)(x1 - x0), (float)(y1 - y0)}};
    image(pack.atlas, source, pos + Vector2{(float)x0, (float)y0}, color, wash);
}

// --- The room's tile art, chosen the way Myth does it so the same room looks the same:
//  - a plain tileset (Grid) picks a tile per cell with Foster's Rng seeded by the cell;
//  - a kit (DualGrid) draws on the dual grid, one tile per cell corner: the shape comes from
//    which of the four cells around the corner are solid, the variant from a hash of the world
//    tile, and where two kits meet each draws only its own quadrants.
// Also Myth's tile field, which picks the lighting dither per tile: every cell gets a variant from
// a hash of its world tile, and the foreground tiles stamp their own over it, family and all. And
// the edges that cast shadows: the room's bounds and the foreground kits' occluders.
// Not ported yet: slopes, fill patterns spanning tiles. ---

List<TileDraw> background_tiles{.arena = &persist}, foreground_tiles{.arena = &persist};
List<Segment> occluders{.arena = &persist};

// Game.TileFieldColor: r = variant, g = dither family << 5 (the low bits are the anchor, 0 for rooms).
static Color tile_field_color(u8 c, Point2 world) {
    u32 hash = 2166136261u;
    hash = (hash ^ (u32)world.x) * 16777619u;
    hash = (hash ^ (u32)world.y) * 16777619u;
    hash = (hash ^ c) * 16777619u;
    u8 family = 4;
    switch (c) {
        case '0': family = 0; break;
        case '1': case '2': case 'x': family = 1; break;
        case 'G': case 'S': case 'X': case 'g': family = 2; break;
        case 'w': family = 3; break;
    }
    return {u8(hash & 255), u8(family << 5), 0, 255};
}

// DualGrid.MaskToTile: corner mask (bit per quadrant) -> tile in the kit's 4x4 layout.
static constexpr Point2 MASK_TO_TILE[16] = {
    {0, 3}, {3, 3}, {0, 0}, {3, 2}, {0, 2}, {1, 2}, {2, 3}, {3, 1},
    {1, 3}, {0, 1}, {3, 0}, {2, 0}, {1, 0}, {2, 2}, {1, 1}, {2, 1},
};
enum : s32 { TOP_LEFT = 1, BOTTOM_LEFT = 2, TOP_RIGHT = 4, BOTTOM_RIGHT = 8, ALL = 15 };
static constexpr struct { s32 bit; RectInt clip; } QUADRANTS[4] = {
    {TOP_LEFT, {0, 0, 4, 4}}, {BOTTOM_LEFT, {0, 4, 4, 4}}, {TOP_RIGHT, {4, 0, 4, 4}}, {BOTTOM_RIGHT, {4, 4, 4, 4}},
};

// Cells off the room clamp to its edge, so terrain runs on past it.
static u8 cell(std::span<u8> layer, s32 x, s32 y) {
    return layer[size_t(clamp(y, 0, room.size.y - 1)) * room.size.x + clamp(x, 0, room.size.x - 1)];
}

// Tileset.GetVariantTile with fill patterns one tile wide.
static std::optional<Subtex> variant_tile(std::span<Subtex> tiles, std::span<std::span<Subtex>> variants, s32 rows, Point2 at, s32 seed) {
    auto source = variants.empty() ? tiles : variants[(seed & 0x7fffffff) % variants.size()];
    size_t index = at.x * rows + at.y;
    if (index >= source.size()) return {};
    return source[index];
}

// Tileset.GetVariantOccluders, picked like variant_tile.
static std::span<Segment> variant_occluders(const PackedTileset& t, Point2 at, s32 seed) {
    auto tiles = t.occluder_variants.empty() ? t.occluders : t.occluder_variants[(seed & 0x7fffffff) % t.occluder_variants.size()];
    size_t index = at.x * t.rows + at.y;
    return index < tiles.size() ? tiles[index] : std::span<Segment>{};
}

// Kit's Hash32.TileVariantSeed.
static s32 tile_variant_seed(Point2 world, s32 c) {
    u32 hash = (u32)world.x * 0x8da6b343u;
    hash ^= (u32)world.y * 0xd8163841u;
    hash ^= (u32)c * 0xcb1ab31fu;
    hash ^= hash >> 16;
    hash *= 0x7feb352du;
    hash ^= hash >> 15;
    hash *= 0x846ca68bu;
    return s32(hash ^ (hash >> 16));
}

// Foster's Rng.U32 (SplitMix64).
static u32 splitmix(u64& state) {
    state += 0x9e3779b97f4a7c15ull;
    u64 n = state;
    n = (n ^ (n >> 30)) * 0xbf58476d1ce4e5bull;
    n = (n ^ (n >> 27)) * 0x94d049bb133111ebull;
    return u32(n ^ (n >> 31));
}

// A legend's tile type name to its tileset: "Myth Back" -> "myth-back".
static string_view tileset_name(string_view entry, const char* suffix = "") {
    const char* name = tprint("%.*s%s", (int)entry.size(), entry.data(), suffix);
    char* c = (char*)name;
    for (; *c; c++) *c = *c == ' ' ? '-' : (char)tolower(*c);
    return name;
}

static void build_layer(std::span<u8> layer, List<TileDraw>& draws, bool foreground) {
    PackedTileset* kits[128] = {};
    PackedTileset* plain[128] = {};
    for (s32 c = 1; c < 128; c++) if (!room.legend[c].empty()) {
        kits[c] = pack.find_tileset(tileset_name(room.legend[c], "-dual"));
        if (!kits[c]) plain[c] = pack.find_tileset(tileset_name(room.legend[c]));
        if (!kits[c] && !plain[c]) log("no tileset for %.*s", (int)room.legend[c].size(), room.legend[c].data());
    }

    for (s32 y = 0; y < room.size.y; y++) for (s32 x = 0; x < room.size.x; x++) {
        u8 c = layer[size_t(y) * room.size.x + x];
        PackedTileset* t = c < 128 ? plain[c] : nullptr;
        if (!t || t->tiles.empty()) continue;
        u64 rng = u64(x + y * room.size.x);
        size_t index = splitmix(rng) % (u32)t->tiles.size();
        draws.add({t->tiles[index], index < t->normal_tiles.size() ? t->normal_tiles[index] : Subtex{}, Point2{x, y}.vector() * TILE_SIZE, {}, tile_field_color(c, room.position + Point2{x, y})});
    }

    for (s32 c = 1; c < 128; c++) if (kits[c]) for (s32 vy = 0; vy <= room.size.y; vy++) for (s32 vx = 0; vx <= room.size.x; vx++) {
        s32 material = 0, join = 0;
        for (auto& q : QUADRANTS) {
            u8 here = cell(layer, vx - 1 + q.clip.x / 4, vy - 1 + q.clip.y / 4);
            if (here == c) material |= q.bit;
            // The shape joins the solid cells (foreground) or the kit cells (background) around the corner.
            if (foreground ? here != '0' : here < 128 && kits[here]) join |= q.bit;
        }
        if (!material || !join) continue;
        Point2 world = room.position + Point2{vx, vy};
        s32 seed = tile_variant_seed(world, c);
        auto tile = variant_tile(kits[c]->tiles, kits[c]->variants, kits[c]->rows, MASK_TO_TILE[join], seed);
        if (!tile) continue;
        Subtex normal = variant_tile(kits[c]->normal_tiles, kits[c]->normal_variants, kits[c]->rows, MASK_TO_TILE[join], seed).value_or(Subtex{});
        Vector2 pos = (Point2{vx, vy}.vector() - Vector2{0.5f, 0.5f}) * TILE_SIZE;
        Color field = tile_field_color((u8)c, world);
        if (foreground) for (Segment& s : variant_occluders(*kits[c], MASK_TO_TILE[join], seed)) {
            Vector2 mid = (s.a + s.b) * 0.5f;
            s32 quadrant = mid.x < TILE_SIZE / 2 ? (mid.y < TILE_SIZE / 2 ? TOP_LEFT : BOTTOM_LEFT) : (mid.y < TILE_SIZE / 2 ? TOP_RIGHT : BOTTOM_RIGHT);
            if (material & quadrant) occluders.add({pos + s.a, pos + s.b});
        }
        if (material == ALL) draws.add({*tile, normal, pos, {}, field});
        else for (auto& q : QUADRANTS) if (material & q.bit) draws.add({*tile, normal, pos, q.clip, field});
    }
}

// DualGrid.MergeAxisAlignedShadowSegments: tile edges that line up become one run, so a light
// tests a handful of long edges instead of every tile's.
static void merge_occluders() {
    struct Run { bool horizontal; float axis, start, end; };
    List<Run> runs;
    s32 kept = 0;
    for (Segment& s : occluders) {
        if (fabsf(s.a.y - s.b.y) < 0.01f) runs.add({true, s.a.y, min(s.a.x, s.b.x), max(s.a.x, s.b.x)});
        else if (fabsf(s.a.x - s.b.x) < 0.01f) runs.add({false, s.a.x, min(s.a.y, s.b.y), max(s.a.y, s.b.y)});
        else occluders[kept++] = s;
    }
    occluders.count = kept;
    std::sort(runs.begin(), runs.end(), [](const Run& a, const Run& b) {
        if (a.horizontal != b.horizontal) return a.horizontal;
        if (fabsf(a.axis - b.axis) >= 0.01f) return a.axis < b.axis;
        return a.start < b.start;
    });
    auto add_run = [](const Run& r) {
        if (r.end <= r.start) return;
        occluders.add(r.horizontal ? Segment{{r.start, r.axis}, {r.end, r.axis}} : Segment{{r.axis, r.start}, {r.axis, r.end}});
    };
    if (!runs.count) return;
    Run current = runs[0];
    for (s32 i = 1; i < runs.count; i++) {
        Run next = runs[i];
        if (next.horizontal == current.horizontal && fabsf(next.axis - current.axis) < 0.01f && next.start <= current.end + 0.001f) {
            current.end = max(current.end, next.end);
            continue;
        }
        add_run(current);
        current = next;
    }
    add_run(current);
}

void build_tile_draws() {
    background_tiles.count = foreground_tiles.count = occluders.count = 0;
    Vector2 size = (room.size * TILE_SIZE).vector();
    Vector2 corners[4] = {{}, {size.x, 0}, size, {0, size.y}};
    for (s32 i = 0; i < 4; i++) occluders.add({corners[i], corners[(i + 1) % 4]});
    build_layer(room.background, background_tiles, false);
    build_layer(room.tiles, foreground_tiles, true);
    merge_occluders();
}

void draw_tiles(std::span<TileDraw> draws, bool normals) {
    for (TileDraw& d : draws) {
        const Subtex& tile = normals ? d.normal : d.tile;
        if (tile.source.w) draw_subtex(tile, d.pos, d.clip);
    }
}

void draw_tile_field() {
    for (s32 y = 0; y < room.size.y; y++) for (s32 x = 0; x < room.size.x; x++)
        rect({Point2{x, y}.vector() * TILE_SIZE, {TILE_SIZE, TILE_SIZE}}, tile_field_color('0', room.position + Point2{x, y}));
    for (TileDraw& d : foreground_tiles) if (d.tile.source.w) draw_subtex(d.tile, d.pos, d.clip, d.field, true);
}

// --- Sprites by name ---

static struct { char name[64]; Sprite sprite; bool tried, loaded; } sprite_cache[64]{};

Sprite* get_sprite(string_view name) {
    for (auto& s : sprite_cache) {
        if (s.tried && name == s.name) return s.loaded ? &s.sprite : nullptr;
        if (s.tried) continue;
        s.tried = true;
        snprintf(s.name, sizeof(s.name), "%.*s", (int)name.size(), name.data());
        auto loaded = load_sprite(asset("Sources/Sprites/%s.ase", s.name));
        if (!loaded) loaded = load_sprite(asset("Sources/Sprites/%s.aseprite", s.name));
        if (!loaded) { log("no sprite %s", s.name); return nullptr; }
        s.sprite = *loaded;
        s.loaded = true;
        return &s.sprite;
    }
    return nullptr;
}

void reload_changed_sprites() {
    for (auto& s : sprite_cache) if (s.loaded) reload_if_changed(s.sprite);
}

// --- Actors moving against the room ---

void draw(Actor& a) { rect({a.position.vector() - Vector2{3, 3}, {6, 6}}, Color::rgb(0xff00ff)); }

void advance(Actor& a, float dt) {
    a.squish = approach(a.squish, {1, 1}, dt * 4);
    a.hit_cooldown = approach(a.hit_cooldown, 0, dt);
    a.animation_time += dt;
}

void play(Actor& a, string_view name, bool looping) {
    if (a.animation != name) a.animation_time = 0;
    a.animation = name;
    a.animation_looping = looping;
}

void draw_sprite(Actor& a, string_view name) {
    Sprite* sprite = get_sprite(name);
    if (!sprite) return;
    draw_sprite(*sprite, sprite->find_tag(a.animation), a.animation_time, a.position.vector(), {a.facing * a.squish.x, a.squish.y}, a.animation_looping);
}

void draw_sprite_normals(Actor& a, string_view name) {
    Sprite* sprite = get_sprite(name);
    PackedSprite* packed = pack.find_sprite(name);
    if (!sprite || !packed) return;
    s32 frame = sprite->frame_at(sprite->find_tag(a.animation), a.animation_time, a.animation_looping);
    if (frame < 0 || frame >= (s32)packed->normals.size()) return;
    push_matrix(a.position.vector(), {a.facing * a.squish.x, a.squish.y});
    draw_subtex(packed->normals[frame], -packed->origin);
    pop_matrix();
}

static s32 tile_of(s32 pixel) { return (s32)floorf((float)pixel / TILE_SIZE); }

bool overlaps_solid(RectInt r) {
    if (r.w <= 0 || r.h <= 0) return false;
    for (s32 y = tile_of(r.y); y <= tile_of(r.y + r.h - 1); y++) for (s32 x = tile_of(r.x); x <= tile_of(r.x + r.w - 1); x++) {
        if (x < 0 || y < 0 || x >= room.size.x || y >= room.size.y) continue;
        if (room.tiles[size_t(y) * room.size.x + x] != '0') return true;
    }
    return false;
}

bool overlaps_solid(const Actor& a, Point2 offset) { return overlaps_solid(a.hitbox + a.position + offset); }
bool grounded(const Actor& a) { return overlaps_solid(a, {0, 1}); }
bool fell_out(const Actor& a) { return a.position.y + a.hitbox.y >= room.size.y * TILE_SIZE; }

static bool move_pixel(Actor& a, Point2 dir) {
    if (overlaps_solid(a, dir) || (dir.y > 0 && grounded(a))) return false;
    a.position += dir;
    return true;
}

u8 move(Actor& a, Vector2 delta) {
    a.remainder += delta;
    Point2 step = to_point(a.remainder);  // toward zero, like Myth's (Point2) cast
    a.remainder -= step.vector();
    u8 moved = 0;

    while (step.x) {
        Point2 dir{sign((float)step.x), 0};
        bool was_grounded = grounded(a);
        if (move_pixel(a, dir)) {
            if (was_grounded && !grounded(a)) a.position.y += 1;  // step down (off a cliff, just the first pixel)
        } else if (was_grounded && !overlaps_solid(a, {0, -1}) && !overlaps_solid(a, dir + Point2{0, -1})) {
            a.position += dir + Point2{0, -1};  // step up
        } else {
            a.velocity.x = a.remainder.x = 0;
            moved |= HIT_X;
            break;
        }
        step.x -= dir.x;
    }
    while (step.y) {
        Point2 dir{0, sign((float)step.y)};
        if (!move_pixel(a, dir)) {
            a.velocity.y = a.remainder.y = 0;
            moved |= HIT_Y;
            break;
        }
        step.y -= dir.y;
    }
    return moved;
}
