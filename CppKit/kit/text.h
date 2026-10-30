// Bitmap text: printable ASCII from a TTF, rasterized once by FreeType into an atlas. Glyphs are
// rendered monochrome, so pixel fonts stay crisp at integer scales.
#pragma once
#include "gfx.h"
#include <string_view>

struct Glyph { Rect source; Vector2 offset; float advance = 0; };

struct Font {
    Texture texture;
    Glyph glyphs[96];  // ' '..DEL
    float line_height = 0;
};

std::optional<Font> load_font(const char* path, s32 pixel_size);
// Top-left at `pos`; '\n' starts a new line.
void text(const Font& font, std::string_view s, Vector2 pos, Color color = WHITE);
Vector2 text_size(const Font& font, std::string_view s);
