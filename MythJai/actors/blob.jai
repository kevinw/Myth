Blob :: struct @GameActor {
    #as using actor: Actor;
    health: int = 2;        @I(1,10)
    jump_timer: float = 2;  @I(0.25,5)
}

draw :: (b: *Blob) { rect(.{to_vector(b.position) - Vector2.{6, 10}, .{12, 10}}, rgb(0x7bd88f)); }
