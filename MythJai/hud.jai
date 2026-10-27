// The player's HUD, as Myth draws it (Game.Rendering.Hud.cs): health in the "numbers" sprite,
// counting down to the real value so a hit visibly ticks away and pulsing red on the last hit's
// worth, over the burrow stamina bar. In view pixels, drawn unlit.

draw_hud :: () {
    for player: context.actors.players {
        y := VIEW.y - 14;
        // Burrow stamina (always full until burrowing is ported).
        BURROW_SECONDS :: 1.133;
        PIXELS_PER_BURROW_SECOND :: 35.0;
        rect(.{.{0, y + 11}, .{BURROW_SECONDS * PIXELS_PER_BURROW_SECOND, 2}}, fade(.{99, 155, 255, 255}, 200.0 / 255));

        health := cast(float) max(player.health, 0);
        if displayed_health < 0 || health > displayed_health displayed_health = health;
        else displayed_health = approach(displayed_health, health, HEALTH_COUNT_RATE * delta);
        low := ifx health <= HIT_DAMAGE then 0.5 + 0.5 * sin(cast(float) now() * TAU / HEALTH_LOW_PULSE_SECONDS) else 0.0;
        color := lerp(WHITE, Color.{255, 89, 89, 255}, low);
        color = fade(color, 0.75);

        digits := get_sprite("numbers");
        if !digits return;
        text := tprint("%", cast(int) ceil(displayed_health));
        at := Vector2.{2, y};
        for 0..text.count-1 {
            draw_frame(digits, text[it] - #char "0", at, color);
            at.x += digits.frame_size.x;
        }
    }
}

#scope_file

HEALTH_COUNT_RATE :: 60.0;          // points per second the readout falls toward a hit
HEALTH_LOW_PULSE_SECONDS :: 0.8;

displayed_health := -1.0;

lerp :: (a: Color, b: Color, t: float) -> Color {
    mix :: (x: u8, y: u8) -> u8 #expand { return xx (x + (cast(float) y - x) * t); }
    return .{mix(a.r, b.r), mix(a.g, b.g), mix(a.b, b.b), mix(a.a, b.a)};
}
