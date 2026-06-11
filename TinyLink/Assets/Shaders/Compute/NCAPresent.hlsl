// Writes the visible RGBA channels of the Neural CA state buffer into a
// regular color texture so it can be drawn with the normal sprite batcher.

#define CHANNEL_N 16

struct CellState
{
    float c[CHANNEL_N];
};

struct RippleState
{
    float H;
    float V;
};

StructuredBuffer<CellState> SourceState : register(t0, space0);
StructuredBuffer<RippleState> Ripple : register(t1, space0);
#if BROWSER
[[vk::image_format("rgba8")]]
#endif
RWTexture2D<float4> Output : register(u0, space1);

cbuffer Params : register(b0, space2)
{
    uint Width;
    uint Height;
    float RippleDisplacement;
    float RippleTint;
};

RippleState load_ripple(int2 pos)
{
    if (pos.x < 0 || pos.y < 0 || pos.x >= (int)Width || pos.y >= (int)Height)
    {
        RippleState zero = (RippleState)0;
        return zero;
    }

    return Ripple[pos.y * Width + pos.x];
}

[numthreads(8, 8, 1)]
void compute_main(uint3 id : SV_DispatchThreadID)
{
    if (id.x >= Width || id.y >= Height)
        return;

    int2 pos = int2((int)id.x, (int)id.y);

    // Only displace cells that are themselves alive, so the ripple can
    // wobble/push the creature's existing pixels without smearing color
    // from alive neighbors into empty space.
    int2 samplePos = pos;
    if (saturate(SourceState[pos.y * Width + pos.x].c[3]) > 0.1)
    {
        float gx = load_ripple(pos + int2(1, 0)).H - load_ripple(pos + int2(-1, 0)).H;
        float gy = load_ripple(pos + int2(0, 1)).H - load_ripple(pos + int2(0, -1)).H;

        samplePos = pos + int2((int)round(gx * RippleDisplacement), (int)round(gy * RippleDisplacement));
        samplePos = clamp(samplePos, int2(0, 0), int2((int)Width - 1, (int)Height - 1));
    }

    CellState cell = SourceState[samplePos.y * Width + samplePos.x];

    float a = saturate(cell.c[3]);
    float3 rgb = saturate(float3(cell.c[0], cell.c[1], cell.c[2]));

    // Hidden channels 4-6 drive a soft additive shimmer/aura, so the
    // creature has a faint living glow even where the simulation has
    // otherwise settled.
    float3 glow = saturate(float3(cell.c[4], cell.c[5], cell.c[6]));
    float glowMax = max(glow.r, max(glow.g, glow.b));

    float3 rgbOut = rgb * a + glow * 0.35;
    float aOut = saturate(a + glowMax * 0.15);

    // Flash white where the ripple wave is currently passing through.
    float ripple = saturate(abs(load_ripple(pos).H) * RippleTint);
    rgbOut = lerp(rgbOut, float3(1.0, 1.0, 1.0) * aOut, ripple);

    // Premultiplied alpha for correct blending.
    Output[id.xy] = float4(rgbOut, aOut);
}
