#!/bin/bash
# Builds the game without hot reload (build.py is the jet-live build): HLSL to shaders with Foster's
# shadercross, then one compiler call over MythCpp and CppKit/kit.
#   ./build.sh                  macOS: build/myth, against Homebrew's sdl3 and freetype
#   ./build.sh run [args]       build, then run (args: room <path>, at <x,y>, hold <key>, demo, shot <png> ...)
#   ./build.sh linux            linux-x64: build-linux/myth, cross-compiled with zig (brew install zig).
#                               zlib and freetype are fetched and built from source the first time.
set -e
cd "$(dirname "$0")"
BREW=$(brew --prefix)
KIT=../CppKit/kit
SOURCES=(*.cpp $(ls $KIT/*.cpp | grep -v /live.cpp))
FLAGS=(-std=c++20 -g -O2 -fno-exceptions -fno-rtti -Wall -Wno-missing-braces -Wno-unused-function -I$KIT -I"$BREW/include")
SHADERS=(../Foster/Framework/Content/Shaders/Batcher.hlsl ../MythJai/shaders/lighting.hlsl shaders/shadow_depths.compute.hlsl
         ../Myth/Assets/Shaders/Crt.hlsl ../Myth/Assets/Shaders/PixelSort.hlsl)
FMOD=../Myth/Platforms/FMOD/Native

shaders() {  # shaders <out> <format> <extension>
    mkdir -p "$1/shaders"
    for f in "${SHADERS[@]}"; do
        name=$(basename "$f" .hlsl)
        [ "$name" = Batcher ] && name=batcher
        if [[ $name == *.compute ]]; then stages=compute; name=${name%.compute}; else stages="vertex fragment"; fi
        for stage in $stages; do
            out=$1/shaders/$name.$stage.$3
            [ "$out" -nt "$f" ] || ../Foster/tools/macos/shadercross "$f" -e ${stage}_main -t $stage -s HLSL -d $2 -I "$(dirname "$f")" -o "$out"
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
    # Each library under its soname, which is what the executable asks for. The ozz runtime is
    # Myth's last `node tools/publish-linux.mjs`'s.
    cp ../Foster/Framework/Internal/Native/linux-x64/libSDL3.so build-linux/libSDL3.so.0
    cp $FMOD/linux-x64/libfmod.so.15 build-linux/
    cp $FMOD/linux-x64/libfmodstudio.so build-linux/libfmodstudio.so.15
    cp ../Myth/bin/publish/linux-x64/libmyth_ozz.so build-linux/
    zig c++ -target x86_64-linux-gnu.2.38 "${FLAGS[@]}" -Ithird_party/freetype-2.13.3/include -Ithird_party/zlib-1.3.1 \
        "${SOURCES[@]}" $DEPS/libdeps.a -o build-linux/myth \
        build-linux/libSDL3.so.0 build-linux/libfmod.so.15 build-linux/libfmodstudio.so.15 build-linux/libmyth_ozz.so -Wl,-rpath,'$ORIGIN'
    echo "built build-linux/myth"
    exit
fi

shaders build MSL msl
cp $FMOD/osx/libfmod.dylib $FMOD/osx/libfmodstudio.dylib ../Myth/bin/Debug/libmyth_ozz.dylib build/  # loaded from beside the executable
clang++ "${FLAGS[@]}" -I"$BREW/include/freetype2" "${SOURCES[@]}" -o build/myth \
    -L"$BREW/lib" -lSDL3 -lfreetype -lz -Wl,-rpath,"$BREW/lib" -Wl,-rpath,@executable_path \
    build/libfmod.dylib build/libfmodstudio.dylib build/libmyth_ozz.dylib
echo "built build/myth"
if [ "$1" = run ]; then shift; exec build/myth "$@"; fi
