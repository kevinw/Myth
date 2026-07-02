#include "Common.hlsl"

cbuffer VertexUniformBlock : register(b0, space1)
{
    float4x4 Matrix;
};

cbuffer FragmentUniformBlock : register(b0, space3)
{
    float4 Region;   // xy = combined-region size (px), zw = SDF atlas size (px)
    float4 RTopLeft; // xy = region top-left (caller-local), zw = world camera top-left
    float4 P0;       // player: xy = padded top-left (unflipped, local), zw = padded size
    float4 P1;       // player: xy = atlas source top-left (px), z = facing (+/-1)
    float4 B0;       // bud:    xy = padded top-left (unflipped, local), zw = padded size
    float4 B1;       // bud:    xy = atlas source top-left (px), z = facing, w = mirror axis X (local)
    float4 A0;       // x = scrolled time, y = max aura dist, z = stripe width, w = stored-distance scale
    float4 A1;       // x = smooth-min k, y = fade amount, z = zoom
};

Texture2D Texture : register(t0, space2);
SamplerState Sampler : register(s0, space2);

struct VsInput
{
    float2 position : TEXCOORD0;
    float2 tex_coord : TEXCOORD1;
    float4 color : TEXCOORD2;
    float4 type : TEXCOORD4;
};

struct VsOutput
{
    float2 tex_coord : TEXCOORD0;
    float4 color : TEXCOORD1;
    float4 type : TEXCOORD4;
    float4 position : SV_Position;
};

VsOutput vertex_main(VsInput input)
{
    VsOutput output;
    output.tex_coord = input.tex_coord;
    output.color = input.color;
    output.type = input.type;
    output.position = mul(Matrix, float4(input.position, 0.0, 1.0));
    return output;
}

float3 hsv2rgb(float h, float s, float v)
{
    float3 p = abs(frac(h + float3(0.0, 2.0 / 3.0, 1.0 / 3.0)) * 6.0 - 3.0);
    return v * lerp(float3(1.0, 1.0, 1.0), saturate(p - 1.0), s);
}

// Ordered 4x4 Bayer threshold (0..1) at a world pixel, matching the lighting
// dither so the aura dissolves in the same stipple style.
float bayer4(float2 worldPixel)
{
    int x = ((int)floor(worldPixel.x)) & 3;
    int y = ((int)floor(worldPixel.y)) & 3;
    int m[16] = { 0, 8, 2, 10, 12, 4, 14, 6, 3, 11, 1, 9, 15, 7, 13, 5 };
    return (m[y * 4 + x] + 0.5) / 16.0;
}

// Exterior distance (px) from one silhouette's padded SDF in the shared atlas.
// Returns "far" when the position is outside that silhouette's padded region.
float sample_dist(float2 pos, float2 padTopLeft, float2 padSize, float2 atlasTopLeft, float2 atlasSize, float distScale)
{
    float2 lp = pos - padTopLeft;
    if (lp.x < 0.0 || lp.y < 0.0 || lp.x >= padSize.x || lp.y >= padSize.y)
        return 9999.0;
    float2 uv = (atlasTopLeft + lp) / atlasSize;
    return Texture.SampleLevel(Sampler, uv, 0.0).r * distScale;
}

float4 fragment_main(VsOutput input) : SV_Target0
{
    // Prevent the optimizer from removing input.type - D3D12 requires matching signatures
    if (input.type.x < -1000) discard;

    // Position in the caller's local space.
    float2 pos = input.tex_coord * Region.xy + RTopLeft.xy;

    // Player silhouette flips around local 0; bud around its own origin.
    float2 pp = pos;
    if (P1.z < 0.0) pp.x = -pp.x;
    float dP = sample_dist(pp, P0.xy, P0.zw, P1.xy, Region.zw, A0.w);

    float2 bp = pos;
    if (B1.z < 0.0) bp.x = 2.0 * B1.w - bp.x;
    float dB = sample_dist(bp, B0.xy, B0.zw, B1.xy, Region.zw, A0.w);

    // Smooth-min (metaball merge): the -k*h*(1-h) term bridges the gap.
    float k = max(A1.x, 0.0001);
    float h = saturate(0.5 + 0.5 * (dB - dP) / k);
    float d = lerp(dB, dP, h) - k * h * (1.0 - h);

    if (d < 0.5 || d > A0.y)
        discard;

    // Coverage = outer-edge falloff * ease amount. Dither-cutoff it against an
    // ordered Bayer threshold (world-pixel, static) so the fade/ease dissolve as
    // a stipple instead of a smooth alpha ramp; the solid core stays solid.
    float coverage = saturate((A0.y - d) / 3.0) * A1.y;
    // NOTE: this world-pixel stipple is currently degenerate — the aura passes A1.z (zoom) as 0
    // (see Player.Aura), so the divide blows up and the dither cutoff never fires. Left as a raw
    // device pixel (no browser flip, unlike screen_pixel) to preserve exact behavior. TODO: pass a
    // real zoom + target height from Player.Aura and switch to screen_to_world to re-enable it.
    float2 worldPixel = RTopLeft.zw + input.position.xy / A1.z;
    if (coverage < bayer4(worldPixel))
        discard;

    // 2px rainbow bands scrolling outward.
    float band = floor((d - A0.x) / A0.z);
    float3 color = hsv2rgb(frac(band / 6.0), 1.0, 1.0);
    return float4(color, 1.0) * input.color;
}
