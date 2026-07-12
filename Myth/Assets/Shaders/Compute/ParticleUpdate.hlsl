// GPU particle simulation step (ping-pong, multi-effect, world space) with
// world collision sampled from a solid mask texture (the foreground coverage).
//
// SDL_gpu cycles read-write storage buffers each dispatch, so state cannot
// persist in place: read a stable Src buffer and write EVERY slot of Dst.
//
// A sampled texture occupies t0 in space0, so the storage buffers follow at
// t1/t2/t3.

#include "Common.hlsl"

#define TRAIL_SAMPLES 16

struct Particle
{
    float2 pos;
    float2 vel;
    float age;
    float lifetime;
    float seed;
    float effect;     // effect index (stored as float)
    float flags;      // 0 = active, 1 = settled (frozen on a surface)
    float trailHead;  // ring index of the newest trail sample
    float trailTimer; // seconds since the head sample was frozen
    float pad;        // pads to 48 bytes (8-byte aligned) to match the C# struct
};

struct EffectParams
{
    float gravity;
    float drag;
    float noiseAmount;
    float noiseScale;
    float sizeStart;
    float sizeEnd;
    float collisionMode; // 0 = none, 1 = kill, 2 = settle, 3 = bounce
    float bounciness;
    float style;
	float trailSeconds; // 0 = no trail
	float trailWidthStart;
    float trailWidthEnd;
};

struct EmitParams
{
    uint start;
    uint count;
    uint seed;
    uint effect;
    float originX;
    float originY;
    uint shape;       // 0 = point, 1 = circle, 2 = cone, 3 = line, 4 = point list
    float radius;
    float dirAngle;   // radians
    float spread;     // radians
    float speedMin;
    float speedMax;
    float lifeMin;
    float lifeMax;
	float baseVelocityX;
	float baseVelocityY;
    uint pointStart;  // first entry in EmitPoints (shape 4)
    uint pad0;
    uint pad1;
    uint pad2;
};

// A per-effect force field bound to a moving sprite's silhouette SDF: pulls
// particles onto the border and drives them along it. Refreshed every frame
// via ParticleSystem.SetEffectField by the owning actor; enable = 0 otherwise.
struct EdgeFieldParams
{
    float originX;    // world-space flip pivot (the actor position)
    float originY;
    float rectLocalX; // padded SDF rect top-left, unflipped sprite-local px
    float rectLocalY;
    float rectW;
    float rectH;
    float srcX;       // padded SDF rect top-left in the SDF atlas, px
    float srcY;
    float flip;       // +1 / -1
    float attraction; // per-second pull onto the border (>= ~1/dt snaps)
    float flow;       // tangential px/s along the border, sign = direction
    float enable;
};

// World collision is sampled from the foreground coverage mask (its alpha),
// edge fields from the sprite silhouette SDF atlas. Sampled textures occupy
// t0/t1 in space0, so the storage buffers follow at t2..t7.
Texture2D SolidMask : register(t0, space0);
SamplerState SolidSampler : register(s0, space0);
Texture2D SdfAtlas : register(t1, space0);
SamplerState SdfSampler : register(s1, space0);
StructuredBuffer<Particle> Src : register(t2, space0);
StructuredBuffer<EffectParams> Effects : register(t3, space0);
StructuredBuffer<EmitParams> Emits : register(t4, space0);
StructuredBuffer<float2> TrailSrc : register(t5, space0);
StructuredBuffer<float4> EmitPoints : register(t6, space0);
StructuredBuffer<EdgeFieldParams> Fields : register(t7, space0);
RWStructuredBuffer<Particle> Dst : register(u0, space1);
RWStructuredBuffer<float2> TrailDst : register(u1, space1);

cbuffer Params : register(b0, space2)
{
    uint Count;
    float Dt;
    float Time;
    uint EmitCount;

    float CameraX; // world-space camera top-left (maps world -> mask uv)
    float CameraY;
    float Zoom;
    float TargetW;

    float TargetH;
    float SdfAtlasW;
    float SdfAtlasH;
    float Pad2;
};

float hash1(uint n)
{
    n = (n ^ 61u) ^ (n >> 16);
    n *= 9u;
    n = n ^ (n >> 4);
    n *= 0x27d4eb2du;
    n = n ^ (n >> 15);
    return float(n & 0x00FFFFFFu) / float(0x01000000u);
}

float hash21(float2 p)
{
    p = frac(p * float2(123.34, 345.45));
    p += dot(p, p + 34.345);
    return frac(p.x * p.y);
}

float vnoise(float2 p)
{
    float2 i = floor(p);
    float2 f = frac(p);
    float2 u = f * f * (3.0 - 2.0 * f);
    float a = hash21(i);
    float b = hash21(i + float2(1.0, 0.0));
    float c = hash21(i + float2(0.0, 1.0));
    float d = hash21(i + float2(1.0, 1.0));
    return lerp(lerp(a, b, u.x), lerp(c, d, u.x), u.y);
}

