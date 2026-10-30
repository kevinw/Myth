// CppKit's basics: Foster's value types, a per-frame scratch arena, and pools of objects
// addressed by generational handles instead of pointers into the heap.
#pragma once
#include <cmath>
#include <cstdint>
#include <cstring>
#include <cstdio>
#include <cstdlib>
#include <span>
#include <optional>

using u8 = uint8_t; using u16 = uint16_t; using u32 = uint32_t; using u64 = uint64_t;
using s8 = int8_t;  using s16 = int16_t;  using s32 = int32_t;  using s64 = int64_t;

constexpr float TAU = 6.2831853f;

struct Vector2 {
    float x = 0, y = 0;
    Vector2 operator+(Vector2 b) const { return {x + b.x, y + b.y}; }
    Vector2 operator-(Vector2 b) const { return {x - b.x, y - b.y}; }
    Vector2 operator*(Vector2 b) const { return {x * b.x, y * b.y}; }
    Vector2 operator/(Vector2 b) const { return {x / b.x, y / b.y}; }
    Vector2 operator*(float k) const { return {x * k, y * k}; }
    Vector2 operator/(float k) const { return {x / k, y / k}; }
    Vector2 operator-() const { return {-x, -y}; }
    Vector2& operator+=(Vector2 b) { return *this = *this + b; }
    Vector2& operator-=(Vector2 b) { return *this = *this - b; }
    bool operator==(const Vector2&) const = default;
    float length() const { return sqrtf(x * x + y * y); }
    Vector2 normalized() const { float l = length(); return l > 0 ? *this / l : Vector2{}; }
    Vector2 floor() const { return {floorf(x), floorf(y)}; }
};
inline float dot(Vector2 a, Vector2 b) { return a.x * b.x + a.y * b.y; }
inline Vector2 operator*(float k, Vector2 v) { return v * k; }
struct Vector3 { float x = 0, y = 0, z = 0; };
struct Vector4 { float x = 0, y = 0, z = 0, w = 0; };

struct Point2 {
    s32 x = 0, y = 0;
    Point2 operator+(Point2 b) const { return {x + b.x, y + b.y}; }
    Point2 operator-(Point2 b) const { return {x - b.x, y - b.y}; }
    Point2 operator*(s32 k) const { return {x * k, y * k}; }
    Point2 operator-() const { return {-x, -y}; }
    Point2& operator+=(Point2 b) { return *this = *this + b; }
    Point2& operator-=(Point2 b) { return *this = *this - b; }
    bool operator==(const Point2&) const = default;
    Vector2 vector() const { return {(float)x, (float)y}; }
};

struct RectInt {
    s32 x = 0, y = 0, w = 0, h = 0;
    RectInt operator+(Point2 p) const { return {x + p.x, y + p.y, w, h}; }
    bool overlaps(RectInt b) const { return x < b.x + b.w && b.x < x + w && y < b.y + b.h && b.y < y + h; }
};

struct Rect {
    Vector2 pos, size;
    Rect inflate(float by) const { return {pos - Vector2{by, by}, size + Vector2{2 * by, 2 * by}}; }
};

// Premultiplied, like everything we draw.
struct Color {
    u8 r = 0, g = 0, b = 0, a = 0;
    static constexpr Color rgb(u32 hex) { return {u8(hex >> 16), u8(hex >> 8), u8(hex), 255}; }
    Color fade(float alpha) const { return {u8(r * alpha), u8(g * alpha), u8(b * alpha), u8(a * alpha)}; }
    bool operator==(const Color&) const = default;
};
constexpr Color WHITE{255, 255, 255, 255};

template <typename T> T min(T a, T b) { return a < b ? a : b; }
template <typename T> T max(T a, T b) { return a > b ? a : b; }
template <typename T> T clamp(T v, T lo, T hi) { return v < lo ? lo : v > hi ? hi : v; }
inline s32 sign(float v) { return (v > 0) - (v < 0); }
inline float approach(float value, float target, float amount) {
    return value < target ? min(value + amount, target) : max(value - amount, target);
}
inline Vector2 approach(Vector2 value, Vector2 target, float amount) {
    return {approach(value.x, target.x, amount), approach(value.y, target.y, amount)};
}

