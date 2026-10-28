// Burrowing (Pawn.Burrow, Game.SurfaceCollision): land with jump held (or, sliding down a wall
// with it held, double-tap up or down: Pawn.WallDive) and you sink into the ground and travel as a point along the boundary between solid and air, round corners and up
// walls, for as long as jump stays held and the burrow lasts. Movement latches when you press
// along the surface and holds through corners; pressing back brakes, then reverses. The clock
// runs out in three intervals, each flashing the rainbow glow line that traces the surface around
// you and shortening it. Letting go (or running out) pops you out with a leap stronger than a
// jump, carrying the glide; climbing a wall pops you into a wall slide. Bud, close by, rides along
// as a white dot and pops out after you. The HUD's bar drains with it and refills on landing.
// Not ported: burrow lines, doors and switches, seams, room crossings, quantum tunnels, sparks,
// the ground churn, the rig sinking in.

Burrow :: struct {
    heading: Point2;            // travel direction along the surface
    solid_left: bool;           // which hand the solid is on
    toward: Vector2;            // the surface normal, into the solid
    timer: float;               // time left while burrowing; negative while cooling down after
    duration: float;
    speed: float;
    remainder: float;           // sub-pixel travel
    orbiting: bool;             // latched by a press along the surface, held through corners
    reverse_queued: bool;       // braking to turn around
    last_intent: Vector2;
    enter_timer: float;         // squashing in
    exit_timer: float;
    needs_grounding: bool;      // after a pop, touch ground before burrowing again
    hud_fill := 1.0;
    hud_spent: bool;
    interval: s32;
    pulse_timer: float;
    spread := 1.0;
    leap_coyote: float;         // just rounded a top corner: still pop up
    leap_exit: Vector2;
    leap_travel_x: float;
    glow_exit_timer: float;
    glow_anchor: Point2;
    glow: [80] Vector2;         // the traced line, room pixels
    glow_count: s32;
    passenger: bool;            // Bud rides along
    sound: *void;               // the burrow loop
    dive_held: s32;             // wall dive: the vertical direction held last frame,
    dive_last_tap: s32;         // the previous tap's,
    dive_tap_timer: float;      // time left for the second tap,
    dive_gripping: bool;        // and the first tap holding you to the wall
}

BURROW_DURATION :: 1.133;
BURROW_MAX_SPEED :: 150.0;
BURROW_HITBOX :: RectInt.{-1, -1, 2, 2};

can_burrow :: (p: *Pawn) -> bool { return p.burrows && !p.burrow.needs_grounding && p.burrow.timer >= 0 && !p.rolling; }

// Pawn.TryStartBurrow: onto the surface `toward` you, heading the way you were moving.
try_start_burrow :: (p: *Pawn, previous_velocity: Vector2, toward_surface := Vector2.{0, 1}) -> bool {
    b := *p.burrow;
    at, toward := p.position, toward_surface;
    if !snap_to_surface(*at, *toward, ATTACH_PROBE) return false;
    p.position = at;
    p.velocity = .{};
    p.rolling = false;
    p.hitbox = BURROW_HITBOX;
    b.toward = toward;
    b.duration = BURROW_DURATION;
    b.timer = b.duration;
    b.hud_fill, b.hud_spent = 1, false;
    b.interval, b.pulse_timer, b.spread = 0, PULSE_TIME, 1;  // the line igniting flashes too
    b.leap_coyote = 0;
    b.enter_timer = ENTER_SQUISH_TIME;
    b.remainder = 0;
    b.last_intent = .{};
    b.orbiting, b.reverse_queued = false, false;
    b.solid_left = false;
    b.heading = .{xx round(toward.y), xx -round(toward.x)};
    // A run or roll dives in heading its way, at its speed.
    entry := sign(previous_velocity.x);
    if entry && b.heading.x && entry != b.heading.x { b.heading = .{-b.heading.x, -b.heading.y}; b.solid_left = !b.solid_left; }
    b.speed = min(abs(previous_velocity.x), BURROW_MAX_SPEED);
    b.passenger = false;
    for context.actors.buds if bud_rides_burrow(it, p) b.passenger = true;
    b.sound = start_sound("player/burrow", to_vector(p.position));
    if b.sound FMOD_Studio_EventInstance_Start(b.sound);
    sound("player/burrow_start", p.position);
    update_glow(p, burrowing = true);
    return true;
}

