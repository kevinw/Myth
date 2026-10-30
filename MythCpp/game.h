// Human Questions (Myth), the C++ port: a vertical slice that plays Myth's own rooms, on CppKit.
// Everything the game's files share: its types, globals and the functions they call across.
#pragma once
#include "gfx.h"
#include "aseprite.h"
#include "json.h"
#include "text.h"
#include <string_view>

using std::string_view;

constexpr Vector2 VIEW{320, 180};
constexpr s32 TILE_SIZE = 8;
constexpr float PI = TAU / 2;
constexpr s32 MAX_LIGHTS = 16;

// A path under Myth/Assets, in temp.
const char* asset(const char* format, ...) __attribute__((format(printf, 1, 2)));
// Lines for `myth trace`/`demo` and errors, on stdout.
void log(const char* format, ...) __attribute__((format(printf, 1, 2)));
// A float as Jai prints one (six places, trailing zeros dropped), so traces diff against MythJai's.
const char* jai_float(float f);

inline Vector2 unit(Vector2 v) { return v.normalized(); }
inline float lerp(float a, float b, float t) { return a + (b - a) * t; }
inline Color mix(Color a, Color b, float t) {
    auto m = [t](u8 x, u8 y) { return u8(x + (float(y) - x) * t); };
    return {m(a.r, b.r), m(a.g, b.g), m(a.b, b.b), m(a.a, b.a)};
}
// Jai's fmod_cycling: always in [0, m).
inline float fmod_cycling(float x, float m) { float r = fmodf(x, m); return r < 0 ? r + m : r; }
inline Point2 to_point(Vector2 v) { return {(s32)v.x, (s32)v.y}; }  // toward zero

// The game's random numbers (blob hops, bark picks, shake), seeded the same every run.
float random01();
float random_range(float lo, float hi);
u64 random_u64();

// --- The room (room.cpp) ---

struct Room {
    Point2 position;            // in world tiles
    Point2 size;                // in tiles
    Color backdrop, ambient;
    float ambient_intensity = 0;
    float reverb = 0;           // how much the room rings (the FMOD global RoomReverb)
    string_view legend[128];    // tile char -> tile type name ("ember", "ruin", ...)
    std::span<u8> tiles;        // size.x * size.y chars, row by row; '0' is empty
    std::span<u8> background;
    Json actors;                // the file's "actors" array
};
std::optional<Room> load_room(const char* path);

// A trimmed image in the atlas: `source` is where its pixels are, `frame` places them in the
// untrimmed image (x/y are minus the trim), like Foster's Subtexture.
struct Subtex { RectInt source, frame; };
struct Segment { Vector2 a, b; };

struct PackedTileset {
    string_view name;
    s32 columns = 0, rows = 0, fill_tiles = 0;
    std::span<Subtex> tiles;
    std::span<std::span<Subtex>> variants;
    std::span<Subtex> normal_tiles;              // tiles' baked normals, same layout
    std::span<std::span<Subtex>> normal_variants;
    std::span<std::span<Segment>> occluders;     // per tile, the edges that cast shadows, in tile pixels
    std::span<std::span<std::span<Segment>>> occluder_variants;
};

// A sprite's baked normals, frame by frame (the sprite itself we load from its .ase).
struct PackedSprite { string_view name; Vector2 origin; std::span<Subtex> normals; };

struct Pack {
    Texture atlas, lighting_noise;
    std::span<PackedTileset> tilesets;
    std::span<PackedSprite> sprites;
    PackedSprite* find_sprite(string_view name);
    PackedTileset* find_tileset(string_view name);
};
std::optional<Pack> load_pack();
// `t` with its untrimmed top-left at `pos`, showing only the part inside `clip` (untrimmed-image
// space) when one is given.
void draw_subtex(const Subtex& t, Vector2 pos, RectInt clip = {}, Color color = WHITE, bool wash = false);

struct TileDraw { Subtex tile, normal; Vector2 pos; RectInt clip; Color field; };
void build_tile_draws();
void draw_tiles(std::span<TileDraw> draws, bool normals = false);
void draw_tile_field();
extern List<TileDraw> background_tiles, foreground_tiles;
extern List<Segment> occluders;  // room pixels

// Sprites by name, loaded on first use from Myth's sources and reloaded when their files change.
Sprite* get_sprite(string_view name);
void reload_changed_sprites();

// --- Actors (actor.cpp) ---

