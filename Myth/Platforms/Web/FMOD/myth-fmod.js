// Verbose [FMOD audio] lifecycle logging. Off by default; enable without a rebuild
// via ?audioLog=1 for on-device debugging.
let LOG_AUDIO = typeof location !== "undefined" && new URLSearchParams(location.search).get("audioLog") === "1";
let fmod = null;
let system = null;
let coreSystem = null;
let initialized = false;
let nextInstanceId = 1;
let updateTimer = null;
let audioResumeHandlersInstalled = false;
let gestureResumeArmed = false;
let mixerSuspended = false;
let reinitRequested = false;
let lastBankPaths = [];

// Every AudioContext FMOD's emscripten glue creates, captured at construction so
// we operate on the real objects regardless of where the module stashes them.
const trackedAudioContexts = [];
const AUDIO_LOG = "[FMOD audio]";

const banks = [];
const eventDescriptions = new Map();
const instances = new Map();

// bankData (optional): Map of bank path -> Uint8Array for banks the host page has
// already downloaded; those are written into the FMOD FS directly instead of being
// re-fetched by emscripten's preloader.
export async function initialize(bankPaths, bankData) {
	if (initialized)
		return;

	await loadFmodScript();

	const normalizedBankPaths = Array.from(bankPaths ?? []);

	// fmodstudioP.js would otherwise create a shared WebAssembly.Memory with a
	// 2 GB maximum (32768 pages). iOS Safari eagerly reserves the full maximum of
	// a *shared* memory up front and fails with "RangeError: Out of memory" on a
	// phone. The module honors a caller-supplied Module.wasmMemory, so we hand it
	// one with an iOS-friendly cap. Tunable via ?fmodMaxMB for on-device probing.
	const PAGE = 64 * 1024;
	const initialBytes = 80 * 1024 * 1024;
	const initialPages = initialBytes / PAGE;
	const requestedMaxMB = Number(new URLSearchParams(location.search).get("fmodMaxMB")) || 256;
	const maxPages = Math.max(initialPages, Math.floor((requestedMaxMB * 1024 * 1024) / PAGE));

	const module = {
		window: globalThis.window,
		INITIAL_MEMORY: initialBytes,
		wasmMemory: new WebAssembly.Memory({ initial: initialPages, maximum: maxPages, shared: true }),
		locateFile: (path) => `./FMOD/${path}`,
		preRun: [() => preloadBanks(module, normalizedBankPaths, bankData)],
		print: (message) => console.log(`[FMOD] ${message}`),
		printErr: (message) => console.error(`[FMOD] ${message}`),
	};

	trackAudioContexts();
	fmod = await globalThis.FMODModule(module);

	lastBankPaths = sortBankPaths(normalizedBankPaths);
	createStudioSystem();
	loadBanksFromFs(lastBankPaths);
	initialized = true;
	installAudioResumeHandlers();
	console.log(`FMOD JS bridge initialized with ${banks.length} loaded banks.`);
}

// Create the FMOD Studio + core system and initialize the output device. Calling
// system.initialize() (re)inits the WebAudio output, which mints a fresh
// AudioContext — the whole point of reinitialize() below. Shared by initialize().
function createStudioSystem() {
	const out = {};
	check(fmod.Studio_System_Create(out), "FMOD Studio system creation failed");
	system = out.val;

	check(system.getCoreSystem(out), "FMOD core system lookup failed");
	coreSystem = out.val;

	check(coreSystem.setDSPBufferSize(2048, 2), "FMOD DSP buffer setup failed");
	const rate = {};
	if (coreSystem.getDriverInfo(0, null, null, rate, null, null) === fmod.OK)
		check(coreSystem.setSoftwareFormat(rate.val, fmod.SPEAKERMODE_DEFAULT, 0), "FMOD software format setup failed");

	check(system.initialize(1024, fmod.STUDIO_INIT_NORMAL, fmod.INIT_NORMAL, null), "FMOD Studio system initialization failed");
}