// Pawn.TryWallDive: against a wall in the air with jump held, a tap up or down grips it and a
// second tap that way dives in, burrowing that way at full speed. `wall_jump` is away from the wall.
try_wall_dive :: (p: *Pawn, input: Pawn_Input, wall_jump: s32, dt: float) -> bool {
    WALL_DIVE_TAP_WINDOW :: 0.28;
    WALL_DIVE_GRAB_FALL_SPEED :: 12.0;
    WALL_DIVE_GRAB_RATE :: 900.0;
    b := *p.burrow;
    held := input.move_y;
    tapped := ifx held != 0 && held != b.dive_held then held else 0;
    b.dive_held = held;
    b.dive_tap_timer = max(0.0, b.dive_tap_timer - dt);
    wall := -wall_jump;
    can_dive := !p.grounded && wall != 0 && input.jump_down && can_burrow(p);
    b.dive_gripping = b.dive_gripping && can_dive && b.dive_tap_timer > 0;
    if b.dive_gripping p.velocity.y = approach(p.velocity.y, WALL_DIVE_GRAB_FALL_SPEED, WALL_DIVE_GRAB_RATE * dt);
    if !tapped return false;
    second := b.dive_tap_timer > 0 && b.dive_last_tap == tapped;
    if second b.dive_last_tap, b.dive_tap_timer = 0, 0;
    else b.dive_last_tap, b.dive_tap_timer = tapped, WALL_DIVE_TAP_WINDOW;
    if !can_dive return false;
    if !second {
        if !b.dive_gripping { b.dive_gripping = true; p.squish = .{1.15, 0.85}; }
        return false;
    }
    b.dive_gripping = false;
    if !try_start_burrow(p, .{}, .{xx wall, 0}) return false;
    if b.heading.y != tapped { b.heading = .{-b.heading.x, -b.heading.y}; b.solid_left = !b.solid_left; }
    b.speed = BURROW_MAX_SPEED;
    return true;
}

// Pawn.BurrowState: one frame underground. True when it popped out.
burrow_state :: (p: *Pawn, input: Pawn_Input, dt: float) -> bool {
    b := *p.burrow;
    p.velocity = .{};
    if b.enter_timer > 0 {
        b.enter_timer = approach(b.enter_timer, 0, dt);
        t := 1 - b.enter_timer / ENTER_SQUISH_TIME;
        p.squish = Vector2.{1, 1} + (Vector2.{SQUASH_TANGENT, SQUASH_NORMAL} - Vector2.{1, 1}) * t;
    }

    // Movement latches when you press along the surface; a press back the way you came brakes, then turns.
    intent := Vector2.{xx move_input(input), xx input.move_y};
    travel := to_vector(b.heading);
    along := dot(intent, travel);
    if intent == .{} b.orbiting = false;
    else if intent != b.last_intent && abs(along) > TANGENT_DEADZONE { b.reverse_queued = along < 0; b.orbiting = true; }
    b.last_intent = intent;

    // Climbing is slower; descending counts as flat (BurrowCursor.CruiseSpeed).
    cruise := BURROW_MAX_SPEED * (1 - (1 - CLIMB_SPEED_MULTIPLIER) * max(0.0, -travel.y));
    target := ifx b.orbiting && !b.reverse_queued then cruise else 0.0;
    b.speed = approach(b.speed, target, (ifx target > b.speed then ACCEL else FRICTION) * dt);
    if b.reverse_queued && b.speed <= REVERSE_FLIP_SPEED {
        b.heading = .{-b.heading.x, -b.heading.y};
        b.solid_left = !b.solid_left;
        b.reverse_queued = false;
    }

    b.remainder += b.speed * dt;
    pixels := cast(s32) b.remainder;
    b.remainder -= pixels;
    if pixels > 0 {
        moved := walk_surface(*p.position, *b.heading, b.solid_left, pixels);
        b.toward = surface_normal(b.heading, b.solid_left);
        if moved == 0 {  // a dead end: driving into it pops you out, coasting into it just stops
            if b.orbiting && can_pop_here(p) { pop_out(p, input); return true; }
            b.speed, b.remainder = 0, 0;
        }
    }

    // Leaving a top surface over its lip still leaps you up for a moment (UpdateBurrowLeapCoyote).
    if b.toward.y > 0.5 {
        b.leap_coyote = LEAP_COYOTE_TIME;
        b.leap_exit = b.toward;
        b.leap_travel_x = ifx b.heading.x then sign(cast(float) b.heading.x) * b.speed else 0.0;
    } else b.leap_coyote = approach(b.leap_coyote, 0, dt);

    // The clock's intervals: each one ticks, flashes the line, and shortens it.
    interval := clamp(cast(s32) ((b.duration - b.timer) / (b.duration / INTERVALS)), 0, INTERVALS - 1);
    if interval != b.interval {
        b.interval = interval;
        b.pulse_timer = PULSE_TIME;
        tick(p, interval);
    }
    spread := MIN_SPREAD + (1 - MIN_SPREAD) * (1 - cast(float) b.interval / (INTERVALS - 1));
    b.spread = approach(b.spread, spread, SPREAD_EASE_RATE * dt);

    update_glow(p, burrowing = true);
    if b.sound {
        place_sound(b.sound, to_vector(p.position));
        FMOD_Studio_EventInstance_SetParameterByName(b.sound, "Speed", b.speed / BURROW_MAX_SPEED, 0);
        FMOD_Studio_EventInstance_SetParameterByName(b.sound, "Climb", max(0.0, -travel.y), 0);
    }
    play(p, "burrow");
    if move_input(input) p.facing = move_input(input);

    b.timer = max(0.0, b.timer - dt);
    b.hud_fill = ifx b.duration > 0 then b.timer / b.duration else 0;
    if (b.timer <= 0 || !input.jump_down) && can_pop_here(p) {
        if b.timer <= 0 tick(p, INTERVALS);
        pop_out(p, input);
        return true;
    }
    return false;
}

