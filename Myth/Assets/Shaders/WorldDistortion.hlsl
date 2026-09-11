#include "Common.hlsl"

cbuffer VertexUniformBlock : register(b0, space1)
{
    float4x4 Matrix;
};

cbuffer FragmentUniformBlock : register(b0, space3)
{
    float4 TextureSizeAmplitude;
    float4 InversionOriginProgressActive;
    float4 InversionSnapshotOffsetMorph;
};

Texture2D Texture : register(t0, space2);
SamplerState Sampler : register(s0, space2);
Texture2D DistortionMask : register(t1, space2);
SamplerState DistortionMaskSampler : register(s1, space2);
Texture2D TileField : register(t2, space2);
SamplerState TileFieldSampler : register(s2, space2);
Texture2D InversionSnapshot : register(t3, space2);
SamplerState InversionSnapshotSampler : register(s3, space2);

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

// Solid/air gradient from raw TileField solidity. Raw (rather than step()'d) keeps the contour
// sub-tile accurate through slopes and shaped tiles, whose stamps carry the sprite's own alpha.
// Tight spread gives the ink line, wide spread the falloff the wash rides on.
float2 solid_gradient(float2 tex_coord, float2 texel, float spread)
{
    float left = TileField.Sample(TileFieldSampler, target_uv(tex_coord - float2(texel.x * spread, 0.0))).b;
    float right = TileField.Sample(TileFieldSampler, target_uv(tex_coord + float2(texel.x * spread, 0.0))).b;
    float up = TileField.Sample(TileFieldSampler, target_uv(tex_coord - float2(0.0, texel.y * spread))).b;
    float down = TileField.Sample(TileFieldSampler, target_uv(tex_coord + float2(0.0, texel.y * spread))).b;
    return float2(right - left, down - up);
}

float4 fragment_main(VsOutput input) : SV_Target0
{
    if (input.Type.x < -1000) discard;

    float2 uv = target_uv(input.TexCoord);
    float4 mask = DistortionMask.Sample(DistortionMaskSampler, uv);
    float strength = saturate(mask.a);
    float2 direction = strength > 0.001 ? mask.rg / strength * 2.0 - 1.0 : 0.0;
    float2 offset = direction * strength * TextureSizeAmplitude.z / TextureSizeAmplitude.xy;

    // The inversion front reveals the post-swap world behind it and leaves the snapshot of the
    // pre-swap world ahead of it, so both states are on screen at once. At the front itself the
    // two images are pushed apart along the solid/air gradient: new solids swell outward while
    // the old ones they replaced recede.
    float2 new_offset = offset;
    float2 old_offset = offset;
    float reveal = 1.0;
    float inversion_strength = 0.0;
    float front_rim = 0.0;
    float contour_ink = 0.0;
    float contour_wash = 0.0;
    if (InversionOriginProgressActive.w > 0.5)
    {
        float2 pixel = input.TexCoord * TextureSizeAmplitude.xy;
        float2 texel = 1.0 / TextureSizeAmplitude.xy;
        float4 field = TileField.Sample(TileFieldSampler, uv);
        float stagger = (field.r - 0.5) * 18.0;
        float max_radius = length(TextureSizeAmplitude.xy) + 24.0;
        float radius = InversionOriginProgressActive.z * max_radius;
        float2 from_origin = pixel - InversionOriginProgressActive.xy;
        // Lobe the front so it does not read as a circle. Integer harmonics keep it seamless
        // across the atan2 wrap, the phase drifts with progress so the lobes crawl as the wave
        // expands. Deviation is proportional to the radius -- tight around the player, wide once
        // it is crossing the room -- and tapers back out by the end so the front converges on the
        // clean circle instead of leaving a lagging lobe to pop when the effect cuts off.
        // Per-tile stagger still breaks the line up at the pixel scale.
        float angle = atan2(from_origin.y, from_origin.x);
        float lobes = sin(angle * 3.0 + InversionOriginProgressActive.z * 4.0) * 0.55
                    + sin(angle * 7.0 - InversionOriginProgressActive.z * 2.3) * 0.30
                    + sin(angle * 13.0 + InversionOriginProgressActive.z * 6.1) * 0.15;
        radius *= 1.0 + lobes * 0.28 * (1.0 - InversionOriginProgressActive.z);
        float wave_distance = length(from_origin) - radius + stagger;
        float wave = 1.0 - smoothstep(0.0, 20.0, abs(wave_distance));

        float2 edge = solid_gradient(input.TexCoord, texel, 1.0);
        float edge_amount = saturate(length(edge));
        float2 edge_direction = edge_amount > 0.001 ? normalize(edge) : 0.0;
        float polarity = step(0.5, field.b) * 2.0 - 1.0;
        float2 push = edge_direction * polarity * wave * edge_amount * TextureSizeAmplitude.w / TextureSizeAmplitude.xy;
        new_offset += push;
        old_offset -= push;
        inversion_strength = wave;
        front_rim = 1.0 - smoothstep(0.0, 4.0, abs(wave_distance));
        reveal = 1.0 - smoothstep(-3.0, 3.0, wave_distance);

        // Complementing the room preserves the contour in place and only swaps which side is
        // filled, so the wave inks that contour and washes the side that is solid now bright
        // and the side that is air now dark -- the fill visibly changes hands at the line.
        // The contour is the same in both states, so it lands on the snapshot half too.
        contour_ink = edge_amount * wave * (0.6 + field.r * 0.8);
        contour_wash = saturate(length(solid_gradient(input.TexCoord, texel, 3.0))) * wave * polarity;
    }

    float4 scene = Texture.Sample(Sampler, uv + new_offset) * input.Color;
    if (strength > 0.001)
    {
        scene.r = Texture.Sample(Sampler, uv + new_offset * (1.0 + strength * 0.16)).r * input.Color.r;
        scene.b = Texture.Sample(Sampler, uv + new_offset * (1.0 - strength * 0.16)).b * input.Color.b;
    }
    if (InversionSnapshotOffsetMorph.z > 0.5 && reveal < 0.999)
    {
        float2 snapshot_uv = target_uv(input.TexCoord + InversionSnapshotOffsetMorph.xy / TextureSizeAmplitude.xy);
        scene = lerp(InversionSnapshot.Sample(InversionSnapshotSampler, snapshot_uv + old_offset) * input.Color, scene, reveal);
    }
    scene.rgb *= 1.0 + inversion_strength * 0.12 + front_rim * 0.35 + contour_wash * 0.45;
    scene.rgb += float3(1.0, 0.96, 0.88) * contour_ink * 0.85;
    return scene;
}
