#!/usr/bin/env bash
set -euo pipefail

script_dir=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" &> /dev/null && pwd)
repo_dir=$(cd -- "$script_dir/.." &> /dev/null && pwd)

configuration="Release"
profile=""
profiles_dir="$repo_dir/Profiles"
convert_speedscope=1
write_summary=1
build_first=1
game_args=()

usage() {
	cat <<'EOF'
Usage: tools/profile-game.sh [options] [-- game args...]

Runs Myth under dotnet-trace until the game exits, then writes trace artifacts.

Options:
  -c, --configuration <Debug|Release>  Build/run configuration. Default: Release
  -p, --profile <name>                Optional dotnet-trace profile. Default: tool default
  -o, --output-dir <dir>              Output root. Default: Profiles
      --no-build                      Skip dotnet build before tracing
      --no-convert                    Do not convert .nettrace to Speedscope JSON
      --no-summary                    Do not write summary.txt
  -h, --help                          Show this help

Artifacts:
  Profiles/<timestamp>/
    myth.nettrace
    myth.speedscope.json
    summary.txt

If dotnet-trace is missing:
  dotnet tool install --global dotnet-trace
  # then ensure ~/.dotnet/tools is on PATH
EOF
}

while [[ $# -gt 0 ]]; do
	case "$1" in
		-c|--configuration)
			configuration="${2:-}"
			shift 2
			;;
		-p|--profile)
			profile="${2:-}"
			shift 2
			;;
		-o|--output-dir)
			profiles_dir="${2:-}"
			shift 2
			;;
		--no-build)
			build_first=0
			shift
			;;
		--no-convert)
			convert_speedscope=0
			shift
			;;
		--no-summary)
			write_summary=0
			shift
			;;
		-h|--help)
			usage
			exit 0
			;;
		--)
			shift
			game_args=("$@")
			break
			;;
		*)
			echo "Unknown option: $1" >&2
			usage >&2
			exit 2
			;;
	esac
done

if ! command -v dotnet >/dev/null 2>&1; then
	echo "dotnet is not on PATH." >&2
	exit 1
fi

if ! command -v dotnet-trace >/dev/null 2>&1; then
	cat >&2 <<'EOF'
dotnet-trace is not installed.

Install it with:
  dotnet tool install --global dotnet-trace

If the install succeeds but this script still cannot find it, add this to your shell profile:
  export PATH="$PATH:$HOME/.dotnet/tools"
EOF
	exit 1
fi

timestamp=$(date +"%Y%m%d-%H%M%S")
run_dir="$profiles_dir/$timestamp"
trace_path="$run_dir/myth.nettrace"
summary_path="$run_dir/summary.txt"
speedscope_path="$run_dir/myth.speedscope.json"
project_path="$repo_dir/Myth.csproj"
target_framework=$(sed -n 's:.*<TargetFramework>\(.*\)</TargetFramework>.*:\1:p' "$project_path" | head -1)
target_dll="$repo_dir/bin/$configuration/$target_framework/Myth.dll"
game_args_display="${game_args[*]-}"

mkdir -p "$run_dir"

if [[ "$build_first" == "1" ]]; then
	echo "Building Myth ($configuration) ..."
	dotnet build "$project_path" -c "$configuration"
fi

if [[ ! -f "$target_dll" ]]; then
	echo "Expected build output not found: $target_dll" >&2
	echo "Run without --no-build, or check the project output path." >&2
	exit 1
fi

{
	echo "Myth profile run"
	echo "Started: $(date)"
	echo "Repository: $repo_dir"
	echo "Configuration: $configuration"
	echo "Profile: ${profile:-dotnet-trace default}"
	echo "Command: dotnet $target_dll $game_args_display"
	echo
	if command -v git >/dev/null 2>&1 && git -C "$repo_dir" rev-parse --is-inside-work-tree >/dev/null 2>&1; then
		echo "Git commit: $(git -C "$repo_dir" rev-parse --short HEAD)"
		echo "Git status:"
		git -C "$repo_dir" status --short
	fi
} > "$summary_path"

