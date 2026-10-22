// build.mc: `minc run` / `minc build` / `minc wasm` in this folder land here.
//
//   minc run     build the host and run it (the game hot-reloads)
//   minc build   build the host
//   minc wasm    stage build/web (page, wasm, sprites) and serve it on :8080
//
// The browser reads files from a VFS the page preloads from assets.json, so
// the sprites are copied next to the page and listed there.

import process;
import file;
import str;

str SPRITES = "../Myth/Assets/Sources/Sprites";
str WEB = "build/web";

i32 run(ProcCmd* c) { var r = proc_run(c); return r.exit_code; }

bool copy_sprites(str_buf* manifest, str ext) {
    var files = dir_list_ext(SPRITES, ext);
    defer dir_list_free(&files);
    for i32 i = 0; i < files.count; i++ {
        var src = path_join(SPRITES, files.items[i]);
        defer free(src);
        var dst = format("{}/sprites/{}", WEB, files.items[i]);
        defer free(dst);
        if !file_copy(src, dst) { print("can't copy {}\n", src); return false; }
        str_buf_add(manifest, manifest.len == 1 ? "\"" : ",\"");
        str_buf_add(manifest, "sprites/");
        str_buf_add(manifest, files.items[i]);
        str_buf_add(manifest, "\"");
    }
    return true;
}

i32 wasm() {
    var sprites = format("{}/sprites", WEB);
    defer free(sprites);
    if !dir_create(sprites) { print("can't create {}\n", sprites); return 1; }

    var wasm_out = format("{}/main.wasm", WEB);
    defer free(wasm_out);
    ProcCmd build = { .args = { "minc", "--target", "wasm", "main.mc", "-o", wasm_out } };
    if run(&build) != 0 { return 1; }

    // The page and its JS host ship with minc.
    var minc = path_which("minc");
    defer free(minc);
    var host_dir = path_join(path_dirname(minc), "lib/wasm");
    defer free(host_dir);
    str[3] shipped = { "sokol_wasm_harness.html", "sokol_wasm_host.js", "sokol_wasm_audio_worklet.js" };
    str[3] to = { "index.html", "sokol_wasm_host.js", "sokol_wasm_audio_worklet.js" };
    for i32 i = 0; i < 3; i++ {
        var src = path_join(host_dir, shipped[i]);
        defer free(src);
        var dst = path_join(WEB, to[i]);
        defer free(dst);
        if !file_copy(src, dst) { print("can't copy {}\n", src); return 1; }
    }

    str_buf manifest;
    str_buf_init(&manifest);
    defer str_buf_free(&manifest);
    str_buf_add(&manifest, "[");
    if !copy_sprites(&manifest, ".ase") || !copy_sprites(&manifest, ".aseprite") { return 1; }
    str_buf_add(&manifest, "]");
    var manifest_path = path_join(WEB, "assets.json");
    defer free(manifest_path);
    if !file_write_str(manifest_path, str_buf_to_str(&manifest)) { return 1; }

    print("serving http://127.0.0.1:8080/index.html?app=main (ctrl+c quits)\n");
    ProcCmd serve = { .args = { "python3", "-m", "http.server", "8080", "--bind", "127.0.0.1", "--directory", WEB } };
    return run(&serve);
}

i32 main() {
    str verb = get_argc() > 1 ? str_from_cstr(get_arg(1)) : "run";
    if str_equal(verb, "wasm") { return wasm(); }
    if str_equal(verb, "build") {
        ProcCmd c = { .args = { "minc", "main.mc", "-o", "build/main" } };
        return run(&c);
    }
    if str_equal(verb, "run") {
        ProcCmd c = { .args = { "minc", "main.mc", "-o", "build/main" } };
        if run(&c) != 0 { return 1; }
        ProcCmd game = { .args = { "build/main" } };
        return run(&game);
    }
    print("usage: minc [run | build | wasm]\n");
    return 1;
}
