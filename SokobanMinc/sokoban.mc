// Sokoban, the hot-reloadable game module. Edit + save while it runs.

import api;

const i32 Tile = 32;
const i32 Columns = 10;
const i32 Rows = 8;

str[Rows] Level = {
    "##########",
    "#        #",
    "#  .     #",
    "#  $     #",
    "#  @ $$ .#",
    "#     .  #",
    "#        #",
    "##########",
};

union Option<T> { Some(T), None }
struct MoveUndo { int2 player; Option<int2> box; }
enum PlayState { PLAYING, WON }

// Lives in host.state. A layout change (new size) resets the level on reload.
struct State {
    i64 layout;
    PlayState play;
    int2 player;
    u8:[Rows][Columns] map;
    MoveUndo[4096] undo;    // oldest moves fall off when full
    i32 undo_count;
    f64 moved_at;
    f64 pushed_at;
    f64 undone_at;
    f64 won_at;
    int2 push_cell;     // where the last pushed box landed
    int2 push_dir;
    f32 warmth;         // eases toward the share of goals filled
}

State* s;

export u32 game_version() { return API_VERSION; }

export void game_tick(Host* h) {
    host = h;
    s = cast(State*, h.state);
    if s.layout != sizeof(State) { reset(); }

    if pressed(KEY_ESCAPE) { host.exit(); }
    if pressed(KEY_R) { reset(); }
    if pressed(KEY_Z) { undo(); }

    var move = int2{0};
    if pressed(KEY_W, KEY_UP) { move = int2{0, -1}; }
    else if pressed(KEY_S, KEY_DOWN) { move = int2{0, 1}; }
    else if pressed(KEY_A, KEY_LEFT) { move = int2{-1, 0}; }
    else if pressed(KEY_D, KEY_RIGHT) { move = int2{1, 0}; }

    if !eq(move, int2{0}) && s.play == PLAYING { try_move(move); }

    render();
}

void render() {
    clear(rgb(0x16181d));

    var board = to_float2(int2{Columns, Rows} * Tile);
    var fit = floor2(host.size / board);
    var scale = max(1.0f, min(fit.x, fit.y));
    push_matrix(floor2((host.size - board * scale) * 0.5f), scale);
    var params = floor_params();
    floor_quad(Rect{float2{0.0f}, board}, &params);
    for i32 y = 0; y < Rows; y++ {
        for i32 x = 0; x < Columns; x++ { draw_cell(int2{x, y}); }
    }
    pop_matrix();
}

FloorParams floor_params() {
    f32 since_push = cast(f32, host.time - s.pushed_at);
    f32 since_undo = cast(f32, host.time - s.undone_at);
    f32 since_win = s.play == WON ? cast(f32, host.time - s.won_at) : -1.0f;
    var p = FloorParams{
        .push = float4{to_float2(s.push_cell), to_float2(s.push_dir)},
        .since = float4{since_push, since_undo, since_win, 0.0f},
        .style = float4{
            tweak("floor/ring radius", 0.28f, 0.05f, 0.5f),
            tweak("floor/breathe speed", 2.5f, 0.0f, 10.0f),
            tweak("floor/ripple speed", 3.5f, 0.5f, 10.0f),
            tweak("floor/win wave speed", 7.0f, 1.0f, 20.0f),
        },
        .goal_color = tweak_color("floor/goal", 0xf2c75c),
    };

    i32 goals = 0;
    i32 filled = 0;
    for i32 y = 0; y < Rows; y++ {
        for i32 x = 0; x < Columns; x++ {
            var c = int2{x, y};
            if level_at(c) != '.' || goals == MAX_GOALS { continue; }
            var on = at(c) == '$';
            if on { filled++; }
            p.goals[goals++] = float4{to_float2(c), on ? 1.0f : 0.0f, 1.0f};
        }
    }
    var progress = goals == 0 ? 0.0f : cast(f32, filled) / cast(f32, goals);
    s.warmth += (progress - s.warmth) * min(1.0f, host.delta * 3.0f);
    p.board = float4{cast(f32, Columns), cast(f32, Rows), cast(f32, host.time), s.warmth};
    return p;
}

u8 at(int2 p) { return s.map[p.y][p.x]; }
void put(int2 p, u8 c) { s.map[p.y][p.x] = c; }
u8 level_at(int2 p) { return Level[p.y].data[p.x]; }

