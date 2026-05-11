#!/usr/bin/env bash
set -euo pipefail

script_dir=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" &> /dev/null && pwd)
out_dir="$script_dir/Compiled"
mkdir -p "$out_dir"

compile() {
	local input=$1
	local stage=$2
	local filename
	filename="$(basename -- "$input")"
	filename="${filename%.*}.$stage"

	echo "Compiling '$input' $stage stage ..."
	shadercross "$input" -e "${stage}_main" -t "$stage" -s HLSL -o "$out_dir/$filename.spv"
	shadercross "$out_dir/$filename.spv" -e "${stage}_main" -t "$stage" -s SPIRV -o "$out_dir/$filename.msl"
	shadercross "$out_dir/$filename.spv" -e "${stage}_main" -t "$stage" -s SPIRV -o "$out_dir/$filename.dxil"
}

for file in "$script_dir"/*.hlsl; do
	compile "$file" vertex
	compile "$file" fragment
done
