prepareReloadPrompt();
prepareFeedback();
const [{ dotnet }, FosterWebGPU, MythBrowserFmod, VirtualGamepad] =
  await Promise.all([
    import("./_framework/dotnet.js"),
    import("./_framework/Content/Web/foster-webgpu.js"),
    import("./FMOD/myth-fmod.js"),
    import("./virtual-gamepad.js"),
  ]);

globalThis.FosterWebGPU = FosterWebGPU;
globalThis.MythBrowserFmod = MythBrowserFmod;

// On-screen touch controller (web analog of the iOS GCVirtualController).
VirtualGamepad.initialize();

// Keep the canvas backing buffer's aspect matched to the CSS shell box (see index.html):
// 4:3 in portrait so the game cover-crops its 16:9 frame to reclaim vertical space, 16:9 in
// landscape, and correct at every size in between (so the frame is never squished). The
// managed side reads the (now narrower) swapchain size and crops the sides automatically; in
// portrait we also let a play-area press-and-hold un-crop.
//
// This runs on every animation frame rather than only on resize/orientation events because
// the managed Window constructor calls ResizeCanvas once at startup (to its 16:9 config size).
// We compare against the *actual* canvas size (getCanvasWidth/Height), not our own bookkeeping,
// so that managed override is detected and corrected on the next frame — otherwise a fresh
// portrait load stays squished (16:9 buffer stretched into the 4:3 box) until a resize event.
// Height is fixed (BASE_HEIGHT) and width is derived from the box aspect, so the internal
// resolution stays constant and we only touch the canvas when it actually diverges.
function setupCanvasAspectSync(FosterWebGPU, VirtualGamepad) {
  const BASE_HEIGHT = 720;
  const shell = document.querySelector("#game-shell");
  let appliedPortrait = null;

  const sync = () => {
    const rect = shell?.getBoundingClientRect();
    if (rect && rect.width > 0 && rect.height > 0) {
      const w = Math.max(1, Math.round(BASE_HEIGHT * (rect.width / rect.height)));
      if (FosterWebGPU.getCanvasWidth() !== w || FosterWebGPU.getCanvasHeight() !== BASE_HEIGHT)
        FosterWebGPU.resizeCanvas(w, BASE_HEIGHT); // resets the drawing buffer; only when diverged
    }
    const portrait = window.matchMedia("(orientation: portrait)").matches;
    if (portrait !== appliedPortrait) {
      appliedPortrait = portrait;
      VirtualGamepad.setPlayAreaZoomEnabled?.(portrait);
    }
    requestAnimationFrame(sync);
  };
  requestAnimationFrame(sync);
}

function isMobileBrowser() {
  if (navigator.userAgentData?.mobile === true) return true;
  if (/Android|iPhone|iPad|iPod|Mobile/i.test(navigator.userAgent)) return true;
  return (
    navigator.maxTouchPoints > 1 && /MacIntel|iPad/i.test(navigator.platform)
  );
}

