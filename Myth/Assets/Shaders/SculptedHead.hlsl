// Ray-marches a SculptedHead's signed distance field: one fragment per head-texture pixel, writing color shaded
// like a SculptedBody (SV_Target0, Include/SculptedShading.hlsl), screen-space normal (SV_Target1, alpha = hit) and
// scene-light highlights (SV_Target2). SculptedHeadMask.hlsl dilates the result into the mask. SculptedHead.cs keeps
// a CPU copy of the shape (not the shading) as the reference it's verified against; keep the two in step.

cbuffer VertexUniformBlock : register(b0, space1)
{
    float4x4 Matrix;
};

#define MAX_LIGHTS 4

cbuffer FragmentUniformBlock : register(b0, space3)
{
    float4 ToHead0;  // view -> head-local rows (row-vector convention, like System.Numerics)
    float4 ToHead1;
    float4 ToHead2;
    float4 ToView0;  // head-local -> view rows
    float4 ToView1;
    float4 ToView2;
    float4 Frame;    // x = half size (px), y = radius (px), z = mouth open
    float4 Cranium;  // width, height, depth, lift
    float4 Jaw;      // width, height, depth, drop
    float4 Jaw2;     // forward, blend, brow size, brow width
    float4 Brow;     // brow height, brow forward, nose size, nose length
    float4 Nose;     // nose height, nose droop, nose flat, nostril size
    float4 Eyes;     // spacing, height, size, depth
    float4 Mouth;    // width, height, size, ear size
    float4 Ears;     // ear height
    float4 Near;
    float4 Core;
    float4 Far;
    float4 Eye;
    float4 Shading;  // x = 0 banded, 1 smooth, 2 dithered; yz = the texture's world position; w = light count
    float4 Surface;  // metallic, roughness, specular
    float4 Lights[MAX_LIGHTS * 2]; // (texture-space position, height, radius), (color * intensity, 0)
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

struct HeadOutput
{
    float4 Color : SV_Target0;
    float4 Normal : SV_Target1;
    float4 Specular : SV_Target2;
};

#include "Include/SculptedShading.hlsl"

static const float Extent = 1.45;
static const int MaxSteps = 40;

float3 to_head(float3 v) { return v.x * ToHead0.xyz + v.y * ToHead1.xyz + v.z * ToHead2.xyz; }
float3 to_view(float3 v) { return v.x * ToView0.xyz + v.y * ToView1.xyz + v.z * ToView2.xyz; }

float sd_sphere(float3 p, float radius) { return length(p) - radius; }

float sd_ellipsoid(float3 p, float3 radii)
{
    float k0 = length(p / radii);
    float k1 = length(p / (radii * radii));
    return k1 > 0.0 ? k0 * (k0 - 1.0) / k1 : -min(radii.x, min(radii.y, radii.z));
}

float sd_capsule(float3 p, float3 a, float3 b, float radius)
{
    float3 pa = p - a;
    float3 ba = b - a;
    float h = clamp(dot(pa, ba) / dot(ba, ba), 0.0, 1.0);
    return length(pa - ba * h) - radius;
}

float sd_cylinder(float3 p, float3 a, float3 b, float radius)
{
    float3 ba = b - a;
    float3 pa = p - a;
    float baba = dot(ba, ba);
    float paba = dot(pa, ba);
    float x = length(pa * baba - ba * paba) - radius * baba;
    float y = abs(paba - baba * 0.5) - baba * 0.5;
    float d = max(x, y) < 0.0 ? -min(x * x, y * y * baba) : (x > 0.0 ? x * x : 0.0) + (y > 0.0 ? y * y * baba : 0.0);
    return sign(d) * sqrt(abs(d)) / baba;
}

float smooth_union(float a, float b, float k)
{
    float h = clamp(0.5 + 0.5 * (b - a) / k, 0.0, 1.0);
    return lerp(b, a, h) - k * h * (1.0 - h);
}

float smooth_subtract(float a, float b, float k)
{
    float h = clamp(0.5 - 0.5 * (a + b) / k, 0.0, 1.0);
    return lerp(a, -b, h) + k * h * (1.0 - h);
}

float3 socket(float side) { return float3(Eyes.x * side, Eyes.y, Eyes.w); }

float head_distance(float3 p)
{
    float mouthOpen = Frame.z;
    float cranium = sd_ellipsoid(p - float3(0.0, Cranium.w, -0.08), Cranium.xyz);
    float jaw = sd_ellipsoid(p - float3(0.0, -Jaw.w, Jaw2.x), Jaw.xyz);
    float d = smooth_union(cranium, jaw, max(0.01, Jaw2.y));
    if (Jaw2.z > 0.0)
        d = smooth_union(d, sd_capsule(p, float3(-Jaw2.w, Brow.x, Brow.y), float3(Jaw2.w, Brow.x, Brow.y), Jaw2.z), 0.1);
    float noseSize = Brow.z;
    if (noseSize > 0.0)
    {
        float3 root = float3(0.0, Nose.x, 0.88);
        float3 tip = root + float3(0.0, -Nose.y, Brow.w);
        float nose = sd_capsule(p, root, tip, noseSize);
        bool long_nose = any(tip != root);
        if (Nose.z > 0.0 && long_nose)
        {
            float edge = noseSize * 0.25;
            nose = lerp(nose, sd_cylinder(p, root, tip, noseSize - edge) - edge, Nose.z);
        }
        d = smooth_union(d, nose, 0.07);
        if (Nose.w > 0.0)
        {
            float3 axis = long_nose ? normalize(tip - root) : float3(0.0, 0.0, 1.0);
            float3 face = tip + axis * (Nose.z > 0.0 ? 0.0 : noseSize);
            float3 apart = float3(noseSize * 0.45, 0.0, 0.0);
            d = smooth_subtract(d, min(sd_sphere(p - face - apart, Nose.w), sd_sphere(p - face + apart, Nose.w)), 0.03);
        }
    }
    if (Mouth.w > 0.0)
    {
        float3 ear = float3(Cranium.x, Ears.x, -0.05);
        float3 earRadii = float3(0.35, 1.0, 0.7) * Mouth.w;
        float ears = min(sd_ellipsoid(p - ear, earRadii), sd_ellipsoid(p - ear * float3(-1.0, 1.0, 1.0), earRadii));
        d = smooth_union(d, ears, 0.05);
    }
    if (Eyes.z > 0.0)
        d = smooth_subtract(d, min(sd_sphere(p - socket(-1.0), Eyes.z), sd_sphere(p - socket(1.0), Eyes.z)), 0.05);
    if (Mouth.x > 0.0)
        d = smooth_subtract(d, sd_capsule(p, float3(-Mouth.x, Mouth.y, 0.86), float3(Mouth.x, Mouth.y, 0.86),
            Mouth.z + mouthOpen * 0.14), 0.04);
    return d;
}

bool in_eye_glow(float3 p)
{
    float glow = Eyes.z * 0.45;
    float3 back = float3(0.0, -0.02, Eyes.z * 0.4);
    float3 l = p - (socket(-1.0) - back);
    float3 r = p - (socket(1.0) - back);
    return Eyes.z > 0.0 && (dot(l, l) < glow * glow || dot(r, r) < glow * glow);
}

bool on_snout_face(float3 p)
{
    float noseSize = Brow.z;
    if (Nose.z < 0.5 || noseSize <= 0.0)
        return false;
    float3 root = float3(0.0, Nose.x, 0.88);
    float3 axis = float3(0.0, -Nose.y, Brow.w);
    float along = dot(p - root, axis) / dot(axis, axis);
    float3 off = p - root - axis * along;
    return along > 0.97 && dot(off, off) < noseSize * noseSize * 0.8;
}

float3 surface_normal(float3 p)
{
    const float e = 0.01;
    const float3 a = float3(1.0, -1.0, -1.0);
    const float3 b = float3(-1.0, -1.0, 1.0);
    const float3 c = float3(-1.0, 1.0, -1.0);
    const float3 d = float3(1.0, 1.0, 1.0);
    return normalize(a * head_distance(p + a * e) + b * head_distance(p + b * e) +
        c * head_distance(p + c * e) + d * head_distance(p + d * e));
}

HeadOutput fragment_main(VsOutput input)
{
    HeadOutput output;
    output.Color = float4(0.0, 0.0, 0.0, 0.0);
    output.Normal = float4(0.0, 0.0, 0.0, 0.0);
    output.Specular = float4(0.0, 0.0, 0.0, 0.0);
    // Keep the batcher's interpolants alive; D3D12 needs matching signatures.
    if (input.type.x < -1000.0)
        return output;

    float half_size = Frame.x;
    float radius = Frame.y;
    float2 across = float2(input.position.x - half_size, -(input.position.y - half_size)) / radius;
    float depth = Extent * Extent - dot(across, across);
    if (depth <= 0.0)
        return output;
    depth = sqrt(depth);

    float3 rayDirection = -ToHead2.xyz;
    float3 position = to_head(float3(across, depth));
    float travelled = 0.0;
    bool found = false;
    for (int step = 0; step < MaxSteps && travelled < depth * 2.0; step++)
    {
        float distance = head_distance(position);
        if (distance < 0.004)
        {
            found = true;
            break;
        }
        position += rayDirection * distance;
        travelled += distance;
    }
    if (!found)
        return output;

    float3 normal = surface_normal(position);
    float3 viewNormal = normalize(to_view(normal));
    output.Normal = float4(viewNormal.x * 0.5 + 0.5, -viewNormal.y * 0.5 + 0.5, viewNormal.z * 0.5 + 0.5, 1.0);
    float occlusion = clamp(head_distance(position + normal * 0.22) / 0.22, 0.0, 1.0);
    // The shared shading works in pixels with y down.
    float3 screenNormal = float3(viewNormal.x, -viewNormal.y, viewNormal.z);
    float3 viewPosition = to_view(position) * radius;
    float3 pixel = float3(half_size + viewPosition.x, half_size - viewPosition.y, viewPosition.z);
    float threshold = sculpted_threshold(input.position.xy);
    float light = sculpted_light(screenNormal, occlusion, Surface.x, 0.0);
    // A flat snout's end face sits a shade down from the cheeks around it.
    if (on_snout_face(position))
        light = min(light, 0.6);
    output.Color = in_eye_glow(position) && Eye.a > 0.0 ? Eye : float4(sculpted_color(light, Near.rgb, Core.rgb, Far.rgb, threshold), 1.0);
    output.Specular = sculpted_specular(pixel, screenNormal, Near.rgb, Surface, threshold);
    return output;
}
