#!/bin/bash
# Drives the game with scripted keys on the X desktop (xdotool), for trying things out headlessly
# and grabbing screenshots: builds and launches the game (build.sh run --no-lighting trace) on $DISPLAY, runs the
# steps, then quits it. Its log goes to /tmp/play_<name>.log, screenshots to /tmp/play_<name>_<n>_<label>.png.
#   ./play-linux.sh <name> [down <key> | up <key> | tap <key> | sleep <s> | snap <label>]...
# e.g. run right, jump, and catch the top of the jump:
#   DISPLAY=:1 ./play-linux.sh jump down Right sleep 0.5 tap space sleep 0.3 snap top up Right sleep 1 snap landed
set -e
cd "$(dirname "$0")"
name=$1; shift
./build.sh > /dev/null
./build.sh run --no-lighting trace > /tmp/play_$name.log 2>&1 &
pid=$!
for i in $(seq 1 50); do wid=$(xdotool search --name "Human Questions" 2>/dev/null | tail -1); [ -n "$wid" ] && break; sleep 0.2; done
sleep 1.5
xdotool windowactivate --sync "$wid" 2>/dev/null || true
xdotool windowfocus "$wid" 2>/dev/null || true
n=0
while [ $# -gt 0 ]; do
    case $1 in
        snap) n=$((n+1)); import -window root "/tmp/play_${name}_${n}_$2.png"; shift 2;;
        sleep) sleep "$2"; shift 2;;
        down) xdotool keydown "$2"; shift 2;;
        up) xdotool keyup "$2"; shift 2;;
        tap) xdotool key "$2"; shift 2;;
        *) echo "unknown step: $1"; shift;;
    esac
done
kill $pid; wait $pid 2>/dev/null || true
