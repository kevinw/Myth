#include "Common.hlsl"

// Uncomment ONE; leave all commented for the original CRT.
// #define CRT_PHOSPHOR_DOTS
#define DOT_RADIUS 0.43             // 0.2..0.65, in source pixels
#define DOT_MASK 0.24               // 0..1, darkness between phosphors
#define DOT_WARMTH 0.0              // 0..2
#define DOT_HALO 0.245              // 0..0.3
#define DOT_RGB_SPLIT 0.9           // multiplier on the existing RGB offset

// #define CRT_ALIEN
#define ALIEN_CELL_SIZE 0.7         // 1..4 source pixels per aperture
#define ALIEN_CURL 5.5              // 0..6, twist of the three-lobed aperture
#define ALIEN_MEMBRANE 0.65         // 0..1, aperture contrast
#define ALIEN_SPECTRUM 0.05         // 0..1, spectral color replacement
#define ALIEN_AURA 0.12             // 0..0.5

// #define PIXEL_GLAZE
#define GLAZE_BEVEL 0.22            // 0..0.8
#define GLAZE_SEAM 0.24             // 0..0.8
#define GLAZE_INSET 0.07            // 0.01..0.2, seam width in source pixels
#define GLAZE_SHEEN 2.0             // 0..3
#define GLAZE_LIGHT float2(0.65, 0.35) // signed XY lighting weights

// #define PIXEL_TAPESTRY
#define THREAD_RELIEF 0.15          // 0..1, raised stitches
#define THREAD_WIDTH 0.14           // 0.1..0.4 source pixels
#define THREAD_LUSTER 0.35          // 0..1, silk highlights

#define FILTER_TILE_VARIATION 0.65  // 0..1; 0 gives a uniform surface
// Ceiling on the chroma fringe as a fraction of an art pixel, so a small window (where an art
// pixel is only a couple of output pixels) can never turn misconvergence back into a bar. Kept
// under half a frame pixel: at exactly half, a nearest tap at a pixel centre lands on the texel
// boundary and snaps a whole pixel anyway.
#define CHROMA_MAX_ART_PIXEL 0.2

cbuffer VertexUniformBlock : register(b0, space1)
{
    float4x4 Matrix;
};

cbuffer FragmentUniformBlock : register(b0, space3)
{
    float2 TextureSize;
    float FlashGlowAmount;
    float VignetteAmount;
    float RgbOffset;
    float FlashAmount; // fx.blooming: zero except during the one-shot screen flash
    float FlashGlowThreshold;
    float ScanlineStrength;
    float Zoom;
    float PixelScale;   // output pixels per frame pixel
    float Scaler;       // PixelScaler: 0 none, 1 Scale2x, 2 Super Eagle, 3 MMPX, 4 xBR
    float ScalerTolerance;
    float CrtEffects;   // 0 runs the scaler alone, without scanlines, bloom, fringe or vignette
};

Texture2D Texture : register(t0, space2);
SamplerState Sampler : register(s0, space2);
Texture2D TileFieldTexture : register(t1, space2);
SamplerState TileFieldSampler : register(s1, space2);

struct VsInput
{
    float2 Position : TEXCOORD0;
    float2 TexCoord : TEXCOORD1;
    float4 Color : TEXCOORD2;
    float4 Type : TEXCOORD4;
};

struct VsOutput
{
    float2 TexCoord : TEXCOORD0;
    float4 Color : TEXCOORD1;
    float4 Type : TEXCOORD4;
    float4 Position : SV_Position;
};

VsOutput vertex_main(VsInput input)
{
    VsOutput output;
    output.TexCoord = input.TexCoord;
    output.Color = input.Color;
    output.Type = input.Type;
    output.Position = mul(Matrix, float4(input.Position, 0.0, 1.0));
    return output;
}

float3 sample_color(float2 uv)
{
    return Texture.Sample(Sampler, texture_uv(saturate(uv))).rgb;
}

