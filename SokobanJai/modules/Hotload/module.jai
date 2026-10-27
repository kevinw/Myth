// Hot reloading for a game built as a dylib that keeps its state in ordinary globals.
//
// The build (see hotload_build.jai) gives each game file an exported table of its
// globals: name, type info, address. On reload the host loads the new dylib and copies
// every old global into the new one of the same name, by type info, so state survives
// even when struct layouts change: members match by name, new members keep their
// defaults, removed ones are dropped. Old dylibs stay loaded, so stale function pointers
// and type infos remain valid.

Global :: struct { name: string; type: *Type_Info; data: *void; }

// Runs `frame` from the newest build until it returns true, rebuilding with `rebuild`
// (plus the build number) whenever a .jai file in `source_dir` changes.
run_host :: (source_dir: string, rebuild: [] string) {
    game: Game;
    built := 0;
    if !load(*game, tprint("build/game_%.dylib", built)) return;
    stamp := newest_source(source_dir);
    next_check: float64;

    while !game.frame() {
        if seconds_since_init() < next_check continue;
        next_check = seconds_since_init() + 0.25;
        latest := newest_source(source_dir);
        if latest == stamp continue;
        stamp = latest;

        start := seconds_since_init();
        command: [..] string;
        command.allocator = temp;
        array_add(*command, ..rebuild);
        array_add(*command, tprint("%", built + 1));
        if run_command(..command).exit_code != 0 { log_error("[hotload] build failed; still running build %", built); continue; }

        fresh: Game;
        if !load(*fresh, tprint("build/game_%.dylib", built + 1)) continue;
        built += 1;
        migrate(game.globals, fresh.globals);
        game = fresh;
        log("[hotload] build % in %ms", built, cast(int) ((seconds_since_init() - start) * 1000));
    }
}

migrate :: (old: [] Global, new: [] Global) {
    for n: new for o: old if o.name == n.name {
        migrate(n.data, n.type, o.data, o.type, n.name);
        break;
    }
}

// Copies `src` (of `src_type`) into `dst` (of `dst_type`), converting what it can.
migrate :: (dst: *void, dst_type: *Type_Info, src: *void, src_type: *Type_Info, path: string) {
    if same_layout(dst_type, src_type) { memcpy(dst, src, dst_type.runtime_size); return; }
    if dst_type.type != src_type.type { log("[hotload] % changed type; reset", path); return; }

    if dst_type.type == {
      case .STRUCT;
        d, s := cast(*Type_Info_Struct) dst_type, cast(*Type_Info_Struct) src_type;
        for dm: d.members {
            if dm.flags & .CONSTANT continue;
            for sm: s.members if sm.name == dm.name && !(sm.flags & .CONSTANT) {
                migrate(dst + dm.offset_in_bytes, dm.type, src + sm.offset_in_bytes, sm.type, tprint("%.%", path, dm.name));
                break;
            }
        }

      case .ARRAY;
        d, s := cast(*Type_Info_Array) dst_type, cast(*Type_Info_Array) src_type;
        de, se := d.element_type, s.element_type;
        if d.array_type != s.array_type { log("[hotload] % changed array kind; reset", path); return; }
        if d.array_type == {
          case .FIXED;
            for i: 0..min(d.array_count, s.array_count)-1
                migrate(dst + i * de.runtime_size, de, src + i * se.runtime_size, se, path);
          case .RESIZABLE;
            // The element type changed, so the elements move to a new allocation.
            da, sa := cast(*Resizable_Array) dst, cast(*Resizable_Array) src;
            da.* = .{count = sa.count, allocated = sa.count, allocator = context.allocator};
            da.data = alloc(sa.count * de.runtime_size);
            for i: 0..sa.count-1 {
                element := da.data + i * de.runtime_size;
                initialize(element, de);
                migrate(element, de, sa.data + i * se.runtime_size, se, path);
            }
          case;
            log("[hotload] % is a view whose element type changed; reset", path);
        }

      case .INTEGER;
        if dst_type.runtime_size <= 8 && src_type.runtime_size <= 8 {
            value: s64;
            memcpy(*value, src, src_type.runtime_size);
            if (cast(*Type_Info_Integer) src_type).signed && src_type.runtime_size < 8 {
                shift := 64 - src_type.runtime_size * 8;
                value = (value << shift) >> shift;
            }
            memcpy(dst, *value, dst_type.runtime_size);
        }

      case .FLOAT;
        if src_type.runtime_size == 4 { (cast(*float64) dst).* = (cast(*float32) src).*; }
        else                          { (cast(*float32) dst).* = xx (cast(*float64) src).*; }

      case;
        log("[hotload] % changed size; reset", path);
    }
}

#scope_file

Game :: struct {
    frame: #type () -> bool;
    globals: [..] Global;
}

load :: (game: *Game, path: string) -> bool {
    handle := dlopen(temp_c_string(path), RTLD_NOW | RTLD_LOCAL);
    if !handle { log_error("[hotload] can't load %: %", path, to_string(dlerror())); return false; }
    game.frame = xx dlsym(handle, "game_frame");
    if !game.frame { log_error("[hotload] % has no game_frame", path); return false; }
    for i: 0..1000 {
        table := cast(#type () -> [] Global #c_call) dlsym(handle, temp_c_string(tprint("hotload_globals_%", i)));
        if !table break;
        array_add(*game.globals, ..table());
    }
    return true;
}

newest_source :: (dir: string) -> Apollo_Time {
    newest: Apollo_Time;
    for file_list(dir,, temp) if ends_with(it, ".jai") newest = max(newest, file_modtime_and_size(it));
    return newest;
}

// Same size and shape all the way down, so a memcpy is a faithful copy.
// Pointers count as the same: they're copied, not followed.
same_layout :: (a: *Type_Info, b: *Type_Info) -> bool {
    if a == b return true;
    if a.type != b.type || a.runtime_size != b.runtime_size return false;
    if a.type == {
      case .STRUCT;
        sa, sb := cast(*Type_Info_Struct) a, cast(*Type_Info_Struct) b;
        if sa.members.count != sb.members.count return false;
        for sa.members {
            m := sb.members[it_index];
            if it.name != m.name || it.offset_in_bytes != m.offset_in_bytes || it.flags != m.flags || !same_layout(it.type, m.type) return false;
        }
      case .ARRAY;
        aa, ab := cast(*Type_Info_Array) a, cast(*Type_Info_Array) b;
        return aa.array_type == ab.array_type && aa.array_count == ab.array_count && same_layout(aa.element_type, ab.element_type);
    }
    return true;
}

// Default member values for a struct; zero for everything else.
initialize :: (data: *void, type: *Type_Info) {
    memset(data, 0, type.runtime_size);
    if type.type == .STRUCT {
        init := (cast(*Type_Info_Struct) type).initializer;
        if init init(data);
    }
}

#import "Basic";
#import "File_Utilities";
#import "Math";
#import "POSIX";
#import "Process";
#import "String";
