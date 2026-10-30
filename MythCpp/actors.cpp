// The actor types: the player, Bud, the blob, lights, pops, and where the player starts.
#include "game.h"

// --- Spawning from the room's json: each type reads the keys that are its fields in PascalCase. ---

static void read(Json j, string_view& v) { if (j.kind == Json::String) v = j.text; }

static void read_actor(Actor& a, Json j) {
    read(j["Id"], a.id);
    read(j["Position"], a.position);
}

bool spawn(Json json) {
    string_view type = json["Type"].as_string();
    if (type == "Blob") {
        if (Blob* b = actors.blobs.add()) { read_actor(*b, json); read(json["Health"], b->health); read(json["JumpTimer"], b->jump_timer); }
    } else if (type == "Light") {
        if (Light* l = actors.lights.add()) {
            read_actor(*l, json);
            read(json["Radius"], l->radius);
            read(json["SpriteName"], l->sprite_name);
            read(json["EnabledByPersistentButtonId"], l->enabled_by_persistent_button_id);
            read(json["LightColor"], l->light_color);
            read(json["Intensity"], l->intensity);
            read(json["Height"], l->height);
            read(json["FlickerFrequency"], l->flicker_frequency);
            read(json["FlickerIntensity"], l->flicker_intensity);
        }
    } else if (type == "PlayerStart") {
        if (PlayerStart* s = actors.player_starts.add()) read_actor(*s, json);
    } else if (type == "Pop") {
        if (Pop* p = actors.pops.add()) { read_actor(*p, json); read(json["SpriteName"], p->sprite_name); read(json["FlipY"], p->flip_y); }
    } else return false;
    return true;
}

// --- The player: Myth's controls driving a Pawn. ---

constexpr float HURT_DURATION = 0.5f;
constexpr float HURT_FRICTION = 200;
constexpr float DEATH_DURATION = 2.2f;
constexpr float ATTACK_ACTIVE_TIME = 0.2f;  // the swing hits for this long
constexpr float HIT_KNOCKBACK = 30;         // what hitting something pushes you back by

static const char* event_names(u8 events) {
    static const char* NAMES[] = {"LANDED", "JUMPED", "WALL_JUMPED", "SKIDDED", "HIT_WALL", "ROLLED", "BURROWED", "DASH_HIT_WALL"};
    char* out = (char*)temp.alloc(128, 1);
    out[0] = 0;
    for (s32 i = 0; i < 8; i++) if (events & (1 << i)) { if (out[0]) strcat(out, " | "); strcat(out, NAMES[i]); }
    return out;
}

// The sounds the body's frame made, as Myth's Pawn and Player make them.
static void sounds(Player& p, u8 events) {
    const char* GROUND_TYPE = "Brick";  // the substance under the player; both of the slice's kits are Brick
    if (events & (JUMPED | WALL_JUMPED)) sound("jump", p.position);
    if (events & LANDED) sound("land", p.position, "Type", GROUND_TYPE);
    if (events & ROLLED) sound("player/roll", p.position);
    if (events & DASH_HIT_WALL) sound("player/dash_wall", p.position);
    if (events & SKIDDED && now() - p.last_skid_sound > 0.5) { sound("player/skid", p.position); p.last_skid_sound = now(); }

    // A footstep on every sixth frame of a running animation, like Myth's AfterStateUpdate.
    Sprite* sprite = get_sprite("player");
    if (!sprite || !p.grounded || (p.animation != "run" && p.animation != "walk" && p.animation != "run_start")) { p.footstep_frame = -1; return; }
    Tag tag = sprite->find_tag(p.animation);
    s32 frame = sprite->frame_at(tag, p.animation_time, p.animation_looping);
    if (frame != p.footstep_frame && (frame - tag.first) % 6 == 0) sound("footstep", p.position, "Type", GROUND_TYPE);
    p.footstep_frame = frame;
}