echo "Tracing until the game exits ..."
echo "Trace: $trace_path"
trace_command=(dotnet-trace collect --output "$trace_path")
if [[ -n "$profile" ]]; then
	trace_command+=(--profile "$profile")
fi
trace_command+=(-- dotnet "$target_dll")
if [[ ${#game_args[@]} -gt 0 ]]; then
	trace_command+=("${game_args[@]}")
fi
"${trace_command[@]}"

if [[ "$convert_speedscope" == "1" ]]; then
	echo "Converting to Speedscope JSON ..."
	dotnet-trace convert "$trace_path" --format speedscope

	converted_path="${trace_path%.nettrace}.speedscope.json"
	if [[ -f "$converted_path" && "$converted_path" != "$speedscope_path" ]]; then
		mv "$converted_path" "$speedscope_path"
	elif [[ ! -f "$speedscope_path" ]]; then
		found=$(find "$run_dir" -maxdepth 1 -name '*.speedscope.json' -print -quit)
		if [[ -n "$found" && "$found" != "$speedscope_path" ]]; then
			mv "$found" "$speedscope_path"
		fi
	fi
fi

if [[ "$write_summary" == "1" ]]; then
	{
		echo
		echo "Finished: $(date)"
		echo "Artifacts:"
		echo "  nettrace: $trace_path"
		if [[ -f "$speedscope_path" ]]; then
			echo "  speedscope: $speedscope_path"
		fi
		echo
	} >> "$summary_path"

	if [[ -f "$speedscope_path" && -x "$(command -v python3 || true)" ]]; then
		python3 - "$speedscope_path" >> "$summary_path" <<'PY'
import json
import sys
from collections import Counter

path = sys.argv[1]
with open(path, "r", encoding="utf-8") as f:
    data = json.load(f)

frames = data.get("shared", {}).get("frames", [])
frame_names = [
    frame.get("name") or frame.get("file") or f"<frame {i}>"
    for i, frame in enumerate(frames)
]

leaf = Counter()
stacks = Counter()
total = 0.0

def clean(name):
    for prefix in ("Myth.", "Foster.Framework."):
        if name.startswith(prefix):
            return name[len(prefix):]
    return name

for profile in data.get("profiles", []):
    samples = profile.get("samples")
    weights = profile.get("weights")
    if samples:
        if not weights:
            weights = [1] * len(samples)
        for sample, weight in zip(samples, weights):
            if not sample:
                continue
            names = [clean(frame_names[i]) for i in sample if 0 <= i < len(frame_names)]
            if not names:
                continue
            w = float(weight)
            total += w
            leaf[names[-1]] += w
            stacks[";".join(names)] += w
        continue

    # Basic support for evented Speedscope profiles. Count closed stack samples.
    open_stack = []
    for event in profile.get("events", []):
        event_type = event.get("type")
        frame_index = event.get("frame")
        if event_type == "O" and frame_index is not None:
            if 0 <= frame_index < len(frame_names):
                open_stack.append(clean(frame_names[frame_index]))
        elif event_type == "C":
            if open_stack:
                names = list(open_stack)
                total += 1.0
                leaf[names[-1]] += 1.0
                stacks[";".join(names)] += 1.0
                open_stack.pop()

if total <= 0:
    print("No samples found in Speedscope JSON.")
    sys.exit(0)

def print_counter(title, counter, limit):
    print(title)
    for name, count in counter.most_common(limit):
        pct = count * 100.0 / total
        print(f"  {pct:6.2f}%  {name}")
    print()

print_counter("Top leaf frames:", leaf, 30)
print_counter("Top folded stacks:", stacks, 40)
PY
	elif [[ -f "$speedscope_path" ]]; then
		echo "python3 not found; skipping automatic Speedscope summary." >> "$summary_path"
	fi
fi

echo
echo "Profile complete."
echo "Summary: $summary_path"
echo "Trace:   $trace_path"
if [[ -f "$speedscope_path" ]]; then
	echo "Open:    $speedscope_path"
	echo "         https://www.speedscope.app"
fi
