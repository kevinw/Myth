#include "Common.hlsl"

cbuffer VertexUniformBlock : register(b0, space1)
{
    float4x4 Matrix;
};

cbuffer FragmentUniformBlock : register(b0, space3)
{
    float4 Atlas; // xy = size, zw = wandering offset in pixels
};

Texture2D Texture : register(t0, space2);
SamplerState Sampler : register(s0, space2);

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

WorldOutput fragment_main(VsOutput input)
{
    float4 original = Texture.Sample(Sampler, input.TexCoord);
    float3 wandering = Texture.Sample(Sampler, frac(input.TexCoord + Atlas.zw / Atlas.xy)).rgb;
    float4 color = float4(wandering, original.a);
    return world_output(input.Type.x * color * input.Color
         + input.Type.y * original.a * input.Color
         + input.Type.z * input.Color, 0.0);
}
