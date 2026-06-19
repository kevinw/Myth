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

function isMobileBrowser() {
  if (navigator.userAgentData?.mobile === true) return true;
  if (/Android|iPhone|iPad|iPod|Mobile/i.test(navigator.userAgent)) return true;
  return (
    navigator.maxTouchPoints > 1 && /MacIntel|iPad/i.test(navigator.platform)
  );
}

try {
  await FosterWebGPU.initialize("#foster-canvas");

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

  for (let i = 0; i < preloadFiles.length; i++) {
    const file = preloadFiles[i];
    const bytes = new Uint8Array(await (await fetch(file)).arrayBuffer());
    createWasmFile(module, file, bytes);
    onProgress?.(i + 1, preloadFiles.length);
  }

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
    if (path.startsWith("Assets/Fonts/")) return false;
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
