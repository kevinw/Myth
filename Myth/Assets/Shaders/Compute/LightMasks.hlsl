#include "Common.hlsl"

#define POINT_SHADOW_SAMPLES 512
#define MAX_LIGHTS 64
#define SUN_SHADOW_ROW MAX_LIGHTS

struct Light
{
    float2 Position;
    float Radius;
    float Height;
    float4 ColorIntensity;
    float4 Cookie;
    float4 CookieShape;
    float4 Shadow;
};

Texture2D NormalTexture : register(t0, space0);
SamplerState NormalSampler : register(s0, space0);
Texture2D SporeNoise : register(t1, space0);
SamplerState SporeNoiseSampler : register(s1, space0);
StructuredBuffer<Light> Lights : register(t2, space0);
StructuredBuffer<float2> ShadowDepth : register(t3, space0);

#if BROWSER
[[vk::image_format("rgba8")]]
#endif
RWTexture2D<float4> BackgroundMask : register(u0, space1);
#if BROWSER
[[vk::image_format("rgba8")]]
#endif
RWTexture2D<float4> ForegroundMask : register(u1, space1);
#if BROWSER
[[vk::image_format("rgba8")]]
#endif
RWTexture2D<float4> SporeFogMask : register(u2, space1);

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
    float PointShadowPadding;
    float SpriteShadowStrength;
    float SpriteShadowMinLength;
    float SpriteShadowMaxLength;
    float SpriteShadowPadding;
    float4 SporeFogTint;
    float2 SporeFogDrift;
    float Time;
    float SporeFogStrength;
    float SporeFogScale;
    float SporeFogContrast;
    float SporeFogPointStrength;
    float SporeFogSunStrength;
};

float normal_response(float3 normal, float normalStrength, float3 lightDirection)
{
    float diffuse = saturate(dot(normal, normalize(lightDirection)));
    return lerp(1.0, 0.12 + diffuse * 2.15, normalStrength);
}

float box_distance(float2 localPosition, float2 halfSize)
{
    float2 offset = abs(localPosition) - halfSize;
    return length(max(offset, 0.0)) + min(max(offset.x, offset.y), 0.0);
}

float light_cookie(Light light, float2 delta)
{
    int mode = (int)round(light.Cookie.x);
    if (mode == 0 || light.CookieShape.x <= 0.0)
        return 1.0;

    float rotation = light.Cookie.y + light.Cookie.z * Time;
    float sine = sin(rotation);
    float cosine = cos(rotation);
    float2 localPosition = float2(
        cosine * delta.x + sine * delta.y,
        -sine * delta.x + cosine * delta.y) / max(light.Radius, 0.0001);
    localPosition *= max(abs(light.Cookie.w), 0.01);
    float radius = length(localPosition);
    float angle = atan2(localPosition.y, localPosition.x);
    float feather = clamp(light.CookieShape.y, 0.001, 0.5);
    float mask = 1.0;

    if (mode == 1)
    {
        float cap = length((localPosition - float2(0.18, 0.0)) * float2(1.0, 1.75)) - 0.62;
        float stem = box_distance(localPosition + float2(0.28, 0.0), float2(0.34, 0.13));
        mask = 1.0 - smoothstep(-feather, feather, min(cap, stem));
    }
    else if (mode == 2)
    {
        float lobes = max(1.0, 5.0 * abs(light.Cookie.w));
        float tendrils = abs(sin(angle * lobes + radius * 18.0));
        mask = smoothstep(0.45 - feather, 0.55 + feather, tendrils);
    }
    else if (mode == 3)
    {
        float width = 0.42 * (1.0 - saturate(localPosition.x) * 0.7);
        width *= 0.82 + 0.18 * sin(localPosition.x * 22.0 + angle * 3.0);
        float body = abs(localPosition.y) - width;
        float ends = max(-localPosition.x - 0.22, localPosition.x - 1.0);
        mask = 1.0 - smoothstep(-feather, feather, max(body, ends));
    }
    else if (mode == 4)
    {
        float halfAngle = clamp(0.78 / max(abs(light.Cookie.w), 0.01), 0.08, 1.5);
        float direction = localPosition.x / max(radius, 0.0001);
        mask = smoothstep(cos(halfAngle) - feather, cos(halfAngle) + feather, direction);
    }
    else if (mode == 5)
    {
        float frequency = max(1.0, abs(light.Cookie.w));
        float angular = sin(angle * 3.1 * frequency + sin(angle * 7.3) * 1.2);
        float radial = sin(radius * 14.0 * frequency - angle * 2.0);
        mask = smoothstep(-feather, feather, angular * 0.65 + radial * 0.35 - 0.1);
    }

    return lerp(1.0, mask, saturate(light.CookieShape.x));
}

float interleaved_gradient_noise(float2 pixel, float frame)
{
    return frac(52.9829189 * frac(dot(pixel, float2(0.06711056, 0.00583715)) + frame * 0.75487766));
}

float point_sample_visibility(uint lightIndex, int sampleIndex, float distanceToLight, float radius)
{
    float2 depth = ShadowDepth[lightIndex * POINT_SHADOW_SAMPLES + sampleIndex];
    if (distanceToLight > depth.x + ShadowBias)
        return 0.0;
    if (depth.y >= radius || distanceToLight <= depth.y + ShadowBias)
        return 1.0;

    float reach = saturate(1.0 - depth.y / radius);
    float shadowLength = lerp(SpriteShadowMinLength, SpriteShadowMaxLength, reach);
    float fade = saturate(1.0 - (distanceToLight - depth.y) / max(shadowLength, 0.0001));
    return 1.0 - saturate(SpriteShadowStrength) * fade;
}

