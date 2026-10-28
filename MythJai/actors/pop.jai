// A one-shot sprite, a puff or a sword's swipe, that plays its "pop" animation once and is gone (Pop.cs).

Pop :: struct @GameActor {
    #as using actor: Actor;
    sprite_name: string;
    flip_y: bool;
}

update :: (p: *Pop) {
    p.animation_time += delta;
    sprite := get_sprite(p.sprite_name);
    if !sprite || p.animation_time >= tag_duration(sprite, find_tag(sprite, "pop")) free(*context.actors.pops, p);
}

draw :: (p: *Pop) {
    sprite := get_sprite(p.sprite_name);
    if sprite draw_sprite(sprite, find_tag(sprite, "pop"), p.animation_time, to_vector(p.position), .{xx p.facing, ifx p.flip_y then -1.0 else 1.0}, looping = false);
}

spawn_pop :: (at: Point2, sprite_name := "pop", facing: s32 = 1, flip_y := false) -> Handle(Pop) {
    p, handle := add(*context.actors.pops);
    p.position, p.sprite_name, p.facing, p.flip_y = at, sprite_name, facing, flip_y;
    return handle;
}
