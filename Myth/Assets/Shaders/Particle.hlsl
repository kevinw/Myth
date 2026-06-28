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
    float pad0;
    float pad1;
    float pad2;
};

// In SDL_gpu's vertex resource model, sampled textures precede storage buffers
// in space0: ColorLut = t0, then the storage buffers at t1/t2.
Texture2D ColorLut : register(t0, space0);
SamplerState ColorLutSampler : register(s0, space0);
StructuredBuffer<Particle> Particles : register(t1, space0);
StructuredBuffer<EffectParams> Effects : register(t2, space0);

cbuffer VertexParams : register(b0, space1)
{
    float2 TargetSize;  // render target size in pixels
    float2 Camera;      // world-space camera top-left (RenderCamera)
    float Zoom;
    float MaxEffects;   // LUT height (effect rows)
    float _pad0;
    float _pad1;
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
    float4 position : SV_Position;
};

VsOutput vertex_main(VsInput input, uint instance : SV_InstanceID)
{
    VsOutput output;
    Particle p = Particles[instance];
    EffectParams fx = Effects[(uint)(p.effect + 0.5)];

    float alive = p.lifetime > 0.0 ? 1.0 : 0.0;
    float life_frac = p.lifetime > 0.0 ? saturate(p.age / p.lifetime) : 1.0;

    float size = lerp(fx.sizeStart, fx.sizeEnd, life_frac) * alive; // dead -> 0
    float2 world = p.pos + input.position * size;
    float2 screen = (world - Camera) * Zoom;
    float2 clip = screen / TargetSize * 2.0 - 1.0;
    clip.y = -clip.y;
    output.position = float4(clip, 0.0, 1.0);

    // Color from the per-effect LUT row. Vertex stage must use SampleLevel.
    float row = (p.effect + 0.5) / MaxEffects;
    float4 col = ColorLut.SampleLevel(ColorLutSampler, float2(life_frac, row), 0.0);
    output.color = float4(col.rgb, col.a * alive);
	output.texCoord = input.tex_coord;
	output.style = fx.style;
    return output;
}

float4 fragment_main(VsOutput input) : SV_Target0
{
	if (input.style > 0.5)
	{
		float radialDistance = length(input.texCoord - 0.5) * 2.0;
		float aura = 1.0 - smoothstep(0.35, 1.0, radialDistance);
		float core = 1.0 - smoothstep(0.0, 0.38, radialDistance);
		float3 color = lerp(input.color.rgb, 1.0, core * 0.85);
		return float4(color, input.color.a * max(aura * 0.45, core));
	}
    return input.color;
}
