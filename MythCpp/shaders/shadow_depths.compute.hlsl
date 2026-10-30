// Myth's point-light shadow depths (Assets/Shaders/Compute/LightShadowDepth.hlsl, its lights' half):
// a thread per ray, SHADOW_SAMPLES rays round each light, each row of the output how far its rays
// get before an occluder. The lighting pass softens and compares against it.

#define MAX_LIGHTS 16

StructuredBuffer<float4> Segments : register(t0, space0);   // xy to zw, room pixels
RWTexture2D<float> Depths : register(u0, space1);           // SHADOW_SAMPLES x MAX_LIGHTS

cbuffer Params : register(b0, space2)
{
    uint SegmentCount;
    uint LightCount;
    uint ShadowSamples;
    uint Padding;
    float4 Lights[MAX_LIGHTS];  // xy position
};

float cross2(float2 a, float2 b) { return a.x * b.y - a.y * b.x; }

[numthreads(64, 1, 1)]
void compute_main(uint3 id : SV_DispatchThreadID)
{
    if (id.x >= ShadowSamples || id.y >= LightCount)
        return;
    float2 origin = Lights[id.y].xy;
    float angle = (id.x + 0.5) / ShadowSamples * 6.28318530718;
    float2 direction = float2(cos(angle), sin(angle));
    float depth = 3.4e38;
    for (uint i = 0; i < SegmentCount; i++)
    {
        float2 a = Segments[i].xy, edge = Segments[i].zw - a;
        float denominator = cross2(direction, edge);
        if (abs(denominator) <= 0.0001)
            continue;
        float2 delta = a - origin;
        float distance = cross2(delta, edge) / denominator;
        float along = cross2(delta, direction) / denominator;
        if (distance >= 0.0 && along >= 0.0 && along <= 1.0)
            depth = min(depth, distance);
    }
    Depths[id.xy] = depth;
}
