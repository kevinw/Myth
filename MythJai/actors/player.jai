// The player: Myth's controls (controls.jai) driving a Pawn.

Player :: struct @GameActor {
    #as using pawn: Pawn;
    health := MAX_HEALTH;
    state: enum u8 { NORMAL; ATTACK; HURT; DEAD; BURROW; };
    state_time: float;
    attack: enum u8 { SIDE; DOWN; UP; DUCK; };
    attack_hit_target: bool;    // this swing has touched something to hit
    attack_blocked: bool;       // ...or a wall stopped it
    attack_swipe: Handle(Pop);
    rig_dive_grace: float;      // jump let go this recently still counts toward a dive
    rig_dive_angle: float;
    rig_dive_weight: float;
    rig_dive_clip: bool;
    footstep_frame: s32 = -1;
    last_safe_ground: Point2;   // where a cliff fall puts you back
    rig: Rig_Pose;
    light: Handle(Light);       // the warm glow around you (Player.CreatePlayerLight)
    last_skid_sound: float64;
}

MAX_HEALTH :: 99;
HIT_DAMAGE :: 17;           // six hits from full
INVINCIBLE_DURATION :: 1.5;
HURT_DURATION :: 0.5;
HURT_FRICTION :: 200.0;
DEATH_DURATION :: 2.2;
ATTACK_ACTIVE_TIME :: 0.2;  // the swing hits for this long
HIT_KNOCKBACK :: 30.0;      // what hitting something pushes you back by

