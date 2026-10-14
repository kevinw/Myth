#!/usr/bin/env bash
# Opens the UAL2 rig in Blender to pose Pose_<Name>; saving bakes it into Assets/Rigs/ual2.ozzpack.
#   pose.sh WallSlide [--from Idle_Rail_Loop] [--wall 0.33]
set -euo pipefail

if (( $# == 0 )); then
	echo "usage: $0 Name [--from Action] [--wall meters]" >&2
	exit 2
fi
script_dir="$(cd "$(dirname "$0")" && pwd)"
myth_dir="$(cd "$script_dir/../.." && pwd)"
blender="/Applications/Blender.app/Contents/MacOS/Blender"
[[ -x "$blender" ]] || blender=blender
name="$1"
shift
exec "$blender" "$myth_dir/Assets/Sources/Models/UAL2_Standard.blend" --python "$script_dir/edit-pose.py" -- \
	--action "Pose_$name" "$@"
