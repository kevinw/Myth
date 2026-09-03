#!/usr/bin/env bash

set -u
cd "$(dirname "$0")/.."

pid_file=obj/myth-dev-host.pid
supervisor_pid_file=obj/myth-dev-watch.pid
run_request=obj/myth-dev-run.request
watch_pid=
marker=

stop() {
	trap - INT TERM EXIT
	if [[ -n "$watch_pid" ]] && kill -0 "$watch_pid" 2>/dev/null; then
		kill -TERM "$watch_pid" 2>/dev/null
		wait "$watch_pid" 2>/dev/null
	fi
	[[ -z "$marker" ]] || rm -f "$marker"
	rm -f "$pid_file"
	if [[ -f "$supervisor_pid_file" ]] && [[ "$(< "$supervisor_pid_file")" == "$$" ]]; then
		rm -f "$supervisor_pid_file"
	fi
}

mkdir -p obj
if [[ -f "$supervisor_pid_file" ]]; then
	supervisor_pid=$(<"$supervisor_pid_file")
	if [[ "$supervisor_pid" =~ ^[0-9]+$ ]] && kill -0 "$supervisor_pid" 2>/dev/null; then
		echo "Myth dev supervisor is already running (PID $supervisor_pid)."
		exit
	fi
fi
echo $$ > "$supervisor_pid_file"
trap stop INT TERM EXIT
rm -f obj/myth-dev-watch-marker.*

host_is_alive() {
	[[ -f "$pid_file" ]] || return 1
	host_pid=$(<"$pid_file")
	[[ "$host_pid" =~ ^[0-9]+$ ]] && kill -0 "$host_pid" 2>/dev/null
}

source_changed() {
	find Source tools/Myth.SourceGen ../Kit ../Foster/Framework \
		-type f \( -name '*.cs' -o -name '*.csproj' -o -name '*.props' -o -name '*.targets' \) \
		-newer "$marker" -print -quit | grep -q . && return
	for build_file in Myth.csproj ../Directory.Build.props ../Directory.Build.targets ../Directory.Packages.props; do
		[[ ! -f "$build_file" || ! "$build_file" -nt "$marker" ]] || return
	done
	return 1
}

wait_for_trigger() {
	marker=$(mktemp obj/myth-dev-watch-marker.XXXXXX)
	while ! source_changed && [[ ! -f "$run_request" ]]; do sleep 0.2; done
	rm -f "$marker"
	marker=
}

while true; do
	restarting=false
	rm -f "$pid_file"
	dotnet watch run --non-interactive --no-restore \
		--property:RunAnalyzers=false --property:UseFosterDll=true -- --dev-host "$@" &
	watch_pid=$!

	while kill -0 "$watch_pid" 2>/dev/null && ! host_is_alive; do sleep 0.1; done
	while kill -0 "$watch_pid" 2>/dev/null && host_is_alive; do sleep 0.2; done

	if kill -0 "$watch_pid" 2>/dev/null; then
		echo "Myth dev host stopped; a source change or Run request will start a fresh watcher."
		wait_for_trigger
		kill -TERM "$watch_pid" 2>/dev/null
		restarting=true
	fi
	wait "$watch_pid" 2>/dev/null
	watch_status=$?
	watch_pid=
	if [[ "$restarting" == false && "$watch_status" -ge 128 ]]; then
		exit
	fi
	if [[ "$restarting" == true ]]; then
		continue
	fi

	if [[ ! -f "$run_request" ]]; then
		echo "dotnet watch stopped; waiting for a source change or Run request."
		wait_for_trigger
	fi
done