try {
  await FosterWebGPU.initialize("#foster-canvas");
  setupCanvasAspectSync(FosterWebGPU, VirtualGamepad);

  const searchParams = new URLSearchParams(location.search);

  const debug = searchParams.get("debug") === "1";
  const webAudioDisabled = searchParams.get("audio") === "0";
  const room = searchParams.get("room")?.trim();

  const dotnetConfig = debug
    ? {
        environmentVariables: {
          MONO_LOG_LEVEL: "debug", //enable Mono VM detailed logging by
          MONO_LOG_MASK: "all", // categories, could be also gc,aot,type,...
        },
      }
    : {};

  // One byte-accurate meter for everything that downloads: the .NET runtime
  // resources, the game content, and the FMOD banks. The manifest supplies expected
  // sizes (built from the deployed files, so immune to Content-Encoding); streamed
  // fetches supply progress. The last 3% is FMOD init + runtime start.
  const startGate = prepareStartGate();
  const progress = createDownloadProgress((fraction) =>
    startGate.progress(fraction * 0.97),
  );
  const manifestPromise = loadAssetManifest().then((manifest) => {
    progress.expect([
      ...manifest.assets.map((entry) => [entry.path, entry.bytes]),
      ...Object.entries(manifest.framework).map(([name, bytes]) => [
        `_framework/${name}`,
        bytes,
      ]),
    ]);
    return manifest;
  });

  let runtime = await dotnet
    .withDiagnosticTracing(debug)
    .withDebugging(debug ? 1 : 0)
    .withConfig(dotnetConfig)
    .withResourceLoader(createResourceLoader(progress))
    .create();
  progress.completePrefix("_framework/");

  const { setModuleImports, runMain } = runtime;

  setModuleImports("FosterWebGPU", FosterWebGPU);
  setModuleImports("MythBrowserFmod", MythBrowserFmod);
  setModuleImports("MythOverlay", {
    setVirtualControllerVisible: VirtualGamepad.setVirtualControllerVisible,
    getAudioDisabled: () => webAudioDisabled,
    isMobileBrowser,
    loadSaveState: () => localStorage.getItem("myth.save.v1"),
    saveSaveState: (json) => localStorage.setItem("myth.save.v1", json),
    deleteSaveState: () => localStorage.removeItem("myth.save.v1"),
  });

  const manifest = await manifestPromise;
  const { bankFiles, bankData } = await preloadAssets(
    runtime.Module,
    manifest,
    progress,
  );
  progress.completePrefix("Assets/");
  runtime.Module.runtimeKeepalivePush?.();

  if (bankFiles.length > 0) {
    await MythBrowserFmod.initialize(bankFiles, bankData);
    MythBrowserFmod.startUpdateLoop(20);
  }

  globalThis.MythStartReady = true;
  startGate.ready();
  await startGate.wait();
  try {
    await runMain("Myth", room ? ["--play-room", room] : []);
  } catch (error) {
    const cleanExit =
      error?.name === "ExitStatus" &&
      (error.status === 0 || error.message?.includes("exit(0)"));
    if (!cleanExit) throw error;
  }
} catch (error) {
  console.error("Myth browser startup failed", error);
  console.error(error?.stack ?? error);
  showStartupFailure(error);
}

function showStartupFailure(error) {
  const gate = document.querySelector("#start-gate");
  const panel = document.querySelector("#startup-error");
  const title = document.querySelector("#startup-error-title");
  const message = document.querySelector("#startup-error-message");
  const help = document.querySelector("#startup-error-help");
  if (
    !(gate instanceof HTMLElement) ||
    !(panel instanceof HTMLElement) ||
    !(title instanceof HTMLElement) ||
    !(message instanceof HTMLElement) ||
    !(help instanceof HTMLElement)
  )
    return;

  const details = String(error?.message ?? error);
  const webGPUUnavailable =
    details.includes("WebGPU adapter was not available") ||
    (!("gpu" in navigator) && details.includes("WebGPU"));

  title.textContent = webGPUUnavailable
    ? "WebGPU isn’t available"
    : "The game couldn’t start";
  message.textContent = webGPUUnavailable
    ? "This game needs WebGPU, but this browser couldn’t provide a compatible GPU adapter."
    : "An unexpected error occurred while starting the browser build.";
  help.textContent = webGPUUnavailable
    ? "Try an up-to-date WebGPU-capable browser or another device."
    : "Try reloading the page or using another browser. Technical details were written to the developer console.";

  gate.hidden = false;
  gate.toggleAttribute("data-error", true);
  panel.hidden = false;
}

function prepareReloadPrompt() {
  const is_localhost = (window.location.hostname === "127.0.0.1" || window.location.hostname === "localhost");
  if (!is_localhost) return;

  const button = document.querySelector("#reload-button");
  if (!(button instanceof HTMLButtonElement) || !("EventSource" in window))
    return;

  button.addEventListener("click", () => location.reload());

  const events = new EventSource("/__myth/events");
  events.addEventListener("built", () => {
    button.hidden = false;
  });
  events.addEventListener("error", () => {
    button.hidden = true;
  });
}

