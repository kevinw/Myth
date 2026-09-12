struct CellState { float4 c[4]; };
StructuredBuffer<CellState> Source : register(t0, space0);
RWStructuredBuffer<CellState> Destination : register(u0, space1);

cbuffer Params : register(b0, space2)
{
    int2 SourceSize;
    int2 DestinationSize;
    float2 Offset;
    float Scale;
    float Padding;
};

CellState load_cell(int2 p)
{
    if (any(p < 0) || any(p >= SourceSize))
        return (CellState)0;
    return Source[p.y * SourceSize.x + p.x];
}

[numthreads(8, 8, 1)]
void compute_main(uint3 id : SV_DispatchThreadID)
{
    if (any(id.xy >= uint2(DestinationSize)))
        return;
    float2 p = (float2(id.xy) + 0.5) * Scale + Offset - 0.5;
    int2 cell = int2(floor(p));
    float2 blend = frac(p);
    CellState a = load_cell(cell);
    CellState b = load_cell(cell + int2(1, 0));
    CellState c = load_cell(cell + int2(0, 1));
    CellState d = load_cell(cell + int2(1, 1));
    CellState result;
    [unroll]
    for (int i = 0; i < 4; i++)
        result.c[i] = lerp(lerp(a.c[i], b.c[i], blend.x), lerp(c.c[i], d.c[i], blend.x), blend.y);
    Destination[id.y * DestinationSize.x + id.x] = result;
}