update :: (p: *Player) {
    input := Pawn_Input.{
        move_x = control_move_x(),
        move_y = control_move_y(),
        jump_pressed = control_jump_pressed(),
        jump_down = control_jump_down(),
        dash_pressed = control_dash_pressed(),
        dash_down = control_dash_down(),
    };
    attack_pressed := control_attack_pressed();

    p.state_time += delta;
    if p.state == .NORMAL && attack_pressed start_attack(p, input);
    falling := p.velocity.y;  // before this frame's landing stops it
    events: Pawn_Events;
    if p.state == {
      case .NORMAL;
        events = pawn_update(p, input, delta);
        if events & .BURROWED { p.state = .BURROW; p.state_time = 0; }
      case .BURROW;
        if burrow_state(p, input, delta) {
            p.state = .NORMAL;
            p.state_time = 0;
            if demo log("%: POPPED at % vel %", demo_time, p.position, p.velocity);
        }
      case .ATTACK;
        move_x := move_input(input);
        horizontal_movement(p, move_x, ifx move_x then input.move_x else 0, delta);
        name := attack_animation(p);
        play(p, name, looping = false);
        if p.state_time < ATTACK_ACTIVE_TIME swing(p);
        events = pawn_update(p, input, delta, normal = false);
        sprite := get_sprite("player");
        if !sprite || p.state_time >= tag_duration(sprite, find_tag(sprite, name)) {
            p.state = .NORMAL;
            play(p, ifx p.attack == .DUCK then "duck" else "idle");
        }
      case .HURT;
        p.velocity.x = approach(p.velocity.x, 0, HURT_FRICTION * delta);
        events = pawn_update(p, .{}, delta, normal = false);
        if p.state_time >= HURT_DURATION { p.state = .NORMAL; p.state_time = 0; }
      case .DEAD;
        events = pawn_update(p, .{}, delta, normal = false);
        if p.state_time >= DEATH_DURATION respawn_requested = true;
    }
    update_burrow_always(p, delta);
    if demo && events log("%: % at % vel % anim %", demo_time, events, p.position, p.velocity, p.animation);
    // What the rig plays, from the pawn's animation, as Pawn.UpdateRig picks it: a let-go walk
    // idles; a crawl that's stopped, or still sliding out of a run, crouches; sprinting gets the
    // full run, and the sprint once it's really moving; holding jump past the top of a jump with a
    // burrow to be had, the body tips into a dive at the ground.
    RIG_DIVE_RELEASE_GRACE :: 0.1;
    RIG_CRAWL_SPEED_SLACK :: 1.15;
    rig_animation := p.animation;
    p.rig_dive_grace = ifx input.jump_down then RIG_DIVE_RELEASE_GRACE else max(0.0, p.rig_dive_grace - delta);
    speed := abs(p.velocity.x);
    if p.animation == "walk" && move_input(input) == 0 rig_animation = "idle";
    if p.animation == "crawl" && (move_input(input) == 0 || speed > CRAWL_SPEED * RIG_CRAWL_SPEED_SLACK) rig_animation = "duck";
    if (p.animation == "run" || p.animation == "run_start") && dash_running(p) && p.state == .NORMAL
        rig_animation = ifx speed > MAX_GROUND_SPEED * 1.15 then "sprint" else "jog";
    // The dive eases in, its angle tracking the fall, and lets go once it's mostly out (Pawn.UpdateRig).
    RIG_DIVE_FULL_ANGLE_SPEED :: 130.0;
    RIG_DIVE_ANGLE_RESPONSE :: 14.0;
    RIG_DIVE_IN_RESPONSE :: 10.0;
    RIG_DIVE_LAND_RESPONSE :: 40.0;
    RIG_DIVE_CLIP_OUT :: 0.3;
    airborne := p.state == .NORMAL && !p.grounded && take_for(p.animation) == "PlayerAir";
    diving := airborne && p.velocity.y >= 0 && p.rig_dive_grace > 0 && can_burrow(p);
    if diving {
        angle := clamp(p.velocity.y / RIG_DIVE_FULL_ANGLE_SPEED, -1, 1) * PI / 2;
        p.rig_dive_angle = ifx p.rig_dive_weight < 0.01 then angle else p.rig_dive_angle + (angle - p.rig_dive_angle) * (1 - exp(-RIG_DIVE_ANGLE_RESPONSE * delta));
    }
    response := ifx p.grounded then RIG_DIVE_LAND_RESPONSE else RIG_DIVE_IN_RESPONSE;
    p.rig_dive_weight += ((ifx diving then 1.0 else 0.0) - p.rig_dive_weight) * (1 - exp(-response * delta));
    p.rig_dive_clip = airborne && (diving || p.rig_dive_clip && p.rig_dive_weight > RIG_DIVE_CLIP_OUT);
    if p.rig_dive_clip rig_animation = "dive";
    // A swing in the air keeps the air legs and, up or down, rolls the torso into it (UpdateRigAirLegs).
    was_air_swing := p.rig.air_swing;
    p.rig.air_swing = p.state == .ATTACK && !p.grounded;
    p.rig.attack_pitch = ifx p.attack == .DOWN then 1.0 else ifx p.attack == .UP then -1.0 else 0.0;
    if p.rig.air_swing && !was_air_swing { p.rig.leg_weight = 1; if p.attack == .DOWN p.rig.attack_reach = 1; }
    p.rig.arm_aim = .{p.facing * cos(p.rig_dive_angle), sin(p.rig_dive_angle)};
    p.rig.arm_aim_weight = p.rig_dive_weight;
    play_rig(*p.rig, rig_animation, p.velocity, delta, restart = p.state == .ATTACK && p.state_time <= delta, facing = p.facing,
        still = p.state == .NORMAL && !input.move_x && !input.move_y && !input.jump_down && !input.dash_down && !attack_pressed);
    update_rig(*p.rig, p, delta, p.grounded);
    sounds(p, events);
    dust(p, events, falling);
    if events & .LANDED land_rig(*p.rig, falling * 0.5);  // Player.OnLanded's RigLandingScale

    if p.grounded && p.state == .NORMAL p.last_safe_ground = p.position;
    if p.state == .BURROW p.grounded = false;  // a point on the surface, not standing on it
    // Player.CheckRoomBounds: well below the room is a cliff fall; at the other edges, where Myth
    // would move on to the next room, the player is held.
    size := room.size * TILE_SIZE;
    if p.position.y > size.y + 12 { if p.state != .DEAD fall_off_cliff(p); }
    else p.position = .{clamp(p.position.x, 0, size.x), max(p.position.y, 0)};

    light := get(*context.actors.lights, p.light);
    if light { light.position = p.position + Point2.{0, -6}; burrow_light(p, p.state == .BURROW, light, delta); }

    // Touching an enemy hurts, except underground.
    if p.hit_cooldown <= 0 && p.state != .DEAD && p.state != .BURROW for context.actors.blobs if overlaps(hitbox_rect(p), hitbox_rect(it)) { hurt(p, it); break; }
}

