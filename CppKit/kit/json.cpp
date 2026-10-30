#include "json.h"
#include <cstdlib>

namespace {
struct Parser {
    std::string_view rest;
    Arena& arena;
    bool failed = false;

    void skip_space() {
        while (!rest.empty()) {
            char c = rest[0];
            if (c == ' ' || c == '\n' || c == '\r' || c == '\t') { rest.remove_prefix(1); continue; }
            if (rest.starts_with("//")) { size_t end = rest.find('\n'); rest.remove_prefix(end == rest.npos ? rest.size() : end); continue; }
            if (rest.starts_with("/*")) { size_t end = rest.find("*/", 2); rest.remove_prefix(end == rest.npos ? rest.size() : end + 2); continue; }
            break;
        }
    }

    bool take(char c) {
        skip_space();
        if (rest.empty() || rest[0] != c) return false;
        rest.remove_prefix(1);
        return true;
    }

    // Comma-separated items until `close`, allowing a trailing comma.
    template <typename F> void list(char close, F item) {
        if (take(close)) return;
        while (!failed) {
            item();
            if (take(',')) { if (take(close)) return; continue; }
            if (!take(close)) failed = true;
            return;
        }
    }

    template <typename T> std::span<T> keep(List<T>& scratch) {
        auto out = arena.array<T>(scratch.count);
        for (s32 i = 0; i < scratch.count; i++) out[i] = scratch[i];
        return out;
    }

    Json value() {
        skip_space();
        if (rest.empty()) { failed = true; return {}; }
        char c = rest[0];
        if (c == '{') {
            rest.remove_prefix(1);
            List<std::string_view> keys;
            List<Json> items;
            list('}', [&] {
                skip_space();
                keys.add(string());
                if (!take(':')) { failed = true; return; }
                items.add(value());
            });
            return {.kind = Json::Object, .items = keep(items), .keys = keep(keys)};
        }
        if (c == '[') {
            rest.remove_prefix(1);
            List<Json> items;
            list(']', [&] { items.add(value()); });
            return {.kind = Json::Array, .items = keep(items)};
        }
        if (c == '"') return {.kind = Json::String, .text = string()};
        if (rest.starts_with("true")) { rest.remove_prefix(4); return {.kind = Json::Bool, .boolean = true}; }
        if (rest.starts_with("false")) { rest.remove_prefix(5); return {.kind = Json::Bool}; }
        if (rest.starts_with("null")) { rest.remove_prefix(4); return {}; }
        char number[64];
        size_t n = min(rest.size(), sizeof(number) - 1);
        memcpy(number, rest.data(), n);
        number[n] = 0;
        char* end;
        double v = strtod(number, &end);
        if (end == number) { failed = true; return {}; }
        rest.remove_prefix(end - number);
        return {.kind = Json::Number, .number = v};
    }

    std::string_view string() {
        if (rest.empty() || rest[0] != '"') { failed = true; return {}; }
        rest.remove_prefix(1);
        size_t close = rest.find('"');
        // No escapes (the usual case): point into the source text.
        if (close != rest.npos && rest.substr(0, close).find('\\') == rest.npos) {
            auto s = rest.substr(0, close);
            rest.remove_prefix(close + 1);
            return s;
        }
        List<char> out{.arena = &arena};
        while (!rest.empty() && rest[0] != '"') {
            char c = rest[0];
            rest.remove_prefix(1);
            if (c != '\\' || rest.empty()) { out.add(c); continue; }
            char e = rest[0];
            rest.remove_prefix(1);
            switch (e) {
                case 'n': out.add('\n'); break;
                case 't': out.add('\t'); break;
                case 'r': out.add('\r'); break;
                case 'b': out.add(8); break;
                case 'f': out.add(12); break;
                case 'u': {
                    if (rest.size() < 4) { failed = true; return {}; }
                    char hex[5] = {rest[0], rest[1], rest[2], rest[3], 0};
                    rest.remove_prefix(4);
                    u32 code = (u32)strtoul(hex, nullptr, 16);
                    if (code < 0x80) out.add((char)code);
                    else if (code < 0x800) { out.add(char(0xc0 | code >> 6)); out.add(char(0x80 | (code & 0x3f))); }
                    else { out.add(char(0xe0 | code >> 12)); out.add(char(0x80 | ((code >> 6) & 0x3f))); out.add(char(0x80 | (code & 0x3f))); }
                    break;
                }
                default: out.add(e);  // \" \\ \/
            }
        }
        if (rest.empty()) { failed = true; return {}; }
        rest.remove_prefix(1);
        return {out.data, (size_t)out.count};
    }
};
}

std::optional<Json> parse_json(std::string_view text, Arena& arena) {
    Parser p{text, arena};
    Json value = p.value();
    p.skip_space();
    if (p.failed || !p.rest.empty()) return {};
    return value;
}

Color rgb(std::string_view hex, Color otherwise) {
    if (hex.size() != 6) return otherwise;
    char text[7];
    memcpy(text, hex.data(), 6);
    text[6] = 0;
    char* end;
    u32 v = (u32)strtoul(text, &end, 16);
    return end == text + 6 ? Color::rgb(v) : otherwise;
}
