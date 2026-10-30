// Loads an .ase/.aseprite file into a one-row atlas texture of its frames.
// A cut-down port of Foster's Aseprite.cs: visible layers with normal blending, raw / compressed /
// linked cels, RGBA / grayscale / indexed, tags, the first slice's pivot. Not handled: tilemaps,
// other blend modes, group opacity, cel z-index. Truncated or malformed files fail the load, so
// a sprite caught mid-save can be retried.
#pragma once
#include "gfx.h"
#include <string_view>

struct Tag {
    std::string_view name;
    s32 first = 0, last = 0;
    enum Loop : u8 { Forward, Reverse, PingPong, PingPongReverse } loop = Forward;
};

struct Sprite {
    const char* path = nullptr;
    s64 modtime = 0;
    Texture texture;
    Vector2 frame_size;
    std::span<s32> durations;   // ms
    std::span<Tag> tags;
    Tag animation;              // the first tag, or every frame
    Vector2 origin;             // the first slice's pivot, else the bottom center of frame 0's opaque pixels
    Arena memory;               // the path, durations and tags

    Tag find_tag(std::string_view name) const;
    // The frame `seconds` into `tag`; a non-looping tag holds its last frame.
    s32 frame_at(const Tag& tag, double seconds, bool looping = true) const;
    // Seconds to play `tag` through once.
    float duration(const Tag& tag) const;
};

std::optional<Sprite> load_sprite(const char* path);
void free_sprite(Sprite& s);
// Reloads the sprite if its file changed; keeps the old one if the new one doesn't load.
void reload_if_changed(Sprite& s);
s64 file_modtime(const char* path);

// The frame `seconds` into `tag`, with the sprite's origin at `pos`, scaled about it (a negative
// x flips it).
void draw_sprite(const Sprite& s, const Tag& tag, double seconds, Vector2 pos, Vector2 scale = {1, 1}, bool looping = true);
// One frame by index, its top-left at `pos` (ignoring the origin), tinted.
void draw_frame(const Sprite& s, s32 frame, Vector2 pos, Color color = WHITE);
