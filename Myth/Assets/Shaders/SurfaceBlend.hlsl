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
    float4 LayerMotion[MAX_LAYERS]; // xy = visual motion in world px / second
    float4 LayerBlack[MAX_LAYERS];  // luminance texture's black endpoint
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

float4 sample_layer(Texture2D tex, SamplerState samp, float2 world, float4 config, float4 tint, float2 motion, float4 black_color)
{
    float2 uv = (world - motion * SurfaceMeta.y) / max(config.xy, float2(1.0, 1.0));
    float4 s = tex.Sample(samp, uv);
    if (config.w == 1.0) // R8 luminance
        return lerp(black_color, tint, s.r);
    return s * tint;
}

float4 blend_over(float4 dst, float4 src, float mode)
{
    if (mode == 1.0) // multiply
        return float4(dst.rgb * lerp(float3(1.0, 1.0, 1.0), src.rgb, src.a), saturate(dst.a + src.a));
    if (mode == 2.0) // add
        return float4(dst.rgb + src.rgb * src.a, saturate(dst.a + src.a));
    if (mode == 3.0) // screen
        return float4(1.0 - (1.0 - dst.rgb) * (1.0 - src.rgb * src.a), saturate(dst.a + src.a));
    // over (0)
    float a = src.a + dst.a * (1.0 - src.a);
    return float4(src.rgb * src.a + dst.rgb * (1.0 - src.a), a);
}

float4 fragment_main(VsOutput input) : SV_Target0
{
    float2 world = input.TexCoord;
    float4 samples[MAX_LAYERS];
    samples[0] = sample_layer(Layer0, Layer0Sampler, world, LayerConfig[0], LayerTint[0], LayerMotion[0].xy, LayerBlack[0]);
    samples[1] = sample_layer(Layer1, Layer1Sampler, world, LayerConfig[1], LayerTint[1], LayerMotion[1].xy, LayerBlack[1]);
    samples[2] = sample_layer(Layer2, Layer2Sampler, world, LayerConfig[2], LayerTint[2], LayerMotion[2].xy, LayerBlack[2]);
    samples[3] = sample_layer(Layer3, Layer3Sampler, world, LayerConfig[3], LayerTint[3], LayerMotion[3].xy, LayerBlack[3]);
    samples[4] = sample_layer(Layer4, Layer4Sampler, world, LayerConfig[4], LayerTint[4], LayerMotion[4].xy, LayerBlack[4]);
    samples[5] = sample_layer(Layer5, Layer5Sampler, world, LayerConfig[5], LayerTint[5], LayerMotion[5].xy, LayerBlack[5]);

    int count = (int)SurfaceMeta.x;
    float4 acc = float4(0.0, 0.0, 0.0, 0.0);
    [unroll]
    for (int i = 0; i < MAX_LAYERS; i++)
        if (i < count)
            acc = blend_over(acc, samples[i], LayerConfig[i].z);

    return acc * input.Color;
}