float2 curl(float2 p)
{
    const float e = 0.1;
    float n_up = vnoise(p + float2(0.0, e));
    float n_down = vnoise(p - float2(0.0, e));
    float n_right = vnoise(p + float2(e, 0.0));
    float n_left = vnoise(p - float2(e, 0.0));
    return float2((n_up - n_down) / (2.0 * e), -(n_right - n_left) / (2.0 * e));
}

// Solidity (0..1) sampled from the foreground mask at a world position.
// Off-screen reads as empty so particles don't collide outside the view.
float solid_at(float2 worldPos)
{
    float2 uv = target_uv((worldPos - float2(CameraX, CameraY)) * Zoom / float2(TargetW, TargetH));
    if (uv.x < 0.0 || uv.x > 1.0 || uv.y < 0.0 || uv.y > 1.0)
        return 0.0;
    return SolidMask.SampleLevel(SolidSampler, uv, 0.0).r;
}

float2 solid_normal(float2 worldPos)
{
    const float e = 2.0;
    float l = solid_at(worldPos - float2(e, 0.0));
    float r = solid_at(worldPos + float2(e, 0.0));
    float u = solid_at(worldPos - float2(0.0, e));
    float d = solid_at(worldPos + float2(0.0, e));
    float2 grad = float2(r - l, d - u); // toward more solid
    if (dot(grad, grad) < 1e-6)
        return float2(0.0, -1.0); // default: up
    return -normalize(grad);
}

