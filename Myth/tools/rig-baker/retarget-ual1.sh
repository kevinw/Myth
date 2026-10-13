#!/usr/bin/env bash
set -euo pipefail

# Retargets the locomotion clips Myth uses from Quaternius UAL1 (a Rigify skeleton) onto UAL2.
script_dir="$(cd "$(dirname "$0")" && pwd)"
target="$script_dir/../Universal Animation Library 2[Standard]/Unreal-Godot/UAL2_Standard.glb"
source_fbx="$script_dir/../ozz-animation/media/fbx/quaternius/ual.fbx"
actions=(Idle_Loop Walk_Loop Jog_Fwd_Loop Sprint_Loop Jump_Start Jump_Loop Jump_Land Roll Crouch_Idle_Loop Crouch_Fwd_Loop)

action_args=()
for action in "${actions[@]}"; do
	action_args+=(--action "$action")
done
blender --factory-startup --background --python "$script_dir/retarget-mixamo.py" -- \
	--target "$target" --output-dir "$script_dir/ual1" \
	--profiles "$script_dir/retarget-mixamo.json" "${action_args[@]}" "$source_fbx"
