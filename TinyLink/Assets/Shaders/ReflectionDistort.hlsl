cbuffer VertexUniformBlock : register(b0, space1)
{
    float4x4 Matrix;
};

cbuffer FragmentUniformBlock : register(b0, space3)
{
    float4 TextureSizeTimeAmplitude;
    float4 FrequencySpeedPadding;
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
    float2 textureSize = TextureSizeTimeAmplitude.xy;
    float time = TextureSizeTimeAmplitude.z;
    float amplitude = TextureSizeTimeAmplitude.w;
    float frequency = FrequencySpeedPadding.x;
    float speed = FrequencySpeedPadding.y;

    float2 pixel = input.TexCoord * textureSize;
    float wave = sin(pixel.y * frequency + time * speed) * amplitude;
    wave += sin(pixel.y * frequency * 2.1 + time * speed * 1.7) * amplitude * 0.35;

    float2 uv = input.TexCoord + float2(wave / textureSize.x, 0.0);
    // Snap distorted samples to texel centers so the reflection keeps a pixel-art edge.
    uv = (floor(uv * textureSize) + 0.5) / textureSize;
    float4 color = Texture.Sample(Sampler, uv) * input.Color;
    return color;
}
