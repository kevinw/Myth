#include "Common.hlsl"

cbuffer VertexUniformBlock : register(b0, space1)
{
    float4x4 Matrix;
};

cbuffer FragmentUniformBlock : register(b0, space3)
{
    float4 BlurParams;  // xy = direction (1,0)/(0,1), z = max penumbra px, w = mode (0 = point, 1 = uniform/sun)
    float4 LightInfo;   // xy = light position in target px, z = light radius px, w = min softness 0..1
    float4 TexelSize;   // xy = 1 / textureSize
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

// One separable pass of a variable-radius blur. The blur radius grows with the
// distance from the light, so a shadow is sharp where it meets a nearby caster
// and softens with distance (contact-hardening). The sun (mode 1) has no
// position, so it blurs uniformly.
#define SOFT_SHADOW_TAPS 6

float4 fragment_main(VsOutput input) : SV_Target0
{
    // Prevent the optimizer from removing input.type - D3D12 requires matching signatures
    if (input.type.x < -1000) discard;

    float2 uv = input.tex_coord;
    // Use SampleLevel (explicit LOD 0) rather than Sample throughout: the later
    // taps live under data-dependent control flow (the radius early-out / blur
    // loop), and WGSL forbids implicit-derivative sampling there. This is a
    // full-screen pass over a non-mipmapped target, so LOD 0 is exact.
    float4 center = Texture.SampleLevel(Sampler, target_uv(uv), 0.0);

    float softness;
    if (BlurParams.w < 0.5)
    {
        float2 pixel = uv / max(TexelSize.xy, 1e-6);
        softness = saturate(distance(pixel, LightInfo.xy) / max(LightInfo.z, 1.0));
    }
    else
    {
        softness = 1.0;
    }
    softness = max(softness, LightInfo.w);

    float radius = softness * BlurParams.z;
    if (radius < 0.5)
        return center * input.color;

    float2 step = BlurParams.xy * TexelSize.xy;
    float sigma = max(radius * 0.5, 0.0001);
    float two_sigma_sq = 2.0 * sigma * sigma;

    float3 sum = center.rgb;
    float weight_sum = 1.0;

    [unroll]
    for (int i = 1; i <= SOFT_SHADOW_TAPS; i++)
    {
        float t = (float(i) / float(SOFT_SHADOW_TAPS)) * radius;
        float w = exp(-(t * t) / two_sigma_sq);
        sum += (Texture.SampleLevel(Sampler, target_uv(uv + step * t), 0.0).rgb +
                Texture.SampleLevel(Sampler, target_uv(uv - step * t), 0.0).rgb) * w;
        weight_sum += 2.0 * w;
    }

    // Keep the original alpha so the light mask's rim-light channel is untouched.
    return float4(sum / weight_sum, center.a) * input.color;
}
