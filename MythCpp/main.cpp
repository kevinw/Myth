// Human Questions (Myth), in C++ on CppKit: the frame, the camera, and the passes that draw it.
#include "game.h"
#include "live.h"
#include <cstdarg>

static const char* ROOM = "Rooms/Myth/super_simple.json";

Room room;
Pack pack;
Actors actors;
float delta;                    // seconds this frame
float zoom = 1;                 // view pixels per room pixel: 2 while Zoom is held, eased (Game.UpdateCamera)
bool respawn_requested;
bool demo, trace;               // `myth demo`: the scripted run; `myth trace`: log what the player does
float demo_time;
List<SDL_Keycode> held{.arena = &persist};

static Texture scene;           // the background (backdrop, back tiles), unlit, at the view's resolution
static Texture front;           // the foreground (tiles, actors) over it, unlit
static Texture tile_field;      // which dither pattern lights each pixel (draw_tile_field)
static Texture actor_mask;      // r: actors that cast sprite shadows; g: background tiles; b: the player and enemies, barely dithered
static Texture normals;         // its normals: rgb a direction, a how much it bends the light
static Texture lit;             // the world lit, before the CRT pass scales it to the window
static Texture no_tile_field;
static Shader lighting, crt;    // crt: Myth's own Assets/Shaders/Crt.hlsl
static Vector2 camera;          // top-left of the view, in room pixels
static float glitch, glitch_target;  // the dither glitch (0..1) a hurt pulses, and the way it stretches
static Vector2 glitch_axis{1, 0};
static List<SDL_Keycode> pressed{.arena = &persist};  // this frame
static u64 last_ticks;
static float hit_stun_timer;    // the world freezes briefly on a hit
static float shake_timer;
static double next_sprite_check;
static bool show_light_mask;    // `myth lightmask`: the background's light alone, like Myth's LightMask view
static const char* shot_path;   // `myth shot <path> [frame]`: save the lit view there at that frame (default 90) and quit
static s32 shot_frame = 90;
static u64 last_frame_ticks;
static s32 frame_count;
static SDL_Joystick* demo_pad;  // `myth demo pad`: the demo through a virtual gamepad

static void populate_room();
static void restart_room();
static void render();
static void update_camera();
static Vector2 clamp_camera(Vector2 target);
static Vector2 camera_target();
static void respawn_after_death();
static void push_demo_keys(float from, float to);
static void attach_demo_pad();

const char* asset(const char* format, ...) {
    char path[512];
    va_list args;
    va_start(args, format);
    vsnprintf(path, sizeof(path), format, args);
    va_end(args);
    return tprint("%s../../Myth/Assets/%s", SDL_GetBasePath(), path);
}

void log(const char* format, ...) {
    va_list args;
    va_start(args, format);
    vprintf(format, args);
    va_end(args);
    putchar('\n');
    fflush(stdout);
}

const char* jai_float(float f) {
    char* text = (char*)tprint("%.6f", f);
    char* end = text + strlen(text) - 1;
    while (*end == '0') *end-- = 0;
    if (*end == '.') *end = 0;
    return text;
}

// Jai's Random, so the same moments roll the same numbers: a 128-bit multiplicative step from its
// default state, the high half out, 24 bits of it to a float.
static unsigned __int128 rng_state = 1;
u64 random_u64() {
    rng_state *= (unsigned __int128)0x0fc94e3bf4e9ab32ull << 64 | 0x866458cd56f5e605ull;
    return u64(rng_state >> 64);
}
float random01() { return float(random_u64() & 0xffffff) / 0xffffff; }
float random_range(float lo, float hi) { return clamp(lo + random01() * (hi - lo), lo, hi); }

double now() { return SDL_GetTicksNS() / 1e9; }
bool key_down(SDL_Keycode key) { return held.contains(key); }
bool key_pressed(SDL_Keycode key) { return pressed.contains(key); }
void hit_stun(float seconds) { hit_stun_timer = max(hit_stun_timer, seconds); }
void shake(float seconds) { shake_timer = max(shake_timer, seconds); }