// Tear down and recreate the Studio system to rebuild the audio output device —
// the only way to get a new AudioContext after iOS kills the old one on an audio
// interruption (setOutput() is illegal post-init; the worklet can't be revived).
// The wasm module + preloaded bank files in its FS survive, so banks just reload.
// Every previously created event instance is invalidated; the bridge's instance
// ops tolerate the now-stale ids (the game re-establishes music via the C# reload
// flow, and one-shot SFX recreate themselves). Returns whether a new AudioContext
// actually appeared — if FMOD reuses its module-global context, this stays false
// and we know reinit alone can't fix iOS.
export function reinitialize() {
	if (!initialized)
		return false;

	const before = trackedAudioContexts.length;
	if (LOG_AUDIO) console.log(`${AUDIO_LOG} reinitialize: releasing Studio system to rebuild output`);

	for (const id of Array.from(instances.keys()))
		releaseEvent(id);
	shutdownBanks();
	eventDescriptions.clear();
	check(system.release(), "FMOD Studio system release failed");
	system = null;
	coreSystem = null;

	createStudioSystem();
	loadBanksFromFs(lastBankPaths);

	const newContexts = trackedAudioContexts.length - before;
	if (LOG_AUDIO) console.log(`${AUDIO_LOG} reinitialize done; new AudioContexts=${newContexts}, banks=${banks.length}`);

	// Resume / arm-gesture for whatever context the rebuild produced.
	enterForeground("reinit");
	return newContexts > 0;
}

export function isInitialized() {
	return initialized;
}

// Consume-once: true when an iOS audio interruption has occurred and the output
// needs rebuilding. The C# side polls this and runs the coordinated reinit (capture
// music -> reinitialize() -> restore music) on the foreground frame.
export function consumeReinitRequest() {
	if (!reinitRequested)
		return false;
	reinitRequested = false;
	return true;
}

export function reloadBanks(bankPaths) {
	ensureInitialized();
	const nextBankPaths = sortBankPaths(bankPaths);
	if (banks.length === nextBankPaths.length && sameBankPaths(lastBankPaths, nextBankPaths))
		return;
	lastBankPaths = nextBankPaths;
	loadBanksFromFs(lastBankPaths);
}

export function resumeFromGesture() {
	if (initialized)
		enterForeground("start");
}

// Load banks from the emscripten FS (preloaded at initialize() and persisted with
// the wasm module, so they survive a reinitialize()). Replaces any loaded banks.
function loadBanksFromFs(bankPaths) {
	shutdownBanks();

	for (const path of Array.from(bankPaths ?? []).sort(bankLoadCompare)) {
		const out = {};
		check(system.loadBankFile(`/${path}`, fmod.STUDIO_LOAD_BANK_NORMAL, out), `Failed to load FMOD bank '${path}'`);
		banks.push(out.val);
	}

	eventDescriptions.clear();
}

export function getEventParameterLabels(eventPath, parameterName) {
	const description = getEventDescription(eventPath);
	const parameter = {};
	check(description.getParameterDescriptionByName(parameterName, parameter), `Failed to find FMOD parameter '${parameterName}' on event '${eventPath}'`);

	const count = Math.max(0, Math.floor((parameter.maximum ?? -1) - (parameter.minimum ?? 0)) + 1);
	const labels = [];
	for (let i = 0; i < count; i++) {
		const label = {};
		try {
			check(description.getParameterLabelByName(parameterName, i, label), `Failed to find FMOD parameter label ${i} for '${parameterName}'`);
		} catch {
			console.warn(`FMOD JS bridge could not query labels for '${parameterName}'; using game defaults.`);
			return "";
		}
		labels.push(label.val ?? "");
	}
	return labels.join("\n");
}

export function createEvent(path) {
	const description = getEventDescription(path);
	const out = {};
	check(description.createInstance(out), `Failed to create FMOD event instance '${path}'`);
	const id = nextInstanceId++;
	instances.set(id, out.val);
	return id;
}

