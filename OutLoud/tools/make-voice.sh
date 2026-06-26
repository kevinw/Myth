#!/usr/bin/env bash
# OutLoud — run the whole pipeline for one voice: Audacity labels -> clips -> manifest ->
# translation cache -> FMOD events -> built banks.
#
# Prereqs: Audacity running with mod-script-pipe enabled; FMOD Studio installed.
# Usage: make-voice.sh <VoiceName> [audacity_project.aup3]
#   e.g. make-voice.sh Kev
set -euo pipefail

NAME=${1:?usage: make-voice.sh <VoiceName> [audacity_project.aup3]}
ROOT=$(cd "$(dirname "$0")/../.." && pwd)
TOOLS="$ROOT/OutLoud/tools"
FMOD_VOICE_DIR="$ROOT/Myth/Assets/Sounds/FMOD/Assets/Voice"
MANIFEST_DIR="$ROOT/Myth/Assets/Voice"
FSPRO="$ROOT/Myth/Assets/Sounds/FMOD/Myth.fspro"
FMODCLI="/Applications/FMOD Studio.app/Contents/MacOS/fmodstudiocl"

lower=$(echo "$NAME" | tr '[:upper:]' '[:lower:]')
PROJECT=${2:-$ROOT/Myth/Production/Audacity/$NAME/$lower.aup3}

echo "==> 1/5 slicing $NAME from Audacity"
python3 "$TOOLS/slice-voice.py" --project "$PROJECT" --name "$NAME"

echo "==> 2/5 baking manifest from $NAME clips"
"$TOOLS/bake-syllables.sh" "$FMOD_VOICE_DIR/$NAME" "$MANIFEST_DIR"

echo "==> 3/5 translating story lines (voice-independent, cached)"
python3 "$TOOLS/translate-voice.py" || echo "   translation step failed (non-fatal) — continuing"

echo "==> 4/5 writing voices list"
python3 - "$FMOD_VOICE_DIR" "$MANIFEST_DIR/voices.json" <<'PY'
import json, os, sys
src, out = sys.argv[1], sys.argv[2]
voices = sorted(n for n in os.listdir(src) if os.path.isdir(os.path.join(src, n)))
json.dump(voices, open(out, "w"))
print("   voices:", voices)
PY

echo "==> 5/5 building FMOD events + banks"
"$FMODCLI" -script "$TOOLS/fmod-create-voice-event.js" "$FSPRO"
"$FMODCLI" -build "$FSPRO"

echo
echo "Done. event:/voice/$NAME is built (flat pitch)."
echo "For pitch variation: in FMOD add automation on event:/voice/$NAME (master Pitch >"
echo "Add Automation > pitch, linear -12..+12), then re-run: \"$FMODCLI\" -build \"$FSPRO\""