// Dust kicked up by jumps, skids, wall jumps and hard landings (Player.EmitJumpParticles).
static void dust(Player& p, u8 events, float falling) {
    const float HARD_LANDING_SPEED = 200;
    auto kick = [](Point2 at) { emit("jump", at.vector(), 8, PI / 2, 2.51f, 59 * PI / 180); };
    if (events & JUMPED) kick(p.position);
    if (events & SKIDDED) kick(p.position + Point2{p.facing * 8, 0});
    if (events & WALL_JUMPED) kick(p.position - Point2{p.facing * p.hitbox.w / 2, 0});
    if (events & LANDED && falling >= HARD_LANDING_SPEED) kick(p.position);
}

// The duck attack's spray of plasma along the blade (Player.EmitPlasmaStrike).
static void plasma_strike(Player& p) {
    Vector2 direction{(float)p.facing, 0};
    emit_line("plasma_strike", p.position.vector() + direction * 5 + Vector2{0, -3}, direction, 7, {20.0f * p.facing + p.velocity.x, p.velocity.y * 0.9f});
}

// The swing's reach, in room pixels.
static RectInt attack_box(const Player& p) {
    RectInt box;
    switch (p.attack) {
        case Player::DUCK: box = {3, -6, 13, 6}; break;
        case Player::DOWN: box = {-19, 0, 39, 14}; break;
        case Player::UP:   box = {-12, -27, 24, 12}; break;
        default:           box = {6, -19, 20, 22};
    }
    if (p.facing < 0) box.x = -(box.x + box.w);
    return box + p.position;
}

static void start_attack(Player& p, const PawnInput& input) {
    s32 move_x = move_input(input);
    if (!p.grounded && move_x) p.facing = move_x;
    p.attack = p.ducking ? Player::DUCK : !p.grounded && input.move_y > 0 ? Player::DOWN : input.move_y < 0 ? Player::UP : Player::SIDE;
    p.state = Player::ATTACK;
    p.state_time = 0;
    p.dash_run_armed = false;  // leaving Normal ends a sprint
    sound(p.attack == Player::DUCK ? "sword_crouched" : "sword", p.position);

    // The swipe, centred on what the swing hits and led a little by your motion (Player.AttackState).
    RectInt box = attack_box(p);
    Point2 at = Point2{box.x + box.w / 2, box.y + box.h / 2} + Point2{(s32)(p.velocity.x * 14 * delta), (s32)(p.velocity.y * 9 * delta)};
    p.attack_hit_target = p.attack_blocked = false;
    p.attack_swipe = {};
    switch (p.attack) {
        case Player::DOWN: p.attack_swipe = spawn_pop(at - Point2{0, 8}, "strike_down_sm", p.facing); break;
        case Player::UP:   p.attack_swipe = spawn_pop(at + Point2{0, 12}, "strike_down_sm", p.facing, true); break;
        case Player::SIDE: p.attack_swipe = spawn_pop(at - Point2{17, 0}, "strike_right_sm", p.facing); break;
        case Player::DUCK: plasma_strike(p); break;
    }
}

static string_view attack_animation(const Player& p) {
    return p.attack == Player::DUCK ? "duck_attack" : p.attack == Player::UP ? "attack_up" : "attack";
}

// How long the swing winds up before it can clang: the attack animation's first frame.
static float attack_windup() {
    Sprite* sprite = get_sprite("player");
    return sprite ? sprite->durations[sprite->find_tag("attack").first] / 1000.0f : 0;
}

// The share of a rect's pixels inside solid tiles (Game.GetRoomCollisionOverlapRatio).
static float solid_fraction(RectInt r) {
    if (r.w <= 0 || r.h <= 0) return 0;
    s32 solid = 0;
    for (s32 y = r.y; y < r.y + r.h; y++) for (s32 x = r.x; x < r.x + r.w; x++) solid += overlaps_solid(RectInt{x, y, 1, 1});
    return (float)solid / (r.w * r.h);
}

