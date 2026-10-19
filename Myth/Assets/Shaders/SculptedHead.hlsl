// Ray-marches a SculptedHead's signed distance field: one fragment per head-texture pixel, writing color shaded
// like a SculptedBody (SV_Target0, Include/SculptedShading.hlsl), screen-space normal (SV_Target1, alpha = hit) and
// scene-light highlights (SV_Target2). SculptedHeadMask.hlsl dilates the result into the mask. SculptedHead.cs keeps
// a CPU copy of the shape (not the shading) as the reference it's verified against; keep the two in step.

cbuffer VertexUniformBlock : register(b0, space1)
{
    float4x4 Matrix;
};

#define MAX_LIGHTS 4
#define MAX_DENTS 4
#define MAX_CONTACTS 2
#define MAX_ADORNMENTS 6

cbuffer FragmentUniformBlock : register(b0, space3)
{
    float4 ToHead0;  // view -> head-local rows (row-vector convention, like System.Numerics)
    float4 ToHead1;
    float4 ToHead2;
    float4 ToView0;  // head-local -> view rows
    float4 ToView1;
    float4 ToView2;
    float4 Frame;    // x = half size (px), y = radius (px), z = mouth open, w = extent (how far past the radius it reaches)
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
    float4 Deform;   // x = wobble amplitude (radii), y = wobble phase, z = dent count, w = contact count
    float4 Bruise;   // what a dent's floor shades toward, by alpha
    float4 Stretch;  // xy = squash and stretch on screen, 1 = none
    float4 Morph;    // x = anvil (the chin grows into an iron block), y = cheeks puffed, z = split open, w = core glow
    float4 Face;     // expression, -1 .. 1: brow tilt (+ inner ends down), brow raise, brow skew (+ head's right up), smile
    float4 Look;     // x = lids closed (0 .. 1), yz = gaze (head-local x, y lean of the pupils)
    float4 Features; // x = eyeball radius (0 = bare sockets), y = iris size, z = teeth height, w = lip band
    float4 Hair;     // brow hair color, a = how far over the brow it grows
    float4 Lips;
    float4 Teeth;
    float4 Sclera;
    float4 Iris;
    float4 Adorn;    // x = count, yz = how far hanging rings swing (head-local x, z)
    float4 Lights[MAX_LIGHTS * 2]; // (texture-space position, height, radius), (color * intensity, 0)
    float4 Dents[MAX_DENTS]; // head-local center, radius: spheres carved out of the head
    float4 Contacts[MAX_CONTACTS * 2]; // (head-local normal toward a wall, the wall's distance), (squash along it, 0, 0, 0)
    float4 Adornments[MAX_ADORNMENTS * 4]; // (point, kind: 0 ring, 1 stud, 2 horn), (ring axis or horn direction, size), (color, metal), (thickness, 0, 0, 0)
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

static const float SplitGap = 0.35; // radii each half slides out, fully split
static const float CoreRadius = 0.42;
static const float3 Iron = float3(0.35, 0.36, 0.44);
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

// Start to end through middle as two capsules; one straight capsule when middle sits on the line.
float sd_bent_capsule(float3 p, float3 start, float3 middle, float3 end, float radius, bool bent)
{
    return bent ? min(sd_capsule(p, start, middle, radius), sd_capsule(p, middle, end, radius)) : sd_capsule(p, start, end, radius);
}

float sd_round_box(float3 p, float3 half_size, float rounding)
{
    float3 q = abs(p) - half_size + rounding;
    return length(max(q, 0.0)) + min(max(q.x, max(q.y, q.z)), 0.0) - rounding;
}

float sd_round_cone(float3 p, float3 a, float3 b, float r1, float r2)
{
    float3 ba = b - a;
    float l2 = dot(ba, ba);
    float rr = r1 - r2;
    float a2 = l2 - rr * rr;
    float il2 = 1.0 / l2;
    float3 pa = p - a;
    float y = dot(pa, ba);
    float z = y - l2;
    float3 xv = pa * l2 - ba * y;
    float x2 = dot(xv, xv);
    float y2 = y * y * l2;
    float z2 = z * z * l2;
    float k = sign(rr) * rr * rr * x2;
    if (sign(z) * a2 * z2 > k)
        return sqrt(x2 + z2) * il2 - r2;
    if (sign(y) * a2 * y2 < k)
        return sqrt(x2 + y2) * il2 - r1;
    return (sqrt(x2 * a2 * il2) + y * rr) * il2 - r1;
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

float3 wobble(float3 p) { return p + Deform.x * sin(p.yzx * 5.0 + Deform.y + float3(0.0, 2.1, 4.2)); }

// How close p is to a dent's floor: 1 at its center, falling off toward the rim, 0 away from it.
float in_dent(float3 p)
{
    float amount = 0.0;
    for (int i = 0; i < (int)Deform.z; i++)
    {
        float3 offset = p - Dents[i].xyz;
        float3 axis = normalize(Dents[i].xyz);
        float lateral = length(offset - axis * dot(offset, axis));
        if (length(offset) < Dents[i].w * 1.15)
            amount = max(amount, saturate(1.0 - lateral / Dents[i].w));
    }
    return amount;
}

// Squashes p's space along each contact's normal by its squash, toward its wall (within a radius) so the side against
// it stays put, bulging the rest to keep the volume; returns how much that shrinks distances, so the march stays safe.
float squash(inout float3 p)
{
    float scale = 1.0;
    for (int i = 0; i < (int)Deform.w; i++)
    {
        float3 n = Contacts[i * 2].xyz;
        float s = Contacts[i * 2 + 1].x;
        float along = dot(p, n);
        float anchor = min(Contacts[i * 2].w, 1.0);
        p = (p - n * along) * sqrt(s) + n * (anchor + (along - anchor) / s);
        scale *= min(s, 1.0 / sqrt(s));
    }
    return scale;
}

float3 squashed(float3 p)
{
    squash(p);
    return p;
}

// The brow, bent by the expression: tilt pulls the inner ends down and forward over the sockets (anger) or up (worry);
// raise lifts it all; skew lifts one side and drops the other.
float sd_brow(float3 p)
{
    float ends = Brow.x + Face.y * 0.08 + Face.x * 0.03;
    float3 middle = float3(0.0, Brow.x + Face.y * 0.08 - Face.x * 0.14, Brow.y + max(Face.x, 0.0) * 0.06);
    return sd_bent_capsule(p, float3(-Jaw2.w, ends - Face.z * 0.08, Brow.y), middle,
        float3(Jaw2.w, ends + Face.z * 0.08, Brow.y), Jaw2.z, Face.x != 0.0);
}

// The mouth's cut, bent by the smile and widened as it opens.
float mouth_radius() { return Mouth.z + Frame.z * 0.14; }
float sd_mouth(float3 p)
{
    float3 corner = float3(Mouth.x, Mouth.y + Face.w * 0.07, 0.86);
    return sd_bent_capsule(p, corner * float3(-1.0, 1.0, 1.0), float3(0.0, Mouth.y - Face.w * 0.03, 0.86), corner,
        mouth_radius(), Face.w != 0.0);
}

// Eyeballs sit back in the sockets, the lids slide down over them from above as they close.
float3 eyeball(float side) { return socket(side) - float3(0.0, 0.0, (Eyes.z - Features.x) * 0.5); }
float sd_eyeballs(float3 p) { return min(sd_sphere(p - eyeball(-1.0), Features.x), sd_sphere(p - eyeball(1.0), Features.x)); }
float sd_lid(float3 q)
{
    float r = Features.x * 1.12;
    return max(sd_sphere(q, r), r * (1.0 - 2.0 * Look.x) - q.y);
}
float sd_lids(float3 p) { return min(sd_lid(p - eyeball(-1.0)), sd_lid(p - eyeball(1.0))); }

// Upper teeth hang from the top of the mouth's cut, lower stand on its floor; clenched they fill the slit.
float sd_teeth(float3 p)
{
    float h = Features.z;
    float r = mouth_radius();
    float3 half_size = float3(Mouth.x * 0.8, h * 0.5, 0.05);
    float upper = sd_round_box(p - float3(0.0, Mouth.y + r - h * 0.5, 0.8), half_size, 0.015);
    float lower = sd_round_box(p - float3(0.0, Mouth.y - r + h * 0.5, 0.8), half_size, 0.015);
    return min(upper, lower);
}

float sd_adornment(float3 p, int i)
{
    float3 at = Adornments[i * 4].xyz;
    float kind = Adornments[i * 4].w;
    float3 direction = Adornments[i * 4 + 1].xyz;
    float size = Adornments[i * 4 + 1].w;
    float thickness = Adornments[i * 4 + 3].x;
    if (kind < 0.5)
    {
        float3 down = normalize(float3(Adorn.y, -1.0, Adorn.z));
        float3 q = p - (at + down * size);
        float along = dot(q, direction);
        return length(float2(length(q - direction * along) - size, along)) - thickness;
    }
    if (kind < 1.5)
        return sd_sphere(p - at, size);
    return sd_round_cone(p, at, at + direction * size, thickness, thickness * 0.15);
}

// The anvil hangs from the jaw's underside, a neck flaring into a wide striking block, growing with Morph.x.
float sd_anvil(float3 p)
{
    float a = Morph.x;
    float3 top = float3(0.0, -Jaw.w - Jaw.y, Jaw2.x * 0.5);
    float neck = sd_round_box(p - top + float3(0.0, 0.14 * a, 0.0), float3(0.35, 0.16, 0.32) * a, 0.04 * a);
    float block = sd_round_box(p - top + float3(0.0, 0.48 * a, 0.0), float3(0.85, 0.2, 0.6) * a, 0.05 * a);
    return smooth_union(neck, block, 0.06 * a);
}

// Split open, each half slides out sideways; where p sat on the whole head.
float3 unsplit(float3 p) { return p - float3(sign(p.x) * Morph.z * SplitGap, 0.0, 0.0); }

float face_distance(float3 p)
{
    p = wobble(p);
    float cranium = sd_ellipsoid(p - float3(0.0, Cranium.w, -0.08), Cranium.xyz);
    float jaw = sd_ellipsoid(p - float3(0.0, -Jaw.w, Jaw2.x), Jaw.xyz);
    float d = smooth_union(cranium, jaw, max(0.01, Jaw2.y));
    if (Morph.y > 0.0)
        d = smooth_union(d, min(sd_sphere(p - float3(-0.5, -0.22, 0.5), 0.3 * Morph.y),
            sd_sphere(p - float3(0.5, -0.22, 0.5), 0.3 * Morph.y)), 0.15);
    if (Morph.x > 0.0)
        d = smooth_union(d, sd_anvil(p), 0.1);
    if (Jaw2.z > 0.0)
        d = smooth_union(d, sd_brow(p), 0.1);
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
    {
        d = smooth_subtract(d, min(sd_sphere(p - socket(-1.0), Eyes.z), sd_sphere(p - socket(1.0), Eyes.z)), 0.05);
        if (Features.x > 0.0)
            d = min(d, min(sd_eyeballs(p), sd_lids(p)));
    }
    if (Mouth.x > 0.0)
    {
        d = smooth_subtract(d, sd_mouth(p), 0.04);
        if (Features.z > 0.0)
            d = min(d, sd_teeth(p));
    }
    for (int i = 0; i < (int)Deform.z; i++)
        d = smooth_subtract(d, sd_sphere(p - Dents[i].xyz, Dents[i].w), 0.06);
    for (int j = 0; j < (int)Adorn.x; j++)
        d = min(d, sd_adornment(p, j));
    return d;
}

// Split open, each half keeps only its own side, and a glowing core hangs in the gap.
float shape_distance(float3 p)
{
    if (Morph.z <= 0.0)
        return face_distance(p);
    float3 q = unsplit(p);
    return min(max(face_distance(q), -q.x * sign(p.x)), sd_sphere(p, CoreRadius * Morph.z));
}

// The shape squashed against its contacts, and flattened where it meets their walls.
float head_distance(float3 p)
{
    float3 q = p;
    float scale = squash(q);
    float d = shape_distance(q) * scale;
    for (int i = 0; i < (int)Deform.w; i++)
        d = smooth_subtract(d, Contacts[i * 2].w - dot(p, Contacts[i * 2].xyz), 0.08);
    return d;
}

// How hard p is pressed into a wall: flesh near a firmly squashed contact blanches.
float blanch(float3 p)
{
    float amount = 0.0;
    for (int i = 0; i < (int)Deform.w; i++)
        amount = max(amount, saturate(1.0 - (Contacts[i * 2].w - dot(p, Contacts[i * 2].xyz)) / 0.3) *
            saturate((1.0 - Contacts[i * 2 + 1].x) * 5.0));
    return amount;
}

// A pupil on each socket's floor, so the glow sits deep in the eye and a lowered brow hides it.
bool in_eye_glow(float3 p)
{
    float glow = Eyes.z * 0.5;
    float3 floor = float3(0.0, -0.02, Eyes.z);
    float3 l = p - (socket(-1.0) - floor);
    float3 r = p - (socket(1.0) - floor);
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
    // Squash and stretch: each pixel marches the unstretched head where it lands, so nothing is resampled.
    float2 across = float2(input.position.x - half_size, -(input.position.y - half_size)) / radius / Stretch.xy;
    float extent = Frame.w;
    float depth = extent * extent - dot(across, across);
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
        // A wobbling field overstates distances; step short so rays don't tunnel.
        distance *= Deform.x > 0.0 ? 0.7 : 1.0;
        position += rayDirection * distance;
        travelled += distance;
    }
    if (!found)
        return output;

    float3 normal = surface_normal(position);
    float3 viewNormal = to_view(normal);
    viewNormal = normalize(float3(viewNormal.xy / Stretch.xy, viewNormal.z));
    output.Normal = float4(viewNormal.x * 0.5 + 0.5, -viewNormal.y * 0.5 + 0.5, viewNormal.z * 0.5 + 0.5, 1.0);
    float occlusion = clamp(head_distance(position + normal * 0.22) / 0.22, 0.0, 1.0);
    // The shared shading works in pixels with y down.
    float3 screenNormal = float3(viewNormal.x, -viewNormal.y, viewNormal.z);
    float3 viewPosition = to_view(position) * radius * float3(Stretch.xy, 1.0);
    float3 pixel = float3(half_size + viewPosition.x, half_size - viewPosition.y, viewPosition.z);
    float threshold = sculpted_threshold(input.position.xy);
    float light = sculpted_light(screenNormal, occlusion, Surface.x, 0.0);
    // Where the hit sits on the whole face, before a split slid it out.
    float3 face = Morph.z > 0.0 ? unsplit(position) : position;
    // A flat snout's end face sits a shade down from the cheeks around it.
    if (on_snout_face(face))
        light = min(light, 0.6);
    bool iron = Morph.x > 0.0 && sd_anvil(wobble(squashed(face))) < 0.03;
    float3 color = iron ? sculpted_color(light, Iron * 1.7, Iron * 1.25, Iron * 0.85, threshold)
        : sculpted_color(light, Near.rgb, Core.rgb, Far.rgb, threshold);
    color = lerp(color, Bruise.rgb, Bruise.a * saturate(in_dent(wobble(squashed(face))) * 2.0));
    if (blanch(position) > threshold)
        color = lerp(Near.rgb, 1.0, 0.25);
    float3 emit = float3(0.0, 0.0, 0.0);
    float3 feature = wobble(squashed(face));
    float3 shadowed = Far.rgb / max(Near.rgb, 0.05); // how the skin's shadow tints, applied to each feature's color
    int adornment = -1;
    for (int j = 0; j < (int)Adorn.x; j++)
        if (sd_adornment(feature, j) < 0.01)
            adornment = j;
    if (adornment >= 0)
    {
        // Metal catches the light: a sky-reflecting shade and a hard glint.
        float3 tint = Adornments[adornment * 4 + 2].rgb;
        bool metal = Adornments[adornment * 4 + 2].a > 0.5;
        float shine = metal ? sculpted_light(screenNormal, occlusion, 1.0, 0.0) : light;
        color = sculpted_color(shine, tint * (metal ? 1.35 : 1.0), tint, tint * shadowed * (metal ? 0.5 : 1.0), threshold);
        if (metal)
            emit = sculpted_specular(pixel, screenNormal, tint, float4(1.0, 0.25, 1.6, 0.0), threshold).rgb;
    }
    else if (Features.x > 0.0 && Eyes.z > 0.0 && sd_eyeballs(feature) < 0.01 && sd_lids(feature) > 0.005)
    {
        // Sclera, iris and pupil by how near the hit sits to where the eye looks; a lit eye's iris glows.
        float3 center = eyeball(feature.x < 0.0 ? -1.0 : 1.0);
        float facing = dot(normalize(feature - center), normalize(float3(Look.y, Look.z, 1.0)));
        float iris = cos(Features.y * 0.9);
        bool glowing = Eye.a > 0.0;
        color = facing > lerp(iris, 1.0, 0.55) ? float3(0.03, 0.02, 0.02)
            : facing > iris ? (glowing ? Eye.rgb : sculpted_color(light, Iris.rgb, Iris.rgb * 0.75, Iris.rgb * shadowed, threshold))
            : sculpted_color(light, Sclera.rgb, Sclera.rgb * 0.8, Sclera.rgb * shadowed, threshold);
        if (glowing && facing > iris && facing <= lerp(iris, 1.0, 0.55))
            emit = Eye.rgb;
    }
    else if (Features.z > 0.0 && Mouth.x > 0.0 && sd_teeth(feature) < 0.01)
        color = sculpted_color(light, Teeth.rgb, Teeth.rgb * 0.75, Teeth.rgb * shadowed, threshold);
    else if (Features.w > 0.0 && Mouth.x > 0.0 && sd_mouth(feature) < Features.w)
        color = sculpted_color(light, Lips.rgb, Lips.rgb * 0.75, Lips.rgb * shadowed, threshold);
    // Hair grows over the brow's top and front, ragged at its edge.
    else if (Hair.a > 0.0 && Jaw2.z > 0.0 && normal.y > -0.35 &&
        sd_brow(feature) < Hair.a * (0.6 + 0.8 * frac(sin(dot(floor(feature * 40.0), float3(12.9898, 78.233, 37.719))) * 43758.5453)))
        color = sculpted_color(light, Hair.rgb, Hair.rgb * 0.7, Hair.rgb * shadowed, threshold);
    else if (Features.x <= 0.0 && in_eye_glow(face) && Eye.a > 0.0)
        color = Eye.rgb;
    // A band of shadow inside the silhouette's edge, so it reads crisply without an outline.
    if (viewNormal.z < 0.22)
        color *= 0.72;
    output.Color = float4(color, 1.0);
    output.Specular = sculpted_specular(pixel, screenNormal, Near.rgb, Surface, threshold);
    output.Specular.rgb += emit;
    // The anvil heats with the eyes' color as it grows.
    if (iron)
        output.Specular += float4(Eye.rgb * Morph.x * 0.45, 0.0);
    if (Morph.z > 0.0 && sd_sphere(position, CoreRadius * Morph.z) < 0.02)
    {
        output.Color = float4(lerp(Eye.rgb, 1.0, 0.35 + 0.4 * Morph.w), 1.0);
        output.Specular = float4(Eye.rgb * (0.5 + Morph.w), 1.0);
    }
    return output;
}