draw_normals :: (p: *Player) {
    if burrow_hides(p, p.state == .BURROW) || p.hit_cooldown > 0 && p.state != .DEAD && cast(int) (now() * 20) % 2 return;
    draw_rig_normals(*p.rig, to_vector(p.position), PLAYER_LOOK);
}

draw :: (p: *Player) {
    if burrow_hides(p, p.state == .BURROW) return;
    if p.hit_cooldown > 0 && p.state != .DEAD && cast(int) (now() * 20) % 2 return;  // i-frame blink
    if p.rig.posed {
        draw_rig(*p.rig, to_vector(p.position), PLAYER_LOOK);
        // The hat sits on the rig's head (Player.RenderHat, RigHatPosition).
        head, radius := rig_head(*p.rig);
        hat := get_sprite("hat");
        if hat {
            origin := Vector2.{floor(hat.frame_size.x * 0.5), hat.frame_size.y};
            push_matrix(to_vector(p.position) + head - Vector2.{0, radius} + Vector2.{0, 1}, Vector2.{xx p.facing, 1});
            draw_frame(hat, 0, -origin);
            pop_matrix();
        }
    } else {
        draw_sprite(p, "player");
    }
}

spawn_player :: (at: Point2) -> *Player {
    p := add(*context.actors.players);
    p.position = at;
    p.last_safe_ground = at;
    // Assets/Actors/player-glow-light.json
    light: *Light;
    light, p.light = add(*context.actors.lights);
    light.position, light.radius, light.intensity, light.light_color, light.height = at, 36, 0.45, rgb(0xffcfa0), 16;
    light.flicker_frequency, light.flicker_intensity, light.sprite_name = 1.5, 0.02, "";
    p.hitbox = .{-4, -STANDING_HEIGHT, 8, STANDING_HEIGHT};
    p.burrows = true;
    play(p, "idle");
    start_roll(p);  // Assets/Actors/player.json starts the player in the Roll state
    return p;
}

#scope_file

// The sounds the body's frame made, as Myth's Pawn and Player make them.
sounds :: (p: *Player, events: Pawn_Events) {
    GROUND_TYPE :: "Brick";  // the substance under the player; both of the slice's kits are Brick
    if events & (.JUMPED | .WALL_JUMPED) sound("jump", p.position);
    if events & .LANDED sound("land", p.position, "Type", GROUND_TYPE);
    if events & .ROLLED sound("player/roll", p.position);
    if events & .DASH_HIT_WALL sound("player/dash_wall", p.position);
    if events & .SKIDDED && now() - p.last_skid_sound > 0.5 { sound("player/skid", p.position); p.last_skid_sound = now(); }

    // A footstep on every sixth frame of a running animation, like Myth's AfterStateUpdate.
    sprite := get_sprite("player");
    if !sprite || !p.grounded || (p.animation != "run" && p.animation != "walk" && p.animation != "run_start") { p.footstep_frame = -1; return; }
    tag := find_tag(sprite, p.animation);
    frame := frame_at(sprite, tag, p.animation_time, p.animation_looping);
    if frame != p.footstep_frame && (frame - tag.first) % 6 == 0 sound("footstep", p.position, "Type", GROUND_TYPE);
    p.footstep_frame = frame;
}

// Dust kicked up by jumps, skids, wall jumps and hard landings (Player.EmitJumpParticles).
dust :: (p: *Player, events: Pawn_Events, falling: float) {
    HARD_LANDING_SPEED :: 200.0;
    kick :: (at: Point2) #expand { emit("jump", to_vector(at), 8, PI / 2, 2.51, 59 * PI / 180); }
    if events & .JUMPED kick(p.position);
    if events & .SKIDDED kick(p.position + Point2.{p.facing * 8, 0});
    if events & .WALL_JUMPED kick(p.position - Point2.{p.facing * p.hitbox.w / 2, 0});
    if events & .LANDED && falling >= HARD_LANDING_SPEED kick(p.position);
}