// Hits whatever the swing's box touches this frame (Player.AttackThisFrame). A down swing that lands
// bounces you up off it and re-arms the burrow; one buried in a wall clangs to a stop.
static void swing(Player& p) {
    const float POGO_SPEED = 125, BLOCK_OVERLAP = 0.5f;
    if (p.attack_blocked) return;
    RectInt box = attack_box(p);
    bool hit_something = false;
    for (Blob& b : actors.blobs) if (box.overlaps(hitbox_rect(b))) {
        p.attack_hit_target = true;
        if (b.hit_cooldown > 0) continue;
        b.hit_cooldown = 0.5f;
        hit_something = true;
        sound("player/sword_hit", p.position, "hit_destination", "flesh");
        hit(b, p);
        Vector2 center{box.x + box.w * 0.5f, box.y + box.h * 0.5f};
        p.velocity += unit(p.position.vector() - center) * HIT_KNOCKBACK;
    }
    for (Bud& b : actors.buds) bud_struck(b, box, p.position.x);
    if (hit_something && p.attack == Player::DOWN) {
        p.velocity.y = -POGO_SPEED;
        p.burrow.needs_grounding = false;
        p.burrow.timer = 0;
    }
    if (!p.attack_hit_target && (p.attack == Player::DUCK || p.state_time >= attack_windup()) && solid_fraction(box) >= BLOCK_OVERLAP) {
        p.attack_blocked = true;
        if (Pop* swipe = actors.pops.get(p.attack_swipe)) actors.pops.remove(swipe);
        sound("player/sword_block", p.position);
    }
}

// Player.HurtState at zero health: the world goes, all but you and your glow.
static void die(Player& p) {
    shake(0.4f);
    actors.buds.clear();
    actors.blobs.clear();
    actors.pops.clear();
    actors.player_starts.clear();
    Light* glow = actors.lights.get(p.light);
    for (Light& l : actors.lights) if (&l != glow) actors.lights.remove(&l);
}

static void hurt(Player& p, const Actor& by) {
    p.hit_cooldown = INVINCIBLE_DURATION;
    hit_stun(0.1f);
    shake(0.1f);
    // The glitch stretches away from what hit you (HurtGlitchAxis).
    Vector2 away = (p.position - by.position).vector();
    if (dot(away, away) <= 0.001f) away = dot(p.velocity, p.velocity) > 0.001f ? p.velocity : Vector2{(float)-p.facing, 0};
    pulse_dither_glitch(away);
    play(p, "hurt");
    s32 dir = sign((float)(p.position.x - by.position.x));
    if (!dir) dir = -p.facing;
    p.velocity = {dir * 100.0f, -80};
    p.health -= HIT_DAMAGE;
    p.state = p.health > 0 ? Player::HURT : Player::DEAD;
    p.state_time = 0;
    if (p.state == Player::DEAD) die(p);
    sound(p.state == Player::DEAD ? "death" : "hurt", p.position);
    bark(Trigger::PLAYER_HURT);
}

// Falling out of the room costs a hit and puts you back on the last ground you stood on.
static void fall_off_cliff(Player& p) {
    hit_stun(0.4f);
    p.position = p.last_safe_ground;
    p.velocity = p.remainder = {};
    hurt(p, p);
    if (p.state != Player::DEAD) p.velocity = {};  // no knockback off the ledge again
}