// Every frame, burrowing or not (UpdateBurrowRecharge, UpdateBurrowAlways).
update_burrow_always :: (p: *Pawn, dt: float) {
    b := *p.burrow;
    if p.grounded b.needs_grounding = false;
    b.exit_timer = approach(b.exit_timer, 0, dt);
    if b.timer < 0 b.timer = min(0.0, b.timer + dt);
    if b.hud_spent && can_burrow(p) { b.hud_fill = 1; b.hud_spent = false; }
    if b.glow_exit_timer > 0 update_glow(p, burrowing = false);
    b.glow_exit_timer = approach(b.glow_exit_timer, 0, dt);
    b.pulse_timer = approach(b.pulse_timer, 0, dt);
}

// Submerged once the squash-in is done (BurrowHidesSprite).
burrow_hides :: (p: *Pawn, burrowing: bool) -> bool { return burrowing && p.burrow.enter_timer <= 0; }

// The rainbow line along the surface, fading from where you are to its ends, and Bud's dot.
draw_burrow_glow :: (p: *Pawn, burrowing: bool) {
    b := *p.burrow;
    if !burrowing && b.glow_exit_timer <= 0 return;
    anchor := to_vector(b.glow_anchor);
    span := 0.0;
    for 0..b.glow_count-1 span = max(span, length(b.glow[it] - anchor));
    if span < 0.001 span = GLOW_ARC;
    for 0..b.glow_count-2 glow_segment(b.glow[it], b.glow[it + 1], glow_color(b, b.glow[it], anchor, span));
    if b.passenger && burrowing circle(anchor, BUD_DOT_RADIUS, 12, WHITE);
}

burrow_light :: (p: *Pawn, burrowing: bool, light: *Light, dt: float) {
    p.burrow_light = approach(p.burrow_light, ifx burrowing then 1.0 else 0.0, LIGHT_BLEND_RATE * dt);
    light.intensity = LIGHT_INTENSITY + (BURROW_LIGHT_INTENSITY - LIGHT_INTENSITY) * p.burrow_light;
    light.light_color = mix_color(LIGHT_COLOR, BURROW_LIGHT_COLOR, p.burrow_light);
}

#scope_file

