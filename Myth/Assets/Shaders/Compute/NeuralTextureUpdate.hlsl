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
RWStructuredBuffer<CellState> DestState : register(u0, space1);

cbuffer Params : register(b0, space2)
{
    uint Width;
    uint Height;
    uint Seed;
    float FireRate;
};

#define NCA_W1_OFFSET 0
#define NCA_B1_OFFSET 2176
#define NCA_W2_OFFSET 2208

float weight_value(int offset, int index)
{
    return Weights[offset + index / 4][index % 4];
}

float hash(uint x, uint y, uint seed)
{
    uint h = x * 374761393u + y * 668265263u + seed * 2654435761u;
    h = (h ^ (h >> 13)) * 1274126177u;
    h ^= h >> 16;
    return float(h & 0x00FFFFFFu) / float(0x01000000u);
}

CellState load_cell(int2 pos)
{
    pos = clamp(pos, int2(0, 0), int2((int)Width - 1, (int)Height - 1));
    return SourceState[pos.y * Width + pos.x];
}

[numthreads(8, 8, 1)]
void compute_main(uint3 id : SV_DispatchThreadID)
{
    if (id.x >= Width || id.y >= Height)
        return;

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

    int2 pos = int2(id.xy);
    uint index = id.y * Width + id.x;
    CellState self = SourceState[index];
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
    perception[16] = float4(
        2.0 * ((float(id.y) + 0.5) / float(Height) - 0.5),
        2.0 * ((float(id.x) + 0.5) / float(Width) - 0.5),
        0.0,
        0.0);

    float hidden[HIDDEN_N];
    [unroll]
    for (int output = 0; output < HIDDEN_N; output++)
    {
        float value = weight_value(NCA_B1_OFFSET, output);
        [unroll]
        for (int input = 0; input < 17; input++)
            value += dot(perception[input], Weights[NCA_W1_OFFSET + output * 17 + input]);
        hidden[output] = max(value, 0.0);
    }

    bool update = hash(id.x, id.y, Seed) <= FireRate;
    CellState result;
    float4 delta[4];
    [unroll]
    for (int group = 0; group < 4; group++)
        delta[group] = 0.0;
    for (int input = 0; input < HIDDEN_N; input++)
        [unroll]
        for (int group = 0; group < 4; group++)
            delta[group] += hidden[input] * Weights[NCA_W2_OFFSET + input * 4 + group];
    [unroll]
    for (int group = 0; group < 4; group++)
        result.c[group] = self.c[group] + (update ? delta[group] : 0.0);

    DestState[index] = result;
}
