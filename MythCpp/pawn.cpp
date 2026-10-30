// Pawn: Myth's walking, jumping, wall-sliding body, as a library its actors call instead of a base
// class that calls back into them. pawn_update runs one frame of the Normal state and reports what
// happened as PawnEvents, for the actor to react to (sounds, particles, ...). The dash runs:
// pressing it arms a sprint held for as long as the button is (Player's DashEngagesRunOnly).
// Not yet ported: climb, carrying, liquids, air hop, turn jump.
//
// Burrowing (Pawn.Burrow, Game.SurfaceCollision): land with jump held (or, sliding down a wall with
// it held, double-tap up or down: Pawn.WallDive) and you sink into the ground and travel as a point
// along the boundary between solid and air, round corners and up walls, for as long as jump stays
// held and the burrow lasts. Movement latches when you press along the surface and holds through
// corners; pressing back brakes, then reverses. The clock runs out in three intervals, each
// flashing the rainbow glow line that traces the surface around you and shortening it. Letting go
// (or running out) pops you out with a leap stronger than a jump, carrying the glide; climbing a
// wall pops you into a wall slide. Bud, close by, rides along as a white dot and pops out after
// you. The HUD's bar drains with it and refills on landing. Sparks fly out of the surface as you
// go, and burst as you leave; the ground churns behind you (effects.cpp's pixel sort).
// Not ported: burrow lines, doors and switches, seams, room crossings, quantum tunnels, the rig
// sinking in.
#include "game.h"

constexpr float MAX_AIR_SPEED = 100;
constexpr float GROUND_ACCEL = 400;
constexpr float AIR_ACCEL = 310;
constexpr float FRICTION = 480;
constexpr float GRAVITY = 450;
constexpr float TERMINAL_VELOCITY = 500;
constexpr float FAST_FALL_MULTIPLIER = 1.25f;
constexpr float JUMP_FORCE = -139;
constexpr float JUMP_HOLD_GRAVITY_MULTIPLIER = 0.35f;
constexpr float JUMP_CUT_MULTIPLIER = 0.5f;
constexpr float JUMP_BUFFER_TIME = 0.08f;
constexpr float COYOTE_JUMP_TIME = 0.08f;
constexpr float WALL_JUMP_SPEED = 125;
constexpr float WALL_JUMP_INPUT_LOCK_TIME = 0.22f;
constexpr float WALL_JUMP_COYOTE_TIME = 0.08f;
constexpr float WALL_HUG_GRAVITY_MULTIPLIER = 0.25f;
constexpr float WALL_SLIDE_MAX_FALL_SPEED = 80;
constexpr float WALL_SLIDE_FALL_DAMPING = 320;
constexpr float MOVE_INPUT_DEADZONE = 0.1f;
constexpr float GROUND_LOCOMOTION_HOLD = 0.1f;
constexpr float GROUND_LOCOMOTION_SPEED = 2;
constexpr float ROLL_MIN_HORIZONTAL_SPEED = 45;
constexpr float ROLL_MIN_FALL_SPEED = 20;
constexpr float ROLL_SPEED = 145;
constexpr float ROLL_DURATION = 0.36f;
constexpr float ROLL_FRICTION = 220;
constexpr float ROLL_JUMP_FORCE = -82;
constexpr float ROLL_JUMP_TIME = 0.11f;
constexpr float ROLL_JUMP_SPEED = 150;
constexpr float AIRBORNE_ROLL_DISTANCE = 16;  // two tiles
constexpr float AIR_DRAG = 150;               // how fast launched speed over MAX_AIR_SPEED bleeds away
constexpr float DASH_RUN_GROUND_SPEED = 145;
constexpr float DASH_RUN_AIR_SPEED = 155;
constexpr float DASH_RUN_ACCEL_MULTIPLIER = 1.5f;
constexpr float DASH_RUN_REVERSE_ACCEL_MULTIPLIER = 0.85f;
constexpr float DASH_COOLDOWN = 0.154f;
constexpr float DASH_CANCEL_FRICTION = 900;
constexpr RectInt DUCKING_HITBOX{-4, -STANDING_HEIGHT / 2, 8, STANDING_HEIGHT / 2};

s32 move_input(const PawnInput& input) { return fabsf(input.move_x) < MOVE_INPUT_DEADZONE ? 0 : sign(input.move_x); }
bool dash_running(const Pawn& p) { return p.dash_run_armed && !p.ducking; }

static bool can_stand_up(const Pawn& p, Point2 offset = {}) {
    return !overlaps_solid(STANDING_HITBOX + p.position + offset);
}

static u8 refresh_grounded(Pawn& p) {
    bool now = p.velocity.y >= 0 && grounded(p);
    bool landed = now && !p.grounded;
    p.grounded = now;
    if (now) {
        p.coyote_timer = COYOTE_JUMP_TIME;
        p.coyote_y = p.position.y;
        p.wall_jump_lock_timer = 0;
        p.wall_coyote_timer = 0;
    }
    return landed ? LANDED : 0;
}

