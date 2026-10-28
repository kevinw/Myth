// Bud, the companion who follows the player around, drawn with the humanoid rig in his own
// palette. Myth steers him with ReachPilot over a route field (GameAI/); until that's ported,
// he walks toward the player, waits at a drop into nothing, and jumps when a wall's in the way
// or the player is above him. Falling out anyway takes him out of play for a few seconds, then
// he turns up where he came in (Bud.BeginFallRespawn). Near the player when they burrow, he
// rides along out of sight and pops out just after them, the same way (TryEnterPlayerBurrow).
// Caught by the player's swing, he rounds on them and hops back out of reach (StartBackDash).

Bud :: struct @GameActor {
    #as using pawn: Pawn;
    rig: Rig_Pose;
    blocked_time: float;
    entry: Point2;          // where he came into the room
    respawn_timer: float;   // out of play while > 0
    riding: bool;           // in the player's burrow
    pop_delay: float;       // > 0: about to pop out of it
    pop_at: Point2;
    pop_velocity: Vector2;
    pop_squish: Vector2;
    pop_facing: s32;
    back_dash_timer: float;
    back_dash_cooldown: float;
}

BUD_FOLLOW_DISTANCE :: 20;
BUD_FALL_RESPAWN_DELAY :: 3.0;
BUD_BURROW_RIDE_RADIUS :: 14;
BUD_BURROW_POP_DELAY :: 0.25;
BUD_BACK_DASH_SPEED :: 120.0;
BUD_BACK_DASH_DURATION :: 0.22;
BUD_BACK_DASH_FRICTION :: 260.0;
BUD_BACK_DASH_COOLDOWN :: 0.6;

update :: (b: *Bud) {
    if b.respawn_timer > 0 {
        b.respawn_timer -= delta;
        if b.respawn_timer <= 0 { b.position = b.entry; b.velocity, b.remainder = .{}, .{}; }
        return;
    }
    if b.riding { for player: context.actors.players b.position = player.position; return; }
    if b.pop_delay > 0 {
        b.pop_delay -= delta;
        if b.pop_delay > 0 return;
        b.position, b.velocity, b.squish, b.facing = b.pop_at, b.pop_velocity, b.pop_squish, b.pop_facing;
        spawn_pop(b.position + Point2.{0, -6});
        b.remainder = .{};
        b.jump_timer, b.jump_cut_immune = JUMP_TIME * 0.9, true;
    }
    b.back_dash_cooldown = approach(b.back_dash_cooldown, 0, delta);
    input: Pawn_Input;
    if b.back_dash_timer > 0 {
        b.back_dash_timer = approach(b.back_dash_timer, 0, delta);
        b.velocity.x = approach(b.velocity.x, 0, BUD_BACK_DASH_FRICTION * delta);
        pawn_update(b, input, delta, normal = false);
        if b.back_dash_timer <= 0 play(b, "idle");
        play_rig(*b.rig, b.animation, b.velocity, delta);
        update_rig(*b.rig, b, delta);
        return;
    }
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
    play_rig(*b.rig, b.animation, b.velocity, delta);
    update_rig(*b.rig, b, delta);
}

draw :: (b: *Bud) { if b.respawn_timer <= 0 && !b.riding && b.pop_delay <= 0 draw_rig(*b.rig, to_vector(b.position), BUD_LOOK); }

// Bud.HitByHitbox: a swing that catches him turns him toward whoever swung and hops him back.
bud_struck :: (b: *Bud, swing: RectInt, attacker_x: s32) {
    if b.respawn_timer > 0 || b.riding || b.pop_delay > 0 || b.back_dash_timer > 0 || b.back_dash_cooldown > 0 return;
    if !overlaps(swing, hitbox_rect(b)) return;
    toward := sign(cast(float) (attacker_x - b.position.x));
    if toward b.facing = toward;
    b.back_dash_timer, b.back_dash_cooldown = BUD_BACK_DASH_DURATION, BUD_BACK_DASH_COOLDOWN;
    b.velocity.x = -b.facing * BUD_BACK_DASH_SPEED;
    b.jump_timer = 0;
    b.squish = .{0.75, 1.3};
    play(b, "back_dash", looping = false);
    sound("hurt", b.position);
}

// Bud.TryEnterPlayerBurrow: close enough and on his feet, he goes under with the player.
bud_rides_burrow :: (b: *Bud, player: *Pawn) -> bool {
    if b.respawn_timer > 0 || b.riding || b.pop_delay > 0 || length(to_vector(b.position - player.position)) > BUD_BURROW_RIDE_RADIUS return false;
    b.riding = true;
    b.velocity = .{};
    spawn_pop(b.position + Point2.{0, -6});
    return true;
}

// Bud.BeginPlayerBurrowPop: out where the player came out, launched the same way, a moment later.
bud_pop_from_burrow :: (b: *Bud, player: *Pawn) {
    if !b.riding return;
    b.riding = false;
    b.pop_delay = BUD_BURROW_POP_DELAY;
    b.pop_at, b.pop_velocity, b.pop_squish, b.pop_facing = player.position, player.velocity, player.squish, player.facing;
}

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
