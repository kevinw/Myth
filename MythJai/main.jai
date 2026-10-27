// Human Questions (Myth), the Jai port: a vertical slice that plays Myth's own rooms.

VIEW :: Vector2.{320, 180};
ASSETS :: "../Myth/Assets";
ROOM :: "Rooms/Myth/super_simple.json";

started: bool;
room: Room;
pack: Pack;
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
    render();
    return false;
}

render :: () {
    size := screen_size();
    scale := max(1.0, floor(min(size.x / VIEW.x, size.y / VIEW.y)));
    origin := (size - VIEW * scale) * 0.5;
    push_matrix(.{floor(origin.x), floor(origin.y)}, scale);
    rect(.{.{}, VIEW}, room.backdrop);

    shaken := camera;
    if shake_timer > 0 shaken += Vector2.{random_get_within_range(-1, 1), random_get_within_range(-1, 1)} * 2;
    push_matrix(-Vector2.{floor(shaken.x), floor(shaken.y)}, 1);
    draw_tiles(tile_draws.background);
    draw_tiles(tile_draws.foreground);
    draw_all(context.actors);
    pop_matrix();
    pop_matrix();

    // Letterbox: cover whatever the world drew outside the view.
    black := rgb(0);
    rect(.{.{}, .{size.x, origin.y}}, black);
    rect(.{.{0, origin.y + VIEW.y * scale}, .{size.x, size.y}}, black);
    rect(.{.{}, .{origin.x, size.y}}, black);
    rect(.{.{origin.x + VIEW.x * scale, 0}, .{size.x, size.y}}, black);
    present(black);
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
#import "Kit";
#import "Basic";
#import "Math";
#import "SDL3";
#import "System";
#import "Random";
