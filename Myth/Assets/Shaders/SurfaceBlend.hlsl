#include "Common.hlsl"

// Color pass for a Surface: samples up to 6 texture layers, each with its own
// world-space tiling, swizzle (RGBA vs R8-luminance), tint and blend mode, and
// composites them bottom -> top in a single draw. Per-vertex TexCoord is the
// fragment's WORLD pixel position (fed by the CPU), so tiling is world-space and
// stable regardless of geometry size; each layer divides it by its own tile size.

#define MAX_LAYERS 6

cbuffer VertexUniformBlock : register(b0, space1)
{
    float4x4 Matrix;
};

cbuffer FragmentUniformBlock : register(b0, space3)
{
    float4 SurfaceMeta;             // x = active layer count, y = elapsed seconds
    float4 LayerConfig[MAX_LAYERS]; // xy = tile size in world px, z = blend mode, w = swizzle mode
    float4 LayerTint[MAX_LAYERS];   // rgba tint (straight alpha)
    float4 LayerMotion[MAX_LAYERS]; // xy = visual motion in world px / second, zw = static world px offset
    float4 LayerBlack[MAX_LAYERS];  // luminance texture's black endpoint
    float4 LayerTransform[MAX_LAYERS]; // x = 90-degree rotation, y = flip x, z = flip y
    float4 LayerAdjust[MAX_LAYERS]; // x = opacity, y = contrast, z = bias, w = quantize levels
};

// t0/s0 is the batcher's texture slot (unused here); layers live at t1..t6.
Texture2D BatchTexture : register(t0, space2);
SamplerState BatchSampler : register(s0, space2);
Texture2D Layer0 : register(t1, space2); SamplerState Layer0Sampler : register(s1, space2);
Texture2D Layer1 : register(t2, space2); SamplerState Layer1Sampler : register(s2, space2);
Texture2D Layer2 : register(t3, space2); SamplerState Layer2Sampler : register(s3, space2);
Texture2D Layer3 : register(t4, space2); SamplerState Layer3Sampler : register(s4, space2);
Texture2D Layer4 : register(t5, space2); SamplerState Layer4Sampler : register(s5, space2);
Texture2D Layer5 : register(t6, space2); SamplerState Layer5Sampler : register(s6, space2);

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

float2 transform_uv(float2 uv, float4 transform)
{
    if (transform.y != 0.0)
        uv.x = -uv.x;
    if (transform.z != 0.0)
        uv.y = -uv.y;

    if (transform.x == 1.0)
        uv = float2(uv.y, -uv.x);
    else if (transform.x == 2.0)
        uv = -uv;
    else if (transform.x == 3.0)
        uv = float2(-uv.y, uv.x);
    return uv;
}

float adjust_value(float value, float4 adjust)
{
    value = saturate((value + adjust.z - 0.5) * max(adjust.y, 0.0) + 0.5);
    if (adjust.w >= 2.0)
    {
        float steps = floor(adjust.w);
        value = floor(value * (steps - 1.0) + 0.5) / (steps - 1.0);
    }
    return value;
}

float3 adjust_color(float3 color, float4 adjust)
{
    color = saturate((color + adjust.z - 0.5) * max(adjust.y, 0.0) + 0.5);
    if (adjust.w >= 2.0)
    {
        float steps = floor(adjust.w);
        color = floor(color * (steps - 1.0) + 0.5) / (steps - 1.0);
    }
    return color;
}

float4 sample_layer(Texture2D tex, SamplerState samp, float2 world, float4 config, float4 tint, float4 motion, float4 black_color, float4 transform, float4 adjust)
{
    float2 uv = transform_uv((world + motion.zw - motion.xy * SurfaceMeta.y) / max(config.xy, float2(1.0, 1.0)), transform);
    float4 s = tex.Sample(samp, uv);
    float4 color = config.w == 1.0 // R8 luminance
        ? lerp(black_color, tint, adjust_value(s.r, adjust))
        : float4(adjust_color(s.rgb, adjust), s.a) * tint;
    color.a *= adjust.x;
    return color;
}

float4 blend_over(float4 dst, float4 src, float mode)
{
    if (mode == 1.0) // multiply
        return float4(dst.rgb * lerp(float3(1.0, 1.0, 1.0), src.rgb, src.a), saturate(dst.a + src.a));
    if (mode == 2.0) // add
        return float4(dst.rgb + src.rgb * src.a, saturate(dst.a + src.a));
    if (mode == 3.0) // screen
        return float4(1.0 - (1.0 - dst.rgb) * (1.0 - src.rgb * src.a), saturate(dst.a + src.a));
    if (mode == 4.0) // darken
        return float4(lerp(dst.rgb, min(dst.rgb, src.rgb), src.a), saturate(dst.a + src.a));
    if (mode == 5.0) // lighten
        return float4(lerp(dst.rgb, max(dst.rgb, src.rgb), src.a), saturate(dst.a + src.a));
    if (mode == 6.0) // difference
        return float4(abs(dst.rgb - src.rgb) * src.a + dst.rgb * (1.0 - src.a), saturate(dst.a + src.a));
    // over (0)
    float a = src.a + dst.a * (1.0 - src.a);
    return float4(src.rgb * src.a + dst.rgb * (1.0 - src.a), a);
}

float4 fragment_main(VsOutput input) : SV_Target0
{
    float2 world = input.TexCoord;
    int count = (int)SurfaceMeta.x;
    float4 acc = float4(0.0, 0.0, 0.0, 0.0);
    if (count > 0)
        acc = blend_over(acc, sample_layer(Layer0, Layer0Sampler, world, LayerConfig[0], LayerTint[0], LayerMotion[0], LayerBlack[0], LayerTransform[0], LayerAdjust[0]), LayerConfig[0].z);
    if (count > 1)
        acc = blend_over(acc, sample_layer(Layer1, Layer1Sampler, world, LayerConfig[1], LayerTint[1], LayerMotion[1], LayerBlack[1], LayerTransform[1], LayerAdjust[1]), LayerConfig[1].z);
    if (count > 2)
        acc = blend_over(acc, sample_layer(Layer2, Layer2Sampler, world, LayerConfig[2], LayerTint[2], LayerMotion[2], LayerBlack[2], LayerTransform[2], LayerAdjust[2]), LayerConfig[2].z);
    if (count > 3)
        acc = blend_over(acc, sample_layer(Layer3, Layer3Sampler, world, LayerConfig[3], LayerTint[3], LayerMotion[3], LayerBlack[3], LayerTransform[3], LayerAdjust[3]), LayerConfig[3].z);
    if (count > 4)
        acc = blend_over(acc, sample_layer(Layer4, Layer4Sampler, world, LayerConfig[4], LayerTint[4], LayerMotion[4], LayerBlack[4], LayerTransform[4], LayerAdjust[4]), LayerConfig[4].z);
    if (count > 5)
        acc = blend_over(acc, sample_layer(Layer5, Layer5Sampler, world, LayerConfig[5], LayerTint[5], LayerMotion[5], LayerBlack[5], LayerTransform[5], LayerAdjust[5]), LayerConfig[5].z);

    return acc * input.Color;
}
