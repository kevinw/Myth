// Cells2Pixels PENCA update for high-resolution dynamic textures.

#define CHANNEL_N 16
#define PERCEPT_N 68
#define HIDDEN_N 128

struct CellState
{
    float4 c[4];
};

StructuredBuffer<CellState> SourceState : register(t0, space0);
StructuredBuffer<float4> Weights : register(t1, space0);
// Per-cell blend weights for up to four models, one byte per slot.
StructuredBuffer<uint> Mask : register(t2, space0);
RWStructuredBuffer<CellState> DestState : register(u0, space1);

cbuffer Params : register(b0, space2)
{
    uint Width;
    uint Height;
    uint Seed;
    float FireRate;
    uint OriginX;
    uint OriginY;
    uint DispatchWidth;
    uint DispatchHeight;
    float2 FlowOrigin;
    float SimulationScale;
    float SmokeBlend;
    uint SmokeSlot;
    float Pad0;
    float2 FlowSize;
};

#define NCA_W1_OFFSET 0
#define NCA_B1_OFFSET 2176
#define NCA_W2_OFFSET 2208
// Models are packed at a fixed stride in the six-channel layout.
#define MODEL_STRIDE 3466
// Cells with no painted model decay toward the zero seed state.
#define EMPTY_DECAY 0.95

float weight_value(int base, int offset, int index)
{
    return Weights[base + offset + index / 4][index % 4];
}

float hash(uint x, uint y, uint seed)
{
    uint h = x * 374761393u + y * 668265263u + seed * 2654435761u;
    h = (h ^ (h >> 13)) * 1274126177u;
    h ^= h >> 16;
    return float(h & 0x00FFFFFFu) / float(0x01000000u);
}

// Repeat the flow domain with continuous mirrored boundaries.
float2 flow_coordinate(float2 position)
{
    float2 phase = frac((position - FlowOrigin) / FlowSize * 0.5);
    return 1.0 - abs(phase * 4.0 - 2.0);
}

CellState load_cell(int2 pos)
{
    pos = clamp(pos, int2(0, 0), int2((int)Width - 1, (int)Height - 1));
    return SourceState[pos.y * Width + pos.x];
}

[numthreads(8, 8, 1)]
void compute_main(uint3 id : SV_DispatchThreadID)
{
    if (id.x >= DispatchWidth || id.y >= DispatchHeight)
        return;

    int2 pos = int2(id.xy + uint2(OriginX, OriginY));
    uint index = pos.y * Width + pos.x;
    CellState self = SourceState[index];
    uint mask = Mask[index];
    float4 slotAlpha = float4(
        float(mask & 0xFFu),
        float((mask >> 8u) & 0xFFu),
        float((mask >> 16u) & 0xFFu),
        float((mask >> 24u) & 0xFFu)) / 255.0;
    // Shutdown eases the whole grid toward a single "just smoke" model.
    if (SmokeBlend > 0.0)
        slotAlpha = lerp(slotAlpha, float4(SmokeSlot == 0u, SmokeSlot == 1u, SmokeSlot == 2u, SmokeSlot == 3u), SmokeBlend);
    float total = dot(slotAlpha, float4(1.0, 1.0, 1.0, 1.0));
    float keep = lerp(EMPTY_DECAY, 1.0, saturate(total));
    bool update = total > 0.0 && hash(pos.x, pos.y, Seed) <= FireRate;
    if (!update)
    {
        CellState result;
        [unroll]
        for (int group = 0; group < 4; group++)
            result.c[group] = self.c[group] * keep;
        DestState[index] = result;
        return;
    }

    static const float sobel_x[3][3] = {
        { -1.0,  0.0, 1.0 },
        { -2.0,  0.0, 2.0 },
        { -1.0,  0.0, 1.0 },
    };
    static const float sobel_y[3][3] = {
        { -1.0, -2.0, -1.0 },
        {  0.0,  0.0,  0.0 },
        {  1.0,  2.0,  1.0 },
    };
    static const float laplacian[3][3] = {
        { 1.0,   2.0, 1.0 },
        { 2.0, -12.0, 2.0 },
        { 1.0,   2.0, 1.0 },
    };

    float4 perception[17];

    [unroll]
    for (int group = 0; group < 4; group++)
    {
        perception[group] = self.c[group];
        perception[4 + group] = 0.0;
        perception[8 + group] = 0.0;
        perception[12 + group] = 0.0;
    }

    [unroll]
    for (int dy = -1; dy <= 1; dy++)
    {
        [unroll]
        for (int dx = -1; dx <= 1; dx++)
        {
            CellState neighbor = load_cell(pos + int2(dx, dy));
            float kx = sobel_x[dy + 1][dx + 1];
            float ky = sobel_y[dy + 1][dx + 1];
            float kl = laplacian[dy + 1][dx + 1];
            [unroll]
            for (int group = 0; group < 4; group++)
            {
                perception[4 + group] += neighbor.c[group] * kx;
                perception[8 + group] += neighbor.c[group] * ky;
                perception[12 + group] += neighbor.c[group] * kl;
            }
        }
    }

    // PENCA appends normalized row then column coordinates. The JSON export pads
    // the two inputs to a vec4, so the last two values remain zero.
    perception[16] = float4(flow_coordinate((float2(pos) + 0.5) / SimulationScale).yx, 0.0, 0.0);

    float4 delta[4];
    [unroll]
    for (int group = 0; group < 4; group++)
        delta[group] = 0.0;

    for (uint slot = 0; slot < 4; slot++)
    {
        float alpha = slotAlpha[slot];
        if (alpha <= 0.0)
            continue;
        int base = (int)slot * MODEL_STRIDE;
        // Each hidden unit feeds the output layer as soon as it is produced. Holding all
        // HIDDEN_N of them in an array across two passes puts them in scratch, and unrolling
        // the outer loop inlines HIDDEN_N * 17 dot products -- together those are most of this
        // shader's compiled size, which is what a cold driver has to chew through on a first
        // run. The inner input loop stays unrolled: it is short, and it keeps the indices into
        // `perception` constant so that array stays in registers.
        for (int output = 0; output < HIDDEN_N; output++)
        {
            float value = weight_value(base, NCA_B1_OFFSET, output);
            [unroll]
            for (int input = 0; input < 17; input++)
                value += dot(perception[input], Weights[base + NCA_W1_OFFSET + output * 17 + input]);

            float hidden = max(value, 0.0);
            [unroll]
            for (int group = 0; group < 4; group++)
                delta[group] += alpha * hidden * Weights[base + NCA_W2_OFFSET + output * 4 + group];
        }
    }

    CellState result;
    [unroll]
    for (int group = 0; group < 4; group++)
        result.c[group] = self.c[group] + delta[group];

    // Backstop against runaway growth in the fullscreen smoke regime, where a synchronous model
    // has no dissipative margin to bleed off the high frequencies a player erase introduces.
    if (SmokeBlend > 0.0)
        [unroll]
        for (int clampGroup = 0; clampGroup < 4; clampGroup++)
            result.c[clampGroup] = clamp(result.c[clampGroup], -4.0, 4.0);

    DestState[index] = result;
}