// The wall to jump off, as the direction to jump (1 = away from a wall on the left), held briefly
// after letting go of it.
static s32 wall_jump_direction(Pawn& p, float move_x) {
    s32 dir = 0;
    if (!p.grounded) {
        bool left = overlaps_solid(p, {-1, 0}), right = overlaps_solid(p, {1, 0});
        if (left && right) dir = fabsf(move_x) < MOVE_INPUT_DEADZONE ? 0 : -sign(move_x);  // a one-tile shaft
        else dir = (s32)left - (s32)right;
    }
    if (dir) {
        p.wall_coyote_direction = dir;
        p.wall_coyote_timer = WALL_JUMP_COYOTE_TIME;
    } else if (p.wall_coyote_timer > 0) {
        dir = p.wall_coyote_direction;
    }
    return dir;
}

static bool wall_hugging(s32 wall, float move_x) {
    return wall != 0 && fabsf(move_x) >= MOVE_INPUT_DEADZONE && sign(move_x) == -wall;
}

void horizontal_movement(Pawn& p, s32 move_x, float movement, float dt) {
    bool running = dash_running(p);
    float max_speed = p.grounded ? (running ? DASH_RUN_GROUND_SPEED : MAX_GROUND_SPEED) : (running ? DASH_RUN_AIR_SPEED : MAX_AIR_SPEED);
    if (p.ducking) max_speed = CRAWL_SPEED;
    if (p.grounded && movement != 0) max_speed *= fabsf(movement);
    float accel = p.grounded ? GROUND_ACCEL : AIR_ACCEL;
    if (running) accel *= DASH_RUN_ACCEL_MULTIPLIER;
    if (running && p.wall_jump_lock_timer <= 0 && movement != 0 && p.velocity.x != 0 && sign(movement) != sign(p.velocity.x)) accel *= DASH_RUN_REVERSE_ACCEL_MULTIPLIER;
    p.velocity.x += movement * accel * dt;
    if (p.dash_cancel_braking) max_speed = max(max_speed, fabsf(p.velocity.x));
    if (!p.grounded) max_speed = max(max_speed, p.carry_speed);
    if (fabsf(p.velocity.x) > max_speed) p.velocity.x = approach(p.velocity.x, sign(p.velocity.x) * max_speed, 2000 * dt);
    if (p.dash_cancel_braking) {
        float target = p.ducking ? movement * CRAWL_SPEED : 0;
        p.velocity.x = approach(p.velocity.x, target, DASH_CANCEL_FRICTION * dt);
        p.dash_cancel_braking = p.velocity.x != target;
    } else if (move_x == 0 && p.grounded) p.velocity.x = approach(p.velocity.x, 0, FRICTION * dt);
}

// Player.TryStartDash: arms the sprint, once per landing.
static bool try_start_dash(Pawn& p, const PawnInput& input, s32 move_x) {
    if (p.dash_cooldown > 0 || p.dash_needs_grounding || (p.ducking && !can_stand_up(p))) return false;
    p.facing = move_x ? move_x : p.facing ? p.facing : 1;
    p.dash_cooldown = DASH_COOLDOWN;
    p.dash_needs_grounding = true;
    p.dash_run_armed = input.dash_down;
    p.ducking = false;
    p.squish = {1.35f, 0.82f};
    return true;
}

static u8 normal_state(Pawn& p, const PawnInput& input, s32 wall, float dt) {
    u8 events = 0;
    if (p.grounded && input.move_y > 0) p.ducking = true;
    else if (p.ducking) p.ducking = !can_stand_up(p);
    if (p.ducking && p.dash_run_armed) { p.dash_cancel_braking = true; p.dash_cooldown = 0; p.dash_run_armed = false; }

    s32 move_x = move_input(input);
    float movement = move_x ? input.move_x : 0;
    if (!p.grounded && p.wall_jump_lock_timer > 0) movement = (float)p.wall_jump_direction;

    if (p.grounded && (move_x || fabsf(p.velocity.x) > GROUND_LOCOMOTION_SPEED)) p.locomotion_timer = GROUND_LOCOMOTION_HOLD;
    else if (p.grounded) p.locomotion_timer = 0;
    else p.locomotion_timer = approach(p.locomotion_timer, 0, dt);
    bool walking = p.locomotion_timer > 0;

    s32 facing = move_x;
    if (p.grounded) {
        if (p.ducking) play(p, walking ? "crawl" : "duck");
        else if (!walking) play(p, input.move_y < 0 ? "look_up" : "idle");
        else if ((p.velocity.x > 0 && move_x < 0) || (p.velocity.x < 0 && move_x > 0)) {
            facing = -facing;
            play(p, "skid", false);
            events |= SKIDDED;
            p.run_start_timer = 0;
        } else {
            p.run_start_timer += dt;
            play(p, p.run_start_timer < 0.35f ? "run_start" : fabsf(movement) < 1 ? "walk" : "run");
        }
    } else {
        p.run_start_timer = 0;
        if (wall_hugging(wall, input.move_x)) { p.facing = -wall; play(p, "wall_slide"); }
        else if (p.velocity.x != 0 && move_x && sign(p.velocity.x) != move_x) play(p, "jump_turn");
        else if (p.velocity.y > 0 && input.move_y == 1) play(p, "jump_down");
        else play(p, "jump");
    }

    horizontal_movement(p, move_x, movement, dt);
    if (p.grounded && move_x) p.facing = facing;

    bool can_coyote = !p.grounded && p.coyote_timer > 0 && can_stand_up(p, {0, p.coyote_y - p.position.y});
    bool can_ground = (p.grounded && can_stand_up(p)) || can_coyote;
    bool can_wall = wall != 0 && move_x != 0;
    bool jump = input.jump_pressed || (can_ground && p.jump_buffer_timer > 0);
    if (jump && (can_ground || can_wall)) {
        p.jump_buffer_timer = 0;
        p.squish = {0.65f, 1.4f};
        if (can_ground) {
            if (can_coyote) p.position.y = p.coyote_y;
            p.coyote_timer = 0;
            events |= JUMPED;
        } else {
            p.wall_jump_direction = wall;
            p.wall_jump_lock_timer = WALL_JUMP_INPUT_LOCK_TIME;
            p.facing = wall;
            p.velocity.x = wall * WALL_JUMP_SPEED;
            p.wall_coyote_timer = 0;
            events |= WALL_JUMPED;
        }
        p.velocity.y = JUMP_FORCE;
        p.jump_timer = JUMP_TIME;
    }
    if (input.dash_pressed) try_start_dash(p, input, move_x);
    return events;
}

