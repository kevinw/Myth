// A tiny Foster-ish host over sokol: window, a batcher of textured quads,
// Aseprite sprites, pressed-this-frame input, and a game module that is
// recompiled and swapped whenever its source changes on disk. Sprites reload
// the same way.

import sokol_all;
import process;
import file;
import api;
import aseprite;
import imgui;
import sokol_audio;
import math;
import sokol_imgui;

@shader vertex
VsOut batch_vs(@attr(0) float4 pos_uv, @attr(1) float4 color, @uniform float4x4 mvp) {
    VsOut o;
    o.pos = mul(mvp, float4{pos_uv.xy, 0.0f, 1.0f});
    o.color = color;
    o.uv = pos_uv.zw;
    return o;
}

@shader fragment
float4 batch_fs(VsOut input, @texture(0) Texture2D tex, @sampler(0) Sampler smp) {
    return sample(tex, smp, input.uv) * input.color;
}

// --- batcher ---

const i32 MAX_VERTS = 1 << 16;
const i32 MAX_BATCHES = 256;

struct Vert { float4 pos_uv; float4 color; }
struct Batch { sg_view texture; bool floor; i32 start; i32 count; }
struct Matrix { float2 translate; f32 scale; }

Vert[MAX_VERTS] verts;
i32 vert_count;
Batch[MAX_BATCHES] batches;     // runs of vertices sharing a texture and pipeline
i32 batch_count;
Matrix[16] matrices = { Matrix{float2{0.0f}, 1.0f} };
i32 matrix_top;
float4 clear_color;
sg_buffer vbuf;
sg_pipeline pip;
sg_pipeline floor_pip;      // the game's floor shader; rebuilt on every reload
FloorParams floor_uniforms; // one floor per frame
sg_sampler nearest;
sg_view white;

float2 transform(float2 p) { var m = matrices[matrix_top]; return p * m.scale + m.translate; }

void quad(Rect r, float2 uv0, float2 uv1, float4 color, sg_view texture, bool floor) {
    if vert_count + 6 > MAX_VERTS { return; }
    var last = batch_count > 0 ? batches[batch_count - 1] : Batch{};
    if batch_count == 0 || last.texture.id != texture.id || last.floor != floor {
        if batch_count == MAX_BATCHES { return; }
        batches[batch_count++] = Batch{texture, floor, vert_count, 0};
    }
    var a = transform(r.pos);
    var b = transform(r.pos + r.size);
    float4[6] corners = {
        float4{a, uv0}, float4{b.x, a.y, uv1.x, uv0.y}, float4{b, uv1},
        float4{a, uv0}, float4{b, uv1}, float4{a.x, b.y, uv0.x, uv1.y},
    };
    for i32 i = 0; i < 6; i++ { verts[vert_count++] = Vert{corners[i], color}; }
    batches[batch_count - 1].count += 6;
}

void host_rect(Rect r, float4 color) { quad(r, float2{0.0f}, float2{1.0f}, color, white, false); }

void host_floor(Rect r, FloorParams* params) {
    floor_uniforms = *params;
    quad(r, float2{0.0f}, float2{1.0f}, float4{1.0f}, white, true);
}

// Both pipelines share the batch vertex shader; the pipeline owns its shader.
sg_pipeline make_pipeline(ShaderMeta* fs) {
    return sg_make_pipeline(&sg_pipeline_desc{
        .shader = sokol_make_shader(&batch_vs_shader, fs),
        .layout.attrs[0].format = SG_VERTEXFORMAT_FLOAT4,
        .layout.attrs[1].format = SG_VERTEXFORMAT_FLOAT4,
        .colors[0].blend = sg_blend_state{
            .enabled = true,
            .src_factor_rgb = SG_BLENDFACTOR_SRC_ALPHA,
            .dst_factor_rgb = SG_BLENDFACTOR_ONE_MINUS_SRC_ALPHA,
        },
    });
}