void update(Player& p) {
    PawnInput input{
        .move_x = control_move_x(),
        .move_y = control_move_y(),
        .jump_pressed = control_jump_pressed(),
        .jump_down = control_jump_down(),
        .dash_pressed = control_dash_pressed(),
        .dash_down = control_dash_down(),
    };
    bool attack_pressed = control_attack_pressed();

    p.state_time += delta;
    if (p.state == Player::NORMAL && attack_pressed) start_attack(p, input);
    float falling = p.velocity.y;  // before this frame's landing stops it
    u8 events = 0;
    switch (p.state) {
    case Player::NORMAL:
        events = pawn_update(p, input, delta);
        if (events & BURROWED) { p.state = Player::BURROW; p.state_time = 0; }
        break;
    case Player::BURROW:
        if (burrow_state(p, input, delta)) {
            p.state = Player::NORMAL;
            p.state_time = 0;
            if (demo) log("%s: POPPED at {%d, %d} vel {%s, %s}", jai_float(demo_time), p.position.x, p.position.y, jai_float(p.velocity.x), jai_float(p.velocity.y));
        }
        break;
    case Player::ATTACK: {
        s32 move_x = move_input(input);
        horizontal_movement(p, move_x, move_x ? input.move_x : 0, delta);
        string_view name = attack_animation(p);
        play(p, name, false);
        if (p.state_time < ATTACK_ACTIVE_TIME) swing(p);
        events = pawn_update(p, input, delta, false);
        Sprite* sprite = get_sprite("player");
        if (!sprite || p.state_time >= sprite->duration(sprite->find_tag(name))) {
            p.state = Player::NORMAL;
            play(p, p.attack == Player::DUCK ? "duck" : "idle");
        }
        break;
    }
    case Player::HURT:
        p.velocity.x = approach(p.velocity.x, 0, HURT_FRICTION * delta);
        events = pawn_update(p, {}, delta, false);
        if (p.state_time >= HURT_DURATION) { p.state = Player::NORMAL; p.state_time = 0; }
        break;
    case Player::DEAD:
        events = pawn_update(p, {}, delta, false);
        if (p.state_time >= DEATH_DURATION) respawn_requested = true;
        break;
    }
    update_burrow_always(p, delta);
    if ((demo || trace) && events)
        log("%s: %s at {%d, %d} vel {%s, %s} anim %.*s", jai_float(demo ? demo_time : (float)now()), event_names(events), p.position.x, p.position.y,
            jai_float(p.velocity.x), jai_float(p.velocity.y), (int)p.animation.size(), p.animation.data());
    // What the rig plays, from the pawn's animation, as Pawn.UpdateRig picks it: a let-go walk idles;
    // a crawl that's stopped, or still sliding out of a run, crouches; sprinting gets the full run,
    // and the sprint once it's really moving; holding jump past the top of a jump with a burrow to
    // be had, the body tips into a dive at the ground.
    const float RIG_DIVE_RELEASE_GRACE = 0.1f, RIG_CRAWL_SPEED_SLACK = 1.15f;
    string_view rig_animation = p.animation;
    p.rig_dive_grace = input.jump_down ? RIG_DIVE_RELEASE_GRACE : max(0.0f, p.rig_dive_grace - delta);
    float speed = fabsf(p.velocity.x);
    if (p.animation == "walk" && move_input(input) == 0) rig_animation = "idle";
    if (p.animation == "crawl" && (move_input(input) == 0 || speed > CRAWL_SPEED * RIG_CRAWL_SPEED_SLACK)) rig_animation = "duck";
    if ((p.animation == "run" || p.animation == "run_start") && dash_running(p) && p.state == Player::NORMAL)
        rig_animation = speed > MAX_GROUND_SPEED * 1.15f ? "sprint" : "jog";
    // The dive eases in, its angle tracking the fall, and lets go once it's mostly out (Pawn.UpdateRig).
    const float RIG_DIVE_FULL_ANGLE_SPEED = 130, RIG_DIVE_ANGLE_RESPONSE = 14, RIG_DIVE_IN_RESPONSE = 10, RIG_DIVE_LAND_RESPONSE = 40, RIG_DIVE_CLIP_OUT = 0.3f;
    bool airborne = p.state == Player::NORMAL && !p.grounded && take_for(p.animation) == "PlayerAir";
    bool diving = airborne && p.velocity.y >= 0 && p.rig_dive_grace > 0 && can_burrow(p);
    if (diving) {
        float angle = clamp(p.velocity.y / RIG_DIVE_FULL_ANGLE_SPEED, -1.0f, 1.0f) * PI / 2;
        p.rig_dive_angle = p.rig_dive_weight < 0.01f ? angle : p.rig_dive_angle + (angle - p.rig_dive_angle) * (1 - expf(-RIG_DIVE_ANGLE_RESPONSE * delta));
    }
    float response = p.grounded ? RIG_DIVE_LAND_RESPONSE : RIG_DIVE_IN_RESPONSE;
    p.rig_dive_weight += ((diving ? 1.0f : 0.0f) - p.rig_dive_weight) * (1 - expf(-response * delta));
    p.rig_dive_clip = airborne && (diving || (p.rig_dive_clip && p.rig_dive_weight > RIG_DIVE_CLIP_OUT));
    if (p.rig_dive_clip) rig_animation = "dive";
    // A swing in the air keeps the air legs and, up or down, rolls the torso into it (UpdateRigAirLegs).
    bool was_air_swing = p.rig.air_swing;
    p.rig.air_swing = p.state == Player::ATTACK && !p.grounded;
    p.rig.attack_pitch = p.attack == Player::DOWN ? 1 : p.attack == Player::UP ? -1 : 0;
    if (p.rig.air_swing && !was_air_swing) { p.rig.leg_weight = 1; if (p.attack == Player::DOWN) p.rig.attack_reach = 1; }
    p.rig.arm_aim = {p.facing * cosf(p.rig_dive_angle), sinf(p.rig_dive_angle)};
    p.rig.arm_aim_weight = p.rig_dive_weight;
    play_rig(p.rig, rig_animation, p.velocity, delta, p.state == Player::ATTACK && p.state_time <= delta, p.facing,
        p.state == Player::NORMAL && !input.move_x && !input.move_y && !input.jump_down && !input.dash_down && !attack_pressed);
    update_rig(p.rig, p, delta, p.grounded);
    sounds(p, events);
    dust(p, events, falling);
    if (events & LANDED) land_rig(p.rig, falling * 0.5f);  // Player.OnLanded's RigLandingScale

    if (p.grounded && p.state == Player::NORMAL) p.last_safe_ground = p.position;
    if (p.state == Player::BURROW) p.grounded = false;  // a point on the surface, not standing on it
    // Player.CheckRoomBounds: well below the room is a cliff fall; at the other edges, where Myth
    // would move on to the next room, the player is held.
    Point2 size = room.size * TILE_SIZE;
    if (p.position.y > size.y + 12) { if (p.state != Player::DEAD) fall_off_cliff(p); }
    else p.position = {clamp(p.position.x, 0, size.x), max(p.position.y, 0)};

    if (Light* light = actors.lights.get(p.light)) { light->position = p.position + Point2{0, -6}; burrow_light(p, p.state == Player::BURROW, *light, delta); }

    // Touching an enemy hurts, except underground.
    if (p.hit_cooldown <= 0 && p.state != Player::DEAD && p.state != Player::BURROW)
        for (Blob& b : actors.blobs) if (hitbox_rect(p).overlaps(hitbox_rect(b))) { hurt(p, b); break; }
}