// Landing with some fall and some run while holding down tucks into a roll (Pawn.TryStartRoll, flat ground).
static bool try_start_roll(Pawn& p, const PawnInput& input, Vector2 impact) {
    if (input.move_y <= 0 || impact.y < ROLL_MIN_FALL_SPEED || fabsf(impact.x) < ROLL_MIN_HORIZONTAL_SPEED) return false;
    if (move_input(input) && move_input(input) != sign(impact.x)) return false;  // braking, not tucking
    p.facing = sign(impact.x);
    p.velocity = {p.facing * ROLL_SPEED, 0};
    start_roll(p);
    return true;
}

void start_roll(Pawn& p) {
    p.ducking = true;
    p.squish = {1.65f, 0.62f};
    play(p, "roll");
    p.rolling = true;
    p.roll_time = 0;
    p.airborne_roll_distance = 0;
}

// Pawn.RollState on flat ground: tucked, easing to running speed, until it times out or down is let go.
static u8 roll_state(Pawn& p, const PawnInput& input, float dt) {
    p.ducking = true;
    play(p, "roll");
    p.roll_time += dt;
    bool airborne = false;
    if (!p.grounded) {
        p.airborne_roll_distance += fabsf(p.velocity.x) * dt;
        if (p.airborne_roll_distance >= AIRBORNE_ROLL_DISTANCE) { p.rolling = false; return 0; }
        airborne = true;
    } else {
        p.airborne_roll_distance = 0;
        p.velocity.x = approach(p.velocity.x, p.facing * MAX_GROUND_SPEED, ROLL_FRICTION * dt);
    }
    if (input.jump_pressed && can_stand_up(p)) {
        p.squish = {0.75f, 1.25f};
        p.velocity.x = p.facing * max(fabsf(p.velocity.x), ROLL_JUMP_SPEED);
        p.velocity.y = ROLL_JUMP_FORCE;
        p.jump_timer = ROLL_JUMP_TIME;
        p.rolling = false;
        return JUMPED;
    }
    if (!airborne && (p.roll_time >= ROLL_DURATION || input.move_y <= 0)) {
        p.rolling = false;
        p.ducking = p.grounded && (input.move_y > 0 || !can_stand_up(p));
    }
    return 0;
}

