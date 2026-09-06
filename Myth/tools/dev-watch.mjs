#!/usr/bin/env node

import { spawn, spawnSync } from "node:child_process";
import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
process.chdir(root);

const obj = path.join(root, "obj");
const hostPidFile = path.join(obj, "myth-dev-host.pid");
const supervisorPidFile = path.join(obj, "myth-dev-watch.pid");
const runRequest = path.join(obj, "myth-dev-run.request");
const watchLog = path.join(obj, "myth-dev-watch-last.log");
const sleep = (ms) => new Promise((resolve) => setTimeout(resolve, ms));

function readPid(filename) {
	try {
		const pid = Number.parseInt(fs.readFileSync(filename, "utf8"), 10);
		return Number.isInteger(pid) && pid > 0 ? pid : null;
	} catch {
		return null;
	}
}

function processIsAlive(pid) {
	if (pid === null) return false;
	try {
		process.kill(pid, 0);
		return true;
	} catch (error) {
		return error.code === "EPERM";
	}
}

fs.mkdirSync(obj, { recursive: true });
const supervisorPid = readPid(supervisorPidFile);
if (processIsAlive(supervisorPid)) {
	console.log(`Myth dev supervisor is already running (PID ${supervisorPid}).`);
	process.exit(0);
}
fs.writeFileSync(supervisorPidFile, `${process.pid}\n`);

for (const entry of fs.readdirSync(obj))
	if (entry.startsWith("myth-dev-watch-marker."))
		fs.rmSync(path.join(obj, entry), { force: true });

let stopping = false;
let watch = null;
let watchClosed = null;
let sourceRevision = 0;
let notificationTimer = null;
let failureNotified = false;
const sourceWatchers = [];
let cleaned = false;

function cleanup() {
	if (cleaned) return;
	cleaned = true;
	if (watch !== null) terminateWatch(watch, true);
	if (notificationTimer !== null) clearTimeout(notificationTimer);
	for (const watcher of sourceWatchers) watcher.close();
	fs.rmSync(hostPidFile, { force: true });
	if (readPid(supervisorPidFile) === process.pid)
		fs.rmSync(supervisorPidFile, { force: true });
}

async function stop(exitCode) {
	if (stopping) return;
	stopping = true;
	if (watch !== null && watch.exitCode === null && watch.signalCode === null) {
		terminateWatch(watch);
		if (watchClosed !== null) await Promise.race([watchClosed, sleep(2000)]);
		if (watch.exitCode === null && watch.signalCode === null) terminateWatch(watch, true);
	}
	cleanup();
	process.exit(exitCode);
}

process.once("SIGINT", () => void stop(130));
process.once("SIGTERM", () => void stop(143));
process.once("exit", cleanup);

let notifierCommand = null;
if (process.platform === "darwin" && process.env.MYTH_WATCH_NOTIFICATIONS !== "0") {
	for (const candidate of ["terminal-notifier", "/opt/homebrew/bin/terminal-notifier", "/usr/local/bin/terminal-notifier"]) {
		const check = spawnSync(candidate, ["-version"], { stdio: "ignore" });
		if (!check.error && check.status === 0) {
			notifierCommand = candidate;
			break;
		}
	}
	if (notifierCommand === null) {
		console.warn("Please run brew install terminal-notifier");
		const prompt = spawn("/usr/bin/osascript", [
			"-e",
			'display notification "Please run brew install terminal-notifier" with title "Myth dev watcher"',
		], { stdio: "ignore" });
		prompt.once("error", () => {});
		prompt.unref();
	}
}

function notify(message) {
	if (notifierCommand === null) return;
	const notification = spawn(notifierCommand, [
		"-title", "Myth",
		"-message", message,
		"-group", `myth-dev-watch:${root}`,
	], { stdio: "ignore" });
	notification.once("error", () => {});
	notification.unref();
}

function notifyBuilding(message = "Building…") {
	failureNotified = false;
	notify(message);
}

const sourceExtensions = new Set([".cs", ".csproj", ".props", ".targets"]);
const ignoredDirectories = new Set([".git", "bin", "node_modules", "obj"]);

function isSource(filename) {
	if (filename === null) return true;
	const parts = filename.toString().split(/[\\/]/);
	if (parts.some((part) => ignoredDirectories.has(part))) return false;
	return sourceExtensions.has(path.extname(parts.at(-1)).toLowerCase());
}

function sourceChanged() {
	sourceRevision++;
	if (notificationTimer !== null) clearTimeout(notificationTimer);
	notificationTimer = setTimeout(() => {
		notificationTimer = null;
		notifyBuilding("Rebuilding…");
	}, 150);
}

