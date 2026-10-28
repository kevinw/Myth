// The player's HUD, as Myth draws it (Game.Rendering.Hud.cs): health in the "numbers" sprite,
// counting down to the real value so a hit visibly ticks away and pulsing red on the last hit's
// worth, over the burrow stamina bar. In view pixels, drawn unlit.

draw_hud :: () {
    for player: context.actors.players {
        y := VIEW.y - 14;
        // Burrow stamina: its width is the capacity; it drains while burrowing, pulsing toward
        // white, and stays dim after a burrow until it re-arms.
        PIXELS_PER_BURROW_SECOND :: 35.0;
        width := player.burrow.duration * PIXELS_PER_BURROW_SECOND;
        if width <= 0 width = BURROW_DURATION * PIXELS_PER_BURROW_SECOND;
        bar_opacity = approach(bar_opacity, ifx player.burrow.hud_spent then 0.5 else 1.0, BURROW_BAR_OPACITY_RATE * delta);
        bar := Color.{99, 155, 255, 200};
        if player.state == .BURROW {
            wave := 0.5 - 0.5 * cos(cast(float) now() * TAU / 0.13);
            bar = lerp(bar, lerp(bar, WHITE, 0.3), wave);
        }
        rect(.{.{0, y + 11}, .{width, 2}}, fade(.{30, 30, 30, 30}, bar_opacity));
        rect(.{.{0, y + 11}, .{width * player.burrow.hud_fill, 2}}, fade(bar, bar_opacity));

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
bar_opacity := 1.0;
BURROW_BAR_OPACITY_RATE :: 8.0;

lerp :: (a: Color, b: Color, t: float) -> Color {
    mix :: (x: u8, y: u8) -> u8 #expand { return xx (x + (cast(float) y - x) * t); }
    return .{mix(a.r, b.r), mix(a.g, b.g), mix(a.b, b.b), mix(a.a, b.a)};
}
