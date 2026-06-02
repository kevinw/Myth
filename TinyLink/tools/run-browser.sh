#!/usr/bin/env bash
set -euo pipefail

script_dir=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" &> /dev/null && pwd)
repo_dir=$(cd -- "$script_dir/.." &> /dev/null && pwd)
foster_dir=$(cd -- "$repo_dir/../../Foster/Framework" &> /dev/null && pwd)

host="127.0.0.1"
port="8082"
configuration="Debug"
open_browser=1
compile_shaders=1
publish=0
dotnet_args=()

usage() {
	cat <<'USAGE'
Usage: tools/run-browser.sh [options] [-- dotnet-args...]

Builds or publishes TinyLink for browser-wasm, starts the local dev server, and opens the browser.

Options:
  --host HOST          Host for the dev server. Default: 127.0.0.1
  --port PORT          Port for the dev server. Default: 8082
  -c, --configuration  Build configuration. Default: Debug
  --publish            Run dotnet publish instead of dotnet build.
  --skip-shaders       Do not regenerate Foster WGSL shaders before building.
  --no-open            Do not open the browser automatically.
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
		--publish)
			publish=1
			shift
			;;
		--skip-shaders)
			compile_shaders=0
			shift
			;;
		--no-open)
			open_browser=0
			shift
			;;
		-h|--help)
			usage
			exit 0
			;;
		--)
			shift
			dotnet_args+=("$@")
			break
			;;
		*)
			dotnet_args+=("$1")
			shift
			;;
	esac
done

cd "$repo_dir"

if [[ "$compile_shaders" == "1" ]]; then
	"$foster_dir/Content/Shaders/compile.sh" web
fi

if [[ "$publish" == "1" ]]; then
	dotnet publish TinyLink.csproj -c "$configuration" -r browser-wasm "${dotnet_args[@]}"
else
	dotnet build TinyLink.csproj -c "$configuration" -r browser-wasm "${dotnet_args[@]}"
fi

app_bundle="$repo_dir/bin/$configuration/net10.0/browser-wasm/AppBundle"
if [[ ! -d "$app_bundle" ]]; then
	echo "Browser app bundle not found: $app_bundle" >&2
	exit 1
fi

url="http://$host:$port/"

port_in_use() {
	python3 - "$host" "$port" <<'PY'
import socket
import sys

host = sys.argv[1]
port = int(sys.argv[2])
sock = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
try:
    sock.bind((host, port))
except OSError:
    sys.exit(0)
finally:
    sock.close()
sys.exit(1)
PY
}

open_url() {
	if [[ "$open_browser" != "1" ]]; then
		return
	fi

	if [[ "$(uname -s)" == "Darwin" ]]; then
		open "$url"
	elif command -v xdg-open > /dev/null 2>&1; then
		xdg-open "$url" > /dev/null 2>&1 &
	else
		echo "Open $url in your browser."
	fi
}

if port_in_use; then
	echo "Port $host:$port is already in use; assuming a dev server is running."
	open_url
	echo "$url"
	exit 0
fi

python3 serve-browser.py --host "$host" --port "$port" --root "$app_bundle" &
server_pid=$!

cleanup() {
	kill "$server_pid" > /dev/null 2>&1 || true
}
trap cleanup EXIT INT TERM

python3 - "$host" "$port" <<'PY'
import socket
import sys
import time

host = sys.argv[1]
port = int(sys.argv[2])
deadline = time.time() + 5.0
while time.time() < deadline:
    try:
        with socket.create_connection((host, port), timeout=0.25):
            sys.exit(0)
    except OSError:
        time.sleep(0.05)
raise SystemExit(f"Server did not start on {host}:{port}")
PY

open_url
echo "$url"
wait "$server_pid"
