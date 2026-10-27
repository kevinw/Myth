// Loads an .ase/.aseprite file into a one-row atlas texture of its frames.
// A cut-down port of Foster's Aseprite.cs (by way of SokobanMinc/aseprite.mc): visible
// layers with normal blending, raw / compressed / linked cels, RGBA / grayscale / indexed, tags,
// the first slice's pivot. Not handled: tilemaps, other blend modes, group opacity, cel z-index.
// Truncated or malformed files fail the load, so a sprite caught mid-save can be retried.

Sprite :: struct {
    path: string;
    modtime: Apollo_Time;
    texture: Texture;
    frame_size: Vector2;
    durations: [] s32;  // ms
    tags: [] Tag;
    animation: Tag;     // the first tag, or every frame
    origin: Vector2;    // the first slice's pivot, else the bottom center of frame 0's opaque pixels
}

Tag :: struct {
    name: string;
    first, last: s32;
    loop: enum u8 { FORWARD; REVERSE; PING_PONG; PING_PONG_REVERSE; };
}

load_sprite :: (path: string) -> Sprite, bool {
    modtime := file_modtime_and_size(path);
    file, ok := read_entire_file(path, log_errors = false);
    if !ok return .{}, false;
    defer free(file);
    r := Reader.{data = xx file};

    read(*r, u32);
    if read(*r, u16) != 0xA5E0 return .{}, false;
    frame_count := cast(s32) read(*r, u16);
    w := cast(s32) read(*r, u16);
    h := cast(s32) read(*r, u16);
    bpp := cast(s32) read(*r, u16) / 8;
    r.pos = 128;

    layers: [MAX_LAYERS] Layer;
    layer_count: s32;
    palette: [1024] u8;  // 256 RGBA entries
    cels := NewArray(frame_count * MAX_LAYERS, Cel);
    defer {
        for cels if !it.linked free(it.pixels);
        array_free(cels);
    }
    sprite := Sprite.{path = copy_string(path), modtime = modtime, frame_size = .{xx w, xx h}};
    sprite.durations = NewArray(frame_count, s32);
    sprite.animation = .{"", 0, frame_count - 1, .FORWARD};
    tags: [..] Tag;
    has_pivot := false;

    for f: 0..frame_count-1 {
        if r.bad break;
        frame_start := r.pos;
        frame_end := frame_start + read(*r, u32);
        if read(*r, u16) != 0xF1FA { r.bad = true; break; }
        old_chunks := read(*r, u16);
        sprite.durations[f] = read(*r, u16);
        r.pos += 2;
        chunks := read(*r, u32);
        if !chunks chunks = old_chunks;

        for 1..chunks {
            if r.bad break;
            chunk_end := r.pos + read(*r, u32);
            if read(*r, u16) == {
              case 0x2004;  // layer
                if layer_count == MAX_LAYERS continue;
                l := *layers[layer_count];
                layer_count += 1;
                l.flags = read(*r, u16);
                r.pos += 10;  // type, child level, default size, blend mode (normal only)
                l.opacity = read(*r, u8);
                r.pos += 3;
                l.name = read_string(*r);

              case 0x2005;  // cel
                li := read(*r, u16);
                cel := Cel.{x = read(*r, s16), y = read(*r, s16), opacity = read(*r, u8)};
                type := read(*r, u16);
                r.pos += 7;  // z-index, reserved
                if li >= layer_count continue;
                if type == 1 {  // linked
                    lf := read(*r, u16);
                    if lf < f { cel = cels[lf * MAX_LAYERS + li]; cel.linked = true; }
                } else if type == 0 || type == 2 {
                    cel.w = read(*r, u16);
                    cel.h = read(*r, u16);
                    cel.pixels = read_cel_pixels(*r, chunk_end, type == 2, cel.w * cel.h, bpp, *palette);
                    if !cel.pixels r.bad = true;
                }
                cels[f * MAX_LAYERS + li] = cel;

              case 0x2018;  // tags
                count := read(*r, u16);
                r.pos += 8;
                for 1..count {
                    tag: Tag;
                    tag.first = read(*r, u16);
                    tag.last = read(*r, u16);
                    tag.loop = xx read(*r, u8);
                    r.pos += 12;  // repeat, reserved, color
                    tag.name = copy_string(read_string(*r));
                    array_add(*tags, tag);
                }

              case 0x2022;  // slice: the first one with a pivot sets the origin
                keys := read(*r, u32);
                flags := read(*r, u32);
                r.pos += 4;
                read_string(*r);
                if keys && flags & 2 && !has_pivot {
                    r.pos += 4;  // frame
                    origin := Vector2.{xx read(*r, s32), xx read(*r, s32)};
                    r.pos += 8;  // size
                    if flags & 1 r.pos += 16;  // nine-patch center
                    sprite.origin = origin + Vector2.{xx read(*r, s32), xx read(*r, s32)};
                    has_pivot = true;
                }

              case 0x2019;  // palette
                read(*r, u32);
                first := read(*r, s32);
                last := read(*r, s32);
                r.pos += 8;
                for i: first..last {
                    if r.bad break;
                    flags := read(*r, u16);
                    for k: 0..3 {
                        v := read(*r, u8);
                        if i < 256 palette[i * 4 + k] = v;
                    }
                    if flags & 1 read_string(*r);
                }
            }
            r.pos = chunk_end;
        }
        r.pos = frame_end;
    }
    if r.bad {
        array_free(sprite.durations);
        free(sprite.path);
        return .{}, false;
    }

    // Composite each frame into its slot in the atlas.
    stride := w * frame_count;
    atlas := cast(*u8) alloc(stride * h * 4);
    defer free(atlas);
    memset(atlas, 0, stride * h * 4);
    for f: 0..frame_count-1 for layer, li: layers {
        if li >= layer_count break;
        cel := *cels[f * MAX_LAYERS + li];
        if !(layer.flags & 1) || !cel.pixels || contains(layer.name, "debug") || ends_with(layer.name, "_socket") continue;
        opacity := mul_un8(cel.opacity, layer.opacity);
        for y: 0..cel.h-1 for x: 0..cel.w-1 {
            dx := cel.x + x;
            dy := cel.y + y;
            if dx < 0 || dx >= w || dy < 0 || dy >= h continue;
            blend_normal(atlas + (dy * stride + f * w + dx) * 4, cel.pixels + (y * cel.w + x) * 4, opacity);
        }
    }

    sprite.tags = tags;
    if tags sprite.animation = tags[0];
    if !has_pivot {  // bottom center of frame 0's opaque pixels
        x0, x1, y1 := w, 0, 0;
        for y: 0..h-1 for x: 0..w-1 if atlas[(y * stride + x) * 4 + 3] {
            x0 = min(x0, x);
            x1 = max(x1, x + 1);
            y1 = max(y1, y + 1);
        }
        sprite.origin = ifx x1 then Vector2.{floor((x0 + x1) * 0.5 + 0.5), xx y1} else Vector2.{w * 0.5, xx h};
    }

    for i: 0..stride * h - 1 {
        p := atlas + i * 4;
        for 0..2 p[it] = xx mul_un8(p[it], p[3]);  // premultiply, like Foster
    }
    sprite.texture = make_texture(atlas, stride, h);
    return sprite, true;
}

