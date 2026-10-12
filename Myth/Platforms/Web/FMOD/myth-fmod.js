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
// Every bank's bytes, kept so a reinitialize() (or a reloadBanks()) can load them again: FMOD 2.04's
// HTML5 build no longer exposes the emscripten FS we used to preload them into.
const bankBytes = new Map();
const eventDescriptions = new Map();
const instances = new Map();

// bankData (optional): Map of bank path -> Uint8Array for banks the host page has
// already downloaded; anything missing is fetched.
export async function initialize(bankPaths, bankData) {
	if (initialized)
		return;

	const { default: createFMOD } = await import("./fmodstudioP.js");

	const normalizedBankPaths = Array.from(bankPaths ?? []);
	for (const path of normalizedBankPaths)
		bankBytes.set(path, bankData?.get?.(path) ?? new Uint8Array(await (await fetch(path)).arrayBuffer()));

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
		locateFile: (path) => new URL(path, import.meta.url).href,
		print: (message) => console.log(`[FMOD] ${message}`),
		printErr: (message) => console.error(`[FMOD] ${message}`),
	};

	trackAudioContexts();
	fmod = await createFMOD(module);

	lastBankPaths = sortBankPaths(normalizedBankPaths);
	createStudioSystem();
	loadBanks(lastBankPaths);
	initialized = true;
	installAudioResumeHandlers();
	console.log(`FMOD JS bridge initialized with ${banks.length} loaded banks.`);
}

// Create the FMOD Studio + core system and initialize the output device. Calling
// system.initialize() (re)inits the WebAudio output, which mints a fresh
// AudioContext — the whole point of reinitialize() below. Shared by initialize().
function createStudioSystem() {
	system = call("FMOD Studio system creation failed", () => fmod.Studio_System_Create());
	coreSystem = call("FMOD core system lookup failed", () => system.getCoreSystem());
	call("FMOD DSP buffer setup failed", () => coreSystem.setDSPBufferSize(2048, 2));
	let rate = null;
	try {
		rate = coreSystem.getDriverInfo(0).systemrate;
	} catch {
		// No driver info yet: keep FMOD's default rate.
	}
	if (rate)
		call("FMOD software format setup failed", () => coreSystem.setSoftwareFormat(rate, fmod.SPEAKERMODE_DEFAULT, 0));
	call("FMOD Studio system initialization failed", () => system.initialize(1024, fmod.STUDIO_INIT_NORMAL, fmod.INIT_NORMAL, null));
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
	call("FMOD Studio system release failed", () => system.release());
	system = null;
	coreSystem = null;

	createStudioSystem();
	loadBanks(lastBankPaths);

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
	loadBanks(lastBankPaths);
}

export function resumeFromGesture() {
	if (initialized)
		enterForeground("start");
}

// Load banks from the bytes kept at initialize(), so they survive a reinitialize(). Replaces any loaded banks.
function loadBanks(bankPaths) {
	shutdownBanks();

	for (const path of Array.from(bankPaths ?? []).sort(bankLoadCompare)) {
		const bytes = bankBytes.get(path);
		if (!bytes)
			throw new Error(`FMOD bank '${path}' was not downloaded at initialize()`);
		const buffer = bytes.byteOffset === 0 && bytes.byteLength === bytes.buffer.byteLength
			? bytes.buffer
			: bytes.buffer.slice(bytes.byteOffset, bytes.byteOffset + bytes.byteLength);
		banks.push(call(`Failed to load FMOD bank '${path}'`, () =>
			system.loadBankMemory(buffer, bytes.byteLength, fmod.STUDIO_LOAD_MEMORY, fmod.STUDIO_LOAD_BANK_NORMAL)));
	}

	eventDescriptions.clear();
}

export function getEventParameterLabels(eventPath, parameterName) {
	const description = getEventDescription(eventPath);
	const parameter = call(`Failed to find FMOD parameter '${parameterName}' on event '${eventPath}'`,
		() => description.getParameterDescriptionByName(parameterName));

	const count = Math.max(0, Math.floor((parameter.maximum ?? -1) - (parameter.minimum ?? 0)) + 1);
	const labels = [];
	for (let i = 0; i < count; i++) {
		try {
			labels.push(description.getParameterLabelByName(parameterName, i) ?? "");
		} catch {
			console.warn(`FMOD JS bridge could not query labels for '${parameterName}'; using game defaults.`);
			return "";
		}
	}
	return labels.join("\n");
}

export function createEvent(path) {
	const description = getEventDescription(path);
	const instance = call(`Failed to create FMOD event instance '${path}'`, () => description.createInstance());
	const id = nextInstanceId++;
	instances.set(id, instance);
	return id;
}

export function startEvent(id) {
	const instance = getInstance(id);
	if (instance)
		call("Failed to start FMOD event instance", () => instance.start());
}

export function stopEvent(id, immediate) {
	const instance = getInstance(id);
	if (!instance)
		return;
	const mode = immediate ? fmod.STUDIO_STOP_IMMEDIATE : fmod.STUDIO_STOP_ALLOWFADEOUT;
	call("Failed to stop FMOD event instance", () => instance.stop(mode));
}

