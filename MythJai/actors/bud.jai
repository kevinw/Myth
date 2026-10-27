// Bud, the companion who follows the player around, drawn with the humanoid rig in his own
// palette. Myth steers him with ReachPilot over a route field (GameAI/); until that's ported,
// he walks toward the player and jumps when a wall's in the way or the player is above him.

Bud :: struct @GameActor {
    #as using pawn: Pawn;
    rig: Rig_Pose;
    blocked_time: float;
}

BUD_FOLLOW_DISTANCE :: 20;

update :: (b: *Bud) {
    input: Pawn_Input;
    for player: context.actors.players {
        dx := player.position.x - b.position.x;
        if abs(dx) > BUD_FOLLOW_DISTANCE input.move_x = xx sign(xx dx);
        else if !input.move_x && dx b.facing = sign(xx dx);
        wants_up := player.position.y < b.position.y - 12 && abs(dx) < 48;
        input.jump_pressed = b.grounded && (b.blocked_time > 0.1 || wants_up);
        input.jump_down = !b.grounded && b.velocity.y < 0;
    }
    events := pawn_update(b, input, delta);
    b.blocked_time = ifx events & .HIT_WALL then b.blocked_time + delta else 0;
    if fell_out(b) for player: context.actors.players { b.position = player.position; b.velocity = .{}; }
    play_rig(*b.rig, b.animation);
    update_rig(*b.rig, b.facing, delta);
}

draw :: (b: *Bud) { draw_rig(*b.rig, to_vector(b.position), BUD_LOOK); }

// Next to the player, behind them if there's room (Game.FindBudSpawnPosition).
spawn_bud :: (player: *Player) {
    b := add(*context.actors.buds);
    b.hitbox = .{-4, -STANDING_HEIGHT, 8, STANDING_HEIGHT};
    play(b, "idle");
    b.facing = player.facing;
    for x: s32.[-12, 12, -20, 20] {
        b.position = player.position + Point2.{x * player.facing, 0};
        if !overlaps_solid(b) return;
    }
    b.position = player.position;
}
