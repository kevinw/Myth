// Myth's point-light shadows (LightShadowDepth.hlsl), cast on the CPU since the room's occluders
// never move and its lights rarely do: each light sends SHADOW_SAMPLES rays around itself, and a
// row of `shadow_depths` holds how far each gets before an occluder. The lighting shader softens
// and compares against it, and only the background is shadowed.
// Not ported yet: sprite shadows (actors casting soft ones), the sun.

SHADOW_SAMPLES :: 512;
shadow_depths: Texture;  // SHADOW_SAMPLES x MAX_LIGHTS, r = distance to the nearest occluder

// `lights`: their positions and reach, in the lighting uniforms' order. A light is recast only when
// it moves, against just the edges within its reach.
update_shadow_depths :: (lights: [] Vector2, radii: [] float) {
    if !shadow_depths.gpu shadow_depths = make_texture(xx depths.data, SHADOW_SAMPLES, MAX_LIGHTS, .R32_FLOAT);
    changed := false;
    near: [..] Segment;
    near.allocator = temp;
    for light, row: lights {
        if row < cast_count && cast_from[row] == light continue;
        cast_from[row] = light;
        changed = true;
        near.count = 0;
        reach := radii[row] * 1.25;  // flicker breathes the radius
        for occluders if segment_distance(light, it) <= reach array_add(*near, it);
        for i: 0..SHADOW_SAMPLES-1 {
            angle := (i + 0.5) / SHADOW_SAMPLES * TAU;
            direction := Vector2.{cos(angle), sin(angle)};
            depth := FLOAT32_MAX;
            for near {
                distance, hit := ray_segment(light, direction, it);
                if hit depth = min(depth, distance);
            }
            depths[row * SHADOW_SAMPLES + i] = depth;
        }
    }
    if lights.count != cast_count changed = true;
    cast_count = lights.count;
    if changed update_texture(shadow_depths, xx depths.data);
}

#scope_file

depths: [SHADOW_SAMPLES * MAX_LIGHTS] float;
cast_from: [MAX_LIGHTS] Vector2;
cast_count := -1;

segment_distance :: (p: Vector2, s: Segment) -> float {
    edge := s.b - s.a;
    t := clamp(dot(p - s.a, edge) / max(dot(edge, edge), 0.0001), 0, 1);
    return length(p - (s.a + edge * t));
}

ray_segment :: (origin: Vector2, direction: Vector2, segment: Segment) -> float, bool {
    cross :: (a: Vector2, b: Vector2) -> float { return a.x * b.y - a.y * b.x; }
    edge := segment.b - segment.a;
    denominator := cross(direction, edge);
    if abs(denominator) <= 0.0001 return 0, false;
    delta := segment.a - origin;
    distance := cross(delta, edge) / denominator;
    along := cross(delta, direction) / denominator;
    return distance, distance >= 0 && along >= 0 && along <= 1;
}

#import "Basic";
#import "Math";
#import "Kit";