u8 pawn_update(Pawn& p, const PawnInput& input, float dt, bool normal) {
    Vector2 previous_velocity = p.velocity;
    u8 events = refresh_grounded(p);
    advance(p, dt);
    if (input.jump_pressed) p.jump_buffer_timer = JUMP_BUFFER_TIME;
    if (!input.dash_down) p.dash_run_armed = false;
    p.dash_cooldown = approach(p.dash_cooldown, 0, dt);
    if (!p.grounded) {
        p.coyote_timer = approach(p.coyote_timer, 0, dt);
        p.wall_jump_lock_timer = approach(p.wall_jump_lock_timer, 0, dt);
        p.wall_coyote_timer = approach(p.wall_coyote_timer, 0, dt);
    }
    p.jump_buffer_timer = approach(p.jump_buffer_timer, 0, dt);

    s32 wall = wall_jump_direction(p, input.move_x);
    if (normal && !p.rolling && p.burrows && try_wall_dive(p, input, wall, dt)) return events | BURROWED;
    if (normal && p.rolling) events |= roll_state(p, input, dt);
    else if (normal) events |= normal_state(p, input, wall, dt);
    p.hitbox = p.ducking ? DUCKING_HITBOX : STANDING_HITBOX;

    if (p.jump_timer > 0) {
        if (!input.jump_down && !p.jump_cut_immune) {  // released early: cut the jump short
            p.jump_timer = 0;
            p.velocity.y *= JUMP_CUT_MULTIPLIER;
        } else {
            p.jump_timer -= dt;
        }
    }
    if (p.jump_timer <= 0) p.jump_cut_immune = false;
    p.carry_speed = p.grounded ? 0 : min(approach(p.carry_speed, 0, AIR_DRAG * dt), fabsf(p.velocity.x));

    if (!p.grounded) {
        float gravity = GRAVITY;
        if (p.jump_timer > 0) gravity *= JUMP_HOLD_GRAVITY_MULTIPLIER;
        else if (p.velocity.y > 0) gravity *= FAST_FALL_MULTIPLIER;
        bool hugging = p.velocity.y > 0 && wall_hugging(wall, input.move_x);
        if (hugging) gravity *= WALL_HUG_GRAVITY_MULTIPLIER;
        p.velocity.y = min(p.velocity.y + gravity * dt, TERMINAL_VELOCITY);
        if (hugging && p.velocity.y > WALL_SLIDE_MAX_FALL_SPEED)
            p.velocity.y = approach(p.velocity.y, WALL_SLIDE_MAX_FALL_SPEED, WALL_SLIDE_FALL_DAMPING * dt);
    }

    if (move(p, p.velocity * dt) & HIT_X) {
        events |= HIT_WALL;
        p.rolling = false;  // a wall stops a roll (Pawn.OnCollideX)
        if (dash_running(p)) events |= DASH_HIT_WALL;
        p.dash_cooldown = 0;
        p.dash_run_armed = false;
    }
    u8 landing = refresh_grounded(p);
    if (p.grounded) p.dash_needs_grounding = false;
    // Landing with jump held dives into a burrow, ahead of anything else a landing does.
    if (landing & LANDED && normal && input.jump_down && can_burrow(p) && try_start_burrow(p, previous_velocity)) return events | BURROWED;
    if (landing & LANDED && normal && !p.rolling && try_start_roll(p, input, previous_velocity)) landing |= ROLLED;
    return events | landing;
}

// --- Burrowing ---

constexpr s32 ATTACH_PROBE = 8;
constexpr float SPARK_RATE = 160;  // sparks a second at full speed
constexpr float BURROW_ACCEL = 500;
constexpr float BURROW_FRICTION = 600;
constexpr float REVERSE_FLIP_SPEED = 8;
constexpr float CLIMB_SPEED_MULTIPLIER = 0.59f;
constexpr float TANGENT_DEADZONE = 0.3f;
constexpr float POP_FORCE = float(-139.0 * 1.18);  // Jai folds its constants in double precision
constexpr float POP_TIME = float(0.22 * 0.9);
constexpr float WALL_SLIDE_LAUNCH = float(-139.0 * 1.2);
constexpr s32 POP_SLIDE = 6;
constexpr float COOLDOWN = 0.25f;
constexpr float SHORT_BURROW_DAMPEN_TIME = 0.09f;
constexpr float EXIT_GRACE = 0.1f;
constexpr float ENTER_SQUISH_TIME = 0.12f;
constexpr float SQUASH_NORMAL = 0.45f;
constexpr float SQUASH_TANGENT = 1.6f;
constexpr float LEAP_COYOTE_TIME = 0.12f;
constexpr s32 INTERVALS = 3;
constexpr float PULSE_TIME = 0.22f;
constexpr float MIN_SPREAD = 0.4f;
constexpr float SPREAD_EASE_RATE = 1.4f;
constexpr float GLOW_ARC = 15;
constexpr float GLOW_EXIT_TIME = 0.13f;
constexpr float GLOW_WEIGHT = 1.5f;
constexpr float GLOW_CYCLE_SPEED = 0.6f;
constexpr float GLOW_HUE_SCALE = 0.03f;
constexpr float BUD_DOT_RADIUS = 2.25f;
constexpr float LIGHT_INTENSITY = 0.45f;
constexpr float BURROW_LIGHT_INTENSITY = 0.85f;
constexpr Color LIGHT_COLOR = Color::rgb(0xffcfa0);
constexpr Color BURROW_LIGHT_COLOR = Color::rgb(0xb98cff);
constexpr float LIGHT_BLEND_RATE = 6;
constexpr RectInt BURROW_HITBOX{-1, -1, 2, 2};

// --- Walking the boundary between solid and air (Game.SurfaceCollision), in room pixels. ---

// C#'s MathF.Round: halves go to the even neighbour, which decides which pixel a face's centre snaps to.
static float round_even(float x) {
    float r = floorf(x + 0.5f);
    if (r - x == 0.5f && fmod_cycling(r, 2) != 0) r -= 1;
    return r;
}

static bool solid_pixel(Point2 cell) { return overlaps_solid(RectInt{cell.x, cell.y, 1, 1}); }
static bool in_room(Point2 cell) { return cell.x >= 0 && cell.y >= 0 && cell.x < room.size.x * TILE_SIZE && cell.y < room.size.y * TILE_SIZE; }
static Point2 cell_at(Vector2 v) { return {(s32)floorf(v.x), (s32)floorf(v.y)}; }

