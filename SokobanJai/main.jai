TILE    :: 32;
COLUMNS :: 10;
ROWS    :: 8;

LEVEL :: string.[
    "##########",
    "#        #",
    "#  .     #",
    "#  $     #",
    "#  @ $$ .#",
    "#     .  #",
    "#        #",
    "##########",
];

Point :: struct { x, y: s32; }
operator + :: (a: Point, b: Point) -> Point { return .{a.x + b.x, a.y + b.y}; }
operator - :: (a: Point, b: Point) -> Point { return .{a.x - b.x, a.y - b.y}; }
operator == :: (a: Point, b: Point) -> bool { return a.x == b.x && a.y == b.y; }
to_vector :: (p: Point) -> Vector2 { return .{xx p.x, xx p.y}; }
index :: (p: Point) -> s32 { return p.y * COLUMNS + p.x; }

MoveUndo :: struct { player: Point; box: Point; pushed: bool; }

map: [ROWS * COLUMNS] u8;
player: Point;
won: bool;
undo: [..] MoveUndo;
pressed: [..] SDL_Keycode;  // this frame
started: bool;
player_sprite: Sprite;
has_player_sprite: bool;
next_sprite_check: float64;

SPRITES :: "../Myth/Assets/Sources/Sprites";
FONTS   :: "../Myth/Assets/Fonts";
font: Font;
has_font: bool;

main :: () { while !frame() {} }

// One frame; true to quit. Under the hotload host this is the whole game: it runs from
// whichever build is newest, with every global carried over from the previous one.
frame :: () -> bool {
    if !started {
        if !SDL_Init(.VIDEO) { log_error("SDL_Init: %", to_string(SDL_GetError())); return true; }
        window := SDL_CreateWindow("Sokoban", 960, 540, .RESIZABLE | .HIGH_PIXEL_DENSITY);
        if !window || !gfx_init(window) { log_error("%", to_string(SDL_GetError())); return true; }
        player_sprite, has_player_sprite = load_sprite(tprint("%/player.ase", SPRITES));
        reset();
        started = true;
    }

    reset_temporary_storage();
    pressed.count = 0;
    event: SDL_Event;
    while SDL_PollEvent(*event) {
        if event.type == xx SDL_EventType.QUIT return true;
        if event.type == xx SDL_EventType.KEY_DOWN && !event.key.repeat array_add(*pressed, event.key.key);
    }
    if update() return true;
    render();
    pump_audio();
    return false;
}

// Seconds since SDL started; unlike seconds_since_init() it doesn't restart with each hotloaded build.
now :: () -> float64 { return SDL_GetTicksNS() / 1_000_000_000.0; }

// true to quit
update :: () -> bool {
    if is_pressed(SDLK_ESCAPE) return true;
    if has_player_sprite && now() > next_sprite_check {
        next_sprite_check = now() + 0.25;
        reload_if_changed(*player_sprite);
    }
    if is_pressed(SDLK_R) reset();
    if is_pressed(SDLK_Z) undo_move();

    move: Point;
    if      is_pressed(SDLK_W, SDLK_UP)    move = .{0, -1};
    else if is_pressed(SDLK_S, SDLK_DOWN)  move = .{0, 1};
    else if is_pressed(SDLK_A, SDLK_LEFT)  move = .{-1, 0};
    else if is_pressed(SDLK_D, SDLK_RIGHT) move = .{1, 0};
    if move != .{} && !won try_move(move);
    return false;
}

render :: () {
    board := Vector2.{COLUMNS * TILE, ROWS * TILE};
    size := screen_size();
    scale := max(1.0, floor(min(size.x / board.x, size.y / board.y)));
    origin := (size - board * scale) * 0.5;
    push_matrix(.{floor(origin.x), floor(origin.y)}, scale);
    for y: 0..ROWS-1 for x: 0..COLUMNS-1 draw_cell(.{xx x, xx y});
    pop_matrix();
    #if HOTLOAD draw_build_status(scale);
    present(rgb(0x16181d));
}

#if HOTLOAD {
    // The hotload host's state: a pulsing "building" while it compiles, the error while the last build is broken.
    draw_build_status :: (scale: float) {
        status := hotload_status;
        if !status.building && !status.error return;
        if !has_font font, has_font = load_font(tprint("%/m3x6.ttf", FONTS), 16);
        if !has_font return;

        push_matrix(.{}, scale);
        view := screen_size() / scale;
        if status.error {
            height := text_size(font, status.error).y + 4;
            rect(.{.{}, .{view.x, height}}, rgb(0x4a161c));
            text(font, status.error, .{4, 2}, rgb(0xffc8c8));
        }
        if status.building {
            pulse := 0.6 + 0.4 * sin(cast(float) now() * 8);
            text(font, "building", .{4, view.y - font.line_height - 2}, fade(rgb(0xf2cc60), pulse));
        }
        pop_matrix();
    }
}

