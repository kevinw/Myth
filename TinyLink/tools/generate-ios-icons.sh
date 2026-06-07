#!/usr/bin/env bash
set -euo pipefail

if [[ $# -ne 2 ]]; then
  echo "usage: generate-ios-icons.sh <source-png> <appiconset-dir>" >&2
  exit 2
fi

source_png="$1"
appiconset_dir="$2"

if [[ ! -f "$source_png" ]]; then
  echo "missing source icon: $source_png" >&2
  exit 1
fi

if ! command -v sips >/dev/null 2>&1; then
  echo "sips is required to generate iOS app icons" >&2
  exit 1
fi

mkdir -p "$appiconset_dir"

make_icon() {
  local size="$1"
  local name="$2"
  sips -s format png -z "$size" "$size" "$source_png" --out "$appiconset_dir/$name" >/dev/null
}

make_icon 20 AppIcon-20.png
make_icon 29 AppIcon-29.png
make_icon 40 AppIcon-40.png
make_icon 58 AppIcon-29@2x.png
make_icon 60 AppIcon-20@3x.png
make_icon 76 AppIcon-76.png
make_icon 80 AppIcon-40@2x.png
make_icon 87 AppIcon-29@3x.png
make_icon 120 AppIcon-60@2x.png
make_icon 152 AppIcon-76@2x.png
make_icon 167 AppIcon-83.5@2x.png
make_icon 180 AppIcon-60@3x.png
make_icon 1024 AppIcon-1024.png
