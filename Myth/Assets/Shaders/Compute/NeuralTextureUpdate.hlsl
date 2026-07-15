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
    float2 WorldStateOrigin;
    float StatePixelsPerCell;
    uint Padding;
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

// Repeat the model's trained [-1, 1] coordinate domain at a fixed 128-cell wavelength.
// Mirroring alternate spans keeps the coordinate field continuous at patch boundaries.
float fixed_coordinate(float cell)
{
    float phase = frac((cell + 64.0) / 256.0) * 256.0;
    return 1.0 - abs(phase - 128.0) / 64.0;
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
    float total = float(
        (mask & 0xFFu) +
        ((mask >> 8u) & 0xFFu) +
        ((mask >> 16u) & 0xFFu) +
        ((mask >> 24u) & 0xFFu)) / 255.0;
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
    perception[16] = StatePixelsPerCell > 0.0
        ? float4(
            fixed_coordinate(WorldStateOrigin.y + float(pos.y)),
            fixed_coordinate(WorldStateOrigin.x + float(pos.x)),
            0.0,
            0.0)
        : float4(
            2.0 * ((float(pos.y) + 0.5) / float(Height) - 0.5),
            2.0 * ((float(pos.x) + 0.5) / float(Width) - 0.5),
            0.0,
            0.0);

    float4 delta[4];
    [unroll]
    for (int group = 0; group < 4; group++)
        delta[group] = 0.0;

    for (uint slot = 0; slot < 4; slot++)
    {
        float alpha = float((mask >> (slot * 8u)) & 0xFFu) / 255.0;
        if (alpha <= 0.0)
            continue;
        int base = (int)slot * MODEL_STRIDE;
        float hidden[HIDDEN_N];
        [unroll]
        for (int output = 0; output < HIDDEN_N; output++)
        {
            float value = weight_value(base, NCA_B1_OFFSET, output);
            [unroll]
            for (int input = 0; input < 17; input++)
                value += dot(perception[input], Weights[base + NCA_W1_OFFSET + output * 17 + input]);
            hidden[output] = max(value, 0.0);
        }

        for (int input = 0; input < HIDDEN_N; input++)
            [unroll]
            for (int group = 0; group < 4; group++)
                delta[group] += alpha * hidden[input] * Weights[base + NCA_W2_OFFSET + input * 4 + group];
    }

    CellState result;
    [unroll]
    for (int group = 0; group < 4; group++)
        result.c[group] = self.c[group] + delta[group];

    DestState[index] = result;
}
