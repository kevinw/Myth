PlayerStart :: struct @GameActor {
    #as using actor: Actor;
}

draw :: (s: *PlayerStart) { draw_sprite(s, "player_start"); }
