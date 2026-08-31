#include "Common.hlsl"

cbuffer VertexUniformBlock : register(b0, space1)
{
    float4x4 Matrix;
};

cbuffer FragmentUniformBlock : register(b0, space3)
{
    float4 TextureSizeAmplitude;
};

Texture2D Texture : register(t0, space2);
SamplerState Sampler : register(s0, space2);
Texture2D DistortionMask : register(t1, space2);
SamplerState DistortionMaskSampler : register(s1, space2);

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
    if (input.Type.x < -1000) discard;

    float2 uv = target_uv(input.TexCoord);
    float4 mask = DistortionMask.Sample(DistortionMaskSampler, uv);
    float strength = saturate(mask.a);
    float2 direction = strength > 0.001 ? mask.rg / strength * 2.0 - 1.0 : 0.0;
    float2 offset = direction * strength * TextureSizeAmplitude.z / TextureSizeAmplitude.xy;

    float4 scene = Texture.Sample(Sampler, uv + offset) * input.Color;
    if (strength > 0.001)
    {
        scene.r = Texture.Sample(Sampler, uv + offset * 1.16).r * input.Color.r;
        scene.b = Texture.Sample(Sampler, uv + offset * 0.84).b * input.Color.b;
    }
    return scene;
}
