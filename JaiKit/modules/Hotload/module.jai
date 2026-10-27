// Hot reloading for a game built as a dylib that keeps its state in ordinary globals.
//
// The build (see hotload_build.jai) gives each game file an exported table of its
// globals: name, type info, address. On reload the host loads the new dylib and copies
// every old global into the new one of the same name, by type info, so state survives
// even when struct layouts change: members match by name, new members keep their
// defaults, removed ones are dropped. Old dylibs stay loaded, so stale function pointers
// and type infos remain valid.

Global :: struct { name: string; type: *Type_Info; data: *void; }

// What the host tells the game, via the hotload_status global the build adds to the game.
Status :: struct {
    building: bool;
    error: string;  // the first error of the last failed build; empty once a build succeeds
}

// Runs `frame` from the newest build until it returns true. Whenever a .jai file under
// `source_dirs` changes, runs `rebuild` (plus the build number) in the background and
// swaps the result in.
run_host :: (source_dirs: [] string, rebuild: [] string) {
    game: Game;
    status: Status;
    if !load(*game, 0, *status) return;

    changed: bool;
    watcher: File_Watcher(bool);
    on_change :: (watcher: *File_Watcher(bool), change: *File_Change, changed: *bool) {
        if ends_with(change.full_path, ".jai") && !contains(change.full_path, "/build/") changed.* = true;
    }
    if !init(*watcher, on_change, *changed, merge_window_seconds = 0.05) || !add_directories(*watcher, ..source_dirs) {
        log_error("[hotload] can't watch %", source_dirs);
        return;
    }

    build: Build;
    while !game.frame() {
        process_changes(*watcher);
        if changed {
            changed = false;
            start(*build, rebuild, game.number + 1);
            status.building = build.running;
        }
        if !build.running || !finished(*build) continue;

        status.building = false;
        free(status.error);
        status.error = "";
        if build.exit_code != 0 {
            status.error = first_error(build.output);
            log_error("[hotload] build failed; still running build %\n%", game.number, build.output);
            continue;
        }
        compiled := seconds_since_init();
        fresh: Game;
        if !load(*fresh, build.number, *status) continue;
        migrate(game.globals, fresh.globals);
        game = fresh;
        ms :: (seconds: float64) -> int { return xx (seconds * 1000); }
        log("[hotload] build % in %ms (swap %ms)", game.number, ms(seconds_since_init() - build.started), ms(seconds_since_init() - compiled));
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
    number: int;
    frame: #type () -> bool;
    globals: [..] Global;
}

load :: (game: *Game, number: int, status: *Status) -> bool {
    path := tprint("build/game_%.dylib", number);
    handle := dlopen(temp_c_string(path), RTLD_NOW | RTLD_LOCAL);
    if !handle { log_error("[hotload] can't load %: %", path, to_string(dlerror())); return false; }
    game.number = number;
    game.frame = xx dlsym(handle, "game_frame");
    if !game.frame { log_error("[hotload] % has no game_frame", path); return false; }
    set_status: #type (*Status) #c_call = xx dlsym(handle, "hotload_set_status");
    set_status(status);
    for i: 0..1000 {
        table := cast(#type () -> [] Global #c_call) dlsym(handle, temp_c_string(tprint("hotload_globals_%", i)));
        if !table break;
        array_add(*game.globals, ..table());
    }
    return true;
}

// A rebuild running in the background. Starting one kills any that's still running.
Build :: struct {
    process: Process;
    running: bool;
    number: int;
    started: float64;
    out, err: [..] u8;
    output: string;
    exit_code: s32;
}

start :: (build: *Build, rebuild: [] string, number: int) {
    if build.running {
        kill_process(*build.process);
        get_process_result(*build.process);
        deinit(*build.process);
    }
    build.out.count = 0;
    build.err.count = 0;
    command: [..] string;
    command.allocator = temp;
    array_add(*command, ..rebuild);
    array_add(*command, tprint("%", number));
    build.process = .{};  // deinit leaves the pipes marked eof
    build.running = create_process(*build.process, ..command, capture_and_return_output = true);
    build.number = number;
    build.started = seconds_since_init();
    if !build.running log_error("[hotload] can't run %", get_quoted_command_string(command));
}

// Collects output without blocking; true once the build exited.
finished :: (build: *Build) -> bool {
    unused :: (a: *[..] u8) -> [] u8 {
        array_reserve(a, a.count + 4096);
        return .{a.allocated - a.count, a.data + a.count};
    }
    ok, out_bytes, err_bytes := read_from_process(*build.process, unused(*build.out), unused(*build.err), timeout_ms = 0);
    build.out.count += out_bytes;
    build.err.count += err_bytes;
    if ok && !(build.process.output.eof && build.process.error.eof) return false;

    _, result := get_process_result(*build.process);
    deinit(*build.process);
    build.running = false;
    build.exit_code = result.exit_code;
    free(build.output);
    build.output = sprint("%1%2", cast(string) build.out, cast(string) build.err);
    return true;
}

// The compiler's first "Error:" line, minus the path, and the source lines it quotes.
first_error :: (output: string) -> string {
    lines := split(output, "\n",, temp);
    for lines if contains(it, "Error:") {
        b: String_Builder;
        append(*b, path_filename(it));
        i := it_index + 1;
        while i < lines.count && !trim(lines[i]) i += 1;
        while i < lines.count && trim(lines[i]) {
            print_to_builder(*b, "\n%", lines[i]);
            i += 1;
        }
        return builder_to_string(*b);
    }
    return copy_string("build failed");
}

comparing: [..] [2] *Type_Info;

// Same size and shape all the way down, so a memcpy is a faithful copy.
// Pointers count as the same: they're copied, not followed.
same_layout :: (a: *Type_Info, b: *Type_Info) -> bool {
    if a == b return true;
    // Types can contain themselves through arrays (a tree node's `children: [] Node`). A pair
    // already being compared is assumed the same; any real difference shows up in its members.
    for comparing if it[0] == a && it[1] == b return true;
    array_add(*comparing, .[a, b]);
    defer pop(*comparing);
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
#import "File_Watcher";
#import "Math";
#import "POSIX";
#import "Process";
#import "String";
