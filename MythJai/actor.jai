// Every actor type embeds Actor (`#as using actor: Actor`) and is noted @GameActor, which
// gives it a pool in context.actors (see JaiKit/actors_build.jai). Instead of virtuals, a type
// defines its own update/draw overloads where it wants them; these are the defaults.
//
// Actors sit on whole pixels and move a pixel at a time against the room's solid tiles, with
// the fraction kept in `remainder`, as in Myth's Actor.Move.

Actor :: struct {
    id: string;
    position: Point2;       // room pixels; the hitbox and sprite hang off it
    velocity: Vector2;      @NoSerialize
    remainder: Vector2;     @NoSerialize
    hitbox: RectInt;        @NoSerialize
    facing: s32 = 1;        @NoSerialize
    squish := Vector2.{1, 1};   @NoSerialize
    animation: string;      @NoSerialize
    animation_time: float;  @NoSerialize
    animation_looping := true;  @NoSerialize
    hit_cooldown: float;    @NoSerialize  // i-frames: can't be hit again while > 0
    alive: bool;            @NoSerialize
    generation: u32;        @NoSerialize
}

update :: (a: *Actor) {}
draw   :: (a: *Actor) { rect(.{to_vector(a.position) - Vector2.{3, 3}, .{6, 6}}, rgb(0xff00ff)); }

// Per-frame upkeep every actor does, like Myth's Actor.AdvanceFrame.
advance :: (a: *Actor, dt: float) {
    a.squish = approach(a.squish, .{1, 1}, dt * 4);
    a.hit_cooldown = approach(a.hit_cooldown, 0, dt);
    a.animation_time += dt;
}

play :: (a: *Actor, name: string, looping := true) {
    if a.animation != name a.animation_time = 0;
    a.animation = name;
    a.animation_looping = looping;
}

// The actor's sprite (Myth/Assets/Sources/Sprites/<name>.ase) in its current animation, facing and squish.
draw_sprite :: (a: *Actor, name: string) {
    sprite := get_sprite(name);
    if !sprite return;
    tag := find_tag(sprite, a.animation);
    draw_sprite(sprite, tag, a.animation_time, to_vector(a.position), .{a.facing * a.squish.x, a.squish.y}, a.animation_looping);
}

hitbox_rect :: (a: *Actor) -> RectInt {
    return .{a.position.x + a.hitbox.x, a.position.y + a.hitbox.y, a.hitbox.w, a.hitbox.h};
}

overlaps :: (a: RectInt, b: RectInt) -> bool {
    return a.x < b.x + b.w && b.x < a.x + a.w && a.y < b.y + b.h && b.y < a.y + a.h;
}

all_actors: Actors;
#add_context actors: *Actors;

Moved :: enum_flags u8 { HIT_X; HIT_Y; }

// Moves by `delta` pixels, carrying the fraction to the next call. Walking into a 1-pixel
// ledge steps up it, walking off one steps down, and hitting a wall stops that axis.
move :: (a: *Actor, delta: Vector2) -> Moved {
    a.remainder += delta;
    step := Point2.{xx a.remainder.x, xx a.remainder.y};  // toward zero, like Myth's (Point2) cast
    a.remainder -= to_vector(step);
    moved: Moved;

    while step.x {
        dir := Point2.{sign(xx step.x), 0};
        was_grounded := grounded(a);
        if move_pixel(a, dir) {
            if was_grounded && !grounded(a) a.position.y += 1;  // step down (off a cliff, just the first pixel)
        } else if was_grounded && !overlaps_solid(a, .{0, -1}) && !overlaps_solid(a, dir + Point2.{0, -1}) {
            a.position += dir + Point2.{0, -1};  // step up
        } else {
            a.velocity.x, a.remainder.x = 0, 0;
            moved |= .HIT_X;
            break;
        }
        step.x -= dir.x;
    }
    while step.y {
        dir := Point2.{0, sign(xx step.y)};
        if !move_pixel(a, dir) {
            a.velocity.y, a.remainder.y = 0, 0;
            moved |= .HIT_Y;
            break;
        }
        step.y -= dir.y;
    }
    return moved;
}

grounded :: (a: *Actor) -> bool { return overlaps_solid(a, .{0, 1}); }

// Fallen out of the bottom of the room.
fell_out :: (a: *Actor) -> bool { return a.position.y + a.hitbox.y >= room.size.y * TILE_SIZE; }

overlaps_solid :: (a: *Actor, offset := Point2.{}) -> bool {
    return overlaps_solid(.{a.position.x + offset.x + a.hitbox.x, a.position.y + offset.y + a.hitbox.y, a.hitbox.w, a.hitbox.h});
}

// Any solid tile under the rect (room pixels). Off the room is open, like Room.IsSolidTile:
// below it is a cliff fall, and the player is held at its other edges (keep_in_room).
overlaps_solid :: (r: RectInt) -> bool {
    if r.w <= 0 || r.h <= 0 return false;
    first := Point2.{tile_of(r.x), tile_of(r.y)};
    last := Point2.{tile_of(r.x + r.w - 1), tile_of(r.y + r.h - 1)};
    for y: first.y..last.y for x: first.x..last.x {
        if x < 0 || y < 0 || x >= room.size.x || y >= room.size.y continue;
        if room.tiles[y * room.size.x + x] != #char "0" return true;
    }
    return false;
}

#scope_file

tile_of :: (pixel: s32) -> s32 { return xx floor(cast(float) pixel / TILE_SIZE); }

move_pixel :: (a: *Actor, dir: Point2) -> bool {
    if overlaps_solid(a, dir) || dir.y > 0 && grounded(a) return false;
    a.position += dir;
    return true;
}

#scope_export

#load "sprites.jai";
#load "pawn.jai";
#load "actors/player.jai";
#load "actors/bud.jai";
#load "actors/player_start.jai";
#load "actors/blob.jai";
#load "actors/light.jai";
