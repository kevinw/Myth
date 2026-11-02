# CppKit's build: plain clang++, no build system, shaped for jet-live's hot reload.
#
#   python3 build.py          build (from a game's directory, via its own build.py)
#   python3 build.py run      build, then run
#
# Every source compiles to its own object with -MD depfiles, and compile_commands.json lands
# next to the executable: that's how jet-live finds what to recompile when a file is saved.
# jet-live itself is fetched at a pinned commit and built once with its own CMake.

import json, os, shlex, subprocess, sys, shutil
from concurrent.futures import ThreadPoolExecutor

KIT = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(KIT)
JET_LIVE_COMMIT = "394948ee312d83c75ca415ff9042d664678f7b38"
JET_LIVE = os.path.join(KIT, "third_party", "jet-live")
SHADERCROSS = os.path.join(ROOT, "Foster", "tools", "macos", "shadercross")
BREW = "/opt/homebrew"

CXX = "clang++"
FLAGS = [
    "-std=c++20", "-g", "-O0",
    "-DLIVE",                   # the game calls into kit/live.h
    "-MD",                      # depfiles: which headers each object depends on
    "-falign-functions=16",     # room for jet-live's hook trampolines at every function
    "-fno-exceptions", "-fno-rtti",
    "-fvisibility-inlines-hidden",  # a reload's inline functions are its own, not coalesced with the running ones
    "-Wall", "-Wno-missing-braces", "-Wno-unused-function",
    "-Werror=global-constructors",  # a reload runs its globals' constructors as it loads, and crashes
]


def run(cmd, **kw):
    result = subprocess.run(cmd, **kw)
    if result.returncode != 0:
        sys.exit(f"failed: {' '.join(cmd) if isinstance(cmd, list) else cmd}")


def jet_live():
    """jet-live's dylib, built once from the pinned commit."""
    lib = os.path.join(JET_LIVE, "build", "libjet-live.dylib")
    if os.path.exists(lib):
        return lib
    if not os.path.exists(os.path.join(JET_LIVE, ".git")):
        os.makedirs(os.path.dirname(JET_LIVE), exist_ok=True)
        run(["git", "clone", "--quiet", "https://github.com/ddovod/jet-live.git", JET_LIVE])
    run(["git", "-C", JET_LIVE, "checkout", "--quiet", JET_LIVE_COMMIT])
    build = os.path.join(JET_LIVE, "build")
    run(["cmake", "-S", JET_LIVE, "-B", build, "-G", "Ninja", "-DCMAKE_BUILD_TYPE=Debug",
         "-DJET_LIVE_SHARED=ON", "-DJET_LIVE_BUILD_EXAMPLE=OFF"], stdout=subprocess.DEVNULL)
    run(["ninja", "-C", build, "jet-live"], stdout=subprocess.DEVNULL)
    return lib


def shaders(out, extra):
    """HLSL to MSL with Foster's shadercross: the batcher's, and any the game names."""
    os.makedirs(os.path.join(out, "shaders"), exist_ok=True)
    sources = [(os.path.join(ROOT, "Foster", "Framework", "Content", "Shaders", "Batcher.hlsl"), "batcher")]
    sources += [(path, os.path.splitext(os.path.basename(path))[0]) for path in extra]
    for source, name in sources:
        # name.compute.hlsl is a compute kernel; anything else is the batcher's vertex and a fragment.
        stages = ("compute",) if name.endswith(".compute") else ("vertex", "fragment")
        if name.endswith(".compute"):
            name = name[:-len(".compute")]
        for stage in stages:
            target = os.path.join(out, "shaders", f"{name}.{stage}.msl")
            if os.path.exists(target) and os.path.getmtime(target) > os.path.getmtime(source):
                continue
            run([SHADERCROSS, source, "-e", f"{stage}_main", "-t", stage, "-s", "HLSL", "-d", "MSL",
                 "-I", os.path.dirname(source), "-o", target])


def build(name, sources, shader_sources=(), copy=(), libs=(), args=()):
    """Builds `name` from the game's directory into build/."""
    game = os.getcwd()
    out = os.path.join(game, "build")
    objects_dir = os.path.join(out, "obj")
    os.makedirs(objects_dir, exist_ok=True)
    lib = jet_live()
    shaders(out, shader_sources)

    sources = [os.path.abspath(s) for s in sources] + [os.path.join(KIT, "kit", f) for f in sorted(os.listdir(os.path.join(KIT, "kit"))) if f.endswith(".cpp")]
    jet_includes = ["src", "libs/efsw/include", "libs/tiny-process-library", "libs/utils"]
    includes = ["-I" + os.path.join(KIT, "kit"), *["-I" + os.path.join(JET_LIVE, d) for d in jet_includes], "-I" + os.path.join(BREW, "include"), "-I" + os.path.join(BREW, "include", "freetype2"), "-I" + game]
    commands = []
    def compile(source):
        obj = os.path.join(objects_dir, os.path.relpath(source, ROOT).replace(os.sep, "_") + ".o")
        cmd = [CXX, *FLAGS, *includes, "-c", source, "-o", obj]
        commands.append({"directory": game, "file": source, "output": obj, "command": shlex.join(cmd)})  # jet-live reads "command"
        # Rebuild when the source or anything it included (per its depfile) is newer than the object.
        dep = obj[:-2] + ".d"
        if os.path.exists(obj) and os.path.exists(dep):
            newest = max((os.path.getmtime(f) for f in depfile_inputs(dep) if os.path.exists(f)), default=0)
            if newest < os.path.getmtime(obj):
                return obj, None
        return obj, subprocess.run(cmd, capture_output=True, text=True)
    with ThreadPoolExecutor() as pool:
        results = list(pool.map(compile, sources))
    failed = False
    for obj, result in results:
        if result and result.returncode != 0:
            print(result.stderr, end="")
            failed = True
        elif result and result.stderr:
            print(result.stderr, end="")
    with open(os.path.join(out, "compile_commands.json"), "w") as f:
        json.dump(commands, f, indent=1)
    if failed:
        sys.exit(1)

    exe = os.path.join(out, name)
    run([CXX, *[obj for obj, _ in results], "-o", exe, "-g",
         "-Wl,-export_dynamic", "-Wl,-flat_namespace", "-Wl,-undefined,dynamic_lookup",   # what jet-live needs
         lib, "-Wl,-rpath," + os.path.dirname(lib),
         "-L" + os.path.join(BREW, "lib"), "-lSDL3", "-lfreetype", "-lz", "-Wl,-rpath," + os.path.join(BREW, "lib"),
         "-Wl,-rpath,@executable_path", *libs])
    # Only when changed: overwriting a library under a running game would kill it.
    for path in copy:
        to = os.path.join(out, os.path.basename(path))
        if not os.path.exists(to) or os.path.getmtime(to) < os.path.getmtime(path):
            shutil.copy2(path, out)
    print(f"built {os.path.relpath(exe)}")
    if "run" in args:
        os.execv(exe, [exe])


def depfile_inputs(dep):
    with open(dep) as f:
        text = f.read().replace("\\\n", " ")
    _, _, inputs = text.partition(":")
    return inputs.split()
