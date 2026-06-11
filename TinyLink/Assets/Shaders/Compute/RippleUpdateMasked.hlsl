// Windowed discrete 2D wave equation step for NCALayer's persistent
// per-region ripple buffers. Identical math to RippleUpdate.hlsl, but only
// recomputes an "active rect" (Origin + dispatched group count) within the
// full Width x Height region buffer; cells outside that rect keep their
// previous state.

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
    uint OriginX;
    uint OriginY;
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
    int2 pos = int2((int)(OriginX + id.x), (int)(OriginY + id.y));
    if (pos.x >= (int)Width || pos.y >= (int)Height)
        return;

    uint index = pos.y * Width + pos.x;

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
