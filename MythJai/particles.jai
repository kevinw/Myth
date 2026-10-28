// Myth's particles (Effects/ParticleSystem, ParticleUpdate.hlsl, Particle.hlsl), on the CPU. An
// effect's json (Assets/Particles/<name>.json) sets how its particles fly -- speed, life, gravity,
// drag and a curl-noise drift -- what they do against the room's solid tiles (bounce, settle or
// die), and how they look over their life: a shrinking square through a three-colour ramp,
// glowing additively over the lit world, or a bright plasma core.
// Not ported: trails, continuous emitters, sprite-edge emission, the lighting response.

// A cone burst from `at`, aimed along `direction` (radians, y down), the effect's speeds scaled.
emit :: (effect_name: string, at: Vector2, count: int, direction: float, speed_scale := 1.0, spread := -1.0) {
    e := find_effect(effect_name);
    if !e return;
    cone := ifx spread >= 0 then spread else ifx e.line then 0.0 else e.spread_degrees * PI / 180;
    for 1..count {
        angle := direction + (rand01() - 0.5) * cone;
        speed := lerp(e.speed_min, e.speed_max, rand01()) * speed_scale;
        add_particle(e, at, Vector2.{cos(angle), sin(angle)} * speed);
    }
}

// The effect's burst spread along a line from `origin` (ParticleSystem.EmitLine): any direction
// out of it, carried by `base_velocity`.
emit_line :: (effect_name: string, origin: Vector2, direction: Vector2, length := -1.0, base_velocity := Vector2.{}) {
    e := find_effect(effect_name);
    if !e return;
    along := unit_vector(direction);
    across := Vector2.{-along.y, along.x};
    reach := ifx length >= 0 then length else e.emitter_radius;
    width := ifx e.line then e.spread_degrees else e.emitter_radius;
    for 1..e.burst_count {
        at := origin + along * (rand01() * reach) + across * ((rand01() - 0.5) * width);
        angle := rand01() * TAU;
        speed := lerp(e.speed_min, e.speed_max, rand01());
        add_particle(e, at, Vector2.{cos(angle), sin(angle)} * speed + base_velocity);
    }
}

update_particles :: (dt: float) {
    time := cast(float) now();
    for * particles {
        if it.life <= 0 continue;
        it.age += dt;
        if it.age >= it.life { it.life = 0; continue; }
        if it.settled continue;
        e := *effects[it.effect];
        it.velocity += curl(it.position * e.noise_scale + Vector2.{1, 1} * (time * 0.5 + it.seed)) * e.noise_amount * dt;
        it.velocity.y += e.gravity * dt;
        it.velocity *= clamp(1 - e.drag * dt, 0, 1);
        next := it.position + it.velocity * dt;
        if e.collision == .NONE || !solid_at(next) { it.position = next; continue; }
        if e.collision == {
          case .KILL;   it.life = min(it.life, it.age);
          case .SETTLE; it.velocity, it.settled = .{}, true;
          case .BOUNCE; n := solid_normal(next); it.velocity = (it.velocity - n * (2 * dot(it.velocity, n))) * e.bounciness;
        }
    }
}

// In the world's matrix, over the lit view.
draw_particles :: () {
    for particles {
        if it.life <= 0 continue;
        e := *effects[it.effect];
        t := it.age / it.life;
        size := lerp(e.size_start, e.size_end, t);
        c := ifx t < 0.5 then lerp(e.birth, e.mid, t * 2) else lerp(e.mid, e.end, (t - 0.5) * 2);
        // Plasma: at these sizes every pixel is its white-hot core.
        if e.plasma c = .{lerp(c.x, 1, 0.85), lerp(c.y, 1, 0.85), lerp(c.z, 1, 0.85), c.w};
        // Additive (SrcAlpha, One): premultiplied colour with no alpha adds onto what's there.
        color := Color.{xx (clamp(c.x * c.w, 0, 1) * 255), xx (clamp(c.y * c.w, 0, 1) * 255), xx (clamp(c.z * c.w, 0, 1) * 255), 0};
        rect(.{it.position - Vector2.{size, size} * 0.5, .{size, size}}, color);
    }
}

#scope_file

MAX_PARTICLES :: 2048;

Particle :: struct {
    position, velocity: Vector2;
    age, life, seed: float;
    effect: s32;
    settled: bool;
}

Particle_Effect :: struct {
    name: string;
    line: bool;                 // a Line emitter: its spread is a width in pixels
    emitter_radius := 4.0;
    spread_degrees := 40.0;
    burst_count: s32 = 256;
    speed_min := 20.0;
    speed_max := 120.0;
    life_min := 0.6;
    life_max := 1.6;
    gravity := 60.0;
    drag := 0.6;
    noise_amount := 40.0;
    noise_scale := 0.05;
    size_start := 2.0;
    size_end := 0.5;
    plasma: bool;
    collision: enum u8 { NONE; KILL; SETTLE; BOUNCE; };
    bounciness := 0.4;
    birth := Vector4.{1, 0.95, 0.6, 1};
    mid := Vector4.{1, 0.5, 0.15, 1};
    end := Vector4.{0.3, 0.05, 0.05, 0};
}

