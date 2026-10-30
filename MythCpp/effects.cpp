// Effects: particles, the burrow's pixel sort, and the point lights' shadows.
#include "game.h"

// --- Myth's particles (Effects/ParticleSystem, ParticleUpdate.hlsl, Particle.hlsl), on the CPU. An
// effect's json (Assets/Particles/<name>.json) sets how its particles fly -- speed, life, gravity,
// drag and a curl-noise drift -- what they do against the room's solid tiles (bounce, settle or
// die), and how they look over their life: a shrinking square through a three-colour ramp, glowing
// additively over the lit world, or a bright plasma core.
// Not ported: trails, continuous emitters, sprite-edge emission, the lighting response. ---

constexpr s32 MAX_PARTICLES = 2048;

struct Particle {
    Vector2 position, velocity;
    float age = 0, life = 0, seed = 0;
    s32 effect = 0;
    bool settled = false;
};

struct ParticleEffect {
    char name[32] = {};
    bool line = false;          // a Line emitter: its spread is a width in pixels
    float emitter_radius = 4;
    float spread_degrees = 40;
    s32 burst_count = 256;
    float speed_min = 20, speed_max = 120;
    float life_min = 0.6f, life_max = 1.6f;
    float gravity = 60;
    float drag = 0.6f;
    float noise_amount = 40, noise_scale = 0.05f;
    float size_start = 2, size_end = 0.5f;
    bool plasma = false;
    enum Collision : u8 { NONE, KILL, SETTLE, BOUNCE } collision = NONE;
    float bounciness = 0.4f;
    Vector4 birth{1, 0.95f, 0.6f, 1}, mid{1, 0.5f, 0.15f, 1}, end{0.3f, 0.05f, 0.05f, 0};
};

static Particle particles[MAX_PARTICLES];
static s32 next_particle;
static ParticleEffect effects[16];
static s32 effect_count;

// Their own random numbers, so effects don't shift the game's (the blob's hops, the demo).
static u32 rng_state = 0x9e3779b9;
static float rand01() {
    u32 x = rng_state;
    x ^= x << 13; x ^= x >> 17; x ^= x << 5;
    rng_state = x;
    return float(x >> 8) / 16777216.0f;
}

static Vector4 lerp(Vector4 a, Vector4 b, float t) { return {lerp(a.x, b.x, t), lerp(a.y, b.y, t), lerp(a.z, b.z, t), lerp(a.w, b.w, t)}; }

static ParticleEffect* find_effect(string_view name) {
    for (s32 i = 0; i < effect_count; i++) if (name == effects[i].name) return &effects[i];
    if (effect_count == 16) return nullptr;
    auto text = read_file(asset("Particles/%.*s.json", (int)name.size(), name.data()));
    if (text.empty()) { log("no particle effect %.*s", (int)name.size(), name.data()); return nullptr; }
    auto json = parse_json({(const char*)text.data(), text.size()}, temp);
    if (!json) { log("particle effect %.*s isn't valid JSON", (int)name.size(), name.data()); return nullptr; }
    ParticleEffect* e = &effects[effect_count++];
    *e = {};
    snprintf(e->name, sizeof(e->name), "%.*s", (int)name.size(), name.data());
    Json j = *json;
    e->line = j["Shape"].as_string() == "Line";
    read(j["EmitterRadius"], e->emitter_radius);
    read(j["SpreadDegrees"], e->spread_degrees);
    read(j["BurstCount"], e->burst_count);
    read(j["SpeedMin"], e->speed_min);
    read(j["SpeedMax"], e->speed_max);
    read(j["LifeMin"], e->life_min);
    read(j["LifeMax"], e->life_max);
    read(j["Gravity"], e->gravity);
    read(j["Drag"], e->drag);
    read(j["NoiseAmount"], e->noise_amount);
    read(j["NoiseScale"], e->noise_scale);
    read(j["SizeStart"], e->size_start);
    read(j["SizeEnd"], e->size_end);
    read(j["Bounciness"], e->bounciness);
    e->plasma = j["Style"].as_string() == "Plasma";
    string_view collision = j["Collision"].as_string();
    if (collision == "Kill") e->collision = ParticleEffect::KILL;
    else if (collision == "Settle") e->collision = ParticleEffect::SETTLE;
    else if (collision == "Bounce") e->collision = ParticleEffect::BOUNCE;
    auto color = [&](Vector4& field, const char* key) {
        string_view hex = j[key].as_string();
        if (hex.empty()) return;
        Color c = rgb(hex);
        field = {c.r / 255.0f, c.g / 255.0f, c.b / 255.0f, c.a / 255.0f};
    };
    color(e->birth, "ColorBirth");
    color(e->mid, "ColorMid");
    color(e->end, "ColorEnd");
    return e;
}