void pulse_dither_glitch(Vector2 axis, float amount) {
    if (dot(axis, axis) > 0.001f) glitch_axis = unit(axis);
    glitch_target = amount;
}

// What a reload that changes these layouts can't carry over: the room restarts instead.
#define X(Type, pool, capacity) , Type
static u64 state_layout() { return layout_of<Actors, Room, Pack ACTOR_TYPES(X)>(); }
#undef X

static int argc;
static char** argv;
const char* arg_after(const char* name) {
    for (int i = 1; i + 1 < argc; i++) if (string_view(argv[i]) == name) return argv[i + 1];
    return nullptr;
}

static bool start() {
    if (!SDL_Init(SDL_INIT_VIDEO | SDL_INIT_GAMEPAD)) { log("SDL_Init: %s", SDL_GetError()); return false; }
    SDL_Window* window = SDL_CreateWindow("Human Questions", 1280, 720, SDL_WINDOW_RESIZABLE | SDL_WINDOW_HIGH_PIXEL_DENSITY);
    if (!window || !gfx_init(window)) { log("%s", SDL_GetError()); return false; }
    auto loaded_room = load_room(asset("%s", ROOM));
    if (!loaded_room) { log("can't load %s", ROOM); return false; }
    room = *loaded_room;
    auto loaded_pack = load_pack();
    if (!loaded_pack) { log("can't load the visual pack"); return false; }
    pack = *loaded_pack;
    build_tile_draws();
    populate_room();
    for (int i = 1; i < argc; i++) {
        string_view arg = argv[i];
        if (arg == "demo") demo = true;
        if (arg == "trace") trace = true;
        if (arg == "lightmask") show_light_mask = true;
        if (arg == "shot" && i + 1 < argc) shot_path = argv[i + 1];
        if (arg == "shot" && i + 2 < argc) shot_frame = atoi(argv[i + 2]);
        if (arg == "pad" && demo) attach_demo_pad();
    }
    render_offscreen = shot_path;  // a capture runs whether or not the window can show
    camera = clamp_camera(camera_target());
    load_story();
    live_init();
    return true;
}

// One frame; true to quit.
static bool frame() {
    temp.reset();
    pressed.count = 0;
    begin_control_frame();
    for (SDL_Event event; SDL_PollEvent(&event);) {
        control_event(event);
        if (event.type == SDL_EVENT_QUIT) return true;
        if (event.type == SDL_EVENT_KEY_DOWN && !event.key.repeat) {
            pressed.add(event.key.key);
            held.add_unique(event.key.key);
        }
        if (event.type == SDL_EVENT_KEY_UP) held.remove_unordered(event.key.key);
    }
    update_controls();
    if (control_map_pressed() || (map_open && control_face_pressed())) toggle_map();

    u64 ticks = SDL_GetTicksNS();
    delta = last_ticks ? min((ticks - last_ticks) / 1e9f, 1.0f / 20) : 1.0f / 60;
    if (shot_path) delta = 1.0f / 60;  // captures step evenly, so a frame number is a moment
    if (shot_path && trace) {           // ...at real speed, for input from outside
        u64 elapsed = SDL_GetTicksNS() - last_frame_ticks;
        if (elapsed < 16666667) SDL_DelayNS(16666667 - elapsed);
        last_frame_ticks = SDL_GetTicksNS();
    }
    last_ticks = ticks;

    if (now() > next_sprite_check) {
        next_sprite_check = now() + 0.25;
        reload_changed_sprites();
    }
    shake_timer = approach(shake_timer, 0, delta);
    if (map_open) {
        // Paused under the map.
    } else if (hit_stun_timer > 0) {
        hit_stun_timer -= delta;
    } else {
        if (demo) { push_demo_keys(demo_time, demo_time + delta); demo_time += delta; }
#define X(Type, pool, capacity) for (Type& it : actors.pool) update(it);
        ACTOR_TYPES(X)
#undef X
        if (respawn_requested) respawn_after_death();
        update_particles(delta);
        update_story();
    }
    // Game.Update's dither glitch: quick to rise to a pulse, slow to settle.
    glitch = approach(glitch, glitch_target, delta * (glitch_target > glitch ? 15.0f : 1.35f));
    if (glitch_target > 0 && glitch >= glitch_target) glitch_target = 0;
    const float ZOOM_RATE = 12;
    float zoom_target = control_zoom_down() ? 2 : 1;
    zoom += (zoom_target - zoom) * (1 - expf(-delta * ZOOM_RATE));
    if (fabsf(zoom - zoom_target) < 0.005f) zoom = zoom_target;
    update_camera();
    update_sound(camera + VIEW * 0.5f);
    render();
    frame_count++;
    if (shot_path && frame_count == shot_frame) {
        if (demo || trace) for (Player& p : actors.players)
            log("shot at demo %s: player %s at {%d, %d} health %d zoom %s glitch %s", jai_float(demo_time), (const char*[]){"NORMAL", "ATTACK", "HURT", "DEAD", "BURROW"}[p.state],
                p.position.x, p.position.y, p.health, jai_float(zoom), jai_float(glitch));
        if (!save_png(lit, shot_path)) log("can't write %s", shot_path);
        return true;
    }
    return false;
}

