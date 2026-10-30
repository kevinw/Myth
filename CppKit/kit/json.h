// A small JSON reader: parse into a tree of Json values in an arena, then look things up with
// `get`/`as_*`, which fall back to a default when a key is missing or the wrong kind.
#pragma once
#include "base.h"
#include <string_view>

struct Json {
    enum Kind : u8 { Null, Bool, Number, String, Array, Object } kind = Null;
    bool boolean = false;
    double number = 0;
    std::string_view text;
    std::span<Json> items;              // array elements, or object values
    std::span<std::string_view> keys;   // object keys, parallel to items

    Json get(std::string_view key) const {
        for (size_t i = 0; i < keys.size(); i++) if (keys[i] == key) return items[i];
        return {};
    }
    Json operator[](std::string_view key) const { return get(key); }
    float as_float(float otherwise = 0) const { return kind == Number ? (float)number : otherwise; }
    s32 as_int(s32 otherwise = 0) const { return kind == Number ? (s32)number : otherwise; }
    bool as_bool(bool otherwise = false) const { return kind == Bool ? boolean : otherwise; }
    std::string_view as_string(std::string_view otherwise = {}) const { return kind == String ? text : otherwise; }
    // [x, y]
    Vector2 as_vector2(Vector2 otherwise = {}) const {
        return kind == Array && items.size() == 2 ? Vector2{items[0].as_float(), items[1].as_float()} : otherwise;
    }
    Json* begin() const { return items.data(); }
    Json* end() const { return items.data() + items.size(); }
};

// Strings and the tree live in `arena`. Also skips // and /* */ comments and trailing commas.
std::optional<Json> parse_json(std::string_view text, Arena& arena);

// "1A0A2E" -> Color
Color rgb(std::string_view hex, Color otherwise = {});

// Fields from JSON objects, keeping what's there when the key is missing or the wrong kind.
inline void read(Json j, float& v) { v = j.as_float(v); }
inline void read(Json j, s32& v) { v = j.as_int(v); }
inline void read(Json j, bool& v) { v = j.as_bool(v); }
inline void read(Json j, Vector2& v) { v = j.as_vector2(v); }
inline void read(Json j, Point2& v) { Vector2 p = j.as_vector2(v.vector()); v = {(s32)p.x, (s32)p.y}; }
inline void read(Json j, Color& v) { v = j.kind == Json::String ? rgb(j.text, v) : j.kind == Json::Number ? Color::rgb((u32)j.number) : v; }
