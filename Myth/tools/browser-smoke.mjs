#!/usr/bin/env node

const args = parseArgs(process.argv.slice(2));
const url = args.url ?? "http://127.0.0.1:8083/";
const cdpPort = Number(process.env.CDP_PORT ?? args.cdpPort ?? await findFreePort());
const timeoutMs = Number(process.env.SMOKE_TIMEOUT_MS ?? "30000");
const settleMs = Number(process.env.SMOKE_SETTLE_MS ?? args.settleMs ?? "8000");
const chromePath = process.env.CHROME_PATH ?? "/Applications/Google Chrome.app/Contents/MacOS/Google Chrome";
const screenshotPath = args.screenshot ?? process.env.SCREENSHOT_PATH ?? "";

const profile = `/tmp/myth-smoke-chrome-${process.pid}`;
const chrome = await launchChrome();

try {
	const target = await waitForPageTarget();
	const result = await runSmoke(target.webSocketDebuggerUrl);
	if (result.errors.length > 0) {
		console.error("Browser smoke failed:");
		for (const error of result.errors)
			console.error(`- ${error}`);
		if (result.state.events) {
			console.error("Recent browser events:");
			for (const event of result.state.events)
				console.error(`  ${event}`);
		}
		process.exitCode = 1;
	} else {
		console.log("Browser smoke passed.");
		console.log(`crossOriginIsolated=${result.state.isolated}, gateHidden=${result.state.gateHidden}`);
		if (result.screenshotPath)
			console.log(`Screenshot: ${result.screenshotPath}`);
	}
} finally {
	await stopChrome(chrome);
}

async function launchChrome() {
	const { spawn } = await import("node:child_process");
	const { rm } = await import("node:fs/promises");
	await rm(profile, { recursive: true, force: true });

	const args = [
		`--remote-debugging-port=${cdpPort}`,
		`--user-data-dir=${profile}`,
		"--enable-unsafe-webgpu",
		"--disable-gpu-sandbox",
		"--no-first-run",
		"--no-default-browser-check",
		"--window-size=1280,720",
		"about:blank",
	];

	return spawn(chromePath, args, { stdio: "ignore" });
}

async function stopChrome(chrome) {
	if (!chrome || chrome.exitCode !== null)
		return;

	const exited = new Promise(resolve => chrome.once("exit", resolve));
	chrome.kill("SIGTERM");
	const didExit = await Promise.race([
		exited.then(() => true),
		delay(2000).then(() => false),
	]);
	if (!didExit)
		chrome.kill("SIGKILL");
}

async function waitForPageTarget() {
	const deadline = Date.now() + timeoutMs;
	let browserWebSocketDebuggerUrl = "";
	while (Date.now() < deadline) {
		try {
			const versionResponse = await fetch(`http://127.0.0.1:${cdpPort}/json/version`);
			const version = await versionResponse.json();
			browserWebSocketDebuggerUrl = version.webSocketDebuggerUrl;
			break;
		} catch {
			// Chrome is still starting.
		}
		await delay(100);
	}
	if (!browserWebSocketDebuggerUrl)
		throw new Error(`Chrome CDP did not start on port ${cdpPort}.`);

	const targetId = await createPageTarget(browserWebSocketDebuggerUrl);
	while (Date.now() < deadline) {
		try {
			const response = await fetch(`http://127.0.0.1:${cdpPort}/json`);
			const targets = await response.json();
			const page = targets.find(target => target.id === targetId || target.type === "page");
			if (page?.webSocketDebuggerUrl)
				return page;
		} catch {
			// The page target is still being registered.
		}
		await delay(100);
	}
	throw new Error(`Chrome page target did not start on port ${cdpPort}.`);
}

