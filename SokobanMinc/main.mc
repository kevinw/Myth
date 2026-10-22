import foster;

// Hot-reloaded on macOS; linked in everywhere else.
when !os(macos) { import sokoban; }

// build.mc copies the sprites next to the web page.
when os(wasm) { str SPRITES = "sprites"; }
else { str SPRITES = "../Myth/Assets/Sources/Sprites"; }

sapp_desc sokol_main() { return foster_app("Sokoban", 960, 540, "sokoban.mc", SPRITES); }
