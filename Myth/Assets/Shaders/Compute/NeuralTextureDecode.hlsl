// Cells2Pixels LPPN decoder. Bilinearly samples a coarse NCA state and
// evaluates the local SIREN network for each output pixel.

#define CHANNEL_N 16
#define DECODER_N 32

struct CellState
{
    float4 c[4];
};

StructuredBuffer<CellState> SourceState : register(t0, space0);
StructuredBuffer<float4> Weights : register(t1, space0);
#if BROWSER
[[vk::image_format("rgba8")]]
#endif
RWTexture2D<float4> Output : register(u0, space1);

cbuffer Params : register(b0, space2)
{
    uint Width;
    uint Height;
    uint OutputWidth;
    uint OutputHeight;
};

#define L0_W_OFFSET 2720
#define L0_B_OFFSET 2880
#define L1_W_OFFSET 2888
#define L1_B_OFFSET 3144
#define L2_W_OFFSET 3152
#define L2_B_OFFSET 3408
#define L3_W_OFFSET 3416
#define L3_B_OFFSET 3440

float weight_value(int offset, int index)
{
    return Weights[offset + index / 4][index % 4];
}

CellState load_cell(int2 pos)
{
    pos = clamp(pos, int2(0, 0), int2((int)Width - 1, (int)Height - 1));
    return SourceState[pos.y * Width + pos.x];
}

[numthreads(8, 8, 1)]
void compute_main(uint3 id : SV_DispatchThreadID)
{
    if (id.x >= OutputWidth || id.y >= OutputHeight)
        return;

    float2 outputSize = float2(OutputWidth, OutputHeight);
    float extent = max(outputSize.x, outputSize.y);
    float2 uv = (float2(id.xy) + 0.5 - outputSize * 0.5) / extent + 0.5;

    float2 statePos = uv * float2(Width, Height) - 0.5;
    int2 cellPos = int2(floor(statePos));
    float2 blend = frac(statePos);
    CellState c00 = load_cell(cellPos);
    CellState c10 = load_cell(cellPos + int2(1, 0));
    CellState c01 = load_cell(cellPos + int2(0, 1));
    CellState c11 = load_cell(cellPos + int2(1, 1));

    float4 input[5];
    float2 patch = (frac(uv * float2(Width, Height)) - 0.5) * 2.0;
    // PyTorch meshgrid order is row, column: Y precedes X.
    input[0] = float4(
        sin(3.14159265359 * patch.y),
        sin(3.14159265359 * patch.x),
        cos(3.14159265359 * patch.y),
        cos(3.14159265359 * patch.x));
    [unroll]
    for (int group = 0; group < 4; group++)
    {
        float4 top = lerp(c00.c[group], c10.c[group], blend.x);
        float4 bottom = lerp(c01.c[group], c11.c[group], blend.x);
        input[1 + group] = lerp(top, bottom, blend.y);
    }

    float a[DECODER_N];
    float b[DECODER_N];
    [unroll]
    for (int output = 0; output < DECODER_N; output++)
    {
        float value = weight_value(L0_B_OFFSET, output);
        [unroll]
        for (int i = 0; i < 5; i++)
            value += dot(input[i], Weights[L0_W_OFFSET + output * 5 + i]);
        a[output] = sin(value * 10.0);
    }

    [unroll]
    for (int output = 0; output < DECODER_N; output++)
    {
        float value = weight_value(L1_B_OFFSET, output);
        [unroll]
        for (int i = 0; i < 8; i++)
            value += dot(float4(a[i * 4], a[i * 4 + 1], a[i * 4 + 2], a[i * 4 + 3]), Weights[L1_W_OFFSET + output * 8 + i]);
        b[output] = sin(value * 10.0);
    }

    [unroll]
    for (int output = 0; output < DECODER_N; output++)
    {
        float value = weight_value(L2_B_OFFSET, output);
        [unroll]
        for (int i = 0; i < 8; i++)
            value += dot(float4(b[i * 4], b[i * 4 + 1], b[i * 4 + 2], b[i * 4 + 3]), Weights[L2_W_OFFSET + output * 8 + i]);
        a[output] = sin(value * 10.0);
    }

    float3 color = 0.0;
    [unroll]
    for (int output = 0; output < 3; output++)
    {
        float value = weight_value(L3_B_OFFSET, output);
        [unroll]
        for (int i = 0; i < 8; i++)
            value += dot(float4(a[i * 4], a[i * 4 + 1], a[i * 4 + 2], a[i * 4 + 3]), Weights[L3_W_OFFSET + output * 8 + i]);
        color[output] = value;
    }

    Output[id.xy] = float4(saturate(color), 1.0);
}
