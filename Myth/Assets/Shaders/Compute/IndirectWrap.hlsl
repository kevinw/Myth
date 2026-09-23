StructuredBuffer<float4> Field : register(t0, space0);
StructuredBuffer<float4> Surface : register(t1, space0);
StructuredBuffer<float4> Emission : register(t2, space0);
RWStructuredBuffer<float4> Wrapped : register(u0, space1);

cbuffer Params : register(b0, space2)
{
    uint GridWidth;
    uint GridHeight;
    float GridSpacing;
    float Depth;
};

[numthreads(64, 1, 1)]
void compute_main(uint3 id : SV_DispatchThreadID)
{
    if (id.x >= GridWidth * GridHeight)
        return;
    Wrapped[id.x] = 0.0;
    if (Depth <= 0.0 || Surface[id.x].a <= 0.5 || Emission[id.x].a <= 0.5)
        return;

    int2 cell = int2(id.x % GridWidth, id.x / GridWidth);
    float3 result = 0.0;
    for (int y = -1; y <= 1; y++)
    for (int x = -1; x <= 1; x++)
    {
        if (x == 0 && y == 0)
            continue;
        int2 direction = int2(x, y);
        float stepLength = length(float2(direction)) * GridSpacing;
        for (int step = 1; (step - 0.5) * stepLength < Depth; step++)
        {
            int2 neighbor = cell + direction * step;
            if (any(neighbor < 0) || neighbor.x >= GridWidth || neighbor.y >= GridHeight)
                break;
            uint index = neighbor.y * GridWidth + neighbor.x;
            if (Emission[index].a <= 0.5)
                break;
            if (Surface[index].a > 0.5)
                continue;
            // Stop at the first boundary, even when it is dark. This is a shading lookup,
            // never a propagation source, so it cannot carry light into the next cavity.
            float fade = saturate(1.0 - (step - 0.5) * stepLength / Depth);
            result = max(result, Field[index].rgb * fade * fade);
            break;
        }
    }
    Wrapped[id.x] = float4(result, 1.0);
}
