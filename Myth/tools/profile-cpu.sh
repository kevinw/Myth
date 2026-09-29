#!/bin/bash -xe

cd "$(dirname "$0")/.."
opener="${XDG_CACHE_HOME:-$HOME/.cache}/perfetto/open_trace_in_ui"
[ -f "$opener" ] || curl -fsSL --create-dirs -o "$opener" https://github.com/google/perfetto/raw/main/tools/open_trace_in_ui
trace="Myth_$(date +%Y%m%d_%H%M%S)"

dotnet build -c Debug
dotnet trace collect --format Chromium -o "$trace.nettrace" -- bin/Debug/net10.0/Myth
# Safari won't let https://ui.perfetto.dev fetch the trace from http://127.0.0.1; Chrome will.
BROWSER="open -a 'Google Chrome' %s" python3 "$opener" "$trace.chromium.json"
