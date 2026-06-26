#!/usr/bin/env bash
# OutLoud syllable baker.
#
# Turns a folder of raw German syllable recordings into a normalized, sequentially
# named clip set (syll_00.wav .. syll_NN.wav) plus a manifest.json, ready to import
# into the FMOD "voice/syllable" multi-instrument (see fmod-create-voice-event.js).
#
# Recording tip: say ~30-40 short CV syllables (ba, de, mi, scho, lu, te, ...). One
# long take is fine -- slice it in Audacity with Analyze > Label Sounds, then
# File > Export > Export Multiple (one wav per label). Drop those wavs in IN_DIR.
#
# IMPORTANT: name each file after the syllable it contains (scho.wav, ba.wav, mü.wav).
# The baker reads the vowel out of the name so OutLoud can colour babble from a line's
# German translation (vowel melody). Sorted filename order is the stable clip index.
#
# Usage: bake-syllables.sh <in_dir> <out_dir>
set -euo pipefail

IN_DIR=${1:?usage: bake-syllables.sh <in_dir> <out_dir>}
OUT_DIR=${2:?usage: bake-syllables.sh <in_dir> <out_dir>}
mkdir -p "$OUT_DIR"
rm -f "$OUT_DIR"/syll_*.wav

# Mono 48kHz, trimmed of silence at both ends, loudness-normalized so every
# syllable plays back at a consistent level. Deterministic order = sorted by name.
i=0
names=()
shopt -s nullglob
for src in $(ls "$IN_DIR"/*.wav "$IN_DIR"/*.aif "$IN_DIR"/*.aiff "$IN_DIR"/*.ogg "$IN_DIR"/*.flac "$IN_DIR"/*.mp3 "$IN_DIR"/*.m4a 2>/dev/null | sort); do
	out=$(printf "%s/syll_%02d.wav" "$OUT_DIR" "$i")
	ffmpeg -hide_banner -loglevel error -y -i "$src" \
		-af "silenceremove=start_periods=1:start_threshold=-50dB:start_silence=0.02,areverse,silenceremove=start_periods=1:start_threshold=-50dB:start_silence=0.02,areverse,loudnorm=I=-16:TP=-1.5:LRA=11" \
		-ac 1 -ar 48000 "$out"
	base=$(basename "$src"); base=${base%.*}
	echo "baked $base -> $(basename "$out")"
	names+=("$base")
	i=$((i + 1))
done

if [ "$i" -eq 0 ]; then
	echo "no input audio found in $IN_DIR" >&2
	exit 1
fi

# Classify each clip's vowel from its syllable-spelling name (unicode-safe via python).
python3 - "$OUT_DIR/manifest.json" "${names[@]}" <<'PY'
import json, sys
out = sys.argv[1]; names = sys.argv[2:]
GROUPS = {'a': 'aàáâ', 'e': 'eéèêä', 'i': 'iíìîy', 'o': 'oóòôö', 'u': 'uúùûü'}
def vowel(name):
	for ch in name.lower():
		for v, group in GROUPS.items():
			if ch in group: return v
	return 'a'
json.dump({"count": len(names), "names": names, "vowels": [vowel(n) for n in names]}, open(out, 'w'))
print(f"baked {len(names)} syllables -> {out} (VoiceSyllableCount auto-derives from this)")
PY
