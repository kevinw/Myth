#include "Common.hlsl"

cbuffer VertexUniformBlock : register(b0, space1)
{
    float4x4 Matrix;
};

cbuffer FragmentUniformBlock : register(b0, space3)
{
    float2 TextureSize;
    float2 OutputSize;
    float BloomAmount;
    float VignetteAmount;
    float RgbOffset;
    float CrtShader;
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

// Ported from crt-gdv-mini-ultra-trinitron.glsl by hhirtz/mpv-retro-shaders.
// The generated mpv shader exposes these as constants; this port keeps its defaults.
float3 gdv_sample(float2 uv)
{
    return pow(sample_color(uv), 2.2);
}

float gdv_scanline(float distance, float luma)
{
    return exp2(-10.0 * pow(distance, lerp(1.5, 2.0, luma)));
}

float3 gdv_mask(float2 position, float luma)
{
    position = floor(position);
    float3 mask = 0.0;
    if (frac(position.x / 3.0) > 0.333)
        mask = 1.0;
    return lerp(mask, 1.0, luma * 0.4);
}

float2 gdv_warp(float2 uv)
{
    float2 position = uv * 2.0 - 1.0;
    position *= float2(1.0, 1.0 + position.x * position.x * 0.05);
    return position * 0.5 + 0.5;
}

float3 gdv_glow(float2 uv, float2 texel)
{
    float3 glow = gdv_sample(uv) * 3.0;
    glow += (gdv_sample(uv + texel * float2(-1.0,  0.0))
          +  gdv_sample(uv + texel * float2( 1.0,  0.0))
          +  gdv_sample(uv + texel * float2(-1.0, -1.0))
          +  gdv_sample(uv + texel * float2( 1.0,  1.0))
          +  gdv_sample(uv + texel * float2(-1.0,  1.0))
          +  gdv_sample(uv + texel * float2( 1.0, -1.0))
          +  gdv_sample(uv + texel * float2( 0.0, -1.0))
          +  gdv_sample(uv + texel * float2( 0.0,  1.0))) * 2.5;
    glow += (gdv_sample(uv + texel * float2(-2.0,  0.0))
          +  gdv_sample(uv + texel * float2( 2.0,  0.0))
          +  gdv_sample(uv + texel * float2(-2.0, -1.0))
          +  gdv_sample(uv + texel * float2(-1.0, -2.0))
          +  gdv_sample(uv + texel * float2( 1.0,  2.0))
          +  gdv_sample(uv + texel * float2( 2.0,  1.0))
          +  gdv_sample(uv + texel * float2(-2.0,  1.0))
          +  gdv_sample(uv + texel * float2(-1.0,  2.0))
          +  gdv_sample(uv + texel * float2( 1.0, -2.0))
          +  gdv_sample(uv + texel * float2( 2.0, -1.0))
          +  gdv_sample(uv + texel * float2( 0.0, -2.0))
          +  gdv_sample(uv + texel * float2( 0.0,  2.0))) * 1.5;
    return glow / 45.0 * 0.35;
}

float3 gdv_saturation(float3 color)
{
    float3 weights = float3(0.3, 0.6, 0.1);
    if (length(color) * 0.5775 < 0.5)
        weights *= weights * 2.0;
    return lerp(dot(color, weights), color, 1.2);
}

float3 gdv_color(float2 texCoord)
{
    float2 uv = texCoord * 1.0001;
    float2 position = gdv_warp(uv);
    float2 texel = 1.0 / TextureSize;
    float2 sourcePosition = position * TextureSize;
    float2 fraction = frac(sourcePosition);
    float2 center = floor(sourcePosition) * texel + texel * 0.5;

    float3 upperLeft = gdv_sample(center);
    float3 upperRight = gdv_sample(center + float2(texel.x, 0.0));
    float3 lowerLeft = gdv_sample(center + float2(0.0, texel.y));
    float3 lowerRight = gdv_sample(center + texel);
    float leftWeight = pow(1.0 - fraction.x, 2.5);
    float rightWeight = pow(fraction.x, 2.5);
    float3 upper = (upperRight * rightWeight + upperLeft * leftWeight) / (leftWeight + rightWeight);
    float3 lower = (lowerRight * rightWeight + lowerLeft * leftWeight) / (leftWeight + rightWeight);
    float upperLuma = dot(upper, float3(0.3, 0.6, 0.1));
    float lowerLuma = dot(lower, float3(0.3, 0.6, 0.1));

    upper = 2.0 * pow(upper, 2.8) - pow(upper, 3.6);
    lower = 2.0 * pow(lower, 2.8) - pow(lower, 3.6);
    upper *= gdv_mask(uv * OutputSize, upperLuma);
    lower *= gdv_mask(uv * OutputSize, lowerLuma);

    float3 color = upper * gdv_scanline(fraction.y, upperLuma)
                 + lower * gdv_scanline(1.0 - fraction.y, lowerLuma);
    if (TextureSize.y >= 400.0)
        color = (upper + lower) * 0.5;

    color = pow(min(color, 1.0), 1.0 / 2.2);
    float luma = dot(color, float3(0.3, 0.6, 0.1));
    color += gdv_glow(center, texel);
    color *= lerp(1.0, 1.2, luma);
    color = gdv_saturation(color);

    float2 vignettePosition = uv * (1.0 - uv);
    color *= min(pow(vignettePosition.x * vignettePosition.y * 45.0, 0.15), 1.0);
    return pow(saturate(color), 1.0 / 2.2);
}

float4 fragment_main(VsOutput input) : SV_Target0
{
    if (CrtShader >= 0.5)
        return float4(gdv_color(input.TexCoord), 1.0) * input.Color;

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