// Every actor type derives from Actor and has a pool in `actors` (ACTOR_TYPES). Instead of
// virtuals, a type defines its own update/draw overloads where it wants them; Actor's are the
// defaults. Actors sit on whole pixels and move a pixel at a time against the room's solid
// tiles, with the fraction kept in `remainder`, as in Myth's Actor.Move.
struct Actor {
    string_view id;
    Point2 position;        // room pixels; the hitbox and sprite hang off it
    Vector2 velocity, remainder;
    RectInt hitbox;
    s32 facing = 1;
    Vector2 squish{1, 1};
    string_view animation;
    float animation_time = 0;
    bool animation_looping = true;
    float hit_cooldown = 0; // i-frames: can't be hit again while > 0
};

inline void update(Actor&) {}
void draw(Actor& a);
// Per-frame upkeep every actor does, like Myth's Actor.AdvanceFrame.
void advance(Actor& a, float dt);
void play(Actor& a, string_view name, bool looping = true);
// The actor's sprite (Myth/Assets/Sources/Sprites/<name>.ase) in its current animation, facing and squish.
void draw_sprite(Actor& a, string_view name);
// Its baked normals for the same frame, from the visual pack (Actor.RenderNormals).
void draw_sprite_normals(Actor& a, string_view name);
inline RectInt hitbox_rect(const Actor& a) { return a.hitbox + a.position; }

enum Moved : u8 { HIT_X = 1, HIT_Y = 2 };
// Moves by `delta` pixels, carrying the fraction to the next call. Walking into a 1-pixel ledge
// steps up it, walking off one steps down, and hitting a wall stops that axis.
u8 move(Actor& a, Vector2 delta);
bool grounded(const Actor& a);
bool fell_out(const Actor& a);
bool overlaps_solid(const Actor& a, Point2 offset = {});
// Any solid tile under the rect (room pixels). Off the room is open, like Room.IsSolidTile.
bool overlaps_solid(RectInt r);

// --- Pawn (pawn.cpp): walking, jumping, wall-sliding, burrowing ---

struct Burrow {
    Point2 heading;             // travel direction along the surface
    bool solid_left = false;    // which hand the solid is on
    Vector2 toward;             // the surface normal, into the solid
    float timer = 0;            // time left while burrowing; negative while cooling down after
    float duration = 0, speed = 0;
    float remainder = 0;        // sub-pixel travel
    bool orbiting = false;      // latched by a press along the surface, held through corners
    bool reverse_queued = false;// braking to turn around
    Vector2 last_intent;
    float enter_timer = 0;      // squashing in
    float exit_timer = 0;
    bool needs_grounding = false;  // after a pop, touch ground before burrowing again
    float hud_fill = 1;
    bool hud_spent = false;
    s32 interval = 0;
    float pulse_timer = 0;
    float spread = 1;
    float leap_coyote = 0;      // just rounded a top corner: still pop up
    Vector2 leap_exit;
    float leap_travel_x = 0;
    float glow_exit_timer = 0;
    Point2 glow_anchor;
    Vector2 glow[80];           // the traced line, room pixels
    s32 glow_count = 0;
    bool passenger = false;     // Bud rides along
    float sparks = 0;           // sparks owed, fractional
    void* sound = nullptr;      // the burrow loop
    s32 dive_held = 0;          // wall dive: the vertical direction held last frame,
    s32 dive_last_tap = 0;      // the previous tap's,
    float dive_tap_timer = 0;   // time left for the second tap,
    bool dive_gripping = false; // and the first tap holding you to the wall
};

struct Pawn : Actor {
    bool grounded = false;
    bool ducking = false;
    float jump_timer = 0;           // > 0 while a held jump keeps its lift
    float jump_buffer_timer = 0;    // a jump pressed just before landing still counts
    float coyote_timer = 0;         // ...and one pressed just after walking off a ledge
    s32 coyote_y = 0;
    s32 wall_jump_direction = 0;    // away from the wall last jumped off
    float wall_jump_lock_timer = 0; // steering is locked away from that wall for a moment
    float wall_coyote_timer = 0;
    s32 wall_coyote_direction = 0;
    float run_start_timer = 0;
    float locomotion_timer = 0;     // holds the run animation through brief stops
    bool rolling = false;
    float roll_time = 0;
    float airborne_roll_distance = 0;
    bool jump_cut_immune = false;   // a burrow pop keeps its float even with jump let go
    float carry_speed = 0;          // a launch's speed, which the air speed cap only bleeds down from
    bool dash_run_armed = false;    // sprinting while the dash button stays held
    float dash_cooldown = 0;
    bool dash_needs_grounding = false;  // one dash per landing
    bool dash_cancel_braking = false;   // ducked out of a sprint: sliding to a stop
    bool burrows = false;           // has the ability at all (PawnAbilities.Burrow): the player, not Bud
    Burrow burrow;
    float burrow_light = 0;         // 0..1, how far the glow has turned to its burrowing color
};

