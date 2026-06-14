// Copies the ripple state from Source to Dest, injecting an impulse around
// a hit point so RippleUpdate.hlsl's wave equation propagates it outward as
// a ring.

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
    float HitX;
    float HitY;
    float HitRadius;
    float HitStrength;
};

[numthreads(8, 8, 1)]
void compute_main(uint3 id : SV_DispatchThreadID)
{
    if (id.x >= Width || id.y >= Height)
        return;

    uint index = id.y * Width + id.x;
    RippleState result = SourceState[index];

    float dx = (float)id.x - HitX;
    float dy = (float)id.y - HitY;
    float dist = sqrt(dx * dx + dy * dy);

    if (dist < HitRadius)
    {
        float falloff = 1.0 - dist / HitRadius;
        result.H += HitStrength * falloff;
    }

    DestState[index] = result;
}