static bool boundary_edge_valid(Point2 v, Point2 d, bool solid_left) {
    Vector2 middle = v.vector() + d.vector() * 0.5f;
    Point2 left = cell_at(middle + Vector2{(float)d.y, (float)-d.x} * 0.5f);
    Point2 right = cell_at(middle + Vector2{(float)-d.y, (float)d.x} * 0.5f);
    Point2 solid = solid_left ? left : right, air = solid_left ? right : left;
    return !solid_pixel(air) && in_room(air) && solid_pixel(solid);
}

// The way on from vertex `v`: turn toward the solid, straight on, or away from it, whichever edge has
// solid on its solid side and open room on its air side.
static Point2 pick_boundary_dir(Point2 v, Point2 heading, bool solid_left) {
    Point2 left{heading.y, -heading.x}, right{-heading.y, heading.x};
    Point2 candidates[3] = {solid_left ? left : right, heading, solid_left ? right : left};
    for (Point2 c : candidates) if (boundary_edge_valid(v, c, solid_left)) return c;
    return {};
}

static s32 walk_surface(Point2& position, Point2& heading, bool solid_left, s32 pixels) {
    s32 moved = 0;
    for (; moved < pixels; moved++) {
        Point2 next = pick_boundary_dir(position, heading, solid_left);
        if (next == Point2{}) break;
        position += next;
        heading = next;
    }
    return moved;
}

static Vector2 surface_normal(Point2 heading, bool solid_left) {
    return solid_left ? Vector2{(float)heading.y, (float)-heading.x} : Vector2{(float)-heading.y, (float)heading.x};
}

// SnapToSurface (SeedBoundaryFace): out of any solid along -toward, then on to the face along it.
static bool snap_to_surface(Point2& position, Vector2& toward, s32 reach) {
    Point2 n{fabsf(toward.x) >= fabsf(toward.y) ? sign(toward.x) : 0, fabsf(toward.y) > fabsf(toward.x) ? sign(toward.y) : 0};
    if (n == Point2{}) return false;
    Point2 c = position;
    for (s32 i = 0; i < reach && solid_pixel(c); i++) c -= n;
    if (solid_pixel(c)) return false;
    for (s32 i = 0; i < reach && !solid_pixel(c + n); i++) c += n;
    if (!solid_pixel(c + n)) return false;
    Vector2 center = c.vector() + Vector2{0.5f, 0.5f} + n.vector() * 0.5f;
    position = {(s32)round_even(center.x), (s32)round_even(center.y)};
    toward = n.vector();
    return true;
}

static s32 trace_surface(Point2 start, Point2 heading, bool solid_left, float arc, std::span<Vector2> into) {
    Point2 position = start, direction = heading;
    into[0] = position.vector();
    s32 count = 1;
    for (float distance = 0; distance < arc && count < (s32)into.size(); distance += 1) {
        Point2 next = pick_boundary_dir(position, direction, solid_left);
        if (next == Point2{}) break;
        position += next;
        direction = next;
        into[count++] = position.vector();
    }
    return count;
}

static void update_glow(Pawn& p, bool burrowing) {
    Burrow& b = p.burrow;
    b.glow_count = 0;
    float arc = burrowing ? GLOW_ARC * (1 - b.enter_timer / ENTER_SQUISH_TIME) * b.spread : GLOW_ARC * b.glow_exit_timer / GLOW_EXIT_TIME;
    if (arc <= 0) return;
    if (burrowing) b.glow_anchor = p.position;
    // Back along the surface (reversed), then forward from the anchor (TraceBurrowPathFromState).
    Vector2 back[40];
    s32 back_count = trace_surface(b.glow_anchor, -b.heading, !b.solid_left, arc, back);
    for (s32 i = back_count - 1; i >= 1; i--) b.glow[b.glow_count++] = back[i];
    b.glow_count += trace_surface(b.glow_anchor, b.heading, b.solid_left, arc, std::span<Vector2>(b.glow).subspan(b.glow_count));
}

static void tick(Pawn& p, s32 interval) {
    void* instance = start_sound("player/burrow_tick", p.position.vector());
    if (!instance) return;
    FMOD_Studio_EventInstance_SetParameterByName(instance, "Interval", (float)interval, 0);
    finish_sound(instance);
}

// RoomToPop: the nearest place the body fits, out along the normal and then a little along the surface.
static bool room_to_pop(Pawn& p, bool* crouch, Point2* at) {
    Vector2 outward = -p.burrow.toward;
    Vector2 along{outward.y, -outward.x};
    for (bool duck : {false, true}) for (s32 distance = 0; distance <= STANDING_HEIGHT; distance++)
    for (s32 slide = 0; slide <= POP_SLIDE; slide++) for (s32 side : {1, -1}) {
        if (slide == 0 && side < 0) continue;
        Vector2 spot = p.position.vector() + outward * (float)distance + along * (float)(slide * side);
        Point2 candidate{(s32)round_even(spot.x), (s32)round_even(spot.y)};
        if (!overlaps_solid((duck ? DUCKING_HITBOX : STANDING_HITBOX) + candidate)) { *crouch = duck; *at = candidate; return true; }
    }
    return false;
}

