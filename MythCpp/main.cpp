// Human Questions (Myth), in C++ on CppKit. For now: the spike proving jet-live's hot reload.
#include "gfx.h"
#include "live.h"

struct Game {
    Texture atlas;
    Vector2 ball{200, 200}, velocity{240, 180};
    float time = 0;
};
static Game game;

static u64 state_layout() { return layout_of<Game>(); }

static void start() {
    game = {};
    auto atlas = load_qoi(tprint("%s../../Myth/Assets/Packed/Visuals/atlas.qoi", SDL_GetBasePath()));
    if (atlas) game.atlas = *atlas;
}

static void update(float dt) {
    game.time += dt;
    Vector2 size = screen_size();
    game.ball += game.velocity * dt;
    if (game.ball.x < 0 || game.ball.x > size.x) game.velocity.x = -game.velocity.x;
    if (game.ball.y < 0 || game.ball.y > size.y) game.velocity.y = -game.velocity.y;
}

static void draw() {
    Vector2 size = screen_size();
    float scale = min(size.x / game.atlas.size.x, size.y / game.atlas.size.y);
    push_matrix((size - game.atlas.size * scale) * 0.5f, scale);
    image(game.atlas, {{}, game.atlas.size}, {});
    pop_matrix();
    circle(game.ball, 24 + 6 * sinf(game.time * 4), 24, Color::rgb(0xffcc33));
    present(Color::rgb(0x1a0a2e));
}

int main() {
    SDL_Init(SDL_INIT_VIDEO);
    SDL_Window* window = SDL_CreateWindow("MythCpp", 1280, 720, SDL_WINDOW_RESIZABLE | SDL_WINDOW_HIGH_PIXEL_DENSITY);
    if (!gfx_init(window)) { SDL_Log("gfx: %s", SDL_GetError()); return 1; }
    live_init();
    start();
    u64 last = SDL_GetTicksNS();
    for (bool running = true; running;) {
        for (SDL_Event e; SDL_PollEvent(&e);)
            if (e.type == SDL_EVENT_QUIT || (e.type == SDL_EVENT_KEY_DOWN && e.key.key == SDLK_ESCAPE)) running = false;
        if (live_update(state_layout) == Reload::Layout) { SDL_Log("state layout changed: restarting"); start(); }
        u64 now = SDL_GetTicksNS();
        update(min((now - last) / 1e9f, 0.1f));
        last = now;
        draw();
        temp.reset();
    }
    return 0;
}
