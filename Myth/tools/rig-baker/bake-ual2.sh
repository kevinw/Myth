#!/usr/bin/env bash
set -euo pipefail

script_dir="$(cd "$(dirname "$0")" && pwd)"
myth_dir="$(cd "$script_dir/../.." && pwd)"
source_glb="$myth_dir/tools/Universal Animation Library 2[Standard]/Unreal-Godot/UAL2_Standard.glb"
build_dir="$(mktemp -d "${TMPDIR:-/tmp}/myth-rig-baker.XXXXXX")"
trap 'rm -rf "$build_dir"' EXIT

cmake -S "$script_dir" -B "$build_dir/build" -G Ninja -DCMAKE_BUILD_TYPE=Release
cmake --build "$build_dir/build" --target gltf2ozz myth_rig_baker

mkdir -p "$build_dir/ozz" "$myth_dir/Assets/Rigs"
(
	cd "$build_dir/ozz"
	"$build_dir/build/ozz/src/animation/offline/gltf/gltf2ozz" \
		--file="$source_glb" --config_file="$script_dir/ual2.json" --endian=little
)

animations=()
while IFS= read -r -d '' animation; do
	animations+=("$animation")
done < <(find "$build_dir/ozz" -name '*.ozz' ! -name skeleton.ozz -print0 | sort -z)

"$build_dir/build/myth_rig_baker" "$build_dir/ozz/skeleton.ozz" \
	"$myth_dir/Assets/Rigs/ual2.mrig" 30 "${animations[@]}"