// The duck attack's spray of plasma along the blade (Player.EmitPlasmaStrike).
plasma_strike :: (p: *Player) {
    direction := Vector2.{xx p.facing, 0};
    emit_line("plasma_strike", to_vector(p.position) + direction * 5 + Vector2.{0, -3}, direction, 7, Vector2.{20.0 * p.facing + p.velocity.x, p.velocity.y * 0.9});
}

start_attack :: (p: *Player, input: Pawn_Input) {
    move_x := move_input(input);
    if !p.grounded && move_x p.facing = move_x;
    p.attack = ifx p.ducking then .DUCK else ifx !p.grounded && input.move_y > 0 then .DOWN else ifx input.move_y < 0 then .UP else .SIDE;
    p.state = .ATTACK;
    p.state_time = 0;
    p.dash_run_armed = false;  // leaving Normal ends a sprint
    sound(ifx p.attack == .DUCK then "sword_crouched" else "sword", p.position);

    // The swipe, centred on what the swing hits and led a little by your motion (Player.AttackState).
    box := attack_box(p);
    at := Point2.{box.x + box.w / 2, box.y + box.h / 2} + Point2.{xx (p.velocity.x * 14 * delta), xx (p.velocity.y * 9 * delta)};
    p.attack_hit_target, p.attack_blocked, p.attack_swipe = false, false, .{};
    if p.attack == {
      case .DOWN; p.attack_swipe = spawn_pop(at - Point2.{0, 8}, "strike_down_sm", p.facing);
      case .UP;   p.attack_swipe = spawn_pop(at + Point2.{0, 12}, "strike_down_sm", p.facing, flip_y = true);
      case .SIDE; p.attack_swipe = spawn_pop(at - Point2.{17, 0}, "strike_right_sm", p.facing);
      case .DUCK; plasma_strike(p);
    }
}

attack_animation :: (p: *Player) -> string {
    if p.attack == {
      case .DUCK; return "duck_attack";
      case .UP;   return "attack_up";
      case;       return "attack";
    }
}

// The swing's reach, in room pixels.
attack_box :: (p: *Player) -> RectInt {
    box: RectInt;
    if p.attack == {
      case .DUCK; box = .{3, -6, 13, 6};
      case .DOWN; box = .{-19, 0, 39, 14};
      case .UP;   box = .{-12, -27, 24, 12};
      case;       box = .{6, -19, 20, 22};
    }
    if p.facing < 0 box.x = -(box.x + box.w);
    box.x += p.position.x;
    box.y += p.position.y;
    return box;
}

// Hits whatever the swing's box touches this frame (Player.AttackThisFrame). A down swing that
// lands bounces you up off it and re-arms the burrow; one buried in a wall clangs to a stop.
swing :: (p: *Player) {
    POGO_SPEED :: 125.0;
    BLOCK_OVERLAP :: 0.5;
    if p.attack_blocked return;
    box := attack_box(p);
    hit_something := false;
    for context.actors.blobs if overlaps(box, hitbox_rect(it)) {
        p.attack_hit_target = true;
        if it.hit_cooldown > 0 continue;
        it.hit_cooldown = 0.5;
        hit_something = true;
        sound("player/sword_hit", p.position, "hit_destination", "flesh");
        hit(it, p);
        center := Vector2.{box.x + box.w * 0.5, box.y + box.h * 0.5};
        p.velocity += unit_vector(to_vector(p.position) - center) * HIT_KNOCKBACK;
    }
    for context.actors.buds bud_struck(it, box, p.position.x);
    if hit_something && p.attack == .DOWN {
        p.velocity.y = -POGO_SPEED;
        p.burrow.needs_grounding, p.burrow.timer = false, 0;
    }
    if !p.attack_hit_target && (p.attack == .DUCK || p.state_time >= attack_windup()) && solid_fraction(box) >= BLOCK_OVERLAP {
        p.attack_blocked = true;
        swipe := get(*context.actors.pops, p.attack_swipe);
        if swipe free(*context.actors.pops, swipe);
        sound("player/sword_block", p.position);
    }
}

