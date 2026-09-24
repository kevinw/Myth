#include "Common.hlsl"

// Normal pass for a Surface: samples the surface's (baked) tangent-space normal
// map, world-space tiled to match its source layer, and writes it into the
// NormalBuffer. A surface with no normal simply isn't drawn in this pass, leaving
// the buffer's flat (0.5,0.5,1) clear value.

cbuffer VertexUniformBlock : register(b0, space1)
{
    float4x4 Matrix;
};

cbuffer FragmentUniformBlock : register(b0, space3)
{
    float4 NormalConfig; // xy = tile size in world px, z = strength, w = elapsed seconds
    float4 NormalMotion; // xy = visual motion, z = rotate normal map with texture coordinates
};

Texture2D NormalMap : register(t0, space2);
SamplerState NormalSampler : register(s0, space2);

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
    float2 uv = (input.TexCoord - NormalMotion.xy * NormalConfig.w) / max(NormalConfig.xy, float2(1.0, 1.0));
    float3 n = NormalMap.Sample(NormalSampler, uv).xyz * 2.0 - 1.0;
    // Scale the tangent-space tilt by strength, keep it normalized.
    n.xy *= max(NormalConfig.z, 0.0);
    if (NormalMotion.z > 0.5)
    {
        float2 dx = ddx(input.TexCoord);
        float2 dy = ddy(input.TexCoord);
        float determinant = dx.x * dy.y - dy.x * dx.y;
        if (abs(determinant) > 0.00001)
        {
            float direction = determinant < 0.0 ? -1.0 : 1.0;
            float2 across = normalize(float2(dy.y, -dx.y) * direction);
            float2 along = normalize(float2(-dy.x, dx.x) * direction);
            n.xy = across * n.x + along * n.y;
        }
    }
    n = normalize(n);
    return float4(n * 0.5 + 0.5, 1.0);
}