static void add_particle(ParticleEffect* e, Vector2 at, Vector2 velocity) {
    Particle& p = particles[next_particle];
    next_particle = (next_particle + 1) % MAX_PARTICLES;
    p = {at, velocity, 0, lerp(e->life_min, e->life_max, rand01()), rand01() * 1000, s32(e - effects), false};
}

void emit(string_view effect, Vector2 at, s32 count, float direction, float speed_scale, float spread) {
    ParticleEffect* e = find_effect(effect);
    if (!e) return;
    float cone = spread >= 0 ? spread : e->line ? 0 : e->spread_degrees * PI / 180;
    for (s32 i = 0; i < count; i++) {
        float angle = direction + (rand01() - 0.5f) * cone;
        float speed = lerp(e->speed_min, e->speed_max, rand01()) * speed_scale;
        add_particle(e, at, Vector2{cosf(angle), sinf(angle)} * speed);
    }
}

void emit_line(string_view effect, Vector2 origin, Vector2 direction, float length, Vector2 base_velocity) {
    ParticleEffect* e = find_effect(effect);
    if (!e) return;
    Vector2 along = unit(direction), across{-along.y, along.x};
    float reach = length >= 0 ? length : e->emitter_radius;
    float width = e->line ? e->spread_degrees : e->emitter_radius;
    for (s32 i = 0; i < e->burst_count; i++) {
        Vector2 at = origin + along * (rand01() * reach) + across * ((rand01() - 0.5f) * width);
        float angle = rand01() * TAU;
        float speed = lerp(e->speed_min, e->speed_max, rand01());
        add_particle(e, at, Vector2{cosf(angle), sinf(angle)} * speed + base_velocity);
    }
}

static bool solid_at(Vector2 p) { return overlaps_solid(RectInt{(s32)floorf(p.x), (s32)floorf(p.y), 1, 1}); }

// Away from the solid, from the slope of solidity two pixels round (up if it's flat).
static Vector2 solid_normal(Vector2 p) {
    Vector2 grad{(float)solid_at(p + Vector2{2, 0}) - (float)solid_at(p - Vector2{2, 0}), (float)solid_at(p + Vector2{0, 2}) - (float)solid_at(p - Vector2{0, 2})};
    if (dot(grad, grad) < 0.000001f) return {0, -1};
    return -unit(grad);
}

static float frac(float x) { return x - floorf(x); }

static float hash21(Vector2 p) {
    Vector2 q{frac(p.x * 123.34f), frac(p.y * 345.45f)};
    float d = dot(q, q + Vector2{34.345f, 34.345f});
    q += Vector2{d, d};
    return frac(q.x * q.y);
}

static float vnoise(Vector2 p) {
    Vector2 i = p.floor(), f = p - i;
    Vector2 u{f.x * f.x * (3 - 2 * f.x), f.y * f.y * (3 - 2 * f.y)};
    float a = hash21(i), b = hash21(i + Vector2{1, 0}), c = hash21(i + Vector2{0, 1}), d = hash21(i + Vector2{1, 1});
    return lerp(lerp(a, b, u.x), lerp(c, d, u.x), u.y);
}

