#include "Common.hlsl"

cbuffer VertexUniformBlock : register(b0, space1)
{
    float4x4 Matrix;
};

cbuffer FragmentUniformBlock : register(b0, space3)
{
    float4 FlashColorAmount;
    float4 TimePadding;
};

Texture2D Texture : register(t0, space2);
SamplerState Sampler : register(s0, space2);
Texture2D TileField : register(t1, space2);
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

float4 fragment_main(VsOutput input) : SV_Target0
{
    float2 uv = target_uv(input.TexCoord);
    float4 scene = Texture.Sample(Sampler, uv) * input.Color;
    float4 field = TileField.Sample(TileFieldSampler, uv);
    float shimmer = 0.88 + 0.12 * sin((input.Position.x + input.Position.y) * 0.45 +
        TimePadding.x * 24.0 + field.r * 12.0);
    float amount = FlashColorAmount.a * (1.0 - field.b) * shimmer;
    return float4(lerp(scene.rgb, FlashColorAmount.rgb, saturate(amount)), scene.a);
}
