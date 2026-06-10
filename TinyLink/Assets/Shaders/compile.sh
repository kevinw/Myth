#!/usr/bin/env bash
set -euo pipefail

script_dir=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" &> /dev/null && pwd)
out_dir="$script_dir/Compiled"
platform="${1:-all}"
fallback_shadercross="$script_dir/../../tools/macos/shadercross"
mkdir -p "$out_dir"

case "$platform" in
	all|mac|osx|darwin|windows|win|linux|web|wgsl)
		;;
	*)
		echo "Usage: $0 [all|mac|windows|linux|web]" >&2
		exit 2
		;;
esac

if [[ "$platform" == "all" || "$platform" == "web" || "$platform" == "wgsl" ]]; then
	if ! command -v dxc > /dev/null 2>&1; then
		echo "dxc not found on PATH; required for WGSL shader output" >&2
		exit 1
	fi
	if ! command -v naga > /dev/null 2>&1; then
		echo "naga not found on PATH; install naga-cli to generate WGSL shader output" >&2
		exit 1
	fi
fi

if [[ "$platform" != "web" && "$platform" != "wgsl" ]]; then
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
fi

compile() {
	local input=$1
	local stage=$2
	local filename
	local spv
	filename="$(basename -- "$input")"
	filename="${filename%.*}.$stage"

	echo "Compiling '$input' $stage stage for $platform ..."

	if [[ "$platform" == "web" || "$platform" == "wgsl" ]]; then
		compile_wgsl "$input" "$stage" "$filename"
		return
	fi

	if [[ "$platform" == "all" || "$platform" == "linux" ]]; then
		spv="$out_dir/$filename.spv"
	else
		spv="$out_dir/$filename.${BASHPID:-$$}.${RANDOM}.spv"
	fi

	"$shadercross_bin" "$input" -I "$script_dir" -e "${stage}_main" -t "$stage" -s HLSL -o "$spv"

	case "$platform" in
		all)
			"$shadercross_bin" "$spv" -e "${stage}_main" -t "$stage" -s SPIRV -o "$out_dir/$filename.msl"
			"$shadercross_bin" "$spv" -e "${stage}_main" -t "$stage" -s SPIRV -o "$out_dir/$filename.dxil"
			compile_wgsl "$input" "$stage" "$filename"
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
		web|wgsl)
			compile_wgsl "$input" "$stage" "$filename"
			rm -f "$spv"
			;;
	esac
}

compile_wgsl() {
	local input=$1
	local stage=$2
	local filename=$3
	local profile
	local wgsl_spv

	case "$stage" in
		vertex) profile="vs_6_0" ;;
		fragment) profile="ps_6_0" ;;
		compute) profile="cs_6_0" ;;
		*)
			echo "Unsupported WGSL shader stage '$stage'" >&2
			exit 2
			;;
	esac

	wgsl_spv="$out_dir/$filename.wgsl.${BASHPID:-$$}.${RANDOM}.spv"
	dxc -spirv -T "$profile" -E "${stage}_main" -I "$script_dir" -D BROWSER=1 -fvk-s-shift 100 2 "$input" -Fo "$wgsl_spv"
	naga --entry-point "${stage}_main" "$wgsl_spv" "$out_dir/$filename.wgsl"
	rm -f "$wgsl_spv"
}

if [[ "$platform" == "web" || "$platform" == "wgsl" ]]; then
	pids=()
	for file in "$script_dir"/*.hlsl; do
		if [[ "$(basename -- "$file")" == "Common.hlsl" ]]; then
			continue
		fi
		compile "$file" vertex &
		pids+=("$!")
		compile "$file" fragment &
		pids+=("$!")
	done

	for file in "$script_dir"/Compute/*.hlsl; do
		[[ -e "$file" ]] || continue
		compile "$file" compute &
		pids+=("$!")
	done

	status=0
	for pid in "${pids[@]}"; do
		if ! wait "$pid"; then
			status=1
		fi
	done
	exit "$status"
else
	for file in "$script_dir"/*.hlsl; do
		if [[ "$(basename -- "$file")" == "Common.hlsl" ]]; then
			continue
		fi
		compile "$file" vertex
		compile "$file" fragment
	done

	for file in "$script_dir"/Compute/*.hlsl; do
		[[ -e "$file" ]] || continue
		compile "$file" compute
	done
fi