static Vector2 curl(Vector2 p) {
    const float E = 0.1f;
    float up = vnoise(p + Vector2{0, E}), down = vnoise(p - Vector2{0, E});
    float right = vnoise(p + Vector2{E, 0}), left = vnoise(p - Vector2{E, 0});
    return {(up - down) / (2 * E), -(right - left) / (2 * E)};
}

void update_particles(float dt) {
    float time = (float)now();
    for (Particle& p : particles) {
        if (p.life <= 0) continue;
        p.age += dt;
        if (p.age >= p.life) { p.life = 0; continue; }
        if (p.settled) continue;
        ParticleEffect& e = effects[p.effect];
        p.velocity += curl(p.position * e.noise_scale + Vector2{1, 1} * (time * 0.5f + p.seed)) * e.noise_amount * dt;
        p.velocity.y += e.gravity * dt;
        p.velocity = p.velocity * clamp(1 - e.drag * dt, 0.0f, 1.0f);
        Vector2 next = p.position + p.velocity * dt;
        if (e.collision == ParticleEffect::NONE || !solid_at(next)) { p.position = next; continue; }
        switch (e.collision) {
            case ParticleEffect::KILL: p.life = min(p.life, p.age); break;
            case ParticleEffect::SETTLE: p.velocity = {}; p.settled = true; break;
            case ParticleEffect::BOUNCE: { Vector2 n = solid_normal(next); p.velocity = (p.velocity - n * (2 * dot(p.velocity, n))) * e.bounciness; break; }
            default: break;
        }
    }
}

void draw_particles() {
    for (Particle& p : particles) {
        if (p.life <= 0) continue;
        ParticleEffect& e = effects[p.effect];
        float t = p.age / p.life;
        float size = lerp(e.size_start, e.size_end, t);
        Vector4 c = t < 0.5f ? lerp(e.birth, e.mid, t * 2) : lerp(e.mid, e.end, (t - 0.5f) * 2);
        // Plasma: at these sizes every pixel is its white-hot core.
        if (e.plasma) c = {lerp(c.x, 1, 0.85f), lerp(c.y, 1, 0.85f), lerp(c.z, 1, 0.85f), c.w};
        // Additive (SrcAlpha, One): premultiplied colour with no alpha adds onto what's there.
        auto channel = [&](float v) { return u8(clamp(v * c.w, 0.0f, 1.0f) * 255); };
        rect({p.position - Vector2{size, size} * 0.5f, {size, size}}, {channel(c.x), channel(c.y), channel(c.z), 0});
    }
}

// --- Myth's pixel sort (Actors/PixelSort.cs, Myth's own PixelSort.hlsl), as the churn a burrow
// leaves: the ground it passes through sorts out of the surface behind it, harder the faster it
// went, and settles back once it's passed. A field of where each view pixel looks, anchored to the
// world, takes a few odd-even sort passes of the lit view a frame while displaced pixels relax
// home, and the view is redrawn through it. Strokes say where and how it sorts.
// Not ported: authored sort regions, the background layer, the glitch's sort. ---

enum : s32 { RIGHT, LEFT, DOWN, UP };  // PixelSortDirection: the way a streak's bright end runs

static struct {
    struct { Vector2 at; float time; s32 direction; float strength; } samples[64];
    s32 head, count;
    float clock;
} trail{};

static struct {
    Texture front, back, stir_a, stir_b, scene;
    Point2 origin;
    s32 pass;
    bool live;
    float idle = 100;
} field{};

static Shader pixel_sort;

// Where the burrow has been lately (PixelSortTrail.Record); `toward` points into the surface.
void churn_record(Point2 at, Vector2 toward, float speed01) {
    const float SPACING = 2, STRENGTH = 0.6f;
    const s32 SIZE = 64;
    Vector2 position = at.vector();
    if (trail.count && (trail.samples[trail.head].at - position).length() < SPACING) return;
    trail.head = (trail.head + 1) % SIZE;
    s32 direction = fabsf(toward.x) > fabsf(toward.y) ? (toward.x > 0 ? LEFT : RIGHT) : (toward.y > 0 ? UP : DOWN);
    trail.samples[trail.head] = {position, trail.clock, direction, clamp(speed01, 0.35f, 1.0f) * STRENGTH};
    trail.count = min(trail.count + 1, SIZE);
}

