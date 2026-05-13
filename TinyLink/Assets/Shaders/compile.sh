#!/usr/bin/env bash
set -euo pipefail

script_dir=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" &> /dev/null && pwd)
out_dir="$script_dir/Compiled"
platform="${1:-all}"
mkdir -p "$out_dir"

case "$platform" in
	all|mac|osx|darwin|windows|win|linux)
		;;
	*)
		echo "Usage: $0 [all|mac|windows|linux]" >&2
		exit 2
		;;
esac

compile() {
	local input=$1
	local stage=$2
	local filename
	local spv
	filename="$(basename -- "$input")"
	filename="${filename%.*}.$stage"

	echo "Compiling '$input' $stage stage for $platform ..."

	if [[ "$platform" == "all" || "$platform" == "linux" ]]; then
		spv="$out_dir/$filename.spv"
	else
		spv="$(mktemp "$out_dir/$filename.XXXXXX.spv")"
	fi

	shadercross "$input" -e "${stage}_main" -t "$stage" -s HLSL -o "$spv"

	case "$platform" in
		all)
			shadercross "$spv" -e "${stage}_main" -t "$stage" -s SPIRV -o "$out_dir/$filename.msl"
			shadercross "$spv" -e "${stage}_main" -t "$stage" -s SPIRV -o "$out_dir/$filename.dxil"
			;;
		mac|osx|darwin)
			shadercross "$spv" -e "${stage}_main" -t "$stage" -s SPIRV -o "$out_dir/$filename.msl"
			rm -f "$spv"
			;;
		windows|win)
			shadercross "$spv" -e "${stage}_main" -t "$stage" -s SPIRV -o "$out_dir/$filename.dxil"
			rm -f "$spv"
			;;
		linux)
			;;
	esac
}

for file in "$script_dir"/*.hlsl; do
	compile "$file" vertex
	compile "$file" fragment
done