function prepareFeedback() {
  const openButton = document.querySelector("#feedback-button");
  const dialog = document.querySelector("#feedback-dialog");
  const form = document.querySelector("#feedback-form");
  const closeButton = document.querySelector("#feedback-close");
  const message = document.querySelector("#feedback-message");
  const submitButton = document.querySelector("#feedback-submit");
  const status = document.querySelector("#feedback-status");
  if (
    !(openButton instanceof HTMLButtonElement) ||
    !(dialog instanceof HTMLDialogElement) ||
    !(form instanceof HTMLFormElement) ||
    !(closeButton instanceof HTMLButtonElement) ||
    !(message instanceof HTMLTextAreaElement) ||
    !(submitButton instanceof HTMLButtonElement) ||
    !(status instanceof HTMLElement)
  )
    return;

  let statusTimer;
  const setStatus = (text, error = false) => {
    clearTimeout(statusTimer);
    status.textContent = text;
    status.toggleAttribute("data-error", error);
  };

  openButton.addEventListener("click", () => {
    dialog.showModal();
    message.focus();
  });
  closeButton.addEventListener("click", () => dialog.close());
  dialog.addEventListener("close", () => openButton.blur());

  // Registered before Foster initializes, so keyboard events used to edit the
  // textarea do not also reach SDL's window-level game input listeners.
  const stopGameKeyboardInput = (event) => {
    if (dialog.open) event.stopImmediatePropagation();
  };
  window.addEventListener("keydown", stopGameKeyboardInput);
  window.addEventListener("keyup", stopGameKeyboardInput);

  message.addEventListener("input", () => {
    if (status.textContent === "Sent!") setStatus("");
  });

  form.addEventListener("submit", async (event) => {
    event.preventDefault();
    const feedback = message.value.trim();
    if (!feedback) {
      message.focus();
      return;
    }

    submitButton.disabled = true;
    submitButton.textContent = "Sending…";
    setStatus("");
    try {
      const response = await fetch("/api/feedback", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({
          message: feedback,
          website: new FormData(form).get("website"),
        }),
      });
      if (!response.ok) throw new Error(`Feedback request failed: ${response.status}`);

      message.value = "";
      setStatus("Sent!");
      statusTimer = setTimeout(() => setStatus(""), 3000);
    } catch (error) {
      console.error("Failed to send feedback", error);
      setStatus("Couldn't send — try again.", true);
    } finally {
      submitButton.disabled = false;
      submitButton.textContent = "Send anonymous feedback";
    }
  });
}

function prepareStartGate() {
  const gate = document.querySelector("#start-gate");
  const button = document.querySelector("#start-button");
  if (!(gate instanceof HTMLElement) || !(button instanceof HTMLButtonElement))
    return {
      progress: () => {},
      ready: () => {},
      wait: () => Promise.resolve(),
    };

  const setFill = (value) =>
    button.style.setProperty(
      "--p",
      `${Math.max(0, Math.min(1, value)) * 100}%`,
    );

  button.disabled = true;
  button.textContent = "Loading...";
  setFill(0);

  let ready = false;
  const wait = new Promise((resolve) => {
    const start = () => {
      button.textContent = "Starting...";
      button.disabled = true;
      button.style.opacity = 0.5;
      if (!ready) return;

      gate.hidden = true;
      setTimeout(resolve, 100);
    };

    button.addEventListener("click", start, { once: true });
  });

  return {
    progress: setFill,
    ready: () => {
      ready = true;
      button.disabled = false;
      button.textContent = "Start";
      setFill(1);
    },
    wait: () => wait,
  };
}

async function preloadAssets(module, manifest, progress) {
  const files = manifest.assets.map((entry) => entry.path);
  const preloadFiles = files.filter(shouldPreloadIntoDotnetFs);
  const bankFiles = files.filter(isFmodBankFile);

  // Everything downloads through the same streaming meter, in a bounded parallel
  // pool. Banks are kept as bytes for the FMOD bridge (which owns their filesystem);
  // everything else lands in the dotnet wasm FS. createWasmFile is synchronous, so
  // FS-path creation never interleaves between workers.
  const bankData = new Map();
  const downloads = [...preloadFiles, ...bankFiles];
  const CONCURRENCY = 8;
  let next = 0;
  const worker = async () => {
    while (next < downloads.length) {
      const file = downloads[next++];
      const response = trackResponseProgress(await fetch(file), progress, file);
      if (!response.ok)
        throw new Error(`Failed to fetch ${file}: ${response.status}`);
      const bytes = new Uint8Array(await response.arrayBuffer());
      progress.complete(file);
      if (isFmodBankFile(file)) bankData.set(file, bytes);
      else createWasmFile(module, file, bytes);
    }
  };
  await Promise.all(
    Array.from({ length: Math.min(CONCURRENCY, downloads.length) }, worker),
  );

  // Managed path checks expect the bank files to exist in the dotnet FS too, but
  // only as placeholders — the FMOD bridge holds the real bytes.
  for (const file of bankFiles) createWasmFile(module, file, new Uint8Array());

  console.log(
    `Preloaded ${preloadFiles.length} Myth asset files into wasm FS + ${bankFiles.length} FMOD banks.`,
  );
  return { files, bankFiles, bankData };
}

