PlayerStart :: struct @GameActor {
    #as using actor: Actor;
}

draw :: (s: *PlayerStart) { rect(.{to_vector(s.position) - Vector2.{4, 16}, .{8, 16}}, fade(rgb(0x5fcde4), 0.5)); }
