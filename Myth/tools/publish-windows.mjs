#!/usr/bin/env node
// Publishes Myth as a self-contained, unsigned Windows x64 build.
// Usage: node tools/publish-windows.mjs [--skip-fmod-mtime-check]

import { execFileSync } from "node:child_process";
import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { checkFmodBankMtimes, copyDesktopFmodBanks } from "./publish-common.mjs";

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

const contentZip = path.join(outRoot, "Content.zip");
run("dotnet", ["run", "--project", "Myth.csproj", "-p:ShaderPlatform=windows", "--",
  "--build-asset-pack", "Assets/Packed/Visuals",
  "--build-content-zip", contentZip,
  "--content-target", "windows",
  "--content-world", world,
]);
run("dotnet", ["run", "--project", "Myth.csproj", "-p:ShaderPlatform=windows", "-p:MythTestShip=true", "--",
  "--check-rooms",
  "--content-zip", contentZip,
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
fs.mkdirSync(path.join(gameDir, "Assets"), { recursive: true });
for (const entry of fs.readdirSync(publishDir))
  if (entry.endsWith(".exe") || entry.endsWith(".dll"))
    fs.copyFileSync(path.join(publishDir, entry), path.join(gameDir, entry));
fs.copyFileSync(contentZip, path.join(gameDir, "Assets", "Content.zip"));
fs.copyFileSync(acknowledgements, path.join(gameDir, "ACKNOWLEDGEMENTS.txt"));

copyDesktopFmodBanks(root, path.join(gameDir, "Assets"));

console.log(`\nBuilt unsigned Windows x64 release: ${gameDir}`);