// Popping out needs room for a crouch, unless there's nowhere to walk at all.
static bool can_pop_here(Pawn& p) {
    bool crouch;
    Point2 at;
    if (room_to_pop(p, &crouch, &at)) return true;
    Burrow& b = p.burrow;
    return pick_boundary_dir(p.position, b.heading, b.solid_left) == Point2{} && pick_boundary_dir(p.position, -b.heading, !b.solid_left) == Point2{};
}

// Pawn.PopOutOfBurrow: out where the body fits, launched off the surface with the glide.
static void pop_out(Pawn& p, const PawnInput& input) {
    Burrow& b = p.burrow;
    Vector2 exit = b.toward;
    bool leap = b.leap_coyote > 0 && exit.y <= 0.5f;
    if (leap) exit = b.leap_exit;
    room_to_pop(p, &p.ducking, &p.position);
    p.hitbox = p.ducking ? DUCKING_HITBOX : STANDING_HITBOX;
    if (fabsf(b.toward.x) > fabsf(b.toward.y) && b.heading.y < 0) {
        // Climbing a wall pops into a wall slide up it.
        p.velocity = {0, WALL_SLIDE_LAUNCH};
        p.facing = sign(b.toward.x);
        p.squish = {1.3f, 0.7f};
    } else {
        Vector2 travel = unit(b.heading.vector()) * b.speed;
        if (leap) travel = {b.leap_travel_x, 0};
        p.velocity = -exit * fabsf(POP_FORCE) + travel;
        if (p.velocity.y < 50 && (b.duration - b.timer < SHORT_BURROW_DAMPEN_TIME || input.move_y > 0)) p.velocity.y *= 0.5f;
        p.carry_speed = max(p.carry_speed, fabsf(p.velocity.x));  // Launch: the glide outruns the air speed cap
        p.squish = {0.65f, 1.4f};
    }
    p.jump_timer = POP_TIME;
    p.jump_cut_immune = true;
    b.enter_timer = 0;
    b.exit_timer = EXIT_GRACE;
    b.timer = -COOLDOWN;
    b.hud_spent = true;
    b.glow_exit_timer = GLOW_EXIT_TIME;
    b.needs_grounding = true;
    if (b.sound) { FMOD_Studio_EventInstance_Stop(b.sound, 0); FMOD_Studio_EventInstance_Release(b.sound); b.sound = nullptr; }
    // A last burst up and away from the way you left.
    Vector2 burst_at = b.glow_anchor.vector() - exit * 2 - unit(b.heading.vector()) * 3;
    emit("sparks", burst_at, 20, atan2f(p.velocity.x, p.velocity.y) + PI / 2, 2.5f, 19 * PI / 180);
    if (b.passenger) for (Bud& bud : actors.buds) bud_pop_from_burrow(bud, p);
    b.passenger = false;
    sound("jump", p.position);
    sound("player/burrow_end", p.position);
    update_glow(p, false);
}

bool can_burrow(const Pawn& p) { return p.burrows && !p.burrow.needs_grounding && p.burrow.timer >= 0 && !p.rolling; }

bool try_start_burrow(Pawn& p, Vector2 previous_velocity, Vector2 toward_surface) {
    Burrow& b = p.burrow;
    Point2 at = p.position;
    Vector2 toward = toward_surface;
    if (!snap_to_surface(at, toward, ATTACH_PROBE)) return false;
    p.position = at;
    p.velocity = {};
    p.rolling = false;
    p.hitbox = BURROW_HITBOX;
    b.toward = toward;
    b.duration = BURROW_DURATION;
    b.timer = b.duration;
    b.hud_fill = 1;
    b.hud_spent = false;
    b.interval = 0;
    b.pulse_timer = PULSE_TIME;  // the line igniting flashes too
    b.spread = 1;
    b.leap_coyote = 0;
    b.enter_timer = ENTER_SQUISH_TIME;
    b.remainder = 0;
    b.last_intent = {};
    b.orbiting = b.reverse_queued = false;
    b.solid_left = false;
    b.heading = {(s32)round_even(toward.y), (s32)-round_even(toward.x)};
    // A run or roll dives in heading its way, at its speed.
    s32 entry = sign(previous_velocity.x);
    if (entry && b.heading.x && entry != b.heading.x) { b.heading = -b.heading; b.solid_left = !b.solid_left; }
    b.speed = min(fabsf(previous_velocity.x), BURROW_MAX_SPEED);
    b.passenger = false;
    for (Bud& bud : actors.buds) if (bud_rides_burrow(bud, p)) b.passenger = true;
    b.sound = start_sound("player/burrow", p.position.vector());
    if (b.sound) FMOD_Studio_EventInstance_Start(b.sound);
    sound("player/burrow_start", p.position);
    update_glow(p, true);
    return true;
}

