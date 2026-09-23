#include "Common.hlsl"

Texture2D BackgroundScene : register(t0, space0);
SamplerState BackgroundSceneSampler : register(s0, space0);
Texture2D ForegroundScene : register(t1, space0);
SamplerState ForegroundSceneSampler : register(s1, space0);
Texture2D BackgroundLight : register(t2, space0);
SamplerState BackgroundLightSampler : register(s2, space0);
Texture2D ForegroundLight : register(t3, space0);
SamplerState ForegroundLightSampler : register(s3, space0);
Texture2D BackgroundLightEmission : register(t4, space0);
SamplerState BackgroundLightEmissionSampler : register(s4, space0);
Texture2D ForegroundLightEmission : register(t5, space0);
SamplerState ForegroundLightEmissionSampler : register(s5, space0);
Texture2D TileField : register(t6, space0);
SamplerState TileFieldSampler : register(s6, space0);
Texture2D BackgroundCoverage : register(t7, space0);
SamplerState BackgroundCoverageSampler : register(s7, space0);
RWStructuredBuffer<float4> Emission : register(u0, space1);
RWStructuredBuffer<float4> Surface : register(u1, space1);
RWStructuredBuffer<float4> FieldA : register(u2, space1);
RWStructuredBuffer<float4> FieldB : register(u3, space1);

cbuffer Params : register(b0, space2)
{
    uint Width;
    uint Height;
    uint GridWidth;
    uint GridHeight;
    float GridSpacing;
    float BounceGain;
    float BounceSaturation;
    float ForegroundReflectivity;
    float AlbedoFloor;
    float Warmth;
    float ColorBleed;
    float Padding;
    float4 Ambient;
    float4 MaterialResponse;
    float4 RoomBounds;
    float4 NextRoomBounds;
};

bool in_room(float2 pixel, float4 bounds)
{
    return all(pixel >= bounds.xy) && all(pixel < bounds.xy + bounds.zw);
}

float material_response(float4 tile)
{
    float family = floor(floor(tile.g * 255.0 + 0.5) / 32.0);
    if (family == 1.0) return MaterialResponse.x;
    if (family == 2.0) return MaterialResponse.y;
    if (family == 3.0) return MaterialResponse.z;
    return MaterialResponse.w;
}

float3 bounce_color(float3 albedo, float response)
{
    float luminance = dot(albedo, float3(0.2126, 0.7152, 0.0722));
    float3 color = max(max(luminance, AlbedoFloor).xxx + (albedo - luminance) * ColorBleed, 0.0);
    return color * response;
}

float3 saturate_color(float3 color, float amount)
{
    float luminance = dot(color, float3(0.2126, 0.7152, 0.0722));
    return lerp(luminance.xxx, color, amount);
}

[numthreads(64, 1, 1)]
void compute_main(uint3 id : SV_DispatchThreadID)
{
    uint cellCount = GridWidth * GridHeight;
    if (id.x >= cellCount)
        return;


    uint2 cell = uint2(id.x % GridWidth, id.x / GridWidth);
    float3 radiance = 0.0;
    float3 surfaceColor = 0.0;
    float coverage = 0.0;
    float solid = 0.0;
    for (uint sampleY = 0; sampleY < 2; sampleY++)
    {
        for (uint sampleX = 0; sampleX < 2; sampleX++)
        {
            float2 sampleOffset = (float2(sampleX, sampleY) + 0.5) * 0.5;
            float2 pixel = min((float2(cell) + sampleOffset) * GridSpacing, float2(Width, Height) - 0.5);
            if (!in_room(pixel, RoomBounds) && !in_room(pixel, NextRoomBounds))
                continue;
            // All room cells may carry GI; only actual surfaces/emission seed it.
            coverage = 1.0;
            float2 uv = target_uv(pixel / float2(Width, Height));
            float backgroundCoverage = BackgroundCoverage.SampleLevel(BackgroundCoverageSampler, uv, 0.0).r;
            float4 background = BackgroundScene.SampleLevel(BackgroundSceneSampler, uv, 0.0);
            float4 foreground = ForegroundScene.SampleLevel(ForegroundSceneSampler, uv, 0.0);
            float4 tile = TileField.SampleLevel(TileFieldSampler, uv, 0.0);
            float response = material_response(tile);
            float3 backgroundColor = bounce_color(background.rgb, response);
            float3 foregroundColor = bounce_color(foreground.rgb * ForegroundReflectivity, response);
            float3 backgroundLighting = max(BackgroundLight.SampleLevel(BackgroundLightSampler, uv, 0.0).rgb - Ambient.rgb, 0.0);
            float3 foregroundLighting = max(ForegroundLight.SampleLevel(ForegroundLightSampler, uv, 0.0).rgb - Ambient.rgb, 0.0);
            float4 backgroundLightEmission = BackgroundLightEmission.SampleLevel(BackgroundLightEmissionSampler, uv, 0.0);
            float4 foregroundLightEmission = ForegroundLightEmission.SampleLevel(ForegroundLightEmissionSampler, uv, 0.0);
            float3 sampleRadiance = lerp(backgroundColor * backgroundLighting * backgroundCoverage, backgroundLightEmission.rgb, backgroundLightEmission.a);
            float solidForeground = foreground.a * step(0.5, tile.b);
            sampleRadiance = lerp(sampleRadiance, foregroundColor * foregroundLighting, solidForeground);
            sampleRadiance = lerp(sampleRadiance, foregroundLightEmission.rgb, foregroundLightEmission.a);
            radiance += sampleRadiance;
            surfaceColor += lerp(backgroundColor, foregroundColor, solidForeground);
            solid += tile.b;
        }
    }
    radiance *= 0.25;
    surfaceColor *= 0.25;
    solid *= 0.25;
    radiance = saturate_color(radiance, BounceSaturation);
    radiance *= lerp(1.0.xxx, float3(1.12, 0.97, 0.82), Warmth) * BounceGain;

    Emission[id.x] = float4(radiance, coverage);
    Surface[id.x] = float4(surfaceColor, step(0.5, solid));
    FieldA[id.x] = 0.0;
    FieldB[id.x] = 0.0;
}
