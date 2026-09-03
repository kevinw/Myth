#!/usr/bin/env bash

set -eu
cd "$(dirname "$0")/.."

supervisor_pid_file=obj/myth-dev-watch.pid
dll=bin/Debug/net10.0/Myth.dll

supervisor_is_alive() {
	[[ -f "$supervisor_pid_file" ]] || return 1
	supervisor_pid=$(<"$supervisor_pid_file")
	[[ "$supervisor_pid" =~ ^[0-9]+$ ]] && kill -0 "$supervisor_pid" 2>/dev/null
}

request_run() {
	for _ in {1..300}; do
		[[ -f "$dll" ]] && break
		sleep 0.1
	done
	if [[ ! -f "$dll" ]]; then
		echo "Myth did not build within 30 seconds." >&2
		return 1
	fi
	dotnet "$dll" --dev-command run
}

if supervisor_is_alive; then
	request_run
	exit
fi

# The first Play owns the long-lived task terminal, so it doubles as the server log.
# Later Plays see this supervisor, signal it from a short-lived terminal, and disappear.
request_run &
exec ./tools/dev-watch.sh "$@"