free_sprite :: (s: *Sprite) {
    release_texture(s.texture);
    array_free(s.durations);
    for s.tags free(it.name);
    array_free(s.tags);
    free(s.path);
    s.* = .{};
}

// Reloads the sprite if its file changed; keeps the old one if the new one doesn't load.
reload_if_changed :: (s: *Sprite) {
    if file_modtime_and_size(s.path) == s.modtime return;
    fresh, ok := load_sprite(s.path);
    if !ok return;
    free_sprite(s);
    s.* = fresh;
}

// Draws the frame `seconds` into the looping default animation, with its origin at `pos`.
draw_sprite :: (s: Sprite, pos: Vector2, seconds: float64) {
    draw_sprite(s, s.animation, seconds, pos);
}

// Draws the frame `seconds` into `tag`, with the sprite's origin at `pos`, scaled about it
// (a negative x flips it). A non-looping tag holds its last frame.
draw_sprite :: (s: Sprite, tag: Tag, seconds: float64, pos: Vector2, scale := Vector2.{1, 1}, looping := true) {
    source := Rect.{.{frame_at(s, tag, seconds, looping) * s.frame_size.x, 0}, s.frame_size};
    push_matrix(pos, scale);
    image(s.texture, source, -s.origin);
    pop_matrix();
}

// Seconds to play `tag` through once.
tag_duration :: (s: Sprite, tag: Tag) -> float {
    ms: s32;
    for tag.first..tag.last ms += s.durations[it];
    return ms / 1000.0;
}

