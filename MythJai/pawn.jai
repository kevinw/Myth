// Pawn: Myth's walking, jumping, wall-sliding body, as a library its actors call instead of a
// base class that calls back into them. pawn_update runs one frame of the Normal state and
// reports what happened as Pawn_Events, for the actor to react to (sounds, particles, ...).
// Not yet ported: dash, burrow, climb, carrying, liquids, air hop, turn jump.

Pawn :: struct {
    #as using actor: Actor;
    grounded: bool;
    ducking: bool;
    jump_timer: float;              // > 0 while a held jump keeps its lift
    jump_buffer_timer: float;       // a jump pressed just before landing still counts
    coyote_timer: float;            // ...and one pressed just after walking off a ledge
    coyote_y: s32;
    wall_jump_direction: s32;       // away from the wall last jumped off
    wall_jump_lock_timer: float;    // steering is locked away from that wall for a moment
    wall_coyote_timer: float;
    wall_coyote_direction: s32;
    run_start_timer: float;
    locomotion_timer: float;        // holds the run animation through brief stops
    rolling: bool;
    roll_time: float;
    airborne_roll_distance: float;
}

Pawn_Input :: struct {
    move_x: float;                  // -1..1
    move_y: s32;                    // -1 up, 1 down
    jump_pressed: bool;
    jump_down: bool;
}

Pawn_Events :: enum_flags u8 { LANDED; JUMPED; WALL_JUMPED; SKIDDED; HIT_WALL; ROLLED; }

MAX_GROUND_SPEED :: 93.0;
MAX_AIR_SPEED :: 100.0;
CRAWL_SPEED :: 28.0;
GROUND_ACCEL :: 400.0;
AIR_ACCEL :: 310.0;
FRICTION :: 480.0;
GRAVITY :: 450.0;
TERMINAL_VELOCITY :: 500.0;
FAST_FALL_MULTIPLIER :: 1.25;
JUMP_FORCE :: -139.0;
JUMP_TIME :: 0.22;
JUMP_HOLD_GRAVITY_MULTIPLIER :: 0.35;
JUMP_CUT_MULTIPLIER :: 0.5;
JUMP_BUFFER_TIME :: 0.08;
COYOTE_JUMP_TIME :: 0.08;
WALL_JUMP_SPEED :: 125.0;
WALL_JUMP_INPUT_LOCK_TIME :: 0.22;
WALL_JUMP_COYOTE_TIME :: 0.08;
WALL_HUG_GRAVITY_MULTIPLIER :: 0.25;
WALL_SLIDE_MAX_FALL_SPEED :: 80.0;
WALL_SLIDE_FALL_DAMPING :: 320.0;
MOVE_INPUT_DEADZONE :: 0.1;
GROUND_LOCOMOTION_HOLD :: 0.1;
GROUND_LOCOMOTION_SPEED :: 2.0;
STANDING_HEIGHT :: 12;
ROLL_MIN_HORIZONTAL_SPEED :: 45.0;
ROLL_MIN_FALL_SPEED :: 20.0;
ROLL_SPEED :: 145.0;
ROLL_DURATION :: 0.36;
ROLL_FRICTION :: 220.0;
ROLL_JUMP_FORCE :: -82.0;
ROLL_JUMP_TIME :: 0.11;
ROLL_JUMP_SPEED :: 150.0;
AIRBORNE_ROLL_DISTANCE :: 16.0;  // two tiles

