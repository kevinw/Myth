// minc 0.9.18, macOS arm64: libminc crashes on the second compile in a
// process when the compiled source indexes an array with a runtime index
// (i.e. emits a bounds check).
//
//   minc run libminc_recompile.mc
//
// Expected: both scripts compile three times.
// Actual: the "index" script compiles once, then the second
// minc_compile_file segfaults inside libminc (exit 139):
//
//   control: compile 0 ok compile 1 ok compile 2 ok
//   index: compile 0 okcrash: access violation (minc debug libminc_recompile.mc shows where)
//
// Same crash when:
//   - the array is a global instead of a local
//   - each compile uses a fresh minc_create() context
//   - previous modules/contexts are never freed
// A constant index (`a[1]`) or indexing through a pointer (`p[y]`) is fine.
// The minc-samples hotreload demo passes because its script never indexes
// an array.

import libminc;
import file;
import str;

void compile_three_times(str name, str source) {
    var path = format("{}.mc", name);
    defer free(path);
    ignore file_write_str(path, source);
    var cpath = str_to_cstr(path);
    defer free(cpath);

    print("{}:", name);
    var ctx = minc_create();
    for i32 i = 0; i < 3; i++ {
        var m = minc_compile_file(ctx, cpath);
        print(" compile {} {}", i, m != null ? "ok" : "failed");
    }
    print("\n");
    minc_destroy(ctx);
}

i32 main() {
    compile_three_times("control", "export i32 f(i32 y) { i32[3] a; return a[1]; }\n");
    compile_three_times("index", "export i32 f(i32 y) { i32[3] a; return a[y]; }\n");
    return 0;
}
