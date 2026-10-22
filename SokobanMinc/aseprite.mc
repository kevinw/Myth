// Loads an .ase/.aseprite file and flattens every frame to RGBA8.
// A cut-down port of Foster's Aseprite.cs: layers (normal blend), raw /
// compressed / linked cels, RGBA / grayscale / indexed, tags, slice pivot.
// Not handled: tilemaps, other blend modes, group opacity, cel z-index.
// Truncated or malformed files fail the load instead of trapping, so a sprite
// caught mid-save can be retried.

import file;
import str;
import zlib;

enum AseLoop { ASE_FORWARD, ASE_REVERSE, ASE_PING_PONG, ASE_PING_PONG_REVERSE }

struct AseTag { str name; i32 first; i32 last; AseLoop loop; }
struct AseLayer { str name; u16 flags; u8 opacity; }

struct Aseprite {
    i32 width;
    i32 height;
    i32 frame_count;
    u8* pixels;         // frame_count frames of width * height RGBA8, frame-major
    i32* durations;     // ms
    AseTag* tags;
    i32 tag_count;
    bool has_pivot;
    int2 pivot;
    FileData file;      // names point into it
}

private {
    const i32 MAX_LAYERS = 64;

    struct Reader { u8* data; i64 len; i64 pos; bool bad; }

    u8 rd_u8(Reader* r) {
        if r.pos >= r.len { r.bad = true; return 0; }
        return r.data[r.pos++];
    }
    u16 rd_u16(Reader* r) { u16 lo = rd_u8(r); u16 hi = rd_u8(r); return cast(u16, lo | hi << 8); }
    u32 rd_u32(Reader* r) { u32 lo = rd_u16(r); u32 hi = rd_u16(r); return lo | hi << 16; }
    i16 rd_i16(Reader* r) { i16 v = rd_u16(r); return v; }
    i32 rd_i32(Reader* r) { i32 v = rd_u32(r); return v; }
    void skip(Reader* r, i64 n) { r.pos += n; if r.pos > r.len { r.bad = true; } }
    str rd_str(Reader* r) {
        i32 n = rd_u16(r);
        if r.pos + n > r.len { r.bad = true; return ""; }
        var s = str_from(r.data + r.pos, n);
        r.pos += n;
        return s;
    }

    struct Cel { int2 pos; int2 size; u8 opacity; u8* pixels; bool linked; }

    i32 mul_un8(i32 a, i32 b) {
        var t = a * b + 0x80;
        return ((t >> 8) + t) >> 8;
    }

    void blend_normal(u8* dst, u8* src, i32 opacity) {
        i32 r = dst[0];
        i32 g = dst[1];
        i32 b = dst[2];
        if dst[3] == 0 {
            r = src[0];
            g = src[1];
            b = src[2];
        }
        else if src[3] != 0 {
            r += mul_un8(src[0] - r, opacity);
            g += mul_un8(src[1] - g, opacity);
            b += mul_un8(src[2] - b, opacity);
        }
        i32 a = dst[3] + mul_un8(src[3] > dst[3] ? src[3] - dst[3] : 0, opacity);
        if a == 0 { r = 0; g = 0; b = 0; }
        dst[0] = cast(u8, r);
        dst[1] = cast(u8, g);
        dst[2] = cast(u8, b);
        dst[3] = cast(u8, a);
    }

    // Decodes a cel's pixels to RGBA8; null if the data is short or corrupt.
    u8* read_cel_pixels(Reader* r, i64 chunk_end, bool compressed, int2 size, i32 bpp, u8[1024]* palette) {
        i32 n = size.x * size.y;
        u8* raw = alloc<u8>(n * bpp);
        defer free(raw);
        if compressed {
            i32 used = 0;
            if zlib_decompress(r.data + r.pos, cast(i32, chunk_end - r.pos), raw, n * bpp, &used) != 0 || used != n * bpp { return null; }
        }
        else {
            if r.pos + n * bpp > chunk_end { return null; }
            memcpy(raw, r.data + r.pos, n * bpp);
        }

        u8* out = alloc<u8>(n * 4);
        for i32 i = 0; i < n; i++ {
            u8* o = out + i * 4;
            if bpp == 4 { memcpy(o, raw + i * 4, 4); }
            else if bpp == 2 {
                o[0] = raw[i * 2];
                o[1] = raw[i * 2];
                o[2] = raw[i * 2];
                o[3] = raw[i * 2 + 1];
            }
            else { memcpy(o, &palette[0][raw[i] * 4], 4); }
        }
        return out;
    }
}

