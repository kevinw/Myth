// Bitmap text: printable ASCII from a TTF, rasterized once by freetype into an atlas.
// Glyphs are rendered monochrome, so pixel fonts stay crisp at integer scales.

Font :: struct {
    texture: Texture;
    glyphs: [96] Glyph;  // ' '..DEL
    line_height: float;
}

Glyph :: struct { source: Rect; offset: Vector2; advance: float; }

load_font :: (path: string, pixel_size: s32) -> Font, bool {
    library: FT_Library;
    face: FT_Face;
    if FT_Init_FreeType(*library) return .{}, false;
    defer FT_Done_FreeType(library);
    if FT_New_Face(library, temp_c_string(path), 0, *face) return .{}, false;
    defer FT_Done_Face(face);
    FT_Set_Pixel_Sizes(face, 0, xx pixel_size);

    font: Font;
    font.line_height = xx (face.size.metrics.height >> 6);
    ascender := cast(float) (face.size.metrics.ascender >> 6);
    cell := pixel_size * 2;
    w, h := cell * 16, cell * 6;
    pixels := NewArray(w * h * 4, u8);
    defer array_free(pixels);

    for *font.glyphs {
        if FT_Load_Char(face, xx (it_index + 32), FT_LOAD_RENDER | FT_LOAD_MONOCHROME) continue;
        slot := face.glyph;
        bitmap := slot.bitmap;
        x0, y0 := (it_index % 16) * cell, (it_index / 16) * cell;
        size := Vector2.{xx min(bitmap.width, xx cell), xx min(bitmap.rows, xx cell)};
        for y: 0..cast(s32) size.y - 1 for x: 0..cast(s32) size.x - 1 {
            on := (bitmap.buffer[y * bitmap.pitch + x / 8] >> (7 - x % 8)) & 1;
            if on memset(pixels.data + ((y0 + y) * w + x0 + x) * 4, 255, 4);
        }
        it.* = .{.{.{xx x0, xx y0}, size}, .{xx slot.bitmap_left, ascender - slot.bitmap_top}, xx (slot.advance.x >> 6)};
    }
    font.texture = make_texture(pixels.data, w, h);
    return font, true;
}

// Top-left at `pos`; '\n' starts a new line.
text :: (font: Font, s: string, pos: Vector2, color := WHITE) {
    pen := pos;
    for 0..s.count-1 {
        c := s[it];
        if c == #char "\n" { pen = .{pos.x, pen.y + font.line_height}; continue; }
        if c == #char "\t" c = #char " ";
        if c < 32 || c > 127 c = #char "?";
        g := font.glyphs[c - 32];
        if g.source.size.x image(font.texture, g.source, pen + g.offset, color);
        pen.x += g.advance;
    }
}

text_size :: (font: Font, s: string) -> Vector2 {
    width, line, lines := 0.0, 0.0, 1;
    for 0..s.count-1 {
        c := s[it];
        if c == #char "\n" { width = max(width, line); line = 0; lines += 1; continue; }
        line += font.glyphs[ifx c >= 32 && c <= 127 then c - 32 else #char "?" - 32].advance;
    }
    return .{max(width, line), lines * font.line_height};
}

#scope_file

#import "Basic";
#import "freetype";
#import "Math";
