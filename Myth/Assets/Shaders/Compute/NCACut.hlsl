// Copies the CA state from Source to Dest, zeroing out cells within the
// given cut rectangle, e.g. from a player attack. The simulation will
// regrow into the cut region.
//
// This must be a full-grid copy (not an in-place partial write): SDL_GPU
// cycles read-write storage buffer bindings, which discards the buffer's
// previous contents outside of whatever this dispatch writes.

#define CHANNEL_N 16

struct CellState
{
    float c[CHANNEL_N];
};

StructuredBuffer<CellState> SourceState : register(t0, space0);
RWStructuredBuffer<CellState> DestState : register(u0, space1);

cbuffer Params : register(b0, space2)
{
    uint Width;
    uint Height;
    uint CutX;
    uint CutY;
    uint CutWidth;
    uint CutHeight;
    uint Seed;
};

float hash(uint x, uint y, uint seed)
{
    uint h = x * 374761393u + y * 668265263u + seed * 2654435761u;
    h = (h ^ (h >> 13)) * 1274126177u;
    h = h ^ (h >> 16);
    return float(h & 0x00FFFFFFu) / float(0x01000000u);
}

[numthreads(8, 8, 1)]
void compute_main(uint3 id : SV_DispatchThreadID)
{
    if (id.x >= Width || id.y >= Height)
        return;

    uint index = id.y * Width + id.x;

    // Distance outside the cut rect along the most-violated axis (negative
    // when inside). Perturbing this with noise before thresholding wobbles
    // the cut edge instead of leaving a perfectly straight rectangle.
    float dist = max(
        max((float)CutX - (float)id.x - 1.0, (float)id.x - ((float)CutX + (float)CutWidth)),
        max((float)CutY - (float)id.y - 1.0, (float)id.y - ((float)CutY + (float)CutHeight)));

    float noise = (hash(id.x, id.y, Seed) - 0.5) * 4.0;

    bool inCut = (dist + noise) < 0.0;

    if (inCut)
    {
        CellState zero = (CellState)0;
        DestState[index] = zero;
    }
    else
    {
        DestState[index] = SourceState[index];
    }
}
