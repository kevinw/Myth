# Exports MythCpp as a standalone project, without jet-live, beside FosterGames/, building for macOS
# and (cross-compiled) linux-x64:
#
#   python3 MythCpp/export.py [dir]      default ../MythCpp-export, then `./build.sh run` there
#
# It copies the game, the kit, the shaders and shadercross, FMOD and the ozz runtime, and the part of
# Myth/Assets the game reads. The assets are a snapshot: export again after rebaking Packed/ or the rig.

import os, shutil, subprocess, sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
OUT = os.path.abspath(sys.argv[1] if len(sys.argv) > 1 else os.path.join(ROOT, "..", "MythCpp-export"))
MYTH = os.path.join(ROOT, "Myth")
ASSETS = os.path.join(MYTH, "Assets")

BUILD_SH = r"""#!/bin/bash
# Builds the game: HLSL to shaders with shadercross, then one compiler call over game/ and kit/.
#   ./build.sh                  macOS: build/myth, against Homebrew's sdl3 and freetype
#   ./build.sh run [args]       build, then run (args: room <path>, at <x,y>, hold <key>, demo, shot <png> ...)
#   ./build.sh linux            linux-x64: build-linux/myth, cross-compiled with zig (brew install zig).
#                               zlib and freetype are fetched and built from source the first time.
set -e
cd "$(dirname "$0")"
BREW=$(brew --prefix)
FLAGS=(-std=c++20 -g -O2 -fno-exceptions -fno-rtti -Wall -Wno-missing-braces -Wno-unused-function -DASSETS='"../Assets/"' -Ikit -I"$BREW/include")

shaders() {  # shaders <out> <format> <extension>
    mkdir -p "$1/shaders"
    for f in shaders/*.hlsl; do
        name=$(basename "$f" .hlsl)
        [ "$name" = Common ] && continue  # included by the others
        if [[ $name == *.compute ]]; then stages=compute; name=${name%.compute}; else stages="vertex fragment"; fi
        for stage in $stages; do
            out=$1/shaders/$name.$stage.$3
            [ "$out" -nt "$f" ] || tools/macos/shadercross "$f" -e ${stage}_main -t $stage -s HLSL -d $2 -I shaders -o "$out"
        done
    done
}

if [ "$1" = linux ]; then
    CC=(zig cc -target x86_64-linux-gnu.2.38 -O2)  # glibc 2.38: what Foster's libSDL3.so links against
    DEPS=third_party/linux-x64
    if [ ! -f $DEPS/libdeps.a ]; then
        mkdir -p $DEPS/obj && cd third_party
        curl -sSfL https://github.com/madler/zlib/releases/download/v1.3.1/zlib-1.3.1.tar.gz | tar xz
        curl -sSfL https://download.savannah.gnu.org/releases/freetype/freetype-2.13.3.tar.xz | tar xJ
        cd ..
        # zlib: only what uncompress() needs
        for f in third_party/zlib-1.3.1/{adler32,crc32,inflate,inftrees,inffast,zutil,uncompr}.c; do "${CC[@]}" -c "$f" -o $DEPS/obj/z_$(basename "$f" .c).o; done
        FT=third_party/freetype-2.13.3
        # freetype: base plus every module its default ftmodule.h registers
        for f in base/{ftsystem,ftinit,ftdebug,ftbase,ftbbox,ftbitmap,ftglyph,ftmm,ftsynth,ftstroke} autofit/autofit \
                 truetype/truetype type1/type1 cff/cff cid/type1cid pfr/pfr type42/type42 winfonts/winfnt pcf/pcf bdf/bdf \
                 psaux/psaux psnames/psnames pshinter/pshinter sfnt/sfnt smooth/smooth raster/raster sdf/sdf svg/svg gzip/ftgzip lzw/ftlzw; do
            "${CC[@]}" -DFT2_BUILD_LIBRARY -I$FT/include -c $FT/src/$f.c -o $DEPS/obj/ft_$(basename $f).o
        done
        zig ar rcs $DEPS/libdeps.a $DEPS/obj/*.o
    fi
    shaders build-linux SPIRV spv
    mkdir -p build-linux && cp -P native/linux-x64/* build-linux/
    zig c++ -target x86_64-linux-gnu.2.38 "${FLAGS[@]}" -Ithird_party/freetype-2.13.3/include -Ithird_party/zlib-1.3.1 \
        game/*.cpp kit/*.cpp $DEPS/libdeps.a -o build-linux/myth \
        build-linux/libSDL3.so.0 build-linux/libfmod.so.15 build-linux/libfmodstudio.so.15 build-linux/libmyth_ozz.so -Wl,-rpath,'$ORIGIN'
    echo "built build-linux/myth"
    exit
fi

shaders build MSL msl
cp native/macos/*.dylib build/  # FMOD's libraries load from beside the executable
clang++ "${FLAGS[@]}" -I"$BREW/include/freetype2" game/*.cpp kit/*.cpp -o build/myth \
    -L"$BREW/lib" -lSDL3 -lfreetype -lz -Wl,-rpath,"$BREW/lib" -Wl,-rpath,@executable_path build/*.dylib
echo "built build/myth"
if [ "$1" = run ]; then shift; exec build/myth "$@"; fi
"""