// How long the swing winds up before it can clang: the attack animation's first frame.
attack_windup :: () -> float {
    sprite := get_sprite("player");
    if !sprite return 0;
    return sprite.durations[find_tag(sprite, "attack").first] / 1000.0;
}

// The share of a rect's pixels inside solid tiles (Game.GetRoomCollisionOverlapRatio).
solid_fraction :: (r: RectInt) -> float {
    if r.w <= 0 || r.h <= 0 return 0;
    solid := 0;
    for y: r.y..r.y+r.h-1 for x: r.x..r.x+r.w-1 if overlaps_solid(.{x, y, 1, 1}) solid += 1;
    return cast(float) solid / (r.w * r.h);
}

hurt :: (p: *Player, by: *Actor) {
    p.hit_cooldown = INVINCIBLE_DURATION;
    hit_stun(0.1);
    shake(0.1);
    // The glitch stretches away from what hit you (HurtGlitchAxis).
    away := to_vector(p.position - by.position);
    if dot(away, away) <= 0.001 away = ifx dot(p.velocity, p.velocity) > 0.001 then p.velocity else Vector2.{xx -p.facing, 0};
    pulse_dither_glitch(away);
    play(p, "hurt");
    dir := sign(xx (p.position.x - by.position.x));
    if !dir dir = -p.facing;
    p.velocity = .{dir * 100.0, -80};
    p.health -= HIT_DAMAGE;
    p.state = ifx p.health > 0 then .HURT else .DEAD;
    p.state_time = 0;
    if p.state == .DEAD die(p);
    sound(ifx p.state == .DEAD then "death" else "hurt", p.position);
    bark(.PLAYER_HURT);
}

// Falling out of the room costs a hit and puts you back on the last ground you stood on.
fall_off_cliff :: (p: *Player) {
    hit_stun(0.4);
    p.position = p.last_safe_ground;
    p.velocity, p.remainder = .{}, .{};
    hurt(p, p);
    if p.state != .DEAD p.velocity = .{};  // no knockback off the ledge again
}

// Player.HurtState at zero health: the world goes, all but you and your glow (Player.HurtState).
die :: (p: *Player) {
    shake(0.4);
    for context.actors.buds free(*context.actors.buds, it);
    for context.actors.blobs free(*context.actors.blobs, it);
    for context.actors.pops free(*context.actors.pops, it);
    for context.actors.player_starts free(*context.actors.player_starts, it);
    glow := get(*context.actors.lights, p.light);
    for context.actors.lights if it != glow free(*context.actors.lights, it);
}

#scope_export

// `myth demo`: a scripted run through the room, played as real SDL key events pushed into the
// event queue, so it goes through the same input path as a keyboard.
demo: bool;
demo_time: float;

DEMO :: struct { at: float; key: SDL_Keycode; down: bool; }.[
    .{0.3, SDLK_RIGHT, true},                               // run right
    .{1.0, SDLK_SPACE, true}, .{1.4, SDLK_SPACE, false},    // a held jump,
    .{1.5, SDLK_DOWN, true}, .{1.95, SDLK_DOWN, false},     // landing into a roll
    .{1.6, SDLK_RIGHT, false},
    .{2.0, SDLK_LEFT, true},                                // run back left
    .{2.4, SDLK_SPACE, true}, .{2.5, SDLK_SPACE, false},    // a short hop
    .{3.0, SDLK_LEFT, false},
    .{3.2, SDLK_DOWN, true}, .{3.5, SDLK_RIGHT, true},      // duck, then crawl
    .{3.8, SDLK_DOWN, false},
    .{5.0, SDLK_E, true}, .{5.05, SDLK_E, false},           // swing at the blob
    .{5.5, SDLK_E, true}, .{5.55, SDLK_E, false},
    .{5.6, SDLK_RIGHT, false},
    .{6.2, SDLK_SPACE, true},                               // jump and keep holding: the landing burrows,
    .{6.9, SDLK_RIGHT, true},                               // travel along the floor,
    .{7.4, SDLK_SPACE, false}, .{7.5, SDLK_RIGHT, false},   // and let go to pop out
    .{9.2, SDLK_LEFT, true}, .{9.3, SDLK_LSHIFT, true},     // sprint left,
    .{9.8, SDLK_SPACE, true}, .{10.0, SDLK_SPACE, false},   // jumping at speed
    .{10.4, SDLK_LSHIFT, false}, .{10.5, SDLK_LEFT, false},
    .{10.8, SDLK_X, true}, .{12.0, SDLK_X, false},          // a look up close
    .{12.4, SDLK_ESCAPE, true}, .{12.45, SDLK_ESCAPE, false}, // and the map (which pauses the demo too)
];

