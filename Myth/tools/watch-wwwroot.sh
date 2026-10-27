#!/usr/bin/env bash
#
# Fast edit/refresh loop for the browser build's static UI files (index.html,
# main.js, virtual-gamepad.js, ...). They aren't compiled, so a full dotnet
# rebuild is unnecessary — this just copies edits from wwwroot/ into the served
# AppBundle on save. Run it alongside `node tools/run-browser.mjs`, then edit a
# wwwroot file and refresh the browser (or pull-to-refresh on a phone).
#
# Usage: Myth/tools/watch-wwwroot.sh [Debug|Release]
#
set -euo pipefail

config="${1:-Debug}"
script_dir=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" &> /dev/null && pwd)
myth_dir=$(cd -- "$script_dir/.." &> /dev/null && pwd)
src="$myth_dir/Platforms/Web/wwwroot"
bundle="$myth_dir/bin/$config/browser-wasm/AppBundle"

if ! command -v fswatch > /dev/null 2>&1; then
	echo "fswatch not found. Install it: brew install fswatch" >&2
	exit 1
fi
if [[ ! -d "$bundle" ]]; then
	echo "AppBundle not found: $bundle" >&2
	echo "Build/run the browser app first (e.g. node tools/run-browser.mjs)." >&2
	exit 1
fi

sync_file() {
	local f="$1"
	[[ -f "$f" ]] || return 0
	local name
	name="$(basename -- "$f")"
	# The dev server serves the AppBundle root, which is where the shell files live.
	cp "$f" "$bundle/$name"
	echo "↻ $name"
}

# Copy current state once, then watch.
for f in "$src"/*; do sync_file "$f"; done
echo "Watching $src → $bundle  (Ctrl-C to stop)"

fswatch -0 "$src" | while IFS= read -r -d '' f; do
	sync_file "$f"
done
