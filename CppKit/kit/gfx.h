// A Foster-ish batcher over SDL_GPU: textured, tinted quads in pixel space, drawn with Foster's own
// Batcher shader (see build.py), into passes: each pass draws into the window or a render target,
// optionally through a game's own fragment shader.
#pragma once
#include "base.h"
#include <SDL3/SDL.h>

struct Texture {
    SDL_GPUTexture* gpu = nullptr;
    Vector2 size;
    SDL_GPUTextureFormat format = SDL_GPU_TEXTUREFORMAT_R8G8B8A8_UNORM;
};

// How a pass's draws land on its target: over it with premultiplied alpha, keeping the larger of
// each channel, or simply replacing it.
enum class Blend : u8 { Premultiplied, Max, Replace };

// A fragment shader from <exe dir>/shaders/<name>.fragment.msl, drawn with the batcher's vertex
// shader and vertex layout. Its first sampler is the batch's texture; the rest are a pass's extra
// textures. `uniforms` is how many fragment uniform buffers it takes (0 or 1).
struct Shader {
    const char* name = nullptr;
    u32 samplers = 1, uniforms = 0;
    struct { SDL_GPUTextureFormat format; Blend blend; SDL_GPUGraphicsPipeline* pipeline; } pipelines[8] = {};  // made as passes need them
};

bool gfx_init(SDL_Window* window);
std::optional<Shader> load_shader(const char* name, u32 samplers = 1, u32 uniforms = 0);

// pixels: w*h premultiplied RGBA8, or another 4-byte `format` (R32_FLOAT).
Texture make_texture(const void* pixels, s32 w, s32 h, SDL_GPUTextureFormat format = SDL_GPU_TEXTUREFORMAT_R8G8B8A8_UNORM);
// A texture to draw into, then draw with.
Texture make_target(s32 w, s32 h, SDL_GPUTextureFormat format = SDL_GPU_TEXTUREFORMAT_R8G8B8A8_UNORM);
void update_texture(const Texture& t, const void* pixels);
void release_texture(const Texture& t);
// A QOI file, premultiplied.
std::optional<Texture> load_qoi(const char* path);

struct PassInfo {
    Texture target;                 // the window when it has no texture
    Color clear;
    bool keep = false;              // draw over what's there instead of clearing
    Shader* shader = nullptr;       // the batcher when null
    std::span<const u8> uniforms;
    std::span<const Texture> extra; // samplers after the first
    Blend blend = Blend::Premultiplied;
};
void begin_pass(const PassInfo& info);

Vector2 screen_size();

void rect(Rect r, Color color);
// `source` in texels; drawn 1:1 at `pos` (top-left). `wash` paints `color` through the
// texture's alpha, ignoring its colors.
void image(const Texture& texture, Rect source, Vector2 pos, Color color = WHITE, bool wash = false);
void triangle(Vector2 a, Vector2 b, Vector2 c, Color ca, Color cb, Color cc);
inline void triangle(Vector2 a, Vector2 b, Vector2 c, Color color) { triangle(a, b, c, color, color, color); }
void circle(Vector2 center, float radius, int steps, Color color);
inline void polygon(Vector2 a, Vector2 b, Vector2 c, Vector2 d, Color color) { triangle(a, b, c, color); triangle(a, c, d, color); }
// Foster's Batcher.Capsule: a tapered quad from `from` to `to`, with round caps.
void capsule(Vector2 from, Vector2 to, float from_radius, float to_radius, int steps, Color color, bool from_cap = true, bool to_cap = true);

// While its alpha is set, everything draws as a flat shape in this color: images washed through
// their alpha, filled shapes recolored. For masks of what something covers.
extern Color silhouette;

// Translate, then scale, relative to the current matrix. A negative scale flips.
void push_matrix(Vector2 translate, Vector2 scale);
inline void push_matrix(Vector2 translate, float scale) { push_matrix(translate, {scale, scale}); }
void pop_matrix();

// Reads a target back (waiting on the GPU) and writes it as a PNG. For dev screenshots.
bool save_png(const Texture& t, const char* path);

// When set, present doesn't wait for the window: if it can't take a frame (hidden, display
// asleep) the passes into targets still render and only the window's are skipped. For headless
// captures, which then pace themselves.
extern bool render_offscreen;

// Draws everything since the last present. With no begin_pass, it all goes to the window, cleared to `clear`.
void present(Color clear = {});
