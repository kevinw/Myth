#include "Common.hlsl"

float tile_field_family(float4 field)
{
    return floor(floor(field.g * 255.0 + 0.5) / 32.0);
}

cbuffer VertexUniformBlock : register(b0, space1)
{
    float4x4 Matrix;
};

cbuffer FragmentUniformBlock : register(b0, space3)
{
    float4 ChannelMinMaxPadding;
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

float4 fragment_main(VsOutput input) : SV_Target0
{
    float4 field = Texture.Sample(Sampler, target_uv(input.TexCoord));
    int channel = int(ChannelMinMaxPadding.x + 0.5);
    float value = channel == 0 ? field.r : channel == 1 ? tile_field_family(field) / 4.0 : field.b;
    float minValue = ChannelMinMaxPadding.y;
    float maxValue = ChannelMinMaxPadding.z;
    value = saturate((value - minValue) / max(maxValue - minValue, 0.0001));
    return float4(value, value, value, channel == 2 ? 1.0 : field.a) * input.Color;
}