static Color unit_color(float r, float g, float b) {
    return {u8(clamp(r, 0.0f, 1.0f) * 255), u8(clamp(g, 0.0f, 1.0f) * 255), u8(clamp(b, 0.0f, 1.0f) * 255), 255};
}

static std::span<const u8> bytes_of(const auto& value) { return {(const u8*)&value, sizeof(value)}; }

void apply_pixel_sort(const Texture& lit, Vector2 view, float dt) {
    const float LIFE = 0.8f, WIDTH = 6, REACH = 17, RELAX_RATE = 40, FEATHER = 0.55f;
    const s32 STEPS = 6;
    const float HOME_RATE = 60;     // how fast a wake left behind slides home
    const float IDLE_SECONDS = 3;   // nothing stirred for this long, it's all home: stop
    const float MAX_GAP = 24;       // a jump further than this breaks the trail
    const s32 SIZE = 64;

    trail.clock += dt;
    // The trail as strokes: fresh stretches hold their sort, older ones let go.
    struct Stroke { Vector2 from, to; float gate, relax; s32 direction; } strokes[64];
    s32 stroke_count = 0;
    for (s32 i = 1; i < trail.count; i++) {
        auto& newer = trail.samples[(trail.head - i + 1 + SIZE) % SIZE];
        auto& older = trail.samples[(trail.head - i + SIZE) % SIZE];
        float fade = 1 - (trail.clock - older.time) / LIFE;
        if (fade <= 0) break;
        if ((older.at - newer.at).length() > MAX_GAP) continue;
        strokes[stroke_count++] = {older.at, newer.at, fade * newer.strength, RELAX_RATE * (1 - fade), newer.direction};
    }
    field.idle = stroke_count ? 0 : field.idle + dt;
    if (field.idle > IDLE_SECONDS) { field.live = false; return; }

    if (!pixel_sort.name) pixel_sort = load_shader("PixelSort", 4, 1).value_or(Shader{.name = "?"});
    if (!field.front.gpu) {
        s32 w = (s32)VIEW.x, h = (s32)VIEW.y;
        field.front = make_target(w, h, SDL_GPU_TEXTUREFORMAT_R16G16B16A16_FLOAT);
        field.back = make_target(w, h, SDL_GPU_TEXTUREFORMAT_R16G16B16A16_FLOAT);
        field.stir_a = make_target(w, h);
        field.stir_b = make_target(w, h);
        field.scene = make_target(w, h);
    }

    // Anchored to the world: last frame's offsets are read back from where they now sit.
    Point2 origin{(s32)floorf(view.x), (s32)floorf(view.y)};
    Point2 shift = origin - field.origin;
    field.origin = origin;
    if (!field.live || abs(shift.x) >= (s32)VIEW.x || abs(shift.y) >= (s32)VIEW.y) {
        begin_pass({.target = field.front, .blend = Blend::Replace});
        shift = {};
        field.live = true;
    }

    // Each stroke twice, a setting per channel (see PixelSort.hlsl's packing), keeping the strongest
    // of whatever they overlap; the gate alone feathers out at the edges.
    for (s32 pass = 0; pass < 2; pass++) {
        begin_pass({.target = pass ? field.stir_b : field.stir_a, .blend = Blend::Max});
        push_matrix(-origin.vector(), 1);
        for (s32 i = 0; i < stroke_count; i++) {
            const Stroke& s = strokes[i];
            Color color = pass == 0 ? unit_color(s.gate, 0, 1) : unit_color(REACH / 255, s.relax / 255, s.direction / 3.0f);
            Color edge = pass == 0 ? Color{0, color.g, color.b, color.a} : color;
            Vector2 along = unit(s.to - s.from);
            Vector2 side = Vector2{-along.y, along.x} * WIDTH * 0.5f;
            Vector2 core = side * (1 - FEATHER);
            triangle(s.from + core, s.from - core, s.to - core, color, color, color);
            triangle(s.from + core, s.to - core, s.to + core, color, color, color);
            triangle(s.from + side, s.from + core, s.to + core, edge, color, color);
            triangle(s.from + side, s.to + core, s.to + side, edge, color, edge);
            triangle(s.from - core, s.from - side, s.to - side, color, edge, edge);
            triangle(s.from - core, s.to - side, s.to - core, color, edge, color);
        }
        pop_matrix();
    }

    float relax = dt / STEPS;
    for (s32 step = 0; step < STEPS; step++) {
        float u[12] = {VIEW.x, VIEW.y, zoom, 0, VIEW.x, VIEW.y, (float)(step == 0 ? shift.x : 0), (float)(step == 0 ? shift.y : 0),
                       (float)(field.pass & 1), (float)(field.pass & 1023), relax, HOME_RATE * relax};
        Texture extra[] = {lit, field.stir_a, field.stir_b};
        begin_pass({.target = field.back, .shader = &pixel_sort, .uniforms = bytes_of(u), .extra = extra, .blend = Blend::Replace});
        image(field.front, {{}, VIEW}, {});
        std::swap(field.front, field.back);
        field.pass++;
    }

    // Redrawn through the field, from a copy so the view isn't read while it's drawn.
    begin_pass({.target = field.scene, .blend = Blend::Replace});
    image(lit, {{}, VIEW}, {});
    float u[12] = {VIEW.x, VIEW.y, zoom, 1, VIEW.x, VIEW.y, 0, 0, 0, 0, 0, 0};
    Texture extra[] = {field.scene, field.stir_a, field.stir_b};
    begin_pass({.target = lit, .keep = true, .shader = &pixel_sort, .uniforms = bytes_of(u), .extra = extra, .blend = Blend::Replace});
    push_matrix({}, zoom);
    image(field.front, {{}, VIEW}, {});
    pop_matrix();
}

