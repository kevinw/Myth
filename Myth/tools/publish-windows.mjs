#!/usr/bin/env node
// Publishes Myth as a self-contained, unsigned Windows x64 build.

import { execFileSync } from "node:child_process";
import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
const outRoot = path.join(root, "bin", "publish");
const publishDir = path.join(outRoot, "win-x64");
const gameDir = path.join(outRoot, "Myth");

function run(cmd, args) {
  console.log(`\n> ${cmd} ${args.join(" ")}`);
  execFileSync(cmd, args, { cwd: root, stdio: "inherit" });
}

const contentZip = path.join(outRoot, "Content.zip");
run("dotnet", ["run", "--project", "Myth.csproj", "-p:MythDesktopOnly=true", "-p:ShaderPlatform=windows", "--",
  "--build-asset-pack", "Assets/Packed/Visuals",
  "--build-content-zip", contentZip,
  "--content-target", "windows",
  "--content-world", "Myth",
]);
run("dotnet", [
  "publish", "Myth.csproj",
  "-c", "Release",
  "-r", "win-x64",
  "--self-contained",
  "-p:MythShip=true",
  "-p:MythDesktopOnly=true",
  "-p:ShaderPlatform=windows",
  "-o", publishDir,
]);

fs.rmSync(gameDir, { recursive: true, force: true });
fs.mkdirSync(path.join(gameDir, "Assets"), { recursive: true });
for (const entry of fs.readdirSync(publishDir))
  if (entry.endsWith(".exe") || entry.endsWith(".dll"))
    fs.copyFileSync(path.join(publishDir, entry), path.join(gameDir, entry));
fs.copyFileSync(contentZip, path.join(gameDir, "Assets", "Content.zip"));

const assetsRoot = path.join(root, "Assets");
for (const entry of fs.readdirSync(assetsRoot, { recursive: true, withFileTypes: true })) {
  if (!entry.isFile()) continue;
  const rel = path.relative(assetsRoot, path.join(entry.parentPath, entry.name));
  if (rel.split(path.sep).some((part) => part.startsWith(".")) ||
      !rel.startsWith(path.join("Sounds", "FMOD", "Build", "Desktop") + path.sep) ||
      !rel.endsWith(".bank")) continue;
  const dest = path.join(gameDir, "Assets", rel);
  fs.mkdirSync(path.dirname(dest), { recursive: true });
  fs.copyFileSync(path.join(assetsRoot, rel), dest);
}

console.log(`\nBuilt unsigned Windows x64 release: ${gameDir}`);
