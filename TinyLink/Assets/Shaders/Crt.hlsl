#define OLD_CRT

cbuffer VertexUniformBlock : register(b0, space1)
{
    float4x4 Matrix;
};

cbuffer FragmentUniformBlock : register(b0, space3)
{
    float2 TextureSize;
    float Time;
    float BloomAmount;
    float ScanlineAmount;
    float VignetteAmount;
    float RgbOffset;
    // float Curvature;
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
    return Texture.Sample(Sampler, saturate(uv)).rgb;
}

float3 sample_crt_color(float2 uv, float2 texel)
{
    float2 rgb = float2(RgbOffset, 0.0) * texel;
    float3 color;
    color.r = sample_color(uv + rgb).r;
    color.g = sample_color(uv).g;
    color.b = sample_color(uv - rgb).b;
    return color;
}

float crt_cell_mask(float2 uv)
{
    float2 source_pixel = uv * TextureSize;
    float2 cell = frac(source_pixel);
    float2 centered = cell - 0.5;

    float rounded = 1.0 - smoothstep(0.54, 0.92, length(centered / float2(0.58, 0.46)));
    float horizontal_gap = 1.0 - smoothstep(0.40, 0.50, abs(centered.y));
    float vertical_gap = 1.0 - smoothstep(0.44, 0.50, abs(centered.x));
    float phosphor = 0.92 + 0.08 * cos((floor(source_pixel.x * 3.0) / 3.0) * 6.2831853);

    return saturate(rounded * 0.72 + horizontal_gap * vertical_gap * phosphor * 0.28);
}

float crt_row_mask(float2 uv)
{
    float y = frac(uv.y * TextureSize.y);
    float row_distance = abs(y - 0.5);
    float beam = 1.0 - smoothstep(0.18, 0.50, row_distance);
    return lerp(0.30, 1.10, beam);
}

float4 fragment_main(VsOutput input) : SV_Target0
{
    float2 uv = input.TexCoord;
    float2 centered = uv * 2.0 - 1.0;

    float radius = dot(centered, centered);
    // uv += centered * radius * Curvature;

    float2 texel = 1.0 / TextureSize;
#ifdef OLD_CRT
    float2 rgb = float2(RgbOffset, 0.0) * texel;

    float3 color;
    color.r = sample_color(uv + rgb).r;
    color.g = sample_color(uv).g;
    color.b = sample_color(uv - rgb).b;
#else

    float3 color = sample_crt_color(uv, texel);
    float3 vertical_beam = color * 0.58;
    vertical_beam += sample_crt_color(uv + texel * float2(0.0,  0.45), texel) * 0.18;
    vertical_beam += sample_crt_color(uv + texel * float2(0.0, -0.45), texel) * 0.18;
    vertical_beam += sample_crt_color(uv + texel * float2(0.0,  1.00), texel) * 0.03;
    vertical_beam += sample_crt_color(uv + texel * float2(0.0, -1.00), texel) * 0.03;
    color = lerp(color, vertical_beam, ScanlineAmount);
#endif

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

#ifdef OLD_CRT
#else
    float cell = crt_cell_mask(uv);
    float row = pow(crt_row_mask(uv), 2.0);
    color *= lerp(1.0, row * lerp(0.88, 1.03, cell), ScanlineAmount);
#endif


    float vignette = smoothstep(1.25, 0.35, radius);
    color *= lerp(1.0, vignette, VignetteAmount);

    return float4(saturate(color), 1.0) * input.Color;
}