// One frame of the body: timers, then (with `normal`) the Normal state's steering, ducking
// and jumping, then gravity and movement. Other states pass normal = false and steer themselves.
pawn_update :: (p: *Pawn, input: Pawn_Input, dt: float, normal := true) -> Pawn_Events {
    previous_velocity := p.velocity;
    events := refresh_grounded(p);
    advance(p, dt);
    if input.jump_pressed p.jump_buffer_timer = JUMP_BUFFER_TIME;
    if !p.grounded {
        p.coyote_timer = approach(p.coyote_timer, 0, dt);
        p.wall_jump_lock_timer = approach(p.wall_jump_lock_timer, 0, dt);
        p.wall_coyote_timer = approach(p.wall_coyote_timer, 0, dt);
    }
    p.jump_buffer_timer = approach(p.jump_buffer_timer, 0, dt);

    wall := wall_jump_direction(p, input.move_x);
    if normal && p.rolling events |= roll_state(p, input, dt);
    else if normal events |= normal_state(p, input, wall, dt);
    p.hitbox = ifx p.ducking then RectInt.{-4, -STANDING_HEIGHT / 2, 8, STANDING_HEIGHT / 2} else RectInt.{-4, -STANDING_HEIGHT, 8, STANDING_HEIGHT};

    if p.jump_timer > 0 {
        if !input.jump_down {  // released early: cut the jump short
            p.jump_timer = 0;
            p.velocity.y *= JUMP_CUT_MULTIPLIER;
        } else {
            p.jump_timer -= dt;
        }
    }

    if !p.grounded {
        gravity := GRAVITY;
        if p.jump_timer > 0 gravity *= JUMP_HOLD_GRAVITY_MULTIPLIER;
        else if p.velocity.y > 0 gravity *= FAST_FALL_MULTIPLIER;
        hugging := p.velocity.y > 0 && wall_hugging(wall, input.move_x);
        if hugging gravity *= WALL_HUG_GRAVITY_MULTIPLIER;
        p.velocity.y = min(p.velocity.y + gravity * dt, TERMINAL_VELOCITY);
        if hugging && p.velocity.y > WALL_SLIDE_MAX_FALL_SPEED
            p.velocity.y = approach(p.velocity.y, WALL_SLIDE_MAX_FALL_SPEED, WALL_SLIDE_FALL_DAMPING * dt);
    }

    if move(p, p.velocity * dt) & .HIT_X {
        events |= .HIT_WALL;
        p.rolling = false;  // a wall stops a roll (Pawn.OnCollideX)
    }
    landing := refresh_grounded(p);
    if landing & .LANDED && normal && !p.rolling && try_start_roll(p, input, previous_velocity) landing |= .ROLLED;
    return events | landing;
}

move_input :: (input: Pawn_Input) -> s32 { return ifx abs(input.move_x) < MOVE_INPUT_DEADZONE then 0 else sign(input.move_x); }

horizontal_movement :: (p: *Pawn, move_x: s32, movement: float, dt: float) {
    max_speed := ifx p.ducking then CRAWL_SPEED else ifx p.grounded then MAX_GROUND_SPEED else MAX_AIR_SPEED;
    if p.grounded && movement != 0 max_speed *= abs(movement);
    p.velocity.x += movement * (ifx p.grounded then GROUND_ACCEL else AIR_ACCEL) * dt;
    if abs(p.velocity.x) > max_speed p.velocity.x = approach(p.velocity.x, sign(p.velocity.x) * max_speed, 2000 * dt);
    if move_x == 0 && p.grounded p.velocity.x = approach(p.velocity.x, 0, FRICTION * dt);
}

#scope_file

normal_state :: (p: *Pawn, input: Pawn_Input, wall: s32, dt: float) -> Pawn_Events {
    events: Pawn_Events;
    if p.grounded && input.move_y > 0 p.ducking = true;
    else if p.ducking p.ducking = !can_stand_up(p);

    move_x := move_input(input);
    movement := ifx move_x then input.move_x else 0;
    if !p.grounded && p.wall_jump_lock_timer > 0 movement = xx p.wall_jump_direction;

    if p.grounded && (move_x || abs(p.velocity.x) > GROUND_LOCOMOTION_SPEED) p.locomotion_timer = GROUND_LOCOMOTION_HOLD;
    else if p.grounded p.locomotion_timer = 0;
    else p.locomotion_timer = approach(p.locomotion_timer, 0, dt);
    walking := p.locomotion_timer > 0;

    facing := move_x;
    if p.grounded {
        if p.ducking play(p, ifx walking then "crawl" else "duck");
        else if !walking play(p, ifx input.move_y < 0 then "look_up" else "idle");
        else if p.velocity.x > 0 && move_x < 0 || p.velocity.x < 0 && move_x > 0 {
            facing = -facing;
            play(p, "skid", looping = false);
            events |= .SKIDDED;
            p.run_start_timer = 0;
        } else {
            p.run_start_timer += dt;
            play(p, ifx p.run_start_timer < 0.35 then "run_start" else ifx abs(movement) < 1 then "walk" else "run");
        }
    } else {
        p.run_start_timer = 0;
        if wall_hugging(wall, input.move_x) { p.facing = -wall; play(p, "wall_slide"); }
        else if p.velocity.x != 0 && move_x && sign(p.velocity.x) != move_x play(p, "jump_turn");
        else if p.velocity.y > 0 && input.move_y == 1 play(p, "jump_down");
        else play(p, "jump");
    }

    horizontal_movement(p, move_x, movement, dt);
    if p.grounded && move_x p.facing = facing;

    can_coyote := !p.grounded && p.coyote_timer > 0 && can_stand_up(p, .{0, p.coyote_y - p.position.y});
    can_ground := p.grounded && can_stand_up(p) || can_coyote;
    can_wall := wall != 0 && move_x != 0;
    jump := input.jump_pressed || can_ground && p.jump_buffer_timer > 0;
    if jump && (can_ground || can_wall) {
        p.jump_buffer_timer = 0;
        p.squish = .{0.65, 1.4};
        if can_ground {
            if can_coyote p.position.y = p.coyote_y;
            p.coyote_timer = 0;
            events |= .JUMPED;
        } else {
            p.wall_jump_direction = wall;
            p.wall_jump_lock_timer = WALL_JUMP_INPUT_LOCK_TIME;
            p.facing = wall;
            p.velocity.x = wall * WALL_JUMP_SPEED;
            p.wall_coyote_timer = 0;
            events |= .WALL_JUMPED;
        }
        p.velocity.y = JUMP_FORCE;
        p.jump_timer = JUMP_TIME;
    }
    return events;
}

