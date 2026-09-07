// Advects the four neural-texture model weights while preserving the authored footprint.

StructuredBuffer<float4> SourceOwnership : register(t0, space0);
StructuredBuffer<uint> UpdateCoverageMask : register(t1, space0);
StructuredBuffer<uint> DecodeCoverageMask : register(t2, space0);
RWStructuredBuffer<float4> DestOwnership : register(u0, space1);
RWStructuredBuffer<uint> UpdateMask : register(u1, space1);
RWStructuredBuffer<uint> DecodeMask : register(u2, space1);

cbuffer Params : register(b0, space2)
{
    uint Width;
    uint Height;
    float Time;
    float DeltaTime;
    float CurlScale;
    float CurlSpeed;
    float BoundarySharpness;
    float Padding;
};

float4 unpack_mask(uint packed)
{
    return float4(
        float(packed & 0xFFu),
        float((packed >> 8u) & 0xFFu),
        float((packed >> 16u) & 0xFFu),
        float((packed >> 24u) & 0xFFu)) / 255.0;
}

// Quantize while preserving the authored total byte coverage exactly. Independently rounding
// four channels can erase faint one-byte silhouette edges after their ownership is mixed.
uint pack_mask(float4 ownership, uint coverageMask)
{
    uint coverageBytes = min(
        (coverageMask & 0xFFu) +
        ((coverageMask >> 8u) & 0xFFu) +
        ((coverageMask >> 16u) & 0xFFu) +
        ((coverageMask >> 24u) & 0xFFu),
        255u);
    float4 scaled = saturate(ownership) * float(coverageBytes);
    uint4 bytes = uint4(floor(scaled));
    uint remaining = coverageBytes - (bytes.x + bytes.y + bytes.z + bytes.w);
    float4 fractions = scaled - float4(bytes);
    for (uint i = 0u; i < 3u && remaining > 0u; i++)
    {
        uint best = 0u;
        if (fractions.y > fractions[best]) best = 1u;
        if (fractions.z > fractions[best]) best = 2u;
        if (fractions.w > fractions[best]) best = 3u;
        bytes[best]++;
        fractions[best] = -1.0;
        remaining--;
    }
    return bytes.x | (bytes.y << 8u) | (bytes.z << 16u) | (bytes.w << 24u);
}

float4 load_ownership(int2 pos)
{
    pos = clamp(pos, int2(0, 0), int2((int)Width - 1, (int)Height - 1));
    return SourceOwnership[pos.y * Width + pos.x];
}

float4 sample_ownership(float2 pos)
{
    pos = clamp(pos, 0.0, float2(Width - 1u, Height - 1u));
    int2 cell = int2(floor(pos));
    float2 blend = frac(pos);
    return lerp(
        lerp(load_ownership(cell), load_ownership(cell + int2(1, 0)), blend.x),
        lerp(load_ownership(cell + int2(0, 1)), load_ownership(cell + int2(1, 1)), blend.x),
        blend.y);
}

// Two analytic stream functions produce a changing, divergence-free velocity field without
// storing or pressure-projecting a separate velocity texture.
float2 curl_velocity(float2 pos)
{
    float extent = max(float(Width), float(Height));
    float2 p = (pos + 0.5) / extent;
    float frequency = max(CurlScale, 0.01) * 6.28318530718;
    float phase = Time * CurlSpeed * 0.12;
    float2 a = p * frequency + float2(phase, -phase * 0.73);
    float2 b = p.yx * frequency * 1.73 + float2(-phase * 0.41, phase * 0.57) + 2.13;
    float2 primary = float2(sin(a.x) * cos(a.y), -cos(a.x) * sin(a.y));
    float2 secondary = float2(-cos(b.x) * sin(b.y), sin(b.x) * cos(b.y));
    return (primary + secondary * 0.35) * CurlSpeed;
}

float4 normalize_or(float4 value, float4 fallback)
{
    float total = dot(value, 1.0);
    if (total > 0.00001)
        return max(value, 0.0) / total;
    total = dot(fallback, 1.0);
    return total > 0.00001 ? fallback / total : 0.0;
}

float4 sharpen(float4 ownership)
{
    float4 result = pow(max(ownership, 0.00001), max(BoundarySharpness, 0.01));
    return result / max(dot(result, 1.0), 0.00001);
}

[numthreads(8, 8, 1)]
void compute_main(uint3 id : SV_DispatchThreadID)
{
    if (id.x >= Width || id.y >= Height)
        return;

    uint index = id.y * Width + id.x;
    float4 authored = unpack_mask(UpdateCoverageMask[index]);
    float updateCoverage = dot(authored, 1.0);
    if (updateCoverage <= 0.0)
    {
        DestOwnership[index] = 0.0;
        UpdateMask[index] = 0u;
        DecodeMask[index] = 0u;
        return;
    }

    float2 pos = float2(id.xy);
    float2 sourcePos = pos - curl_velocity(pos) * DeltaTime;
    float4 ownership = normalize_or(sample_ownership(sourcePos), authored);

    // Counter the numerical diffusion inherent in semi-Lagrangian advection gently in the
    // persistent field; stronger sharpening is applied only to the packed masks below.
    float4 competitive = normalize_or(ownership * ownership, ownership);
    ownership = normalize_or(lerp(ownership, competitive,
        saturate((max(BoundarySharpness, 1.0) - 1.0) * DeltaTime)), authored);
    DestOwnership[index] = ownership;

    float4 packedOwnership = sharpen(ownership);
    UpdateMask[index] = pack_mask(packedOwnership, UpdateCoverageMask[index]);
    DecodeMask[index] = pack_mask(packedOwnership, DecodeCoverageMask[index]);
}
