Light :: struct @GameActor {
    #as using actor: Actor;
    radius: float = 48;                 @I(8,256)
    sprite_name := "fire";
    enabled_by_persistent_button_id: string;
    light_color := #run rgb(0xffd37a);
    intensity: float = 1;               @I(0,3)
    height: float = 16;                 @I(0,64)
    flicker_frequency: float = 4;       @I(0,20)
    flicker_intensity: float = 0.08;    @I(0,0.5)
    flicker_offset: float;              @NoSerialize
}

update :: (l: *Light) {
    if !l.flicker_offset l.flicker_offset = random_get_within_range(0.001, 3);
    advance(l, delta);
    play(l, "idle");
}

// The light's sprite is emissive: drawn over the lit scene, not lit by it.
draw :: (l: *Light) {}
draw_emissive :: (l: *Light) { if l.sprite_name draw_sprite(l, l.sprite_name); }

// Breathing: radius and intensity both scale by this, as in Myth's Light.RenderScale.
flicker_scale :: (l: *Light) -> float {
    flicker := 0.0;
    if l.flicker_frequency > 0 {
        t := l.animation_time;
        flicker = 0.7 * sin(t * l.flicker_frequency * 0.23 * TAU + l.flicker_offset * 1.7) + 0.3 * sin(t * l.flicker_frequency * TAU + l.flicker_offset);
    }
    return (1 + l.flicker_intensity) * (1 + flicker * l.flicker_intensity);
}
