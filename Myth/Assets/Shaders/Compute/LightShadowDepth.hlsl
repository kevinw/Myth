#define POINT_SHADOW_SAMPLES 512
#define MAX_LIGHTS 64

struct Segment
{
    float4 Coordinates;
    float4 Properties;
};

struct Light
{
    float2 Position;
    float Radius;
    float Height;
    float4 ColorIntensity;
};

Texture2D SpriteShadowMask : register(t0, space0);
SamplerState SpriteShadowSampler : register(s0, space0);
StructuredBuffer<Segment> Segments : register(t1, space0);
StructuredBuffer<Light> Lights : register(t2, space0);
RWStructuredBuffer<float> ShadowDepth : register(u0, space1);

cbuffer Params : register(b0, space2)
{
    uint SegmentCount;
    uint LightCount;
    uint SunEnabled;
    uint ShadowSamples;
    float2 SunDirection;
    float2 SunPerpendicular;
    float SunPerpendicularMin;
    float SunPerpendicularMax;
    float SunAlongMin;
    float SunAlongMax;
    float2 Camera;
    float2 TargetSize;
    float4 ZoomPadding;
};

float cross2(float2 a, float2 b)
{
    return a.x * b.y - a.y * b.x;
}

bool ray_segment(float2 origin, float2 direction, Segment segment, out float distance)
{
    float2 a = segment.Coordinates.xy;
    float2 b = segment.Coordinates.zw;
    float2 edge = b - a;
    float denominator = cross2(direction, edge);
    if (abs(denominator) <= 0.0001)
    {
        distance = 0.0;
        return false;
    }

    float2 delta = a - origin;
    float rayDistance = cross2(delta, edge) / denominator;
    float segmentAmount = cross2(delta, direction) / denominator;
    distance = rayDistance;
    return rayDistance >= 0.0 && segmentAmount >= 0.0 && segmentAmount <= 1.0;
}

bool sprite_occludes(float2 world)
{
    float2 pixel = (world - Camera) * ZoomPadding.x;
    if (any(pixel < 0.0) || any(pixel >= TargetSize))
        return false;
    float2 uv = (pixel + 0.5) / TargetSize;
    return SpriteShadowMask.SampleLevel(SpriteShadowSampler, uv, 0.0).r > 0.1;
}

[numthreads(64, 1, 1)]
void compute_main(uint3 id : SV_DispatchThreadID)
{
    if (id.x >= ShadowSamples || id.y > MAX_LIGHTS)
        return;

    uint outputIndex = id.y * ShadowSamples + id.x;
    if (id.y < MAX_LIGHTS)
    {
        if (id.y >= LightCount)
        {
            ShadowDepth[outputIndex] = 0.0;
            return;
        }

        Light light = Lights[id.y];
        float angle = (id.x + 0.5) / ShadowSamples * 6.28318530718;
        float2 direction = float2(cos(angle), sin(angle));
        float closest = light.Radius;
        for (uint i = 0; i < SegmentCount; i++)
        {
            float distance;
            if (ray_segment(light.Position, direction, Segments[i], distance))
                closest = min(closest, distance);
        }
        float stepSize = max(0.5, 0.5 / ZoomPadding.x);
        for (float distance = stepSize; distance < closest; distance += stepSize)
            if (sprite_occludes(light.Position + direction * distance))
            {
                closest = distance;
                break;
            }
        ShadowDepth[outputIndex] = closest;
        return;
    }

    if (SunEnabled == 0)
    {
        ShadowDepth[outputIndex] = SunAlongMax;
        return;
    }

    float across = lerp(SunPerpendicularMin, SunPerpendicularMax, (id.x + 0.5) / ShadowSamples);
    float2 origin = SunPerpendicular * across + SunDirection * SunAlongMin;
    float closest = SunAlongMax - SunAlongMin;
    for (uint i = 0; i < SegmentCount; i++)
    {
        if (Segments[i].Properties.x <= 0.0)
            continue;
        float distance;
        if (ray_segment(origin, SunDirection, Segments[i], distance))
            closest = min(closest, distance);
    }
    ShadowDepth[outputIndex] = SunAlongMin + closest;
}
