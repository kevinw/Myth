import { dotnet } from "./_framework/dotnet.js";
import * as FosterWebGPU from "./_framework/Content/Web/foster-webgpu.js";
import * as FosterBrowserFmod from "./_framework/Content/Web/foster-fmod.js";

globalThis.FosterWebGPU = FosterWebGPU;
globalThis.FosterBrowserFmod = FosterBrowserFmod;

const isTinyLinkCleanExitStatus = (error) =>
	error?.name === "ExitStatus" &&
	(error.status === 0 || error.message?.includes("exit(0)"));

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
		if (!isTinyLinkCleanExitStatus(error))
			throw error;
	}
} catch (error) {
	console.error("TinyLink browser startup failed", error);
	console.error(error?.stack ?? error);
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
	const files = await loadAssetManifest();
	const preloadFiles = files.filter(shouldPreloadIntoDotnetFs);
	const fmodBankFiles = files.filter(isFmodBankFile);
	onProgress?.(0, preloadFiles.length);

	for (let i = 0; i < preloadFiles.length; i++) {
		const file = preloadFiles[i];
		const bytes = new Uint8Array(await (await fetch(file)).arrayBuffer());
		createWasmFile(module, file, bytes);
		onProgress?.(i + 1, preloadFiles.length);
	}

	for (const file of fmodBankFiles)
		createWasmFile(module, file, new Uint8Array());

	console.log(`Preloaded ${preloadFiles.length} TinyLink asset files into wasm FS.`);
	return files;
}

async function loadAssetManifest() {
	const response = await fetch("asset-manifest.json");
	if (!response.ok)
		throw new Error(`Failed to load asset manifest: ${response.status}`);

	const files = await response.json();
	if (!Array.isArray(files))
		throw new Error("Asset manifest must be a JSON array.");

	return files
		.filter((path) => typeof path === "string" && path.startsWith("Assets/"))
		.sort();
}

function shouldPreloadIntoDotnetFs(path) {
	return !isFmodBankFile(path);
}

function isFmodBankFile(path) {
	return path.startsWith("Assets/Sounds/FMOD/Build/Mobile/") && path.endsWith(".bank");
}

function createWasmFile(module, file, bytes) {
	const parts = file.split("/");
	const name = parts.pop();
	ensureWasmPath(module, parts);
	module.FS_createDataFile(`/${parts.join("/")}`, name, bytes, true, false, true);
}

function ensureWasmPath(module, parts) {
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
}
