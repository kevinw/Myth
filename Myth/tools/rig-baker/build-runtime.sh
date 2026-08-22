#!/usr/bin/env bash
set -euo pipefail

script_dir="$(cd "$(dirname "$0")" && pwd)"
myth_dir="$(cd "$script_dir/../.." && pwd)"
build_dir="${1:-$myth_dir/obj/ozz-runtime}"

cmake -S "$script_dir" -B "$build_dir" -G Ninja \
	-DCMAKE_BUILD_TYPE=MinSizeRel \
	-Dozz_build_tools=OFF \
	-Dozz_build_gltf=OFF \
	-Dozz_build_fbx=OFF \
	-Dozz_build_data=OFF \
	-Dozz_build_samples=OFF \
	-Dozz_build_howtos=OFF \
	-Dozz_build_tests=OFF \
	-Dozz_build_postfix=OFF
cmake --build "$build_dir" --target myth_ozz