function watchDirectory(directory, recursive, accepts) {
	if (!fs.existsSync(directory)) return;
	try {
		const watcher = fs.watch(directory, { recursive }, (_, filename) => {
			if (accepts(filename)) sourceChanged();
		});
		watcher.on("error", (error) => console.error(`Source watcher failed for ${directory}: ${error.message}`));
		sourceWatchers.push(watcher);
	} catch (error) {
		console.error(`Could not watch ${directory}: ${error.message}`);
	}
}

for (const directory of ["Source", "tools/Myth.SourceGen", "../Kit", "../Foster/Framework"])
	watchDirectory(path.resolve(root, directory), true, isSource);

const rootBuildFiles = new Set(["Myth.csproj"]);
watchDirectory(root, false, (filename) => filename === null || rootBuildFiles.has(filename.toString()));
const sharedBuildFiles = new Set(["Directory.Build.props", "Directory.Build.targets", "Directory.Packages.props"]);
watchDirectory(path.resolve(root, ".."), false, (filename) => filename === null || sharedBuildFiles.has(filename.toString()));

function hostIsAlive() {
	return processIsAlive(readPid(hostPidFile));
}

function terminateWatch(child, force = false) {
	if (child.pid === undefined) return;
	try {
		if (process.platform === "win32") {
			const args = ["/PID", child.pid.toString(), "/T"];
			if (force) args.push("/F");
			const result = spawnSync("taskkill", args, { stdio: "ignore" });
			if (result.error || result.status !== 0) child.kill(force ? "SIGKILL" : "SIGTERM");
		} else {
			process.kill(-child.pid, force ? "SIGKILL" : "SIGTERM");
		}
	} catch (error) {
		if (error.code !== "ESRCH") child.kill(force ? "SIGKILL" : "SIGTERM");
	}
}

async function waitForTrigger(revision = sourceRevision) {
	while (!stopping && sourceRevision === revision && !fs.existsSync(runRequest))
		await sleep(200);
}

function watchFailedInternally() {
	try {
		const log = fs.readFileSync(watchLog, "utf8");
		return log.includes("An unexpected error occurred:") &&
			/Microsoft\.CodeAnalysis\.(Emit|EditAndContinue)/.test(log);
	} catch {
		return false;
	}
}

let outputTail = "";
function inspectOutput(chunk) {
	outputTail = (outputTail + chunk).slice(-8192);
	if (!failureNotified && outputTail.includes("Build FAILED.")) {
		failureNotified = true;
		notify("Build failed");
	}
}

async function startWatch() {
	fs.rmSync(hostPidFile, { force: true });
	outputTail = "";
	notifyBuilding();
	const log = fs.openSync(watchLog, "w");
	const child = spawn("dotnet", [
		"watch", "run", "--non-interactive", "--no-restore",
		"--property:RunAnalyzers=false", "--property:UseFosterDll=true",
		"--", "--dev-host", ...process.argv.slice(2),
	], {
		cwd: root,
		detached: process.platform !== "win32",
		stdio: ["inherit", "pipe", "pipe"],
	});
	watch = child;
	watchClosed = new Promise((resolve) => child.once("close", (code, signal) => resolve({ code, signal })));

	const forward = (stream, target) => stream.on("data", (chunk) => {
		target.write(chunk);
		fs.writeSync(log, chunk);
		inspectOutput(chunk.toString());
	});
	forward(child.stdout, process.stdout);
	forward(child.stderr, process.stderr);
	child.once("error", (error) => console.error(`Failed to start dotnet watch: ${error.message}`));

	const cycleRevision = sourceRevision;
	while (!stopping && child.exitCode === null && child.signalCode === null && !hostIsAlive()) await sleep(100);
	while (!stopping && child.exitCode === null && child.signalCode === null && hostIsAlive()) await sleep(200);

	let restarting = false;
	if (!stopping && child.exitCode === null && child.signalCode === null) {
		console.log("Myth dev host stopped; a source change or Run request will start a fresh watcher.");
		await waitForTrigger(cycleRevision);
		if (!stopping) {
			terminateWatch(child);
			restarting = true;
		}
	}

	const result = await watchClosed;
	if (!restarting && hostIsAlive()) terminateWatch(child);
	fs.closeSync(log);
	watch = null;
	watchClosed = null;
	return { ...result, restarting };
}

async function main() {
	while (!stopping) {
		const result = await startWatch();
		if (stopping) return;
		if (result.signal !== null && !result.restarting) return;
		if ((result.code ?? 0) >= 128 && !result.restarting) return;
		if (result.restarting) continue;
		if (watchFailedInternally()) {
			console.log("dotnet watch hot reload failed internally; restarting immediately.");
			await sleep(100);
			continue;
		}
		if (!fs.existsSync(runRequest)) {
			console.log("dotnet watch stopped; waiting for a source change or Run request.");
			await waitForTrigger();
		}
	}
}

try {
	await main();
} catch (error) {
	console.error(error);
	process.exitCode = 1;
} finally {
	cleanup();
}