static bool blinking(const Player& p) { return p.hit_cooldown > 0 && p.state != Player::DEAD && (s64)(now() * 20) % 2; }  // i-frames

void draw_normals(Player& p) {
    if (burrow_hides(p, p.state == Player::BURROW) || blinking(p)) return;
    draw_rig_normals(p.rig, p.position.vector(), PLAYER_LOOK);
}

void draw(Player& p) {
    if (burrow_hides(p, p.state == Player::BURROW) || blinking(p)) return;
    if (!p.rig.posed) { draw_sprite(p, "player"); return; }
    draw_rig(p.rig, p.position.vector(), PLAYER_LOOK);
    // The hat sits on the rig's head (Player.RenderHat, RigHatPosition).
    float radius;
    Vector2 head = rig_head(p.rig, &radius);
    if (Sprite* hat = get_sprite("hat")) {
        Vector2 origin{floorf(hat->frame_size.x * 0.5f), hat->frame_size.y};
        push_matrix(p.position.vector() + head - Vector2{0, radius} + Vector2{0, 1}, {(float)p.facing, 1});
        draw_frame(*hat, 0, -origin);
        pop_matrix();
    }
}

Player* spawn_player(Point2 at) {
    Player* p = actors.players.add();
    if (!p) return nullptr;
    p->position = at;
    p->last_safe_ground = at;
    // Assets/Actors/player-glow-light.json
    if (Light* light = actors.lights.add()) {
        p->light = actors.lights.handle_of(light);
        light->position = at;
        light->radius = 36;
        light->intensity = 0.45f;
        light->light_color = Color::rgb(0xffcfa0);
        light->height = 16;
        light->flicker_frequency = 1.5f;
        light->flicker_intensity = 0.02f;
        light->sprite_name = "";
    }
    p->hitbox = STANDING_HITBOX;
    p->burrows = true;
    play(*p, "idle");
    start_roll(*p);  // Assets/Actors/player.json starts the player in the Roll state
    return p;
}