// Pawn.TryWallDive: against a wall in the air with jump held, a tap up or down grips it and a second
// tap that way dives in, burrowing that way at full speed. `wall_jump` is away from the wall.
bool try_wall_dive(Pawn& p, const PawnInput& input, s32 wall_jump, float dt) {
    const float WALL_DIVE_TAP_WINDOW = 0.28f, WALL_DIVE_GRAB_FALL_SPEED = 12, WALL_DIVE_GRAB_RATE = 900;
    Burrow& b = p.burrow;
    s32 held = input.move_y;
    s32 tapped = held != 0 && held != b.dive_held ? held : 0;
    b.dive_held = held;
    b.dive_tap_timer = max(0.0f, b.dive_tap_timer - dt);
    s32 wall = -wall_jump;
    bool can_dive = !p.grounded && wall != 0 && input.jump_down && can_burrow(p);
    b.dive_gripping = b.dive_gripping && can_dive && b.dive_tap_timer > 0;
    if (b.dive_gripping) p.velocity.y = approach(p.velocity.y, WALL_DIVE_GRAB_FALL_SPEED, WALL_DIVE_GRAB_RATE * dt);
    if (!tapped) return false;
    bool second = b.dive_tap_timer > 0 && b.dive_last_tap == tapped;
    if (second) { b.dive_last_tap = 0; b.dive_tap_timer = 0; }
    else { b.dive_last_tap = tapped; b.dive_tap_timer = WALL_DIVE_TAP_WINDOW; }
    if (!can_dive) return false;
    if (!second) {
        if (!b.dive_gripping) { b.dive_gripping = true; p.squish = {1.15f, 0.85f}; }
        return false;
    }
    b.dive_gripping = false;
    if (!try_start_burrow(p, {}, {(float)wall, 0})) return false;
    if (b.heading.y != tapped) { b.heading = -b.heading; b.solid_left = !b.solid_left; }
    b.speed = BURROW_MAX_SPEED;
    return true;
}

bool burrow_state(Pawn& p, const PawnInput& input, float dt) {
    Burrow& b = p.burrow;
    p.velocity = {};
    if (b.enter_timer > 0) {
        b.enter_timer = approach(b.enter_timer, 0, dt);
        float t = 1 - b.enter_timer / ENTER_SQUISH_TIME;
        p.squish = Vector2{1, 1} + (Vector2{SQUASH_TANGENT, SQUASH_NORMAL} - Vector2{1, 1}) * t;
    }

    // Movement latches when you press along the surface; a press back the way you came brakes, then turns.
    Vector2 intent{(float)move_input(input), (float)input.move_y};
    Vector2 travel = b.heading.vector();
    float along = dot(intent, travel);
    if (intent == Vector2{}) b.orbiting = false;
    else if (!(intent == b.last_intent) && fabsf(along) > TANGENT_DEADZONE) { b.reverse_queued = along < 0; b.orbiting = true; }
    b.last_intent = intent;

    // Climbing is slower; descending counts as flat (BurrowCursor.CruiseSpeed).
    float cruise = BURROW_MAX_SPEED * (1 - (1 - CLIMB_SPEED_MULTIPLIER) * max(0.0f, -travel.y));
    float target = b.orbiting && !b.reverse_queued ? cruise : 0;
    b.speed = approach(b.speed, target, (target > b.speed ? BURROW_ACCEL : BURROW_FRICTION) * dt);
    if (b.reverse_queued && b.speed <= REVERSE_FLIP_SPEED) {
        b.heading = -b.heading;
        b.solid_left = !b.solid_left;
        b.reverse_queued = false;
    }

    b.remainder += b.speed * dt;
    s32 pixels = (s32)b.remainder;
    b.remainder -= pixels;
    if (pixels > 0) {
        s32 moved = walk_surface(p.position, b.heading, b.solid_left, pixels);
        b.toward = surface_normal(b.heading, b.solid_left);
        if (moved == 0) {  // a dead end: driving into it pops you out, coasting into it just stops
            if (b.orbiting && can_pop_here(p)) { pop_out(p, input); return true; }
            b.speed = b.remainder = 0;
        }
    }

    // Leaving a top surface over its lip still leaps you up for a moment (UpdateBurrowLeapCoyote).
    if (b.toward.y > 0.5f) {
        b.leap_coyote = LEAP_COYOTE_TIME;
        b.leap_exit = b.toward;
        b.leap_travel_x = b.heading.x ? sign((float)b.heading.x) * b.speed : 0;
    } else b.leap_coyote = approach(b.leap_coyote, 0, dt);

    // The clock's intervals: each one ticks, flashes the line, and shortens it.
    s32 interval = clamp((s32)((b.duration - b.timer) / (b.duration / INTERVALS)), 0, INTERVALS - 1);
    if (interval != b.interval) {
        b.interval = interval;
        b.pulse_timer = PULSE_TIME;
        tick(p, interval);
    }
    float spread = MIN_SPREAD + (1 - MIN_SPREAD) * (1 - (float)b.interval / (INTERVALS - 1));
    b.spread = approach(b.spread, spread, SPREAD_EASE_RATE * dt);

    // Sparks thrown out of the surface, more the faster you go (EmitBurrowSparks), and the ground
    // churned behind you.
    float speed01 = b.speed / BURROW_MAX_SPEED;
    churn_record(p.position, b.toward, speed01);
    if (speed01 > 0.05f) {
        b.sparks += SPARK_RATE * speed01 * dt;
        s32 count = (s32)b.sparks;
        b.sparks -= count;
        Vector2 outward = -b.toward;
        Vector2 origin = p.position.vector() + (outward.y != 0 ? outward : Vector2{});
        if (count) emit("sparks", origin, count, atan2f(outward.y, outward.x), 0.4f + 0.6f * speed01);
    }

    update_glow(p, true);
    if (b.sound) {
        place_sound(b.sound, p.position.vector());
        FMOD_Studio_EventInstance_SetParameterByName(b.sound, "Speed", b.speed / BURROW_MAX_SPEED, 0);
        FMOD_Studio_EventInstance_SetParameterByName(b.sound, "Climb", max(0.0f, -travel.y), 0);
    }
    play(p, "burrow");
    if (move_input(input)) p.facing = move_input(input);

    b.timer = max(0.0f, b.timer - dt);
    b.hud_fill = b.duration > 0 ? b.timer / b.duration : 0;
    if ((b.timer <= 0 || !input.jump_down) && can_pop_here(p)) {
        if (b.timer <= 0) tick(p, INTERVALS);
        pop_out(p, input);
        return true;
    }
    return false;
}

