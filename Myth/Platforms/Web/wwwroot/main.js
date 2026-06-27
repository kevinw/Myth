prepareReloadPrompt();
const [{ dotnet }, FosterWebGPU, FosterBrowserFmod, VirtualGamepad] =
  await Promise.all([
    import("./_framework/dotnet.js"),
    import("./_framework/Content/Web/foster-webgpu.js"),
    import("./_framework/Content/Web/foster-fmod.js"),
    import("./virtual-gamepad.js"),
  ]);

globalThis.FosterWebGPU = FosterWebGPU;
globalThis.FosterBrowserFmod = FosterBrowserFmod;

// On-screen touch controller (web analog of the iOS GCVirtualController).
VirtualGamepad.initialize();

function isMythCleanExitStatus(error) {
  return (
    error?.name === "ExitStatus" &&
    (error.status === 0 || error.message?.includes("exit(0)"))
  );
}

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

  const dotnetConfig = debug
    ? {
        environmentVariables: {
          MONO_LOG_LEVEL: "debug", //enable Mono VM detailed logging by
          MONO_LOG_MASK: "all", // categories, could be also gc,aot,type,...
        },
      }
    : {};

  let runtime = await dotnet
    .withDiagnosticTracing(debug)
    .withDebugging(debug ? 1 : 0)
    .withConfig(dotnetConfig)
    .create();

  const { setModuleImports, runMain } = runtime;

  setModuleImports("FosterWebGPU", FosterWebGPU);
  setModuleImports("FosterBrowserFmod", FosterBrowserFmod);
  setModuleImports("MythOverlay", {
    setVirtualControllerVisible: VirtualGamepad.setVirtualControllerVisible,
    getAudioDisabled: () => webAudioDisabled,
    isMobileBrowser,
  });
  const startGate = prepareStartGate();
  startGate.progress(0.05);
  const assetFiles = await preloadAssets(runtime.Module, (loaded, total) => {
    startGate.progress(0.05 + (0.75 * loaded) / Math.max(total, 1));
  });
  runtime.Module.runtimeKeepalivePush?.();

  const bankFiles = assetFiles.filter(isFmodBankFile);
  if (bankFiles.length > 0) {
    startGate.progress(0.85);
    await FosterBrowserFmod.initialize(bankFiles);
    FosterBrowserFmod.startUpdateLoop(20);
  }

  globalThis.MythStartReady = true;
  startGate.ready();
  await startGate.wait();
  try {
    await runMain("Myth", []);
  } catch (error) {
    if (!isMythCleanExitStatus(error)) throw error;
  }
} catch (error) {
  console.error("Myth browser startup failed", error);
  console.error(error?.stack ?? error);
}

function prepareReloadPrompt() {
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

async function preloadAssets(module, onProgress) {
  const files = await loadAssetManifest();
  const hasVisualAssetPack = files.includes(
    "Assets/Packed/Visuals/manifest.bin",
  );
  const preloadFiles = files.filter((file) =>
    shouldPreloadIntoDotnetFs(file, hasVisualAssetPack),
  );
  const fmodBankFiles = files.filter(isFmodBankFile);
  onProgress?.(0, preloadFiles.length);

  // Fetch in parallel with a bounded pool: the loop is fetch-bound, so running
  // several at once is much faster, but unbounded would flood the connection
  // pool and hold every file's bytes in memory at once. Each worker pulls the
  // next index, fetches, and writes it straight into the wasm FS (createWasmFile
  // is synchronous, so the FS-path creation never interleaves). done is a shared
  // counter so progress stays monotonic regardless of completion order.
  const CONCURRENCY = 25;
  let next = 0;
  let done = 0;
  const worker = async () => {
    while (next < preloadFiles.length) {
      const file = preloadFiles[next++];
      const bytes = new Uint8Array(await (await fetch(file)).arrayBuffer());
      createWasmFile(module, file, bytes);
      onProgress?.(++done, preloadFiles.length);
    }
  };
  await Promise.all(
    Array.from({ length: Math.min(CONCURRENCY, preloadFiles.length) }, worker),
  );

  for (const file of fmodBankFiles)
    createWasmFile(module, file, new Uint8Array());

  console.log(
    `Preloaded ${preloadFiles.length} Myth asset files into wasm FS.`,
  );
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

function shouldPreloadIntoDotnetFs(path, hasVisualAssetPack) {
  if (isFmodBankFile(path)) return false;

  if (hasVisualAssetPack) {
    if (path.startsWith("Assets/Sprites/")) return false;
    if (path.startsWith("Assets/Tilesets/")) return false;
    if (path.startsWith("Assets/Textures/")) return false;
    // Fonts (dogica/m3x6, and Myna for ImGui) are rasterized from these TTFs at
    // runtime via SpriteFont/Font, so they must stay in the wasm FS — don't exclude.
    if (path === "Assets/inbetweens.json") return false;
    if (path === "Assets/anim_transitions.json") return false;
  }

  return true;
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