// --- Bud, the companion who follows the player around, drawn with the humanoid rig in his own
// palette. Myth steers him with ReachPilot over a route field (GameAI/); until that's ported, he
// walks toward the player, waits at a drop into nothing, and jumps when a wall's in the way or the
// player is above him. Falling out anyway takes him out of play for a few seconds, then he turns up
// where he came in (Bud.BeginFallRespawn). Near the player when they burrow, he rides along out of
// sight and pops out just after them, the same way (TryEnterPlayerBurrow). Caught by the player's
// swing, he rounds on them and hops back out of reach (StartBackDash). ---

constexpr s32 BUD_FOLLOW_GAP = 11;
constexpr float BUD_FALL_RESPAWN_DELAY = 3;
constexpr float BUD_BURROW_RIDE_RADIUS = 14;
constexpr float BUD_BURROW_POP_DELAY = 0.25f;
constexpr float BUD_BACK_DASH_SPEED = 120;
constexpr float BUD_BACK_DASH_DURATION = 0.22f;
constexpr float BUD_BACK_DASH_FRICTION = 260;
constexpr float BUD_BACK_DASH_COOLDOWN = 0.6f;

// Any floor under room pixel x, from y down to the bottom of the room.
static bool ground_below(s32 x, s32 y) { return overlaps_solid(RectInt{x, y, 1, room.size.y * TILE_SIZE - y}); }

void update(Bud& b) {
    if (b.respawn_timer > 0) {
        b.respawn_timer -= delta;
        if (b.respawn_timer <= 0) { b.position = b.entry; b.velocity = b.remainder = {}; }
        return;
    }
    if (b.riding) { for (Player& player : actors.players) b.position = player.position; return; }
    if (b.pop_delay > 0) {
        b.pop_delay -= delta;
        if (b.pop_delay > 0) return;
        b.position = b.pop_at;
        b.velocity = b.pop_velocity;
        b.squish = b.pop_squish;
        b.facing = b.pop_facing;
        spawn_pop(b.position + Point2{0, -6});
        b.remainder = {};
        b.jump_timer = JUMP_TIME * 0.9f;
        b.jump_cut_immune = true;
    }
    b.back_dash_cooldown = approach(b.back_dash_cooldown, 0, delta);
    PawnInput input;
    if (b.back_dash_timer > 0) {
        b.back_dash_timer = approach(b.back_dash_timer, 0, delta);
        b.velocity.x = approach(b.velocity.x, 0, BUD_BACK_DASH_FRICTION * delta);
        pawn_update(b, input, delta, false);
        if (b.back_dash_timer <= 0) play(b, "idle");
        play_rig(b.rig, b.animation, b.velocity, delta, false, b.facing, b.velocity.x == 0);
        update_rig(b.rig, b, delta, b.grounded);
        return;
    }
    for (Player& player : actors.players) {
        // His goal is the player's body widened by a gap either side (Game's budRoute); outside it he
        // walks to its near edge, easing in as he gets there (ReachPilot.Arrive).
        const float ARRIVE_GAIN = 8, ARRIVE_MIN_SPEED = 10;
        s32 dx = player.position.x - b.position.x;
        s32 reach = player.hitbox.w / 2 + BUD_FOLLOW_GAP;
        if (abs(dx) > reach) {
            float to_edge = dx - sign((float)dx) * reach;
            float speed = clamp(fabsf(to_edge) * ARRIVE_GAIN, ARRIVE_MIN_SPEED, MAX_GROUND_SPEED);
            input.move_x = sign(to_edge) * min(1.0f, speed / MAX_GROUND_SPEED);
        } else if (dx) b.facing = sign((float)dx);
        bool wants_up = player.position.y < b.position.y - 12 && abs(dx) < 48;
        input.jump_pressed = b.grounded && (b.blocked_time > 0.1f || wants_up);
        input.jump_down = !b.grounded && b.velocity.y < 0;
        if (b.grounded && input.move_x != 0 && !ground_below(b.position.x + sign(input.move_x) * (b.hitbox.w / 2 + 2), b.position.y)) input.move_x = 0;
    }
    u8 events = pawn_update(b, input, delta);
    b.blocked_time = events & HIT_WALL ? b.blocked_time + delta : 0;
    if (b.position.y > room.size.y * TILE_SIZE + 12) b.respawn_timer = BUD_FALL_RESPAWN_DELAY;
    play_rig(b.rig, b.animation, b.velocity, delta, false, b.facing, b.velocity.x == 0);
    update_rig(b.rig, b, delta, b.grounded);
}

