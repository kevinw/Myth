#include "live.h"
#include <cstdarg>
#include <memory>
#include <jet/live/Live.hpp>

int mix_layout_line(u64* hash, const char* format, ...) {
    char line[512];
    va_list args;
    va_start(args, format);
    vsnprintf(line, sizeof(line), format, args);
    va_end(args);
    // "  float x = 0.000000": the type and name, not the value.
    for (const char* c = line; *c && !(c[0] == ' ' && c[1] == '='); c++) *hash = (*hash ^ (u8)*c) * 1099511628211ull;
    return 0;
}

struct Listener : jet::ILiveListener {
    bool* reloaded;
    explicit Listener(bool* r) : reloaded(r) {}
    void onLog(jet::LogSeverity severity, const std::string& message) override {
        if (severity >= jet::LogSeverity::kInfo) fprintf(stderr, "[live] %s\n", message.c_str());
    }
    void onCodePostLoad() override { *reloaded = true; }
};

static jet::Live* live;
static bool reloaded, compiling;
static u64 layout_hash;

void live_init() {
    jet::LiveConfig config;
    config.reloadOnSignal = false;
    live = new jet::Live(std::make_unique<Listener>(&reloaded), config);
}

Reload live_update(u64 (*layout)()) {
    if (!layout_hash) layout_hash = layout();
    live->update();
    if (!live->isInitialized()) return Reload::None;
    // A reload is queued (and linked, then loaded, during later updates) once edits finish compiling cleanly.
    jet::Status status = live->getStatus();
    bool was_compiling = compiling;
    compiling = !status.compilingFiles.empty();
    if (was_compiling && !compiling && status.failedFiles.empty()) live->tryReload();
    if (!reloaded) return Reload::None;
    reloaded = false;
    u64 now = layout();
    bool changed = now != layout_hash;
    layout_hash = now;
    return changed ? Reload::Layout : Reload::Code;
}
