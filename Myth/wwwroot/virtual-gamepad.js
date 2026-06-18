// On-screen touch controller for the browser build — the web analog of the iOS
// GCVirtualController (see Platforms/iOS/MythIos/Program.cs). It renders a left
// analog stick + A/B/X/Y face buttons and exposes itself to the game by merging
// a synthetic "standard" Gamepad into navigator.getGamepads(). Foster's browser
// input provider already polls navigator.getGamepads() every frame, so no engine
// or gameplay changes are needed — the existing Controls.cs bindings just work.
//
// Visibility is driven from C# via setVirtualControllerVisible() (wired to
// Manager.WantsVirtualGameController). If a *real* gamepad is connected we hide
// ourselves and stop injecting, so we never create a phantom second controller.

// Standard mapping indices (https://w3c.github.io/gamepad/#remapping):
//   buttons: 0=South(A) 1=East(B) 2=West(X) 3=North(Y) ... 12-15=Dpad
//   axes:    0=LeftX 1=LeftY (-1 up, +1 down)
const BUTTON_COUNT = 17;
const AXIS_COUNT = 4;

const virtualPad = {
	__mythVirtual: true,
	id: "Myth Virtual Controller (Standard Mapping)",
	index: 0,
	connected: true,
	mapping: "standard",
	timestamp: 0,
	axes: new Array(AXIS_COUNT).fill(0),
	buttons: Array.from({ length: BUTTON_COUNT }, () => ({ pressed: false, touched: false, value: 0 })),
};

let wantsController = false; // mirrors Manager.WantsVirtualGameController
let injected = false;       // whether the virtual pad is currently in getGamepads()
let root = null;            // overlay DOM root

const touchCapable = () =>
	window.matchMedia?.("(pointer: coarse)").matches || "ontouchstart" in window;

// Dev bypass: ?vpad=1 force-shows the overlay on desktop (ignores touch + play
// state) so it can be tested with a mouse. Real-gamepad auto-hide still applies.
const forceShow = new URLSearchParams(location.search).get("vpad") === "1";

// --- navigator.getGamepads() override ------------------------------------
let originalGetGamepads = null;

function realPadsPresent() {
	if (!originalGetGamepads) return false;
	for (const pad of originalGetGamepads()) if (pad) return true;
	return false;
}

function installGamepadHook() {
	if (originalGetGamepads || typeof navigator.getGamepads !== "function") return;
	originalGetGamepads = navigator.getGamepads.bind(navigator);
	navigator.getGamepads = () => {
		const pads = Array.from(originalGetGamepads());
		if (injected) {
			let slot = pads.findIndex((p) => p == null);
			if (slot < 0) slot = pads.length;
			virtualPad.index = slot;
			pads[slot] = virtualPad;
		}
		return pads;
	};
}

function setInjected(on) {
	if (on === injected) return;
	injected = on;
	bump();
	const type = on ? "gamepadconnected" : "gamepaddisconnected";
	try {
		window.dispatchEvent(new GamepadEvent(type, { gamepad: virtualPad }));
	} catch {
		/* GamepadEvent ctor missing on some browsers; Foster polls anyway */
	}
}

function bump() {
	virtualPad.timestamp = performance.now();
}

// --- input state mutation ------------------------------------------------
function setButton(index, down) {
	const b = virtualPad.buttons[index];
	if (!b || b.pressed === down) return;
	b.pressed = down;
	b.touched = down;
	b.value = down ? 1 : 0;
	bump();
}

function setStick(x, y) {
	virtualPad.axes[0] = x;
	virtualPad.axes[1] = y;
	bump();
}

// --- visibility ----------------------------------------------------------
function reevaluate() {
	const show = (forceShow || (wantsController && touchCapable())) && !realPadsPresent();
	if (root) root.style.display = show ? "block" : "none";
	setInjected(show);
	if (!show) resetState();
}

function resetState() {
	setStick(0, 0);
	for (let i = 0; i < BUTTON_COUNT; i++) setButton(i, false);
	resetKnob();
}

export function setVirtualControllerVisible(visible) {
	wantsController = !!visible;
	reevaluate();
}

// --- DOM / rendering -----------------------------------------------------
let knobEl = null;
let stickBaseEl = null;
const STICK_RADIUS = 56; // px travel radius for the knob

function resetKnob() {
	if (knobEl) knobEl.style.transform = "translate(-50%, -50%)";
}

