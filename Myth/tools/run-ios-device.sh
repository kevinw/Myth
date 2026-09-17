#!/usr/bin/env bash
set -euo pipefail

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
repo_root="$(cd "$script_dir/.." && pwd)"

configuration="Debug"
runtime_id="ios-arm64"
project_path="$repo_root/Platforms/iOS/MythIos/MythIos.csproj"
bundle_id="com.kevinwatters.myth"
device_id="${MYTH_IOS_DEVICE:-}"
no_build=0
console=0
start_stopped=0
app_args=()

usage() {
	cat <<'EOF'
Usage: tools/run-ios-device.sh [options] [-- app-args...]

Builds Myth for a connected physical iPhone, installs it, and launches it.

Options:
  -d, --device <id|name>       Device identifier or name. Defaults to the only connected physical iOS device.
  -c, --configuration <name>   Build configuration. Default: Debug.
      --project <path>         iOS csproj path.
      --bundle-id <id>         App bundle identifier. Default: com.kevinwatters.myth.
      --no-build               Skip dotnet build and use the existing .app.
      --console                Attach devicectl console and wait for the app to exit.
      --start-stopped          Launch suspended so a debugger can attach.
  -h, --help                   Show this help.

Environment:
  MYTH_IOS_DEVICE          Optional default device id or name.
EOF
}

while (($# > 0)); do
	case "$1" in
		-d|--device)
			device_id="${2:?Missing value for $1}"
			shift 2
			;;
		-c|--configuration)
			configuration="${2:?Missing value for $1}"
			shift 2
			;;
		--project)
			project_path="${2:?Missing value for $1}"
			shift 2
			;;
		--bundle-id)
			bundle_id="${2:?Missing value for $1}"
			shift 2
			;;
		--no-build)
			no_build=1
			shift
			;;
		--console)
			console=1
			shift
			;;
		--start-stopped)
			start_stopped=1
			shift
			;;
		-h|--help)
			usage
			exit 0
			;;
		--)
			shift
			app_args=("$@")
			break
			;;
		*)
			echo "Unknown option: $1" >&2
			usage >&2
			exit 2
			;;
	esac
done

if [[ "$project_path" != /* ]]; then
	project_path="$repo_root/$project_path"
fi

project_dir="$(cd "$(dirname "$project_path")" && pwd)"
project_name="$(basename "$project_path" .csproj)"
app_path="$project_dir/bin/$configuration/net10.0-ios/$runtime_id/$project_name.app"

require_tool() {
	if ! command -v "$1" >/dev/null 2>&1; then
		echo "Missing required tool: $1" >&2
		exit 1
	fi
}

require_tool dotnet
require_tool xcrun
require_tool /usr/bin/python3

now_ms() {
	/usr/bin/python3 - <<'PY'
import time
print(int(time.time() * 1000))
PY
}

format_elapsed() {
	/usr/bin/python3 - "$1" <<'PY'
import sys
ms = int(sys.argv[1])
print(f"{ms / 1000:.2f}s")
PY
}

print_elapsed() {
	local label="$1"
	local start_ms="$2"
	local end_ms
	end_ms="$(now_ms)"
	echo "$label completed in $(format_elapsed "$((end_ms - start_ms))")."
}

select_device() {
	local json_path
	json_path="$(mktemp)"
	trap 'rm -f "$json_path"' RETURN

	xcrun devicectl list devices --json-output "$json_path" --timeout 10 >/dev/null
	/usr/bin/python3 - "$json_path" <<'PY'
import json
import sys

path = sys.argv[1]
with open(path, "r", encoding="utf-8") as f:
	data = json.load(f)

devices = []
for device in data.get("result", {}).get("devices", []):
	hardware = device.get("hardwareProperties", {})
	props = device.get("deviceProperties", {})
	connection = device.get("connectionProperties", {})
	if hardware.get("platform") != "iOS":
		continue
	if hardware.get("reality") != "physical":
		continue
	if connection.get("pairingState") != "paired":
		continue
	if connection.get("tunnelState") == "unavailable":
		continue
	devices.append((device.get("identifier", ""), props.get("name", "Unknown iPhone")))

if len(devices) == 1:
	print(devices[0][0])
	sys.exit(0)

if not devices:
	print("No connected paired physical iOS devices found.", file=sys.stderr)
else:
	print("Multiple connected physical iOS devices found; pass --device:", file=sys.stderr)
	for identifier, name in devices:
		print(f"  {identifier}  {name}", file=sys.stderr)
sys.exit(1)
PY
}

script_start_ms="$(now_ms)"

if [[ -z "$device_id" ]]; then
	select_start_ms="$(now_ms)"
	device_id="$(select_device)"
	print_elapsed "Device selection" "$select_start_ms"
fi

if (( no_build == 0 )); then
	echo "Staging iOS content ..."
	assets_start_ms="$(now_ms)"
	(cd "$repo_root" && dotnet run --project Myth.csproj -- --stage-content ios --world Myth)
	print_elapsed "Assets" "$assets_start_ms"

	echo "Building Myth for $runtime_id ($configuration) ..."
	build_start_ms="$(now_ms)"
	dotnet build "$project_path" -c "$configuration" -f net10.0-ios -r "$runtime_id" /p:ArchiveOnBuild=false
	print_elapsed "Build" "$build_start_ms"
fi

if [[ ! -d "$app_path" ]]; then
	echo "Could not find built app: $app_path" >&2
	echo "Run without --no-build, or check --configuration/--project." >&2
	exit 1
fi

echo "Installing on device: $device_id"
install_start_ms="$(now_ms)"
xcrun devicectl device install app --device "$device_id" "$app_path"
print_elapsed "Install" "$install_start_ms"

launch_args=(xcrun devicectl device process launch --device "$device_id" --terminate-existing)
if (( start_stopped != 0 )); then
	launch_args+=(--start-stopped)
fi
if (( console != 0 )); then
	launch_args+=(--console)
fi
launch_args+=("$bundle_id")
if ((${#app_args[@]} > 0)); then
	launch_args+=("${app_args[@]}")
fi

echo "Launching $bundle_id ..."
launch_start_ms="$(now_ms)"
"${launch_args[@]}"
print_elapsed "Launch" "$launch_start_ms"
print_elapsed "Total" "$script_start_ms"