void destroy_pipeline(sg_pipeline p) {
    if p.id == 0 { return; }
    sg_destroy_shader(sg_query_pipeline_desc(p).shader);
    sg_destroy_pipeline(p);
}

void host_push_matrix(float2 translate, f32 scale) {
    var m = matrices[matrix_top];
    matrices[++matrix_top] = Matrix{m.translate + translate * m.scale, m.scale * scale};
}

void host_pop_matrix() { matrix_top--; }
void host_clear(float4 color) { clear_color = color; }

// --- sprites ---
//
// Loaded by name from the sprites folder on first use, flattened into a grid
// texture of frames, and reloaded when the file changes. A load that fails
// (say, a file caught mid-save) keeps the previous version.

const i32 MAX_SPRITES = 64;
const i32 SPRITE_COLUMNS = 16;

struct Sprite {
    string name;
    string path;
    FileStamp stamp;
    Aseprite ase;
    int4 visible;   // frame 0's opaque pixels: xy = min, zw = max
    int2 grid;      // frames across, down
    sg_image image;
    sg_view view;
}

str sprites_dir;
Sprite[MAX_SPRITES] sprites;
i32 sprite_count;
f64 next_sprite_check;

bool keep_layer(str name) { return !str_contains(name, "debug") && !str_ends_with(name, "_socket"); }

void load_sprite(Sprite* s) {
    Aseprite ase;
    if !ase_load(&ase, s.path, keep_layer) { return; }
    if s.ase.frame_count > 0 {
        sg_destroy_view(s.view);
        sg_destroy_image(s.image);
        ase_free(&s.ase);
    }
    s.ase = ase;
    s.visible = ase_visible_bounds(&ase, 0);
    s.grid = int2{ase.frame_count < SPRITE_COLUMNS ? ase.frame_count : SPRITE_COLUMNS, (ase.frame_count + SPRITE_COLUMNS - 1) / SPRITE_COLUMNS};

    var size = s.grid * int2{ase.width, ase.height};
    i64 frame_bytes = ase.width * ase.height * 4;
    var atlas = alloc<u8>(size.x * size.y * 4);
    defer free(atlas);
    for i32 f = 0; f < ase.frame_count; f++ {
        var origin = int2{f % s.grid.x, f / s.grid.x} * int2{ase.width, ase.height};
        for i32 y = 0; y < ase.height; y++ {
            memcpy(atlas + ((origin.y + y) * size.x + origin.x) * 4, ase.pixels + f * frame_bytes + y * ase.width * 4, ase.width * 4);
        }
    }
    s.image = sg_make_image(&sg_image_desc{
        .width = size.x,
        .height = size.y,
        .pixel_format = SG_PIXELFORMAT_RGBA8,
        .data.mip_levels[0] = sg_range{ .ptr = atlas, .size = size.x * size.y * 4 },
    });
    s.view = sg_make_view(&sg_view_desc{ .texture.image = s.image });
    print("loaded sprite {} ({} frames)\n", s.name, ase.frame_count);
}

Sprite* find_sprite(str name) {
    for i32 i = 0; i < sprite_count; i++ {
        if str_equal(sprites[i].name, name) { return &sprites[i]; }
    }
    if sprite_count == MAX_SPRITES { return null; }

    // The name lives in the game module, which can be unloaded; keep a copy.
    var s = &sprites[sprite_count++];
    s.name = string(name);
    s.path = format("{}/{}.ase", sprites_dir, name);
    if file_size(s.path) < 0 {   // not path_exists: that has no wasm VFS support
        free(s.path);
        s.path = format("{}/{}.aseprite", sprites_dir, name);
    }
    s.stamp = file_stamp(s.path);
    load_sprite(s);
    return s;
}

void reload_changed_sprites() {
    if app.time < next_sprite_check { return; }
    next_sprite_check = app.time + 0.25;
    for i32 i = 0; i < sprite_count; i++ {
        var st = file_stamp(sprites[i].path);
        if st.ok && file_stamp_changed(st, sprites[i].stamp) {
            sprites[i].stamp = st;
            load_sprite(&sprites[i]);
        }
    }
}

