// The host <-> game contract. The host (foster.mc) recompiles and swaps the
// game module at runtime, so the game reaches the host only through Host and
// keeps everything that must survive a reload in host.state.

const u32 API_VERSION = 5;
const i32 STATE_BYTES = 1 << 20;

// sokol keycodes
enum Key {
    KEY_A = 65, KEY_D = 68, KEY_R = 82, KEY_S = 83, KEY_W = 87, KEY_Z = 90,
    KEY_ESCAPE = 256, KEY_RIGHT = 262, KEY_LEFT = 263, KEY_DOWN = 264, KEY_UP = 265,
}

struct Rect { float2 pos; float2 size; }

// Where `pos` sits on a sprite. AUTO: the slice pivot if there is one, else
// BOTTOM_CENTER. The others use frame 0's opaque pixels.
enum Anchor { ANCHOR_AUTO, ANCHOR_BOTTOM_CENTER, ANCHOR_CENTER, ANCHOR_TOP_LEFT }

// The host's vertex shader output, which a game fragment shader receives.
struct VsOut { float4 pos; float4 color; float2 uv; }

const i32 MAX_GOALS = 8;

// Uniforms for the game's floor shader (`game_floor_shader`). All float4s, so
// the CPU and GPU layouts agree.
struct FloorParams {
    float4 board;               // xy = size in cells, z = time, w = progress 0..1
    float4 push;                // xy = cell the last box was pushed to, zw = direction
    float4 since;               // seconds since x = push, y = undo, z = win (negative: not won)
    float4 style;               // x = ring radius, y = breathe speed, z = ripple speed, w = win wave speed
    float4 goal_color;
    float4[MAX_GOALS] goals;    // xy = cell, z = 1 when a box is on it, w = 1 when in use
}

// A procedural sound effect; the host synthesizes it.
struct Sound {
    f32 freq;       // Hz at the start
    f32 sweep;      // octaves per second
    f32 attack;     // seconds to swell in
    f32 decay;      // seconds to fade out
    f32 square;     // 0 = sine, 1 = square
    f32 noise;      // 0 = tone, 1 = noise (resampled at freq)
    f32 volume;
    f32 delay;      // seconds before it starts
}

struct Host {
    u32 version;
    void* state;    // STATE_BYTES, zeroed at startup, survives reloads
    f32 delta;
    f64 time;
    float2 size;    // window, in pixels
    fn(i32): bool key_pressed;
    fn(float4): void clear;
    fn(Rect, float4): void rect;
    fn(str, str, float2, Anchor): bool sprite;  // name, tag; false if there's no such sprite
    fn(Rect, FloorParams*): void floor;         // a quad shaded by the game's floor shader, uv 0..1
    fn(str, f32, f32, f32): f32 tweak;          // name, default, min, max: the panel's current value
    fn(str, float4): float4 tweak_color;        // name, default
    fn(Sound): void play;
    fn(float2, f32): void push_matrix;  // translate, then uniform scale
    fn(): void pop_matrix;
    fn(): void exit;
}

// --- game-side helpers (the game sets `host` at the top of each call) ---

Host* host;

bool pressed(Key a) { return host.key_pressed(a); }
bool pressed(Key a, Key b) { return host.key_pressed(a) || host.key_pressed(b); }
void clear(float4 color) { host.clear(color); }
void rect(Rect r, float4 color) { host.rect(r, color); }
bool sprite(str name, str tag, float2 pos, Anchor anchor) { return host.sprite(name, tag, pos, anchor); }
void floor_quad(Rect r, FloorParams* params) { host.floor(r, params); }

// A value the tweak panel (F1) can drag. It survives reloads; changing the
// default here resets it, so code edits still win.
f32 tweak(str name, f32 initial, f32 lo, f32 hi) { return host.tweak(name, initial, lo, hi); }
float4 tweak_color(str name, u32 hex) { return host.tweak_color(name, rgb(hex)); }

void play(Sound s) {
    s.volume *= tweak("sfx/volume", 1.0f, 0.0f, 2.0f);
    host.play(s);
}
void push_matrix(float2 translate, f32 scale) { host.push_matrix(translate, scale); }
void pop_matrix() { host.pop_matrix(); }

float4 rgb(u32 hex) {
    return float4{cast(f32, (hex >> 16) & 0xff), cast(f32, (hex >> 8) & 0xff), cast(f32, hex & 0xff), 255.0f} / 255.0f;
}

Rect inflate(Rect r, f32 by) { return Rect{r.pos - by, r.size + 2.0f * by}; }
float2 to_float2(int2 p) { return float2{cast(f32, p.x), cast(f32, p.y)}; }
float2 floor2(float2 v) { return float2{floorf(v.x), floorf(v.y)}; }
f32 min(f32 a, f32 b) { return a < b ? a : b; }
f32 max(f32 a, f32 b) { return a > b ? a : b; }
// minc 0.9.18: `==` on int2 compiles but compares wrong; use this.
bool eq(int2 a, int2 b) { return a.x == b.x && a.y == b.y; }