// Pushes the key events due by now.
push_demo_keys :: (from: float, to: float) {
    for DEMO if it.at > from && it.at <= to {
        if demo_pad { press_pad(it.key, it.down); continue; }
        event: SDL_Event;
        event.type = xx ifx it.down then SDL_EventType.KEY_DOWN else .KEY_UP;
        event.key.key = it.key;
        event.key.down = it.down;
        SDL_PushEvent(*event);
    }
}

// `myth demo pad`: the demo through a virtual gamepad instead of the keyboard, the way a real
// one's input arrives (arrows on the left stick, Space South, E West, Shift the right trigger,
// X the stick's click, Escape Start), to show the gamepad drives the game the same.
demo_pad: *SDL_Joystick;

attach_demo_pad :: () {
    // A capture runs without focus (the display may be asleep), and SDL drops pad input then.
    SDL_SetHint(SDL_HINT_JOYSTICK_ALLOW_BACKGROUND_EVENTS, "1");
    desc: SDL_VirtualJoystickDesc;
    desc.version = size_of(SDL_VirtualJoystickDesc);
    desc.type = xx SDL_JoystickType.GAMEPAD;
    desc.naxes = xx SDL_GamepadAxis.COUNT;
    desc.nbuttons = xx SDL_GamepadButton.COUNT;
    desc.button_mask = 0xffff_ffff >> (32 - cast(u32) SDL_GamepadButton.COUNT);
    desc.axis_mask = (1 << cast(u32) SDL_GamepadAxis.COUNT) - 1;
    desc.name = "demo pad";
    id := SDL_AttachVirtualJoystick(*desc);
    demo_pad = SDL_OpenJoystick(id);
    if !demo_pad log_error("no virtual pad: %", to_string(SDL_GetError()));
}

press_pad :: (key: SDL_Keycode, down: bool) {
    FULL :: 32767;
    if key == {
      case SDLK_RIGHT;  SDL_SetJoystickVirtualAxis(demo_pad, xx SDL_GamepadAxis.LEFTX, xx ifx down then FULL else 0);
      case SDLK_LEFT;   SDL_SetJoystickVirtualAxis(demo_pad, xx SDL_GamepadAxis.LEFTX, xx ifx down then -FULL else 0);
      case SDLK_DOWN;   SDL_SetJoystickVirtualAxis(demo_pad, xx SDL_GamepadAxis.LEFTY, xx ifx down then FULL else 0);
      case SDLK_UP;     SDL_SetJoystickVirtualAxis(demo_pad, xx SDL_GamepadAxis.LEFTY, xx ifx down then -FULL else 0);
      case SDLK_LSHIFT; SDL_SetJoystickVirtualAxis(demo_pad, xx SDL_GamepadAxis.RIGHT_TRIGGER, xx ifx down then FULL else 0);
      case SDLK_SPACE;  SDL_SetJoystickVirtualButton(demo_pad, xx SDL_GamepadButton.SOUTH, down);
      case SDLK_E;      SDL_SetJoystickVirtualButton(demo_pad, xx SDL_GamepadButton.WEST, down);
      case SDLK_X;      SDL_SetJoystickVirtualButton(demo_pad, xx SDL_GamepadButton.LEFT_STICK, down);
      case SDLK_ESCAPE; SDL_SetJoystickVirtualButton(demo_pad, xx SDL_GamepadButton.START, down);
    }
}