ATTACH_PROBE :: 8;
ACCEL :: 500.0;
FRICTION :: 600.0;
REVERSE_FLIP_SPEED :: 8.0;
CLIMB_SPEED_MULTIPLIER :: 0.59;
TANGENT_DEADZONE :: 0.3;
POP_FORCE :: JUMP_FORCE * 1.18;
POP_TIME :: JUMP_TIME * 0.9;
WALL_SLIDE_LAUNCH :: JUMP_FORCE * 1.2;
POP_SLIDE :: 6;
COOLDOWN :: 0.25;
SHORT_BURROW_DAMPEN_TIME :: 0.09;
EXIT_GRACE :: 0.1;
ENTER_SQUISH_TIME :: 0.12;
SQUASH_NORMAL :: 0.45;
SQUASH_TANGENT :: 1.6;
LEAP_COYOTE_TIME :: 0.12;
INTERVALS :: 3;
PULSE_TIME :: 0.22;
MIN_SPREAD :: 0.4;
SPREAD_EASE_RATE :: 1.4;
GLOW_ARC :: 15.0;
GLOW_EXIT_TIME :: 0.13;
GLOW_WEIGHT :: 1.5;
GLOW_CYCLE_SPEED :: 0.6;
GLOW_HUE_SCALE :: 0.03;
BUD_DOT_RADIUS :: 2.25;
BUD_RIDE_RADIUS :: 14;
LIGHT_INTENSITY :: 0.45;
BURROW_LIGHT_INTENSITY :: 0.85;
LIGHT_COLOR :: #run rgb(0xffcfa0);
BURROW_LIGHT_COLOR :: #run rgb(0xb98cff);
LIGHT_BLEND_RATE :: 6.0;

tick :: (p: *Pawn, interval: s32) {
    instance := start_sound("player/burrow_tick", to_vector(p.position));
    if !instance return;
    FMOD_Studio_EventInstance_SetParameterByName(instance, "Interval", xx interval, 0);
    finish_sound(instance);
}

// Pawn.PopOutOfBurrow: out where the body fits, launched off the surface with the glide.
pop_out :: (p: *Pawn, input: Pawn_Input) {
    b := *p.burrow;
    exit := b.toward;
    leap := b.leap_coyote > 0 && exit.y <= 0.5;
    if leap exit = b.leap_exit;
    if room_to_pop(p, *p.ducking, *p.position) {}
    p.hitbox = ifx p.ducking then RectInt.{-4, -STANDING_HEIGHT / 2, 8, STANDING_HEIGHT / 2} else RectInt.{-4, -STANDING_HEIGHT, 8, STANDING_HEIGHT};
    if abs(b.toward.x) > abs(b.toward.y) && b.heading.y < 0 {
        // Climbing a wall pops into a wall slide up it.
        p.velocity = .{0, WALL_SLIDE_LAUNCH};
        p.facing = sign(b.toward.x);
        p.squish = .{1.3, 0.7};
    } else {
        travel := unit_vector(to_vector(b.heading)) * b.speed;
        if leap travel = .{b.leap_travel_x, 0};
        p.velocity = -exit * abs(POP_FORCE) + travel;
        if p.velocity.y < 50 && (b.duration - b.timer < SHORT_BURROW_DAMPEN_TIME || input.move_y > 0) p.velocity.y *= 0.5;
        p.carry_speed = max(p.carry_speed, abs(p.velocity.x));  // Launch: the glide outruns the air speed cap
        p.squish = .{0.65, 1.4};
    }
    p.jump_timer = POP_TIME;
    p.jump_cut_immune = true;
    b.enter_timer = 0;
    b.exit_timer = EXIT_GRACE;
    b.timer = -COOLDOWN;
    b.hud_spent = true;
    b.glow_exit_timer = GLOW_EXIT_TIME;
    b.needs_grounding = true;
    if b.sound { FMOD_Studio_EventInstance_Stop(b.sound, FMOD_STUDIO_STOP_ALLOWFADEOUT); FMOD_Studio_EventInstance_Release(b.sound); b.sound = null; }
    if b.passenger for context.actors.buds bud_pop_from_burrow(it, p);
    b.passenger = false;
    sound("jump", p.position);
    sound("player/burrow_end", p.position);
    update_glow(p, burrowing = false);
}