export function startEvent(id) {
	const instance = getInstance(id);
	if (instance)
		check(instance.start(), "Failed to start FMOD event instance");
}

export function stopEvent(id, immediate) {
	const instance = getInstance(id);
	if (!instance)
		return;
	const mode = immediate ? fmod.STUDIO_STOP_IMMEDIATE : fmod.STUDIO_STOP_ALLOWFADEOUT;
	check(instance.stop(mode), "Failed to stop FMOD event instance");
}

export function setEventPaused(id, paused) {
	const instance = getInstance(id);
	if (instance)
		check(instance.setPaused(paused), `Failed to ${paused ? "pause" : "resume"} FMOD event instance`);
}

export function setEventVolume(id, volume) {
	const instance = getInstance(id);
	if (instance)
		check(instance.setVolume(volume), "Failed to set FMOD event volume");
}

export function getTimelinePosition(id) {
	const instance = getInstance(id);
	if (!instance)
		return 0;
	const out = {};
	check(instance.getTimelinePosition(out), "Failed to get FMOD event timeline position");
	return out.val;
}

export function setTimelinePosition(id, position) {
	const instance = getInstance(id);
	if (instance)
		check(instance.setTimelinePosition(position), "Failed to set FMOD event timeline position");
}

export function setEventParameter(id, name, value, ignoreSeekSpeed) {
	const instance = getInstance(id);
	if (instance)
		check(instance.setParameterByName(name, value, ignoreSeekSpeed), `Failed to set FMOD event parameter '${name}'`);
}

export function setEventParameterLabel(id, name, label, ignoreSeekSpeed) {
	const instance = getInstance(id);
	if (instance)
		check(instance.setParameterByNameWithLabel(name, label, ignoreSeekSpeed), `Failed to set FMOD event parameter '${name}' to label '${label}'`);
}

export function setEvent3DAttributes(id, px, py, pz, vx, vy, vz) {
	const instance = getInstance(id);
	if (instance)
		check(instance.set3DAttributes(attributes3D(px, py, pz, vx, vy, vz)), "Failed to set FMOD event 3D attributes");
}

export function releaseEvent(id) {
	const instance = instances.get(id);
	if (!instance)
		return;

	check(instance.release(), "Failed to release FMOD event instance");
	instances.delete(id);
}

export function setParameter(name, value, ignoreSeekSpeed) {
	ensureInitialized();
	check(system.setParameterByName(name, value, ignoreSeekSpeed), `Failed to set FMOD global parameter '${name}'`);
}

export function setParameterLabel(name, label, ignoreSeekSpeed) {
	ensureInitialized();
	check(system.setParameterByNameWithLabel(name, label, ignoreSeekSpeed), `Failed to set FMOD global parameter '${name}' to label '${label}'`);
}

export function setListenerAttributes(px, py, pz, vx, vy, vz) {
	ensureInitialized();
	check(system.setListenerAttributes(0, attributes3D(px, py, pz, vx, vy, vz), null), "Failed to set FMOD listener attributes");
}

export function setBusPaused(path, paused) {
	ensureInitialized();
	const out = {};
	check(system.getBus(path, out), `Failed to get FMOD bus '${path}'`);
	check(out.val.setPaused(paused), `Failed to ${paused ? "pause" : "resume"} FMOD bus '${path}'`);
}

export function update() {
	if (initialized)
		check(system.update(), "FMOD Studio system update failed");
}

export function shutdown() {
	if (!initialized)
		return;

	for (const id of Array.from(instances.keys()))
		releaseEvent(id);
	shutdownBanks();
	check(system.flushCommands(), "FMOD Studio command flush failed");
	check(system.release(), "FMOD Studio system release failed");
	system = null;
	coreSystem = null;
	initialized = false;
}

export function startUpdateLoop(milliseconds) {
	if (updateTimer)
		clearInterval(updateTimer);
	updateTimer = setInterval(() => {
		if (initialized)
			update();
	}, Math.max(1, milliseconds | 0));
}

