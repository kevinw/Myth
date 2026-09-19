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

cbuffer VertexUniformBlock : register(b0, space1)
{
    float4x4 Matrix;
};

cbuffer FragmentUniformBlock : register(b0, space3)
{
    float2 TextureSize;
    float BloomAmount;
    float VignetteAmount;
    float RgbOffset;
    float ExtraGlow;
    float BloomThreshold;
    float ScanlineStrength;
    float Zoom;
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

float4 fragment_main(VsOutput input) : SV_Target0
{
    float2 uv = input.TexCoord;
    float2 centered = uv * 2.0 - 1.0;

    float radius = dot(centered, centered);
    // uv += centered * radius * Curvature;

    float2 artSize = TextureSize / max(Zoom, 0.0001);
    float2 texel = 1.0 / artSize;
    float2 rgb = float2(RgbOffset, 0.0) * texel;

#if defined(CRT_ALIEN)
    rgb = float2(0.65, -0.65) * RgbOffset * texel;
#elif defined(CRT_PHOSPHOR_DOTS)
    rgb *= DOT_RGB_SPLIT;
#endif

    float3 color;
#if defined(PIXEL_GLAZE) || defined(PIXEL_TAPESTRY)
    color = sample_color((floor(uv * artSize) + 0.5) * texel);
#else
    color.r = sample_color(uv + rgb).r;
    color.g = sample_color(uv).g;
    color.b = sample_color(uv - rgb).b;
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


    float brightness = max(max(glow.r, glow.g), glow.b) + ExtraGlow;
    color += glow * smoothstep(BloomThreshold, 1.0, brightness) * BloomAmount * (ExtraGlow * 3.0);

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
    color *= 1.0 - scanline * ScanlineStrength;
#endif

    float vignette = smoothstep(1.25, 0.35, radius);
    color *= lerp(1.0, vignette, VignetteAmount);

    return float4(saturate(color), 1.0) * input.Color;
}
