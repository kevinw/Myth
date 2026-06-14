#include "Common.hlsl"

cbuffer VertexUniformBlock : register(b0, space1)
{
    float4x4 Matrix;
};

cbuffer FragmentUniformBlock : register(b0, space3)
{
    float4 TextureSizeTimeAmplitude;
    float4 FrequencySpeedFlatOnlyPad;
};

Texture2D Texture : register(t0, space2);
SamplerState Sampler : register(s0, space2);

Texture2D DistortMask : register(t1, space2);
SamplerState DistortMaskSampler : register(s1, space2);

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

float4 fragment_main(VsOutput input) : SV_Target0
{
    // Prevent optimizer from removing input.Type - D3D12 requires matching signatures
    if (input.Type.x < -1000) discard;

    float2 textureSize = TextureSizeTimeAmplitude.xy;
    float time = TextureSizeTimeAmplitude.z;
    float amplitude = TextureSizeTimeAmplitude.w;
    float frequency = FrequencySpeedFlatOnlyPad.x;
    float speed = FrequencySpeedFlatOnlyPad.y;
    float flatOnly = FrequencySpeedFlatOnlyPad.z;

    float distort = DistortMask.Sample(DistortMaskSampler, target_uv(input.TexCoord)).r;

    float2 pixel = screen_pixel(input.Position);
    float wave = sin(pixel.y * frequency + time * speed) * amplitude;
    wave += sin(pixel.y * frequency * 2.1 + time * speed * 1.7) * amplitude * 0.35;

    // Snap both UVs to texel centers for pixel-art edges, then lerp by mask.
    float2 baseUv = (floor(target_uv(input.TexCoord) * textureSize) + 0.5) / textureSize;
    float2 waveUv = (floor((target_uv(input.TexCoord) + float2(wave / textureSize.x, 0.0)) * textureSize) + 0.5) / textureSize;
    float2 uv = lerp(baseUv, waveUv, distort);
    float4 color = Texture.Sample(Sampler, uv) * input.Color;
    color.a *= lerp(1.0, 1.0 - distort, flatOnly);
    return color;
}