struct PawnInput {
    float move_x = 0;               // -1..1
    s32 move_y = 0;                 // -1 up, 1 down
    bool jump_pressed = false, jump_down = false, dash_pressed = false, dash_down = false;
};

enum PawnEvent : u8 { LANDED = 1, JUMPED = 2, WALL_JUMPED = 4, SKIDDED = 8, HIT_WALL = 16, ROLLED = 32, BURROWED = 64, DASH_HIT_WALL = 128 };

constexpr float MAX_GROUND_SPEED = 93;
constexpr float CRAWL_SPEED = 28;
constexpr float JUMP_TIME = 0.22f;
constexpr s32 STANDING_HEIGHT = 12;
constexpr float BURROW_DURATION = 1.133f;
constexpr float BURROW_MAX_SPEED = 150;
constexpr RectInt STANDING_HITBOX{-4, -STANDING_HEIGHT, 8, STANDING_HEIGHT};

// One frame of the body: timers, then (with `normal`) the Normal state's steering, ducking and
// jumping, then gravity and movement. Other states pass normal = false and steer themselves.
u8 pawn_update(Pawn& p, const PawnInput& input, float dt, bool normal = true);
s32 move_input(const PawnInput& input);
bool dash_running(const Pawn& p);
void horizontal_movement(Pawn& p, s32 move_x, float movement, float dt);
void start_roll(Pawn& p);

bool can_burrow(const Pawn& p);
bool try_start_burrow(Pawn& p, Vector2 previous_velocity, Vector2 toward_surface = {0, 1});
bool try_wall_dive(Pawn& p, const PawnInput& input, s32 wall_jump, float dt);
// One frame underground. True when it popped out.
bool burrow_state(Pawn& p, const PawnInput& input, float dt);
void update_burrow_always(Pawn& p, float dt);
bool burrow_hides(const Pawn& p, bool burrowing);
void draw_burrow_glow(Pawn& p, bool burrowing);
struct Light;
void burrow_light(Pawn& p, bool burrowing, Light& light, float dt);
Color hue_color(float hue);

// --- The rig (rig.cpp) ---

constexpr s32 MAX_JOINTS = 128;
constexpr s32 BONE_COUNT = 23;

struct RigPose {
    s32 take = -1;
    float time = 0;
    s32 previous = -1;          // the take being faded out of
    float previous_time = 0;
    float blend = 1;            // 0..1 from previous toward take
    float step = 0;             // time since the last posed frame
    bool posed = false;
    void* instance = nullptr;
    Vector2 projected[MAX_JOINTS];      // rig-local pixels from the actor's position
    float depth[MAX_JOINTS] = {};       // meters toward the camera
    float lean = 0, lean_rate = 0;      // radians, a spring
    float previous_velocity_x = 0;
    s32 requested = -1;                 // the take asked for, which a lead may be opening
    s32 lead = -1;
    float playback = 1;                 // the take's speed, times its own
    float run_lean_time = 0;
    bool was_running = false;
    float extra_lean = 0;               // degrees, leaning into a run's start
    s32 skid_facing = 0;                // the facing a skid started from
    float skid_from_yaw = 0, skid_to_yaw = 0;  // a skid sweeps the yaw between these over its take,
    float skid_yaw_weight = 0;          // and lets go after (UpdateRigSkidYaw)
    float skid_yaw = 0;
    bool air_swing = false;             // attacking in the air: the legs keep the air pose,
    float attack_pitch = 0;             // and a down (1) or up (-1) swipe rolls the torso
    float leg_weight = 0, leg_time = 0, attack_reach = 0;
    float arm_arc_from = 0;             // the swing arm's angle when the take began
    bool arm_arc_set = false;
    float arm_reach_offset = 0;
    s32 fidget_take = -1;               // standing about, now and then a fidget plays (RigFidget)
    float fidget_timer = 5;
    s32 fidget_last = -1;
    Vector2 arm_aim;                    // the figure rolls so its arms point this way on screen,
    float arm_aim_weight = 0;           // this much (ApplyPoseRoll)
    float yaw = 0;                      // radians the figure is turned: 0 facing left, PI right
    float turn_from = 0, turn_to = 0, turn_time = 0;
    s32 turning_to = 0;                 // the facing it's turning toward, 0 before the first pose
    struct { Vector2 position, velocity; bool ready = false; } motion[3];  // head, left hand, right hand, on springs
    bool gait = false;                  // a walk, run or crawl is playing
    float foot_offset[2] = {};          // how far foot IK has moved each foot onto the ground
    // The body on springs over the animation (UpdateRagdoll), meters: it follows every move, but a
    // landing knocks it down and it springs back up.
    Vector3 ragdoll[MAX_JOINTS], carried[MAX_JOINTS], ragdoll_velocity[MAX_JOINTS];
    float bone_lengths[BONE_COUNT] = {};
    bool ragdoll_ready = false;
    float landing_speed = 0;            // a landing to take on the next pose, pixels a second
    float freeze = 0;                   // a hard landing holds the pose this long
};