static bool bud_shown(const Bud& b) { return b.respawn_timer <= 0 && !b.riding && b.pop_delay <= 0; }
void draw(Bud& b) { if (bud_shown(b)) draw_rig(b.rig, b.position.vector(), BUD_LOOK); }
void draw_normals(Bud& b) { if (bud_shown(b)) draw_rig_normals(b.rig, b.position.vector(), BUD_LOOK); }

void bud_struck(Bud& b, RectInt swing, s32 attacker_x) {
    if (!bud_shown(b) || b.back_dash_timer > 0 || b.back_dash_cooldown > 0) return;
    if (!swing.overlaps(hitbox_rect(b))) return;
    if (s32 toward = sign((float)(attacker_x - b.position.x))) b.facing = toward;
    b.back_dash_timer = BUD_BACK_DASH_DURATION;
    b.back_dash_cooldown = BUD_BACK_DASH_COOLDOWN;
    b.velocity.x = -b.facing * BUD_BACK_DASH_SPEED;
    b.jump_timer = 0;
    b.squish = {0.75f, 1.3f};
    play(b, "back_dash", false);
    sound("hurt", b.position);
}

bool bud_rides_burrow(Bud& b, Pawn& player) {
    if (!bud_shown(b) || (b.position - player.position).vector().length() > BUD_BURROW_RIDE_RADIUS) return false;
    b.riding = true;
    b.velocity = {};
    spawn_pop(b.position + Point2{0, -6});
    return true;
}

void bud_pop_from_burrow(Bud& b, Pawn& player) {
    if (!b.riding) return;
    b.riding = false;
    b.pop_delay = BUD_BURROW_POP_DELAY;
    b.pop_at = player.position;
    b.pop_velocity = player.velocity;
    b.pop_squish = player.squish;
    b.pop_facing = player.facing;
}

// Next to the player, behind them if there's room (Game.FindBudSpawnPosition).
void spawn_bud(Player& player) {
    Bud* b = actors.buds.add();
    if (!b) return;
    b->hitbox = STANDING_HITBOX;
    play(*b, "idle");
    b->facing = player.facing;
    b->position = player.position;
    for (s32 x : {-12, 12, -20, 20}) {
        Point2 at = player.position + Point2{x * player.facing, 0};
        if (!overlaps_solid(b->hitbox + at)) { b->position = at; break; }
    }
    b->entry = b->position;
}

// --- A hopping slime: waits, then jumps at the player. Two hits kill it. ---