float2 sprite_origin(Sprite* s, Anchor anchor) {
    var v = s.visible;
    if anchor == ANCHOR_AUTO && s.ase.has_pivot { return to_float2(s.ase.pivot); }
    if anchor == ANCHOR_CENTER { return floor2(to_float2(v.xy + v.zw) * 0.5f + 0.5f); }
    if anchor == ANCHOR_TOP_LEFT { return to_float2(v.xy); }
    return float2{floorf(cast(f32, v.x + v.z) * 0.5f + 0.5f), cast(f32, v.w)};
}

bool host_sprite(str name, str tag, float2 pos, Anchor anchor) {
    var s = find_sprite(name);
    if s == null || s.ase.frame_count == 0 { return false; }

    var t = AseTag{"", 0, s.ase.frame_count - 1, ASE_FORWARD};
    for i32 i = 0; i < s.ase.tag_count; i++ {
        if str_equal(s.ase.tags[i].name, tag) { t = s.ase.tags[i]; }
    }
    var frame = ase_frame_at(&s.ase, t, app.time);
    var uv0 = to_float2(int2{frame % s.grid.x, frame / s.grid.x}) / to_float2(s.grid);
    var size = to_float2(int2{s.ase.width, s.ase.height});
    quad(Rect{pos - sprite_origin(s, anchor), size}, uv0, uv0 + float2{1.0f} / to_float2(s.grid), float4{1.0f}, s.view, false);
    return true;
}

// --- tweaks ---
//
// The game asks for a value by name every frame; the first ask registers it
// with the panel. Values live here, so they survive game reloads.

const i32 MAX_TWEAKS = 64;
const i32 KEY_TOGGLE_TWEAKS = 290;  // F1

struct Tweak {
    u8* label;      // NUL-terminated copy: the game module's strings go away on reload
    str name;       // view of label
    bool color;
    float4 value;   // a slider uses .x
    float4 initial; // the default the code passed
    float2 range;
}

Tweak[MAX_TWEAKS] tweaks;
i32 tweak_count;
bool show_tweaks = true;

bool same(float4 a, float4 b) { return a.x == b.x && a.y == b.y && a.z == b.z && a.w == b.w; }

Tweak* find_tweak(str name, float4 initial) {
    for i32 i = 0; i < tweak_count; i++ {
        var t = &tweaks[i];
        if !str_equal(t.name, name) { continue; }
        if !same(t.initial, initial) { t.initial = initial; t.value = initial; }
        return t;
    }
    if tweak_count == MAX_TWEAKS { return null; }
    var t = &tweaks[tweak_count++];
    t.label = str_to_cstr(name);
    t.name = str_from(t.label, name.len);
    t.initial = initial;
    t.value = initial;
    return t;
}

f32 host_tweak(str name, f32 initial, f32 lo, f32 hi) {
    var t = find_tweak(name, float4{initial});
    if t == null { return initial; }
    t.range = float2{lo, hi};
    return t.value.x;
}

float4 host_tweak_color(str name, float4 initial) {
    var t = find_tweak(name, initial);
    if t == null { return initial; }
    t.color = true;
    return t.value;
}

void draw_tweaks() {
    if !show_tweaks || tweak_count == 0 { return; }
    ImGui_SetNextWindowPos(ImVec2{12.0f, 12.0f}, ImGuiCond_FirstUseEver);
    if ImGui_Begin("Tweaks (F1)", &show_tweaks, ImGuiWindowFlags_AlwaysAutoResize) {
        for i32 i = 0; i < tweak_count; i++ {
            var t = &tweaks[i];
            var v = cast(f32*, &t.value);
            if t.color { ignore ImGui_ColorEdit3(t.label, v, 0); }
            else { ignore ImGui_SliderFloat(t.label, v, t.range.x, t.range.y); }
            // right-click: back to the code's default
            if ImGui_IsItemClicked(ImGuiMouseButton_Right) { t.value = t.initial; }
        }
        if ImGui_Button("Reset all") {
            for i32 i = 0; i < tweak_count; i++ { tweaks[i].value = tweaks[i].initial; }
        }
    }
    ImGui_End();
}

