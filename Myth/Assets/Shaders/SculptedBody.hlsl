// Ray-marches a SculptedBody: a smooth union of round cones (and carved grooves) posed by the rig, one fragment per
// body-texture pixel, writing color (SV_Target0: the nearest piece's own color, shaded toward Shadow), screen-space
// normal (SV_Target1, alpha = hit) and the highlights the nearest scene lights put on it (SV_Target2, premultiplied,
// drawn into the emissive pass). Each pixel only visits the pieces binned to its tile.
// SculptedHeadMask.hlsl dilates the result into the mask. Units are pixels: x right, y down, z toward the viewer.

cbuffer VertexUniformBlock : register(b0, space1)
{
    float4x4 Matrix;
};

#define MAX_PRIMITIVES 96
#define MAX_LIGHTS 4
#define MAX_TILES 512
#define MAX_TILE_ENTRIES 4104

cbuffer FragmentUniformBlock : register(b0, space3)
{
    float4 Frame;   // x = primitive count, y = occlusion probe distance (px), z = front z, w = back z
    float4 Shading; // x = 0 banded, 1 smooth, 2 dithered; yz = the texture's world position; w = light count
    float4 Depth;   // x = the body's center z, y = range (px), z = how much the far side darkens
    float4 Tiling;  // x = tile size (px), y = tiles across, z = tiles down
    float4 Shadow;  // the tint the darkest band multiplies a piece's color by
    float4 Materials[2]; // flesh, metal: metallic, roughness, specular, 0
    float4 Lights[MAX_LIGHTS * 2]; // (texture-space position, height, radius), (color * intensity, 0)
    float4 Primitives[MAX_PRIMITIVES * 3]; // (a, radius a), (b, radius b), (blend, material, carve, packed rgb)
    float4 Tiles[MAX_TILES / 4]; // per tile: first entry * 128 + entry count
    float4 Entries[MAX_TILE_ENTRIES / 12]; // primitive indices, 8 bits each, 3 to a float
};

static int TileFirst;
static int TileCount;

#include "Include/SculptedShading.hlsl"

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

struct BodyOutput
{
    float4 Color : SV_Target0;
    float4 Normal : SV_Target1;
    float4 Specular : SV_Target2;
};

static const int MaxSteps = 64;

float sd_round_cone(float3 p, float3 a, float3 b, float r1, float r2)
{
    float3 ba = b - a;
    float l2 = dot(ba, ba);
    float rr = r1 - r2;
    float a2 = l2 - rr * rr;
    if (a2 <= 0.0001)
        return r1 > r2 ? length(p - a) - r1 : length(p - b) - r2;
    float il2 = 1.0 / l2;
    float3 pa = p - a;
    float y = dot(pa, ba);
    float z = y - l2;
    float3 q = pa * l2 - ba * y;
    float x2 = dot(q, q);
    float y2 = y * y * l2;
    float z2 = z * z * l2;
    float k = sign(rr) * rr * rr * x2;
    if (sign(z) * a2 * z2 > k)
        return sqrt(x2 + z2) * il2 - r2;
    if (sign(y) * a2 * y2 < k)
        return sqrt(x2 + y2) * il2 - r1;
    return (sqrt(x2 * a2 * il2) + y * rr) * il2 - r1;
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

float primitive_distance(int i, float3 p)
{
    float4 a = Primitives[i * 3];
    float4 b = Primitives[i * 3 + 1];
    return sd_round_cone(p, a.xyz, b.xyz, a.w, b.w);
}

int tile_primitive(int j)
{
    int k = TileFirst + j;
    int f = k / 3;
    uint packed = (uint)Entries[f >> 2][f & 3];
    return (int)((packed >> ((k - f * 3) * 8)) & 255u);
}

float body_distance(float3 p)
{
    float d = 1e5;
    for (int j = 0; j < TileCount; j++)
    {
        int i = tile_primitive(j);
        float4 c = Primitives[i * 3 + 2];
        float di = primitive_distance(i, p);
        if (c.z > 0.5)
            d = smooth_subtract(d, di, max(c.x, 0.001));
        else
            d = c.x > 0.0 ? smooth_union(d, di, c.x) : min(d, di);
    }
    return d;
}

// The solid piece nearest the surface: whose color and material it shows.
int nearest_primitive(float3 p)
{
    float nearest = 1e5;
    int found = 0;
    for (int j = 0; j < TileCount; j++)
    {
        int i = tile_primitive(j);
        float di = primitive_distance(i, p);
        if (Primitives[i * 3 + 2].z < 0.5 && di < nearest)
        {
            nearest = di;
            found = i;
        }
    }
    return found;
}

float3 unpack_rgb(float packed)
{
    uint rgb = (uint)packed;
    return float3((rgb >> 16) & 255u, (rgb >> 8) & 255u, rgb & 255u) / 255.0;
}

float3 surface_normal(float3 p)
{
    const float e = 0.25;
    const float3 a = float3(1.0, -1.0, -1.0);
    const float3 b = float3(-1.0, -1.0, 1.0);
    const float3 c = float3(-1.0, 1.0, -1.0);
    const float3 d = float3(1.0, 1.0, 1.0);
    return normalize(a * body_distance(p + a * e) + b * body_distance(p + b * e) +
        c * body_distance(p + c * e) + d * body_distance(p + d * e));
}

BodyOutput fragment_main(VsOutput input)
{
    BodyOutput output;
    output.Color = float4(0.0, 0.0, 0.0, 0.0);
    output.Normal = float4(0.0, 0.0, 0.0, 0.0);
    output.Specular = float4(0.0, 0.0, 0.0, 0.0);
    // Keep the batcher's interpolants alive; D3D12 needs matching signatures.
    if (input.type.x < -1000.0)
        return output;

    int2 tile = int2(input.position.xy / Tiling.x);
    if (tile.x >= (int)Tiling.y || tile.y >= (int)Tiling.z)
        return output;
    int tileIndex = tile.y * (int)Tiling.y + tile.x;
    uint entry = (uint)Tiles[tileIndex >> 2][tileIndex & 3];
    TileFirst = (int)(entry >> 7);
    TileCount = (int)(entry & 127u);
    if (TileCount == 0)
        return output;

    float3 position = float3(input.position.xy, Frame.z);
    float distance = 1e5;
    for (int step = 0; step < MaxSteps && position.z > Frame.w; step++)
    {
        distance = body_distance(position);
        if (distance < 0.02)
            break;
        position.z -= max(distance, 0.01);
    }
    // A ray grazing a groove can run out of steps still creeping along the surface: that's a hit too.
    if (distance > 0.5)
        return output;

    float3 normal = surface_normal(position);
    output.Normal = float4(normal * 0.5 + 0.5, 1.0);
    float probe = Frame.y;
    float occlusion = clamp(body_distance(position + normal * probe) / probe, 0.0, 1.0);
    float4 piece = Primitives[nearest_primitive(position) * 3 + 2];
    float4 surface = Materials[(int)piece.y];
    float3 near = unpack_rgb(piece.w);
    float3 far = near * Shadow.rgb;
    float threshold = sculpted_threshold(input.position.xy);
    float light = sculpted_light(normal, occlusion, surface.x, Depth.z * saturate((Depth.x - position.z) / Depth.y));
    output.Color = float4(sculpted_color(light, near, lerp(near, far, 0.5), far, threshold), 1.0);
    output.Specular = sculpted_specular(position, normal, near, surface, threshold);
    return output;
}
