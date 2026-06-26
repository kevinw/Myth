#!/usr/bin/env python3
"""OutLoud voice slicer — Audacity labels -> per-syllable oggs.

Drives a *running* Audacity (via mod-script-pipe) to open a labelled project and export
each label region as <labeltext>.ogg into the FMOD project's asset tree, ready for the
bake + fmod-create-voice-event scripts.

Prerequisites (one-time):
  1. Audacity > Preferences > Modules > set "mod-script-pipe" to Enabled, restart Audacity.
  2. Leave Audacity running (it needn't have anything open — this script opens the project).

Usage: slice-voice.py [--project Myth/Production/kev.aup3] [--name Kev]
Record tip: one label per syllable, the label's text IS the syllable spelling (ba, scho, mü).
"""
import argparse, json, os, sys, time
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]

if sys.platform == "win32":
	TONAME, FROMNAME, EOL = r"\\.\pipe\ToSrvPipe", r"\\.\pipe\FromSrvPipe", "\r\n\0"
else:
	uid = str(os.getuid())
	TONAME = "/tmp/audacity_script_pipe.to." + uid
	FROMNAME = "/tmp/audacity_script_pipe.from." + uid
	EOL = "\n"


def connect():
	if not os.path.exists(TONAME):
		sys.exit("Audacity pipe not found. Start Audacity with mod-script-pipe enabled "
			"(Preferences > Modules), then re-run.")
	return open(TONAME, "w"), open(FROMNAME, "r")


def command(to, frm, cmd):
	to.write(cmd + EOL)
	to.flush()
	out, line = "", ""
	while True:
		out += line
		line = frm.readline()
		if line == "\n" and out:
			return out


def labels(to, frm):
	# GetInfo returns the JSON then a "BatchCommand finished: OK" status line.
	raw = command(to, frm, "GetInfo: Type=Labels Format=JSON")
	data = json.loads(raw.split("BatchCommand finished")[0])
	out = []
	for track in data:                      # [[trackIndex, [[start, end, text], ...]], ...]
		entries = track[1] if len(track) == 2 and isinstance(track[1], list) else [track]
		for start, end, text in entries:
			if text.strip():
				out.append((float(start), float(end), text.strip()))
	return out


def main():
	ap = argparse.ArgumentParser()
	ap.add_argument("--project", default=str(ROOT / "Myth/Production/Audacity/Kev/kev.aup3"))
	ap.add_argument("--name", default="Kev", help="voice folder under Assets/Voice/")
	args = ap.parse_args()

	out_dir = ROOT / "Myth/Assets/Sounds/FMOD/Assets/Voice" / args.name
	out_dir.mkdir(parents=True, exist_ok=True)

	to, frm = connect()
	command(to, frm, f'OpenProject2: Filename="{Path(args.project).resolve()}"')
	time.sleep(0.5)

	found = labels(to, frm)
	if not found:
		sys.exit("No labels found — add a label track (one label per syllable) in the project.")

	for start, end, text in found:
		path = out_dir / f"{text}.ogg"
		command(to, frm, f"Select: Start={start} End={end}")
		resp = command(to, frm, f'Export2: Filename="{path}" NumChannels=1')
		ok = "finished: OK" in resp
		print(f"  {'ok ' if ok else 'FAIL'} {text} [{start:.3f}-{end:.3f}] -> {path.name}")

	print(f"exported {len(found)} syllables -> {out_dir}")


if __name__ == "__main__":
	main()