// RoomToPop: the nearest place the body fits, out along the normal and then a little along the surface.
room_to_pop :: (p: *Pawn, crouch: *bool, at: *Point2) -> bool {
    outward := -p.burrow.toward;
    along := Vector2.{outward.y, -outward.x};
    for duck: bool.[false, true] for distance: 0..STANDING_HEIGHT for slide: 0..POP_SLIDE for side: s32.[1, -1] {
        if slide == 0 && side < 0 continue;
        spot := to_vector(p.position) + outward * cast(float) distance + along * cast(float) (slide * side);
        candidate := Point2.{xx round(spot.x), xx round(spot.y)};
        box := ifx duck then RectInt.{-4, -STANDING_HEIGHT / 2, 8, STANDING_HEIGHT / 2} else RectInt.{-4, -STANDING_HEIGHT, 8, STANDING_HEIGHT};
        if !overlaps_solid(.{candidate.x + box.x, candidate.y + box.y, box.w, box.h}) { crouch.*, at.* = duck, candidate; return true; }
    }
    return false;
}

// Popping out needs room for a crouch, unless there's nowhere to walk at all.
can_pop_here :: (p: *Pawn) -> bool {
    crouch: bool;
    at: Point2;
    if room_to_pop(p, *crouch, *at) return true;
    b := *p.burrow;
    return pick_boundary_dir(p.position, b.heading, b.solid_left) == .{} && pick_boundary_dir(p.position, .{-b.heading.x, -b.heading.y}, !b.solid_left) == .{};
}

update_glow :: (p: *Pawn, burrowing: bool) {
    b := *p.burrow;
    b.glow_count = 0;
    arc := ifx burrowing then GLOW_ARC * (1 - b.enter_timer / ENTER_SQUISH_TIME) * b.spread else GLOW_ARC * b.glow_exit_timer / GLOW_EXIT_TIME;
    if arc <= 0 return;
    if burrowing b.glow_anchor = p.position;
    // Back along the surface (reversed), then forward from the anchor (TraceBurrowPathFromState).
    back: [40] Vector2;
    back_count := trace(b.glow_anchor, .{-b.heading.x, -b.heading.y}, !b.solid_left, arc, back);
    for < 1..back_count-1 { b.glow[b.glow_count] = back[it]; b.glow_count += 1; }
    b.glow_count += trace(b.glow_anchor, b.heading, b.solid_left, arc, array_view(b.glow, b.glow_count, b.glow.count - b.glow_count));
}

trace :: (start: Point2, heading: Point2, solid_left: bool, arc: float, into: [] Vector2) -> s32 {
    count: s32;
    position, direction := start, heading;
    into[0] = to_vector(position);
    count = 1;
    distance := 0.0;
    while distance < arc && count < into.count {
        next := pick_boundary_dir(position, direction, solid_left);
        if next == .{} break;
        position += next;
        direction = next;
        distance += 1;
        into[count] = to_vector(position);
        count += 1;
    }
    return count;
}

glow_color :: (b: *Burrow, point: Vector2, anchor: Vector2, span: float) -> Color {
    distance := length(point - anchor);
    alpha := clamp(1 - distance / span, 0, 1);
    hue := distance * GLOW_HUE_SCALE - cast(float) now() * GLOW_CYCLE_SPEED;
    color := hue_color(hue - floor(hue));
    if b.pulse_timer > 0 {  // the interval flash, easing out
        pulse := b.pulse_timer / PULSE_TIME;
        color = mix_color(color, WHITE, pulse * pulse * 0.5);
        alpha = min(1.0, alpha + (1 - alpha) * 0.6 * pulse * pulse * 0.5);
    }
    return fade(color, alpha);
}

hue_color :: (hue: float) -> Color {
    h := hue * 6;
    x := 1 - abs(fmod_cycling(h, 2) - 1);
    r, g, bl: float;
    if h < 1 { r, g, bl = 1, x, 0; }
    else if h < 2 { r, g, bl = x, 1, 0; }
    else if h < 3 { r, g, bl = 0, 1, x; }
    else if h < 4 { r, g, bl = 0, x, 1; }
    else if h < 5 { r, g, bl = x, 0, 1; }
    else { r, g, bl = 1, 0, x; }
    return .{xx (r * 255), xx (g * 255), xx (bl * 255), 255};
}

