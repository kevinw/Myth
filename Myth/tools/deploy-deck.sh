#!/usr/bin/env bash
# Pushes the Linux build (tools/publish-linux.mjs) to a Steam Deck over ssh and registers
# it as a devkit title — what the SteamOS Devkit Client does, minus the Windows machine.
#
# Usage: tools/deploy-deck.sh [--run]        DECK=deck@host overrides the target.
set -euo pipefail

root="$(cd "$(dirname "$0")/.." && pwd)"
game="$root/bin/publish/Myth-linux"
deck="${DECK:-deck@steamdeck.local}"
gameid=MythLinux  # no hyphens: the Steam client rejects them in a devkit gameid
remote="devkit-game/$gameid"

[ -x "$game/Myth" ] || { echo "No Linux build in $game — run: node tools/publish-linux.mjs" >&2; exit 1; }

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

# Launch into the running gamescope session, to check a build without picking the Deck up.
if [ "${1:-}" = "--run" ]; then
  ssh "$deck" "cd '$remote' && XDG_RUNTIME_DIR=/run/user/1000 WAYLAND_DISPLAY=gamescope-0 SDL_VIDEODRIVER=wayland ./Myth"
fi