// Wrap the AudioContext constructor(s) so every context FMOD creates is recorded
// the moment it exists — the only reliable handle, since the module may keep it on
// a property we don't know (mContext/context were guesses). Each gets a statechange
// log so we can watch suspend/interrupt/resume transitions live.
function trackAudioContexts() {
	for (const name of ["AudioContext", "webkitAudioContext"]) {
		const Original = globalThis[name];
		if (typeof Original !== "function" || Original.__fmodTracked)
			continue;

		const Wrapped = function (...args) {
			const context = new Original(...args);
			trackedAudioContexts.push(context);
			if (LOG_AUDIO) console.log(`${AUDIO_LOG} ${name} created: state=${context.state}, sampleRate=${context.sampleRate} (tracked: ${trackedAudioContexts.length})`);
			context.addEventListener?.("statechange", () => {
				if (LOG_AUDIO) console.log(`${AUDIO_LOG} statechange -> ${context.state}`);
				// iOS-only: an audio-session interruption permanently kills the worklet,
				// so flag that the C# side should rebuild FMOD's output (new context) on
				// return. Desktop never reaches this state.
				if (context.state === "interrupted")
					reinitRequested = true;
			});
			return context;
		};
		Wrapped.prototype = Original.prototype;
		Wrapped.__fmodTracked = true;

		try {
			globalThis[name] = Wrapped;
		} catch (err) {
			console.warn(`${AUDIO_LOG} could not wrap ${name}`, err);
		}
	}
}

// All known contexts: the ones we captured at construction, plus FMOD's guessed
// slots in case the wrap missed one (e.g. created before the bridge loaded).
function audioContexts() {
	const list = [...trackedAudioContexts];
	for (const context of [fmod?.mContext, fmod?.context]) {
		if (context && typeof context.resume === "function" && !list.includes(context))
			list.push(context);
	}
	return list;
}

function describeContexts(list) {
	return list.length ? list.map((c) => c.state).join(", ") : "<none>";
}

// Mobile audio lifecycle, layered because backgrounding behaves differently per OS:
//   - Most browsers (e.g. Android) just suspend the AudioContext. mixerSuspend on
//     hide / mixerResume + context.resume() on return is enough.
//   - iOS fires an audio-session *interruption* that zombifies the worklet — the
//     context can return "running" with the node "connected" yet stay silent. No
//     in-place resume fixes it; the statechange listener flags reinitRequested and
//     the C# side rebuilds FMOD's output via reinitialize() (a fresh AudioContext).
// Either way audio only (re)starts inside a user gesture, so when the context isn't
// running we retry the whole foreground path on the next touch.
function installAudioResumeHandlers() {
	if (audioResumeHandlersInstalled || typeof document === "undefined")
		return;
	audioResumeHandlersInstalled = true;

	// Console-pokeable from a remote-debugged phone: globalThis.fmodAudioStatus()
	globalThis.fmodAudioStatus = () => {
		const list = audioContexts();
		if (LOG_AUDIO) console.log(`${AUDIO_LOG} status: visibility=${document.visibilityState}, mixerSuspended=${mixerSuspended}, contexts=${list.length}, states=[${describeContexts(list)}]`);
		return list.map((c) => ({ state: c.state, sampleRate: c.sampleRate }));
	};

	if (LOG_AUDIO) console.log(`${AUDIO_LOG} installing visibility/focus handlers; contexts so far=[${describeContexts(audioContexts())}]`);

	document.addEventListener("visibilitychange", () => {
		if (LOG_AUDIO) console.log(`${AUDIO_LOG} visibilitychange -> ${document.visibilityState}`);
		if (document.visibilityState === "visible")
			enterForeground("visible");
		else
			enterBackground("hidden");
	});
	globalThis.addEventListener?.("focus", () => enterForeground("focus"));
	globalThis.addEventListener?.("pageshow", () => enterForeground("pageshow"));
	globalThis.addEventListener?.("pagehide", () => enterBackground("pagehide"));
}

