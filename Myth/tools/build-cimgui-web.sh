#!/usr/bin/env bash
set -euo pipefail

# Builds cimgui as a static wasm archive (libcimgui.a) for the browser-wasm build,
# using the exact Emscripten that the installed .NET wasm-tools workload ships
# (3.1.56).

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
# cimgui must match the ImGui.NET managed bindings exactly: ImGui.NET 1.91.6.1 targets
# imgui 1.91.6 (docking); 1.92.x renames functions we call (e.g. igGetIO -> igGetIO_Nil),
# so we vendor our own pinned checkout.
CIMGUI_TAG="${CIMGUI_TAG:-1.91.6dock}"
CIMGUI_URL="https://github.com/cimgui/cimgui.git"
SRC="$ROOT/Platforms/Web/Native/cimgui-src"
BUILD="$ROOT/Platforms/Web/Native/cimgui-build"
OUT="$ROOT/Platforms/Web/Native"

if [[ ! -f "$SRC/cimgui.cpp" ]]; then
  echo "Cloning cimgui $CIMGUI_TAG into $SRC ..."
  rm -rf "$SRC"
  git clone --depth 1 --branch "$CIMGUI_TAG" --recurse-submodules "$CIMGUI_URL" "$SRC"
fi

if [[ ! -d "$SRC/imgui" ]]; then
  echo "Missing imgui submodule at: $SRC/imgui" >&2
  echo "Delete $SRC and re-run to re-clone." >&2
  exit 1
fi

# imgui/cimgui default ImTextureID to ImU64 (8 bytes). ImGui.NET marshals it as IntPtr,
# which is 4 bytes on wasm32, so on the browser the P/Invoke signatures and the ImDrawCmd
# struct layout mismatch the native lib. Pin ImTextureID to a 4-byte type so both sides
# agree on wasm32. Idempotent (matches nothing on re-run). Desktop is 64-bit and uses
# the unmodified ImGui.NET native, so this only affects the web build.
sed -i '' 's/typedef ImU64 ImTextureID;/typedef unsigned int ImTextureID;/' \
  "$SRC/cimgui.h" "$SRC/imgui/imgui.h"

# Locate the Emscripten packs the .NET wasm workload installed. The expected
# version is pinned by the workload manifest (microsoft.net.workload.emscripten.current).
EM_VERSION="3.1.56"
PACKS="${DOTNET_PACKS_ROOT:-$HOME/.dotnet/packs}"
RID="osx-arm64"
case "$(uname -s)-$(uname -m)" in
  Darwin-arm64) RID="osx-arm64" ;;
  Darwin-x86_64) RID="osx-x64" ;;
  Linux-x86_64) RID="linux-x64" ;;
  Linux-aarch64) RID="linux-arm64" ;;
esac

pack_dir() { # name -> newest versioned tools dir
  local name="$1" dirs
  dirs=("$PACKS/Microsoft.NET.Runtime.Emscripten.$EM_VERSION.$name.$RID"/*/tools)
  echo "${dirs[${#dirs[@]}-1]}"
}

EMSDK_PATH="$(pack_dir Sdk)"
NODE_DIR="$(pack_dir Node)"
PY_DIR="$(pack_dir Python)"
CACHE_DIR="$(pack_dir Cache)/emscripten/cache"

if [[ ! -x "$EMSDK_PATH/emscripten/emcc" ]]; then
  echo "Could not find Emscripten $EM_VERSION SDK pack under $PACKS." >&2
  echo "Install the wasm workload: dotnet workload install wasm-tools" >&2
  exit 1
fi

export EMSDK_PATH
export DOTNET_EMSCRIPTEN_LLVM_ROOT="$EMSDK_PATH/bin"
export DOTNET_EMSCRIPTEN_BINARYEN_ROOT="$EMSDK_PATH"
export DOTNET_EMSCRIPTEN_NODE_JS="$NODE_DIR/bin/node"
export EMSDK_PYTHON="$PY_DIR/bin/python3"
# Use the workload's prebuilt (frozen) Emscripten cache so we never rebuild the sysroot.
export EM_CACHE="${EM_CACHE:-$CACHE_DIR}"

# shellcheck disable=SC1091
source "$EMSDK_PATH/emsdk_env.sh" >/dev/null

echo "Using emcc: $(command -v emcc)"
emcc --version | head -1

rm -rf "$BUILD"
mkdir -p "$BUILD" "$OUT"

emcmake cmake -S "$SRC" -B "$BUILD" -G Ninja \
  -DCMAKE_BUILD_TYPE=Release \
  -DIMGUI_STATIC=yes \
  -DCIMGUI_TEST=no

cmake --build "$BUILD" --config Release

archive="$BUILD/libcimgui.a"
[[ -f "$archive" ]] || archive="$BUILD/cimgui.a"
if [[ ! -f "$archive" ]]; then
  echo "Unable to find built cimgui archive in $BUILD" >&2
  exit 1
fi

# Name the output cimgui.a (not libcimgui.a): the .NET PInvokeTableGenerator derives
# the static module name from the archive filename, and ImGui.NET's DllImports target
# "cimgui". A "lib" prefix would yield module "libcimgui" and silently link nothing.
cp "$archive" "$OUT/cimgui.a"
echo "Built: $OUT/cimgui.a"
