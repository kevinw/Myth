#include "Common.hlsl"

cbuffer VertexUniformBlock : register(b0, space1)
{
    float4x4 Matrix;
};

cbuffer FragmentUniformBlock : register(b0, space3)
{
    float4 FieldSize; // xy = size, z = world_output phase
};

Texture2D Texture : register(t0, space2);
SamplerState Sampler : register(s0, space2);
Texture2D Field : register(t1, space2);
SamplerState FieldSampler : register(s1, space2);

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
    float4 sampled = Texture.Sample(Sampler, input.TexCoord);
    float4 color =
        input.Type.x * sampled * input.Color +
        input.Type.y * sampled.a * input.Color +
        input.Type.z * input.Color;

    float2 pixel = screen_pixel(input.Position, FieldSize.y);
    float4 field = Field.Sample(FieldSampler, target_uv(pixel / FieldSize.xy));
    color.rgb *= 1.0.xxx - field.aaa + field.rgb;
    return world_output(color, FieldSize.z);
}
