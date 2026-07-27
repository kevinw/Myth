#!/usr/bin/env bash
set -euo pipefail

root="$(cd "$(dirname "$0")/.." && pwd)"
art="$root/Production/SteamMedia/Library"
appid=2829988821
gameid=12154709434274152448
grid=/home/deck/.local/share/Steam/userdata/11218204/config/grid
deck_ssh='ssh -i C:\Users\kev\AppData\Local\steamos-devkit\steamos-devkit\devkit_rsa -o BatchMode=yes -o StrictHostKeyChecking=no -o UserKnownHostsFile=NUL deck@192.168.178.127'

"$root/tools/generate-steam-library-art.sh"

staging="$(mktemp -d)"
trap 'rm -rf "$staging"' EXIT
cp "$art/library-capsule.png" "$staging/${appid}p.png"
cp "$art/library-header.png" "$staging/$appid.png"
cp "$art/library-header.png" "$staging/$gameid.png"
cp "$art/library-hero.png" "$staging/${appid}_hero.png"
cp "$art/library-logo.png" "$staging/${appid}_logo.png"

tar -C "$staging" -cf - . |
  ssh bro "$deck_ssh \"mkdir -p $grid && tar -xf - -C $grid\""

ssh bro "$deck_ssh \"sha256sum $grid/${appid}p.png $grid/$appid.png $grid/$gameid.png $grid/${appid}_hero.png $grid/${appid}_logo.png\""

echo "Installed Steam library art for Myth on the Steam Deck"
