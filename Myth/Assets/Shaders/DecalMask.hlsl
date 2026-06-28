#include "Common.hlsl"

cbuffer VertexUniformBlock : register(b0, space1)
{
    float4x4 Matrix;
};

cbuffer FragmentUniformBlock : register(b0, space3)
{
    float4 CoverageSize;
};

Texture2D Texture : register(t0, space2);
SamplerState Sampler : register(s0, space2);
Texture2D Coverage : register(t1, space2);
SamplerState CoverageSampler : register(s1, space2);

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
    float4 sampled = Texture.Sample(Sampler, input.TexCoord);
    float4 color =
        input.Type.x * sampled * input.Color +
        input.Type.y * sampled.a * input.Color +
        input.Type.z * input.Color;

    float2 pixel = screen_pixel(input.Position);
#if BROWSER
    pixel.y = CoverageSize.y - pixel.y;
#endif
    float coverage = Coverage.Sample(CoverageSampler, target_uv(pixel / CoverageSize.xy)).a;
    return color * coverage;
}
