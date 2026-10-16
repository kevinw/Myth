// Ray-marches a SculptedBody: a smooth union of round cones (and carved grooves) posed by the rig, one fragment per
// body-texture pixel, writing color (SV_Target0: the nearest piece's own color, shaded toward Shadow), screen-space
// normal (SV_Target1, alpha = hit) and the highlights the nearest scene lights put on it (SV_Target2, premultiplied,
// drawn into the emissive pass). The pieces and their per-tile bins come in storage buffers (Vulkan only sees the
// first 4 KB of a uniform block); each pixel only visits the pieces binned to its tile.
// SculptedHeadMask.hlsl dilates the result into the mask. Units are pixels: x right, y down, z toward the viewer.

cbuffer VertexUniformBlock : register(b0, space1)
{
    float4x4 Matrix;
};

#define MAX_LIGHTS 4

cbuffer FragmentUniformBlock : register(b0, space3)
{
    float4 Frame;   // y = occlusion probe distance (px), z = front z, w = back z
    float4 Shading; // x = 0 banded, 1 smooth, 2 dithered; yz = the texture's world position; w = light count
    float4 Depth;   // x = the body's center z, y = range (px), z = how much the far side darkens
    float4 Tiling;  // x = tile size (px), y = tiles across, z = tiles down
    float4 Shadow;  // the tint the darkest band multiplies a piece's color by
    float4 Materials[2]; // flesh, metal: metallic, roughness, specular, 0
    float4 Lights[MAX_LIGHTS * 2]; // (texture-space position, height, radius), (color * intensity, 0)
};

// Mirrors SculptedBody.cs's BodyPiece.
struct Piece
{
    float3 A;
    float RadiusA;
    float3 B;
    float RadiusB;
    float Blend;
    uint Material;
    uint Carve;
    uint Rgb;
};

// No texture: fragment storage buffers follow the sampled textures, so these sit at the bottom of the range.
StructuredBuffer<Piece> Pieces : register(t0, space2);
// Per tile (row-major): its first entry and entry count; then every tile's entries, piece indices in draw order.
StructuredBuffer<uint> Bins : register(t1, space2);

static uint TileFirst;
static uint TileCount;

#include "Include/SculptedShading.hlsl"

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

float piece_distance(Piece piece, float3 p)
{
    return sd_round_cone(p, piece.A, piece.B, piece.RadiusA, piece.RadiusB);
}

float body_distance(float3 p)
{
    float d = 1e5;
    for (uint j = 0; j < TileCount; j++)
    {
        Piece piece = Pieces[Bins[TileFirst + j]];
        float di = piece_distance(piece, p);
        if (piece.Carve != 0)
            d = smooth_subtract(d, di, max(piece.Blend, 0.001));
        else
            d = piece.Blend > 0.0 ? smooth_union(d, di, piece.Blend) : min(d, di);
    }
    return d;
}

// The solid piece nearest the surface: whose color and material it shows.
Piece nearest_piece(float3 p)
{
    float nearest = 1e5;
    uint found = Bins[TileFirst];
    for (uint j = 0; j < TileCount; j++)
    {
        uint i = Bins[TileFirst + j];
        float di = piece_distance(Pieces[i], p);
        if (Pieces[i].Carve == 0 && di < nearest)
        {
            nearest = di;
            found = i;
        }
    }
    return Pieces[found];
}

float3 unpack_rgb(uint rgb)
{
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
    uint tileIndex = (uint)(tile.y * (int)Tiling.y + tile.x);
    TileFirst = (uint)(Tiling.y * Tiling.z) * 2 + Bins[tileIndex * 2];
    TileCount = Bins[tileIndex * 2 + 1];
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
    Piece piece = nearest_piece(position);
    float4 surface = Materials[piece.Material];
    float3 near = unpack_rgb(piece.Rgb);
    float3 far = near * Shadow.rgb;
    float threshold = sculpted_threshold(input.position.xy);
    float light = sculpted_light(normal, occlusion, surface.x, Depth.z * saturate((Depth.x - position.z) / Depth.y));
    output.Color = float4(sculpted_color(light, near, lerp(near, far, 0.5), far, threshold), 1.0);
    output.Specular = sculpted_specular(position, normal, near, surface, threshold);
    return output;
}