async function runSmoke(webSocketDebuggerUrl) {
	const ws = new WebSocket(webSocketDebuggerUrl);
	const pending = new Map();
	const events = [];
	// Error-severity events (console.error, uncaught exceptions, error-level log
	// entries — this is where WebGPU uncaptured/validation errors surface). These
	// fail the smoke unconditionally; `events` is only context for the report.
	const consoleErrors = [];
	let id = 0;

	ws.onmessage = event => {
		const message = JSON.parse(event.data);
		if (message.id && pending.has(message.id)) {
			pending.get(message.id)(message);
			pending.delete(message.id);
			return;
		}

		if (message.method === "Runtime.consoleAPICalled") {
			const text = message.params.args.map(arg => arg.value ?? arg.description ?? "").join(" ");
			events.push(text);
			if (message.params.type === "error" || message.params.type === "assert")
				consoleErrors.push(`console.${message.params.type}: ${text}`);
		} else if (message.method === "Runtime.exceptionThrown") {
			const details = message.params.exceptionDetails;
			const text = details.exception?.description ?? details.text;
			events.push(text);
			consoleErrors.push(`uncaught exception: ${text}`);
		} else if (message.method === "Log.entryAdded") {
			const entry = message.params.entry;
			events.push(entry.text);
			if (entry.level === "error")
				consoleErrors.push(`${entry.source}: ${entry.text}`);
		}
	};

	await new Promise((resolve, reject) => {
		ws.onopen = resolve;
		ws.onerror = reject;
	});

	const send = (method, params = {}) => {
		const messageId = ++id;
		ws.send(JSON.stringify({ id: messageId, method, params }));
		return new Promise(resolve => pending.set(messageId, resolve));
	};

	const evaluate = async expression => {
		const response = await send("Runtime.evaluate", { expression, awaitPromise: true, returnByValue: true });
		if (response.result?.exceptionDetails)
			throw new Error(response.result.exceptionDetails.exception?.description ?? response.result.exceptionDetails.text);
		return response.result.result.value;
	};

	await send("Runtime.enable");
	await send("Log.enable");
	await send("Page.enable");
	await send("Emulation.setDeviceMetricsOverride", {
		width: 1280,
		height: 720,
		deviceScaleFactor: 1,
		mobile: false,
	});
	await send("Page.navigate", { url: `${url}${url.includes("?") ? "&" : "?"}smoke=${Date.now()}` });

	let timedOut = false;
	try {
		await evaluate(`new Promise((resolve, reject) => {
		const deadline = performance.now() + ${timeoutMs};
		const tick = () => {
			if (globalThis.MythStartReady && document.querySelector("#start-button")) resolve(true);
			else if (performance.now() > deadline) reject(new Error("start ready timeout"));
			else setTimeout(tick, 100);
		};
		tick();
	})`);
	} catch (error) {
		timedOut = true;
		events.push(String(error?.message ?? error));
	}

	if (!timedOut) {
		await evaluate(`document.querySelector("#start-button").click(); true`);
		try {
			await waitForGateHidden(evaluate);
			await delay(settleMs);
		} catch (error) {
			timedOut = true;
			events.push(String(error?.message ?? error));
		}
	}

	const state = await evaluate(`({
		isolated: crossOriginIsolated,
		gateHidden: document.querySelector("#start-gate")?.hidden ?? null,
		canvas: (() => {
			const canvas = document.querySelector("canvas");
			return canvas ? { width: canvas.width, height: canvas.height } : null;
		})()
	})`);
	let savedScreenshotPath = "";
	if (screenshotPath && errorsFromState(events, consoleErrors, timedOut, state).length === 0)
		savedScreenshotPath = await captureScreenshot(send, screenshotPath);

	ws.close();
	const errors = errorsFromState(events, consoleErrors, timedOut, state);
	if (errors.length > 0)
		state.events = events.slice(-20);

	return { errors, state, screenshotPath: savedScreenshotPath };
}

async function createPageTarget(browserWebSocketDebuggerUrl) {
	const connection = await openCdpConnection(browserWebSocketDebuggerUrl);
	try {
		const response = await connection.send("Target.createTarget", { url: "about:blank" });
		if (response.error)
			throw new Error(response.error.message ?? "Target.createTarget failed.");
		return response.result.targetId;
	} finally {
		connection.close();
	}
}