// --- audio ---
//
// A tiny sfxr-style synth. Voices are rendered on the main thread and pushed
// to sokol_audio each frame, so no host or game code runs on the audio thread
// (which matters when the game module is swapped out from under it).

const i32 AUDIO_RATE = 44100;
const i32 MAX_VOICES = 16;

struct Voice {
    Sound sound;
    bool active;
    f32 t;          // seconds since start; negative while delayed
    f32 freq;
    f32 sweep;      // per-sample frequency multiplier
    f32 phase;
    u32 rng;
    f32 noise;
}

Voice[MAX_VOICES] voices;
f32[4096] audio_buf;

void host_play(Sound s) {
    // a free voice, else steal the one furthest along
    var slot = 0;
    for i32 i = 0; i < MAX_VOICES; i++ {
        if !voices[i].active { slot = i; break; }
        if voices[i].t > voices[slot].t { slot = i; }
    }
    voices[slot] = Voice{
        .sound = s,
        .active = true,
        .t = -s.delay,
        .freq = s.freq,
        .sweep = cast(f32, pow(2.0, cast(f64, s.sweep) / AUDIO_RATE)),
        .rng = 0x9E3779B9 + slot,
    };
}

f32 voice_sample(Voice* v) {
    var s = &v.sound;
    v.t += 1.0f / AUDIO_RATE;
    if v.t < 0.0f { return 0.0f; }
    if v.t > s.attack + s.decay { v.active = false; return 0.0f; }

    var env = v.t < s.attack ? v.t / s.attack : 1.0f - (v.t - s.attack) / s.decay;
    v.freq *= v.sweep;
    v.phase += v.freq / AUDIO_RATE;
    if v.phase >= 1.0f {
        v.phase -= floorf(v.phase);
        v.rng ^= v.rng << 13;
        v.rng ^= v.rng >> 17;
        v.rng ^= v.rng << 5;
        v.noise = cast(f32, v.rng) / 2147483648.0f - 1.0f;
    }
    var sine = sinf(v.phase * 6.2831853f);
    var square = v.phase < 0.5f ? 1.0f : -1.0f;
    var tone = sine + (square - sine) * s.square;
    return (tone + (v.noise - tone) * s.noise) * env * env * s.volume;
}

void pump_audio() {
    var n = saudio_expect();
    if n > 4096 { n = 4096; }
    if n <= 0 { return; }
    for i32 i = 0; i < n; i++ {
        f32 mix = 0.0f;
        for i32 k = 0; k < MAX_VOICES; k++ {
            if voices[k].active { mix += voice_sample(&voices[k]); }
        }
        audio_buf[i] = mix / (1.0f + fabsf(mix));   // soft clip
    }
    ignore saudio_push(&audio_buf[0], n);
}

// --- input ---

bool[SAPP_MAX_KEYCODES] keys_pressed;

bool host_key_pressed(i32 key) { return keys_pressed[key]; }

void on_event(sapp_event* ev) {
    if ev.type == SAPP_EVENTTYPE_KEY_DOWN && ev.key_code == KEY_TOGGLE_TWEAKS { show_tweaks = !show_tweaks; }
    ignore simgui_handle_event(ev);
    if ImGui_GetIO().WantCaptureKeyboard { return; }
    if ev.type == SAPP_EVENTTYPE_KEY_DOWN && !ev.key_repeat { keys_pressed[ev.key_code] = true; }
}

// --- game module ---
//
// On macOS the game is hot-reloaded. Elsewhere main.mc links it in directly.

