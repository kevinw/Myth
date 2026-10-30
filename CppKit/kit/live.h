// Hot reload via jet-live: save a .cpp and its changed functions swap into the running game.
// Functions reload; data layouts can't (globals carry over byte for byte), so the game hands
// live_update a function hashing the layouts of its state, and when a reload changes that hash
// the game is told to rebuild its state rather than run on memory shaped for the old code.
#pragma once
#include "base.h"

int mix_layout_line(u64* hash, const char* format, ...);

// Hashes the size and field names and types of each T (recursing into members, but not into arrays:
// name an array's element type too), not their values. Internal to each
// file: a template's weak symbol would bind a reload's calls to the old copy, with the old layout.
namespace {
template <typename... T> u64 layout_of() {
    u64 hash = 14695981039346656037ull;
    ([&] {
        hash = (hash ^ sizeof(T)) * 1099511628211ull;
        alignas(T) static char blank[sizeof(T)];
        __builtin_dump_struct((T*)blank, mix_layout_line, &hash);
    }(), ...);
    return hash;
}
}

void live_init();
enum class Reload { None, Code, Layout };
// Once a frame: reloads when edits have compiled cleanly.
Reload live_update(u64 (*layout)());
