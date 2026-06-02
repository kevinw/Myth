#!/usr/bin/env node

const url = process.argv[2] ?? "http://127.0.0.1:8083/";
const cdpPort = Number(process.env.CDP_PORT ?? "9223");
const timeoutMs = Number(process.env.SMOKE_TIMEOUT_MS ?? "30000");
const chromePath = process.env.CHROME_PATH ?? "/Applications/Google Chrome.app/Contents/MacOS/Google Chrome";

const profile = `/tmp/tinylink-smoke-chrome-${process.pid}`;
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
	}
} finally {
	chrome?.kill("SIGTERM");
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
		"about:blank",
	];

	const child = spawn(chromePath, args, { stdio: "ignore" });
	child.unref();
	return child;
}

async function waitForPageTarget() {
	const deadline = Date.now() + timeoutMs;
	while (Date.now() < deadline) {
		try {
			const response = await fetch(`http://127.0.0.1:${cdpPort}/json`);
			const targets = await response.json();
			const page = targets.find(target => target.type === "page");
			if (page)
				return page;
		} catch {
			// Chrome is still starting.
		}
		await delay(100);
	}
	throw new Error(`Chrome CDP did not start on port ${cdpPort}.`);
}

async function runSmoke(webSocketDebuggerUrl) {
	const ws = new WebSocket(webSocketDebuggerUrl);
	const pending = new Map();
	const events = [];
	let id = 0;

	ws.onmessage = event => {
		const message = JSON.parse(event.data);
		if (message.id && pending.has(message.id)) {
			pending.get(message.id)(message);
			pending.delete(message.id);
			return;
		}

		if (message.method === "Runtime.consoleAPICalled") {
			events.push(message.params.args.map(arg => arg.value ?? arg.description ?? "").join(" "));
		} else if (message.method === "Runtime.exceptionThrown") {
			events.push(message.params.exceptionDetails.exception?.description ?? message.params.exceptionDetails.text);
		} else if (message.method === "Log.entryAdded") {
			events.push(message.params.entry.text);
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
	await send("Page.navigate", { url: `${url}${url.includes("?") ? "&" : "?"}smoke=${Date.now()}` });

	let timedOut = false;
	try {
		await evaluate(`new Promise((resolve, reject) => {
		const deadline = performance.now() + ${timeoutMs};
		const tick = () => {
			if (globalThis.TinyLinkStartReady && document.querySelector("#start-button")) resolve(true);
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
		await delay(8000);
	}

	const state = await evaluate(`({
		isolated: crossOriginIsolated,
		gateHidden: document.querySelector("#start-gate")?.hidden ?? null,
		canvas: (() => {
			const canvas = document.querySelector("canvas");
			return canvas ? { width: canvas.width, height: canvas.height } : null;
		})()
	})`);

	ws.close();
	const errors = events.filter(event =>
		/MONO_WASM|Assertion at|TinyLink browser startup failed|Unhandled Promise Rejection|Invalid UTF-8|program exited/.test(event) &&
		!/favicon\\.ico/.test(event));
	if (timedOut)
		errors.push("start ready timeout");
	if (state.gateHidden !== true)
		errors.push(`start gate did not close: gateHidden=${state.gateHidden}`);
	if (errors.length > 0)
		state.events = events.slice(-20);

	return { errors, state };
}

function delay(ms) {
	return new Promise(resolve => setTimeout(resolve, ms));
}
