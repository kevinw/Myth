#include "aseprite.h"
#include <zlib.h>

static constexpr s32 MAX_LAYERS = 64;

namespace {
struct Layer { std::string_view name; u16 flags; u8 opacity; };
struct Cel { s32 x, y, w, h; u8 opacity; u8* pixels; bool linked; };

struct Reader {
    std::span<u8> data;
    size_t pos = 0;
    bool bad = false;

    template <typename T> T read() {
        if (pos + sizeof(T) > data.size()) { bad = true; pos = data.size(); return T{}; }
        T v;
        memcpy(&v, &data[pos], sizeof(T));
        pos += sizeof(T);
        return v;
    }
    // Points into the file.
    std::string_view string() {
        u16 n = read<u16>();
        if (pos + n > data.size()) { bad = true; return {}; }
        std::string_view s{(const char*)&data[pos], n};
        pos += n;
        return s;
    }
};

s32 mul_un8(s32 a, s32 b) {
    s32 t = a * b + 0x80;
    return ((t >> 8) + t) >> 8;
}

void blend_normal(u8* dst, const u8* src, s32 opacity) {
    s32 r = dst[0], g = dst[1], b = dst[2];
    if (!dst[3]) {
        r = src[0]; g = src[1]; b = src[2];
    } else if (src[3]) {
        r += mul_un8(src[0] - r, opacity);
        g += mul_un8(src[1] - g, opacity);
        b += mul_un8(src[2] - b, opacity);
    }
    s32 a = dst[3] + mul_un8(src[3] > dst[3] ? src[3] - dst[3] : 0, opacity);
    if (!a) r = g = b = 0;
    dst[0] = u8(r); dst[1] = u8(g); dst[2] = u8(b); dst[3] = u8(a);
}

// A cel's pixels as RGBA8 in temp; null if the data is short or corrupt.
u8* read_cel_pixels(Reader& r, size_t chunk_end, bool compressed, s32 n, s32 bpp, const u8* palette) {
    const u8* raw = &r.data[r.pos];
    if (compressed) {
        u8* inflated = (u8*)temp.alloc(n * bpp);
        uLongf size = n * bpp;
        if (uncompress(inflated, &size, raw, uLong(chunk_end - r.pos)) != Z_OK || size != uLongf(n * bpp)) return nullptr;
        raw = inflated;
    } else if (r.pos + n * bpp > chunk_end) return nullptr;

    u8* out = (u8*)temp.alloc(n * 4);
    for (s32 i = 0; i < n; i++) {
        u8* o = out + i * 4;
        if (bpp == 4) memcpy(o, raw + i * 4, 4);
        else if (bpp == 2) { o[0] = o[1] = o[2] = raw[i * 2]; o[3] = raw[i * 2 + 1]; }
        else memcpy(o, palette + raw[i] * 4, 4);
    }
    return out;
}

s32 tag_step(const Tag& tag, s32 len, s32 k) {
    if (k >= len) k = len * 2 - 2 - k;  // ping-pong return leg
    if (tag.loop == Tag::Reverse || tag.loop == Tag::PingPongReverse) k = len - 1 - k;
    return tag.first + k;
}
}

s64 file_modtime(const char* path) {
    SDL_PathInfo info;
    return SDL_GetPathInfo(path, &info) ? info.modify_time : 0;
}