float point_visibility(uint lightIndex, Light light, float2 delta, float distanceToLight)
{
    float angle = atan2(delta.y, delta.x);
    if (angle < 0.0)
        angle += 6.28318530718;
    float samplePosition = angle / 6.28318530718 * POINT_SHADOW_SAMPLES - 0.5;
    int center = (int)round(samplePosition);
    if (SoftShadowSamples <= 0.0 || light.Shadow.x <= 0.0 || light.Shadow.y <= 0.0)
    {
        int sampleIndex = center % POINT_SHADOW_SAMPLES;
        if (sampleIndex < 0)
            sampleIndex += POINT_SHADOW_SAMPLES;
        return point_sample_visibility(lightIndex, sampleIndex, distanceToLight, light.Radius);
    }
    float radialSoftness = saturate(distanceToLight / light.Radius);
    int stepSize = max(1, (int)round(light.Shadow.x * light.Shadow.y * radialSoftness));
    if (light.Shadow.z > 0.0)
    {
        float frame = floor(Time * max(0.0, light.Shadow.w));
        float noise = interleaved_gradient_noise(floor(delta * Zoom), lightIndex + frame);
        center = (int)round(samplePosition + (noise - 0.5) * light.Shadow.z * stepSize);
    }
    float visibility = 0.0;
    static const int offsets[5] = { -2, -1, 0, 1, 2 };
    static const float weights[5] = { 0.1, 0.2, 0.4, 0.2, 0.1 };
    for (uint i = 0; i < 5; i++)
    {
        int sampleIndex = (center + offsets[i] * stepSize) % POINT_SHADOW_SAMPLES;
        if (sampleIndex < 0)
            sampleIndex += POINT_SHADOW_SAMPLES;
        visibility += weights[i] * point_sample_visibility(lightIndex, sampleIndex, distanceToLight, light.Radius);
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
        float depth = ShadowDepth[SUN_SHADOW_ROW * POINT_SHADOW_SAMPLES + center].x;
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
        float depth = ShadowDepth[SUN_SHADOW_ROW * POINT_SHADOW_SAMPLES + sampleIndex].x;
        visibility += weights[i] * (along <= depth + ShadowBias ? 1.0 : 0.0);
    }
    return visibility;
}

float spore_density(float2 world)
{
    if (SporeFogStrength <= 0.0)
        return 0.0;

    float2 drift = SporeFogDrift * Time;
    float coarse = SporeNoise.SampleLevel(SporeNoiseSampler, (world + drift) * SporeFogScale, 0.0).r;
    float2 rotatedWorld = float2(-world.y, world.x);
    float fine = SporeNoise.SampleLevel(
        SporeNoiseSampler,
        (rotatedWorld - drift * 0.4) * SporeFogScale * 3.17 + float2(0.37, 0.61),
        0.0).g;
    float density = coarse * lerp(0.75, 1.25, fine);
    return saturate((density - 0.5) * SporeFogContrast + 0.5);
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
    float2 normalXY = normalData.rg * 2.0 - 1.0;
    float3 normal = normalize(float3(normalXY, sqrt(saturate(1.0 - dot(normalXY, normalXY)))));
    float4 shadowed = Ambient;
    float4 unshadowed = Ambient;
    float3 sporeFog = 0.0;

    if (SunEnabled != 0)
    {
        float response = normal_response(normal, normalStrength, float3(-SunDirection, 1.0));
        float4 contribution = float4(SunColor.rgb * SunStrength * response, 1.0);
        float visibility = sun_visibility(world);
        unshadowed += contribution;
        shadowed += contribution * visibility;
        sporeFog += SunColor.rgb * SunStrength * SporeFogSunStrength * visibility;
    }

    for (uint i = 0; i < LightCount; i++)
    {
        Light light = Lights[i];
        float2 toLight = light.Position - world;
        float distanceToLight = length(toLight);
        float attenuation = saturate(1.0 - distanceToLight / light.Radius);
        attenuation *= light_cookie(light, -toLight);
        if (attenuation <= 0.0)
            continue;

        float fogAttenuation = attenuation;
        attenuation *= attenuation;
        float response = normal_response(normal, normalStrength, float3(toLight, light.Height));
        float4 contribution = float4(light.ColorIntensity.rgb * light.ColorIntensity.a * attenuation * response, attenuation);
        float visibility = point_visibility(i, light, -toLight, distanceToLight);
        unshadowed += contribution;
        shadowed += contribution * visibility;
        sporeFog += light.ColorIntensity.rgb * light.ColorIntensity.a * fogAttenuation * SporeFogPointStrength * visibility;
    }

    sporeFog *= spore_density(world) * SporeFogStrength * SporeFogTint.rgb;
    sporeFog = 1.0 - exp(-sporeFog);
    BackgroundMask[id.xy] = saturate(shadowed);
    ForegroundMask[id.xy] = saturate(unshadowed);
    SporeFogMask[id.xy] = float4(saturate(sporeFog), saturate(max(max(sporeFog.r, sporeFog.g), sporeFog.b)));
}
