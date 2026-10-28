// Human Questions (Myth), the Jai port: a vertical slice that plays Myth's own rooms.

VIEW :: Vector2.{320, 180};
ASSETS :: "../Myth/Assets";
ROOM :: "Rooms/Myth/super_simple.json";

started: bool;
room: Room;
pack: Pack;
scene: Texture;             // the background (backdrop, back tiles), unlit, at the view's resolution
front: Texture;             // the foreground (tiles, actors) over it, unlit
tile_field: Texture;        // which dither pattern lights each pixel (tiles.jai's draw_tile_field)
actor_mask: Texture;        // r: actors that cast sprite shadows; g: background tiles; b: the player and enemies, barely dithered (Myth's ActorDitherAmount)
normals: Texture;           // its normals: rgb a direction, a how much it bends the light
lit: Texture;               // the world lit, before the CRT pass scales it to the window
lighting: Shader;
crt: Shader;                // Myth's own Assets/Shaders/Crt.hlsl
no_tile_field: Texture;
camera: Vector2;            // top-left of the view, in room pixels
pressed: [..] SDL_Keycode;  // this frame
held: [..] SDL_Keycode;
delta: float;               // seconds this frame
last_ticks: u64;
hit_stun_timer: float;      // the world freezes briefly on a hit
shake_timer: float;
next_sprite_check: float64;
show_light_mask: bool;      // `myth lightmask`: the background's light alone, like Myth's LightMask view
shot_path: string;          // `myth shot <path> [frame]`: save the lit view there at that frame (default 90) and quit
shot_frame := 90;
frame_count: int;

main :: () { while !frame() {} }

// One frame; true to quit. Under the hotload host this is the whole game: it runs from
// whichever build is newest, with every global carried over from the previous one.
frame :: () -> bool {
    if !started {
        if !SDL_Init(.VIDEO) { log_error("SDL_Init: %", to_string(SDL_GetError())); return true; }
        window := SDL_CreateWindow("Human Questions", 1280, 720, .RESIZABLE | .HIGH_PIXEL_DENSITY);
        if !window || !gfx_init(window) { log_error("%", to_string(SDL_GetError())); return true; }
        ok: bool;
        room, ok = load_room(tprint("%/%", ASSETS, ROOM));
        if !ok { log_error("can't load %", ROOM); return true; }
        pack, ok = load_pack();
        if !ok { log_error("can't load the visual pack"); return true; }
        build_tile_draws();
        context.actors = *all_actors;
        for room.actors.items if !spawn(context.actors, it) log_error("no actor type %", as_string(get(it, "Type")));
        spawn_bud(spawn_player(start_position()));
        args := get_command_line_arguments();
        demo = array_find(args, "demo");
        show_light_mask = array_find(args, "lightmask");
        found, at := array_find(args, "shot");
        if found && at + 1 < args.count shot_path = copy_string(args[at + 1]);
        if found && at + 2 < args.count shot_frame = string_to_int(args[at + 2]);
        render_offscreen = shot_path != "";  // a capture runs whether or not the window can show
        camera = clamp_camera(camera_target());
        load_story();
        started = true;
    }

    context.actors = *all_actors;
    reset_temporary_storage();
    pressed.count = 0;
    event: SDL_Event;
    while SDL_PollEvent(*event) {
        if event.type == xx SDL_EventType.QUIT return true;
        if event.type == xx SDL_EventType.KEY_DOWN && !event.key.repeat {
            array_add(*pressed, event.key.key);
            array_add_if_unique(*held, event.key.key);
        }
        if event.type == xx SDL_EventType.KEY_UP array_unordered_remove_by_value(*held, event.key.key);
    }
    if array_find(pressed, SDLK_ESCAPE) return true;

    ticks := SDL_GetTicksNS();
    delta = ifx last_ticks then min(cast(float) (ticks - last_ticks) / 1_000_000_000, 1.0 / 20) else 1.0 / 60;
    if shot_path delta = 1.0 / 60;  // captures step evenly, so a frame number is a moment
    last_ticks = ticks;

    if now() > next_sprite_check {
        next_sprite_check = now() + 0.25;
        reload_changed_sprites();
    }
    shake_timer = approach(shake_timer, 0, delta);
    if hit_stun_timer > 0 {
        hit_stun_timer -= delta;
    } else {
        if demo { push_demo_keys(demo_time, demo_time + delta); demo_time += delta; }
        update_all(context.actors);
        update_story();
    }
    camera = clamp_camera(camera_target());
    update_sound(camera + VIEW * 0.5);
    render();
    frame_count += 1;
    if shot_path && frame_count == shot_frame {
        if !save_png(lit, shot_path) log_error("can't write %", shot_path);
        return true;
    }
    return false;
}