[numthreads(64, 1, 1)]
void compute_main(uint3 id : SV_DispatchThreadID)
{
    uint i = id.x;
    if (i >= Count)
        return;

    uint trailBase = i * TRAIL_SAMPLES;

    // Emission: find the burst (if any) whose ring range contains this slot.
    for (uint e = 0u; e < EmitCount; e++)
    {
        EmitParams em = Emits[e];
        uint rel = (i + Count - em.start) % Count;
        if (rel >= em.count)
            continue;

        float2 origin = float2(em.originX, em.originY);
        if (em.shape == 1u)
        {
            float r = sqrt(hash1(i * 23u + em.seed + 4u)) * em.radius;
            float da = hash1(i * 29u + em.seed + 5u) * 6.2831853;
            origin += float2(cos(da), sin(da)) * r;
        }
		else if (em.shape == 3u)
		{
			float2 along = float2(cos(em.dirAngle), sin(em.dirAngle));
			float2 across = float2(-along.y, along.x);
			origin += along * (hash1(i * 23u + em.seed + 4u) * em.radius);
			origin += across * ((hash1(i * 29u + em.seed + 5u) - 0.5) * em.spread);
		}

        float pointAngle = 0.0;
        if (em.shape == 4u)
        {
            float4 pt = EmitPoints[em.pointStart + rel];
            origin = pt.xy;
            pointAngle = pt.z;
        }

        float angle;
        if (em.shape == 2u)
            angle = em.dirAngle + (hash1(i * 7u + em.seed) - 0.5) * em.spread;
        else if (em.shape == 4u)
            angle = pointAngle + (hash1(i * 7u + em.seed) - 0.5) * em.spread;
        else
            angle = hash1(i * 7u + em.seed) * 6.2831853;

        float speed = lerp(em.speedMin, em.speedMax, hash1(i * 13u + em.seed + 1u));

        Particle np;
        np.pos = origin;
		np.vel = float2(cos(angle), sin(angle)) * speed + float2(em.baseVelocityX, em.baseVelocityY);
        np.age = 0.0;
        np.lifetime = lerp(em.lifeMin, em.lifeMax, hash1(i * 17u + em.seed + 2u));
        np.seed = hash1(i * 19u + em.seed + 3u) * 1000.0;
        np.effect = (float)em.effect;
        np.flags = 0.0;
        np.trailHead = 0.0;
        np.trailTimer = 0.0;
        Dst[i] = np;
        for (uint t = 0u; t < TRAIL_SAMPLES; t++)
            TrailDst[trailBase + t] = np.pos; // recycled slot: no stale streak
        return;
    }

    Particle p = Src[i];
    if (p.lifetime <= 0.0)
    {
        Dst[i] = p; // keep dead slot defined
        for (uint t = 0u; t < TRAIL_SAMPLES; t++)
            TrailDst[trailBase + t] = TrailSrc[trailBase + t];
        return;
    }

    EffectParams fx = Effects[(uint)(p.effect + 0.5)];

    p.age += Dt;
    if (p.age >= p.lifetime + fx.trailSeconds)
    {
        p.lifetime = 0.0; // trail fully drained; free the slot
        Dst[i] = p;
        for (uint t = 0u; t < TRAIL_SAMPLES; t++)
            TrailDst[trailBase + t] = TrailSrc[trailBase + t];
        return;
    }

    // Settled particles and dead-but-draining ones are frozen in place; their
    // trail keeps recording the frozen position so it retracts naturally.
    if (p.flags < 0.5 && p.age < p.lifetime)
    {
        uint mode = (uint)(fx.collisionMode + 0.5);

        float2 force = curl(p.pos * fx.noiseScale + Time * 0.5 + p.seed) * fx.noiseAmount;
        p.vel += force * Dt;
        p.vel.y += fx.gravity * Dt;
        p.vel *= saturate(1.0 - fx.drag * Dt);

        // Edge field: spring toward the owning sprite's silhouette (the ~1px
        // SDF isoline) and steer tangential speed along it. Positions map
        // world -> unflipped sprite-local -> padded SDF rect in the atlas.
        EdgeFieldParams field = Fields[(uint)(p.effect + 0.5)];
        if (field.enable > 0.5)
        {
            float2 local = p.pos - float2(field.originX, field.originY);
            local.x *= field.flip;
            float2 px = local - float2(field.rectLocalX, field.rectLocalY);
            if (px.x >= 1.0 && px.y >= 1.0 && px.x <= field.rectW - 2.0 && px.y <= field.rectH - 2.0)
            {
                float2 uvBase = float2(field.srcX, field.srcY);
                float2 atlasSize = float2(SdfAtlasW, SdfAtlasH);
                float d  = SdfAtlas.SampleLevel(SdfSampler, (uvBase + px) / atlasSize, 0.0).r * 255.0;
                float dr = SdfAtlas.SampleLevel(SdfSampler, (uvBase + px + float2(1.0, 0.0)) / atlasSize, 0.0).r * 255.0;
                float dd = SdfAtlas.SampleLevel(SdfSampler, (uvBase + px + float2(0.0, 1.0)) / atlasSize, 0.0).r * 255.0;
                float2 grad = float2(dr - d, dd - d);
                if (dot(grad, grad) > 1e-6)
                {
                    float2 g = normalize(grad); // outward, unflipped local
                    g.x *= field.flip;          // back to world
                    // kill radial velocity, then pull the position onto a
                    // ~1.5px standoff isoline. Projection (not a spring) so
                    // fast flow around tight curves can't inflate the orbit;
                    // attraction is the per-second correction rate (>= ~1/dt
                    // snaps). Clamped so a stray particle glides back instead
                    // of teleporting, and the standoff keeps flow chords from
                    // cutting into the interior where the gradient dies.
                    p.vel -= g * (dot(p.vel, g) * saturate(8.0 * Dt));
                    p.pos -= g * (clamp(d - 1.5, -3.0, 3.0) * saturate(field.attraction * Dt));
                    if (field.flow != 0.0)
                    {
                        // perp of the world gradient, handedness kept sprite-local
                        float2 tang = float2(-g.y, g.x) * field.flip;
                        p.vel += tang * ((field.flow - dot(p.vel, tang)) * saturate(10.0 * Dt));
                    }
                }
            }
        }

        float2 nextPos = p.pos + p.vel * Dt;

        if (mode > 0u && solid_at(nextPos) > 0.5)
        {
            if (mode == 1u) // kill (the trail still drains)
            {
                p.lifetime = min(p.lifetime, p.age);
            }
            else if (mode == 2u) // settle
            {
                p.vel = float2(0.0, 0.0);
                p.flags = 1.0; // freeze at the last free position
            }
            else // bounce
            {
                float2 n = solid_normal(nextPos);
                p.vel = reflect(p.vel, n) * fx.bounciness;
            }
            // stay at the last free position regardless of response
        }
        else
        {
            p.pos = nextPos;
        }
    }

    // Trail: carry the ring slice across the ping-pong, keep the head sample
    // live at the current position, and advance the head on a fixed cadence so
    // the oldest sample ages out at exactly trailSeconds.
    for (uint t = 0u; t < TRAIL_SAMPLES; t++)
        TrailDst[trailBase + t] = TrailSrc[trailBase + t];
    if (fx.trailSeconds > 0.0)
    {
        float cadence = fx.trailSeconds / float(TRAIL_SAMPLES - 1);
        uint head = (uint)(p.trailHead + 0.5);
        p.trailTimer += Dt;
        if (p.trailTimer >= cadence)
        {
            p.trailTimer = fmod(p.trailTimer, cadence);
            head = (head + 1u) % TRAIL_SAMPLES;
            p.trailHead = (float)head;
        }
        TrailDst[trailBase + head] = p.pos;
    }

    Dst[i] = p;
}