int main(int count, char** values) {
    argc = count;
    argv = values;
    if (!start()) return 1;
    for (;;) {
        if (live_update(state_layout) == Reload::Layout) restart_room();
        if (frame()) return 0;
    }
}

struct LightingUniforms {
    Vector4 view, ambient, noise, noise_scale, response, dither, counts, normal_light, sprite_shadow, glitch, glitch_axis;
    Vector4 lights[MAX_LIGHTS * 2];
};

static LightingUniforms lighting_uniforms(Vector2 view) {
    // Myth's defaults: Manager.Visuals and Room.
    const float LIGHT_NOISE = 0.30f, LIGHT_NOISE_PIVOT = 0.48f;
    const Vector2 LIGHT_NOISE_DRIFT{5, 0};  // world pixels per second
    const float BACKGROUND_EXPOSURE = 1, RIM_STRENGTH = 0.18f, SHOULDER_KNEE = 0.9f, SHOULDER_CEILING = 1.3f;
    const float DITHER_STRENGTH = 0.155f, DITHER_SCALE = 1;
    const float NORMAL_LIGHT_FLOOR = 0.12f, NORMAL_LIGHT_GAIN = 2.15f;
    const float SHADOW_BIAS = 0.5f, SHADOW_SOURCE_RADIUS = 2.3f;
    const float DITHER_GLITCH_RADIUS = 150;
    const float SPRITE_SHADOW_STRENGTH = 0.5f, SPRITE_SHADOW_MIN_LENGTH = 4, SPRITE_SHADOW_MAX_LENGTH = 28;

    LightingUniforms u{};
    u.view = {VIEW.x, VIEW.y, view.x, view.y};
    float k = room.ambient_intensity / 255;
    u.ambient = {room.ambient.r * k, room.ambient.g * k, room.ambient.b * k, room.ambient_intensity};
    Vector2 noise_size = pack.lighting_noise.size;
    if (noise_size.x > 0) {
        Vector2 drift = LIGHT_NOISE_DRIFT * (float)now() / noise_size;
        u.noise = {LIGHT_NOISE, LIGHT_NOISE_PIVOT, drift.x - floorf(drift.x), drift.y - floorf(drift.y)};
        u.noise_scale = {1 / noise_size.x, 1 / noise_size.y, 0, 0};
    }
    u.response = {BACKGROUND_EXPOSURE, RIM_STRENGTH, SHOULDER_KNEE, SHOULDER_CEILING};
    u.dither = {DITHER_STRENGTH, DITHER_SCALE, 0, 0};
    u.normal_light = {NORMAL_LIGHT_FLOOR, NORMAL_LIGHT_GAIN, 0, 0};
    u.sprite_shadow = {SPRITE_SHADOW_STRENGTH, SPRITE_SHADOW_MIN_LENGTH, SPRITE_SHADOW_MAX_LENGTH, 0};
    for (Player& p : actors.players) { u.glitch = {(float)p.position.x, (float)p.position.y, DITHER_GLITCH_RADIUS, (float)now()}; break; }
    u.glitch_axis = {glitch_axis.x, glitch_axis.y, glitch, 0};
    s32 count = 0;
    Vector2 positions[MAX_LIGHTS];
    for (Light& l : actors.lights) {
        if (count == MAX_LIGHTS || l.intensity <= 0) continue;
        float scale = flicker_scale(l);
        float c = l.intensity * scale / 255;
        // The soft shadow's penumbra breathes with the flicker (Light.RenderShadowSourceRadius).
        float source_radius = SHADOW_SOURCE_RADIUS * (1 + (scale / (1 + l.flicker_intensity) - 1) * 0.45f);
        positions[count] = l.position.vector();
        u.lights[count * 2] = {positions[count].x, positions[count].y, l.radius * scale, l.height};
        u.lights[count * 2 + 1] = {l.light_color.r * c, l.light_color.g * c, l.light_color.b * c, source_radius};
        count++;
    }
    update_shadow_depths({positions, (size_t)count});
    u.counts = {(float)count, SHADOW_BIAS, (float)show_light_mask, zoom};
    return u;
}