mix_color :: (a: Color, b: Color, t: float) -> Color {
    m :: (x: u8, y: u8) -> u8 #expand { return xx (x + (cast(float) y - x) * t); }
    return .{m(a.r, b.r), m(a.g, b.g), m(a.b, b.b), m(a.a, b.a)};
}

glow_segment :: (from: Vector2, to: Vector2, color: Color) {
    direction := to - from;
    if dot(direction, direction) < 0.0001 return;
    side := unit_vector(Vector2.{direction.y, -direction.x}) * (GLOW_WEIGHT * 0.5);
    polygon(from + side, to + side, to - side, from - side, color);
}

// --- Walking the boundary between solid and air (Game.SurfaceCollision), in room pixels. ---

// C#'s MathF.Round: halves go to the even neighbour, which decides which pixel a face's centre snaps to.
round :: (x: float) -> float {
    r := floor(x + 0.5);
    if r - x == 0.5 && fmod_cycling(r, 2) != 0 r -= 1;
    return r;
}

solid_pixel :: (cell: Point2) -> bool { return overlaps_solid(.{cell.x, cell.y, 1, 1}); }
in_room :: (cell: Point2) -> bool { return cell.x >= 0 && cell.y >= 0 && cell.x < room.size.x * TILE_SIZE && cell.y < room.size.y * TILE_SIZE; }
cell_at :: (v: Vector2) -> Point2 { return .{xx floor(v.x), xx floor(v.y)}; }

// The way on from vertex `v`: turn toward the solid, straight on, or away from it, whichever edge has
// solid on its solid side and open room on its air side.
pick_boundary_dir :: (v: Point2, heading: Point2, solid_left: bool) -> Point2 {
    left, right := Point2.{heading.y, -heading.x}, Point2.{-heading.y, heading.x};
    candidates := ifx solid_left then Point2.[left, heading, right] else Point2.[right, heading, left];
    for candidates if boundary_edge_valid(v, it, solid_left) return it;
    return .{};
}

boundary_edge_valid :: (v: Point2, d: Point2, solid_left: bool) -> bool {
    middle := to_vector(v) + to_vector(d) * 0.5;
    left := cell_at(middle + Vector2.{xx d.y, xx -d.x} * 0.5);
    right := cell_at(middle + Vector2.{xx -d.y, xx d.x} * 0.5);
    solid, air := ifx solid_left then left else right, ifx solid_left then right else left;
    return !solid_pixel(air) && in_room(air) && solid_pixel(solid);
}

walk_surface :: (position: *Point2, heading: *Point2, solid_left: bool, pixels: s32) -> s32 {
    moved: s32;
    for 1..pixels {
        next := pick_boundary_dir(position.*, heading.*, solid_left);
        if next == .{} break;
        position.* += next;
        heading.* = next;
        moved += 1;
    }
    return moved;
}

surface_normal :: (heading: Point2, solid_left: bool) -> Vector2 {
    return ifx solid_left then Vector2.{xx heading.y, xx -heading.x} else .{xx -heading.y, xx heading.x};
}

// SnapToSurface (SeedBoundaryFace): out of any solid along -toward, then on to the face along it.
snap_to_surface :: (position: *Point2, toward: *Vector2, reach: s32) -> bool {
    n := Point2.{ifx abs(toward.x) >= abs(toward.y) then sign(toward.x) else 0, ifx abs(toward.y) > abs(toward.x) then sign(toward.y) else 0};
    if n == .{} return false;
    c := position.*;
    for 1..reach { if !solid_pixel(c) break; c -= n; }
    if solid_pixel(c) return false;
    for 1..reach { if solid_pixel(c + n) break; c += n; }
    if !solid_pixel(c + n) return false;
    center := to_vector(c) + Vector2.{0.5, 0.5} + to_vector(n) * 0.5;
    position.* = .{xx round(center.x), xx round(center.y)};
    toward.* = to_vector(n);
    return true;
}

#import "Basic";
#import "Math";
#import "FMOD";
#import "Kit";
