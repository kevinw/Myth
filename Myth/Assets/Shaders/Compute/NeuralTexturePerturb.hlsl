struct CellState
{
    float4 c[4];
};

struct PerturbStamp
{
    float2 From;
    float2 To;
    float Radius;
    float Strength;
    float2 Push;
};

StructuredBuffer<CellState> SourceState : register(t0, space0);
StructuredBuffer<PerturbStamp> Stamps : register(t1, space0);
RWStructuredBuffer<CellState> DestState : register(u0, space1);

cbuffer Params : register(b0, space2)
{
    uint Width;
    uint Height;
    uint StampCount;
    uint Padding;
};

float distance_to_segment(float2 position, float2 from, float2 to)
{
    float2 segment = to - from;
    float t = saturate(dot(position - from, segment) / max(dot(segment, segment), 0.0001));
    return length(position - (from + segment * t));
}

[numthreads(8, 8, 1)]
void compute_main(uint3 id : SV_DispatchThreadID)
{
    if (id.x >= Width || id.y >= Height)
        return;

    float erase = 0.0;
    float2 push = 0.0;
    float pushLengthSquared = 0.0;
    for (uint i = 0; i < StampCount; i++)
    {
        PerturbStamp stamp = Stamps[i];
        float influence = saturate(1.0 - distance_to_segment(float2(id.xy), stamp.From, stamp.To) / max(stamp.Radius, 0.0001));
        influence = influence * influence * (3.0 - 2.0 * influence);
        erase = max(erase, saturate(stamp.Strength) * influence);
        float2 displacement = stamp.Push * influence;
        float lengthSquared = dot(displacement, displacement);
        if (lengthSquared > pushLengthSquared)
        {
            push = displacement;
            pushLengthSquared = lengthSquared;
        }
    }

    uint index = id.y * Width + id.x;
    CellState result;
    [branch]
    if (pushLengthSquared > 0.000001)
    {
        // Backtrace through the wave, moving every latent channel together. Read only the
        // source buffer so cells never depend on compute-thread execution order.
        float2 source = clamp(float2(id.xy) - push, 0.0, float2(Width - 1, Height - 1));
        uint2 lo = uint2(floor(source));
        uint2 hi = min(lo + 1, uint2(Width - 1, Height - 1));
        float2 blend = frac(source);
        CellState a = SourceState[lo.y * Width + lo.x];
        CellState b = SourceState[lo.y * Width + hi.x];
        CellState c = SourceState[hi.y * Width + lo.x];
        CellState d = SourceState[hi.y * Width + hi.x];
        [unroll]
        for (int channel = 0; channel < 4; channel++)
            result.c[channel] = lerp(lerp(a.c[channel], b.c[channel], blend.x),
                lerp(c.c[channel], d.c[channel], blend.x), blend.y);
    }
    else
        result = SourceState[index];
    [unroll]
    for (int group = 0; group < 4; group++)
        result.c[group] *= 1.0 - erase;
    DestState[index] = result;
}
