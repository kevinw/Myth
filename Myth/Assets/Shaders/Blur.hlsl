#include "Common.hlsl"

cbuffer VertexUniformBlock : register(b0, space1)
{
    float4x4 Matrix;
};

cbuffer FragmentUniformBlock : register(b0, space3)
{
    float4 BlurParams;   // xy = direction (1,0) or (0,1), z = radius in pixels, w unused
    float4 TexelSize;    // xy = 1 / textureSize
    float4 ChannelMask;  // dotted with the sample to extract the source value
};

Texture2D Texture : register(t0, space2);
SamplerState Sampler : register(s0, space2);

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

// Fixed tap count so cost is constant; the sampling stride scales with the
// requested radius, giving a tweakable blur width without a variable loop.
#define BLUR_TAPS 8

float fetch(float2 uv)
{
    return dot(Texture.Sample(Sampler, target_uv(uv)), ChannelMask);
}

float4 fragment_main(VsOutput input) : SV_Target0
{
    // Prevent the optimizer from removing input.type - D3D12 requires matching signatures
    if (input.type.x < -1000) discard;

    float2 step = BlurParams.xy * TexelSize.xy;
    float radius = max(BlurParams.z, 0.0001);
    float sigma = max(radius * 0.5, 0.0001);
    float two_sigma_sq = 2.0 * sigma * sigma;

    float sum = fetch(input.tex_coord);
    float weight_sum = 1.0;

    [unroll]
    for (int i = 1; i <= BLUR_TAPS; i++)
    {
        float t = (float(i) / float(BLUR_TAPS)) * radius;
        float w = exp(-(t * t) / two_sigma_sq);
        sum += (fetch(input.tex_coord + step * t) + fetch(input.tex_coord - step * t)) * w;
        weight_sum += 2.0 * w;
    }

    float value = sum / weight_sum;
    return float4(value, value, value, value) * input.color;
}