// ---- Pixel-art scalers -------------------------------------------------------------------------
// Each evaluates the magnified image at one output pixel: p is the art texel it falls in, q its
// position inside that texel (0..1). The 2x rules pick the quadrant q lands in. Colors here are
// lit, so "same color" is a luma-weighted distance under ScalerTolerance, not exact equality.

#define T(x, y) sample_color((p + float2(x, y) + 0.5) * texel)

float luma(float3 c) { return dot(c, float3(0.299, 0.587, 0.114)); }
bool same(float3 a, float3 b) { return dot(abs(a - b), float3(0.299, 0.587, 0.114)) <= ScalerTolerance; }
float3 half_mix(float3 a, float3 b) { return (a + b) * 0.5; }
float3 quadrant(float2 q, float3 topLeft, float3 topRight, float3 bottomLeft, float3 bottomRight)
{
    return q.y < 0.5 ? (q.x < 0.5 ? topLeft : topRight) : (q.x < 0.5 ? bottomLeft : bottomRight);
}

// Scale2x / EPX: rounds 45° corners, never invents a color.
float3 scale2x(float2 p, float2 q, float2 texel)
{
    float3 B = T(0, -1), D = T(-1, 0), E = T(0, 0), F = T(1, 0), H = T(0, 1);
    if (same(B, H) || same(D, F))
        return E;
    float3 vertical = q.y < 0.5 ? B : H;
    float3 horizontal = q.x < 0.5 ? D : F;
    return same(vertical, horizontal) ? horizontal : E;
}

int eagle_vote(float3 a, float3 b, float3 c, float3 d)
{
    int x = 0, y = 0, r = 0;
    if (same(a, c)) x++; else if (same(b, c)) y++;
    if (same(a, d)) x++; else if (same(b, d)) y++;
    if (x <= 1) r++;
    if (y <= 1) r--;
    return r;
}

// Super Eagle (Kreed's 2xSaI family): the 2x2 block for a texel is built from it and its right,
// lower and diagonal neighbors, blending in-between colors.
float3 super_eagle(float2 p, float2 q, float2 texel)
{
    float3 b1 = T(0, -1), b2 = T(1, -1);
    float3 c4 = T(-1, 0), c5 = T(0, 0), c6 = T(1, 0), s2 = T(2, 0);
    float3 c1 = T(-1, 1), c2 = T(0, 1), c3 = T(1, 1), s1 = T(2, 1);
    float3 a1 = T(0, 2), a2 = T(1, 2);
    float3 p1a, p1b, p2a, p2b;
    if (same(c2, c6) && !same(c5, c3))
    {
        p1b = c2; p2a = c2;
        p1a = same(c1, c2) || same(c6, b2) ? half_mix(c2, half_mix(c2, c5)) : half_mix(c5, c6);
        p2b = same(c6, s2) || same(c2, a1) ? half_mix(c2, half_mix(c2, c3)) : half_mix(c2, c3);
    }
    else if (same(c5, c3) && !same(c2, c6))
    {
        p2b = c5; p1a = c5;
        p1b = same(b1, c5) || same(c3, s1) ? half_mix(c5, half_mix(c5, c6)) : half_mix(c5, c6);
        p2a = same(c3, a2) || same(c4, c5) ? half_mix(c5, half_mix(c5, c2)) : half_mix(c2, c3);
    }
    else if (same(c5, c3) && same(c2, c6))
    {
        int r = eagle_vote(c6, c5, c1, a1) + eagle_vote(c6, c5, c4, b1) + eagle_vote(c6, c5, a2, s1) + eagle_vote(c6, c5, b2, s2);
        if (r > 0) { p1b = c2; p2a = c2; p1a = half_mix(c5, c6); p2b = p1a; }
        else if (r < 0) { p2b = c5; p1a = c5; p1b = half_mix(c5, c6); p2a = p1b; }
        else { p2b = c5; p1a = c5; p1b = c2; p2a = c2; }
    }
    else
    {
        float3 cross1 = half_mix(c2, c6);
        float3 cross2 = half_mix(c5, c3);
        p2b = (c3 * 3.0 + cross1) * 0.25;
        p1a = (c5 * 3.0 + cross1) * 0.25;
        p2a = (c2 * 3.0 + cross2) * 0.25;
        p1b = (c6 * 3.0 + cross2) * 0.25;
    }
    return quadrant(q, p1a, p1b, p2a, p2b);
}

