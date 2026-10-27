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
}

// Until lighting is ported: the light's reach, faintly, and its source.
draw :: (l: *Light) {
    reach := l.radius * 0.5;
    rect(.{to_vector(l.position) - Vector2.{reach, reach}, .{reach * 2, reach * 2}}, fade(l.light_color, 0.06 * l.intensity));
    rect(.{to_vector(l.position) - Vector2.{2, 2}, .{4, 4}}, l.light_color);
}
