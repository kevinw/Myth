#include "Common.hlsl"

// Stateful pixel sort over one screen-sized, world-anchored field. The field (rg, in world
// pixels) says where each pixel samples the live scene from; two stir textures, drawn fresh each
// frame by whatever wants sorting, say how each pixel may be sorted:
//   stir_a: gate, band low, band high, present
//   stir_b: reach / 255, relax px/s / 255, direction code / 3, present
//   Mode 0 (step): one odd-even transposition pass. Each pixel pairs with a neighbour along its
//     stirred axis; if both are stirred the same way, in their bands and out of order, they swap
//     offsets -- the sort moves *where pixels look*, never their colour, so whatever animates
//     underneath keeps animating through it. Offsets then relax toward zero.
//   Mode 1 (composite): redraws the scene through the field.

cbuffer VertexUniformBlock : register(b0, space1)
{
    float4x4 Matrix;
};

cbuffer FragmentUniformBlock : register(b0, space3)
{
    float4 SceneSizeZoomMode;
    float4 FieldSizeShift;
    float4 ParityStepRelax;
};

Texture2D Texture : register(t0, space2);
SamplerState Sampler : register(s0, space2);

Texture2D SceneTexture : register(t1, space2);
SamplerState SceneSampler : register(s1, space2);

Texture2D StirA : register(t2, space2);
SamplerState StirASampler : register(s2, space2);

Texture2D StirB : register(t3, space2);
SamplerState StirBSampler : register(s3, space2);

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

float hash_float(float2 position)
{
    float2 p = floor(position);
    return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453);
}

bool inside(float2 local)
{
    return all(local >= 0.0) && all(local < FieldSizeShift.xy);
}

float2 field_uv(float2 local)
{
    return target_uv((local + 0.5) / FieldSizeShift.xy);
}

// The field as it stood last frame, re-anchored by however far the camera moved since.
float2 offset_at(float2 local)
{
    float2 from = local + FieldSizeShift.zw;
    return inside(from) ? Texture.SampleLevel(Sampler, field_uv(from), 0.0).xy : float2(0.0, 0.0);
}

float4 scene_at_screen(float2 pixel)
{
    float2 uv = pixel / SceneSizeZoomMode.xy;
    if (any(uv < 0.0) || any(uv > 1.0))
        return float4(0.0, 0.0, 0.0, 0.0);
    return SceneTexture.SampleLevel(SceneSampler, target_uv(uv), 0.0);
}

// -1 when the pixel stays out of the sort: transparent, off-scene, or outside its band.
float sort_key(float2 local, float2 band)
{
    float4 color = scene_at_screen((floor(local) + 0.5) * SceneSizeZoomMode.z);
    float key = dot(color.rgb, float3(0.2126, 0.7152, 0.0722)) / max(color.a, 0.0001);
    return color.a > 0.5 && key >= band.x && key <= band.y ? key : -1.0;
}

float2 relax(float2 offset, float4 stir_a, float4 stir_b)
{
    float rate = stir_a.a > 0.5 ? stir_b.g * 255.0 * ParityStepRelax.z : ParityStepRelax.w;
    float distance = length(offset);
    return distance > 0.0 ? offset * max(0.0, distance - rate) / distance : offset;
}

float4 step_pass(float2 local)
{
    float2 offset = offset_at(local);
    float4 stir_a = StirA.SampleLevel(StirASampler, field_uv(local), 0.0);
    float4 stir_b = StirB.SampleLevel(StirBSampler, field_uv(local), 0.0);
    if (stir_a.r <= 0.0)
        return float4(relax(offset, stir_a, stir_b), 0.0, 1.0);

    float code = round(stir_b.b * 3.0);
    float2 axis = code >= 2.0 ? float2(0.0, 1.0) : float2(1.0, 0.0);
    float along = dot(local, axis);
    float side = fmod(along + ParityStepRelax.x, 2.0) < 0.5 ? 1.0 : -1.0;
    float2 partner = local + axis * side;
    if (!inside(partner))
        return float4(relax(offset, stir_a, stir_b), 0.0, 1.0);

    // Every term below is computed from both halves of the pair, so both ends agree on a swap.
    float4 partner_a = StirA.SampleLevel(StirASampler, field_uv(partner), 0.0);
    float4 partner_b = StirB.SampleLevel(StirBSampler, field_uv(partner), 0.0);
    if (partner_a.r <= 0.0 || round(partner_b.b * 3.0) != code)
        return float4(relax(offset, stir_a, stir_b), 0.0, 1.0);

    float2 partner_offset = offset_at(partner);
    float key = sort_key(local + offset, stir_a.gb);
    float partner_key = sort_key(partner + partner_offset, partner_a.gb);
    float low = side > 0.0 ? key : partner_key;
    float high = side > 0.0 ? partner_key : key;
    bool descending = code == 1.0 || code == 3.0;
    bool out_of_order = descending ? low < high : low > high;
    float2 taken = partner_offset + partner - local;
    float2 given = offset + local - partner;
    float2 pair = side > 0.0 ? local : partner;
    bool gate = hash_float(pair + ParityStepRelax.y * float2(17.0, 31.0)) < min(stir_a.r, partner_a.r);
    float reach = min(stir_b.r, partner_b.r) * 255.0;
    if (key >= 0.0 && partner_key >= 0.0 && out_of_order && gate && length(taken) <= reach && length(given) <= reach)
        offset = taken;
    return float4(relax(offset, stir_a, stir_b), 0.0, 1.0);
}

float4 fragment_main(VsOutput input) : SV_Target0
{
    // Prevent optimizer from removing input.type - D3D12 requires matching signatures
    if (input.type.x < -1000) discard;

    if (SceneSizeZoomMode.w < 0.5)
        return step_pass(floor(input.position.xy));

    float2 pixel = screen_pixel(input.position, SceneSizeZoomMode.y);
    float2 local = floor(pixel / SceneSizeZoomMode.z);
    float2 offset = inside(local) ? round(Texture.SampleLevel(Sampler, field_uv(local), 0.0).xy) : float2(0.0, 0.0);
    return scene_at_screen(pixel + offset * SceneSizeZoomMode.z);
}