bool all_eq2(float3 b, float3 a0, float3 a1) { return same(b, a0) && same(b, a1); }
bool all_eq3(float3 b, float3 a0, float3 a1, float3 a2) { return same(b, a0) && same(b, a1) && same(b, a2); }
bool all_eq4(float3 b, float3 a0, float3 a1, float3 a2, float3 a3) { return all_eq2(b, a0, a1) && all_eq2(b, a2, a3); }
bool any_eq3(float3 b, float3 a0, float3 a1, float3 a2) { return same(b, a0) || same(b, a1) || same(b, a2); }
bool none_eq2(float3 b, float3 a0, float3 a1) { return !same(b, a0) && !same(b, a1); }
bool none_eq4(float3 b, float3 a0, float3 a1, float3 a2, float3 a3) { return none_eq2(b, a0, a1) && none_eq2(b, a2, a3); }

// MMPX (McGuire & Gagiu 2021): 2x magnification for pixel-art games; never invents a color and
// tries to keep 1px lines, text and deliberate patterns intact.
float3 mmpx(float2 p, float2 q, float2 texel)
{
    float3 A = T(-1, -1), B = T(0, -1), C = T(1, -1);
    float3 D = T(-1, 0), E = T(0, 0), F = T(1, 0);
    float3 G = T(-1, 1), H = T(0, 1), I = T(1, 1);
    float3 J = E, K = E, L = E, M = E;
    if (all_eq4(E, A, B, C, D) && all_eq4(E, F, G, H, I))
        return E;

    float3 P = T(0, -2), Q = T(-2, 0), R = T(2, 0), S = T(0, 2);
    float Bl = luma(B), Dl = luma(D), El = luma(E), Fl = luma(F), Hl = luma(H);

    // 1:1 slopes
    if (same(D, B) && !same(D, H) && !same(D, F) && (El >= Dl || same(E, A)) && any_eq3(E, A, C, G) &&
        (El < Dl || !same(A, D) || !same(E, P) || !same(E, Q))) J = D;
    if (same(B, F) && !same(B, D) && !same(B, H) && (El >= Bl || same(E, C)) && any_eq3(E, A, C, I) &&
        (El < Bl || !same(C, B) || !same(E, P) || !same(E, R))) K = B;
    if (same(H, D) && !same(H, F) && !same(H, B) && (El >= Hl || same(E, G)) && any_eq3(E, A, G, I) &&
        (El < Hl || !same(G, H) || !same(E, S) || !same(E, Q))) L = H;
    if (same(F, H) && !same(F, B) && !same(F, D) && (El >= Fl || same(E, I)) && any_eq3(E, C, G, I) &&
        (El < Fl || !same(I, H) || !same(E, R) || !same(E, S))) M = F;

    // Intersections
    if (!same(E, F) && all_eq4(E, C, I, D, Q) && all_eq2(F, B, H) && !same(F, T(3, 0))) { K = F; M = F; }
    if (!same(E, D) && all_eq4(E, A, G, F, R) && all_eq2(D, B, H) && !same(D, T(-3, 0))) { J = D; L = D; }
    if (!same(E, H) && all_eq4(E, G, I, B, P) && all_eq2(H, D, F) && !same(H, T(0, 3))) { L = H; M = H; }
    if (!same(E, B) && all_eq4(E, A, C, H, S) && all_eq2(B, D, F) && !same(B, T(0, -3))) { J = B; K = B; }

    // Triangle tips
    if (Bl < El && all_eq4(E, G, H, I, S) && none_eq4(E, A, D, C, F)) { J = B; K = B; }
    if (Hl < El && all_eq4(E, A, B, C, P) && none_eq4(E, D, G, I, F)) { L = H; M = H; }
    if (Fl < El && all_eq4(E, A, D, G, Q) && none_eq4(E, B, C, I, H)) { K = F; M = F; }
    if (Dl < El && all_eq4(E, C, F, I, R) && none_eq4(E, B, A, G, H)) { J = D; L = D; }

    // 2:1 slopes
    if (!same(H, B))
    {
        if (!same(H, A) && !same(H, E) && !same(H, C))
        {
            if (all_eq3(H, G, F, R) && none_eq2(H, D, T(2, -1))) L = M;
            if (all_eq3(H, I, D, Q) && none_eq2(H, F, T(-2, -1))) M = L;
        }
        if (!same(B, I) && !same(B, G) && !same(B, E))
        {
            if (all_eq3(B, A, F, R) && none_eq2(B, D, T(2, 1))) J = K;
            if (all_eq3(B, C, D, Q) && none_eq2(B, F, T(-2, 1))) K = J;
        }
    }
    if (!same(F, D))
    {
        if (!same(D, I) && !same(D, E) && !same(D, C))
        {
            if (all_eq3(D, A, H, S) && none_eq2(D, B, T(1, 2))) J = L;
            if (all_eq3(D, G, B, P) && none_eq2(D, H, T(1, -2))) L = J;
        }
        if (!same(F, E) && !same(F, A) && !same(F, G))
        {
            if (all_eq3(F, C, H, S) && none_eq2(F, B, T(-1, 2))) K = M;
            if (all_eq3(F, I, B, P) && none_eq2(F, H, T(-1, -2))) M = K;
        }
    }
    return quadrant(q, J, K, L, M);
}