std::optional<Sprite> load_sprite(const char* path) {
    s64 modtime = file_modtime(path);
    auto file = read_file(path);
    if (file.empty()) return {};
    Reader r{file};

    r.read<u32>();
    if (r.read<u16>() != 0xA5E0) return {};
    s32 frame_count = r.read<u16>();
    s32 w = r.read<u16>();
    s32 h = r.read<u16>();
    s32 bpp = r.read<u16>() / 8;
    r.pos = 128;
    if (r.bad || frame_count <= 0 || w <= 0 || h <= 0) return {};

    Sprite sprite{.modtime = modtime, .frame_size = {(float)w, (float)h}};
    size_t memory = strlen(path) + 1 + frame_count * sizeof(s32) + 256 * sizeof(Tag) + file.size() + 64;
    sprite.memory = {(u8*)malloc(memory), memory};
    char* own_path = (char*)sprite.memory.alloc(strlen(path) + 1, 1);
    strcpy(own_path, path);
    sprite.path = own_path;
    sprite.durations = sprite.memory.array<s32>(frame_count);
    sprite.animation = {"", 0, frame_count - 1};
    auto tags = sprite.memory.array<Tag>(256);
    size_t tag_count = 0;
    bool has_pivot = false;

    Layer layers[MAX_LAYERS] = {};
    s32 layer_count = 0;
    u8 palette[1024] = {};  // 256 RGBA entries
    auto cels = temp.array<Cel>(size_t(frame_count) * MAX_LAYERS);
    memset(cels.data(), 0, cels.size_bytes());

    for (s32 f = 0; f < frame_count && !r.bad; f++) {
        size_t frame_end = r.pos + r.read<u32>();
        if (r.read<u16>() != 0xF1FA) { r.bad = true; break; }
        u32 chunks = r.read<u16>();
        sprite.durations[f] = r.read<u16>();
        r.pos += 2;
        if (u32 count = r.read<u32>()) chunks = count;

        for (u32 c = 0; c < chunks && !r.bad; c++) {
            size_t chunk_end = r.pos + r.read<u32>();
            switch (r.read<u16>()) {
            case 0x2004: {  // layer
                if (layer_count == MAX_LAYERS) break;
                Layer& l = layers[layer_count++];
                l.flags = r.read<u16>();
                r.pos += 10;  // type, child level, default size, blend mode (normal only)
                l.opacity = r.read<u8>();
                r.pos += 3;
                l.name = r.string();
                break;
            }
            case 0x2005: {  // cel
                u16 li = r.read<u16>();
                Cel cel{.x = r.read<s16>(), .y = r.read<s16>(), .opacity = r.read<u8>()};
                u16 type = r.read<u16>();
                r.pos += 7;  // z-index, reserved
                if (li >= layer_count) break;
                if (type == 1) {  // linked
                    u16 lf = r.read<u16>();
                    if (lf < f) { cel = cels[lf * MAX_LAYERS + li]; cel.linked = true; }
                } else if (type == 0 || type == 2) {
                    cel.w = r.read<u16>();
                    cel.h = r.read<u16>();
                    cel.pixels = read_cel_pixels(r, chunk_end, type == 2, cel.w * cel.h, bpp, palette);
                    if (!cel.pixels) r.bad = true;
                }
                cels[f * MAX_LAYERS + li] = cel;
                break;
            }
            case 0x2018: {  // tags
                u16 count = r.read<u16>();
                r.pos += 8;
                for (u16 i = 0; i < count; i++) {
                    Tag tag;
                    tag.first = r.read<u16>();
                    tag.last = r.read<u16>();
                    tag.loop = (Tag::Loop)r.read<u8>();
                    r.pos += 12;  // repeat, reserved, color
                    std::string_view name = r.string();
                    char* own = (char*)sprite.memory.alloc(name.size(), 1);
                    memcpy(own, name.data(), name.size());
                    tag.name = {own, name.size()};
                    if (tag_count < tags.size()) tags[tag_count++] = tag;
                }
                break;
            }
            case 0x2022: {  // slice: the first one with a pivot sets the origin
                u32 keys = r.read<u32>();
                u32 flags = r.read<u32>();
                r.pos += 4;
                r.string();
                if (keys && flags & 2 && !has_pivot) {
                    r.pos += 4;  // frame
                    Vector2 origin{(float)r.read<s32>(), (float)r.read<s32>()};
                    r.pos += 8;  // size
                    if (flags & 1) r.pos += 16;  // nine-patch center
                    sprite.origin = origin + Vector2{(float)r.read<s32>(), (float)r.read<s32>()};
                    has_pivot = true;
                }
                break;
            }
            case 0x2019: {  // palette
                r.read<u32>();
                s32 first = r.read<s32>();
                s32 last = r.read<s32>();
                r.pos += 8;
                for (s32 i = first; i <= last && !r.bad; i++) {
                    u16 flags = r.read<u16>();
                    for (s32 k = 0; k < 4; k++) {
                        u8 v = r.read<u8>();
                        if (i >= 0 && i < 256) palette[i * 4 + k] = v;
                    }
                    if (flags & 1) r.string();
                }
                break;
            }
            }
            r.pos = chunk_end;
        }
        r.pos = frame_end;
    }
    if (r.bad) {
        free(sprite.memory.base);
        return {};
    }

    // Composite each frame into its slot in the atlas.
    s32 stride = w * frame_count;
    auto atlas = temp.array<u8>(size_t(stride) * h * 4);
    memset(atlas.data(), 0, atlas.size());
    for (s32 f = 0; f < frame_count; f++) for (s32 li = 0; li < layer_count; li++) {
        const Layer& layer = layers[li];
        const Cel& cel = cels[f * MAX_LAYERS + li];
        if (!(layer.flags & 1) || !cel.pixels || layer.name.find("debug") != layer.name.npos || layer.name.ends_with("_socket")) continue;
        s32 opacity = mul_un8(cel.opacity, layer.opacity);
        for (s32 y = 0; y < cel.h; y++) for (s32 x = 0; x < cel.w; x++) {
            s32 dx = cel.x + x, dy = cel.y + y;
            if (dx < 0 || dx >= w || dy < 0 || dy >= h) continue;
            blend_normal(&atlas[(size_t(dy) * stride + f * w + dx) * 4], cel.pixels + (y * cel.w + x) * 4, opacity);
        }
    }

    sprite.tags = tags.first(tag_count);
    if (tag_count) sprite.animation = tags[0];
    if (!has_pivot) {  // bottom center of frame 0's opaque pixels
        s32 x0 = w, x1 = 0, y1 = 0;
        for (s32 y = 0; y < h; y++) for (s32 x = 0; x < w; x++) if (atlas[(size_t(y) * stride + x) * 4 + 3]) {
            x0 = min(x0, x);
            x1 = max(x1, x + 1);
            y1 = max(y1, y + 1);
        }
        sprite.origin = x1 ? Vector2{floorf((x0 + x1) * 0.5f + 0.5f), (float)y1} : Vector2{w * 0.5f, (float)h};
    }

    for (size_t i = 0; i < atlas.size(); i += 4)
        for (s32 k = 0; k < 3; k++) atlas[i + k] = u8(mul_un8(atlas[i + k], atlas[i + 3]));  // premultiply, like Foster
    sprite.texture = make_texture(atlas.data(), stride, h);
    return sprite;
}

