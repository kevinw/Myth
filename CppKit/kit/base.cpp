#include "base.h"
#include <cstdarg>

static u8 temp_memory[64 << 20];
Arena temp{temp_memory, sizeof(temp_memory)};
static u8 persist_memory[256 << 20];  // untouched pages cost nothing
Arena persist{persist_memory, sizeof(persist_memory)};

const char* tprint(const char* format, ...) {
    va_list args, again;
    va_start(args, format);
    va_copy(again, args);
    int length = vsnprintf(nullptr, 0, format, args);
    char* text = (char*)temp.alloc(length + 1, 1);
    vsnprintf(text, length + 1, format, again);
    va_end(again);
    va_end(args);
    return text;
}

std::span<u8> read_file(const char* path, Arena& arena) {
    FILE* f = fopen(path, "rb");
    if (!f) return {};
    fseek(f, 0, SEEK_END);
    size_t size = ftell(f);
    fseek(f, 0, SEEK_SET);
    u8* data = (u8*)arena.alloc(size + 1);
    size_t read = fread(data, 1, size, f);
    fclose(f);
    data[read] = 0;
    return {data, read};
}
