// Myth's map overlay (Minimap, GameView.DrawMinimap): Escape, or a gamepad's Start or Back,
// toggles it, and the game pauses underneath. It dims the view and draws the room a pixel to a
// tile, its solid ground darker, the player a blinking dot; and since it doubles as the status
// screen, the counts of what's been collected slide in at the top while it's up (DrawCurrencyHud).
// Not ported: other rooms and their exits (there's just the one), the baked world map's colors,
// map pins, save point warps.

map_open: bool;

toggle_map :: () {
    map_open = !map_open;
    for context.actors.players sound(ifx map_open then "map/open" else "map/close", it.position);
}

// In view pixels, over the lit view (so the CRT takes it too).
draw_map :: () {
    FADE_SPEED :: 10.0;
    DIM :: 0.8;
    ROOM_FILL :: #run fade(rgb(0x241a3e), 50.0 / 255);  // premultiplied, as Foster's Color(rgb, alpha) is
    WALL :: Color.{0, 0, 0, 80};
    REVEAL_SPEED :: 3.5;
    map_opacity = approach(map_opacity, ifx map_open then 1.0 else 0.0, FADE_SPEED * delta);
    currency_reveal = approach(currency_reveal, ifx map_open then 1.0 else 0.0, REVEAL_SPEED * delta);

    if map_opacity > 0.001 {
        rect(.{.{}, VIEW}, fade(rgb(0), DIM * map_opacity));
        // Centered on the room, a pixel per tile.
        origin := VIEW * 0.5 - to_vector(room.size) * 0.5;
        origin = .{floor(origin.x), floor(origin.y)};
        rect(.{origin, to_vector(room.size)}, fade(ROOM_FILL, map_opacity));
        for y: 0..room.size.y-1 for x: 0..room.size.x-1 if room.tiles[y * room.size.x + x] != #char "0"
            rect(.{origin + Vector2.{xx x, xx y}, .{1, 1}}, fade(WALL, map_opacity));
        // The player blinks: off a fifth of a second, on three fifths.
        if fmod_cycling(cast(float) now(), 0.8) >= 0.2 for context.actors.players {
            at := origin + to_vector(it.position) / TILE_SIZE;
            rect(.{.{floor(at.x) - 1, floor(at.y) - 1}, .{2, 2}}, fade(WHITE, map_opacity));
        }
    }

    if currency_reveal > 0.001 {
        if !counter_font.texture.gpu counter_font = load_font(tprint("%/Fonts/dogica.ttf", ASSETS), 8);
        ease := currency_reveal * currency_reveal * (3 - 2 * currency_reveal);
        tint := fade(WHITE, ease);
        x, y := 4.0, -16 + 20 * ease;
        mycelon := get_sprite("mycelon");
        if mycelon { draw_frame(mycelon, 0, .{x, y}, tint); x += mycelon.frame_size.x + 3; }
        text(counter_font, "0", .{x, y}, tint);  // nothing to collect in this room yet
        x += text_size(counter_font, "0").x + 8;
        duck := get_sprite("Props/duck");
        if duck {
            push_matrix(.{x, y}, 0.5);
            draw_frame(duck, 0, .{}, tint);
            pop_matrix();
            x += duck.frame_size.x * 0.5 + 3;
        }
        text(counter_font, "0", .{x, y}, tint);
    }
}

#scope_file

map_opacity: float;
currency_reveal: float;
counter_font: Font;

#import "Basic";
#import "Math";
#import "Kit";
