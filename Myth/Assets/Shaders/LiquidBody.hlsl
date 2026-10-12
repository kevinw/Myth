#include "Common.hlsl"

// Liquid body (see LiquidBody.cs). TexCoord is the fragment's WORLD pixel and Color.r its depth below
// the surface in px / 255, both fed by the CPU; Color.g is the share of pixels drawn, dithered. Smooth noise, warped by a slower copy of itself, is cut
// into band lines over a depth gradient: on lava, thin dark crust streaks that flow sideways. Water
// instead draws a caustic web: the edges of slowly wandering cells, bright, like light through ripples. The shared stir field washes the lines out and brightens the body. Quantized to a
// four-color palette (alpha included, so water stays translucent) to stay pixel art.

cbuffer VertexUniformBlock : register(b0, space1)
{
    float4x4 Matrix;
};

cbuffer FragmentUniformBlock : register(b0, space3)
{
    float4 Meta;       // x = flow clock (seconds), y = world_output phase, z = depth (px) of the full depth drop, w = sideways stretch
    float4 Flow;       // x = noise feature size (px), y = drift speed (px / flow second), z = line half-width (world px), w = lines per noise swing
    float4 Shape;      // x = level at the surface, y = level lost at full depth, z = noise contrast, w = level a line takes away (negative adds)
    float4 StirRect;   // xy = stir field world top-left, zw = world size
    float4 Palette[4]; // deepest -> hottest/brightest
    float4 Stir;       // x = stir brightening, y = dither at band edges, z = how much of each line is gap, w = caustic cell size (px; 0 = flow lines)
};

// The stir field rides in the batcher's texture slot.
Texture2D StirField : register(t0, space2);
SamplerState StirSampler : register(s0, space2);

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

float hash(float2 p)
{
    p = frac(p * float2(127.1, 311.7));
    p += dot(p, p + 19.19);
    return frac(p.x * p.y);
}

float value_noise(float2 p)
{
    float2 i = floor(p);
    float2 f = frac(p);
    f = f * f * (3.0 - 2.0 * f);
    return lerp(lerp(hash(i), hash(i + float2(1, 0)), f.x),
                lerp(hash(i + float2(0, 1)), hash(i + float2(1, 1)), f.x), f.y);
}

float fbm(float2 p)
{
    return value_noise(p) * 0.65 + value_noise(p * 2.03 + 17.0) * 0.35;
}

float2 hash2(float2 p)
{
    return frac(sin(float2(dot(p, float2(127.1, 311.7)), dot(p, float2(269.5, 183.3)))) * 43758.5453);
}

// Distance (px) to the nearest edge between wandering cells: the caustic web.
float caustic_edge(float2 world, float cellSize, float phase)
{
    float2 p = world / cellSize;
    float2 cell = floor(p);
    float nearest = 8.0, second = 8.0;
    for (int y = -1; y <= 1; y++)
        for (int x = -1; x <= 1; x++)
        {
            float2 o = float2(x, y);
            float2 h = hash2(cell + o);
            float d = length(o + 0.5 + 0.4 * sin(phase + 6.2831 * h) - frac(p));
            second = d < nearest ? nearest : min(second, d);
            nearest = min(nearest, d);
        }
    return (second - nearest) * 0.5 * cellSize;
}

// Warped, stretched noise: the coarse copy bends the fine one, so contours fold like flow lines.
float flow_noise(float2 world, float drift)
{
    float2 p = world / Flow.x * float2(Meta.w, 1.0);
    float warp = fbm(p * 0.6 + float2(drift * 0.4, drift * 0.15));
    return fbm(p + float2(warp * 2.0 - drift, warp * 0.8));
}

// 4x4 ordered dither, so heat bands meet in a pixel-art crosshatch instead of a hard edge.
float bayer(float2 pixel)
{
    int2 i = int2(fmod(pixel, 4.0));
    int index = i.x + i.y * 4;
    const float m[16] = { 0, 8, 2, 10, 12, 4, 14, 6, 3, 11, 1, 9, 15, 7, 13, 5 };
    return (m[index] + 0.5) / 16.0;
}

WorldOutput fragment_main(VsOutput input)
{
    float2 world = floor(input.TexCoord) + 0.5; // one value per world pixel, so it stays crisp under zoom
    float drift = Meta.x * Flow.y / Flow.x;

    // Flow lines are bands of the noise, held to a fixed width in world pixels by the local gradient.
    // A caustic web bends its cells by the same noise so they wobble instead of sitting on a grid.
    float n = flow_noise(world, drift);
    float distancePx;
    if (Stir.w > 0.0)
        distancePx = caustic_edge(world + (n - 0.5) * Stir.w * 0.6, Stir.w, Meta.x * Flow.y / Stir.w);
    else
    {
        float2 slope = float2(flow_noise(world + float2(1, 0), drift), flow_noise(world + float2(0, 1), drift)) - n;
        distancePx = abs(frac(n * Flow.w) - 0.5) / max(length(slope) * Flow.w, 1e-4);
    }
    // A second, faster noise breaks the lines into drifting fragments.
    float breakup = value_noise(world / Flow.x * float2(0.5, 1.5) + float2(drift * 1.7, 0.0));
    float onLine = distancePx < Flow.z && breakup > Stir.z ? 1.0 : 0.0;

    float depth = saturate(input.Color.r * 255.0 / Meta.z);
    float2 stirUv = (world - StirRect.xy) / StirRect.zw;
    float stir = all(stirUv == saturate(stirUv)) ? StirField.Sample(StirSampler, stirUv).r : 0.0;

    float heat = Shape.x - depth * Shape.y + (n - 0.5) * Shape.z
        - onLine * (1.0 - stir) * Shape.w * (Stir.w > 0.0 ? 1.0 - depth * 0.7 : 1.0) // caustics dim with depth
        + stir * Stir.x
        + (bayer(world) - 0.5) * Stir.y;
    float level = saturate(heat) * 4.0;
    float4 color = level < 1.0 ? Palette[0] : level < 2.0 ? Palette[1] : level < 3.0 ? Palette[2] : Palette[3];
    color *= bayer(world + 2.0) < input.Color.g ? 1.0 : 0.0;
    return world_output(color, Meta.y);
}
