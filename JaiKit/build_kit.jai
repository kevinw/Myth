// The shared build for JaiKit games. A game's build.jai is just:
//
//   #run,stallable kit_build("mygame");
//   #load "../JaiKit/build_kit.jai";
//
// and then, from the game's folder:
//
//   jai build.jai            build build/<name> from main.jai
//   jai build.jai - run      ...and run it
//   jai build.jai - hot      build the game as build/game_0.dylib plus a host that runs it, and run that:
//                            saving any .jai in the game or in JaiKit rebuilds it and swaps it in, keeping its state
//   jai build.jai - game N   build build/game_N.dylib (what the host runs on a change)
//
// The game provides `frame :: () -> bool` (true to quit); HOTLOAD is true in hot builds.

kit_build :: (name: string) {
    set_build_options_dc(.{do_output=false});
    args := get_build_options().compile_time_command_line;

    if !compile_shader(tprint("%../Foster/Framework/Content/Shaders/Batcher.hlsl", JAIKIT), "batcher") return;

    if array_find(args, "game") {
        build(tprint("game_%", args[args.count - 1]), .DYNAMIC_LIBRARY, "main.jai");
    } else if array_find(args, "hot") {
        for file_list("build") if begins_with(path_filename(it), "game_") file_delete(it);
        if !build("game_0", .DYNAMIC_LIBRARY, "main.jai") return;
        host := tprint("main :: () { run_host(.[\".\", \"%modules\"], .[\"jai\", \"build.jai\", \"-\", \"game\"]); }\n#import \"Hotload\";", JAIKIT);
        if !build(tprint("%_host", name), .EXECUTABLE, source = host) return;
        run_command(tprint("build/%_host", name));
    } else {
        if !build(name, .EXECUTABLE, "main.jai") return;
        if array_find(args, "run") run_command(tprint("build/%", name));
    }
}

// HLSL -> MSL with Foster's shadercross (it goes through SPIR-V internally), into build/shaders.
compile_shader :: (source: string, name: string) -> bool {
    SHADERCROSS :: #run tprint("%../Foster/tools/macos/shadercross", JAIKIT);
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

#scope_file

JAIKIT :: #filepath;

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
    array_add(*import_path, "modules", tprint("%modules", JAIKIT));
    array_add(*import_path, ..options.import_path);
    options.import_path = import_path;
    set_build_options(options, w);

    compiler_begin_intercept(w);
    if file add_build_file(tprint("%/%", get_working_directory(), file), w);  // relative to the game, not this file
    if source add_build_string(source, w);
    add_build_string(tprint("HOTLOAD :: %;", type == .DYNAMIC_LIBRARY), w);
    ok := true;
    while true {
        message := compiler_wait_for_message();
        actors_message(w, message);
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
    // macOS checks each new binary on its first load (~200ms) and caches the result per file.
    // Pay that here, in the background build, rather than in the host when it swaps.
    if ok && type == .DYNAMIC_LIBRARY dlclose(dlopen(temp_c_string(tprint("build/%.dylib", name)), RTLD_LAZY | RTLD_LOCAL));
    compiler_custom_link_command_is_complete(w);
    return ok;
}

#load "modules/Hotload/hotload_build.jai";
#load "actors_build.jai";
#import "Basic";
#import "Compiler";
#import "File";
#import "File_Utilities";
#import "Process";
#import "POSIX";
#import "String";
#import "System";
