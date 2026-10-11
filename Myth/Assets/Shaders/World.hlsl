#include "Common.hlsl"

// Foster's Batcher shader, writing world color + emissive (see world_output).
cbuffer UniformBlock : register(b0, space1)
{
    float4x4 Matrix;
};

cbuffer FragmentUniformBlock : register(b0, space3)
{
    float4 TargetSize; // xy = size, z = world_output phase
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
	float4 color = Texture.Sample(Sampler, input.TexCoord);
	return world_output(
		input.Type.x * color * input.Color +
		input.Type.y * color.a * input.Color +
		input.Type.z * input.Color,
		TargetSize.z);
}
