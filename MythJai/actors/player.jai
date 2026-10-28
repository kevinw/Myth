// The player: Myth's controls (controls.jai) driving a Pawn.

Player :: struct @GameActor {
    #as using pawn: Pawn;
    health := MAX_HEALTH;
    state: enum u8 { NORMAL; ATTACK; HURT; DEAD; BURROW; };
    state_time: float;
    attack: enum u8 { SIDE; DOWN; UP; DUCK; };
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
        if p.state_time >= DEATH_DURATION respawn(p);
    }
    update_burrow_always(p, delta);
    if demo && events log("%: % at % vel % anim %", demo_time, events, p.position, p.velocity, p.animation);
    // Sprinting gets the full run, and the sprint once it's really moving (Pawn.RigClipFor).
    rig_animation := p.animation;
    if (p.animation == "run" || p.animation == "run_start") && dash_running(p) && p.state == .NORMAL
        rig_animation = ifx abs(p.velocity.x) > MAX_GROUND_SPEED * 1.15 then "sprint" else "jog";
    play_rig(*p.rig, rig_animation, restart = p.state == .ATTACK && p.state_time <= delta);
    update_rig(*p.rig, p, delta);
    sounds(p, events);
    dust(p, events, falling);

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
    if p.attack == {
      case .DOWN; spawn_pop(at - Point2.{0, 8}, "strike_down_sm", p.facing);
      case .UP;   spawn_pop(at + Point2.{0, 12}, "strike_down_sm", p.facing, flip_y = true);
      case .SIDE; spawn_pop(at - Point2.{17, 0}, "strike_right_sm", p.facing);
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

// Hits whatever the swing's box touches this frame.
swing :: (p: *Player) {
    box := attack_box(p);
    for context.actors.blobs if it.hit_cooldown <= 0 && overlaps(box, hitbox_rect(it)) {
        it.hit_cooldown = 0.5;
        sound("player/sword_hit", p.position, "hit_destination", "flesh");
        hit(it, p);
        center := Vector2.{box.x + box.w * 0.5, box.y + box.h * 0.5};
        p.velocity += unit_vector(to_vector(p.position) - center) * HIT_KNOCKBACK;
    }
}

hurt :: (p: *Player, by: *Actor) {
    p.hit_cooldown = INVINCIBLE_DURATION;
    hit_stun(0.1);
    shake(0.1);
    play(p, "hurt");
    dir := sign(xx (p.position.x - by.position.x));
    if !dir dir = -p.facing;
    p.velocity = .{dir * 100.0, -80};
    p.health -= HIT_DAMAGE;
    p.state = ifx p.health > 0 then .HURT else .DEAD;
    p.state_time = 0;
    if p.state == .DEAD shake(0.4);
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

respawn :: (p: *Player) {
    p.position = start_position();
    p.velocity = .{};
    p.health = MAX_HEALTH;
    p.state = .NORMAL;
    p.hit_cooldown = INVINCIBLE_DURATION;
    play(p, "idle");
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
];

// Pushes the key events due by now.
push_demo_keys :: (from: float, to: float) {
    for DEMO if it.at > from && it.at <= to {
        event: SDL_Event;
        event.type = xx ifx it.down then SDL_EventType.KEY_DOWN else .KEY_UP;
        event.key.key = it.key;
        event.key.down = it.down;
        SDL_PushEvent(*event);
    }
}