static std::span<const u8> bytes_of(const auto& value) { return {(const u8*)&value, sizeof(value)}; }

static void render() {
    if (!scene.gpu) {
        for (Texture* t : {&scene, &front, &tile_field, &actor_mask, &normals, &lit}) *t = make_target((s32)VIEW.x, (s32)VIEW.y);
        Color clear{};
        no_tile_field = make_texture(&clear, 1, 1);
        crt = load_shader("Crt", 2, 1).value_or(Shader{});
        lighting = load_shader("lighting", 7, 1).value_or(Shader{});
    }

    Vector2 size = screen_size();
    float scale = max(1.0f, floorf(min(size.x / VIEW.x, size.y / VIEW.y)));
    Vector2 origin = ((size - VIEW * scale) * 0.5f).floor();
    Vector2 view = camera;
    if (shake_timer > 0) view += Vector2{random_range(-1, 1), random_range(-1, 1)} * 2;
    view = view.floor();
    auto world = [&] { push_matrix(-view * zoom, zoom); };

    // The world, unlit: the background, and the foreground over a transparent clear.
    begin_pass({.target = scene, .clear = room.backdrop});
    world();
    draw_tiles(background_tiles.span());
    pop_matrix();
    begin_pass({.target = front});
    world();
    draw_tiles(foreground_tiles.span());
#define X(Type, pool, capacity) for (Type& it : actors.pool) draw(it);
    ACTOR_TYPES(X)
#undef X
    pop_matrix();

    // The tiles' and rigs' normals. Elsewhere it's flat and weightless (Myth's NormalBuffer clear).
    begin_pass({.target = normals, .clear = {128, 128, 255, 0}});
    world();
    draw_tiles(background_tiles.span(), true);
    draw_tiles(foreground_tiles.span(), true);
    for (Player& it : actors.players) draw_normals(it);
    for (Bud& it : actors.buds) draw_normals(it);
    for (Blob& it : actors.blobs) draw_normals(it);
    pop_matrix();

    begin_pass({.target = tile_field});
    world();
    draw_tile_field();
    pop_matrix();

    // r: what casts sprite shadows (not the player or Bud: their glow would shadow itself, and
    // Myth leaves light emitters out); g: background tiles, where ambient occlusion lands;
    // b: what's barely dithered (not Bud).
    begin_pass({.target = actor_mask});
    world();
    silhouette = {0, 255, 0, 255};
    draw_tiles(background_tiles.span());
    silhouette = {0, 0, 255, 255};
    for (Player& it : actors.players) draw(it);
    silhouette = {255, 0, 255, 255};
    for (Blob& it : actors.blobs) draw(it);
    silhouette = {};
    pop_matrix();

    // Lit.
    LightingUniforms uniforms = lighting_uniforms(view);
    Texture extra[] = {pack.lighting_noise, normals, front, tile_field, shadow_depths, actor_mask};
    begin_pass({.target = lit, .clear = {0, 0, 0, 255}, .shader = &lighting, .uniforms = bytes_of(uniforms), .extra = extra});
    image(scene, {{}, VIEW}, {});

    apply_pixel_sort(lit, view, delta);

    // Unlit on top: what glows.
    begin_pass({.target = lit, .keep = true});
    world();
    for (Light& it : actors.lights) draw_emissive(it);
    draw_particles();
    for (Player& it : actors.players) draw_burrow_glow(it, it.state == Player::BURROW);
    pop_matrix();
    draw_speech(view);
    draw_map();

    // Through Myth's CRT filter onto the window, with Myth's default settings (Manager.Visuals).
    Vector2 output = VIEW * scale;
    float crt_uniforms[] = {output.x, output.y, 0.09f, 0.06f, 1.5f, 0, 0.35f, 0.1f, 1, scale, 0, 0.06f, 1};
    Texture crt_extra[] = {no_tile_field};
    begin_pass({.clear = {0, 0, 0, 255}, .shader = &crt, .uniforms = bytes_of(crt_uniforms), .extra = crt_extra});
    push_matrix(origin, scale);
    image(lit, {{}, VIEW}, {});

    begin_pass({.keep = true});
    draw_hud();
    pop_matrix();
    present();
}