// What tells one rigged actor from another: its palette and costume (Assets/Actors/<name>.json).
struct RigLook {
    Color far = Color::rgb(0x435263), near = Color::rgb(0xb7cdd0), core = Color::rgb(0x708b94);
    string_view costume;
};
const RigLook PLAYER_LOOK{.costume = "brass-wanderer"};
const RigLook BUD_LOOK{Color::rgb(0x45648c), Color::rgb(0xabcfff), Color::rgb(0x7fb6ff), "bud-crest"};

string_view take_for(string_view animation);
void play_rig(RigPose& pose, string_view animation, Vector2 velocity, float dt, bool restart = false, s32 facing = 0, bool still = false);
void update_rig(RigPose& pose, Actor& a, float dt, bool grounded = false);
void land_rig(RigPose& pose, float speed);
// Where the head is, from the actor's position, and its radius.
Vector2 rig_head(const RigPose& pose, float* radius);
void draw_rig(RigPose& pose, Vector2 at, const RigLook& look);
void draw_rig_normals(RigPose& pose, Vector2 at, const RigLook& look);

// --- The actor types (actors.cpp) ---

struct Pop : Actor {
    string_view sprite_name;
    bool flip_y = false;
};

struct Light : Actor {
    float radius = 48;
    string_view sprite_name = "fire";
    string_view enabled_by_persistent_button_id;
    Color light_color = Color::rgb(0xffd37a);
    float intensity = 1;
    float height = 16;
    float flicker_frequency = 4;
    float flicker_intensity = 0.08f;
    float flicker_offset = 0;
};

struct PlayerStart : Actor {};

struct Blob : Actor {
    s32 health = 2;
    float jump_timer = 2;
    bool grounded = true;
    bool started = false;
};

struct Player : Pawn {
    s32 health = 99;
    enum State : u8 { NORMAL, ATTACK, HURT, DEAD, BURROW } state = NORMAL;
    float state_time = 0;
    enum Attack : u8 { SIDE, DOWN, UP, DUCK } attack = SIDE;
    bool attack_hit_target = false; // this swing has touched something to hit
    bool attack_blocked = false;    // ...or a wall stopped it
    Handle<Pop> attack_swipe;
    float rig_dive_grace = 0;       // jump let go this recently still counts toward a dive
    float rig_dive_angle = 0;
    float rig_dive_weight = 0;
    bool rig_dive_clip = false;
    s32 footstep_frame = -1;
    Point2 last_safe_ground;        // where a cliff fall puts you back
    RigPose rig;
    Handle<Light> light;            // the warm glow around you (Player.CreatePlayerLight)
    double last_skid_sound = 0;
};

struct Bud : Pawn {
    RigPose rig;
    float blocked_time = 0;
    Point2 entry;               // where he came into the room
    float respawn_timer = 0;    // out of play while > 0
    bool riding = false;        // in the player's burrow
    float pop_delay = 0;        // > 0: about to pop out of it
    Point2 pop_at;
    Vector2 pop_velocity, pop_squish;
    s32 pop_facing = 0;
    float back_dash_timer = 0, back_dash_cooldown = 0;
};

constexpr s32 HIT_DAMAGE = 17;
constexpr float INVINCIBLE_DURATION = 1.5f;

// X(Type, pool, capacity), the pools in `actors`, and the order update_all/draw_all visit them.
#define ACTOR_TYPES(X) X(Blob, blobs, 64) X(Bud, buds, 4) X(Light, lights, 64) X(Player, players, 2) X(PlayerStart, player_starts, 4) X(Pop, pops, 64)

