#!/usr/bin/env node
// Publishes Myth as a self-contained, unsigned Windows x64 build. Runs on Windows, or cross-builds
// on macOS (Myth.csproj, tools/rig-baker/build-runtime.mjs) after a one-time host setup:
//   brew install llvm lld cmake ninja
//   cargo install xwin && xwin --accept-license splat --output ~/win-sdk   (or set MYTH_WIN_SDK_ROOT)
// Usage: node tools/publish-windows.mjs [--skip-fmod-mtime-check] [--skip-check-rooms]
//
// CI passes --skip-check-rooms: the macOS publish already checks the same rooms and content.

import { execFileSync } from "node:child_process";
import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { checkFmodBankMtimes, copyStagedAssets, stageContent } from "./publish-common.mjs";

const world = process.env.MYTH_WORLD ?? "Myth";
const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
const outRoot = path.join(root, "bin", "publish");
const publishDir = path.join(outRoot, "win-x64");
const gameDir = path.join(outRoot, "Myth");
const acknowledgements = path.join(root, "ACKNOWLEDGEMENTS.txt");

function run(cmd, args) {
  console.log(`\n> ${cmd} ${args.join(" ")}`);
  execFileSync(cmd, args, { cwd: root, stdio: "inherit" });
}

checkFmodBankMtimes(root, "Desktop");
run(process.execPath, ["tools/check-world-map-bake.mjs", world]);

// Content.pak carries the target's DXIL, so compile it before packing; the host-flavored shaders
// the staging and room-check runs use coexist with it in Compiled/.
run("dotnet", ["msbuild", "Myth.csproj", "-t:CompileShaders", "-p:ShaderPlatform=windows"]);

// --check-rooms creates real GPU pipelines, so off Windows it validates a host-flavored pak with the
// same content graph (see publish-linux.mjs).
const checkRooms = !process.argv.includes("--skip-check-rooms");
const hostTarget = { win32: "windows", linux: "linux" }[process.platform] ?? "mac";
const [stagedAssets, hostAssets = stagedAssets] = stageContent(root, checkRooms && hostTarget !== "windows" ? ["windows", hostTarget] : ["windows"], world);
if (checkRooms)
  run("dotnet", ["run", "--project", "Myth.csproj", "-p:MythTestShip=true", "--",
    "--check-rooms",
    "--content-pak", path.join(hostAssets, "Content.pak"),
    "--asset-pack",
  ]);
run("dotnet", [
  "publish", "Myth.csproj",
  "-c", "Release",
  "-r", "win-x64",
  "--self-contained",
  "-p:MythShip=true",
  "-p:ShaderPlatform=windows",
  "-o", publishDir,
]);

fs.rmSync(gameDir, { recursive: true, force: true });
fs.mkdirSync(gameDir, { recursive: true });
for (const entry of fs.readdirSync(publishDir))
  if (entry.endsWith(".exe") || entry.endsWith(".dll"))
    fs.copyFileSync(path.join(publishDir, entry), path.join(gameDir, entry));
fs.copyFileSync(acknowledgements, path.join(gameDir, "ACKNOWLEDGEMENTS.txt"));
copyStagedAssets(stagedAssets, path.join(gameDir, "Assets"));

console.log(`\nBuilt unsigned Windows x64 release: ${gameDir}`);