// Visible layers that pass `keep_layer` are flattened. Returns false (and leaves
// `out` empty) on a missing or malformed file.
bool ase_load(Aseprite* out, str path, fn(str): bool keep_layer) {
    *out = Aseprite{};
    var file = file_read(path);
    if file.data == null { return false; }
    var r = Reader{file.data, file.len, 0, false};

    ignore rd_u32(&r);
    if rd_u16(&r) != 0xA5E0 { free(file.data); return false; }
    i32 frame_count = rd_u16(&r);
    i32 width = rd_u16(&r);
    i32 height = rd_u16(&r);
    i32 bpp = rd_u16(&r) / 8;
    r.pos = 128;

    AseLayer[MAX_LAYERS] layers;
    i32 layer_count = 0;
    u8[1024] palette;   // 256 RGBA entries
    var cels = alloc<Cel>(frame_count * MAX_LAYERS);
    memset(cels, 0, frame_count * MAX_LAYERS * sizeof(Cel));
    var pixels = alloc<u8>(frame_count * width * height * 4);
    memset(pixels, 0, frame_count * width * height * 4);
    var durations = alloc<i32>(frame_count);
    AseTag* tags = null;
    i32 tag_count = 0;
    bool has_pivot = false;
    int2 pivot;

    for i32 f = 0; f < frame_count && !r.bad; f++ {
        var frame_start = r.pos;
        var frame_len = rd_u32(&r);
        if rd_u16(&r) != 0xF1FA { r.bad = true; break; }
        i32 old_chunks = rd_u16(&r);
        durations[f] = rd_u16(&r);
        skip(&r, 2);
        i32 chunks = rd_i32(&r);
        if chunks == 0 { chunks = old_chunks; }

        for i32 c = 0; c < chunks && !r.bad; c++ {
            var chunk_start = r.pos;
            var chunk_end = chunk_start + rd_u32(&r);
            switch rd_u16(&r) {
                case 0x2004: {  // layer
                    if layer_count == MAX_LAYERS { break; }
                    var l = &layers[layer_count++];
                    l.flags = rd_u16(&r);
                    skip(&r, 8);    // type, child level, default size
                    ignore rd_u16(&r);  // blend mode: normal only
                    l.opacity = rd_u8(&r);
                    skip(&r, 3);
                    l.name = rd_str(&r);
                }
                case 0x2005: {  // cel
                    i32 li = rd_u16(&r);
                    var cel = Cel{ .pos = int2{rd_i16(&r), rd_i16(&r)}, .opacity = rd_u8(&r) };
                    i32 type = rd_u16(&r);
                    skip(&r, 7);    // z-index, reserved
                    if li >= layer_count { break; }
                    if type == 1 {  // linked
                        i32 lf = rd_u16(&r);
                        if lf < f { cel = cels[lf * MAX_LAYERS + li]; cel.linked = true; }
                    }
                    else if type == 0 || type == 2 {
                        cel.size = int2{rd_u16(&r), rd_u16(&r)};
                        cel.pixels = read_cel_pixels(&r, chunk_end, type == 2, cel.size, bpp, &palette);
                        if cel.pixels == null { r.bad = true; }
                    }
                    cels[f * MAX_LAYERS + li] = cel;
                }
                case 0x2018: {  // tags
                    tag_count = rd_u16(&r);
                    skip(&r, 8);
                    tags = alloc<AseTag>(tag_count);
                    for i32 t = 0; t < tag_count; t++ {
                        tags[t].first = rd_u16(&r);
                        tags[t].last = rd_u16(&r);
                        tags[t].loop = rd_u8(&r);
                        skip(&r, 12);   // repeat, reserved, color
                        tags[t].name = rd_str(&r);
                    }
                }
                case 0x2019: {  // palette
                    ignore rd_u32(&r);
                    i32 first = rd_i32(&r);
                    i32 last = rd_i32(&r);
                    skip(&r, 8);
                    for i32 i = first; i <= last && !r.bad; i++ {
                        var flags = rd_u16(&r);
                        for i32 k = 0; k < 4; k++ {
                            var v = rd_u8(&r);
                            if i < 256 { palette[i * 4 + k] = v; }
                        }
                        if (flags & 1) != 0 { ignore rd_str(&r); }
                    }
                }
                case 0x2022: {  // slice: the first pivot becomes the sprite's pivot
                    var keys = rd_u32(&r);
                    var flags = rd_u32(&r);
                    skip(&r, 4);
                    ignore rd_str(&r);
                    if keys > 0 && (flags & 2) != 0 && !has_pivot {
                        skip(&r, 4);
                        var origin = int2{rd_i32(&r), rd_i32(&r)};
                        skip(&r, 8);
                        if (flags & 1) != 0 { skip(&r, 16); }
                        pivot = origin + int2{rd_i32(&r), rd_i32(&r)};
                        has_pivot = true;
                    }
                }
                default: {}
            }
            r.pos = chunk_end;
        }
        r.pos = frame_start + frame_len;
    }

    if !r.bad {
        for i32 f = 0; f < frame_count; f++ {
            u8* frame = pixels + f * width * height * 4;
            for i32 li = 0; li < layer_count; li++ {
                var layer = &layers[li];
                var cel = &cels[f * MAX_LAYERS + li];
                if (layer.flags & 1) == 0 || cel.pixels == null || !keep_layer(layer.name) { continue; }
                var opacity = mul_un8(cel.opacity, layer.opacity);
                for i32 y = 0; y < cel.size.y; y++ {
                    i32 dy = cel.pos.y + y;
                    if dy < 0 || dy >= height { continue; }
                    for i32 x = 0; x < cel.size.x; x++ {
                        i32 dx = cel.pos.x + x;
                        if dx < 0 || dx >= width { continue; }
                        blend_normal(frame + (dy * width + dx) * 4, cel.pixels + (y * cel.size.x + x) * 4, opacity);
                    }
                }
            }
        }
    }

    for i32 i = 0; i < frame_count * MAX_LAYERS; i++ {
        if !cels[i].linked { free(cels[i].pixels); }
    }
    free(cels);

    *out = Aseprite{width, height, frame_count, pixels, durations, tags, tag_count, has_pivot, pivot, file};
    if r.bad {
        ase_free(out);
        return false;
    }
    return true;
}