async function openCdpConnection(webSocketDebuggerUrl) {
	const ws = new WebSocket(webSocketDebuggerUrl);
	const pending = new Map();
	let id = 0;

	ws.onmessage = event => {
		const message = JSON.parse(event.data);
		if (message.id && pending.has(message.id)) {
			pending.get(message.id)(message);
			pending.delete(message.id);
		}
	};

	await new Promise((resolve, reject) => {
		ws.onopen = resolve;
		ws.onerror = reject;
	});

	return {
		send: (method, params = {}) => {
			const messageId = ++id;
			ws.send(JSON.stringify({ id: messageId, method, params }));
			return new Promise(resolve => pending.set(messageId, resolve));
		},
		close: () => ws.close(),
	};
}

function delay(ms) {
	return new Promise(resolve => setTimeout(resolve, ms));
}

async function waitForGateHidden(evaluate) {
	await evaluate(`new Promise((resolve, reject) => {
		const deadline = performance.now() + ${timeoutMs};
		const tick = () => {
			if (document.querySelector("#start-gate")?.hidden === true) resolve(true);
			else if (performance.now() > deadline) reject(new Error("start gate timeout"));
			else setTimeout(tick, 100);
		};
		tick();
	})`);
}

async function captureScreenshot(send, path) {
	const { mkdir, writeFile } = await import("node:fs/promises");
	const { dirname, resolve } = await import("node:path");
	const outputPath = resolve(path);
	await mkdir(dirname(outputPath), { recursive: true });

	const response = await send("Page.captureScreenshot", {
		format: "png",
		fromSurface: true,
	});
	if (response.error)
		throw new Error(response.error.message ?? "Screenshot capture failed.");

	await writeFile(outputPath, Buffer.from(response.result.data, "base64"));
	return outputPath;
}

async function findFreePort() {
	const { createServer } = await import("node:net");
	const server = createServer();
	await new Promise((resolve, reject) => {
		server.on("error", reject);
		server.listen(0, "127.0.0.1", resolve);
	});
	const { port } = server.address();
	await new Promise(resolve => server.close(resolve));
	return port;
}

function errorsFromState(events, consoleErrors, timedOut, state) {
	// Any error-severity console/exception/log event fails the smoke (this is how
	// WebGPU validation errors get caught), plus the legacy fatal-string patterns.
	const errors = [
		...consoleErrors.filter(event => !/favicon\.ico/.test(event)),
		...events.filter(event =>
			/MONO_WASM|Assertion at|Myth browser startup failed|Unhandled Promise Rejection|Invalid UTF-8|program exited/.test(event) &&
			!/favicon\.ico/.test(event)),
	];
	if (timedOut)
		errors.push("start ready timeout");
	if (state.gateHidden !== true)
		errors.push(`start gate did not close: gateHidden=${state.gateHidden}`);
	return errors;
}

function parseArgs(argv) {
	const parsed = {};
	for (let i = 0; i < argv.length; i++) {
		const arg = argv[i];
		if (arg === "--screenshot") {
			parsed.screenshot = argv[++i];
		} else if (arg.startsWith("--screenshot=")) {
			parsed.screenshot = arg.slice("--screenshot=".length);
		} else if (arg === "--settle-ms") {
			parsed.settleMs = argv[++i];
		} else if (arg.startsWith("--settle-ms=")) {
			parsed.settleMs = arg.slice("--settle-ms=".length);
		} else if (arg === "--cdp-port") {
			parsed.cdpPort = argv[++i];
		} else if (arg.startsWith("--cdp-port=")) {
			parsed.cdpPort = arg.slice("--cdp-port=".length);
		} else if (arg === "-h" || arg === "--help") {
			console.log("Usage: tools/browser-smoke.mjs [url] [--screenshot PATH] [--settle-ms MS] [--cdp-port PORT]");
			process.exit(0);
		} else if (!parsed.url) {
			parsed.url = arg;
		} else {
			throw new Error(`Unknown argument: ${arg}`);
		}
	}
	return parsed;
}
