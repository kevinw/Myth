// Build side of typed actor pools: every struct noted @GameActor gets a pool in a generated
// `Actors` struct, so game code says `for context.actors.blobs` instead of filtering one big
// list, and calls resolve per type at compile time instead of through virtuals.
//
// Generated (into the main program scope):
//   Actors :: struct { blobs: Pool(Blob); lights: Pool(Light); ... }
//   spawn(*Actors, json) -> bool      adds the actor named by json's "Type" and reads its fields
//   update_all / draw_all(*Actors)    calls update(it) / draw(it) for every live actor; overloading
//                                     picks the type's own procedure, else the game's *Actor default
//   for actors { }                    every live actor, as *Actor
//   clear_all(*Actors)
//
// The game supplies Actor (with alive, generation), and update/draw for *Actor.

actors_message :: (w: Workspace, message: *Message) {
    if message.kind == {
      case .TYPECHECKED;
        for (cast(*Message_Typechecked) message).structs {
            s := it.expression;
            if s.defined_type for s.notes if it.text == "GameActor" array_add_if_unique(*actor_types, s.defined_type.name);
        }

      case .PHASE;
        if actors_added || !actor_types || (cast(*Message_Phase) message).phase != .TYPECHECKED_ALL_WE_CAN return;
        actors_added = true;
        quick_sort(actor_types, compare);

        b: String_Builder;
        append(*b, "Actors :: struct {\n");
        for actor_types print_to_builder(*b, "    %: Pool(%);\n", pool_name(it), it);
        append(*b, "}\n\nspawn :: (actors: *Actors, json: Json) -> bool {\n    if as_string(get(json, \"Type\")) == {\n");
        for actor_types print_to_builder(*b, "      case \"%\"; read_fields(add(*actors.%), json);\n", it, pool_name(it));
        append(*b, "      case; return false;\n    }\n    return true;\n}\n\n");
        for proc: string.["update", "draw"] {
            print_to_builder(*b, "%_all :: (actors: *Actors) {\n", proc);
            for actor_types print_to_builder(*b, "    for actors.% %(it);\n", pool_name(it), proc);
            append(*b, "}\n\n");
        }
        append(*b, "clear_all :: (actors: *Actors) {\n");
        for actor_types print_to_builder(*b, "    clear(*actors.%);\n", pool_name(it));
        append(*b, "}\n\nfor_expansion :: (actors: *Actors, body: Code, flags: For_Flags) #expand {\n");
        for actor_types print_to_builder(*b, "    for actor: actors.% { `it := cast(*Actor) actor; `it_index := -1; #insert body; }\n", pool_name(it));
        append(*b, "}\n");
        add_build_string(builder_to_string(*b), w);
    }
}

#scope_file

actor_types: [..] string;
actors_added: bool;

// PlayerStart -> player_starts
pool_name :: (type: string) -> string {
    b: String_Builder;
    for 0..type.count-1 {
        c := type[it];
        if c >= #char "A" && c <= #char "Z" {
            if it append(*b, #char "_");
            c += 32;
        }
        append(*b, c);
    }
    name := builder_to_string(*b);
    if ends_with(name, "s") || ends_with(name, "x") || ends_with(name, "ch") || ends_with(name, "sh") return tprint("%es", name);
    if ends_with(name, "y") && name.count > 1 && !is_any(name[name.count - 2], "aeiou") return tprint("%ies", slice(name, 0, name.count - 1));
    return tprint("%s", name);
}

#import "Basic";
#import "Compiler";
#import "Sort";
#import "String";