// --- Myth's point-light shadows (LightShadowDepth.hlsl), cast on the GPU like Myth's: a compute
// pass (shaders/shadow_depths.compute.hlsl) sends SHADOW_SAMPLES rays round each light against the
// room's merged occluder edges, and a row of `shadow_depths` holds how far each gets. The lighting
// shader softens and compares against it, and only the background is shadowed.
// Not ported yet: the sun. ---

constexpr s32 SHADOW_SAMPLES = 512;
Texture shadow_depths;  // SHADOW_SAMPLES x MAX_LIGHTS, r = distance to the nearest occluder
static ComputeShader shadow_kernel;
static GpuBuffer segments;

void update_shadow_depths(std::span<Vector2> lights) {
    if (!shadow_depths.gpu) {
        shadow_depths = make_storage(SHADOW_SAMPLES, MAX_LIGHTS, SDL_GPU_TEXTUREFORMAT_R32_FLOAT);
        shadow_kernel = load_compute("shadow_depths", 1, 1, 1, 64).value_or(ComputeShader{});
    }
    // The occluders only change with the room.
    if (segments.size != occluders.count * sizeof(Segment)) {
        segments = make_buffer(occluders.count * sizeof(Segment));
        update_buffer(segments, occluders.data, segments.size);
    }
    struct { u32 segment_count, light_count, samples, padding; Vector4 lights[MAX_LIGHTS]; } params{(u32)occluders.count, (u32)lights.size(), SHADOW_SAMPLES};
    for (size_t i = 0; i < lights.size(); i++) params.lights[i] = {lights[i].x, lights[i].y};
    Texture targets[] = {shadow_depths};
    GpuBuffer buffers[] = {segments};
    dispatch({.shader = &shadow_kernel, .textures = targets, .buffers = buffers, .uniforms = bytes_of(params),
              .groups_x = SHADOW_SAMPLES / 64, .groups_y = (u32)max<size_t>(lights.size(), 1)});
}
