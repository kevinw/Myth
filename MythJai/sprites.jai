// Sprites by name, loaded on first use from Myth's sources and reloaded when their files change.

get_sprite :: (name: string) -> *Sprite {
    for * sprite_cache if it.name == name return ifx it.loaded then *it.sprite else null;
    for * sprite_cache if !it.name {
        it.name = copy_string(name);
        it.sprite, it.loaded = load_sprite(tprint("%/Sources/Sprites/%.ase", ASSETS, name));
        if !it.loaded it.sprite, it.loaded = load_sprite(tprint("%/Sources/Sprites/%.aseprite", ASSETS, name));
        if !it.loaded log_error("no sprite %", name);
        return ifx it.loaded then *it.sprite else null;
    }
    return null;
}

reload_changed_sprites :: () {
    for * sprite_cache if it.loaded reload_if_changed(*it.sprite);
}

#scope_file

sprite_cache: [64] struct { name: string; sprite: Sprite; loaded: bool; };
