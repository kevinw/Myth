#include "Common.hlsl"

cbuffer VertexUniformBlock : register(b0, space1)
{
    float4x4 Matrix;
};

cbuffer FragmentUniformBlock : register(b0, space3)
{
    float2 TextureSize;
    float BloomAmount;
    float VignetteAmount;
    float RgbOffset;
    float Padding0;
    float Padding1;
    float Padding2;
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

float3 sample_color(float2 uv)
{
    return Texture.Sample(Sampler, texture_uv(saturate(uv))).rgb;
}

float4 fragment_main(VsOutput input) : SV_Target0
{
    float2 uv = input.TexCoord;
    float2 centered = uv * 2.0 - 1.0;

    float radius = dot(centered, centered);
    // uv += centered * radius * Curvature;

    float2 texel = 1.0 / TextureSize;
    float2 rgb = float2(RgbOffset, 0.0) * texel;

    float3 color;
    color.r = sample_color(uv + rgb).r;
    color.g = sample_color(uv).g;
    color.b = sample_color(uv - rgb).b;

    float3 glow = 0.0;
    glow += sample_color(uv + texel * float2( 1.0,  0.0));
    glow += sample_color(uv + texel * float2(-1.0,  0.0));
    glow += sample_color(uv + texel * float2( 0.0,  1.0));
    glow += sample_color(uv + texel * float2( 0.0, -1.0));
    glow += sample_color(uv + texel * float2( 1.0,  1.0)) * 0.5;
    glow += sample_color(uv + texel * float2(-1.0,  1.0)) * 0.5;
    glow += sample_color(uv + texel * float2( 1.0, -1.0)) * 0.5;
    glow += sample_color(uv + texel * float2(-1.0, -1.0)) * 0.5;
    glow /= 4.5;

    float brightness = max(max(glow.r, glow.g), glow.b);
    color += glow * smoothstep(0.35, 1.0, brightness) * BloomAmount;

    float vignette = smoothstep(1.25, 0.35, radius);
    color *= lerp(1.0, vignette, VignetteAmount);

    float scanline = 0.5 + 0.5 * cos((uv.y * TextureSize.y) * 3.14159265 * 2.0);
    color *= 1.0 - scanline * 0.1;

    return float4(saturate(color), 1.0) * input.Color;
}
