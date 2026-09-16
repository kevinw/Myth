#!/usr/bin/env bash
# Rebuilds the PDF and html/, then publishes html/ (plus functions/) to https://human-questions-pitch.pages.dev
# Needs Docker running (`colima start`) and a wrangler login (`npx wrangler login`).
set -euo pipefail
cd "$(dirname "$0")"

tectonic --synctex --keep-logs pitchdeck.tex
./build-html.sh
# run from this folder so Pages picks up functions/ (byte-range video serving)
npx wrangler@latest pages deploy html --project-name human-questions-pitch --branch main --commit-dirty=true
