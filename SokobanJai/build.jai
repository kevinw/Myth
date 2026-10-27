// jai build.jai            build build/sokoban
// jai build.jai - run      ...and run it
// jai build.jai - hot      build the game as build/game_0.dylib plus a host that runs it, and run that:
//                          saving any .jai here rebuilds the game and swaps it in, keeping its state
// jai build.jai - game N   build build/game_N.dylib (what the host runs on a change)

#run,stallable {
    set_build_options_dc(.{do_output=false});
    args := get_build_options().compile_time_command_line;

    if !compile_shader("../Foster/Framework/Content/Shaders/Batcher.hlsl", "batcher") return;
    // SDL's install name is @rpath/libSDL3.0.dylib; executables look in their own folder.
    // Copy only when it changed: overwriting it under a running host would kill it.
    SDL :: "../Foster/Framework/Internal/Native/osx-arm64/libSDL3.dylib";
    if file_modtime_and_size("build/libSDL3.0.dylib") < file_modtime_and_size(SDL) copy_file(SDL, "build/libSDL3.0.dylib");

    game := array_find(args, "game");
    if game {
        build(tprint("game_%", args[args.count - 1]), .DYNAMIC_LIBRARY, "main.jai");
    } else if array_find(args, "hot") {
        if !build("game_0", .DYNAMIC_LIBRARY, "main.jai") return;
        if !build("sokoban_host", .EXECUTABLE, source = HOST) return;
        run_command("build/sokoban_host");
    } else {
        if !build("sokoban", .EXECUTABLE, "main.jai") return;
        if array_find(args, "run") run_command("build/sokoban");
    }
}

HOST :: #string END
main :: () { run_host(".", .["jai", "build.jai", "-", "game"]); }
#import "Hotload";
END

build :: (name: string, type: Build_Options.Output_Type, file := "", source := "") -> bool {
    make_directory_if_it_does_not_exist("build");
    w := compiler_create_workspace(name);
    options := get_build_options(w);
    options.output_type = type;
    options.output_executable_name = name;
    options.output_path = "build";
    options.intermediate_path = "build/.build";
    options.use_custom_link_command = true;
    import_path: [..] string;
    array_add(*import_path, "modules");
    array_add(*import_path, ..options.import_path);
    options.import_path = import_path;
    set_build_options(options, w);

    compiler_begin_intercept(w);
    if file add_build_file(file, w);
    if source add_build_string(source, w);
    ok := true;
    while true {
        message := compiler_wait_for_message();
        if type == .DYNAMIC_LIBRARY hotload_message(w, message, "frame");
        if message.kind == .PHASE {
            phase := cast(*Message_Phase) message;
            if phase.phase == .READY_FOR_CUSTOM_LINK_COMMAND ok = link(w, phase, name, type);
        }
        if message.kind == .COMPLETE {
            ok &&= (cast(*Message_Complete) message).error_code == .NONE;
            break;
        }
    }
    compiler_end_intercept(w);
    return ok;
}

// HLSL -> MSL with Foster's shadercross (it goes through SPIR-V internally).
compile_shader :: (source: string, name: string) -> bool {
    SHADERCROSS :: "../Foster/tools/macos/shadercross";
    make_directory_if_it_does_not_exist("build/shaders", recursive = true);
    for stage: string.["vertex", "fragment"] {
        out := tprint("build/shaders/%.%.msl", name, stage);
        entry := tprint("%_main", stage);
        if file_modtime_and_size(out) > file_modtime_and_size(source) continue;
        if run_command(SHADERCROSS, source, "-e", entry, "-t", stage, "-s", "HLSL", "-d", "MSL", "-o", out).exit_code != 0 {
            compiler_report(tprint("can't compile % (%)", source, stage));
            return false;
        }
    }
    return true;
}

// Jai's bundled lld can't read the macOS 27 SDK's .tbd files, so link with clang, which picks the SDK itself.
link :: (w: Workspace, m: *Message_Phase, name: string, type: Build_Options.Output_Type) -> bool {
    command: [..] string;
    array_add(*command, "clang", "-Wl,-rpath,@executable_path", "-Wl,-export_dynamic");
    if type == .DYNAMIC_LIBRARY array_add(*command, "-dynamiclib", "-o", tprint("build/%.dylib", name));
    else                        array_add(*command, "-o", tprint("build/%", name));
    array_add(*command, ..m.compiler_generated_object_files);
    array_add(*command, ..m.support_object_files);
    array_add(*command, ..m.system_libraries);
    array_add(*command, ..m.user_libraries);
    ok := run_command(..command).exit_code == 0;
    if !ok compiler_report(tprint("link failed: %", get_quoted_command_string(command)));
    compiler_custom_link_command_is_complete(w);
    return ok;
}

#load "modules/Hotload/hotload_build.jai";
#import "Basic";
#import "Compiler";
#import "File";
#import "File_Utilities";
#import "Process";