float4 eq4(float4 a, float4 b) { return step(abs(a - b), ScalerTolerance); }
float4 ne4(float4 a, float4 b) { return 1.0 - eq4(a, b); }
float c_df(float3 a, float3 b) { return dot(abs(a - b), 1.0); }

// xBR level 2 (Hyllian): per corner direction (x bottom-right, y top-right, z top-left, w
// bottom-left), detect 30°/45°/60° edges and cut the corner along them. Works at any scale;
// delta is the anti-aliased width of the cut, in texels.
float3 xbr(float2 p, float2 q, float2 texel, float delta)
{
    float3 A1 = T(-1, -2), B1 = T(0, -2), C1 = T(1, -2);
    float3 A0 = T(-2, -1), A = T(-1, -1), B = T(0, -1), C = T(1, -1), C4 = T(2, -1);
    float3 D0 = T(-2, 0), D = T(-1, 0), E = T(0, 0), F = T(1, 0), F4 = T(2, 0);
    float3 G0 = T(-2, 1), G = T(-1, 1), H = T(0, 1), I = T(1, 1), I4 = T(2, 1);
    float3 G5 = T(-1, 2), H5 = T(0, 2), I5 = T(1, 2);

    float4 b = float4(luma(B), luma(D), luma(H), luma(F));
    float4 c = float4(luma(C), luma(A), luma(G), luma(I));
    float4 e = luma(E).xxxx;
    float4 d = b.yzwx, f = b.wxyz, g = c.zwxy, h = b.zwxy, i = c.wxyz;
    float4 i4 = float4(luma(I4), luma(C1), luma(A0), luma(G5));
    float4 i5 = float4(luma(I5), luma(C4), luma(A1), luma(G0));
    float4 h5 = float4(luma(H5), luma(F4), luma(B1), luma(D0));
    float4 f4 = h5.yzwx;

    const float4 Ao = float4(1.0, -1.0, -1.0, 1.0);
    const float4 Bo = float4(1.0, 1.0, -1.0, -1.0);
    const float4 Co = float4(1.5, 0.5, -0.5, 0.5);
    const float4 Bx = float4(0.5, 2.0, -0.5, -2.0);
    const float4 Cx = float4(1.0, 1.0, -0.5, 0.0);
    const float4 By = float4(2.0, 0.5, -2.0, -0.5);
    const float4 Cy = float4(2.0, 0.0, -1.0, 0.5);
    float4 fx = Ao * q.y + Bo * q.x;
    float4 fxLeft = Ao * q.y + Bx * q.x;
    float4 fxUp = Ao * q.y + By * q.x;

    float4 lv0 = ne4(e, f) * ne4(e, h);
    float4 lv1 = lv0 * saturate(ne4(f, b) * ne4(h, d) + eq4(e, i) * ne4(f, i4) * ne4(h, i5) + eq4(e, g) + eq4(e, c));
    float4 lv2Left = ne4(e, g) * ne4(d, g);
    float4 lv2Up = ne4(e, c) * ne4(b, c);

    float4 wd1 = abs(e - c) + abs(e - g) + abs(i - h5) + abs(i - f4) + 4.0 * abs(h - f);
    float4 wd2 = abs(h - d) + abs(h - i5) + abs(f - i4) + abs(f - b) + 4.0 * abs(e - i);
    float4 edri = step(wd1, wd2) * lv0;
    float4 edr = (1.0 - step(wd2, wd1)) * lv1;
    float4 edrLeft = step(2.0 * abs(f - g), abs(h - c)) * lv2Left * edr;
    float4 edrUp = step(2.0 * abs(h - c), abs(f - g)) * lv2Up * edr;

    float4 fx45i = saturate((fx + delta - Co - 0.25) / (2.0 * delta)) * edri;
    float4 fx45 = saturate((fx + delta - Co) / (2.0 * delta)) * edr;
    float4 fx30 = saturate((fxLeft + delta - Cx) / (2.0 * delta)) * edrLeft;
    float4 fx60 = saturate((fxUp + delta - Cy) / (2.0 * delta)) * edrUp;
    float4 blend = max(max(fx30, fx60), max(fx45, fx45i));
    float4 px = step(abs(e - f), abs(e - h));

    float3 res1 = lerp(E, lerp(H, F, px.x), blend.x);
    float3 res2 = lerp(E, lerp(B, D, px.z), blend.z);
    res1 = lerp(res1, res2, step(c_df(E, res1), c_df(E, res2)));
    float3 res3 = lerp(E, lerp(F, B, px.y), blend.y);
    float3 res4 = lerp(E, lerp(D, H, px.w), blend.w);
    res2 = lerp(res3, res4, step(c_df(E, res3), c_df(E, res4)));
    return lerp(res1, res2, step(c_df(E, res1), c_df(E, res2)));
}