// Bump allocation, reset all at once: `temp` is reset every frame; `persist` never is.
struct Arena {
    u8* base = nullptr;
    size_t capacity = 0, used = 0;

    void* alloc(size_t bytes, size_t align = 16) {
        size_t at = (used + align - 1) & ~(align - 1);
        if (at + bytes > capacity) { fprintf(stderr, "arena out of space (%zu of %zu)\n", at + bytes, capacity); abort(); }
        used = at + bytes;
        return base + at;
    }
    // Uninitialized, so only for trivial types.
    template <typename T> std::span<T> array(size_t count) { return {(T*)alloc(sizeof(T) * count, alignof(T)), count}; }
    void reset() { used = 0; }
};
extern Arena temp, persist;

// printf into the scratch arena.
const char* tprint(const char* format, ...) __attribute__((format(printf, 1, 2)));

// Growable array of trivial types that lives in an arena (default: temp), for this frame's lists.
template <typename T> struct List {
    T* data = nullptr;
    s32 count = 0, capacity = 0;
    Arena* arena = &temp;

    void add(const T& v) {
        if (count == capacity) {
            s32 grown = max(capacity * 2, 16);
            T* bigger = (T*)arena->alloc(sizeof(T) * grown, alignof(T));
            if (count) memcpy(bigger, data, sizeof(T) * count);
            data = bigger; capacity = grown;
        }
        data[count++] = v;
    }
    T& operator[](s32 i) { return data[i]; }
    bool contains(const T& v) const { for (s32 i = 0; i < count; i++) if (data[i] == v) return true; return false; }
    void add_unique(const T& v) { if (!contains(v)) add(v); }
    void remove_unordered(const T& v) { for (s32 i = 0; i < count; i++) if (data[i] == v) { data[i] = data[--count]; return; } }
    T* begin() { return data; }
    T* end() { return data + count; }
    std::span<T> span() { return {data, (size_t)count}; }
};

// Objects live in fixed slots and are named by Handle: a slot index and the generation that was
// alive when the handle was made, so a handle to a freed object resolves to null.
template <typename T> struct Handle {
    u32 index = 0, generation = 0;  // generation 0 is never alive: a default Handle is null
    explicit operator bool() const { return generation != 0; }
    bool operator==(const Handle&) const = default;
};

// `T*` from a pool is good until that slot is removed; hold a Handle across frames.
template <typename T, u32 N> struct Pool {
    T items[N];
    u32 generations[N] = {};
    bool alive[N] = {};
    u32 used = 0;  // slots ever handed out: iteration stops here

    // A fresh default T, or null when full. Freed slots are reused first, most recent first.
    T* add() {
        u32 i = 0;
        for (u32 k = used; k-- > 0;) if (!alive[k]) { i = k; goto found; }
        if (used == N) { fprintf(stderr, "pool full (%u)\n", N); return nullptr; }
        i = used++;
    found:
        alive[i] = true;
        if (++generations[i] == 0) generations[i] = 1;
        items[i] = T{};
        return &items[i];
    }
    Handle<T> handle_of(const T* item) const { u32 i = u32(item - items); return {i, generations[i]}; }
    T* get(Handle<T> h) { return h && h.index < used && alive[h.index] && generations[h.index] == h.generation ? &items[h.index] : nullptr; }
    void remove(const T* item) { alive[item - items] = false; }
    void clear() { for (u32 i = 0; i < used; i++) alive[i] = false; }

    // for (T& it : pool) visits the live ones.
    struct Iterator {
        Pool* pool; u32 i;
        void skip() { while (i < pool->used && !pool->alive[i]) i++; }
        T& operator*() { return pool->items[i]; }
        Iterator& operator++() { i++; skip(); return *this; }
        bool operator!=(const Iterator& o) const { return i != o.i; }
    };
    Iterator begin() { Iterator it{this, 0}; it.skip(); return it; }
    Iterator end() { return {this, used}; }
};

// A whole file into the arena, null-terminated; empty if it can't be read.
std::span<u8> read_file(const char* path, Arena& arena = temp);
