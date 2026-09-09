#!/usr/bin/env bash
# Pushes the Linux build (tools/publish-linux.mjs) to a Steam Deck over ssh and registers
# it as a devkit title — what the SteamOS Devkit Client does, minus the Windows machine.
#
# Usage: tools/deploy-deck.sh [--run] [--art]    DECK=deck@host overrides the target.
#   --run  launch the build in the Deck's running gamescope session
#   --art  also push the Steam library art (tools/install-steam-deck-art.sh)
set -euo pipefail

run=0
art=0
for arg in "$@"; do
  case "$arg" in
    --run) run=1 ;;
    --art) art=1 ;;
    *) echo "unknown option: $arg (expected --run or --art)" >&2; exit 1 ;;
  esac
done

root="$(cd "$(dirname "$0")/.." && pwd)"
game="$root/bin/publish/Myth-linux"
deck="${DECK:-deck@steamdeck.local}"
gameid=HumanQuestions  # shows in Steam as "Devkit Game: $gameid"; steam-client-create-shortcut
                       # drops it into a URL unescaped, so no hyphens and no spaces
remote="devkit-game/$gameid"

[ -x "$game/Myth" ] || { echo "No Linux build in $game — run: node tools/publish-linux.mjs" >&2; exit 1; }

echo rsync -az --delete "$game/" "$deck:$remote/"
rsync -az --delete "$game/" "$deck:$remote/"

# The launcher reads the two json files; a game folder without them is ignored. steam_play 0
# runs the ELF directly rather than handing it to Proton. resolve registers every devkit
# folder Steam doesn't know about yet and drops stale ones — it is what the devkit client
# itself calls. The utils were uploaded with CRLF endings, which breaks their shebangs, so
# invoke python3 explicitly.
ssh "$deck" bash -s <<EOF
set -e
printf '%s' '["Myth"]' > "\$HOME/$remote-argv.json"
printf '%s' '{"steam_play": "0"}' > "\$HOME/$remote-settings.json"
cd "\$HOME/devkit-utils" && python3 -m devkit_utils.resolve
EOF

echo "Deployed $gameid to $deck"

# Opt-in: the art only changes when the source images do, and generating it needs ImageMagick.
if [ "$art" = 1 ]; then
  DECK="$deck" "$root/tools/install-steam-deck-art.sh"
fi

# Launch into the running gamescope session, to check a build without picking the Deck up.
if [ "$run" = 1 ]; then
  ssh "$deck" "cd '$remote' && XDG_RUNTIME_DIR=/run/user/1000 WAYLAND_DISPLAY=gamescope-0 SDL_VIDEODRIVER=wayland ./Myth"
fi