static Point2 start_position() {
    for (PlayerStart& s : actors.player_starts) return s.position;
    return {};
}

// The room's actors as its json has them, the player at its start (rolling in, as player.json
// starts it) and Bud beside them.
static void populate_room() {
    for (Json& a : room.actors) if (!spawn(a)) log("no actor type %.*s", (int)a["Type"].as_string().size(), a["Type"].as_string().data());
    if (Player* p = spawn_player(start_position())) spawn_bud(*p);
}

// After a reload that changed the state's layout: the actors as the room has them, fresh. A function
// of its own so it runs the reloaded code on the reloaded globals; main still runs the code it started with.
static void restart_room() {
    log("state layout changed: restarting the room");
    actors = {};
    populate_room();
}

// Game.RespawnPlayerAtLastSafeGrounded after a death: the room reloads, everything back in its
// place, the player at its start, whole again and on their feet.
static void respawn_after_death() {
    respawn_requested = false;
#define X(Type, pool, capacity) actors.pool.clear();
    ACTOR_TYPES(X)
#undef X
    populate_room();
    for (Player& p : actors.players) {
        p.rolling = p.ducking = false;
        p.hitbox = STANDING_HITBOX;
        p.hit_cooldown = INVINCIBLE_DURATION;
        play(p, "idle");
    }
}

// Where the view centres: the player's feet, pushed ahead by the right stick and, burrowing, the
// way the burrow is climbing or diving (Game.UpdateCamera).
static Vector2 look_ahead;
static float burrow_lead, burrow_lead_target;
static Vector2 camera_error;    // a jump in the clamped target, gliding away
static Vector2 previous_target, previous_focus;
static bool has_previous_target;

static Vector2 camera_target() {
    for (Player& p : actors.players) return p.position.vector() + look_ahead + Vector2{0, burrow_lead} - VIEW / zoom * 0.5f;
    return camera;
}