str game_path;
fn(Host*): void tick_game;
bool compile_failed;

when os(macos) {

//
// The game is built out of process to a dylib and dlopen'd: in-process
// libminc (0.9.18, macOS) crashes on the second compile once the game indexes
// an array. Each build gets its own file name since dlopen caches by path.

extern "libSystem.B.dylib" i32 dlclose(void* handle);

FileWatch sources;  // the game's directory: any .mc there can be part of it
void* module;
string module_path;
i32 builds;

// Build, load, validate, then swap; any failure keeps the running module.
void reload() {
    var path = format("build/game_{}.dylib", builds++);
    ProcCmd cmd = { .args = { "minc", "--agent", "--shared", game_path, "-o", path }, .capture = true };
    var built = proc_run(&cmd);
    defer proc_result_free(&built);
    var cpath = str_to_cstr(path);
    defer free(cpath);
    var m = proc_ok(&built) ? dlopen(cpath, 2) : null;   // RTLD_NOW
    var version = cast(fn(): u32, m == null ? null : dlsym(m, "game_version"));
    var tick = cast(fn(Host*): void, m == null ? null : dlsym(m, "game_tick"));
    compile_failed = version == null || tick == null || version() != API_VERSION;

    // The GPU driver compiles the shader source now, so the old module's
    // copy isn't needed after this.
    sg_pipeline new_floor = {};
    var floor_shader = cast(fn(): ShaderMeta*, m == null ? null : dlsym(m, "game_floor_shader"));
    if !compile_failed && floor_shader != null {
        new_floor = make_pipeline(floor_shader());
        if sg_query_pipeline_state(new_floor) != SG_RESOURCESTATE_VALID {
            print("reload: floor shader rejected by the GPU driver\n");
            destroy_pipeline(new_floor);
            compile_failed = true;
        }
    }

    if compile_failed {
        print("reload failed, keeping previous:\n{}", built.out);
        if m != null { ignore dlclose(m); }
        ignore file_remove(path);
        free(path);
        return;
    }

    if module != null {
        ignore dlclose(module);
        ignore file_remove(module_path);
        free(module_path);
    }
    module = m;
    module_path = path;
    tick_game = tick;
    destroy_pipeline(floor_pip);
    floor_pip = new_floor;
    print("reloaded {}\n", path);
}

void start_game() {
    ignore dir_create("build");
    var dir = path_dirname(game_path);
    sources = file_watch_dir(dir.len == 0 ? "." : dir);
    reload();
}

void poll_game() {
    if file_watch_poll(&sources) { reload(); }
}

void stop_game() {
    if module != null { ignore file_remove(module_path); }
    file_watch_close(&sources);
}

}
else {

void start_game() {
    tick_game = game_tick;
    floor_pip = make_pipeline(game_floor_shader());
}

void poll_game() {}
void stop_game() {}

}

// --- app ---

Host app;

void host_exit() { sapp_quit(); }

void init() {
    sg_setup(&sg_desc{ .environment = sglue_environment() });
    vbuf = sg_make_buffer(&sg_buffer_desc{
        .size = MAX_VERTS * sizeof(Vert),
        .usage.write_transient = true,
    });
    pip = make_pipeline(&batch_fs_shader);
    nearest = sg_make_sampler(&sg_sampler_desc{
        .min_filter = SG_FILTER_NEAREST,
        .mag_filter = SG_FILTER_NEAREST,
        .wrap_u = SG_WRAP_CLAMP_TO_EDGE,
        .wrap_v = SG_WRAP_CLAMP_TO_EDGE,
    });
    u32 white_pixel = 0xffffffff;
    white = sg_make_view(&sg_view_desc{ .texture.image = sg_make_image(&sg_image_desc{
        .width = 1,
        .height = 1,
        .pixel_format = SG_PIXELFORMAT_RGBA8,
        .data.mip_levels[0] = sg_range{ .ptr = &white_pixel, .size = 4 },
    })});

    app = Host{
        .version = API_VERSION,
        .state = alloc<u8>(STATE_BYTES),
        .key_pressed = host_key_pressed,
        .clear = host_clear,
        .rect = host_rect,
        .sprite = host_sprite,
        .floor = host_floor,
        .tweak = host_tweak,
        .tweak_color = host_tweak_color,
        .play = host_play,
        .push_matrix = host_push_matrix,
        .pop_matrix = host_pop_matrix,
        .exit = host_exit,
    };
    memset(app.state, 0, STATE_BYTES);
    simgui_setup(&simgui_desc_t{});
    saudio_setup(&saudio_desc{ .sample_rate = AUDIO_RATE, .num_channels = 1 });

    start_game();
}

