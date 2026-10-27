// Human Questions (Myth), the Jai port: a vertical slice that plays Myth's own rooms.

VIEW_TILES :: Point2.{40, 23};  // enough to cover Myth's 320x180 game resolution
VIEW :: Vector2.{320, 180};
ASSETS :: "../Myth/Assets";
ROOM :: "Rooms/Myth/super_simple.json";

started: bool;
room: Room;
camera: Vector2;            // top-left of the view, in room pixels
pressed: [..] SDL_Keycode;  // this frame
held: [..] SDL_Keycode;
delta: float;               // seconds this frame
last_ticks: u64;

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

    if demo demo_time += delta;
    update_all(context.actors);
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

    push_matrix(-Vector2.{floor(camera.x), floor(camera.y)}, 1);
    first := Point2.{xx floor(camera.x / TILE_SIZE), xx floor(camera.y / TILE_SIZE)};
    last := first + VIEW_TILES;
    for y: first.y..last.y for x: first.x..last.x {
        cell := Point2.{xx x, xx y};
        bounds := Rect.{to_vector(cell * TILE_SIZE), .{TILE_SIZE, TILE_SIZE}};
        back := tile_at(room.background, room, cell);
        if back != #char "0" rect(bounds, fade(tile_color(back), 0.4));
        front := tile_at(room.tiles, room, cell);
        if front != #char "0" rect(bounds, tile_color(front));
    }
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

// A stable color per tile type, from its legend name, so types are told apart before their real look is ported.
tile_color :: (c: u8) -> Color {
    name := room.legend[c];
    hash: u32 = 2166136261;
    for 0..name.count-1 hash = (hash ^ name[it]) * 16777619;
    return .{xx (90 + hash % 120), xx (70 + (hash >> 8) % 110), xx (60 + (hash >> 16) % 100), 255};
}

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
#import "Kit";
#import "Basic";
#import "Math";
#import "SDL3";
#import "System";