#undef T

float3 scaled_color(float2 uv, float2 artSize, float2 texel)
{
    float2 pixel = uv * artSize;
    float2 p = floor(pixel);
    float2 q = pixel - p;
    if (Scaler < 1.5)
        return scale2x(p, q, texel);
    if (Scaler < 2.5)
        return super_eagle(p, q, texel);
    if (Scaler < 3.5)
        return mmpx(p, q, texel);
    return xbr(p, q, texel, max(fwidth(pixel.x), 0.02) * 0.5);
}

float4 fragment_main(VsOutput input) : SV_Target0
{
    float2 uv = input.TexCoord;
    float2 centered = uv * 2.0 - 1.0;

    float radius = dot(centered, centered);
    // uv += centered * radius * Curvature;

    float2 artSize = TextureSize / max(Zoom, 0.0001);
    float2 texel = 1.0 / artSize;
    // Misconvergence is a property of the tube, not of the signal: the fringe is RgbOffset
    // OUTPUT pixels wide (capped above), so it stays a hairline at the edge of an art pixel
    // instead of a hard bar a third of one wide -- at any room zoom or window size.
    float2 chroma = min(RgbOffset / (TextureSize * max(PixelScale, 0.0001)), CHROMA_MAX_ART_PIXEL * texel) * CrtEffects;
    float2 rgb = float2(chroma.x, 0.0);

#if defined(CRT_ALIEN)
    rgb = float2(0.65, -0.65) * chroma;
#elif defined(CRT_PHOSPHOR_DOTS)
    rgb *= DOT_RGB_SPLIT;
#endif

    float3 color;
#if defined(PIXEL_GLAZE) || defined(PIXEL_TAPESTRY)
    color = sample_color((floor(uv * artSize) + 0.5) * texel);
#else
    // A scaler replaces the per-channel fringe taps: it's sampled once, so the fringe drops out.
    if (Scaler > 0.5)
        color = scaled_color(uv, artSize, texel);
    else
    {
        color.r = sample_color(uv + rgb).r;
        color.g = sample_color(uv).g;
        color.b = sample_color(uv - rgb).b;
    }
#endif

    float3 glow = 0.0;
    glow += sample_color(uv + texel * float2( 1.0,  0.0));
    glow += sample_color(uv + texel * float2(-1.0,  0.0));
    glow += sample_color(uv + texel * float2( 0.0,  1.0));
    glow += sample_color(uv + texel * float2( 0.0, -1.0));
    glow += sample_color(uv + texel * float2( 1.0,  1.0)) * 0.5;
    glow += sample_color(uv + texel * float2(-1.0,  1.0)) * 0.5;
    glow += sample_color(uv + texel * float2( 1.0, -1.0)) * 0.5;
    glow += sample_color(uv + texel * float2(-1.0, -1.0)) * 0.5;
    glow /= 4.5;


    float brightness = max(max(glow.r, glow.g), glow.b) + FlashAmount;
    color += glow * smoothstep(FlashGlowThreshold, 1.0, brightness) * FlashGlowAmount * (FlashAmount * 3.0) * CrtEffects;

#if defined(CRT_PHOSPHOR_DOTS) || defined(CRT_ALIEN) || defined(PIXEL_GLAZE) || defined(PIXEL_TAPESTRY)
    float2 pixel = uv * artSize;
    float2 footprint = fwidth(pixel);
    float detail = 1.0 - smoothstep(0.25, 0.8, max(footprint.x, footprint.y));
    float aa = max(max(footprint.x, footprint.y) * 0.5, 0.001);
    // R = variant, upper G bits = family, A = coverage. Empty field is neutral.
    float4 field = TileFieldTexture.SampleLevel(TileFieldSampler, texture_uv(uv), 0.0);
    float variation = FILTER_TILE_VARIATION * field.a;
    float material = 1.0 + variation * (field.r * 2.0 - 1.0) * 0.4;
    float phase = variation * floor(field.g * 255.0 / 32.0 + 0.001);
#endif

#if defined(CRT_PHOSPHOR_DOTS)
    // Warm shadow-mask tube: staggered round phosphors, with a soft amber halo.
    float2 cell = frac(pixel + float2(frac(floor(pixel.y) * 0.5) * 0.5, 0.0)) - 0.5;
    float dotMask = 1.0 - smoothstep(DOT_RADIUS * material - 0.07 - aa, DOT_RADIUS * material + 0.07 + aa, length(cell));
    float3 phosphor = 0.92 + 0.08 * cos(pixel.x * 6.2831853 + float3(0.0, 2.0943951, 4.1887902));
    color *= 1.0 + DOT_WARMTH * float3(0.06, 0.015, -0.06);
    color *= lerp(float3(1.0, 1.0, 1.0), (1.1 - DOT_MASK * (1.0 - dotMask)) * phosphor, detail);
    color += glow * float3(1.0, 0.55, 0.24) * DOT_HALO;
#elif defined(CRT_ALIEN)
    // Three-lobed apertures curl around dark pupils; their fringes emit a foreign spectrum.
    float2 lattice = pixel / ALIEN_CELL_SIZE;
    lattice.x += floor(lattice.y) * 0.5;
    float2 cell = frac(lattice) - 0.5;
    float distance = length(cell);
    float angle = atan2(cell.y, cell.x + 0.0001);
    float curl = angle * 3.0 + distance * ALIEN_CURL * 6.2831853 + phase;
    float aperture = distance + 0.075 * cos(curl) * material;
    float cellAA = aa / ALIEN_CELL_SIZE;
    float ring = smoothstep(0.09 - cellAA, 0.18 + cellAA, aperture)
        * (1.0 - smoothstep(0.32 - cellAA, 0.46 + cellAA, aperture));
    float alienDetail = 1.0 - smoothstep(0.25, 0.8, max(footprint.x, footprint.y) / ALIEN_CELL_SIZE);
    float3 spectrum = 0.55 + 0.45 * cos(distance * 11.0 + phase + float3(0.0, 2.0943951, 4.1887902));
    float energy = max(max(color.r, color.g), color.b);
    color = lerp(color, energy * spectrum, ALIEN_SPECTRUM * alienDetail);
    color *= 1.0 + alienDetail * ALIEN_MEMBRANE * (ring * 1.25 - 0.65);
    float3 aura = glow.gbr * float3(0.45, 0.9, 0.75);
    color += aura * smoothstep(0.15, 0.8, brightness) * ALIEN_AURA;
#elif defined(PIXEL_GLAZE)
    // Tiny glazed tiles: inset seams, directional bevels, and a colored sheen.
    float2 cell = frac(pixel) - 0.5;
    float2 edge = smoothstep(0.28 - aa, 0.48 + aa, abs(cell));
    float seam = smoothstep(0.5 - GLAZE_INSET - aa, 0.5 + aa, max(abs(cell.x), abs(cell.y)));
    float bevel = dot(-sign(cell) * edge, GLAZE_LIGHT);
    float sheen = pow(saturate(1.0 - abs(dot(cell, float2(0.8, 1.0)) + 0.12) * 2.0), 6.0);
    color *= 1.0 + detail * (GLAZE_BEVEL * material * bevel - GLAZE_SEAM * seam);
    color += detail * GLAZE_SHEEN * material * sheen * color * (1.0 - saturate(color)) * float3(0.22, 0.3, 0.34);
#elif defined(PIXEL_TAPESTRY)
    // Alternating diagonal stitches: dark under-weave and rounded silk crowns.
    float2 cell = frac(pixel) - 0.5;
    float direction = frac((floor(pixel.x) + floor(pixel.y) + floor(phase)) * 0.5) * 4.0 - 1.0;
    float across = abs(cell.x + direction * cell.y) * 0.7071068;
    float along = abs(cell.x - direction * cell.y) * 0.7071068;
    float stitch = (1.0 - smoothstep(THREAD_WIDTH - aa, THREAD_WIDTH + aa, across))
        * (1.0 - smoothstep(0.48 - aa, 0.65 + aa, along));
    float crown = exp2(-across * across * 60.0 / material);
    color *= 1.0 + detail * THREAD_RELIEF * (stitch - 0.65);
    color += detail * THREAD_LUSTER * crown * stitch * color * (1.0 - saturate(color));
#else
    float scanline = 0.5 + 0.5 * cos((uv.y * artSize.y) * 3.14159265 * 2.0);
    color *= 1.0 - scanline * ScanlineStrength * CrtEffects;
#endif

    float vignette = smoothstep(1.25, 0.35, radius);
    color *= lerp(1.0, vignette, VignetteAmount * CrtEffects);

    return float4(saturate(color), 1.0) * input.Color;
}