void frame() {
    poll_game();
    reload_changed_sprites();

    app.delta = cast(f32, sapp_frame_duration());
    app.time += app.delta;
    app.size = float2{sapp_widthf(), sapp_heightf()};
    vert_count = 0;
    batch_count = 0;
    matrix_top = 0;
    simgui_new_frame(&simgui_frame_desc_t{
        .width = sapp_width(),
        .height = sapp_height(),
        .delta_time = sapp_frame_duration(),
        .dpi_scale = sapp_dpi_scale(),
    });
    if tick_game != null { tick_game(&app); }
    keys_pressed = {};
    pump_audio();
    draw_tweaks();
    simgui_flush();

    // A broken compile keeps the old game running, under a red bar.
    if compile_failed { host_rect(Rect{float2{0.0f}, float2{app.size.x, 6.0f}}, float4{0.9f, 0.2f, 0.2f, 1.0f}); }

    if vert_count > 0 { sg_write_buffer_transient(&sg_write_buffer_desc{ .src.data.ptr = &verts, .src.data.size = vert_count * sizeof(Vert), .dst.buffer = vbuf }); }
    sg_begin_pass(&sg_pass{
        .action.colors[0].load_action = SG_LOADACTION_CLEAR,
        .action.colors[0].clear_value = sg_color{clear_color.x, clear_color.y, clear_color.z, 1.0f},
        .swapchain = sglue_swapchain(),
    });
    if vert_count > 0 {
        // pixels -> NDC, y down
        float4x4 mvp = float4x4{
            2.0f / app.size.x, 0.0f, 0.0f, 0.0f,
            0.0f, -2.0f / app.size.y, 0.0f, 0.0f,
            0.0f, 0.0f, 1.0f, 0.0f,
            -1.0f, 1.0f, 0.0f, 1.0f,
        };
        sg_pipeline current = {};
        for i32 i = 0; i < batch_count; i++ {
            var b = &batches[i];
            var p = b.floor ? floor_pip : pip;
            if p.id == 0 { continue; }
            if p.id != current.id {
                current = p;
                sg_apply_pipeline(p);
                sg_apply_uniforms(0, &sg_range{ .ptr = &mvp, .size = sizeof(mvp) });
                if b.floor { sg_apply_uniforms(1, &sg_range{ .ptr = &floor_uniforms, .size = sizeof(FloorParams) }); }
            }
            sg_apply_bindings(&sg_bindings{ .vertex_buffers[0] = vbuf, .views[0] = b.texture, .samplers[0] = nearest });
            sg_draw(b.start, b.count, 1);
        }
    }
    simgui_draw();
    sg_end_pass();
    sg_commit();
}

void cleanup() {
    stop_game();
    saudio_shutdown();
    simgui_shutdown();
    sg_shutdown();
}

sapp_desc foster_app(u8* title, i32 width, i32 height, str game, str sprites) {
    game_path = game;
    sprites_dir = sprites;
    return sapp_desc{
        .init_cb = init,
        .frame_cb = frame,
        .cleanup_cb = cleanup,
        .event_cb = on_event,
        .width = width,
        .height = height,
        .high_dpi = true,
        .window_title = title,
    };
}