void reset() {
    *s = State{};
    s.layout = sizeof(State);
    s.pushed_at = -100.0;
    s.undone_at = -100.0;
    for i32 y = 0; y < Rows; y++ {
        for i32 x = 0; x < Columns; x++ {
            var p = int2{x, y};
            var c = level_at(p);
            put(p, c == '@' ? ' ' : c);
            if c == '@' { s.player = p; }
        }
    }
}

void try_move(int2 dir) {
    var next = s.player + dir;
    var beyond = next + dir;
    var box = at(next) == '$';
    if at(next) == '#' || box && (at(beyond) == '#' || at(beyond) == '$') {
        sfx_bump();
        return;
    }

    if s.undo_count == 4096 {
        for i32 i = 1; i < 4096; i++ { s.undo[i - 1] = s.undo[i]; }
        s.undo_count--;
    }
    s.undo[s.undo_count++] = MoveUndo{s.player, box ? Some(next) : None};
    s.player = next;
    s.moved_at = host.time;
    if box {
        put(next, ' ');
        put(beyond, '$');
        s.pushed_at = host.time;
        s.push_cell = beyond;
        s.push_dir = dir;
        sfx_push(beyond);
        if level_at(beyond) == '.' { sfx_goal(); }
    }
    else { sfx_step(); }
    if won() {
        s.play = WON;
        s.won_at = host.time;
        sfx_win();
    }
}

void undo() {
    if s.undo_count == 0 { return; }
    var move = s.undo[--s.undo_count];

    var dir = s.player - move.player;
    switch move.box {
        case Some(box): {
            put(box + dir, ' ');
            put(box, '$');
        }
        case None: {}
    }
    s.player = move.player;
    s.moved_at = host.time;
    s.undone_at = host.time;
    s.play = PLAYING;
    sfx_undo();
}

bool won() {
    for i32 y = 0; y < Rows; y++ {
        for i32 x = 0; x < Columns; x++ {
            var p = int2{x, y};
            if level_at(p) == '.' && at(p) != '$' { return false; }
        }
    }
    return true;
}

// --- sound: each effect reads the puzzle state ---

f32[5] Pentatonic = { 523.25f, 587.33f, 659.25f, 783.99f, 880.0f };

i32 filled_goals() {
    i32 n = 0;
    for i32 y = 0; y < Rows; y++ {
        for i32 x = 0; x < Columns; x++ {
            var c = int2{x, y};
            if level_at(c) == '.' && at(c) == '$' { n++; }
        }
    }
    return n;
}

i32 nearest_empty_goal(int2 cell) {
    i32 best = Rows + Columns;
    for i32 y = 0; y < Rows; y++ {
        for i32 x = 0; x < Columns; x++ {
            var c = int2{x, y};
            if level_at(c) != '.' || at(c) == '$' { continue; }
            var d = c - cell;
            i32 steps = (d.x < 0 ? -d.x : d.x) + (d.y < 0 ? -d.y : d.y);
            if steps < best { best = steps; }
        }
    }
    return best;
}

void sfx_step() {
    var foot = (s.undo_count & 1) == 0 ? 1.0f : 1.12f;
    play(Sound{ .freq = tweak("sfx/step pitch", 140.0f, 40.0f, 400.0f) * foot, .decay = 0.045f, .noise = 0.7f, .volume = 0.12f });
}

void sfx_bump() {
    play(Sound{ .freq = 55.0f, .sweep = -3.0f, .decay = 0.08f, .square = 0.6f, .noise = 0.4f, .volume = 0.25f });
}

// Pitch rises as the box nears an empty goal: hot/cold by ear.
void sfx_push(int2 box) {
    var near = 1.0f / (1.0f + cast(f32, nearest_empty_goal(box)));
    var pitch = tweak("sfx/push pitch", 70.0f, 30.0f, 200.0f) * (1.0f + near);
    play(Sound{ .freq = pitch, .sweep = -2.5f, .attack = 0.004f, .decay = 0.16f, .square = 0.35f, .noise = 0.35f, .volume = 0.4f });
}

// One step up the scale per filled goal.
void sfx_goal() {
    var filled = filled_goals();
    var note = Pentatonic[filled < 1 ? 0 : filled > 5 ? 4 : filled - 1];
    play(Sound{ .freq = note, .attack = 0.004f, .decay = tweak("sfx/chime decay", 0.7f, 0.1f, 2.0f), .volume = 0.25f });
    play(Sound{ .freq = note * 1.5f, .attack = 0.004f, .decay = 0.4f, .volume = 0.08f, .delay = 0.03f });
}

