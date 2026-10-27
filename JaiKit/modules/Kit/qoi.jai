// QOI image decoding (https://qoiformat.org): to RGBA8, straight alpha.

decode_qoi :: (data: [] u8) -> pixels: [] u8, width: s32, height: s32, ok: bool {
    if data.count < 22 || data[0] != #char "q" || data[1] != #char "o" || data[2] != #char "i" || data[3] != #char "f" return .[], 0, 0, false;
    be32 :: (p: *u8) -> s32 { return ((cast(s32) p[0]) << 24) | ((cast(s32) p[1]) << 16) | ((cast(s32) p[2]) << 8) | p[3]; }
    w, h := be32(data.data + 4), be32(data.data + 8);
    if w <= 0 || h <= 0 return .[], 0, 0, false;

    pixels := NewArray(w * h * 4, u8);
    seen: [64] [4] u8;
    px: [4] u8 = .[0, 0, 0, 255];
    p := 14;
    end := data.count - 8;  // the stream ends with 7 zero bytes and a 1
    run := 0;
    for i: 0..w * h - 1 {
        if run > 0 {
            run -= 1;
        } else if p < end {
            b := data[p];
            p += 1;
            if b == 0xfe {          // RGB
                px[0], px[1], px[2] = data[p], data[p + 1], data[p + 2];
                p += 3;
            } else if b == 0xff {   // RGBA
                px[0], px[1], px[2], px[3] = data[p], data[p + 1], data[p + 2], data[p + 3];
                p += 4;
            } else if b >> 6 == 0 { // INDEX
                px = seen[b];
            } else if b >> 6 == 1 { // DIFF
                px[0] = wrap(px[0] + cast(s32) ((b >> 4) & 3) - 2);
                px[1] = wrap(px[1] + cast(s32) ((b >> 2) & 3) - 2);
                px[2] = wrap(px[2] + cast(s32) (b & 3) - 2);
            } else if b >> 6 == 2 { // LUMA
                b2 := data[p];
                p += 1;
                dg := cast(s32) (b & 0x3f) - 32;
                px[0] = wrap(px[0] + dg - 8 + ((b2 >> 4) & 0xf));
                px[1] = wrap(px[1] + dg);
                px[2] = wrap(px[2] + dg - 8 + (b2 & 0xf));
            } else {                // RUN
                run = b & 0x3f;
            }
            seen[(px[0] * 3 + px[1] * 5 + px[2] * 7 + px[3] * 11) % 64] = px;
        }
        memcpy(pixels.data + i * 4, px.data, 4);
    }
    return pixels, w, h, true;
}

#scope_file

wrap :: (v: s32) -> u8 { return cast,no_check(u8) (v & 0xff); }

#import "Basic";
