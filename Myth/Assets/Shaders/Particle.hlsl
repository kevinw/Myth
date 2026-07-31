#include "Common.hlsl"

// GPU-driven particle rendering (world space, multi-effect). The simulation and
// per-effect params buffers are bound to the vertex stage; one instanced quad is
// drawn per particle, indexed by SV_InstanceID, transformed world -> screen.

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

// In SDL_gpu's vertex resource model, sampled textures precede storage buffers
// in space0: ColorLut = t0, then the storage buffers at t1/t2.
Texture2D ColorLut : register(t0, space0);
SamplerState ColorLutSampler : register(s0, space0);
StructuredBuffer<Particle> Particles : register(t1, space0);
StructuredBuffer<EffectParams> Effects : register(t2, space0);

cbuffer VertexParams : register(b0, space1)
{
    float4x4 Matrix;
    float LutHeight;   // particle gradient rows + trail gradient rows
    float _pad0;
    float _pad1;
    float _pad2;
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
	float2 texCoord : TEXCOORD1;
	float style : TEXCOORD2;
    float lightResponse : TEXCOORD3;
    float4 position : SV_Position;
};

VsOutput vertex_main(VsInput input, uint instance : SV_InstanceID)
{
    VsOutput output;
    Particle p = Particles[instance];
    EffectParams fx = Effects[(uint)(p.effect + 0.5)];

    // Hidden once past lifetime (the slot may linger while its trail drains).
    float alive = (p.lifetime > 0.0 && p.age < p.lifetime) ? 1.0 : 0.0;
    float life_frac = p.lifetime > 0.0 ? saturate(p.age / p.lifetime) : 1.0;

    float size = lerp(fx.sizeStart, fx.sizeEnd, life_frac) * alive; // dead -> 0
    float2 world = p.pos + input.position * size;
    output.position = mul(Matrix, float4(world, 0.0, 1.0));

    // Color from the per-effect LUT row. Vertex stage must use SampleLevel.
    float row = (p.effect + 0.5) / LutHeight;
    float4 col = ColorLut.SampleLevel(ColorLutSampler, float2(life_frac, row), 0.0);
    output.color = float4(col.rgb, col.a * alive);
	output.texCoord = input.tex_coord;
	output.style = fx.style;
    output.lightResponse = fx.lightResponse;
    return output;
}

float4 fragment_main(VsOutput input) : SV_Target0
{
	float4 color = input.color;
	if (input.style > 0.5)
	{
		float radialDistance = length(input.texCoord - 0.5) * 2.0;
		float aura = 1.0 - smoothstep(0.35, 1.0, radialDistance);
		float core = 1.0 - smoothstep(0.0, 0.38, radialDistance);
		color = float4(lerp(input.color.rgb, 1.0, core * 0.85), input.color.a * max(aura * 0.45, core));
	}

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
