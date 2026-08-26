#!/usr/bin/env node
// Publishes Myth as a self-contained, unsigned Linux x64 build. Runs on macOS as well as
// Linux: ILC emits a linux-x64 object file on any host, and tools/linux-cross-cc.sh
// supplies the ELF linker and glibc sysroot the link needs. Cross-building therefore wants
// `brew install zig llvm`. Push the result to a Steam Deck with tools/deploy-deck.sh.
//
// Usage: node tools/publish-linux.mjs [--skip-fmod-mtime-check]

import { execFileSync } from "node:child_process";
import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { checkFmodBankMtimes } from "./publish-common.mjs";

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
const outRoot = path.join(root, "bin", "publish");
const publishDir = path.join(outRoot, "linux-x64");
const gameDir = path.join(outRoot, "Myth-linux");
const acknowledgements = path.join(root, "ACKNOWLEDGEMENTS.txt");

function run(cmd, args) {
  console.log(`\n> ${cmd} ${args.join(" ")}`);
  execFileSync(cmd, args, { cwd: root, stdio: "inherit" });
}

checkFmodBankMtimes(root, "Desktop");
run(process.execPath, ["tools/check-world-map-bake.mjs"]);

// Content.zip carries the target's compiled shaders, so the SPIR-V has to exist before it
// is packed. The steps that follow run the game as a host tool, so they build for the host
// and pick up the .spv purely as content — the two shader flavors coexist in Compiled/.
run("dotnet", ["msbuild", "Myth.csproj", "-t:CompileShaders", "-p:ShaderPlatform=linux"]);

const contentZip = path.join(outRoot, "Content.zip");
run("dotnet", ["run", "--project", "Myth.csproj", "--",
  "--build-asset-pack", "Assets/Packed/Visuals",
  "--build-content-zip", contentZip,
  "--content-target", "linux",
  "--content-world", "Myth",
]);

run("dotnet", [
  "publish", "Myth.csproj",
  "-c", "Release",
  "-r", "linux-x64",
  "--self-contained",
  "-p:MythShip=true",
  "-p:ShaderPlatform=linux",
  "-o", publishDir,
]);

// The game dir is the executable, the shared libraries it dlopens, and content. The AOT
// publish also drops Myth.dbg (26 MB of symbols); it stays behind.
fs.rmSync(gameDir, { recursive: true, force: true });
fs.mkdirSync(path.join(gameDir, "Assets"), { recursive: true });
for (const entry of fs.readdirSync(publishDir))
  if (entry === "Myth" || entry.includes(".so"))
    fs.copyFileSync(path.join(publishDir, entry), path.join(gameDir, entry));
fs.chmodSync(path.join(gameDir, "Myth"), 0o755);
fs.copyFileSync(contentZip, path.join(gameDir, "Assets", "Content.zip"));
fs.copyFileSync(acknowledgements, path.join(gameDir, "ACKNOWLEDGEMENTS.txt"));

// FMOD opens banks by real path, so they ship as loose files rather than in the zip.
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

console.log(`\nBuilt unsigned Linux x64 release: ${gameDir}`);
