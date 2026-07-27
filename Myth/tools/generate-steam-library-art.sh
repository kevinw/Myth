#!/usr/bin/env bash
set -euo pipefail

root="$(cd "$(dirname "$0")/.." && pwd)"
media="$root/Production/SteamMedia"
source="${1:-$media/duck.png}"
logo="$media/logo.png"
output="$media/Library"

if ! command -v magick >/dev/null 2>&1; then
  echo "ImageMagick is required to generate Steam library art" >&2
  exit 1
fi

if [[ ! -f "$source" ]]; then
  echo "missing source image: $source" >&2
  exit 1
fi

if [[ ! -f "$logo" ]]; then
  echo "missing logo: $logo" >&2
  exit 1
fi

mkdir -p "$output"

magick "$source" -resize '600x900^' -gravity center -extent 600x900 \
  \( -size 600x300 gradient:'#100c20d9-#100c2000' \) -gravity north -composite \
  \( "$logo" -filter point -resize 480x \) -gravity north -geometry +0+70 -composite \
  -background black -alpha remove -alpha off -strip -sampling-factor 4:4:4 -quality 88 \
  "$output/library-capsule.jpg"

magick "$source" -resize '920x430^' -gravity south -extent 920x430 \
  \( -size 920x190 gradient:'#100c20d9-#100c2000' \) -gravity north -composite \
  \( "$logo" -filter point -resize 480x \) -gravity northwest -geometry +48+38 -composite \
  -background black -alpha remove -alpha off -strip -sampling-factor 4:4:4 -quality 88 \
  "$output/library-header.jpg"

magick "$source" -resize '3840x1240^' -gravity south -extent 3840x1240 \
  -background black -alpha remove -alpha off -strip -sampling-factor 4:4:4 -quality 88 \
  "$output/library-hero.jpg"

magick "$logo" -filter point -resize 960x -strip "$output/library-logo.png"

echo "Generated Steam library art in $output"
