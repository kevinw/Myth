#include "Common.hlsl"

// Particle trail ribbons. Each particle owns a TRAIL_SAMPLES ring of recorded
// positions (see ParticleUpdate.hlsl); one instanced quad is drawn per segment
// between adjacent samples, extruded along mitered edge perpendiculars. Sample
// 0 is live at the particle position, so the head segment tracks it exactly.

#define TRAIL_SAMPLES 16
#define TRAIL_SEGS (TRAIL_SAMPLES - 1)

struct Particle
{
    float2 pos;
    float2 vel;
    float age;
    float lifetime;
    float seed;
    float effect;
    float flags;
    float trailHead;
    float trailTimer;
    float pad;
};

struct EffectParams
{
    float gravity;
    float drag;
    float noiseAmount;
    float noiseScale;
    float sizeStart;
    float sizeEnd;
    float collisionMode;
    float bounciness;
    float style;
    float trailSeconds;
    float trailWidthStart;
    float trailWidthEnd;
    float lightResponse;
    float _effectPad0;
    float _effectPad1;
    float _effectPad2;
};

Texture2D ColorLut : register(t0, space0);
SamplerState ColorLutSampler : register(s0, space0);
StructuredBuffer<Particle> Particles : register(t1, space0);
StructuredBuffer<EffectParams> Effects : register(t2, space0);
StructuredBuffer<float2> Trail : register(t3, space0);

cbuffer VertexParams : register(b0, space1)
{
    float4x4 Matrix;
    float LutHeight;   // particle gradient rows + trail gradient rows
    float MaxEffects;  // trail gradient row = MaxEffects + effect
    float _pad0;
    float _pad1;
};

Texture2D LightTexture : register(t0, space2);
SamplerState LightSampler : register(s0, space2);

cbuffer FragmentParams : register(b0, space3)
{
    float2 TargetSize;
    float LightingEnabled;
    float _fragmentPad0;
    float3 AmbientLight;
    float _fragmentPad1;
};

struct VsInput
{
    float2 position : TEXCOORD0;  // unit-quad corner offset in [-0.5, 0.5]
    float2 tex_coord : TEXCOORD1;
    float4 color : TEXCOORD2;
};

struct VsOutput
{
    float4 color : TEXCOORD0;
    float2 texCoord : TEXCOORD1; // x = trail t, y = across the ribbon [0, 1]
    float lightResponse : TEXCOORD2;
    float4 position : SV_Position;
};

VsOutput vertex_main(VsInput input, uint instance : SV_InstanceID)
{
    uint particleIndex = instance / TRAIL_SEGS;
    uint seg = instance % TRAIL_SEGS;
    Particle p = Particles[particleIndex];
    EffectParams fx = Effects[(uint)(p.effect + 0.5)];

    uint trailBase = particleIndex * TRAIL_SAMPLES;
    uint head = (uint)(p.trailHead + 0.5);

    // This corner sits at edge j: j samples behind the head, mitered between
    // the neighboring samples. t is the edge's age as a fraction of the trail.
    uint j = seg + (uint)(input.position.x + 0.5 + 0.5);
    uint jNewer = j == 0u ? 0u : j - 1u;
    uint jOlder = min(j + 1u, TRAIL_SAMPLES - 1u);
    float2 posAt = Trail[trailBase + (head + TRAIL_SAMPLES - j) % TRAIL_SAMPLES];
    float2 posNewer = Trail[trailBase + (head + TRAIL_SAMPLES - jNewer) % TRAIL_SAMPLES];
    float2 posOlder = Trail[trailBase + (head + TRAIL_SAMPLES - jOlder) % TRAIL_SAMPLES];

    float cadence = fx.trailSeconds / float(TRAIL_SEGS);
    float t = j == 0u ? 0.0 : saturate((float(j - 1u) * cadence + p.trailTimer) / max(fx.trailSeconds, 1e-5));

    float alive = (p.lifetime > 0.0 && fx.trailSeconds > 0.0) ? 1.0 : 0.0;
    float width = lerp(fx.trailWidthStart, fx.trailWidthEnd, t) * alive;

    float2 along = posNewer - posOlder;
    float len = length(along);
    float2 perp = len > 1e-4 ? float2(-along.y, along.x) / len : float2(0.0, 0.0);

    float side = input.position.y * 2.0; // -1 or 1
    float2 world = posAt + perp * side * width * 0.5;

    VsOutput output;
    output.position = mul(Matrix, float4(world, 0.0, 1.0));

    float row = (MaxEffects + p.effect + 0.5) / LutHeight;
    float4 col = ColorLut.SampleLevel(ColorLutSampler, float2(t, row), 0.0);
    output.color = float4(col.rgb, col.a * alive);
    output.texCoord = float2(t, side * 0.5 + 0.5);
    output.lightResponse = fx.lightResponse;
    return output;
}

float4 fragment_main(VsOutput input) : SV_Target0
{
    // Soft lateral falloff so overlapping additive segments blend smoothly.
    float across = abs(input.texCoord.y * 2.0 - 1.0);
    float4 color = input.color * (1.0 - smoothstep(0.3, 1.0, across));
    float response = saturate(input.lightResponse * LightingEnabled);
    if (response > 0.0)
    {
        float3 light = LightTexture.Sample(LightSampler, input.position.xy / TargetSize).rgb;
        float3 directLight = max(light - AmbientLight, 0.0);
        float illumination = max(directLight.r, max(directLight.g, directLight.b));
        color.rgb = lerp(color.rgb, color.rgb * directLight * 2.0, response);
        color.a *= lerp(1.0, smoothstep(0.02, 0.45, illumination), response);
    }
    return color;
}