void free_sprite(Sprite& s) {
    release_texture(s.texture);
    free(s.memory.base);
    s = {};
}

void reload_if_changed(Sprite& s) {
    if (file_modtime(s.path) == s.modtime) return;
    if (auto fresh = load_sprite(s.path)) {
        free_sprite(s);
        s = *fresh;
    }
}

Tag Sprite::find_tag(std::string_view name) const {
    for (const Tag& t : tags) if (t.name == name) return t;
    return animation;
}

float Sprite::duration(const Tag& tag) const {
    s32 ms = 0;
    for (s32 i = tag.first; i <= tag.last; i++) ms += durations[i];
    return ms / 1000.0f;
}

s32 Sprite::frame_at(const Tag& tag, double seconds, bool looping) const {
    s32 len = tag.last - tag.first + 1;
    bool ping_pong = tag.loop == Tag::PingPong || tag.loop == Tag::PingPongReverse;
    s32 steps = ping_pong && len > 1 ? len * 2 - 2 : len;
    s64 total = 0;
    for (s32 k = 0; k < steps; k++) total += durations[tag_step(tag, len, k)];
    if (total <= 0) return tag.first;

    s64 t = s64(seconds * 1000);
    if (!looping && t >= total) return tag_step(tag, len, steps - 1);
    t %= total;
    for (s32 k = 0; k < steps; k++) {
        s32 frame = tag_step(tag, len, k);
        t -= durations[frame];
        if (t < 0) return frame;
    }
    return tag.last;
}

void draw_sprite(const Sprite& s, const Tag& tag, double seconds, Vector2 pos, Vector2 scale, bool looping) {
    Rect source{{s.frame_at(tag, seconds, looping) * s.frame_size.x, 0}, s.frame_size};
    push_matrix(pos, scale);
    image(s.texture, source, -s.origin);
    pop_matrix();
}

void draw_frame(const Sprite& s, s32 frame, Vector2 pos, Color color) {
    image(s.texture, {{frame * s.frame_size.x, 0}, s.frame_size}, pos, color);
}
