cbuffer VertexUniformBlock : register(b0, space1)
{
    float4x4 Matrix;
};

Texture2D Texture : register(t0, space2);
SamplerState Sampler : register(s0, space2);

Texture2D LightTexture : register(t1, space2);
SamplerState LightSampler : register(s1, space2);

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
    float4 scene = Texture.Sample(Sampler, input.TexCoord) * input.Color;
    float3 light = LightTexture.Sample(LightSampler, input.TexCoord).rgb;
    return float4(saturate(scene.rgb * light), scene.a);
}
