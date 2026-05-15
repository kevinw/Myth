#!/usr/bin/env bash
set -euo pipefail

script_dir=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" &> /dev/null && pwd)
out_dir="$script_dir/Compiled"
platform="${1:-all}"
fallback_shadercross="$script_dir/../../tools/macos/shadercross"
mkdir -p "$out_dir"

if command -v shadercross > /dev/null 2>&1; then
	shadercross_bin=$(command -v shadercross)
elif [[ -x "$fallback_shadercross" ]]; then
	shadercross_bin="$fallback_shadercross"
else
	echo "shadercross not found on PATH or at '$fallback_shadercross'" >&2
	exit 1
fi

if [[ "$(uname -s)" == "Darwin" ]]; then
	shadercross_dir=$(cd -- "$(dirname -- "$shadercross_bin")" &> /dev/null && pwd)
	dyld_paths=("$shadercross_dir")
	for lib_dir in /opt/homebrew/lib /usr/local/lib; do
		if [[ -d "$lib_dir" ]]; then
			dyld_paths+=("$lib_dir")
		fi
	done

	if [[ -n "${DYLD_LIBRARY_PATH:-}" ]]; then
		dyld_paths+=("$DYLD_LIBRARY_PATH")
	fi

	export DYLD_LIBRARY_PATH
	DYLD_LIBRARY_PATH=$(IFS=:; printf '%s' "${dyld_paths[*]}")
fi

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
		spv="$out_dir/$filename.$$.${RANDOM}.spv"
	fi

	"$shadercross_bin" "$input" -e "${stage}_main" -t "$stage" -s HLSL -o "$spv"

	case "$platform" in
		all)
			"$shadercross_bin" "$spv" -e "${stage}_main" -t "$stage" -s SPIRV -o "$out_dir/$filename.msl"
			"$shadercross_bin" "$spv" -e "${stage}_main" -t "$stage" -s SPIRV -o "$out_dir/$filename.dxil"
			;;
		mac|osx|darwin)
			"$shadercross_bin" "$spv" -e "${stage}_main" -t "$stage" -s SPIRV -o "$out_dir/$filename.msl"
			rm -f "$spv"
			;;
		windows|win)
			"$shadercross_bin" "$spv" -e "${stage}_main" -t "$stage" -s SPIRV -o "$out_dir/$filename.dxil"
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
