#define CHANNEL_N 16

struct CellState
{
    float4 c[4];
};

StructuredBuffer<CellState> SourceState : register(t0, space0);
StructuredBuffer<uint> Mask : register(t1, space0);
RWStructuredBuffer<float4> Result : register(u0, space1);

cbuffer Params : register(b0, space2)
{
    uint Width;
    uint Height;
    float DensityLow;
    float DensityHigh;
    float SmokeBlend;
    uint Padding0;
    uint Padding1;
    uint Padding2;
    float2 Head;
    float2 Torso;
    float2 Feet;
    float2 BelowFeet;
};

int2 clamp_cell(int2 pos)
{
    return clamp(pos, int2(0, 0), int2((int)Width - 1, (int)Height - 1));
}

CellState load_cell(int2 pos)
{
    pos = clamp_cell(pos);
    return SourceState[pos.y * Width + pos.x];
}

float mask_coverage(int2 pos)
{
    pos = clamp_cell(pos);
    uint mask = Mask[pos.y * Width + pos.x];
    float4 slots = float4(
        float(mask & 0xFFu),
        float((mask >> 8u) & 0xFFu),
        float((mask >> 16u) & 0xFFu),
        float((mask >> 24u) & 0xFFu)) / 255.0;
    return saturate(dot(slots, 1.0));
}

float density_at(float2 statePos)
{
    if (any(statePos < 0.0) || statePos.x > Width - 1 || statePos.y > Height - 1)
        return 0.0;

    int2 cellPos = int2(floor(statePos));
    float2 blend = frac(statePos);
    CellState c00 = load_cell(cellPos);
    CellState c10 = load_cell(cellPos + int2(1, 0));
    CellState c01 = load_cell(cellPos + int2(0, 1));
    CellState c11 = load_cell(cellPos + int2(1, 1));
    float energy = 0.0;
    [unroll]
    for (int group = 0; group < 4; group++)
    {
        float4 latent = lerp(
            lerp(c00.c[group], c10.c[group], blend.x),
            lerp(c01.c[group], c11.c[group], blend.x), blend.y);
        energy += dot(latent, latent);
    }

    float coverage = lerp(
        lerp(mask_coverage(cellPos), mask_coverage(cellPos + int2(1, 0)), blend.x),
        lerp(mask_coverage(cellPos + int2(0, 1)), mask_coverage(cellPos + int2(1, 1)), blend.x), blend.y);
    coverage = lerp(coverage, 1.0, SmokeBlend);
    return coverage * smoothstep(DensityLow, max(DensityHigh, DensityLow + 0.0001), sqrt(energy / CHANNEL_N));
}

[numthreads(1, 1, 1)]
void compute_main(uint3 id : SV_DispatchThreadID)
{
    Result[0] = float4(density_at(Head), density_at(Torso), density_at(Feet), density_at(BelowFeet));
}
