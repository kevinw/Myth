// Discrete 2D wave equation step for the ripple displacement field used by
// NeuralCellularActor. Each cell stores a height H and velocity V; impulses
// injected by RippleHit.hlsl propagate outward as a decaying ring, which
// NCAPresent.hlsl uses to push/tint the rendered cells.

struct RippleState
{
    float H;
    float V;
};

StructuredBuffer<RippleState> SourceState : register(t0, space0);
RWStructuredBuffer<RippleState> DestState : register(u0, space1);

cbuffer Params : register(b0, space2)
{
    uint Width;
    uint Height;
    float Speed;
    float Damping;
};

RippleState load(int2 pos)
{
    if (pos.x < 0 || pos.y < 0 || pos.x >= (int)Width || pos.y >= (int)Height)
    {
        RippleState zero = (RippleState)0;
        return zero;
    }

    return SourceState[pos.y * Width + pos.x];
}

[numthreads(8, 8, 1)]
void compute_main(uint3 id : SV_DispatchThreadID)
{
    if (id.x >= Width || id.y >= Height)
        return;

    int2 pos = int2((int)id.x, (int)id.y);
    uint index = id.y * Width + id.x;

    RippleState self = load(pos);
    float laplacian =
        load(pos + int2(-1, 0)).H +
        load(pos + int2(1, 0)).H +
        load(pos + int2(0, -1)).H +
        load(pos + int2(0, 1)).H -
        4.0 * self.H;

    RippleState result;
    result.V = (self.V + Speed * Speed * laplacian) * Damping;
    result.H = self.H + result.V;

    DestState[index] = result;
}
