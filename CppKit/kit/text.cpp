#include "text.h"
#include <ft2build.h>
#include FT_FREETYPE_H

std::optional<Font> load_font(const char* path, s32 pixel_size) {
    FT_Library library;
    FT_Face face;
    if (FT_Init_FreeType(&library)) return {};
    if (FT_New_Face(library, path, 0, &face)) { FT_Done_FreeType(library); return {}; }
    FT_Set_Pixel_Sizes(face, 0, pixel_size);

    Font font;
    font.line_height = float(face->size->metrics.height >> 6);
    float ascender = float(face->size->metrics.ascender >> 6);
    s32 cell = pixel_size * 2, w = cell * 16, h = cell * 6;
    auto pixels = temp.array<Color>(size_t(w) * h);
    memset(pixels.data(), 0, pixels.size_bytes());

    for (s32 i = 0; i < 96; i++) {
        if (FT_Load_Char(face, i + 32, FT_LOAD_RENDER | FT_LOAD_MONOCHROME)) continue;
        FT_GlyphSlot slot = face->glyph;
        const FT_Bitmap& bitmap = slot->bitmap;
        s32 x0 = (i % 16) * cell, y0 = (i / 16) * cell;
        s32 gw = min((s32)bitmap.width, cell), gh = min((s32)bitmap.rows, cell);
        for (s32 y = 0; y < gh; y++) for (s32 x = 0; x < gw; x++)
            if ((bitmap.buffer[y * bitmap.pitch + x / 8] >> (7 - x % 8)) & 1) pixels[size_t(y0 + y) * w + x0 + x] = WHITE;
        font.glyphs[i] = {{{(float)x0, (float)y0}, {(float)gw, (float)gh}}, {(float)slot->bitmap_left, ascender - slot->bitmap_top}, float(slot->advance.x >> 6)};
    }
    FT_Done_Face(face);
    FT_Done_FreeType(library);
    font.texture = make_texture(pixels.data(), w, h);
    return font;
}

static const Glyph& glyph(const Font& font, char c) {
    if (c == '\t') c = ' ';
    if (c < 32 || c > 127) c = '?';
    return font.glyphs[c - 32];
}

void text(const Font& font, std::string_view s, Vector2 pos, Color color) {
    Vector2 pen = pos;
    for (char c : s) {
        if (c == '\n') { pen = {pos.x, pen.y + font.line_height}; continue; }
        const Glyph& g = glyph(font, c);
        if (g.source.size.x) image(font.texture, g.source, pen + g.offset, color);
        pen.x += g.advance;
    }
}

Vector2 text_size(const Font& font, std::string_view s) {
    float width = 0, line = 0;
    s32 lines = 1;
    for (char c : s) {
        if (c == '\n') { width = max(width, line); line = 0; lines++; continue; }
        line += glyph(font, c).advance;
    }
    return {max(width, line), lines * font.line_height};
}
