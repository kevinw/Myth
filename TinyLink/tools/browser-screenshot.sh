#!/usr/bin/env bash
set -euo pipefail

script_dir=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" &> /dev/null && pwd)
repo_dir=$(cd -- "$script_dir/.." &> /dev/null && pwd)

host="127.0.0.1"
port="8083"
configuration="Debug"
output="$repo_dir/artifacts/browser-smoke/tinylink-web.png"
settle_ms="8000"
reference=""
diff_output="$repo_dir/artifacts/browser-smoke/tinylink-web-diff.png"
run_browser_args=()

usage() {
	cat <<'USAGE'
Usage: tools/browser-screenshot.sh [options] [-- run-browser-args...]

Builds TinyLink for browser-wasm, serves it locally, clicks the browser start button,
and saves a verification screenshot.

Options:
  --host HOST          Host for the dev server. Default: 127.0.0.1
  --port PORT          Port for the dev server. Default: 8083
  -c, --configuration  Build configuration. Default: Debug
  -o, --output PATH    Screenshot output path. Default: artifacts/browser-smoke/tinylink-web.png
  --reference PATH     Compare the screenshot against a reference PNG.
  --diff-output PATH   Visual diff output path. Default: artifacts/browser-smoke/tinylink-web-diff.png
  --settle-ms MS       Delay after the start gate closes before capture. Default: 8000
  -h, --help           Show this help.
USAGE
}

while [[ $# -gt 0 ]]; do
	case "$1" in
		--host)
			host="$2"
			shift 2
			;;
		--port)
			port="$2"
			shift 2
			;;
		-c|--configuration)
			configuration="$2"
			shift 2
			;;
		-o|--output)
			output="$2"
			shift 2
			;;
		--reference)
			reference="$2"
			shift 2
			;;
		--diff-output)
			diff_output="$2"
			shift 2
			;;
		--settle-ms)
			settle_ms="$2"
			shift 2
			;;
		-h|--help)
			usage
			exit 0
			;;
		--)
			shift
			run_browser_args+=("$@")
			break
			;;
		*)
			run_browser_args+=("$1")
			shift
			;;
	esac
done

cd "$repo_dir"

log_dir="$repo_dir/artifacts/browser-smoke"
mkdir -p "$log_dir"
log_file="$log_dir/run-browser.log"
url="http://$host:$port/"

if [[ ${#run_browser_args[@]} -gt 0 ]]; then
	"$script_dir/run-browser.sh" --host "$host" --port "$port" -c "$configuration" --no-open "${run_browser_args[@]}" > "$log_file" 2>&1 &
else
	"$script_dir/run-browser.sh" --host "$host" --port "$port" -c "$configuration" --no-open > "$log_file" 2>&1 &
fi
server_pid=$!

cleanup() {
	kill "$server_pid" > /dev/null 2>&1 || true
	wait "$server_pid" > /dev/null 2>&1 || true
}
trap cleanup EXIT INT TERM

python3 - "$url" "$server_pid" "$log_file" <<'PY'
import os
import sys
import time
import urllib.request

url = sys.argv[1]
server_pid = int(sys.argv[2])
log_file = sys.argv[3]
deadline = time.time() + 180.0

while time.time() < deadline:
    try:
        with urllib.request.urlopen(url, timeout=0.5) as response:
            if response.status < 500:
                raise SystemExit(0)
    except Exception:
        pass

    try:
        os.kill(server_pid, 0)
    except OSError:
        sys.stderr.write("Browser server exited before becoming ready.\n")
        try:
            with open(log_file, "r", encoding="utf-8", errors="replace") as handle:
                lines = handle.readlines()[-80:]
            sys.stderr.write("".join(lines))
        except OSError:
            pass
        raise SystemExit(1)

    time.sleep(0.25)

raise SystemExit(f"Timed out waiting for {url}")
PY

"$script_dir/browser-smoke.sh" "$url" --screenshot "$output" --settle-ms "$settle_ms"

if [[ -n "$reference" ]]; then
	"$script_dir/image-diff.py" "$reference" "$output" --diff "$diff_output"
fi