// Call a core-system mixer method (mixerSuspend/mixerResume) and log its FMOD result.
function mixerCall(method, reason) {
	if (!coreSystem || typeof coreSystem[method] !== "function") {
		console.warn(`${AUDIO_LOG} coreSystem.${method} not available`);
		return false;
	}
	const result = coreSystem[method]();
	const ok = result === fmod.OK;
	if (LOG_AUDIO) console.log(`${AUDIO_LOG} ${method} (${reason}) -> ${result}${ok ? " OK" : " FAIL"}`);
	return ok;
}

function enterBackground(reason) {
	if (mixerSuspended)
		return;
	mixerSuspended = mixerCall("mixerSuspend", reason);
}

function enterForeground(reason) {
	const list = liveAudioContexts();
	if (LOG_AUDIO) console.log(`${AUDIO_LOG} foreground (${reason}); states=[${describeContexts(list)}], mixerSuspended=${mixerSuspended}`);

	// Wake the WebAudio context, then let FMOD resume its mixer on top of it. On iOS
	// the resume may require a user gesture, which the retry below provides.
	for (const context of list)
		if (context.state !== "running")
			context.resume().catch(() => {});

	// Drive FMOD's own worklet-resume hook directly. After a reinitialize() it's
	// freshly armed and builds the new AudioWorkletNode; FMOD otherwise waits for a
	// touchend/click on window to bubble to it, which the virtual gamepad mostly eats
	// (preventDefault) — that race was the ~5s "tap a bunch of times" delay. The hook
	// self-guards (no-ops unless armed), so calling it whenever is safe.
	pokeFmodResumeHook();

	if (mixerSuspended && mixerCall("mixerResume", reason))
		mixerSuspended = false;

	reconnectFmodOutput(reason);

	// Retry only while the context still isn't running (the legit iOS "resume needs a
	// gesture" case). Once it's running we stop — re-arming further can't help and
	// would just spam every touch.
	if (liveAudioContexts().some((c) => c.state !== "running"))
		armGestureResume(reason);
}

// FMOD spins up two throwaway "probe" contexts at startup (capability detection)
// that immediately close; only the live one carries the mixer. Operate on that —
// calling resume() on a closed context throws InvalidStateError forever.
function liveAudioContexts() {
	return audioContexts().filter((c) => c.state !== "closed");
}

// FMOD's WebAudio output exposes these on the module; whichever path is active
// resumes the context and (re)builds its worklet/script node. Both self-guard
// (mInputRegistered), so calling them when nothing is armed is a harmless no-op.
function pokeFmodResumeHook() {
	for (const fn of [fmod?.OutputAudioWorklet_resumeAudio, fmod?.OutputWebAudio_resumeAudio]) {
		if (typeof fn !== "function")
			continue;
		try {
			fn();
		} catch (err) {
			console.warn(`${AUDIO_LOG} FMOD resume hook failed`, err);
		}
	}
}

// FMOD's own worklet-resume hook is one-shot (it disarms after the initial unlock),
// so after an iOS interruption the context returns to "running" but FMOD's output
// node is left dangling off the destination — audible silence. Re-link it.
function reconnectFmodOutput(reason) {
	const context = fmod?.mContext;
	const node = fmod?.mWorkletNode;
	if (LOG_AUDIO) console.log(`${AUDIO_LOG} fmod output (${reason}): mContext=${context?.state}, mWorkletNode=${!!node}, connected=${fmod?.mWorkletNodeConnected}`);

	if (!context || context.state !== "running" || !node)
		return;

	try {
		node.disconnect();
		node.connect(context.destination);
		fmod.mWorkletNodeConnected = true;
		if (LOG_AUDIO) console.log(`${AUDIO_LOG} reconnected mWorkletNode -> destination (${reason})`);
	} catch (err) {
		console.warn(`${AUDIO_LOG} worklet reconnect failed`, err);
	}
}

