#!/usr/bin/env bash
# Installs the Steam library art (Production/SteamMedia/Library) over the devkit shortcut on a
# Steam Deck, so the library shows key art instead of a blank capsule. Also reachable as
# tools/deploy-deck.sh --art.
#
# Usage: tools/install-steam-deck-art.sh      DECK=deck@host overrides the target.
#
# The ids below are Steam's own non-Steam-shortcut ids, derived from the shortcut's name and
# exe path. Rename deploy-deck.sh's gameid and Steam mints a different pair, which leaves this
# art keyed to a shortcut that no longer exists -- read the new ones out of the Deck's
# config/shortcuts.vdf and update STEAM_APPID.
set -euo pipefail

root="$(cd "$(dirname "$0")/.." && pwd)"
art="$root/Production/SteamMedia/Library"
deck="${DECK:-deck@steamdeck.local}"
appid="${STEAM_APPID:-3374069405}"  # "Devkit Game: HumanQuestions"; re-read shortcuts.vdf after a rename
# Steam's 64-bit grid id for a shortcut is just its 32-bit appid in the high word, so the
# appid is the only value a rename actually has to be re-derived for.
shortcutid=$(printf '%llu' $(( (appid << 32) | 0x02000000 )))  # bash arithmetic is signed
grid=".local/share/Steam/userdata/${STEAM_USER_ID:-11218204}/config/grid"

"$root/tools/generate-steam-library-art.sh"

staging="$(mktemp -d)"
trap 'rm -rf "$staging"' EXIT
cp "$art/library-capsule.jpg" "$staging/${appid}p.jpg"
cp "$art/library-header.jpg" "$staging/$appid.jpg"
cp "$art/library-header.jpg" "$staging/$shortcutid.jpg"
cp "$art/library-hero.jpg" "$staging/${appid}_hero.jpg"
cp "$art/library-logo.png" "$staging/${appid}_logo.png"

# COPYFILE_DISABLE keeps macOS tar from shipping ._ AppleDouble twins of every file, which
# an earlier run left littered through the Deck's grid folder.
COPYFILE_DISABLE=1 tar --no-xattrs -C "$staging" -cf - . | ssh "$deck" "mkdir -p ~/$grid && tar -xf - -C ~/$grid"

# Steam takes whichever extension it finds first, so stale PNGs from an earlier install would
# win over the JPEGs written above.
ssh "$deck" "cd ~/$grid && rm -f ${appid}p.png $appid.png $shortcutid.png ${appid}_hero.png"

echo "Installed Steam library art on $deck"
