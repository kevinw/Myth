#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
ASEPRITE="${ASEPRITE:-/Applications/Aseprite.app/Contents/MacOS/aseprite}"

if [[ ! -x "$ASEPRITE" ]]; then
	echo "Aseprite executable not found: $ASEPRITE" >&2
	echo "Set ASEPRITE=/path/to/aseprite and retry." >&2
	exit 1
fi

"$ASEPRITE" -b \
	--script "$ROOT/tools/generate-myth-bg-tilesets.lua" \
	--script-param "out-dir=$ROOT/Assets/Tilesets"
