#!/usr/bin/env bash
set -euo pipefail

script_dir="$(cd "$(dirname "$0")" && pwd)"
myth_dir="$(cd "$script_dir/../.." && pwd)"
source_glb="$myth_dir/tools/Universal Animation Library 2[Standard]/Unreal-Godot/UAL2_Standard.glb"
authored_blend="$myth_dir/Assets/Sources/Models/UAL2_Standard.blend"
authored_actions=(Eye_Laser_Attack)
performance_glb="$script_dir/Pose2Sim_Performance_Rokoko_MatchedPose.glb"
mixamo_dir="$script_dir/mixamo"
build_dir="$(mktemp -d "${TMPDIR:-/tmp}/myth-rig-baker.XXXXXX")"
trap 'rm -rf "$build_dir"' EXIT

authored_glb="$build_dir/UAL2_Authored.glb"
authored_action_args=()
for action in "${authored_actions[@]}"; do
	authored_action_args+=(--action "$action")
done
blender --factory-startup --background "$authored_blend" --python "$script_dir/export-blend.py" -- \
	--output "$authored_glb" "${authored_action_args[@]}" --action-prefix Pose_

cmake -S "$script_dir" -B "$build_dir/build" -G Ninja -DCMAKE_BUILD_TYPE=Release
cmake --build "$build_dir/build" --target gltf2ozz myth_rig_pack

mkdir -p "$build_dir/ozz/ual2" "$myth_dir/Assets/Rigs"
(
	cd "$build_dir/ozz/ual2"
	"$build_dir/build/ozz/src/animation/offline/gltf/gltf2ozz" \
		--file="$source_glb" --config_file="$script_dir/ual2.json" --endian=little
)
mkdir -p "$build_dir/ozz/authored"
(
	cd "$build_dir/ozz/authored"
	"$build_dir/build/ozz/src/animation/offline/gltf/gltf2ozz" \
		--file="$authored_glb" --config_file="$script_dir/ual2-animation.json" --endian=little
)
if [[ -f "$performance_glb" ]]; then
	mkdir -p "$build_dir/ozz/pose2sim"
	(
		cd "$build_dir/ozz/pose2sim"
		"$build_dir/build/ozz/src/animation/offline/gltf/gltf2ozz" \
			--file="$performance_glb" --config_file="$script_dir/ual2-animation.json" --endian=little
	)
fi
if [[ -d "$mixamo_dir" ]]; then
	while IFS= read -r -d '' mixamo_glb; do
		name="$(basename "$mixamo_glb" .glb)"
		mkdir -p "$build_dir/ozz/$name"
		(
			cd "$build_dir/ozz/$name"
			"$build_dir/build/ozz/src/animation/offline/gltf/gltf2ozz" \
				--file="$mixamo_glb" --config_file="$script_dir/ual2-animation.json" --endian=little
		)
	done < <(find "$mixamo_dir" -name '*.glb' -print0 | sort -z)
fi

animations=()
while IFS= read -r -d '' animation; do
	animations+=("$animation")
done < <(find "$build_dir/ozz" -name '*.ozz' ! -name skeleton.ozz -print0 | sort -z)

pack="$build_dir/ual2.ozzpack"
"$build_dir/build/myth_rig_pack" "$pack" \
	"$build_dir/ozz/ual2/skeleton.ozz" "${animations[@]}"
mv "$pack" "$myth_dir/Assets/Rigs/ual2.ozzpack"
