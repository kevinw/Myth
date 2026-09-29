#include "Common.hlsl"

// Turns the HDR lit scene into the two things it should become: the displayable pixel, and
// the energy that pixel could not hold. Both read the same buffer, so the composite's work
// happens once and neither result is a re-derivation of the other.

cbuffer VertexUniformBlock : register(b0, space1)
{
    float4x4 Matrix;
};

cbuffer FragmentUniformBlock : register(b0, space3)
{
    float4 Settings; // x = knee, y = desaturate, z = mode (0 resolve, 1 extract), w = bloom strength
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

float4 fragment_main(VsOutput input) : SV_Target0
{
    // Prevent the optimizer from removing input.type - D3D12 requires matching signatures
    if (input.type.x < -1000) discard;

    float4 lit = Texture.Sample(Sampler, target_uv(input.tex_coord));
    if (Settings.z > 0.5)
        return float4(tonemap_overflow(lit.rgb, Settings.x) * Settings.w, lit.a);
    return float4(tonemap_color(lit.rgb, Settings.x, Settings.y), lit.a);
}
