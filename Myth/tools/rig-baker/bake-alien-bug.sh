#!/usr/bin/env bash
set -euo pipefail

script_dir="$(cd "$(dirname "$0")" && pwd)"
myth_dir="$(cd "$script_dir/../.." && pwd)"
source_glb="$script_dir/alien-bug/alien-bug.glb"
build_dir="$(mktemp -d "${TMPDIR:-/tmp}/myth-alien-bug-baker.XXXXXX")"
trap 'rm -rf "$build_dir"' EXIT

cmake -S "$script_dir" -B "$build_dir/build" -G Ninja -DCMAKE_BUILD_TYPE=Release
cmake --build "$build_dir/build" --target gltf2ozz myth_rig_pack

mkdir -p "$build_dir/ozz" "$myth_dir/Assets/Rigs"
(
	cd "$build_dir/ozz"
	"$build_dir/build/ozz/src/animation/offline/gltf/gltf2ozz" \
		--file="$source_glb" --config_file="$script_dir/alien-bug.json" --endian=little
)

animations=()
while IFS= read -r -d '' animation; do
	animations+=("$animation")
done < <(find "$build_dir/ozz" -name '*.ozz' ! -name skeleton.ozz -print0 | sort -z)

"$build_dir/build/myth_rig_pack" "$myth_dir/Assets/Rigs/alien-bug.ozzpack" \
	"$build_dir/ozz/skeleton.ozz" "${animations[@]}"
