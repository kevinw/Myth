// A hopping slime: waits, then jumps at the player. Two hits kill it.

Blob :: struct @GameActor {
    #as using actor: Actor;
    health: int = 2;        @I(1,10)
    jump_timer: float = 2;  @I(0.25,5)
    grounded := true;       @NoSerialize
    started: bool;          @NoSerialize
}

update :: (b: *Blob) {
    if !b.started {  // Myth's Added: stagger the first jump
        b.started = true;
        b.hitbox = .{-4, -8, 8, 8};
        b.jump_timer = max(b.jump_timer - random_get_within_range(0, 1), 0);
        play(b, "idle");
    }
    move(b, b.velocity * delta);
    advance(b, delta);
    b.velocity.y += 300 * delta;

    if grounded(b) {
        if !b.grounded {
            play(b, "idle");
            b.squish = .{1.5, 0.5};
            sound("blob/land", b.position);
        }
        b.velocity.x = approach(b.velocity.x, 0, 400 * delta);
        b.jump_timer -= delta;
        b.grounded = true;
    } else {
        b.grounded = false;
    }

    if b.jump_timer <= 0 {
        play(b, "jump");
        sound("blob/jump", b.position);
        b.jump_timer = ifx chance(0.4) then random_get_within_range(2.4, 2.9) else random_get_within_range(1.5, 2.1);
        b.velocity.y = ifx chance(0.2) then -130.0 else -90.0;
        b.squish = .{0.5, 1.5};
        for context.actors.players {
            b.facing = sign(xx (it.position.x - b.position.x));
            if !b.facing b.facing = 1;
            b.velocity.x = b.facing * 40.0;
        }
    }
}

hit :: (b: *Blob, by: *Actor) {
    b.health -= 1;
    hit_stun(0.09);
    b.squish = .{0.5, 1};
    if b.health <= 0 { free(*context.actors.blobs, b); return; }
    b.velocity.x = sign(xx (b.position.x - by.position.x)) * 120.0;
}

draw :: (b: *Blob) { draw_sprite(b, "blob"); }

chance :: (p: float) -> bool { return random_get_zero_to_one() < p; }
