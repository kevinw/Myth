#include "Common.hlsl"

cbuffer VertexUniformBlock : register(b0, space1)
{
    float4x4 Matrix;
};

cbuffer FragmentUniformBlock : register(b0, space3)
{
    float4 TextureSizeAmplitude;
    float4 InversionOriginProgressActive;
};

Texture2D Texture : register(t0, space2);
SamplerState Sampler : register(s0, space2);
Texture2D DistortionMask : register(t1, space2);
SamplerState DistortionMaskSampler : register(s1, space2);
Texture2D TileField : register(t2, space2);
SamplerState TileFieldSampler : register(s2, space2);

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
    if (input.Type.x < -1000) discard;

    float2 uv = target_uv(input.TexCoord);
    float4 mask = DistortionMask.Sample(DistortionMaskSampler, uv);
    float strength = saturate(mask.a);
    float2 direction = strength > 0.001 ? mask.rg / strength * 2.0 - 1.0 : 0.0;
    float2 offset = direction * strength * TextureSizeAmplitude.z / TextureSizeAmplitude.xy;

    float inversion_strength = 0.0;
    if (InversionOriginProgressActive.w > 0.5)
    {
        float2 pixel = input.TexCoord * TextureSizeAmplitude.xy;
        float2 texel = 1.0 / TextureSizeAmplitude.xy;
        float4 field = TileField.Sample(TileFieldSampler, uv);
        float stagger = (field.r - 0.5) * 18.0;
        float max_radius = length(TextureSizeAmplitude.xy) + 24.0;
        float radius = InversionOriginProgressActive.z * max_radius;
        float wave_distance = length(pixel - InversionOriginProgressActive.xy) - radius + stagger;
        float wave = 1.0 - smoothstep(0.0, 20.0, abs(wave_distance));

        float solid_left = step(0.5, TileField.Sample(TileFieldSampler, target_uv(input.TexCoord - float2(texel.x * 2.0, 0.0))).b);
        float solid_right = step(0.5, TileField.Sample(TileFieldSampler, target_uv(input.TexCoord + float2(texel.x * 2.0, 0.0))).b);
        float solid_up = step(0.5, TileField.Sample(TileFieldSampler, target_uv(input.TexCoord - float2(0.0, texel.y * 2.0))).b);
        float solid_down = step(0.5, TileField.Sample(TileFieldSampler, target_uv(input.TexCoord + float2(0.0, texel.y * 2.0))).b);
        float2 edge = float2(solid_right - solid_left, solid_down - solid_up);
        float edge_amount = saturate(length(edge));
        float2 edge_direction = edge_amount > 0.001 ? normalize(edge) : 0.0;
        float polarity = step(0.5, field.b) * 2.0 - 1.0;
        inversion_strength = wave;
        offset += edge_direction * polarity * wave * edge_amount * TextureSizeAmplitude.w / TextureSizeAmplitude.xy;
    }

    float4 scene = Texture.Sample(Sampler, uv + offset) * input.Color;
    float chroma = max(strength, inversion_strength);
    if (chroma > 0.001)
    {
        scene.r = Texture.Sample(Sampler, uv + offset * (1.0 + chroma * 0.16)).r * input.Color.r;
        scene.b = Texture.Sample(Sampler, uv + offset * (1.0 - chroma * 0.16)).b * input.Color.b;
        scene.rgb *= 1.0 + inversion_strength * 0.12;
    }
    return scene;
}
