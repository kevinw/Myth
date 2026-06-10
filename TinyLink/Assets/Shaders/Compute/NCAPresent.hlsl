// Writes the visible RGBA channels of the Neural CA state buffer into a
// regular color texture so it can be drawn with the normal sprite batcher.

#define CHANNEL_N 16

struct CellState
{
    float c[CHANNEL_N];
};

StructuredBuffer<CellState> SourceState : register(t0, space0);
RWTexture2D<float4> Output : register(u0, space1);

cbuffer Params : register(b0, space2)
{
    uint Width;
    uint Height;
};

[numthreads(8, 8, 1)]
void compute_main(uint3 id : SV_DispatchThreadID)
{
    if (id.x >= Width || id.y >= Height)
        return;

    CellState cell = SourceState[id.y * Width + id.x];

    float a = saturate(cell.c[3]);
    float3 rgb = saturate(float3(cell.c[0], cell.c[1], cell.c[2]));

    // Hidden channels 4-6 drive a soft additive shimmer/aura, so the
    // creature has a faint living glow even where the simulation has
    // otherwise settled.
    float3 glow = saturate(float3(cell.c[4], cell.c[5], cell.c[6]));
    float glowMax = max(glow.r, max(glow.g, glow.b));

    float3 rgbOut = rgb * a + glow * 0.35;
    float aOut = saturate(a + glowMax * 0.15);

    // Premultiplied alpha for correct blending.
    Output[id.xy] = float4(rgbOut, aOut);
}
