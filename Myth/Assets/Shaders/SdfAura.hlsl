#include "Common.hlsl"

cbuffer VertexUniformBlock : register(b0, space1)
{
    float4x4 Matrix;
};

cbuffer FragmentUniformBlock : register(b0, space3)
{
    float4 Params; // x = scrolled time, y = max aura distance (px), z = stripe width (px), w = stored-distance scale
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

float3 hsv2rgb(float h, float s, float v)
{
    float3 p = abs(frac(h + float3(0.0, 2.0 / 3.0, 1.0 / 3.0)) * 6.0 - 3.0);
    return v * lerp(float3(1.0, 1.0, 1.0), saturate(p - 1.0), s);
}

float4 fragment_main(VsOutput input) : SV_Target0
{
    // Prevent the optimizer from removing input.type - D3D12 requires matching signatures
    if (input.type.x < -1000) discard;

    float distance = Texture.Sample(Sampler, target_uv(input.tex_coord)).r * Params.w;

    // Skip inside the silhouette and beyond the aura range.
    if (distance < 0.5 || distance > Params.y)
        discard;

    // 2px rainbow bands; subtracting time scrolls them outward from the silhouette.
    float band = floor((distance - Params.x) / Params.z);
    float3 color = hsv2rgb(frac(band / 6.0), 1.0, 1.0);
    float alpha = saturate((Params.y - distance) / 3.0); // soften the outer edge
    return float4(color, alpha) * input.color;
}
