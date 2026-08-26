#!/usr/bin/env bash
# Pushes the Linux build (tools/publish-linux.mjs) to a Steam Deck over ssh and registers
# it as a devkit title — what the SteamOS Devkit Client does, minus the Windows machine.
# Re-running is cheap: rsync moves only what changed and registration is idempotent.
#
# Usage: tools/deploy-deck.sh [--run]        DECK=deck@host overrides the target.
set -euo pipefail

root="$(cd "$(dirname "$0")/.." && pwd)"
game="$root/bin/publish/Myth-linux"
deck="${DECK:-deck@steamdeck.local}"
gameid=MythLinux  # no hyphens: the Steam client rejects them in a devkit gameid
remote="/home/deck/devkit-game/$gameid"

[ -x "$game/Myth" ] || { echo "No Linux build in $game — run: node tools/publish-linux.mjs" >&2; exit 1; }

rsync -az --delete "$game/" "$deck:$remote/"

# The devkit launcher reads these two; a game folder without them is ignored. steam_play 0
# runs the ELF directly rather than handing it to Proton.
ssh "$deck" "cat > '$remote-argv.json'" <<< '["Myth"]'
ssh "$deck" "cat > '$remote-settings.json'" <<< '{"steam_play": "0"}'

# Registers every devkit folder Steam doesn't know about yet, and drops stale ones. The
# standalone steam-client-create-shortcut sends an older form of the command that Steam now
# rejects; this is the path the devkit client itself uses. The utils were uploaded with CRLF
# endings, which breaks their shebangs, so invoke python3 explicitly.
ssh "$deck" "cd ~/devkit-utils && python3 -m devkit_utils.resolve"

echo "Deployed $gameid to $deck"

# Launch into the running gamescope session, so a build can be checked without picking the
# Deck up. Ctrl-C stops it.
if [ "${1:-}" = "--run" ]; then
  ssh "$deck" "cd '$remote' && XDG_RUNTIME_DIR=/run/user/1000 WAYLAND_DISPLAY=gamescope-0 SDL_VIDEODRIVER=wayland ./Myth"
fi
