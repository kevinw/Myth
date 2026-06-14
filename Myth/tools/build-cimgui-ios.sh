#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
SRC="$ROOT/Platforms/iOS/Native/cimgui-src"
BUILD="$ROOT/Platforms/iOS/Native/cimgui-build"
OUT="$ROOT/Platforms/iOS/Native/cimgui.xcframework"
DEPLOYMENT_TARGET="${IOS_DEPLOYMENT_TARGET:-14.2}"

if [[ ! -d "$SRC/imgui" ]]; then
  echo "Missing cimgui source or imgui submodule at: $SRC" >&2
  echo "Run: git submodule update --init --recursive Myth/Platforms/iOS/Native/cimgui-src" >&2
  exit 1
fi

build_slice() {
  local name="$1"
  local sdk="$2"
  local archs="$3"
  local build_dir="$BUILD/$name"
  local framework_dir="$BUILD/frameworks/$name/cimgui.framework"

  cmake -S "$SRC" -B "$build_dir" -G Ninja \
    -DCMAKE_SYSTEM_NAME=iOS \
    -DCMAKE_OSX_SYSROOT="$sdk" \
    -DCMAKE_OSX_ARCHITECTURES="$archs" \
    -DCMAKE_OSX_DEPLOYMENT_TARGET="$DEPLOYMENT_TARGET" \
    -DCMAKE_BUILD_TYPE=Release \
    -DCMAKE_INSTALL_NAME_DIR="@rpath/cimgui.framework" \
    -DCMAKE_XCODE_ATTRIBUTE_ONLY_ACTIVE_ARCH=NO \
    -DIMGUI_STATIC=no \
    -DCIMGUI_TEST=no

  cmake --build "$build_dir" --config Release

  rm -rf "$framework_dir"
  mkdir -p "$framework_dir/Headers" "$framework_dir/Modules"

  local dylib="$build_dir/cimgui.dylib"
  if [[ ! -f "$dylib" ]]; then
    dylib="$build_dir/libcimgui.dylib"
  fi
  if [[ ! -f "$dylib" ]]; then
    echo "Unable to find built cimgui dylib in $build_dir" >&2
    exit 1
  fi

  cp "$dylib" "$framework_dir/cimgui"
  cp "$SRC/cimgui.h" "$framework_dir/Headers/cimgui.h"
  cp "$SRC/cimconfig.h" "$framework_dir/Headers/cimconfig.h"

  install_name_tool -id "@rpath/cimgui.framework/cimgui" "$framework_dir/cimgui"

  cat > "$framework_dir/Info.plist" <<'PLIST'
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN"
  "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>CFBundleDevelopmentRegion</key>
  <string>en</string>
  <key>CFBundleExecutable</key>
  <string>cimgui</string>
  <key>CFBundleIdentifier</key>
  <string>com.kev.cimgui</string>
  <key>CFBundleInfoDictionaryVersion</key>
  <string>6.0</string>
  <key>CFBundleName</key>
  <string>cimgui</string>
  <key>CFBundlePackageType</key>
  <string>FMWK</string>
  <key>CFBundleShortVersionString</key>
  <string>1.91.6</string>
  <key>CFBundleVersion</key>
  <string>1</string>
  <key>MinimumOSVersion</key>
  <string>14.2</string>
</dict>
</plist>
PLIST

  cat > "$framework_dir/Modules/module.modulemap" <<'MODULEMAP'
framework module cimgui {
  umbrella header "cimgui.h"
  export *
  module * { export * }
}
MODULEMAP
}

rm -rf "$BUILD/frameworks" "$OUT"

build_slice "ios-arm64" "iphoneos" "arm64"
build_slice "ios-simulator" "iphonesimulator" "arm64;x86_64"

xcodebuild -create-xcframework \
  -framework "$BUILD/frameworks/ios-arm64/cimgui.framework" \
  -framework "$BUILD/frameworks/ios-simulator/cimgui.framework" \
  -output "$OUT"

echo "Built: $OUT"
