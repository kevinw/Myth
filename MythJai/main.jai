// Human Questions (Myth), the Jai port: a vertical slice that plays Myth's own rooms.

VIEW :: Vector2.{320, 180};
ASSETS :: "../Myth/Assets";
ROOM :: "Rooms/Myth/super_simple.json";

started: bool;
room: Room;
pack: Pack;
scene: Texture;             // the world, unlit, at the view's resolution
lighting: Shader;
camera: Vector2;            // top-left of the view, in room pixels
pressed: [..] SDL_Keycode;  // this frame
held: [..] SDL_Keycode;
delta: float;               // seconds this frame
last_ticks: u64;
hit_stun_timer: float;      // the world freezes briefly on a hit
shake_timer: float;
next_sprite_check: float64;

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
        spawn_player(start_position());
        demo = array_find(get_command_line_arguments(), "demo");
        camera = clamp_camera(camera_target());
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
    last_ticks = ticks;

    if now() > next_sprite_check {
        next_sprite_check = now() + 0.25;
        reload_changed_sprites();
    }
    shake_timer = approach(shake_timer, 0, delta);
    if hit_stun_timer > 0 {
        hit_stun_timer -= delta;
    } else {
        if demo demo_time += delta;
        update_all(context.actors);
    }
    camera = clamp_camera(camera_target());
    update_sound(camera + VIEW * 0.5);
    render();
    return false;
}

render :: () {
    if !scene.gpu scene = make_target(xx VIEW.x, xx VIEW.y);
    if !lighting.pipelines[0] lighting = load_shader("lighting", samplers = 2, uniforms = 1);

    size := screen_size();
    scale := max(1.0, floor(min(size.x / VIEW.x, size.y / VIEW.y)));
    origin := (size - VIEW * scale) * 0.5;
    origin = .{floor(origin.x), floor(origin.y)};
    view := camera;
    if shake_timer > 0 view += Vector2.{random_get_within_range(-1, 1), random_get_within_range(-1, 1)} * 2;
    view = .{floor(view.x), floor(view.y)};

    // The world, unlit.
    begin_pass(scene, clear = room.backdrop);
    push_matrix(-view, 1);
    draw_tiles(tile_draws.background);
    draw_tiles(tile_draws.foreground);
    draw_all(context.actors);
    pop_matrix();

    // Lit onto the window.
    uniforms := lighting_uniforms(view);
    begin_pass(clear = rgb(0), shader = *lighting, uniforms = .{size_of(type_of(uniforms)), xx *uniforms}, extra = .[pack.lighting_noise]);
    push_matrix(origin, scale);
    image(scene, .{.{}, VIEW}, .{});

    // Unlit on top: what glows.
    begin_pass(keep = true);
    push_matrix(-view, 1);
    for context.actors.lights draw_emissive(it);
    pop_matrix();
    draw_hud();
    pop_matrix();
    present();
}

MAX_LIGHTS :: 16;
Lighting_Uniforms :: struct {
    view, ambient, noise, noise_scale, response, dither, counts: Vector4;
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
    u.dither = .{DITHER_STRENGTH, 0, 0, 0};
    count := 0;
    for context.actors.lights {
        if count == MAX_LIGHTS || it.intensity <= 0 continue;
        scale := flicker_scale(it);
        c := it.intensity * scale / 255.0;
        u.lights[count * 2] = .{xx it.position.x, xx it.position.y, it.radius * scale, 0};
        u.lights[count * 2 + 1] = .{it.light_color.r * c, it.light_color.g * c, it.light_color.b * c, 0};
        count += 1;
    }
    u.counts.x = xx count;
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
#load "hud.jai";
#load "sound.jai";
#import "Kit";
#import "Basic";
#import "Math";
#import "SDL3";
#import "System";
#import "Random";
