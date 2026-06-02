#!/usr/bin/env bash
set -euo pipefail

script_dir=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" &> /dev/null && pwd)
url="${1:-http://127.0.0.1:8083/}"

node "$script_dir/browser-smoke.mjs" "$url"