static bool chance(float p) { return random01() < p; }

void update(Blob& b) {
    if (!b.started) {  // Myth's Added: stagger the first jump
        b.started = true;
        b.hitbox = {-4, -8, 8, 8};
        b.jump_timer = max(b.jump_timer - random_range(0, 1), 0.0f);
        play(b, "idle");
    }
    move(b, b.velocity * delta);
    if (fell_out(b)) { actors.blobs.remove(&b); return; }
    advance(b, delta);
    b.velocity.y += 300 * delta;

    if (grounded(b)) {
        if (!b.grounded) {
            play(b, "idle");
            b.squish = {1.5f, 0.5f};
            sound("blob/land", b.position);
        }
        b.velocity.x = approach(b.velocity.x, 0, 400 * delta);
        b.jump_timer -= delta;
        b.grounded = true;
    } else {
        b.grounded = false;
    }

    if (b.jump_timer <= 0) {
        play(b, "jump");
        sound("blob/jump", b.position);
        b.jump_timer = chance(0.4f) ? random_range(2.4f, 2.9f) : random_range(1.5f, 2.1f);
        b.velocity.y = chance(0.2f) ? -130 : -90;
        b.squish = {0.5f, 1.5f};
        for (Player& p : actors.players) {
            b.facing = sign((float)(p.position.x - b.position.x));
            if (!b.facing) b.facing = 1;
            b.velocity.x = b.facing * 40.0f;
        }
    }
}

void hit(Blob& b, Actor& by) {
    b.health -= 1;
    hit_stun(0.09f);
    b.squish = {0.5f, 1};
    if (b.health <= 0) { spawn_pop(b.position + Point2{0, -4}); actors.blobs.remove(&b); bark(Trigger::ENEMY_KILLED); return; }
    b.velocity.x = sign((float)(b.position.x - by.position.x)) * 120.0f;
}

void draw(Blob& b) { draw_sprite(b, "blob"); }
void draw_normals(Blob& b) { draw_sprite_normals(b, "blob"); }

// --- Lights: their sprite is emissive, drawn over the lit scene, not lit by it. ---

void update(Light& l) {
    if (!l.flicker_offset) l.flicker_offset = random_range(0.001f, 3);
    advance(l, delta);
    play(l, "idle");
}

void draw_emissive(Light& l) { if (!l.sprite_name.empty()) draw_sprite(l, l.sprite_name); }

// Breathing: radius and intensity both scale by this, as in Myth's Light.RenderScale.
float flicker_scale(const Light& l) {
    float flicker = 0;
    if (l.flicker_frequency > 0) {
        float t = l.animation_time;
        flicker = 0.7f * sinf(t * l.flicker_frequency * 0.23f * TAU + l.flicker_offset * 1.7f) + 0.3f * sinf(t * l.flicker_frequency * TAU + l.flicker_offset);
    }
    return (1 + l.flicker_intensity) * (1 + flicker * l.flicker_intensity);
}

// --- A one-shot sprite, a puff or a sword's swipe, that plays its "pop" animation once and is gone (Pop.cs). ---

void update(Pop& p) {
    p.animation_time += delta;
    Sprite* sprite = get_sprite(p.sprite_name);
    if (!sprite || p.animation_time >= sprite->duration(sprite->find_tag("pop"))) actors.pops.remove(&p);
}

void draw(Pop& p) {
    if (Sprite* sprite = get_sprite(p.sprite_name))
        draw_sprite(*sprite, sprite->find_tag("pop"), p.animation_time, p.position.vector(), {(float)p.facing, p.flip_y ? -1.0f : 1.0f}, false);
}

Handle<Pop> spawn_pop(Point2 at, string_view sprite_name, s32 facing, bool flip_y) {
    Pop* p = actors.pops.add();
    if (!p) return {};
    p->position = at;
    p->sprite_name = sprite_name;
    p->facing = facing;
    p->flip_y = flip_y;
    return actors.pops.handle_of(p);
}