particles: [MAX_PARTICLES] Particle;
next_particle: int;
effects: [16] Particle_Effect;
effect_count: int;

add_particle :: (e: *Particle_Effect, at: Vector2, velocity: Vector2) {
    p := *particles[next_particle];
    next_particle = (next_particle + 1) % MAX_PARTICLES;
    p.* = .{at, velocity, 0, lerp(e.life_min, e.life_max, rand01()), rand01() * 1000, xx (e - effects.data), false};
}

find_effect :: (name: string) -> *Particle_Effect {
    for i: 0..effect_count-1 if effects[i].name == name return *effects[i];
    if effect_count == effects.count return null;
    text, ok := read_entire_file(tprint("%/Particles/%.json", ASSETS, name), log_errors = false);
    if !ok { log_error("no particle effect %", name); return null; }
    json:, ok = parse_json(text);
    if !ok { log_error("particle effect % isn't valid JSON", name); return null; }
    e := *effects[effect_count];
    effect_count += 1;
    e.* = .{name = copy_string(name)};
    e.line = as_string(get(json, "Shape")) == "Line";
    number :: (field: *float, key: string) #expand { field.* = as_float(get(json, key), field.*); }
    number(*e.emitter_radius, "EmitterRadius");
    number(*e.spread_degrees, "SpreadDegrees");
    e.burst_count = xx as_int(get(json, "BurstCount"), e.burst_count);
    number(*e.speed_min, "SpeedMin");
    number(*e.speed_max, "SpeedMax");
    number(*e.life_min, "LifeMin");
    number(*e.life_max, "LifeMax");
    number(*e.gravity, "Gravity");
    number(*e.drag, "Drag");
    number(*e.noise_amount, "NoiseAmount");
    number(*e.noise_scale, "NoiseScale");
    number(*e.size_start, "SizeStart");
    number(*e.size_end, "SizeEnd");
    number(*e.bounciness, "Bounciness");
    e.plasma = as_string(get(json, "Style")) == "Plasma";
    if as_string(get(json, "Collision")) == {
      case "Kill";   e.collision = .KILL;
      case "Settle"; e.collision = .SETTLE;
      case "Bounce"; e.collision = .BOUNCE;
    }
    color :: (field: *Vector4, key: string) #expand {
        hex := as_string(get(json, key));
        if !hex return;
        c := rgb(hex);
        field.* = .{c.r / 255.0, c.g / 255.0, c.b / 255.0, c.a / 255.0};
    }
    color(*e.birth, "ColorBirth");
    color(*e.mid, "ColorMid");
    color(*e.end, "ColorEnd");
    return e;
}

solid_at :: (p: Vector2) -> bool { return overlaps_solid(.{xx floor(p.x), xx floor(p.y), 1, 1}); }

// Away from the solid, from the slope of solidity two pixels round (up if it's flat).
solid_normal :: (p: Vector2) -> Vector2 {
    grad := Vector2.{cast(float) solid_at(p + .{2, 0}) - cast(float) solid_at(p - .{2, 0}), cast(float) solid_at(p + .{0, 2}) - cast(float) solid_at(p - .{0, 2})};
    if dot(grad, grad) < 0.000001 return .{0, -1};
    return -unit_vector(grad);
}

hash21 :: (p: Vector2) -> float {
    q := Vector2.{frac(p.x * 123.34), frac(p.y * 345.45)};
    d := dot(q, q + Vector2.{34.345, 34.345});
    q += Vector2.{d, d};
    return frac(q.x * q.y);
}

vnoise :: (p: Vector2) -> float {
    i := Vector2.{floor(p.x), floor(p.y)};
    f := p - i;
    u := Vector2.{f.x * f.x * (3 - 2 * f.x), f.y * f.y * (3 - 2 * f.y)};
    a, b := hash21(i), hash21(i + .{1, 0});
    c, d := hash21(i + .{0, 1}), hash21(i + .{1, 1});
    return lerp(lerp(a, b, u.x), lerp(c, d, u.x), u.y);
}

curl :: (p: Vector2) -> Vector2 {
    E :: 0.1;
    up, down := vnoise(p + .{0, E}), vnoise(p - .{0, E});
    right, left := vnoise(p + .{E, 0}), vnoise(p - .{E, 0});
    return .{(up - down) / (2 * E), -(right - left) / (2 * E)};
}

frac :: (x: float) -> float { return x - floor(x); }

// Their own random numbers, so effects don't shift the game's (the blob's hops, the demo).
rng_state: u32 = 0x9e3779b9;
rand01 :: () -> float {
    x := rng_state;
    x ^= x << 13; x ^= x >> 17; x ^= x << 5;
    rng_state = x;
    return cast(float) (x >> 8) / 16777216.0;
}

#import "Basic";
#import "File";
#import "Math";
#import "Kit";
