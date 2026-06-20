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
const STICK_RADIUS = 45; // px travel radius for the knob (stick scaled to 80%)

function resetKnob() {
	if (knobEl) knobEl.style.transform = "translate(-50%, -50%)";
}

function buildDom() {
	const style = document.createElement("style");
	style.textContent = `
		#myth-vpad { inset: 0; z-index: 5; display: none;
			pointer-events: none;
			touch-action: manipulation;
			--stick: 112px; --knob: 51px;
			--btn: 46px;       /* visible button circle */
			--hit: 51px;       /* (larger) touch area around it */
			--cluster: 143px;  /* button diamond box; taller so circles don't overlap */
			--pad: max(18px, env(safe-area-inset-bottom));
			--mono: ui-monospace, "SF Mono", "Cascadia Code", Menlo, Consolas, monospace; }
		#myth-vpad .ctl { position: absolute; pointer-events: auto; touch-action: none;
			user-select: none; -webkit-user-select: none; -webkit-tap-highlight-color: transparent;
			-webkit-touch-callout: none; }
		#myth-vpad .stick-base { left: max(24px, env(safe-area-inset-left)); bottom: var(--pad);
			width: var(--stick); height: var(--stick); border-radius: 50%;
			background: rgba(20, 14, 34, 0.35); border: 2px solid rgba(236, 226, 203, 0.35); }
		#myth-vpad .stick-knob { position: absolute; left: 50%; top: 50%;
			width: var(--knob); height: var(--knob); border-radius: 50%; transform: translate(-50%, -50%);
			background: rgba(224, 211, 167, 0.55); border: 2px solid rgba(236, 226, 203, 0.7); }
		/* Each button is a large transparent hit area (.btn) holding a smaller
		   visible circle (.cap), so the pushable radius exceeds the visual one. */
		#myth-vpad .buttons { right: max(24px, env(safe-area-inset-right)); bottom: var(--pad);
			width: var(--cluster); height: var(--cluster); pointer-events: none; }
		#myth-vpad .btn { position: absolute; width: var(--hit); height: var(--hit);
			pointer-events: auto; touch-action: none; user-select: none; -webkit-user-select: none;
			-webkit-touch-callout: none; background: transparent; border: 0;
			display: grid; place-items: center; }
		#myth-vpad .btn .cap { width: var(--btn); height: var(--btn); border-radius: 50%;
			pointer-events: none; /* decorative only; touches hit .btn (touch-action:none) */
			display: grid; place-items: center;
			font-weight: 700; font-size: calc(var(--btn) * 0.45); font-family: var(--mono);
			color: #f5e9c8; background: rgba(47, 33, 63, 0.55); border: 2px solid rgba(224, 211, 167, 0.6); }
		#myth-vpad .btn.active .cap { background: rgba(224, 211, 167, 0.75); color: #150e22; }
		#myth-vpad .btn.north { top: 0; left: 50%; transform: translateX(-50%); }
		#myth-vpad .btn.south { bottom: 0; left: 50%; transform: translateX(-50%); }
		#myth-vpad .btn.west  { left: 0; top: 50%; transform: translateY(-50%); }
		#myth-vpad .btn.east  { right: 0; top: 50%; transform: translateY(-50%); }
	`;
	document.head.appendChild(style);

	root = document.createElement("div");
	root.id = "myth-vpad";

	// iOS Safari pops the text-selection magnifier ("loupe") on double-tap-and-hold
	// even with user-select/touch-callout: none. That gesture lives on the touch
	// layer, so preventing the default on POINTER events (as the button handlers do)
	// doesn't stop it — only a non-passive touchstart preventDefault does. Scope it
	// to the overlay so the start button's click elsewhere is unaffected. Pointer
	// events (the actual gamepad input) still fire; only the loupe/zoom is cancelled.
	root.addEventListener("touchstart", (e) => e.preventDefault(), { passive: false });

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
		const cap = document.createElement("div");
		cap.className = "cap";
		cap.textContent = label;
		el.appendChild(cap);
		bindButton(el, idx);
		buttons.appendChild(el);
	}
	root.appendChild(buttons);

	document.body.appendChild(root);
}

function bindButton(el, index) {
	const press = (e) => { e.preventDefault(); el.classList.add("active"); setButton(index, true); el.setPointerCapture?.(e.pointerId); };
  const release = (e) => { e.preventDefault();  el.classList.remove("active"); setButton(index, false); };
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

// iOS Safari ignores user-scalable=no and is unreliable about touch-action
// suppressing *double-tap* zoom, so rapidly tapping a control (e.g. double
// jump) zooms the page into the buttons. Cancel it by preventing the default on
// a second touchend within the double-tap window. Single taps are untouched, so
// the start button's click still works, and pointer events (gamepad input) are
// unaffected — only the synthesized zoom/click is suppressed.
function installDoubleTapZoomGuard() {
	let lastTouchEnd = 0;
	document.addEventListener("touchend", (e) => {
		const now = Date.now();
		if (now - lastTouchEnd <= 300)
			e.preventDefault();
		lastTouchEnd = now;
	}, { passive: false });
}

export function initialize() {
	installDoubleTapZoomGuard();
	installGamepadHook();
	buildDom();
	bindStick();
	// Hide/show the overlay when real controllers come and go.
	window.addEventListener("gamepadconnected", (e) => { if (!e.gamepad?.__mythVirtual) reevaluate(); });
	window.addEventListener("gamepaddisconnected", (e) => { if (!e.gamepad?.__mythVirtual) reevaluate(); });
	reevaluate();
}
