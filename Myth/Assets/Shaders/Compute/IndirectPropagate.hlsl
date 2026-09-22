StructuredBuffer<float4> Emission : register(t0, space0);
StructuredBuffer<float4> Surface : register(t1, space0);
StructuredBuffer<float4> Previous : register(t2, space0);
RWStructuredBuffer<float4> Field : register(u0, space1);

cbuffer Params : register(b0, space2)
{
    uint GridWidth;
    uint GridHeight;
    float CardinalDecay;
    float DiagonalDecay;
};

uint cell_index(int2 cell)
{
    return cell.y * GridWidth + cell.x;
}

bool in_bounds(int2 cell)
{
    return all(cell >= 0) && cell.x < GridWidth && cell.y < GridHeight;
}

bool open_cell(int2 cell)
{
    if (!in_bounds(cell))
        return false;
    uint index = cell_index(cell);
    return Emission[index].a > 0.5 && Surface[index].a <= 0.5;
}

[numthreads(64, 1, 1)]
void compute_main(uint3 id : SV_DispatchThreadID)
{
    uint cellCount = GridWidth * GridHeight;
    if (id.x >= cellCount)
        return;

    int2 cell = int2(id.x % GridWidth, id.x / GridWidth);
    if (!open_cell(cell))
    {
        Field[id.x] = 0.0;
        return;
    }

    float3 result = Previous[id.x].rgb;
    for (int y = -1; y <= 1; y++)
    {
        for (int x = -1; x <= 1; x++)
        {
            if (x == 0 && y == 0)
                continue;

            int2 neighbor = cell + int2(x, y);
            if (!in_bounds(neighbor))
                continue;
            if (x != 0 && y != 0 && (!open_cell(cell + int2(x, 0)) || !open_cell(cell + int2(0, y))))
                continue;

            uint neighborIndex = cell_index(neighbor);
            float4 source = Emission[neighborIndex];
            if (source.a <= 0.5)
                continue;
            float3 radiance = source.rgb;
            if (Surface[neighborIndex].a <= 0.5)
                radiance = max(radiance, Previous[neighborIndex].rgb);
            result = max(result, radiance * (x == 0 || y == 0 ? CardinalDecay : DiagonalDecay));
        }
    }

    Field[id.x] = float4(result, 1.0);
}
