// Cells2Pixels LPPN decoder. Bilinearly samples a coarse NCA state and a
// per-cell model mask, evaluates each painted model's local SIREN network,
// and blends the results. Outputs are premultiplied by total mask coverage,
// so unpainted cells are transparent.

#define CHANNEL_N 16
#define DECODER_N 32

struct CellState
{
    float4 c[4];
};

StructuredBuffer<CellState> SourceState : register(t0, space0);
StructuredBuffer<float4> Weights : register(t1, space0);
// Per-cell blend weights for up to four models, one byte per slot.
StructuredBuffer<uint> Mask : register(t2, space0);
#if BROWSER
[[vk::image_format("rgba8")]]
#endif
RWTexture2D<float4> Output : register(u0, space1);
#if BROWSER
[[vk::image_format("rgba8")]]
#endif
RWTexture2D<float4> NormalOutput : register(u1, space1);

cbuffer Params : register(b0, space2)
{
    uint Width;
    uint Height;
    uint OutputWidth;
    uint OutputHeight;
    uint NormalSlots; // bit per slot: model was trained with normal channels
    uint Pad0;
    uint Pad1;
    uint Pad2;
};

// Models are packed at a fixed stride in the six-channel layout.
#define MODEL_STRIDE 3466
#define L0_W_OFFSET 2720
#define L0_B_OFFSET 2880
#define L1_W_OFFSET 2888
#define L1_B_OFFSET 3144
#define L2_W_OFFSET 3152
#define L2_B_OFFSET 3408
#define L3_W_OFFSET 3416
#define L3_B_OFFSET 3464

float weight_value(int base, int offset, int index)
{
    return Weights[base + offset + index / 4][index % 4];
}

int2 clamp_cell(int2 pos)
{
    return clamp(pos, int2(0, 0), int2((int)Width - 1, (int)Height - 1));
}

CellState load_cell(int2 pos)
{
    pos = clamp_cell(pos);
    return SourceState[pos.y * Width + pos.x];
}

float4 load_mask(int2 pos)
{
    pos = clamp_cell(pos);
    uint mask = Mask[pos.y * Width + pos.x];
    return float4(
        float(mask & 0xFFu),
        float((mask >> 8u) & 0xFFu),
        float((mask >> 16u) & 0xFFu),
        float((mask >> 24u) & 0xFFu)) / 255.0;
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

    float4 m00 = load_mask(cellPos);
    float4 m10 = load_mask(cellPos + int2(1, 0));
    float4 m01 = load_mask(cellPos + int2(0, 1));
    float4 m11 = load_mask(cellPos + int2(1, 1));
    float4 slotAlpha = lerp(lerp(m00, m10, blend.x), lerp(m01, m11, blend.x), blend.y);
    float total = dot(slotAlpha, 1.0);

    if (total < 1.0 / 512.0)
    {
        Output[id.xy] = 0.0;
        NormalOutput[id.xy] = 0.0;
        return;
    }

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

    float3 color = 0.0;
    float3 normal = 0.0;
    float normalTotal = 0.0;

    for (uint slot = 0; slot < 4; slot++)
    {
        float alpha = slotAlpha[slot];
        if (alpha < 1.0 / 512.0)
            continue;

        int base = (int)slot * MODEL_STRIDE;
        float a[DECODER_N];
        float b[DECODER_N];
        [unroll]
        for (int output = 0; output < DECODER_N; output++)
        {
            float value = weight_value(base, L0_B_OFFSET, output);
            [unroll]
            for (int i = 0; i < 5; i++)
                value += dot(input[i], Weights[base + L0_W_OFFSET + output * 5 + i]);
            a[output] = sin(value * 10.0);
        }

        [unroll]
        for (int output = 0; output < DECODER_N; output++)
        {
            float value = weight_value(base, L1_B_OFFSET, output);
            [unroll]
            for (int i = 0; i < 8; i++)
                value += dot(float4(a[i * 4], a[i * 4 + 1], a[i * 4 + 2], a[i * 4 + 3]), Weights[base + L1_W_OFFSET + output * 8 + i]);
            b[output] = sin(value * 10.0);
        }

        [unroll]
        for (int output = 0; output < DECODER_N; output++)
        {
            float value = weight_value(base, L2_B_OFFSET, output);
            [unroll]
            for (int i = 0; i < 8; i++)
                value += dot(float4(b[i * 4], b[i * 4 + 1], b[i * 4 + 2], b[i * 4 + 3]), Weights[base + L2_W_OFFSET + output * 8 + i]);
            a[output] = sin(value * 10.0);
        }

        float slotNormal = (NormalSlots & (1u << slot)) != 0u ? 1.0 : 0.0;
        [unroll]
        for (int output = 0; output < 6; output++)
        {
            float value = weight_value(base, L3_B_OFFSET, output);
            [unroll]
            for (int i = 0; i < 8; i++)
                value += dot(float4(a[i * 4], a[i * 4 + 1], a[i * 4 + 2], a[i * 4 + 3]), Weights[base + L3_W_OFFSET + output * 8 + i]);
            if (output < 3)
                color[output] += alpha * saturate(value);
            else
                normal[output - 3] += alpha * slotNormal * saturate(value);
        }
        normalTotal += alpha * slotNormal;
    }

    // The batcher blends premultiplied alpha: weighted sums are already premultiplied.
    Output[id.xy] = float4(saturate(color), saturate(total));
    NormalOutput[id.xy] = float4(saturate(normal), saturate(normalTotal));
}