// One frame by index, its top-left at `pos` (ignoring the origin), tinted.
draw_frame :: (s: Sprite, frame: s32, pos: Vector2, color := WHITE) {
    image(s.texture, .{.{frame * s.frame_size.x, 0}, s.frame_size}, pos, color);
}

find_tag :: (s: Sprite, name: string) -> Tag, bool {
    for s.tags if it.name == name return it, true;
    return s.animation, false;
}

#scope_file

MAX_LAYERS :: 64;

Layer :: struct { name: string; flags: u16; opacity: u8; }
Cel :: struct { x, y, w, h: s32; opacity: u8; pixels: *u8; linked: bool; }
Reader :: struct { data: [] u8; pos: s64; bad: bool; }

read :: (r: *Reader, $T: Type) -> T {
    if r.pos < 0 || r.pos + size_of(T) > r.data.count { r.bad = true; return 0; }
    defer r.pos += size_of(T);
    return (cast(*T) *r.data[r.pos]).*;
}

// Points into the file.
read_string :: (r: *Reader) -> string {
    n := read(r, u16);
    if r.pos + n > r.data.count { r.bad = true; return ""; }
    defer r.pos += n;
    return .{n, *r.data[r.pos]};
}

// Decodes a cel's pixels to RGBA8; null if the data is short or corrupt.
read_cel_pixels :: (r: *Reader, chunk_end: s64, compressed: bool, n: s32, bpp: s32, palette: *[1024] u8) -> *u8 {
    raw := *r.data[r.pos];
    if compressed {
        len: s32;
        raw = stbi_zlib_decode_malloc(raw, xx (chunk_end - r.pos), *len);
        if !raw || len != n * bpp { stbi_image_free(raw); return null; }
    } else if r.pos + n * bpp > chunk_end return null;
    defer if compressed stbi_image_free(raw);

    out := cast(*u8) alloc(n * 4);
    for i: 0..n-1 {
        o := out + i * 4;
        if bpp == {
          case 4; memcpy(o, raw + i * 4, 4);
          case 2; o[0], o[1], o[2], o[3] = raw[i * 2], raw[i * 2], raw[i * 2], raw[i * 2 + 1];
          case;   memcpy(o, *palette.*[raw[i] * 4], 4);
        }
    }
    return out;
}

mul_un8 :: (a: s32, b: s32) -> s32 {
    t := a * b + 0x80;
    return ((t >> 8) + t) >> 8;
}

blend_normal :: (dst: *u8, src: *u8, opacity: s32) {
    r, g, b: s32 = dst[0], dst[1], dst[2];
    if !dst[3] {
        r, g, b = src[0], src[1], src[2];
    } else if src[3] {
        r += mul_un8(src[0] - r, opacity);
        g += mul_un8(src[1] - g, opacity);
        b += mul_un8(src[2] - b, opacity);
    }
    a := dst[3] + mul_un8(ifx src[3] > dst[3] then src[3] - dst[3] else 0, opacity);
    if !a r, g, b = 0, 0, 0;
    dst[0], dst[1], dst[2], dst[3] = xx r, xx g, xx b, xx a;
}

frame_at :: (s: Sprite, tag: Tag, seconds: float64, looping: bool) -> s32 {
    len := tag.last - tag.first + 1;
    ping_pong := tag.loop == .PING_PONG || tag.loop == .PING_PONG_REVERSE;
    steps := ifx ping_pong && len > 1 then len * 2 - 2 else len;
    total: s64;
    for k: 0..steps-1 total += s.durations[tag_step(tag, len, k)];
    if total <= 0 return tag.first;

    t := cast(s64) (seconds * 1000);
    if !looping && t >= total return tag_step(tag, len, steps - 1);
    t %= total;
    for k: 0..steps-1 {
        frame := tag_step(tag, len, k);
        t -= s.durations[frame];
        if t < 0 return frame;
    }
    return tag.last;
}

tag_step :: (tag: Tag, len: s32, k: s32) -> s32 {
    if k >= len k = len * 2 - 2 - k;  // ping-pong return leg
    if tag.loop == .REVERSE || tag.loop == .PING_PONG_REVERSE k = len - 1 - k;
    return tag.first + k;
}

#import "Basic";
#import "File";
#import "File_Utilities";
#import "Math";
#import "String";
#import "stb_image";
