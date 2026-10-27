// A small JSON reader: parse into a tree of Json values, then look things up with
// `get`/`as_*`, which fall back to a default when a key is missing or the wrong kind.
// Everything is allocated with context.allocator; push a pool or temp to free it all at once.

Json :: struct {
    kind: enum u8 { NULL; BOOL; NUMBER; STRING; ARRAY; OBJECT; };
    boolean: bool;
    number: float64;
    text: string;
    items: [] Json;   // array elements, or object values
    keys: [] string;  // object keys, parallel to items
}

parse_json :: (text: string) -> Json, bool {
    p := Json_Parser.{rest = text};
    value := parse_value(*p);
    skip_space(*p);
    return value, !p.failed && !p.rest;
}

get :: (j: Json, key: string) -> Json {
    for j.keys if it == key return j.items[it_index];
    return .{};
}

as_float  :: (j: Json, default := 0.0) -> float  { return ifx j.kind == .NUMBER then cast(float) j.number else default; }
as_int    :: (j: Json, default := 0) -> int      { return ifx j.kind == .NUMBER then cast(int) j.number else default; }
as_bool   :: (j: Json, default := false) -> bool { return ifx j.kind == .BOOL then j.boolean else default; }
as_string :: (j: Json, default := "") -> string  { return ifx j.kind == .STRING then j.text else default; }

// [x, y] -> Vector2
as_vector2 :: (j: Json, default := Vector2.{}) -> Vector2 {
    if j.kind != .ARRAY || j.items.count != 2 return default;
    return .{as_float(j.items[0]), as_float(j.items[1])};
}

// Fills the struct `dst` from a JSON object, by type info: each member reads the key that's
// its name in PascalCase (`flicker_frequency` <- "FlickerFrequency"), keeping its default
// when the key is missing. Embedded (`using`) structs read from the same object.
// Members noted @NoSerialize are skipped.
read_fields :: (dst: *$T, j: Json) { read_fields(dst, type_info(T), j); }

read_fields :: (dst: *void, info: *Type_Info_Struct, j: Json) {
    for m: info.members {
        if m.flags & .CONSTANT || array_find(m.notes, "NoSerialize") continue;
        if m.flags & .USING && m.type.type == .STRUCT { read_fields(dst + m.offset_in_bytes, cast(*Type_Info_Struct) m.type, j); continue; }
        value := get(j, pascal_case(m.name,, temp));
        if value.kind != .NULL read_value(dst + m.offset_in_bytes, m.type, value);
    }
}

#scope_file

read_value :: (dst: *void, type: *Type_Info, j: Json) {
    if type.type == {
      case .INTEGER;
        if j.kind != .NUMBER return;
        value := cast(s64) j.number;
        memcpy(dst, *value, type.runtime_size);  // little-endian: the low bytes
      case .FLOAT;
        if j.kind != .NUMBER return;
        if type.runtime_size == 4 { (cast(*float32) dst).* = xx j.number; }
        else                      { (cast(*float64) dst).* = j.number; }
      case .BOOL;
        if j.kind == .BOOL { (cast(*bool) dst).* = j.boolean; }
      case .STRING;
        if j.kind == .STRING { (cast(*string) dst).* = copy_string(j.text); }
      case .ENUM;
        info := cast(*Type_Info_Enum) type;
        value: s64;
        if j.kind == .NUMBER value = xx j.number;
        else if j.kind == .STRING { found := false; for info.names if it == j.text { value = info.values[it_index]; found = true; } if !found return; }
        else return;
        memcpy(dst, *value, type.runtime_size);
      case .STRUCT;
        if type == type_info(Color) {
            if j.kind == .STRING { (cast(*Color) dst).* = rgb(j.text, default = (cast(*Color) dst).*); }
            else if j.kind == .NUMBER { (cast(*Color) dst).* = rgb(cast(u32) j.number); }
        } else if j.kind == .ARRAY {
            // [x, y, ...] into a struct of numbers, like Vector2
            members := (cast(*Type_Info_Struct) type).members;
            for members if it_index < j.items.count && !(it.flags & .CONSTANT) read_value(dst + it.offset_in_bytes, it.type, j.items[it_index]);
        } else if j.kind == .OBJECT {
            read_fields(dst, cast(*Type_Info_Struct) type, j);
        }
    }
}

