// Myth's point-light shadows (LightShadowDepth.hlsl), cast on the CPU since the room's occluders
// never move and its lights rarely do: each light sends SHADOW_SAMPLES rays around itself, and a
// row of `shadow_depths` holds how far each gets before an occluder. The lighting shader softens
// and compares against it, and only the background is shadowed.
// Not ported yet: sprite shadows (actors casting soft ones), the sun.

SHADOW_SAMPLES :: 512;
shadow_depths: Texture;  // SHADOW_SAMPLES x MAX_LIGHTS, r = distance to the nearest occluder

// `lights`: their positions, in the lighting uniforms' order. Recasts only when they move.
update_shadow_depths :: (lights: [] Vector2) {
    if !shadow_depths.gpu shadow_depths = make_texture(xx depths.data, SHADOW_SAMPLES, MAX_LIGHTS, .R32_FLOAT);
    if lights.count == cast_count && memcmp(lights.data, cast_from.data, lights.count * size_of(Vector2)) == 0 return;
    for light, row: lights {
        cast_from[row] = light;
        for i: 0..SHADOW_SAMPLES-1 {
            angle := (i + 0.5) / SHADOW_SAMPLES * TAU;
            direction := Vector2.{cos(angle), sin(angle)};
            depth := FLOAT32_MAX;
            for occluders {
                distance, hit := ray_segment(light, direction, it);
                if hit depth = min(depth, distance);
            }
            depths[row * SHADOW_SAMPLES + i] = depth;
        }
    }
    cast_count = lights.count;
    update_texture(shadow_depths, xx depths.data);
}

#scope_file

depths: [SHADOW_SAMPLES * MAX_LIGHTS] float;
cast_from: [MAX_LIGHTS] Vector2;
cast_count := -1;

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