async function loadAssetManifest() {
  const response = await fetch("asset-manifest.json");
  if (!response.ok)
    throw new Error(`Failed to load asset manifest: ${response.status}`);

  const manifest = await response.json();
  if (!Array.isArray(manifest?.assets) || typeof manifest?.framework !== "object")
    throw new Error("Unexpected asset manifest format (expected {assets, framework}).");

  manifest.assets = manifest.assets.filter(
    (entry) =>
      typeof entry?.path === "string" && entry.path.startsWith("Assets/"),
  );
  return manifest;
}

// Shared byte meter for the whole boot download. The denominator is the manifest's
// expected sizes; the numerator is streamed bytes, clamped per item so a
// Content-Encoding surprise can't overshoot. complete()/completePrefix() snap items
// to their expected size, so anything the runtime skipped (cached, absent in this
// build) self-heals at phase boundaries and the bar always reaches the end.
function createDownloadProgress(onFraction) {
  const expected = new Map(); // key -> expected bytes
  const counted = new Map(); // key -> streamed bytes
  let shown = 0;

  const report = () => {
    let totalExpected = 0;
    let totalLoaded = 0;
    for (const [key, bytes] of expected) {
      totalExpected += bytes;
      totalLoaded += Math.min(counted.get(key) ?? 0, bytes);
    }
    if (totalExpected <= 0) return;
    const fraction = Math.min(totalLoaded / totalExpected, 1);
    if (fraction > shown) {
      shown = fraction;
      onFraction(fraction);
    }
  };

  return {
    expect(entries) {
      for (const [key, bytes] of entries)
        if (Number.isFinite(bytes) && bytes > 0) expected.set(key, bytes);
      report();
    },
    // Registers a fallback size (e.g. Content-Length) for a file the manifest
    // doesn't know; manifest sizes win.
    begin(key, fallbackBytes) {
      if (!expected.has(key) && Number.isFinite(fallbackBytes) && fallbackBytes > 0)
        expected.set(key, fallbackBytes);
    },
    count(key, bytes) {
      counted.set(key, (counted.get(key) ?? 0) + bytes);
      report();
    },
    complete(key) {
      if (expected.has(key)) counted.set(key, expected.get(key));
      report();
    },
    completePrefix(prefix) {
      for (const [key, bytes] of expected)
        if (key.startsWith(prefix)) counted.set(key, bytes);
      report();
    },
  };
}

// Wraps a fetch Response so its body bytes tick the progress meter as they stream.
// The counted bytes are post-transfer-decompression, matching the manifest's
// on-disk sizes.
function trackResponseProgress(response, progress, key) {
  if (!response.ok || !response.body) return response;
  progress.begin(key, Number(response.headers.get("Content-Length")) || 0);
  const counter = new TransformStream({
    transform(chunk, controller) {
      progress.count(key, chunk.byteLength);
      controller.enqueue(chunk);
    },
  });
  return new Response(response.body.pipeThrough(counter), {
    status: response.status,
    statusText: response.statusText,
    headers: response.headers,
  });
}

// Streams .NET boot resources (native wasm, webcil assemblies, icu data) through
// the shared meter via dotnet.js's loadBootResource hook. The dotnet.js module
// itself and the boot manifest must use the default loader (dotnet.js requires a
// URL string for those). Fetching here bypasses dotnet.js's integrity check — a
// custom Response is trusted as-is — which is both acceptable (same-origin HTTPS)
// and necessary: fetch() with integrity metadata buffers the entire body before
// releasing any bytes, which would defeat streaming progress.
function createResourceLoader(progress) {
  return (type, name, defaultUri, _integrity, _behavior) => {
    if (type === "dotnetjs" || type === "manifest") return undefined;
    return (async () => {
      const response = await fetch(defaultUri, { cache: "no-cache" });
      if (!response.ok)
        throw new Error(`Failed to fetch ${defaultUri}: ${response.status}`);
      return trackResponseProgress(response, progress, `_framework/${name}`);
    })();
  };
}

// The bundle is filtered at build time (csproj Content excludes decide what ships), so
// everything the manifest lists belongs in the wasm FS. The one exception is FMOD banks:
// the FMOD JS bridge streams those itself, and they only get empty placeholder files so
// path checks succeed.
function shouldPreloadIntoDotnetFs(path) {
  return !isFmodBankFile(path);
}

function isFmodBankFile(path) {
  return (
    path.startsWith("Assets/Sounds/FMOD/Build/Mobile/") &&
    path.endsWith(".bank")
  );
}

function createWasmFile(module, file, bytes) {
  const parts = file.split("/");
  const name = parts.pop();
  ensureWasmPath(module, parts);
  module.FS_createDataFile(
    `/${parts.join("/")}`,
    name,
    bytes,
    true,
    false,
    true,
  );
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
