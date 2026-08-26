#!/usr/bin/env bash
set -euo pipefail

script_dir="$(cd "$(dirname "$0")" && pwd)"
target="$script_dir/../Universal Animation Library 2[Standard]/Unreal-Godot/UAL2_Standard.glb"

if (( $# == 0 )); then
	echo "usage: $0 animation.fbx [animation.fbx ...]" >&2
	exit 2
fi

blender --factory-startup --background --python "$script_dir/retarget-mixamo.py" -- \
	--target "$target" --output-dir "$script_dir/mixamo" "$@"
