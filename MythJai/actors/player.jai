// The player: Myth's controls (arrows/Space) driving a Pawn, drawn with its Aseprite sprite.

Player :: struct @GameActor {
    #as using pawn: Pawn;
}

update :: (p: *Player) {
    input := Pawn_Input.{
        move_x = cast(float) (cast(s32) key_down(SDLK_RIGHT) - cast(s32) key_down(SDLK_LEFT)),
        move_y = cast(s32) key_down(SDLK_DOWN) - cast(s32) key_down(SDLK_UP),
        jump_pressed = key_pressed(SDLK_SPACE),
        jump_down = key_down(SDLK_SPACE),
    };
    if demo input = demo_input();
    events := pawn_update(p, input, delta);
    if demo && events log("%: % at % vel % anim %", demo_time, events, p.position, p.velocity, p.animation);
}

// `myth demo`: a scripted run through the room, for checking movement without a keyboard.
demo: bool;
demo_time: float;

demo_input :: () -> Pawn_Input {
    t := demo_time;
    input: Pawn_Input;
    if t > 0.3 && t < 1.6 input.move_x = 1;                     // run right
    if t > 1.0 && t < 1.05 input.jump_pressed = true;           // jump, held
    input.jump_down = t > 1.0 && t < 1.4;
    if t > 2.0 && t < 3.0 input.move_x = -1;                    // run back left
    if t > 2.4 && t < 2.45 input.jump_pressed = true;           // short hop
    input.jump_down |= t > 2.4 && t < 2.5;
    if t > 3.2 && t < 3.8 input.move_y = 1;                     // duck, then crawl
    if t > 3.5 && t < 3.8 input.move_x = 1;
    return input;
}

draw :: (p: *Player) {
    if !player_sprite.texture.gpu player_sprite = load_sprite(tprint("%/Sources/Sprites/player.ase", ASSETS));
    tag := find_tag(player_sprite, p.animation);
    draw_sprite(player_sprite, tag, p.animation_time, to_vector(p.position), .{xx p.facing * p.squish.x, p.squish.y}, p.animation_looping);
}

spawn_player :: (at: Point2) -> *Player {
    p := add(*context.actors.players);
    p.position = at;
    p.hitbox = .{-4, -STANDING_HEIGHT, 8, STANDING_HEIGHT};
    p.animation = "idle";
    p.animation_looping = true;
    return p;
}

player_sprite: Sprite;
