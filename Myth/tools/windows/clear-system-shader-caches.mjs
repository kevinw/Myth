#!/usr/bin/env node
// Clears the Windows D3D12 driver/OS shader caches so the next launch recompiles shaders cold.
//
// Why: the ~10s stall entering a neural room (e.g. neural_duck) is the GPU driver compiling the
// NCT compute DXIL to native ISA. The driver caches that to disk, so it only stalls on a cold
// cache. To verify the boot warmup (Manager's "Loading…" screen) actually helps, clear the cache
// and compare: `Myth.exe --no-warmup` (stall on room entry) vs `Myth.exe` (stall on the loading
// screen, room entry smooth).
//
// Usage:
//   node tools/windows/clear-system-shader-caches.mjs            # clear
//   node tools/windows/clear-system-shader-caches.mjs --dry-run  # report sizes only
//
// Note: these caches are shared across ALL apps, so other games will also recompile their shaders
// once on their next launch (a harmless one-time first-run stutter). Close Myth (and other 3D
// apps) first — files held open by a running app are skipped.

import fs from "node:fs";
import path from "node:path";

if (process.platform !== "win32") {
  console.error("This tool only applies to Windows D3D12 shader caches.");
  process.exit(1);
}

const dryRun = process.argv.includes("--dry-run");
const localAppData = process.env.LOCALAPPDATA;
if (!localAppData) {
  console.error("LOCALAPPDATA is not set; cannot locate the shader caches.");
  process.exit(1);
}

// D3D12 shader caches only. NVIDIA\GLCache and NVIDIA\DXCache differ: GLCache is OpenGL, which
// SDL_gpu does not use on Windows, so it is intentionally left alone.
const cacheDirs = [
  "NVIDIA\\DXCache", // NVIDIA D3D12 compiled-shader cache (the big one on this machine)
  "D3DSCache", // OS-level DirectX shader cache
  "AMD\\DxCache",
  "AMD\\DXCache",
  "Intel\\ShaderCache",
].map((rel) => path.join(localAppData, rel));

function dirSizeBytes(dir) {
  let total = 0;
  for (const entry of fs.readdirSync(dir, { withFileTypes: true })) {
    const full = path.join(dir, entry.name);
    try {
      if (entry.isDirectory()) total += dirSizeBytes(full);
      else total += fs.statSync(full).size;
    } catch {
      // vanished or locked mid-walk; ignore
    }
  }
  return total;
}

// Removes the contents of a directory but keeps the directory itself (the driver expects it to
// exist). Returns the number of top-level entries it could not remove (typically locked by a
// running app).
function clearDirContents(dir) {
  let failed = 0;
  for (const entry of fs.readdirSync(dir)) {
    try {
      fs.rmSync(path.join(dir, entry), { recursive: true, force: true });
    } catch {
      failed++;
    }
  }
  return failed;
}

const mb = (bytes) => (bytes / 1024 / 1024).toFixed(1).padStart(8) + " MB";

let totalFreed = 0;
let anyFound = false;

for (const dir of cacheDirs) {
  if (!fs.existsSync(dir)) continue;
  anyFound = true;

  const before = dirSizeBytes(dir);
  if (dryRun) {
    console.log(`${mb(before)}  ${dir}`);
    continue;
  }

  const failed = clearDirContents(dir);
  const after = dirSizeBytes(dir);
  totalFreed += before - after;
  const suffix = failed > 0 ? `  (${failed} entr${failed === 1 ? "y" : "ies"} locked — is an app using the GPU?)` : "";
  console.log(`${mb(before - after)}  ${dir}${suffix}`);
}

if (!anyFound) {
  console.log("No known D3D12 shader cache directories found — nothing to clear.");
} else if (dryRun) {
  console.log("\nDry run: nothing was deleted.");
} else {
  console.log(`\nCleared ${mb(totalFreed).trim()}. The next launch will recompile shaders cold.`);
}
