import { dotnet } from "./_framework/dotnet.js";
import * as FosterWebGPU from "./_framework/Content/Web/foster-webgpu.js";
import * as FosterBrowserFmod from "./_framework/Content/Web/foster-fmod.js";

globalThis.FosterWebGPU = FosterWebGPU;
globalThis.FosterBrowserFmod = FosterBrowserFmod;

try {
	await FosterWebGPU.initialize("#foster-canvas");

	const runtime = await dotnet
		.withDiagnosticTracing(false)
		.create();
	const { setModuleImports, runMain } = runtime;

	setModuleImports("FosterWebGPU", FosterWebGPU);
	setModuleImports("FosterBrowserFmod", FosterBrowserFmod);
	const startGate = prepareStartGate();
	startGate.progress(0.05);
	const assetFiles = await preloadAssets(runtime.Module, (loaded, total) => {
		startGate.progress(0.05 + 0.75 * loaded / Math.max(total, 1));
	});
	runtime.Module.runtimeKeepalivePush?.();

	const bankFiles = assetFiles.filter(isFmodBankFile);
	if (bankFiles.length > 0) {
		startGate.progress(0.85);
		await FosterBrowserFmod.initialize(bankFiles);
		FosterBrowserFmod.startUpdateLoop(20);
	}

	globalThis.TinyLinkStartReady = true;
	startGate.ready();
	await startGate.wait();
	try {
		await runMain("TinyLink", []);
	} catch (error) {
		if (!isCleanExitStatus(error))
			throw error;
	}
} catch (error) {
	console.error("TinyLink browser startup failed", error);
	console.error(error?.stack ?? error);
}

function isCleanExitStatus(error) {
	return error?.name === "ExitStatus" &&
		(error.status === 0 || error.message?.includes("exit(0)"));
}

function prepareStartGate() {
	const gate = document.querySelector("#start-gate");
	const button = document.querySelector("#start-button");
	const progress = document.querySelector("#start-progress");
	if (!(gate instanceof HTMLElement) || !(button instanceof HTMLButtonElement))
		return {
			progress: () => {},
			ready: () => {},
			wait: () => Promise.resolve(),
		};

	button.disabled = true;
	button.textContent = "Loading...";
	if (progress instanceof HTMLProgressElement)
		progress.value = 0;

	let ready = false;
	const wait = new Promise((resolve) => {
		const start = () => {
			if (!ready)
				return;

			gate.hidden = true;
			resolve();
		};

		button.addEventListener("click", start, { once: true });
	});

	return {
		progress: (value) => {
			if (progress instanceof HTMLProgressElement)
				progress.value = Math.max(0, Math.min(1, value));
		},
		ready: () => {
			ready = true;
			button.disabled = false;
			button.textContent = "Start";
			if (progress instanceof HTMLProgressElement)
				progress.value = 1;
		},
		wait: () => wait,
	};
}

async function preloadAssets(module, onProgress) {
	const files = [];
	await collectFiles("Assets/", files);
	const preloadFiles = files.filter(shouldPreloadIntoDotnetFs);
	onProgress?.(0, preloadFiles.length);

	for (let i = 0; i < preloadFiles.length; i++) {
		const file = preloadFiles[i];
		const bytes = new Uint8Array(await (await fetch(file)).arrayBuffer());
		const parts = file.split("/");
		const name = parts.pop();
		let path = "";

		for (const part of parts) {
			path += `/${part}`;
			try {
				module.FS.stat(path);
			} catch {
				const parent = path.slice(0, path.lastIndexOf("/")) || "/";
				module.FS_createPath(parent, part, true, true);
			}
		}

		module.FS_createDataFile(`/${parts.join("/")}`, name, bytes, true, false, true);
		onProgress?.(i + 1, preloadFiles.length);
	}

	console.log(`Preloaded ${preloadFiles.length} TinyLink asset files into wasm FS.`);
	return files;
}

async function collectFiles(path, files) {
	const response = await fetch(path);
	if (!response.ok)
		throw new Error(`Failed to list ${path}: ${response.status}`);

	const html = await response.text();
	const doc = new DOMParser().parseFromString(html, "text/html");

	for (const link of doc.querySelectorAll("a[href]")) {
		const href = link.getAttribute("href");
		if (!href || href === "../" || href.startsWith("?"))
			continue;

		const url = new URL(href, new URL(path, location.href));
		if (url.origin !== location.origin)
			continue;

		const relative = decodeURIComponent(url.pathname.slice(1));
		if (relative.endsWith("/"))
			await collectFiles(relative, files);
		else if (shouldIncludeAsset(relative))
			files.push(relative);
	}
}

function shouldIncludeAsset(path) {
	if (path.endsWith(".DS_Store"))
		return false;
	if (path.startsWith("Assets/Shaders/Compiled/"))
		return path.endsWith(".wgsl");
	return true;
}

function shouldPreloadIntoDotnetFs(path) {
	return !isFmodBankFile(path);
}

function isFmodBankFile(path) {
	return path.startsWith("Assets/Sounds/FMOD/Build/Mobile/") && path.endsWith(".bank");
}