function armGestureResume(reason) {
	if (gestureResumeArmed || typeof globalThis.addEventListener !== "function")
		return;
	gestureResumeArmed = true;
	if (LOG_AUDIO) console.log(`${AUDIO_LOG} arming one-shot gesture retry (next pointerdown/touchend)`);

	const retry = () => {
		gestureResumeArmed = false;
		globalThis.removeEventListener("pointerdown", retry);
		globalThis.removeEventListener("touchend", retry);
		if (LOG_AUDIO) console.log(`${AUDIO_LOG} gesture received; retrying foreground`);
		enterForeground(`${reason}+gesture`);
	};
	globalThis.addEventListener("pointerdown", retry, { once: true });
	globalThis.addEventListener("touchend", retry, { once: true });
}

function loadFmodScript() {
	if (globalThis.FMODModule)
		return Promise.resolve();

	return new Promise((resolve, reject) => {
		const script = document.createElement("script");
		script.src = "./FMOD/fmodstudioP.js";
		script.async = true;
		script.onload = () => resolve();
		script.onerror = () => reject(new Error("Failed to load fmodstudioP.js"));
		document.head.appendChild(script);
	});
}

function preloadBanks(module, bankPaths, bankData) {
	for (const path of bankPaths) {
		const parts = path.split("/");
		const name = parts.pop();
		const folder = `/${parts.join("/")}`;
		ensureFmodPath(module, folder);
		const bytes = bankData?.get?.(path);
		if (bytes)
			module.FS_createDataFile(folder, name, bytes, true, false, true);
		else
			module.FS_createPreloadedFile(folder, name, path, true, false);
	}
}

function ensureFmodPath(module, path) {
	if (path === "" || path === "/")
		return;

	let current = "";
	for (const part of path.split("/").filter(Boolean)) {
		const parent = current || "/";
		current += `/${part}`;
		try {
			module.FS_createPath(parent, part, true, true);
		} catch {
			// Existing paths throw in Emscripten's FS helpers.
		}
	}
}

function shutdownBanks() {
	for (let i = banks.length - 1; i >= 0; i--)
		check(banks[i].unload(), "Failed to unload FMOD bank");
	banks.length = 0;
}

function getEventDescription(path) {
	ensureInitialized();
	if (eventDescriptions.has(path))
		return eventDescriptions.get(path);

	const out = {};
	check(system.getEvent(path, out), `Failed to find FMOD event '${path}'`);
	eventDescriptions.set(path, out.val);
	return out.val;
}

// Returns null for unknown ids rather than throwing: after reinitialize() the game
// still holds SoundInstance ids from the released system. Their ops become inert
// (the game re-establishes what matters) instead of crashing the frame.
function getInstance(id) {
	return instances.get(id) ?? null;
}

function attributes3D(px, py, pz, vx, vy, vz) {
	const attributes = fmod._3D_ATTRIBUTES();
	setVector(attributes.position, px, py, pz);
	setVector(attributes.velocity, vx, vy, vz);
	setVector(attributes.forward, 0, 0, 1);
	setVector(attributes.up, 0, -1, 0);
	return attributes;
}

function setVector(vector, x, y, z) {
	vector.x = x;
	vector.y = y;
	vector.z = z;
}

function check(result, message) {
	if (result !== fmod.OK)
		throw new Error(`${message}: ${fmod.ErrorString(result)}`);
}

function ensureInitialized() {
	if (!initialized)
		throw new Error("FMOD JS bridge is not initialized.");
}

function bankLoadCompare(a, b) {
	return bankLoadOrder(a) - bankLoadOrder(b) || a.localeCompare(b);
}

function sortBankPaths(bankPaths) {
	return Array.from(bankPaths ?? []).sort(bankLoadCompare);
}

function sameBankPaths(a, b) {
	return a.length === b.length && a.every((path, i) => path === b[i]);
}

function bankLoadOrder(path) {
	const name = path.split("/").pop();
	if (name === "Master.bank")
		return 0;
	if (name === "Master.strings.bank")
		return 1;
	if (name?.endsWith(".strings.bank"))
		return 2;
	return 3;
}