render :: () {
    if !scene.gpu scene = make_target(xx VIEW.x, xx VIEW.y);
    if !front.gpu front = make_target(xx VIEW.x, xx VIEW.y);
    if !tile_field.gpu tile_field = make_target(xx VIEW.x, xx VIEW.y);
    if !actor_mask.gpu actor_mask = make_target(xx VIEW.x, xx VIEW.y);
    if !normals.gpu normals = make_target(xx VIEW.x, xx VIEW.y);
    if !lit.gpu lit = make_target(xx VIEW.x, xx VIEW.y);
    if !crt.pipelines[0] crt = load_shader("Crt", samplers = 2, uniforms = 1);
    if !no_tile_field.gpu { clear: Color; no_tile_field = make_texture(xx *clear, 1, 1); }
    if !lighting.pipelines[0] lighting = load_shader("lighting", samplers = 7, uniforms = 1);

    size := screen_size();
    scale := max(1.0, floor(min(size.x / VIEW.x, size.y / VIEW.y)));
    origin := (size - VIEW * scale) * 0.5;
    origin = .{floor(origin.x), floor(origin.y)};
    view := camera;
    if shake_timer > 0 view += Vector2.{random_get_within_range(-1, 1), random_get_within_range(-1, 1)} * 2;
    view = .{floor(view.x), floor(view.y)};

    // The world, unlit: the background, and the foreground over a transparent clear.
    begin_pass(scene, clear = room.backdrop);
    push_matrix(-view, 1);
    draw_tiles(tile_draws.background);
    pop_matrix();
    begin_pass(front);
    push_matrix(-view, 1);
    draw_tiles(tile_draws.foreground);
    draw_all(context.actors);
    pop_matrix();

    // The tiles' normals. Elsewhere it's flat and weightless (Myth's NormalBuffer clear).
    begin_pass(normals, clear = .{128, 128, 255, 0});
    push_matrix(-view, 1);
    draw_tiles(tile_draws.background, normals = true);
    draw_tiles(tile_draws.foreground, normals = true);
    pop_matrix();

    begin_pass(tile_field);
    push_matrix(-view, 1);
    draw_tile_field();
    pop_matrix();

    // r: what casts sprite shadows (not the player or Bud: their glow would shadow itself, and
    // Myth leaves light emitters out); g: background tiles, where ambient occlusion lands;
    // b: what's barely dithered (not Bud).
    begin_pass(actor_mask);
    push_matrix(-view, 1);
    silhouette = .{0, 255, 0, 255};
    draw_tiles(tile_draws.background);
    silhouette = .{0, 0, 255, 255};
    for context.actors.players draw(it);
    silhouette = .{255, 0, 255, 255};
    for context.actors.blobs draw(it);
    silhouette = .{};
    pop_matrix();

    // Lit.
    uniforms := lighting_uniforms(view);
    begin_pass(lit, clear = rgb(0), shader = *lighting, uniforms = .{size_of(type_of(uniforms)), xx *uniforms}, extra = .[pack.lighting_noise, normals, front, tile_field, shadow_depths, actor_mask]);
    image(scene, .{.{}, VIEW}, .{});

    // Unlit on top: what glows.
    begin_pass(lit, keep = true);
    push_matrix(-view, 1);
    for context.actors.lights draw_emissive(it);
    pop_matrix();
    draw_speech(view);

    // Through Myth's CRT filter onto the window, with Myth's default settings (Manager.Visuals).
    output := VIEW * scale;
    crt_uniforms := float.[output.x, output.y, 0.09, 0.06, 1.5, 0, 0.35, 0.1, 1, scale, 0, 0.06, 1];
    begin_pass(clear = rgb(0), shader = *crt, uniforms = .{size_of(type_of(crt_uniforms)), xx crt_uniforms.data}, extra = .[no_tile_field]);
    push_matrix(origin, scale);
    image(lit, .{.{}, VIEW}, .{});

    begin_pass(keep = true);
    draw_hud();
    pop_matrix();
    present();
}

MAX_LIGHTS :: 16;
Lighting_Uniforms :: struct {
    view, ambient, noise, noise_scale, response, dither, counts, normal_light, sprite_shadow: Vector4;
    lights: [MAX_LIGHTS * 2] Vector4;
}

