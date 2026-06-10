// Growing Neural Cellular Automata update step.
// https://distill.pub/2020/growing-ca/

#define CHANNEL_N 16
#define PERCEPT_N 48
#define HIDDEN_N 128

struct CellState
{
    float c[CHANNEL_N];
};

StructuredBuffer<CellState> SourceState : register(t0, space0);
StructuredBuffer<float> Weights : register(t1, space0);
RWStructuredBuffer<CellState> DestState : register(u0, space1);

cbuffer Params : register(b0, space2)
{
    uint Width;
    uint Height;
    uint Seed;
    float FireRate;
};

// Weight buffer layout:
//   w0: 48 x 128, offset 0      (index = i * 128 + j)
//   b0: 128,      offset 6144
//   w1: 128 x 16, offset 6272   (index = j * 16 + k)
//   b1: 16,       offset 8320
#define W0_OFFSET 0
#define B0_OFFSET 6144
#define W1_OFFSET 6272
#define B1_OFFSET 8320

float hash(uint x, uint y, uint seed)
{
    uint h = x * 374761393u + y * 668265263u + seed * 2654435761u;
    h = (h ^ (h >> 13)) * 1274126177u;
    h = h ^ (h >> 16);
    return float(h & 0x00FFFFFFu) / float(0x01000000u);
}

CellState load_cell(int2 pos)
{
    CellState cell;
    if (pos.x < 0 || pos.y < 0 || pos.x >= (int)Width || pos.y >= (int)Height)
    {
        [unroll]
        for (int i = 0; i < CHANNEL_N; i++)
            cell.c[i] = 0.0;
        return cell;
    }

    return SourceState[pos.y * Width + pos.x];
}

bool is_alive(int2 pos)
{
    // 3x3 max-pool of alpha (channel 3) > 0.1
    float m = -1.0;
    [unroll]
    for (int dy = -1; dy <= 1; dy++)
    {
        [unroll]
        for (int dx = -1; dx <= 1; dx++)
        {
            CellState n = load_cell(pos + int2(dx, dy));
            m = max(m, n.c[3]);
        }
    }
    return m > 0.1;
}

[numthreads(8, 8, 1)]
void compute_main(uint3 id : SV_DispatchThreadID)
{
    if (id.x >= Width || id.y >= Height)
        return;

    int2 pos = int2((int)id.x, (int)id.y);
    uint index = id.y * Width + id.x;

    if (!is_alive(pos))
    {
        CellState dead;
        [unroll]
        for (int i = 0; i < CHANNEL_N; i++)
            dead.c[i] = 0.0;
        DestState[index] = dead;
        return;
    }

    // Perception vector: per-channel [identity, sobelX, sobelY] interleaved.
    float perception[PERCEPT_N];

    static const float sobel_x[3][3] = {
        { -1.0 / 8.0, 0.0,  1.0 / 8.0 },
        { -2.0 / 8.0, 0.0,  2.0 / 8.0 },
        { -1.0 / 8.0, 0.0,  1.0 / 8.0 },
    };
    static const float sobel_y[3][3] = {
        { -1.0 / 8.0, -2.0 / 8.0, -1.0 / 8.0 },
        {  0.0,        0.0,        0.0       },
        {  1.0 / 8.0,  2.0 / 8.0,  1.0 / 8.0 },
    };

    float identity[CHANNEL_N];
    float gx[CHANNEL_N];
    float gy[CHANNEL_N];

    [unroll]
    for (int i = 0; i < CHANNEL_N; i++)
    {
        identity[i] = 0.0;
        gx[i] = 0.0;
        gy[i] = 0.0;
    }

    CellState self = load_cell(pos);
    [unroll]
    for (int i = 0; i < CHANNEL_N; i++)
        identity[i] = self.c[i];

    [unroll]
    for (int dy = -1; dy <= 1; dy++)
    {
        [unroll]
        for (int dx = -1; dx <= 1; dx++)
        {
            float kx = sobel_x[dy + 1][dx + 1];
            float ky = sobel_y[dy + 1][dx + 1];
            if (kx == 0.0 && ky == 0.0)
                continue;

            CellState n = load_cell(pos + int2(dx, dy));
            [unroll]
            for (int i = 0; i < CHANNEL_N; i++)
            {
                gx[i] += kx * n.c[i];
                gy[i] += ky * n.c[i];
            }
        }
    }

    [unroll]
    for (int i = 0; i < CHANNEL_N; i++)
    {
        perception[i * 3 + 0] = identity[i];
        perception[i * 3 + 1] = gx[i];
        perception[i * 3 + 2] = gy[i];
    }

    // Dense(48 -> 128) + ReLU
    float hidden[HIDDEN_N];
    [unroll]
    for (int j = 0; j < HIDDEN_N; j++)
    {
        float sum = Weights[B0_OFFSET + j];
        [unroll]
        for (int i = 0; i < PERCEPT_N; i++)
            sum += perception[i] * Weights[W0_OFFSET + i * HIDDEN_N + j];
        hidden[j] = max(sum, 0.0);
    }

    // Dense(128 -> 16), linear
    float delta[CHANNEL_N];
    [unroll]
    for (int k = 0; k < CHANNEL_N; k++)
    {
        float sum = Weights[B1_OFFSET + k];
        [unroll]
        for (int j = 0; j < HIDDEN_N; j++)
            sum += hidden[j] * Weights[W1_OFFSET + j * CHANNEL_N + k];
        delta[k] = sum;
    }

    // Stochastic update mask (per-cell, per-step).
    bool update = hash(id.x, id.y, Seed) <= FireRate;

    CellState result;
    [unroll]
    for (int i = 0; i < CHANNEL_N; i++)
        result.c[i] = self.c[i] + (update ? delta[i] : 0.0);

    // Inject a tiny amount of noise into the hidden channels every step so
    // the simulation never fully settles into a static image - it keeps a
    // faint living shimmer (visible via the glow in NCAPresent.hlsl).
    [unroll]
    for (int h = 4; h < CHANNEL_N; h++)
    {
        float n = hash(id.x, id.y, Seed + 1000u + (uint)h * 97u) - 0.5;
        result.c[h] += n * 0.01;
    }

    DestState[index] = result;
}