void update_burrow_always(Pawn& p, float dt) {
    Burrow& b = p.burrow;
    if (p.grounded) b.needs_grounding = false;
    b.exit_timer = approach(b.exit_timer, 0, dt);
    if (b.timer < 0) b.timer = min(0.0f, b.timer + dt);
    if (b.hud_spent && can_burrow(p)) { b.hud_fill = 1; b.hud_spent = false; }
    if (b.glow_exit_timer > 0) update_glow(p, false);
    b.glow_exit_timer = approach(b.glow_exit_timer, 0, dt);
    b.pulse_timer = approach(b.pulse_timer, 0, dt);
}

bool burrow_hides(const Pawn& p, bool burrowing) { return burrowing && p.burrow.enter_timer <= 0; }

Color hue_color(float hue) {
    float h = hue * 6, x = 1 - fabsf(fmod_cycling(h, 2) - 1);
    float r, g, b;
    if (h < 1) { r = 1; g = x; b = 0; }
    else if (h < 2) { r = x; g = 1; b = 0; }
    else if (h < 3) { r = 0; g = 1; b = x; }
    else if (h < 4) { r = 0; g = x; b = 1; }
    else if (h < 5) { r = x; g = 0; b = 1; }
    else { r = 1; g = 0; b = x; }
    return {u8(r * 255), u8(g * 255), u8(b * 255), 255};
}

static Color glow_color(const Burrow& b, Vector2 point, Vector2 anchor, float span) {
    float distance = (point - anchor).length();
    float alpha = clamp(1 - distance / span, 0.0f, 1.0f);
    float hue = distance * GLOW_HUE_SCALE - (float)now() * GLOW_CYCLE_SPEED;
    Color color = hue_color(hue - floorf(hue));
    if (b.pulse_timer > 0) {  // the interval flash, easing out
        float pulse = b.pulse_timer / PULSE_TIME;
        color = mix(color, WHITE, pulse * pulse * 0.5f);
        alpha = min(1.0f, alpha + (1 - alpha) * 0.6f * pulse * pulse * 0.5f);
    }
    return color.fade(alpha);
}

static void glow_segment(Vector2 from, Vector2 to, Color color) {
    Vector2 direction = to - from;
    if (dot(direction, direction) < 0.0001f) return;
    Vector2 side = unit({direction.y, -direction.x}) * (GLOW_WEIGHT * 0.5f);
    polygon(from + side, to + side, to - side, from - side, color);
}

void draw_burrow_glow(Pawn& p, bool burrowing) {
    Burrow& b = p.burrow;
    if (!burrowing && b.glow_exit_timer <= 0) return;
    Vector2 anchor = b.glow_anchor.vector();
    float span = 0;
    for (s32 i = 0; i < b.glow_count; i++) span = max(span, (b.glow[i] - anchor).length());
    if (span < 0.001f) span = GLOW_ARC;
    for (s32 i = 0; i + 1 < b.glow_count; i++) glow_segment(b.glow[i], b.glow[i + 1], glow_color(b, b.glow[i], anchor, span));
    if (b.passenger && burrowing) circle(anchor, BUD_DOT_RADIUS, 12, WHITE);
}

void burrow_light(Pawn& p, bool burrowing, Light& light, float dt) {
    p.burrow_light = approach(p.burrow_light, burrowing ? 1.0f : 0.0f, LIGHT_BLEND_RATE * dt);
    light.intensity = LIGHT_INTENSITY + (BURROW_LIGHT_INTENSITY - LIGHT_INTENSITY) * p.burrow_light;
    light.light_color = mix(LIGHT_COLOR, BURROW_LIGHT_COLOR, p.burrow_light);
}