// Myth's defaults: Manager.Visuals and Room.
LIGHT_NOISE :: 0.30;
LIGHT_NOISE_PIVOT :: 0.48;
LIGHT_NOISE_DRIFT :: Vector2.{5, 0};    // world pixels per second
BACKGROUND_EXPOSURE :: 1.0;
RIM_STRENGTH :: 0.18;
SHOULDER_KNEE :: 0.9;
SHOULDER_CEILING :: 1.3;
DITHER_STRENGTH :: 0.155;
DITHER_SCALE :: 1.0;
NORMAL_LIGHT_FLOOR :: 0.12;
NORMAL_LIGHT_GAIN :: 2.15;
SHADOW_BIAS :: 0.5;
SHADOW_SOURCE_RADIUS :: 2.3;
SPRITE_SHADOW_STRENGTH :: 0.5;
SPRITE_SHADOW_MIN_LENGTH :: 4.0;
SPRITE_SHADOW_MAX_LENGTH :: 28.0;

lighting_uniforms :: (view: Vector2) -> Lighting_Uniforms {
    u: Lighting_Uniforms;
    u.view = .{VIEW.x, VIEW.y, view.x, view.y};
    k := room.ambient_intensity / 255.0;
    u.ambient = .{room.ambient.r * k, room.ambient.g * k, room.ambient.b * k, room.ambient_intensity};
    noise_size := pack.lighting_noise.size;
    if noise_size.x > 0 {
        drift := LIGHT_NOISE_DRIFT * cast(float) now() / noise_size;
        u.noise = .{LIGHT_NOISE, LIGHT_NOISE_PIVOT, drift.x - floor(drift.x), drift.y - floor(drift.y)};
        u.noise_scale = .{1 / noise_size.x, 1 / noise_size.y, 0, 0};
    }
    u.response = .{BACKGROUND_EXPOSURE, RIM_STRENGTH, SHOULDER_KNEE, SHOULDER_CEILING};
    u.dither = .{DITHER_STRENGTH, DITHER_SCALE, 0, 0};
    u.normal_light = .{NORMAL_LIGHT_FLOOR, NORMAL_LIGHT_GAIN, 0, 0};
    u.sprite_shadow = .{SPRITE_SHADOW_STRENGTH, SPRITE_SHADOW_MIN_LENGTH, SPRITE_SHADOW_MAX_LENGTH, 0};
    count := 0;
    positions: [MAX_LIGHTS] Vector2;
    for context.actors.lights {
        if count == MAX_LIGHTS || it.intensity <= 0 continue;
        scale := flicker_scale(it);
        c := it.intensity * scale / 255.0;
        // The soft shadow's penumbra breathes with the flicker (Light.RenderShadowSourceRadius).
        source_radius := SHADOW_SOURCE_RADIUS * (1 + (scale / (1 + it.flicker_intensity) - 1) * 0.45);
        positions[count] = .{xx it.position.x, xx it.position.y};
        u.lights[count * 2] = .{positions[count].x, positions[count].y, it.radius * scale, it.height};
        u.lights[count * 2 + 1] = .{it.light_color.r * c, it.light_color.g * c, it.light_color.b * c, source_radius};
        count += 1;
    }
    update_shadow_depths(array_view(positions, 0, count));
    u.counts = .{xx count, SHADOW_BIAS, xx show_light_mask, 0};
    return u;
}

hit_stun :: (seconds: float) { hit_stun_timer = max(hit_stun_timer, seconds); }
shake :: (seconds: float) { shake_timer = max(shake_timer, seconds); }
now :: () -> float64 { return SDL_GetTicksNS() / 1_000_000_000.0; }

key_down :: (key: SDL_Keycode) -> bool { return array_find(held, key); }
key_pressed :: (key: SDL_Keycode) -> bool { return array_find(pressed, key); }

start_position :: () -> Point2 {
    for context.actors.player_starts return it.position;
    return .{};
}

// The view centered on the player.
camera_target :: () -> Vector2 {
    for context.actors.players return to_vector(it.position) - Vector2.{0, STANDING_HEIGHT / 2} - VIEW * 0.5;
    return camera;
}

// Keeps the view inside the room, or centers it when the room is smaller.
clamp_camera :: (target: Vector2) -> Vector2 {
    limit := to_vector(room.size * TILE_SIZE) - VIEW;
    return .{
        ifx limit.x > 0 then clamp(target.x, 0, limit.x) else limit.x * 0.5,
        ifx limit.y > 0 then clamp(target.y, 0, limit.y) else limit.y * 0.5,
    };
}

#load "room.jai";
#load "actor.jai";
#load "pack.jai";
#load "tiles.jai";
#load "shadows.jai";
#load "story.jai";
#load "hud.jai";
#load "sound.jai";
#load "ozz.jai";
#load "rig.jai";
#import "Kit";
#import "Basic";
#import "Math";
#import "SDL3";
#import "System";
#import "Random";
