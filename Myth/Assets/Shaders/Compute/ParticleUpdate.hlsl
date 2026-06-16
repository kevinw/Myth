// GPU particle simulation step (ping-pong, multi-effect, world space) with
// world collision sampled from a solid mask texture (the foreground coverage).
//
// SDL_gpu cycles read-write storage buffers each dispatch, so state cannot
// persist in place: read a stable Src buffer and write EVERY slot of Dst.
//
// A sampled texture occupies t0 in space0, so the storage buffers follow at
// t1/t2/t3.

struct Particle
{
    float2 pos;
    float2 vel;
    float age;
    float lifetime;
    float seed;
    float effect; // effect index (stored as float)
    float flags;  // 0 = active, 1 = settled (frozen on a surface)
    float pad;    // pads to 40 bytes (8-byte aligned) to match the C# struct
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
};

struct EmitParams
{
    uint start;
    uint count;
    uint seed;
    uint effect;
    float originX;
    float originY;
    uint shape;       // 0 = point, 1 = circle, 2 = cone
    float radius;
    float dirAngle;   // radians
    float spread;     // radians
    float speedMin;
    float speedMax;
    float lifeMin;
    float lifeMax;
    float pad0;
    float pad1;
};

// World collision is sampled from the foreground coverage mask (its alpha).
// A sampled texture occupies t0 in space0, so the storage buffers follow at
// t1/t2/t3.
Texture2D SolidMask : register(t0, space0);
SamplerState SolidSampler : register(s0, space0);
StructuredBuffer<Particle> Src : register(t1, space0);
StructuredBuffer<EffectParams> Effects : register(t2, space0);
StructuredBuffer<EmitParams> Emits : register(t3, space0);
RWStructuredBuffer<Particle> Dst : register(u0, space1);

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
    float Pad0;
    float Pad1;
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
    float2 uv = (worldPos - float2(CameraX, CameraY)) * Zoom / float2(TargetW, TargetH);
    if (uv.x < 0.0 || uv.x > 1.0 || uv.y < 0.0 || uv.y > 1.0)
        return 0.0;
    return SolidMask.SampleLevel(SolidSampler, uv, 0.0).a;
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

        float angle;
        if (em.shape == 2u)
            angle = em.dirAngle + (hash1(i * 7u + em.seed) - 0.5) * em.spread;
        else
            angle = hash1(i * 7u + em.seed) * 6.2831853;

        float speed = lerp(em.speedMin, em.speedMax, hash1(i * 13u + em.seed + 1u));

        Particle np;
        np.pos = origin;
        np.vel = float2(cos(angle), sin(angle)) * speed;
        np.age = 0.0;
        np.lifetime = lerp(em.lifeMin, em.lifeMax, hash1(i * 17u + em.seed + 2u));
        np.seed = hash1(i * 19u + em.seed + 3u) * 1000.0;
        np.effect = (float)em.effect;
        np.flags = 0.0;
        Dst[i] = np;
        return;
    }

    Particle p = Src[i];
    if (p.lifetime <= 0.0)
    {
        Dst[i] = p; // keep dead slot defined
        return;
    }

    p.age += Dt;
    if (p.age >= p.lifetime)
    {
        p.lifetime = 0.0;
        Dst[i] = p;
        return;
    }

    // Settled particles are frozen on a surface; they only age out.
    if (p.flags >= 0.5)
    {
        Dst[i] = p;
        return;
    }

    EffectParams fx = Effects[(uint)(p.effect + 0.5)];
    uint mode = (uint)(fx.collisionMode + 0.5);

    float2 force = curl(p.pos * fx.noiseScale + Time * 0.5 + p.seed) * fx.noiseAmount;
    p.vel += force * Dt;
    p.vel.y += fx.gravity * Dt;
    p.vel *= saturate(1.0 - fx.drag * Dt);

    float2 nextPos = p.pos + p.vel * Dt;

    if (mode > 0u && solid_at(nextPos) > 0.5)
    {
        if (mode == 1u) // kill
        {
            p.lifetime = 0.0;
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

    Dst[i] = p;
}