// Landing with some fall and some run while holding down tucks into a roll (Pawn.TryStartRoll, flat ground).
try_start_roll :: (p: *Pawn, input: Pawn_Input, impact: Vector2) -> bool {
    if input.move_y <= 0 || impact.y < ROLL_MIN_FALL_SPEED || abs(impact.x) < ROLL_MIN_HORIZONTAL_SPEED return false;
    if move_input(input) && move_input(input) != sign(impact.x) return false;  // braking, not tucking
    p.facing = sign(impact.x);
    p.velocity = .{p.facing * ROLL_SPEED, 0};
    start_roll(p);
    return true;
}

#scope_export

start_roll :: (p: *Pawn) {
    p.ducking = true;
    p.squish = .{1.65, 0.62};
    play(p, "roll");
    p.rolling = true;
    p.roll_time = 0;
    p.airborne_roll_distance = 0;
}

#scope_file

// Pawn.RollState on flat ground: tucked, easing to running speed, until it times out or down is let go.
roll_state :: (p: *Pawn, input: Pawn_Input, dt: float) -> Pawn_Events {
    p.ducking = true;
    play(p, "roll");
    p.roll_time += dt;
    airborne := false;
    if !p.grounded {
        p.airborne_roll_distance += abs(p.velocity.x) * dt;
        if p.airborne_roll_distance >= AIRBORNE_ROLL_DISTANCE { p.rolling = false; return 0; }
        airborne = true;
    } else {
        p.airborne_roll_distance = 0;
        p.velocity.x = approach(p.velocity.x, p.facing * MAX_GROUND_SPEED, ROLL_FRICTION * dt);
    }
    if input.jump_pressed && can_stand_up(p) {
        p.squish = .{0.75, 1.25};
        p.velocity.x = p.facing * max(abs(p.velocity.x), ROLL_JUMP_SPEED);
        p.velocity.y = ROLL_JUMP_FORCE;
        p.jump_timer = ROLL_JUMP_TIME;
        p.rolling = false;
        events: Pawn_Events;
        events |= .JUMPED;
        return events;
    }
    if !airborne && (p.roll_time >= ROLL_DURATION || input.move_y <= 0) {
        p.rolling = false;
        p.ducking = p.grounded && (input.move_y > 0 || !can_stand_up(p));
    }
    return 0;
}

refresh_grounded :: (p: *Pawn) -> Pawn_Events {
    now := p.velocity.y >= 0 && grounded(p);
    landed := now && !p.grounded;
    p.grounded = now;
    if now {
        p.coyote_timer = COYOTE_JUMP_TIME;
        p.coyote_y = p.position.y;
        p.wall_jump_lock_timer = 0;
        p.wall_coyote_timer = 0;
    }
    events: Pawn_Events;
    if landed events |= .LANDED;
    return events;
}

// The wall to jump off, as the direction to jump (1 = away from a wall on the left), held
// briefly after letting go of it.
wall_jump_direction :: (p: *Pawn, move_x: float) -> s32 {
    dir: s32;
    if !p.grounded {
        left, right := overlaps_solid(p, .{-1, 0}), overlaps_solid(p, .{1, 0});
        if left && right dir = ifx abs(move_x) < MOVE_INPUT_DEADZONE then 0 else -sign(move_x);  // a one-tile shaft
        else dir = cast(s32) left - cast(s32) right;
    }
    if dir {
        p.wall_coyote_direction = dir;
        p.wall_coyote_timer = WALL_JUMP_COYOTE_TIME;
    } else if p.wall_coyote_timer > 0 {
        dir = p.wall_coyote_direction;
    }
    return dir;
}

wall_hugging :: (wall: s32, move_x: float) -> bool {
    return wall != 0 && abs(move_x) >= MOVE_INPUT_DEADZONE && sign(move_x) == -wall;
}

can_stand_up :: (p: *Pawn, offset := Point2.{}) -> bool {
    return !overlaps_solid(.{p.position.x + offset.x - 4, p.position.y + offset.y - STANDING_HEIGHT, 8, STANDING_HEIGHT});
}
