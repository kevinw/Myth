// Build side of Hotload: #load this into a build metaprogram and call it from the
// message loop of the game's DYNAMIC_LIBRARY workspace, for every message.
//
// It adds the exports the host needs: `game_frame`, which remaps the host's Context and
// calls `frame_proc`; per file, a `hotload_globals_N` table of that file's globals; and
// `hotload_status`, a *Hotload.Status the game can read to show what the host is doing.
// The tables are added inside each file's scope, so #scope_file globals are included. Globals
// from every file outside the Jai install are carried over: the game's and its own modules'.

hotload_message :: (w: Workspace, message: *Message, frame_proc: string) {
    if message.kind == {
      case .TYPECHECKED;
        for (cast(*Message_Typechecked) message).declarations {
            decl := it.expression;
            file := decl.enclosing_load;
            if !(decl.flags & .IS_GLOBAL) || decl.flags & (.IS_CONSTANT | .ELSEWHERE | .IS_IMPORTED) continue;
            // The game's own globals and its modules' (JaiKit's renderer and audio keep state), but not Jai's.
            if !file || file.from_a_string || begins_with(file.fully_pathed_filename, compiler_get_base_path()) continue;
            array_add(*find_or_add(*hotload_files, file).*, decl);
        }

      case .PHASE;
        phase := cast(*Message_Phase) message;
        if phase.phase != .TYPECHECKED_ALL_WE_CAN return;
        if !hotload_exports_added {
            hotload_exports_added = true;
            add_build_string(tprint(#string END
#import "Remap_Context"(VERBOSE = false);
#program_export game_frame :: () -> bool { Remap_Context(); return %(); }
Hotload :: #import "Hotload";
hotload_status: *Hotload.Status;
#program_export hotload_set_status :: (status: *Hotload.Status) #c_call { hotload_status = status; }
END, frame_proc), w);
        }
        // The tables wait until everything else has typechecked, so they include globals whose
        // types other metaprograms generate on this same phase (like the actor pools).
        if hotload_tables_added || phase.num_items_waiting_to_typecheck return;
        hotload_tables_added = true;

        index := 0;
        for decls, file: hotload_files {
            b: String_Builder;
            print_to_builder(*b, "Hotload_%1 :: #import \"Hotload\";\nhotload_table_%1: [%2] Hotload_%1.Global;\n", index, decls.count);
            print_to_builder(*b, "#program_export hotload_globals_%1 :: () -> [] Hotload_%1.Global #c_call {\n", index);
            for decls {
                key := ifx it.flags & .SCOPE_FILE then tprint("%:%", path_filename(file.fully_pathed_filename), it.name) else it.name;
                print_to_builder(*b, "    hotload_table_%1[%2] = .{\"%3\", type_info(type_of(%4)), *%4};\n", index, it_index, key, it.name);
            }
            print_to_builder(*b, "    return hotload_table_%;\n}\n", index);
            add_build_string(builder_to_string(*b), w, file);
            index += 1;
        }
    }
}

#scope_file

hotload_files: Table(*Message_File, [..] *Code_Declaration);
hotload_exports_added: bool;
hotload_tables_added: bool;

#import "Basic";
#import "Compiler";
#import "Hash_Table";
#import "String";