export function setEventPaused(id, paused) {
	const instance = getInstance(id);
	if (instance)
		call(`Failed to ${paused ? "pause" : "resume"} FMOD event instance`, () => instance.setPaused(paused));
}

export function setEventVolume(id, volume) {
	const instance = getInstance(id);
	if (instance)
		call("Failed to set FMOD event volume", () => instance.setVolume(volume));
}

export function getTimelinePosition(id) {
	const instance = getInstance(id);
	if (!instance)
		return 0;
	return call("Failed to get FMOD event timeline position", () => instance.getTimelinePosition());
}

export function setTimelinePosition(id, position) {
	const instance = getInstance(id);
	if (instance)
		call("Failed to set FMOD event timeline position", () => instance.setTimelinePosition(position));
}

export function setEventParameter(id, name, value, ignoreSeekSpeed) {
	const instance = getInstance(id);
	if (instance)
		call(`Failed to set FMOD event parameter '${name}'`, () => instance.setParameterByName(name, value, ignoreSeekSpeed));
}

export function setEventParameterLabel(id, name, label, ignoreSeekSpeed) {
	const instance = getInstance(id);
	if (instance)
		call(`Failed to set FMOD event parameter '${name}' to label '${label}'`, () => instance.setParameterByNameWithLabel(name, label, ignoreSeekSpeed));
}

export function setEvent3DAttributes(id, px, py, pz, vx, vy, vz) {
	const instance = getInstance(id);
	if (instance)
		call("Failed to set FMOD event 3D attributes", () => instance.set3DAttributes(attributes3D(px, py, pz, vx, vy, vz)));
}

export function releaseEvent(id) {
	const instance = instances.get(id);
	if (!instance)
		return;

	call("Failed to release FMOD event instance", () => instance.release());
	instances.delete(id);
}

export function setParameter(name, value, ignoreSeekSpeed) {
	ensureInitialized();
	call(`Failed to set FMOD global parameter '${name}'`, () => system.setParameterByName(name, value, ignoreSeekSpeed));
}

export function setParameterLabel(name, label, ignoreSeekSpeed) {
	ensureInitialized();
	call(`Failed to set FMOD global parameter '${name}' to label '${label}'`, () => system.setParameterByNameWithLabel(name, label, ignoreSeekSpeed));
}

export function setListenerAttributes(px, py, pz, vx, vy, vz) {
	ensureInitialized();
	call("Failed to set FMOD listener attributes", () => system.setListenerAttributes(0, attributes3D(px, py, pz, vx, vy, vz)));
}

export function setBusPaused(path, paused) {
	ensureInitialized();
	const bus = call(`Failed to get FMOD bus '${path}'`, () => system.getBus(path));
	call(`Failed to ${paused ? "pause" : "resume"} FMOD bus '${path}'`, () => bus.setPaused(paused));
}

export function setVcaVolume(path, volume) {
	ensureInitialized();
	let vca;
	try {
		vca = system.getVCA(path);
	} catch {
		return false;
	}
	call(`Failed to set FMOD VCA volume '${path}'`, () => vca.setVolume(volume));
	return true;
}

export function update() {
	if (initialized)
		call("FMOD Studio system update failed", () => system.update());
}

export function shutdown() {
	if (!initialized)
		return;

	for (const id of Array.from(instances.keys()))
		releaseEvent(id);
	shutdownBanks();
	call("FMOD Studio command flush failed", () => system.flushCommands());
	call("FMOD Studio system release failed", () => system.release());
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
	try {
		coreSystem[method]();
		if (LOG_AUDIO) console.log(`${AUDIO_LOG} ${method} (${reason}) -> OK`);
		return true;
	} catch (err) {
		if (LOG_AUDIO) console.log(`${AUDIO_LOG} ${method} (${reason}) -> FAIL ${err}`);
		return false;
	}
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

function shutdownBanks() {
	for (let i = banks.length - 1; i >= 0; i--)
		call("Failed to unload FMOD bank", () => banks[i].unload());
	banks.length = 0;
}

function getEventDescription(path) {
	ensureInitialized();
	if (eventDescriptions.has(path))
		return eventDescriptions.get(path);

	const description = call(`Failed to find FMOD event '${path}'`, () => system.getEvent(path));
	eventDescriptions.set(path, description);
	return description;
}

// Returns null for unknown ids rather than throwing: after reinitialize() the game
// still holds SoundInstance ids from the released system. Their ops become inert
// (the game re-establishes what matters) instead of crashing the frame.
function getInstance(id) {
	return instances.get(id) ?? null;
}

function attributes3D(px, py, pz, vx, vy, vz) {
	return {
		position: { x: px, y: py, z: pz },
		velocity: { x: vx, y: vy, z: vz },
		forward: { x: 0, y: 0, z: 1 },
		up: { x: 0, y: -1, z: 0 },
	};
}

// FMOD 2.04's HTML5 API throws on failure instead of returning a result code; this
// keeps our context on the message.
function call(message, fn) {
	try {
		return fn();
	} catch (err) {
		throw new Error(`${message}: ${err?.message ?? err}`);
	}
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
