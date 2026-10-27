#!/bin/bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
CFG="${CFG:-Debug}"
ROOM="${ROOM:-start}"
WARMUP="${WARMUP:-300}"
MEASURE="${MEASURE:-600}"

dotnet build "$ROOT/Myth.csproj" -c "$CFG" --no-restore

cd "$ROOT"
exec "./bin/$CFG/Myth" \
	--room "$ROOM" \
	--headless \
	--alloc-warmup-frames "$WARMUP" \
	--alloc-measure-frames "$MEASURE" \
	"$@"