pascal_case :: (snake: string) -> string {
    b: String_Builder;
    upper := true;
    for 0..snake.count-1 {
        c := snake[it];
        if c == #char "_" { upper = true; continue; }
        append(*b, ifx upper && c >= #char "a" && c <= #char "z" then c - 32 else c);
        upper = false;
    }
    return builder_to_string(*b);
}

Json_Parser :: struct { rest: string; failed: bool; }

skip_space :: (p: *Json_Parser) {
    while p.rest && (p.rest[0] == #char " " || p.rest[0] == #char "\n" || p.rest[0] == #char "\r" || p.rest[0] == #char "\t") advance(*p.rest);
}

expect :: (p: *Json_Parser, c: u8) -> bool {
    skip_space(p);
    if !p.rest || p.rest[0] != c { p.failed = true; return false; }
    advance(*p.rest);
    return true;
}

parse_value :: (p: *Json_Parser) -> Json {
    skip_space(p);
    if !p.rest { p.failed = true; return .{}; }
    c := p.rest[0];
    if c == {
      case #char "{";
        advance(*p.rest);
        keys: [..] string;
        items: [..] Json;
        skip_space(p);
        if p.rest && p.rest[0] == #char "}" { advance(*p.rest); return .{kind = .OBJECT}; }
        while !p.failed {
            skip_space(p);
            array_add(*keys, parse_string(p));
            if !expect(p, #char ":") break;
            array_add(*items, parse_value(p));
            skip_space(p);
            if p.rest && p.rest[0] == #char "," { advance(*p.rest); continue; }
            expect(p, #char "}");
            break;
        }
        return .{kind = .OBJECT, keys = keys, items = items};

      case #char "[";
        advance(*p.rest);
        items: [..] Json;
        skip_space(p);
        if p.rest && p.rest[0] == #char "]" { advance(*p.rest); return .{kind = .ARRAY}; }
        while !p.failed {
            array_add(*items, parse_value(p));
            skip_space(p);
            if p.rest && p.rest[0] == #char "," { advance(*p.rest); continue; }
            expect(p, #char "]");
            break;
        }
        return .{kind = .ARRAY, items = items};

      case #char "\"";
        return .{kind = .STRING, text = parse_string(p)};
    }
    if begins_with(p.rest, "true")  { advance(*p.rest, 4); return .{kind = .BOOL, boolean = true}; }
    if begins_with(p.rest, "false") { advance(*p.rest, 5); return .{kind = .BOOL}; }
    if begins_with(p.rest, "null")  { advance(*p.rest, 4); return .{}; }

    value, ok, rest := string_to_float64(p.rest);
    if !ok { p.failed = true; return .{}; }
    p.rest = rest;
    return .{kind = .NUMBER, number = value};
}

parse_string :: (p: *Json_Parser) -> string {
    if !p.rest || p.rest[0] != #char "\"" { p.failed = true; return ""; }
    advance(*p.rest);
    b: String_Builder;
    while p.rest && p.rest[0] != #char "\"" {
        c := p.rest[0];
        advance(*p.rest);
        if c != #char "\\" || !p.rest { append(*b, c); continue; }
        e := p.rest[0];
        advance(*p.rest);
        if e == {
          case #char "n"; append(*b, #char "\n");
          case #char "t"; append(*b, #char "\t");
          case #char "r"; append(*b, #char "\r");
          case #char "b"; append(*b, 8);
          case #char "f"; append(*b, 12);
          case #char "u";
            if p.rest.count < 4 { p.failed = true; break; }
            code, ok := string_to_int(slice(p.rest, 0, 4), base = 16);
            advance(*p.rest, 4);
            append(*b, character_utf32_to_utf8(xx code,, temp));
          case; append(*b, e);  // \" \\ \/
        }
    }
    if !p.rest { p.failed = true; return ""; }
    advance(*p.rest);
    return builder_to_string(*b);
}

#import "Basic";
#import "Math";
#import "String";
#import "Unicode";
