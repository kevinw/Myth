#include "../Common.hlsl"

#define POINT_SHADOW_SAMPLES 512
#define MAX_LIGHTS 64
#define SUN_SHADOW_ROW MAX_LIGHTS

struct Light
{
    float2 Position;
    float Radius;
    float Height;
    float4 ColorIntensity;
};

Texture2D NormalTexture : register(t0, space0);
SamplerState NormalSampler : register(s0, space0);
StructuredBuffer<Light> Lights : register(t1, space0);
StructuredBuffer<float> ShadowDepth : register(t2, space0);

#if BROWSER
[[vk::image_format("rgba8")]]
#endif
RWTexture2D<float4> BackgroundMask : register(u0, space1);
#if BROWSER
[[vk::image_format("rgba8")]]
#endif
RWTexture2D<float4> ForegroundMask : register(u1, space1);

cbuffer Params : register(b0, space2)
{
    uint Width;
    uint Height;
    uint LightCount;
    uint SunEnabled;
    float2 Camera;
    float Zoom;
    float ShadowBias;
    float4 Ambient;
    float2 SunDirection;
    float SunStrength;
    float SoftShadowSamples;
    float4 SunColor;
    float2 SunPerpendicular;
    float SunPerpendicularMin;
    float SunPerpendicularMax;
    float SunAlongMin;
    float SunAlongMax;
    float SunSoftness;
    float MinimumSoftness;
};

float normal_response(float3 normal, float normalStrength, float3 lightDirection)
{
    float diffuse = saturate(dot(normal, normalize(lightDirection)));
    return lerp(1.0, 0.12 + diffuse * 2.15, normalStrength);
}

float point_visibility(uint lightIndex, float2 delta, float distanceToLight, float radius)
{
    float angle = atan2(delta.y, delta.x);
    if (angle < 0.0)
        angle += 6.28318530718;
    float samplePosition = angle / 6.28318530718 * POINT_SHADOW_SAMPLES - 0.5;
    int center = (int)round(samplePosition);
    if (SoftShadowSamples <= 0.0)
    {
        int sampleIndex = center % POINT_SHADOW_SAMPLES;
        if (sampleIndex < 0)
            sampleIndex += POINT_SHADOW_SAMPLES;
        return distanceToLight <= ShadowDepth[lightIndex * POINT_SHADOW_SAMPLES + sampleIndex] + ShadowBias ? 1.0 : 0.0;
    }
    float radialSoftness = lerp(MinimumSoftness, 1.0, saturate(distanceToLight / radius));
    int stepSize = max(1, (int)round(SoftShadowSamples * radialSoftness));
    float visibility = 0.0;
    static const int offsets[5] = { -2, -1, 0, 1, 2 };
    static const float weights[5] = { 0.1, 0.2, 0.4, 0.2, 0.1 };
    for (uint i = 0; i < 5; i++)
    {
        int sampleIndex = (center + offsets[i] * stepSize) % POINT_SHADOW_SAMPLES;
        if (sampleIndex < 0)
            sampleIndex += POINT_SHADOW_SAMPLES;
        float depth = ShadowDepth[lightIndex * POINT_SHADOW_SAMPLES + sampleIndex];
        visibility += weights[i] * (distanceToLight <= depth + ShadowBias ? 1.0 : 0.0);
    }
    return visibility;
}

float sun_visibility(float2 world)
{
    float across = dot(world, SunPerpendicular);
    float samplePosition = saturate((across - SunPerpendicularMin) / (SunPerpendicularMax - SunPerpendicularMin));
    int center = (int)round(samplePosition * (POINT_SHADOW_SAMPLES - 1));
    if (SunSoftness <= 0.0)
    {
        float depth = ShadowDepth[SUN_SHADOW_ROW * POINT_SHADOW_SAMPLES + center];
        return dot(world, SunDirection) <= depth + ShadowBias ? 1.0 : 0.0;
    }
    int stepSize = max(1, (int)round(SunSoftness));
    float along = dot(world, SunDirection);
    float visibility = 0.0;
    static const int offsets[5] = { -2, -1, 0, 1, 2 };
    static const float weights[5] = { 0.1, 0.2, 0.4, 0.2, 0.1 };
    for (uint i = 0; i < 5; i++)
    {
        int sampleIndex = clamp(center + offsets[i] * stepSize, 0, POINT_SHADOW_SAMPLES - 1);
        float depth = ShadowDepth[SUN_SHADOW_ROW * POINT_SHADOW_SAMPLES + sampleIndex];
        visibility += weights[i] * (along <= depth + ShadowBias ? 1.0 : 0.0);
    }
    return visibility;
}

[numthreads(8, 8, 1)]
void compute_main(uint3 id : SV_DispatchThreadID)
{
    if (id.x >= Width || id.y >= Height)
        return;

    float2 uv = (float2(id.xy) + 0.5) / float2(Width, Height);
    float2 world = Camera + (float2(id.xy) + 0.5) / Zoom;
    float4 normalData = NormalTexture.SampleLevel(NormalSampler, target_uv(uv), 0.0);
    float normalStrength = normalData.a;
    float3 normal = normalize(normalData.rgb * 2.0 - 1.0);
    float4 shadowed = Ambient;
    float4 unshadowed = Ambient;

    if (SunEnabled != 0)
    {
        float response = normal_response(normal, normalStrength, float3(-SunDirection, 1.0));
        float4 contribution = float4(SunColor.rgb * SunStrength * response, 1.0);
        unshadowed += contribution;
        shadowed += contribution * sun_visibility(world);
    }

    for (uint i = 0; i < LightCount; i++)
    {
        Light light = Lights[i];
        float2 toLight = light.Position - world;
        float distanceToLight = length(toLight);
        float attenuation = saturate(1.0 - distanceToLight / light.Radius);
        attenuation *= attenuation;
        if (attenuation <= 0.0)
            continue;

        float response = normal_response(normal, normalStrength, float3(toLight, light.Height));
        float4 contribution = float4(light.ColorIntensity.rgb * light.ColorIntensity.a * attenuation * response, attenuation);
        unshadowed += contribution;
        shadowed += contribution * point_visibility(i, -toLight, distanceToLight, light.Radius);
    }

    BackgroundMask[id.xy] = saturate(shadowed);
    ForegroundMask[id.xy] = saturate(unshadowed);
}
