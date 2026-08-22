#!/usr/bin/env bash
set -euo pipefail

script_dir="$(cd "$(dirname "$0")" && pwd)"
myth_dir="$(cd "$script_dir/../.." && pwd)"
source_glb="$myth_dir/tools/Universal Animation Library 2[Standard]/Unreal-Godot/UAL2_Standard.glb"
performance_glb="$script_dir/Pose2Sim_Performance_Rokoko_MatchedPose.glb"
build_dir="$(mktemp -d "${TMPDIR:-/tmp}/myth-rig-baker.XXXXXX")"
trap 'rm -rf "$build_dir"' EXIT

cmake -S "$script_dir" -B "$build_dir/build" -G Ninja -DCMAKE_BUILD_TYPE=Release
cmake --build "$build_dir/build" --target gltf2ozz myth_rig_pack

mkdir -p "$build_dir/ozz/ual2" "$myth_dir/Assets/Rigs"
(
	cd "$build_dir/ozz/ual2"
	"$build_dir/build/ozz/src/animation/offline/gltf/gltf2ozz" \
		--file="$source_glb" --config_file="$script_dir/ual2.json" --endian=little
)
if [[ -f "$performance_glb" ]]; then
	mkdir -p "$build_dir/ozz/pose2sim"
	(
		cd "$build_dir/ozz/pose2sim"
		"$build_dir/build/ozz/src/animation/offline/gltf/gltf2ozz" \
			--file="$performance_glb" --config_file="$script_dir/ual2.json" --endian=little
	)
fi

animations=()
while IFS= read -r -d '' animation; do
	animations+=("$animation")
done < <(find "$build_dir/ozz" -name '*.ozz' ! -name skeleton.ozz -print0 | sort -z)

"$build_dir/build/myth_rig_pack" "$myth_dir/Assets/Rigs/ual2.ozzpack" \
	"$build_dir/ozz/ual2/skeleton.ozz" "${animations[@]}"
