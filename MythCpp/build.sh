#!/bin/bash
# Builds the game without hot reload (build.py is the jet-live build): HLSL to shaders with Foster's
# shadercross, then a generated <out>/build.ninja compiles MythCpp and CppKit/kit incrementally (needs ninja).
#   ./build.sh                  macOS: build/myth, against Homebrew's sdl3 and freetype
#   ./build.sh run [args]       build, then run (args: room <path>, at <x,y>, hold <key>, demo, shot <png> ...)
#   ./build.sh linux            linux-x64: build-linux/myth, cross-compiled with zig (brew install zig).
#                               zlib and freetype are fetched and built from source the first time.
# On a Linux host it builds natively instead, into build-linux/ (see linux_host below).
set -e
cd "$(dirname "$0")"
KIT=../CppKit/kit
SOURCES=(*.cpp $(ls $KIT/*.cpp | grep -v /live.cpp))
FLAGS=(-std=c++20 -g -O2 -fno-exceptions -fno-rtti -Wall -Wno-missing-braces -Wno-unused-function -I$KIT)
SHADERCROSS=../Foster/tools/$([ "$(uname)" = Linux ] && echo linux || echo macos)/shadercross
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
            [ "$out" -nt "$f" ] || $SHADERCROSS "$f" -e ${stage}_main -t $stage -s HLSL -d $2 -I "$(dirname "$f")" -o "$out"
        done
    done
}

linux_libs() {
    # Each library under its soname, which is what the executable asks for. The ozz runtime is
    # Myth's last `node tools/publish-linux.mjs`'s.
    mkdir -p build-linux
    cp ../Foster/Framework/Internal/Native/linux-x64/libSDL3.so build-linux/libSDL3.so.0
    cp $FMOD/linux-x64/libfmod.so.15 build-linux/
    cp $FMOD/linux-x64/libfmodstudio.so build-linux/libfmodstudio.so.15
    cp ../Myth/bin/publish/linux-x64/libmyth_ozz.so build-linux/
    LINUX_LIBS=(build-linux/libSDL3.so.0 build-linux/libfmod.so.15 build-linux/libfmodstudio.so.15 build-linux/libmyth_ozz.so -Wl,-rpath,'$ORIGIN')
}

compile() {  # compile <out> <link args...>: CXX compiles each of SOURCES with FLAGS, then links <out>/myth
    local q objs=() o
    q() { printf '%q ' "$@" | sed 's/\$/$$/g'; }
    {
        echo "builddir = $1"
        echo "cxx = $(q "${CXX[@]}")"
        echo "flags = $(q "${FLAGS[@]}")"
        echo "ldflags = $(q "${@:2}")"
        echo 'rule cc
  command = $cxx $flags -MMD -MF $out.d -c $in -o $out
  depfile = $out.d
  deps = gcc
  description = cc $in
rule link
  command = $cxx $in -o $out $ldflags
  description = link $out'
        for s in "${SOURCES[@]}"; do o=$1/obj/$(basename "$s" .cpp).o; objs+=("$o"); echo "build $o: cc $s"; done
        echo "build $1/myth: link ${objs[*]}"
    } > "$1/build.ninja"
    ninja -f "$1/build.ninja" && ninja -f "$1/build.ninja" -t compdb cc > ../compile_commands.json  # for clangd, at the root so CppKit is covered too
}

fetch_deb() {  # fetch_deb <url> <dir>: unpacks a .deb's files into <dir>, no root needed
    mkdir -p "$2" && curl -sSfL -o "$2.deb" "$1" && dpkg-deb -x "$2.deb" "$2" || { echo "can't fetch $1 (bump its version?)"; exit 1; }
}

# Linux, natively (an agent's sandbox, say): the system's c++, freetype and zlib (apt-get install g++
# ninja-build libfreetype-dev zlib1g-dev), and SDL3's headers from Debian's package, as Ubuntu 24.04 has none.
# `run` without a Vulkan driver fetches Mesa's software one, lavapipe (better: apt-get install
# mesa-vulkan-drivers, as it only loads with Mesa's dependencies installed); without a display, Xvfb.
if [ "$(uname)" = Linux ]; then
    SDL3=third_party/libsdl3-dev
    [ -f $SDL3/usr/include/SDL3/SDL.h ] || fetch_deb https://deb.debian.org/debian/pool/main/libs/libsdl3/libsdl3-dev_3.4.18+ds-1_amd64.deb $SDL3
    shaders build-linux SPIRV spv
    linux_libs
    c++ --version | grep -q clang || FLAGS+=(-Wno-class-memaccess)  # g++'s
    CXX=(c++) FLAGS+=(-I$SDL3/usr/include -I/usr/include/freetype2)
    compile build-linux "${LINUX_LIBS[@]}" -lfreetype -lz
    [ "$1" = run ] || exit 0
    shift
    if ! ls /usr/share/vulkan/icd.d/*.json >/dev/null 2>&1; then
        LVP=third_party/mesa-vulkan-drivers
        [ -f $LVP/lvp_icd.json ] || { fetch_deb http://archive.ubuntu.com/ubuntu/pool/main/m/mesa/mesa-vulkan-drivers_25.2.8-0ubuntu0.24.04.4_amd64.deb $LVP
            sed "s#\"libvulkan_lvp.so\"#\"$(pwd)/$LVP/usr/lib/x86_64-linux-gnu/libvulkan_lvp.so\"#" $LVP/usr/share/vulkan/icd.d/lvp_icd.json > $LVP/lvp_icd.json; }
        export VK_DRIVER_FILES=$(pwd)/$LVP/lvp_icd.json VK_ICD_FILENAMES=$(pwd)/$LVP/lvp_icd.json
    fi
    export SDL_VIDEODRIVER=x11
    [ -n "$DISPLAY" ] || exec xvfb-run -a -s "-screen 0 1280x720x24" build-linux/myth "$@"
    exec build-linux/myth "$@"
fi

BREW=$(brew --prefix)
FLAGS+=(-I"$BREW/include")

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
    linux_libs
    CXX=(zig c++ -target x86_64-linux-gnu.2.38) FLAGS+=(-Ithird_party/freetype-2.13.3/include -Ithird_party/zlib-1.3.1)
    compile build-linux $DEPS/libdeps.a "${LINUX_LIBS[@]}"
    exit
fi

shaders build MSL msl
cp $FMOD/osx/libfmod.dylib $FMOD/osx/libfmodstudio.dylib ../Myth/bin/Debug/libmyth_ozz.dylib build/  # loaded from beside the executable
CXX=(clang++) FLAGS+=(-I"$BREW/include/freetype2")
compile build -L"$BREW/lib" -lSDL3 -lfreetype -lz -Wl,-rpath,"$BREW/lib" -Wl,-rpath,@executable_path \
    build/libfmod.dylib build/libfmodstudio.dylib build/libmyth_ozz.dylib
if [ "$1" = run ]; then shift; exec build/myth "$@"; fi
