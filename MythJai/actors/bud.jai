// Bud, the companion who follows the player around, drawn with the humanoid rig in his own
// palette. Myth steers him with ReachPilot over a route field (GameAI/); until that's ported,
// he walks toward the player, waits at a drop into nothing, and jumps when a wall's in the way
// or the player is above him. Falling out anyway takes him out of play for a few seconds, then
// he turns up where he came in (Bud.BeginFallRespawn).

Bud :: struct @GameActor {
    #as using pawn: Pawn;
    rig: Rig_Pose;
    blocked_time: float;
    entry: Point2;          // where he came into the room
    respawn_timer: float;   // out of play while > 0
}

BUD_FOLLOW_DISTANCE :: 20;
BUD_FALL_RESPAWN_DELAY :: 3.0;

update :: (b: *Bud) {
    if b.respawn_timer > 0 {
        b.respawn_timer -= delta;
        if b.respawn_timer <= 0 { b.position = b.entry; b.velocity, b.remainder = .{}, .{}; }
        return;
    }
    input: Pawn_Input;
    for player: context.actors.players {
        dx := player.position.x - b.position.x;
        if abs(dx) > BUD_FOLLOW_DISTANCE input.move_x = xx sign(xx dx);
        else if !input.move_x && dx b.facing = sign(xx dx);
        wants_up := player.position.y < b.position.y - 12 && abs(dx) < 48;
        input.jump_pressed = b.grounded && (b.blocked_time > 0.1 || wants_up);
        input.jump_down = !b.grounded && b.velocity.y < 0;
        if b.grounded && input.move_x && !ground_below(b.position.x + cast(s32) sign(input.move_x) * (b.hitbox.w / 2 + 2), b.position.y) input.move_x = 0;
    }
    events := pawn_update(b, input, delta);
    b.blocked_time = ifx events & .HIT_WALL then b.blocked_time + delta else 0;
    if b.position.y > room.size.y * TILE_SIZE + 12 b.respawn_timer = BUD_FALL_RESPAWN_DELAY;
    play_rig(*b.rig, b.animation);
    update_rig(*b.rig, b.facing, delta);
}

draw :: (b: *Bud) { if b.respawn_timer <= 0 draw_rig(*b.rig, to_vector(b.position), BUD_LOOK); }

// Any floor under room pixel x, from y down to the bottom of the room.
ground_below :: (x: s32, y: s32) -> bool { return overlaps_solid(.{x, y, 1, room.size.y * TILE_SIZE - y}); }

// Next to the player, behind them if there's room (Game.FindBudSpawnPosition).
spawn_bud :: (player: *Player) {
    b := add(*context.actors.buds);
    b.hitbox = .{-4, -STANDING_HEIGHT, 8, STANDING_HEIGHT};
    play(b, "idle");
    b.facing = player.facing;
    defer b.entry = b.position;
    for x: s32.[-12, 12, -20, 20] {
        b.position = player.position + Point2.{x * player.facing, 0};
        if !overlaps_solid(b) return;
    }
    b.position = player.position;
}
