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
    float2 position : TEXCOORD0;
    float2 tex_coord : TEXCOORD1;
    float4 color : TEXCOORD2;
    float4 type : TEXCOORD4;
};

struct VsOutput
{
    float2 tex_coord : TEXCOORD0;
    float4 color : TEXCOORD1;
    float4 type : TEXCOORD4;
    float4 position : SV_Position;
};

VsOutput vertex_main(VsInput input)
{
    VsOutput output;
    output.tex_coord = input.tex_coord;
    output.color = input.color;
    output.type = input.type;
    output.position = mul(Matrix, float4(input.position, 0.0, 1.0));
    return output;
}

float4 fragment_main(VsOutput input) : SV_Target0
{
    float4 scene = Texture.Sample(Sampler, input.tex_coord) * input.color;
    float4 light = LightTexture.Sample(LightSampler, input.tex_coord);
    float brightness = max(max(scene.r, scene.g), scene.b);
    float rim_light = light.a * saturate(1.0 - brightness * 2.2) * 0.18;
    float3 color = scene.rgb * light.rgb + light.rgb * rim_light;
    return float4(saturate(color), scene.a);
}