// A swell rather than a hit: time running backwards.
void sfx_undo() {
    play(Sound{ .freq = 260.0f, .sweep = 2.0f, .attack = 0.12f, .decay = 0.02f, .square = 0.5f, .volume = 0.12f });
}

void sfx_win() {
    for i32 i = 0; i < 5; i++ {
        play(Sound{ .freq = Pentatonic[i], .attack = 0.004f, .decay = 0.9f, .volume = 0.18f, .delay = 0.15f + cast(f32, i) * 0.09f });
    }
}

void draw_cell(int2 cell) {
    var base = Rect{to_float2(cell * Tile), float2{Tile}};
    if at(cell) == '#' { rect(base, tweak_color("board/wall", 0x59616d)); }
    if at(cell) == '$' {
        var on_goal = level_at(cell) == '.';
        rect(inflate(base, -5.0f), on_goal ? tweak_color("board/box on goal", 0xe0a060) : tweak_color("board/box", 0xc17f59));
    }
    if eq(cell, s.player) {
        var tag = s.play == WON ? "attack_up" : host.time - s.moved_at < tweak("player/walk time", 0.25f, 0.0f, 1.0f) ? "walk" : "idle";
        if !sprite("player", tag, base.pos + float2{Tile * 0.5f, Tile}, ANCHOR_BOTTOM_CENTER) {
            rect(inflate(base, -7.0f), s.play == WON ? rgb(0x7bd88f) : rgb(0x5fcde4));
        }
    }
}

// --- floor shader ---
//
// Runs on the GPU; it's rebuilt with the rest of this file on every save.
// Everything it shows comes from FloorParams, i.e. from the game state.

@shader fragment
float4 floor_fs(VsOut input, @uniform FloorParams p) {
    float2 cell = input.uv * p.board.xy;
    float2 id = floor(cell);
    f32 time = p.board.z;

    // a checker that warms from slate toward amber as goals fill
    f32 checker = frac((id.x + id.y) * 0.5f) * 2.0f;
    float3 cold = lerp(float3{0.141f, 0.165f, 0.192f}, float3{0.125f, 0.149f, 0.176f}, checker);
    float3 warm = lerp(float3{0.212f, 0.173f, 0.133f}, float3{0.188f, 0.153f, 0.118f}, checker);
    float3 color = lerp(cold, warm, p.board.w);

    // goals: empty ones breathe behind a ring, filled ones hold a steady glow
    for i32 i = 0; i < MAX_GOALS; i++ {
        float4 g = p.goals[i];
        f32 d = distance(cell, g.xy + 0.5f);
        f32 breathe = lerp(0.6f + 0.4f * sin(time * p.style.y + g.x * 1.7f), 1.0f, g.z);
        f32 glow = smoothstep(1.5f, 0.0f, d) * breathe * lerp(0.25f, 0.6f, g.z);
        f32 ring = smoothstep(0.05f, 0.0f, abs(d - p.style.x)) * (1.0f - g.z) * breathe;
        color = color + p.goal_color.xyz * (glow + ring) * g.w;
    }

    // push: a dust ripple out of the pushed box, stretched along the push
    float2 origin = p.push.xy + 0.5f;
    float2 rel = cell - origin;
    float2 dir = p.push.zw;
    f32 stretched = length(rel - dir * dot(rel, dir) * 0.5f);
    f32 tp = p.since.x;
    f32 ripple = smoothstep(0.12f, 0.0f, abs(stretched - tp * p.style.z)) * saturate(1.0f - tp / 0.4f);
    color = color + float3{0.55f, 0.5f, 0.45f} * ripple * 0.6f;

    // undo: cold scanlines rolling up the board for a third of a second
    f32 rewind = saturate(1.0f - p.since.y / 0.35f);
    f32 lines = 0.5f + 0.5f * sin(cell.y * 18.0f + time * 30.0f);
    color = lerp(color, color * 0.55f + float3{0.08f, 0.18f, 0.32f} * lines, rewind);

    // win: a wave out from the last box placed, leaving the floor lit behind it
    f32 tw = p.since.z;
    f32 reach = distance(cell, origin) - tw * p.style.w;
    f32 wave = smoothstep(0.7f, 0.0f, abs(reach)) * saturate(1.0f - tw / 2.0f);
    f32 lit = smoothstep(0.5f, -0.5f, reach) * 0.15f;
    color = color + float3{1.0f, 0.86f, 0.5f} * (wave * 0.7f + lit) * step(0.0f, tw);

    return float4{color, 1.0f};
}

export ShaderMeta* game_floor_shader() { return &floor_fs_shader; }