def copy(source, to):
    to = os.path.join(OUT, to)
    os.makedirs(os.path.dirname(to), exist_ok=True)
    (shutil.copytree if os.path.isdir(source) else shutil.copy2)(source, to)


# Inputs git doesn't carry, checked before a previous export is cleared.
LINUX_OZZ = os.path.join(MYTH, "bin", "publish", "linux-x64", "libmyth_ozz.so")
missing = [f"  {os.path.relpath(path, ROOT)}: {fix}" for path, fix in [
    (os.path.join(ROOT, "Foster", "tools", "macos", "shadercross"), "git submodule update --init"),
    (os.path.join(ASSETS, "Packed", "Visuals", "manifest.bin"), "cd Myth && dotnet build"),
    (os.path.join(MYTH, "bin", "Debug", "libmyth_ozz.dylib"), "cd Myth && dotnet build"),
] if not os.path.exists(path)]
if missing:
    sys.exit("missing inputs; run, from FosterGames/:\n" + "\n".join(missing))
if not os.path.exists(LINUX_OZZ):
    print(f"warning: no {os.path.relpath(LINUX_OZZ, ROOT)}, so `./build.sh linux` won't link: cd Myth && node tools/publish-linux.mjs")

# A previous export's sources go; build/ stays, as the build's cache.
for d in ("game", "kit", "shaders", "tools", "native", "Assets"):
    shutil.rmtree(os.path.join(OUT, d), ignore_errors=True)

for f in os.listdir(os.path.join(ROOT, "MythCpp")):
    if f.endswith((".cpp", ".h")):
        copy(os.path.join(ROOT, "MythCpp", f), os.path.join("game", f))
for f in os.listdir(os.path.join(ROOT, "CppKit", "kit")):
    if not f.startswith("live."):
        copy(os.path.join(ROOT, "CppKit", "kit", f), os.path.join("kit", f))

copy(os.path.join(ROOT, "Foster", "Framework", "Content", "Shaders", "Batcher.hlsl"), "shaders/batcher.hlsl")
copy(os.path.join(ROOT, "MythJai", "shaders", "lighting.hlsl"), "shaders/lighting.hlsl")
copy(os.path.join(ROOT, "MythCpp", "shaders", "shadow_depths.compute.hlsl"), "shaders/shadow_depths.compute.hlsl")
for f in ("Crt.hlsl", "PixelSort.hlsl", "Common.hlsl"):
    copy(os.path.join(ASSETS, "Shaders", f), os.path.join("shaders", f))
for f in ("shadercross", "libSDL3.0.dylib", "libdxcompiler.dylib", "libspirv-cross-c-shared.0.dylib"):
    copy(os.path.join(ROOT, "Foster", "tools", "macos", f), os.path.join("tools", "macos", f))

fmod = os.path.join(MYTH, "Platforms", "FMOD", "Native")
copy(os.path.join(fmod, "osx", "libfmod.dylib"), "native/macos/libfmod.dylib")
copy(os.path.join(fmod, "osx", "libfmodstudio.dylib"), "native/macos/libfmodstudio.dylib")
copy(os.path.join(MYTH, "bin", "Debug", "libmyth_ozz.dylib"), "native/macos/libmyth_ozz.dylib")
# Linux: each under its soname, which is what the executable asks for. The ozz runtime is the last
# `node Myth/tools/publish-linux.mjs`'s.
copy(os.path.join(ROOT, "Foster", "Framework", "Internal", "Native", "linux-x64", "libSDL3.so"), "native/linux-x64/libSDL3.so.0")
copy(os.path.join(fmod, "linux-x64", "libfmod.so.15"), "native/linux-x64/libfmod.so.15")
copy(os.path.join(fmod, "linux-x64", "libfmodstudio.so"), "native/linux-x64/libfmodstudio.so.15")
if os.path.exists(LINUX_OZZ):
    copy(LINUX_OZZ, "native/linux-x64/libmyth_ozz.so")

for path in ["Rooms", "Packed/Visuals", "Sources/Sprites", "Sources/Costumes", "Particles",
             "Rigs/ual2.ozzpack", "Rigs/ual2.takes.json", "Fonts/m3x6.ttf", "Fonts/dogica.ttf",
             "Voice/manifest.json", "Voice/voice-de.json", "Sounds/FMOD/Build/Desktop"]:
    copy(os.path.join(ASSETS, path), os.path.join("Assets", path))

with open(os.path.join(OUT, "build.sh"), "w") as f:
    f.write(BUILD_SH)
os.chmod(os.path.join(OUT, "build.sh"), 0o755)
size = subprocess.run(["du", "-sh", OUT], capture_output=True, text=True).stdout.split()[0]
print(f"exported {size} to {OUT}")
