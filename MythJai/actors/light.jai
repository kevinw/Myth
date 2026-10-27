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

update :: (l: *Light) { advance(l, delta); }

draw :: (l: *Light) {
    if l.sprite_name { play(l, "idle"); draw_sprite(l, l.sprite_name); }
}
