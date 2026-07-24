#!/usr/bin/env bash
# Builds libMythCapture.dylib from MythCapture.swift.
# Invoked automatically by Myth.csproj on macOS builds; safe to run by hand too.
set -euo pipefail
cd "$(dirname "$0")"

ARCH="$(uname -m)"
swiftc -O -emit-library -o libMythCapture.dylib MythCapture.swift \
	-target "${ARCH}-apple-macos13.0" \
	-framework ScreenCaptureKit \
	-framework AVFoundation \
	-framework CoreMedia \
	-framework CoreVideo

echo "[MythCapture] built libMythCapture.dylib (${ARCH})"
