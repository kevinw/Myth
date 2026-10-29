// Myth's pixel sort (Actors/PixelSort.cs, Myth's own PixelSort.hlsl), as the churn a burrow
// leaves: the ground it passes through sorts out of the surface behind it, harder the faster it
// went, and settles back once it's passed. A field of where each view pixel looks, anchored to
// the world, takes a few odd-even sort passes of the lit view a frame while displaced pixels
// relax home, and the view is redrawn through it. Strokes say where and how it sorts.
// Not ported: authored sort regions, the background layer, the glitch's sort.

// Where the burrow has been lately (PixelSortTrail.Record); `toward` points into the surface.
churn_record :: (at: Point2, toward: Vector2, speed01: float) {
    SPACING :: 2.0;
    STRENGTH :: 0.6;
    position := to_vector(at);
    if trail.count && length(trail.samples[trail.head].at - position) < SPACING return;
    trail.head = (trail.head + 1) % trail.samples.count;
    direction: s32 = ifx abs(toward.x) > abs(toward.y) then (ifx toward.x > 0 then LEFT else RIGHT) else (ifx toward.y > 0 then UP else DOWN);
    trail.samples[trail.head] = .{position, trail.clock, direction, clamp(speed01, 0.35, 1) * STRENGTH};
    trail.count = min(trail.count + 1, trail.samples.count);
}

// After the lighting: stirs, sorts and redraws `lit` through the field. `view` is the camera.
apply_pixel_sort :: (lit: Texture, view: Vector2, dt: float) {
    LIFE :: 0.8;
    WIDTH :: 6.0;
    REACH :: 17.0;
    RELAX_RATE :: 40.0;
    FEATHER :: 0.55;
    STEPS :: 6;
    HOME_RATE :: 60.0;      // how fast a wake left behind slides home
    IDLE_SECONDS :: 3.0;    // nothing stirred for this long, it's all home: stop
    MAX_GAP :: 24.0;        // a jump further than this breaks the trail

    trail.clock += dt;
    // The trail as strokes: fresh stretches hold their sort, older ones let go.
    strokes: [64] struct { from, to: Vector2; gate, relax: float; direction: s32; };
    stroke_count := 0;
    for i: 1..trail.count-1 {
        newer := trail.samples[(trail.head - i + 1 + trail.samples.count) % trail.samples.count];
        older := trail.samples[(trail.head - i + trail.samples.count) % trail.samples.count];
        fade := 1 - (trail.clock - older.time) / LIFE;
        if fade <= 0 break;
        if length(older.at - newer.at) > MAX_GAP continue;
        strokes[stroke_count] = .{older.at, newer.at, fade * newer.strength, RELAX_RATE * (1 - fade), newer.direction};
        stroke_count += 1;
    }
    field.idle = ifx stroke_count then 0.0 else field.idle + dt;
    if field.idle > IDLE_SECONDS { field.live = false; return; }

    if !shader.pipelines[0].pipeline shader = load_shader("PixelSort", samplers = 4, uniforms = 1);
    if !field.front.gpu {
        field.front = make_target(xx VIEW.x, xx VIEW.y, .R16G16B16A16_FLOAT);
        field.back = make_target(xx VIEW.x, xx VIEW.y, .R16G16B16A16_FLOAT);
        field.stir_a = make_target(xx VIEW.x, xx VIEW.y);
        field.stir_b = make_target(xx VIEW.x, xx VIEW.y);
        field.scene = make_target(xx VIEW.x, xx VIEW.y);
    }

    // Anchored to the world: last frame's offsets are read back from where they now sit.
    origin := Point2.{xx floor(view.x), xx floor(view.y)};
    shift := origin - field.origin;
    field.origin = origin;
    if !field.live || abs(shift.x) >= cast(s32) VIEW.x || abs(shift.y) >= cast(s32) VIEW.y {
        begin_pass(field.front, blend = .REPLACE);
        shift = .{};
        field.live = true;
    }

    // Each stroke twice, a setting per channel (see PixelSort.hlsl's packing), keeping the
    // strongest of whatever they overlap; the gate alone feathers out at the edges.
    for pass: 0..1 {
        begin_pass(ifx pass then field.stir_b else field.stir_a, blend = .MAX);
        push_matrix(-to_vector(origin), 1);
        for i: 0..stroke_count-1 {
            it := strokes[i];
            color := ifx pass == 0 then unit_color(it.gate, 0, 1) else unit_color(REACH / 255, it.relax / 255, it.direction / 3.0);
            edge := ifx pass == 0 then Color.{0, color.g, color.b, color.a} else color;
            along := unit_vector(it.to - it.from);
            side := Vector2.{-along.y, along.x} * WIDTH * 0.5;
            core := side * (1 - FEATHER);
            triangle(it.from + core, it.from - core, it.to - core, color, color, color);
            triangle(it.from + core, it.to - core, it.to + core, color, color, color);
            triangle(it.from + side, it.from + core, it.to + core, edge, color, color);
            triangle(it.from + side, it.to + core, it.to + side, edge, color, edge);
            triangle(it.from - core, it.from - side, it.to - side, color, edge, edge);
            triangle(it.from - core, it.to - side, it.to - core, color, edge, color);
        }
        pop_matrix();
    }

    relax := dt / STEPS;
    for 0..STEPS-1 {
        u := float.[VIEW.x, VIEW.y, zoom, 0,  VIEW.x, VIEW.y, xx ifx it == 0 then shift.x else 0, xx ifx it == 0 then shift.y else 0,
                    xx (field.pass & 1), xx (field.pass & 1023), relax, HOME_RATE * relax];
        begin_pass(field.back, shader = *shader, uniforms = .{size_of(type_of(u)), xx u.data}, extra = .[lit, field.stir_a, field.stir_b], blend = .REPLACE);
        image(field.front, .{.{}, VIEW}, .{});
        field.front, field.back = field.back, field.front;
        field.pass += 1;
    }

    // Redrawn through the field, from a copy so the view isn't read while it's drawn.
    begin_pass(field.scene, blend = .REPLACE);
    image(lit, .{.{}, VIEW}, .{});
    u := float.[VIEW.x, VIEW.y, zoom, 1,  VIEW.x, VIEW.y, 0, 0,  0, 0, 0, 0];
    begin_pass(lit, keep = true, shader = *shader, uniforms = .{size_of(type_of(u)), xx u.data}, extra = .[field.scene, field.stir_a, field.stir_b], blend = .REPLACE);
    push_matrix(.{}, zoom);
    image(field.front, .{.{}, VIEW}, .{});
    pop_matrix();
}

#scope_file

RIGHT :s32: 0; LEFT :s32: 1; DOWN :s32: 2; UP :s32: 3;  // PixelSortDirection: the way a streak's bright end runs

trail: struct {
    samples: [64] struct { at: Vector2; time: float; direction: s32; strength: float; };
    head, count: s32;
    clock: float;
};

field: struct {
    front, back, stir_a, stir_b, scene: Texture;
    origin: Point2;
    pass: s32;
    live: bool;
    idle := 100.0;
};

shader: Shader;

unit_color :: (r: float, g: float, b: float) -> Color {
    return .{xx (clamp(r, 0, 1) * 255), xx (clamp(g, 0, 1) * 255), xx (clamp(b, 0, 1) * 255), 255};
}

#import "Basic";
#import "Math";
#import "SDL3";
#import "Kit";