static void update_camera() {
    const float LOOK_AHEAD_EDGE_INSET = 0.15f, LOOK_AHEAD_EASE_RATE = 8;
    const float BURROW_LEAD_DISTANCE = 40, BURROW_LEAD_EASE_RATE = 3, BURROW_LEAD_RETURN_RATE = 12;
    const float EASE_RATE = 7, EASE_TRIGGER = 2;
    Vector2 look_target = control_look() * (VIEW / zoom * (0.5f - LOOK_AHEAD_EDGE_INSET));
    look_ahead += (look_target - look_ahead) * (1 - expf(-delta * LOOK_AHEAD_EASE_RATE));
    float lead = 0, rate = BURROW_LEAD_RETURN_RATE;
    for (Player& p : actors.players) if (p.state == Player::BURROW) {
        lead = p.burrow.heading.y * p.burrow.speed / BURROW_MAX_SPEED * BURROW_LEAD_DISTANCE;
        rate = BURROW_LEAD_EASE_RATE;
    }
    float amount = 1 - expf(-delta * rate);
    burrow_lead_target += (lead - burrow_lead_target) * amount;
    burrow_lead += (burrow_lead_target - burrow_lead) * amount;

    // A clamp that jumps the target further than the player moved glides there instead.
    Vector2 target = clamp_camera(camera_target());
    Vector2 focus = target;
    for (Player& p : actors.players) focus = p.position.vector() + look_ahead + Vector2{0, burrow_lead};
    if (has_previous_target) {
        Vector2 moved = target - previous_target, followed = focus - previous_focus;
        if (fabsf(moved.x) - fabsf(followed.x) > EASE_TRIGGER) camera_error.x -= moved.x - followed.x;
        if (fabsf(moved.y) - fabsf(followed.y) > EASE_TRIGGER) camera_error.y -= moved.y - followed.y;
    }
    camera_error = camera_error * expf(-delta * EASE_RATE);
    if (dot(camera_error, camera_error) < 0.25f) camera_error = {};
    camera = clamp_camera(target + camera_error);
    previous_target = target;
    previous_focus = focus;
    has_previous_target = true;
}

// Keeps the view inside the room, or centers it when the room is smaller.
static Vector2 clamp_camera(Vector2 target) {
    Vector2 limit = (room.size * TILE_SIZE).vector() - VIEW / zoom;
    return {limit.x > 0 ? clamp(target.x, 0.0f, limit.x) : limit.x * 0.5f, limit.y > 0 ? clamp(target.y, 0.0f, limit.y) : limit.y * 0.5f};
}

// --- `myth demo`: a scripted run through the room, played as real SDL key events pushed into the
// event queue, so it goes through the same input path as a keyboard. ---

struct DemoKey { float at; SDL_Keycode key; bool down; };
static const DemoKey DEMO[] = {
    {0.3f, SDLK_RIGHT, true},                               // run right
    {1.0f, SDLK_SPACE, true}, {1.4f, SDLK_SPACE, false},    // a held jump,
    {1.5f, SDLK_DOWN, true}, {1.95f, SDLK_DOWN, false},     // landing into a roll
    {1.6f, SDLK_RIGHT, false},
    {2.0f, SDLK_LEFT, true},                                // run back left
    {2.4f, SDLK_SPACE, true}, {2.5f, SDLK_SPACE, false},    // a short hop
    {3.0f, SDLK_LEFT, false},
    {3.2f, SDLK_DOWN, true}, {3.5f, SDLK_RIGHT, true},      // duck, then crawl
    {3.8f, SDLK_DOWN, false},
    {5.0f, SDLK_E, true}, {5.05f, SDLK_E, false},           // swing at the blob
    {5.5f, SDLK_E, true}, {5.55f, SDLK_E, false},
    {5.6f, SDLK_RIGHT, false},
    {6.2f, SDLK_SPACE, true},                               // jump and keep holding: the landing burrows,
    {6.9f, SDLK_RIGHT, true},                               // travel along the floor,
    {7.4f, SDLK_SPACE, false}, {7.5f, SDLK_RIGHT, false},   // and let go to pop out
    {9.2f, SDLK_LEFT, true}, {9.3f, SDLK_LSHIFT, true},     // sprint left,
    {9.8f, SDLK_SPACE, true}, {10.0f, SDLK_SPACE, false},   // jumping at speed
    {10.4f, SDLK_LSHIFT, false}, {10.5f, SDLK_LEFT, false},
    {10.8f, SDLK_X, true}, {12.0f, SDLK_X, false},          // a look up close
    {12.2f, SDLK_SPACE, true}, {12.5f, SDLK_SPACE, false},  // a jump,
    {12.45f, SDLK_DOWN, true}, {12.5f, SDLK_E, true}, {12.55f, SDLK_E, false}, {12.8f, SDLK_DOWN, false},  // swiping down
    {13.4f, SDLK_ESCAPE, true}, {13.45f, SDLK_ESCAPE, false}, // and the map (which pauses the demo too)
};

