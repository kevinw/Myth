// Regenerates the SDL3 bindings from the installed headers:
//   jai generate.jai          (from this folder)
// The library itself is Foster's runtime copy; see the footer.

HEADERS :: "/opt/homebrew/include";
LIB_DIR :: "../../../Foster/Framework/Internal/Native/osx-arm64";

#run,stallable {
    set_build_options_dc(.{do_output=false});
    options: Generate_Bindings_Options;
    {
        using options;
        array_add(*include_paths, HEADERS);
        array_add(*source_files, tprint("%/SDL3/SDL.h", HEADERS));
        array_add(*path_fragments_to_treat_as_non_system_paths, "SDL3/");
        array_add(*library_search_paths, LIB_DIR);
        array_add(*libraries, .{filename="libSDL3"});
        generate_library_declarations = false;
        footer = tprint("%\n%", window_flags(), FOOTER);
        log_stripped_declarations = false;
        generate_compile_time_struct_checks = false;
        for FLAGS array_add(*generate_enums_from_macros_with_prefixes, it[0]);
        visitor = visit;
    }
    if !generate_bindings(options, "macos.jai") compiler_set_workspace_status(.FAILED);
}

// #define groups that become enum_flags named after (and typed like) the typedef they belong to.
// More specific prefixes first: a macro joins the first group it matches.
FLAGS :: [2] string.[
    .["SDL_WINDOW_SURFACE_VSYNC_", ""],
    .["SDL_WINDOW_",               "SDL_WindowFlags"],  // only to drop the typedef; see window_flags()
    .["SDL_INIT_",                 "SDL_InitFlags"],
    .["SDL_KMOD_",                 "SDL_Keymod"],
    .["SDL_GPU_TEXTUREUSAGE_",     "SDL_GPUTextureUsageFlags"],
    .["SDL_GPU_BUFFERUSAGE_",      "SDL_GPUBufferUsageFlags"],
    .["SDL_GPU_SHADERFORMAT_",     "SDL_GPUShaderFormat"],
    .["SDL_GPU_COLORCOMPONENT_",   "SDL_GPUColorComponentFlags"],
];

visit :: (decl: *Declaration, parent_decl: *Declaration) -> Declaration_Visit_Result {
    if parent_decl {
        // The generator mangles this multi-line comment when it hoists the field for #overlay.
        if parent_decl.name == "SDL_Event" && decl.name == "padding" decl.comment = .{};
        return .RECURSE;
    }
    for FLAGS {
        if !it[1] continue;
        if decl.kind == .TYPEDEF && decl.name == it[1] {
            decl.decl_flags |= .OMIT_FROM_OUTPUT;
        } else if decl.kind == .ENUM && decl.name == trim_right(it[0], "_") {
            e := cast(*Enum) decl;
            e.output_name = it[1];
            e.flags |= .IS_ENUM_FLAGS;
            for context.generator.global_scope.members if it.kind == .TYPEDEF && it.name == e.output_name e.type = it.type;
        }
    }
    // Platform/compiler plumbing from SDL_begin_code.h, SDL_stdinc.h etc. that has no business in Jai.
    if decl.kind == .MACRO_DEFINITION && !begins_with(decl.name, "SDL_") && !begins_with(decl.name, "SDLK_")
        decl.decl_flags |= .OMIT_FROM_OUTPUT;
    return .RECURSE;
}

// The generator can't evaluate SDL_UINT64_C(...), which every SDL_WINDOW_ flag is wrapped in.
window_flags :: () -> string {
    b: String_Builder;
    append(*b, "SDL_WindowFlags :: enum_flags u64 {\n");
    for line: split(read_entire_file(tprint("%/SDL3/SDL_video.h", HEADERS)), "\n") {
        found, _, rest := split_from_left(line, "#define SDL_WINDOW_");
        found=, name, value := split_from_left(rest, " SDL_UINT64_C(");
        if !found continue;
        print_to_builder(*b, "    % :: %;\n", trim(name), slice(value, 0, find_index_from_left(value, #char ")")));
    }
    append(*b, "}\n");
    return builder_to_string(*b);
}

FOOTER :: #string END
#if OS == .MACOS libsdl3 :: #library "../../../Foster/Framework/Internal/Native/osx-arm64/libSDL3";
END

#import "Basic";
#import "Compiler";
#import "File";
#import "String";
#import "Bindings_Generator";