function buildDom() {
	const style = document.createElement("style");
	style.textContent = `
		#myth-vpad { position: fixed; inset: 0; z-index: 5; display: none;
			pointer-events: none; touch-action: none;
			--pad: max(18px, env(safe-area-inset-bottom)); }
		#myth-vpad .ctl { position: absolute; pointer-events: auto; touch-action: none;
			user-select: none; -webkit-user-select: none; -webkit-tap-highlight-color: transparent; }
		#myth-vpad .stick-base { left: max(24px, env(safe-area-inset-left)); bottom: var(--pad);
			width: 140px; height: 140px; border-radius: 50%;
			background: rgba(20, 14, 34, 0.35); border: 2px solid rgba(236, 226, 203, 0.35); }
		#myth-vpad .stick-knob { position: absolute; left: 50%; top: 50%;
			width: 64px; height: 64px; border-radius: 50%; transform: translate(-50%, -50%);
			background: rgba(224, 211, 167, 0.55); border: 2px solid rgba(236, 226, 203, 0.7); }
		#myth-vpad .buttons { right: max(24px, env(safe-area-inset-right)); bottom: var(--pad);
			width: 168px; height: 168px; pointer-events: none; }
		#myth-vpad .btn { position: absolute; width: 64px; height: 64px; border-radius: 50%;
			pointer-events: auto; touch-action: none; user-select: none; -webkit-user-select: none;
			display: grid; place-items: center; font: 700 22px system-ui, sans-serif;
			color: #f5e9c8; background: rgba(47, 33, 63, 0.55); border: 2px solid rgba(224, 211, 167, 0.6); }
		#myth-vpad .btn.active { background: rgba(224, 211, 167, 0.75); color: #150e22; }
		#myth-vpad .btn.south { left: 52px; bottom: 0; }
		#myth-vpad .btn.east  { right: 0; bottom: 52px; }
		#myth-vpad .btn.west  { left: 0; bottom: 52px; }
		#myth-vpad .btn.north { left: 52px; bottom: 104px; }
	`;
	document.head.appendChild(style);

	root = document.createElement("div");
	root.id = "myth-vpad";

	stickBaseEl = document.createElement("div");
	stickBaseEl.className = "ctl stick-base";
	knobEl = document.createElement("div");
	knobEl.className = "stick-knob";
	stickBaseEl.appendChild(knobEl);
	root.appendChild(stickBaseEl);

	const buttons = document.createElement("div");
	buttons.className = "ctl buttons";
	// label, css class, standard button index
	for (const [label, cls, idx] of [["Y", "north", 3], ["X", "west", 2], ["B", "east", 1], ["A", "south", 0]]) {
		const el = document.createElement("div");
		el.className = `btn ${cls}`;
		el.textContent = label;
		bindButton(el, idx);
		buttons.appendChild(el);
	}
	root.appendChild(buttons);

	document.body.appendChild(root);
}

function bindButton(el, index) {
	const press = (e) => { e.preventDefault(); el.classList.add("active"); setButton(index, true); el.setPointerCapture?.(e.pointerId); };
	const release = (e) => { el.classList.remove("active"); setButton(index, false); };
	el.addEventListener("pointerdown", press);
	el.addEventListener("pointerup", release);
	el.addEventListener("pointercancel", release);
	el.addEventListener("lostpointercapture", release);
}

function bindStick() {
	let activeId = null;
	const update = (e) => {
		const rect = stickBaseEl.getBoundingClientRect();
		let dx = e.clientX - (rect.left + rect.width / 2);
		let dy = e.clientY - (rect.top + rect.height / 2);
		const len = Math.hypot(dx, dy);
		if (len > STICK_RADIUS) { dx = (dx / len) * STICK_RADIUS; dy = (dy / len) * STICK_RADIUS; }
		knobEl.style.transform = `translate(calc(-50% + ${dx}px), calc(-50% + ${dy}px))`;
		setStick(dx / STICK_RADIUS, dy / STICK_RADIUS);
	};
	stickBaseEl.addEventListener("pointerdown", (e) => {
		e.preventDefault();
		activeId = e.pointerId;
		stickBaseEl.setPointerCapture?.(e.pointerId);
		update(e);
	});
	stickBaseEl.addEventListener("pointermove", (e) => { if (e.pointerId === activeId) update(e); });
	const end = (e) => { if (e.pointerId !== activeId) return; activeId = null; setStick(0, 0); resetKnob(); };
	stickBaseEl.addEventListener("pointerup", end);
	stickBaseEl.addEventListener("pointercancel", end);
	stickBaseEl.addEventListener("lostpointercapture", end);
}

export function initialize() {
	installGamepadHook();
	buildDom();
	bindStick();
	// Hide/show the overlay when real controllers come and go.
	window.addEventListener("gamepadconnected", (e) => { if (!e.gamepad?.__mythVirtual) reevaluate(); });
	window.addEventListener("gamepaddisconnected", (e) => { if (!e.gamepad?.__mythVirtual) reevaluate(); });
	reevaluate();
}