static void press_pad(SDL_Keycode key, bool down) {
    const s16 FULL = 32767;
    switch (key) {
        case SDLK_RIGHT:  SDL_SetJoystickVirtualAxis(demo_pad, SDL_GAMEPAD_AXIS_LEFTX, down ? FULL : 0); break;
        case SDLK_LEFT:   SDL_SetJoystickVirtualAxis(demo_pad, SDL_GAMEPAD_AXIS_LEFTX, down ? -FULL : 0); break;
        case SDLK_DOWN:   SDL_SetJoystickVirtualAxis(demo_pad, SDL_GAMEPAD_AXIS_LEFTY, down ? FULL : 0); break;
        case SDLK_UP:     SDL_SetJoystickVirtualAxis(demo_pad, SDL_GAMEPAD_AXIS_LEFTY, down ? -FULL : 0); break;
        case SDLK_LSHIFT: SDL_SetJoystickVirtualAxis(demo_pad, SDL_GAMEPAD_AXIS_RIGHT_TRIGGER, down ? FULL : 0); break;
        case SDLK_SPACE:  SDL_SetJoystickVirtualButton(demo_pad, SDL_GAMEPAD_BUTTON_SOUTH, down); break;
        case SDLK_E:      SDL_SetJoystickVirtualButton(demo_pad, SDL_GAMEPAD_BUTTON_WEST, down); break;
        case SDLK_X:      SDL_SetJoystickVirtualButton(demo_pad, SDL_GAMEPAD_BUTTON_LEFT_STICK, down); break;
        case SDLK_ESCAPE: SDL_SetJoystickVirtualButton(demo_pad, SDL_GAMEPAD_BUTTON_START, down); break;
    }
}

// Pushes the key events due by now.
static void push_demo_keys(float from, float to) {
    for (const DemoKey& k : DEMO) if (k.at > from && k.at <= to) {
        if (demo_pad) { press_pad(k.key, k.down); continue; }
        SDL_Event event{};
        event.type = k.down ? SDL_EVENT_KEY_DOWN : SDL_EVENT_KEY_UP;
        event.key.key = k.key;
        event.key.down = k.down;
        SDL_PushEvent(&event);
    }
}

// The demo through a virtual gamepad instead of the keyboard, the way a real one's input arrives
// (arrows on the left stick, Space South, E West, Shift the right trigger, X the stick's click,
// Escape Start), to show the gamepad drives the game the same.
static void attach_demo_pad() {
    // A capture runs without focus (the display may be asleep), and SDL drops pad input then.
    SDL_SetHint(SDL_HINT_JOYSTICK_ALLOW_BACKGROUND_EVENTS, "1");
    SDL_VirtualJoystickDesc desc;
    SDL_INIT_INTERFACE(&desc);
    desc.type = SDL_JOYSTICK_TYPE_GAMEPAD;
    desc.naxes = SDL_GAMEPAD_AXIS_COUNT;
    desc.nbuttons = SDL_GAMEPAD_BUTTON_COUNT;
    desc.button_mask = 0xffffffffu >> (32 - SDL_GAMEPAD_BUTTON_COUNT);
    desc.axis_mask = (1u << SDL_GAMEPAD_AXIS_COUNT) - 1;
    desc.name = "demo pad";
    demo_pad = SDL_OpenJoystick(SDL_AttachVirtualJoystick(&desc));
    if (!demo_pad) log("no virtual pad: %s", SDL_GetError());
}
