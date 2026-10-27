// A Myth room file (Myth/Assets/Rooms/**.json): two layers of tile chars, a legend naming
// each char's tile type, lighting settings, and the actors, which spawn reads (step 2).

TILE_SIZE :: 8;

Room :: struct {
    size: Point2;               // in tiles
    backdrop: Color;
    ambient: Color;
    ambient_intensity: float;
    legend: [128] string;       // tile char -> tile type name ("ember", "ruin", ...)
    tiles: [] u8;               // size.x * size.y chars, row by row; '0' is empty
    background: [] u8;
    actors: Json;               // the file's "actors" array
}

load_room :: (path: string) -> Room, bool {
    text, ok := read_entire_file(path, log_errors = false);
    if !ok return .{}, false;
    json:, ok = parse_json(text);
    if !ok { log_error("% isn't valid JSON", path); return .{}, false; }

    room: Room;
    size := as_vector2(get(json, "size"));
    room.size = .{xx size.x, xx size.y};
    room.backdrop = rgb(as_string(get(json, "backdropColor")), default = rgb(0x1a0a2e));
    room.ambient = rgb(as_string(get(json, "ambientColor")), default = rgb(0x525252));
    room.ambient_intensity = as_float(get(json, "ambientIntensity"), 0.8);
    legend := get(json, "legend");
    for legend.keys if it.count == 1 && it[0] < 128 room.legend[it[0]] = as_string(legend.items[it_index]);
    room.tiles = read_layer(get(json, "tiles"), room.size);
    room.background = read_layer(get(json, "backgroundTiles"), room.size);
    room.actors = get(json, "actors");
    return room, true;
}

tile_at :: (layer: [] u8, room: Room, cell: Point2) -> u8 {
    if cell.x < 0 || cell.y < 0 || cell.x >= room.size.x || cell.y >= room.size.y return #char "0";
    return layer[cell.y * room.size.x + cell.x];
}

#scope_file

read_layer :: (rows: Json, size: Point2) -> [] u8 {
    layer := NewArray(size.x * size.y, u8);
    memset(layer.data, #char "0", layer.count);
    for row, y: rows.items {
        if y >= size.y break;
        text := as_string(row);
        memcpy(layer.data + y * size.x, text.data, min(text.count, size.x));
    }
    return layer;
}

#import "Basic";
#import "File";
#import "Math";
#import "Kit";