void ase_free(Aseprite* a) {
    free(a.pixels);
    free(a.durations);
    free(a.tags);
    free(a.file.data);
    *a = Aseprite{};
}

// The frame `seconds` into a looping tag.
i32 ase_frame_at(Aseprite* a, AseTag tag, f64 seconds) {
    i32 len = tag.last - tag.first + 1;
    bool ping_pong = tag.loop == ASE_PING_PONG || tag.loop == ASE_PING_PONG_REVERSE;
    i32 steps = ping_pong && len > 1 ? len * 2 - 2 : len;
    i32 total = 0;
    for i32 k = 0; k < steps; k++ { total += a.durations[tag_step(tag, len, k)]; }
    if total <= 0 { return tag.first; }

    i64 t = cast(i64, seconds * 1000.0) % total;
    for i32 k = 0; k < steps; k++ {
        var frame = tag_step(tag, len, k);
        t -= a.durations[frame];
        if t < 0 { return frame; }
    }
    return tag.last;
}

private i32 tag_step(AseTag tag, i32 len, i32 k) {
    if k >= len { k = len * 2 - 2 - k; }    // ping-pong return leg
    if tag.loop == ASE_REVERSE || tag.loop == ASE_PING_PONG_REVERSE { k = len - 1 - k; }
    return tag.first + k;
}

// Bounding box of a frame's non-transparent pixels: xy = min, zw = max (exclusive).
int4 ase_visible_bounds(Aseprite* a, i32 frame) {
    var b = int4{a.width, a.height, 0, 0};
    u8* p = a.pixels + frame * a.width * a.height * 4;
    for i32 y = 0; y < a.height; y++ {
        for i32 x = 0; x < a.width; x++ {
            if p[(y * a.width + x) * 4 + 3] == 0 { continue; }
            if x < b.x { b.x = x; }
            if y < b.y { b.y = y; }
            if x >= b.z { b.z = x + 1; }
            if y >= b.w { b.w = y + 1; }
        }
    }
    return b.z == 0 ? int4{0, 0, a.width, a.height} : b;
}