struct Actors {
#define X(Type, pool, capacity) Pool<Type, capacity> pool;
    ACTOR_TYPES(X)
#undef X
};
extern Actors actors;

void update(Player& p);
void draw(Player& p);
void draw_normals(Player& p);
Player* spawn_player(Point2 at);
void update(Bud& b);
void draw(Bud& b);
void draw_normals(Bud& b);
void spawn_bud(Player& player);
void bud_struck(Bud& b, RectInt swing, s32 attacker_x);
bool bud_rides_burrow(Bud& b, Pawn& player);
void bud_pop_from_burrow(Bud& b, Pawn& player);
void update(Blob& b);
void draw(Blob& b);
void draw_normals(Blob& b);
void hit(Blob& b, Actor& by);
void update(Light& l);
inline void draw(Light&) {}
void draw_emissive(Light& l);
float flicker_scale(const Light& l);
void update(Pop& p);
void draw(Pop& p);
Handle<Pop> spawn_pop(Point2 at, string_view sprite_name = "pop", s32 facing = 1, bool flip_y = false);
inline void draw(PlayerStart&) {}
// Adds the actor the json's "Type" names, its fields read from the json.
bool spawn(Json json);

// --- Effects (effects.cpp): particles, the pixel sort, shadows ---

// A cone burst from `at`, aimed along `direction` (radians, y down), the effect's speeds scaled.
void emit(string_view effect, Vector2 at, s32 count, float direction, float speed_scale = 1, float spread = -1);
// The effect's burst spread along a line from `origin` (ParticleSystem.EmitLine).
void emit_line(string_view effect, Vector2 origin, Vector2 direction, float length = -1, Vector2 base_velocity = {});
void update_particles(float dt);
void draw_particles();
void churn_record(Point2 at, Vector2 toward, float speed01);
void apply_pixel_sort(const Texture& lit, Vector2 view, float dt);
extern Texture shadow_depths;
void update_shadow_depths(std::span<Vector2> lights, std::span<float> radii);

// --- Sound (ui.cpp) ---

void sound(string_view path, Point2 at, const char* parameter = nullptr, const char* label = nullptr);
// An instance of the event placed at `at`, to set parameters on before finish_sound plays it.
void* start_sound(string_view path, Vector2 at);
void place_sound(void* instance, Vector2 at);
void finish_sound(void* instance);
void update_sound(Vector2 listener);

extern "C" {
int FMOD_Studio_EventInstance_Start(void* instance);
int FMOD_Studio_EventInstance_Stop(void* instance, int mode);
int FMOD_Studio_EventInstance_Release(void* instance);
int FMOD_Studio_EventInstance_SetVolume(void* instance, float volume);
int FMOD_Studio_EventInstance_SetParameterByName(void* instance, const char* name, float value, int ignore_seek_speed);
int FMOD_Studio_EventInstance_SetParameterByNameWithLabel(void* instance, const char* name, const char* label, int ignore_seek_speed);
}

// --- Controls, story, HUD, map (ui.cpp) ---

float control_move_x();
s32 control_move_y();
bool control_jump_pressed();
bool control_jump_down();
bool control_attack_pressed();
bool control_dash_pressed();
bool control_dash_down();
bool control_map_pressed();
bool control_face_pressed();
Vector2 control_look();
bool control_zoom_down();
void update_controls();
void control_event(const SDL_Event& event);
void begin_control_frame();

enum class Trigger : u8 { ROOM_ENTERED, PLAYER_HURT, ENEMY_KILLED, PLAYER_IDLE };
void bark(Trigger trigger);
void load_story();
void update_story();
void draw_speech(Vector2 view);
void draw_hud();
extern bool map_open;
void toggle_map();
void draw_map();

// --- The frame (main.cpp) ---

extern Room room;
extern Pack pack;
extern float delta;             // seconds this frame
extern float zoom;              // view pixels per room pixel
extern bool respawn_requested;
extern bool demo, trace;
extern float demo_time;
extern List<SDL_Keycode> held;  // in persist
bool key_down(SDL_Keycode key);
bool key_pressed(SDL_Keycode key);
double now();
// The command-line argument after `name`, if it's there.
const char* arg_after(const char* name);
void hit_stun(float seconds);
void shake(float seconds);
void pulse_dither_glitch(Vector2 axis, float amount = 1);