reset :: () {
    undo.count = 0;
    won = false;
    for y: 0..ROWS-1 for x: 0..COLUMNS-1 {
        c := LEVEL[y][x];
        map[y * COLUMNS + x] = ifx c == #char "@" then #char " " else c;
        if c == #char "@" player = .{xx x, xx y};
    }
}

try_move :: (direction: Point) {
    next := player + direction;
    beyond := next + direction;
    box := map[index(next)] == #char "$";
    if map[index(next)] == #char "#" || box && (map[index(beyond)] == #char "#" || map[index(beyond)] == #char "$") {
        sfx_bump();
        return;
    }

    array_add(*undo, .{player, next, box});
    player = next;
    if box {
        map[index(next)] = #char " ";
        map[index(beyond)] = #char "$";
        sfx_push(beyond);
        if LEVEL[beyond.y][beyond.x] == #char "." sfx_goal();
    } else {
        sfx_step();
    }
    won = all_goals_filled();
    if won sfx_win();
}

undo_move :: () {
    if !undo.count return;
    move := pop(*undo);
    if move.pushed {
        map[index(move.box + (player - move.player))] = #char " ";
        map[index(move.box)] = #char "$";
    }
    player = move.player;
    won = false;
    sfx_undo();
}

all_goals_filled :: () -> bool {
    for y: 0..ROWS-1 for x: 0..COLUMNS-1
        if LEVEL[y][x] == #char "." && map[y * COLUMNS + x] != #char "$" return false;
    return true;
}

filled_goals :: () -> int {
    n := 0;
    for y: 0..ROWS-1 for x: 0..COLUMNS-1
        if LEVEL[y][x] == #char "." && map[y * COLUMNS + x] == #char "$" n += 1;
    return n;
}

// Steps from `cell` to the nearest goal without a box.
nearest_empty_goal :: (cell: Point) -> int {
    best := ROWS + COLUMNS;
    for y: 0..ROWS-1 for x: 0..COLUMNS-1 {
        if LEVEL[y][x] != #char "." || map[y * COLUMNS + x] == #char "$" continue;
        best = min(best, abs(x - cell.x) + abs(y - cell.y));
    }
    return best;
}

// --- sounds ---

PENTATONIC :: float.[523.25, 587.33, 659.25, 783.99, 880];

sfx_step :: () {
    foot := ifx undo.count & 1 then 1.12 else 1.0;  // alternating feet
    play(.{freq = 140 * foot, decay = 0.045, noise = 0.7, volume = 0.12});
}

sfx_bump :: () {
    play(.{freq = 55, sweep = -3, decay = 0.08, square = 0.6, noise = 0.4, volume = 0.25});
}

// Pitch rises as the box nears an empty goal: hot/cold by ear.
sfx_push :: (box: Point) {
    near := 1.0 / (1 + nearest_empty_goal(box));
    play(.{freq = 70 * (1 + near), sweep = -2.5, attack = 0.004, decay = 0.16, square = 0.35, noise = 0.35, volume = 0.4});
}

// One step up the scale per filled goal.
sfx_goal :: () {
    note := PENTATONIC[clamp(filled_goals(), 1, PENTATONIC.count) - 1];
    play(.{freq = note, attack = 0.004, decay = 0.7, volume = 0.25});
    play(.{freq = note * 1.5, attack = 0.004, decay = 0.4, volume = 0.08, delay = 0.03});
}

// A swell rather than a hit: time running backwards.
sfx_undo :: () {
    play(.{freq = 260, sweep = 2, attack = 0.12, decay = 0.02, square = 0.5, volume = 0.12});
}

sfx_win :: () {
    for PENTATONIC play(.{freq = it, attack = 0.004, decay = 0.9, volume = 0.18, delay = 0.15 + it_index * 0.09});
}

draw_cell :: (cell: Point) {
    base := Rect.{to_vector(cell) * TILE, .{TILE, TILE}};
    floor_color := ifx (cell.x + cell.y) & 1 then rgb(0x20262d) else rgb(0x242a31);
    rect(base, ifx map[index(cell)] == #char "#" then rgb(0x59616d) else floor_color);
    if LEVEL[cell.y][cell.x] == #char "." rect(inflate(base, -10), rgb(0xf2cc60));
    if map[index(cell)] == #char "$" rect(inflate(base, -5), rgb(0xc17f59));
    if cell != player return;
    if has_player_sprite draw_sprite(player_sprite, base.pos + Vector2.{TILE * 0.5, TILE}, now());
    else rect(inflate(base, -7), ifx won then rgb(0x7bd88f) else rgb(0x5fcde4));
}

is_pressed :: (keys: .. SDL_Keycode) -> bool {
    for keys if array_find(pressed, it) return true;
    return false;
}

#import "Kit";
#import "Basic";
#import "Math";
#import "SDL3";
