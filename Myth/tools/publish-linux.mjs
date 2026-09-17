#!/usr/bin/env node
// Publishes Myth as a self-contained, unsigned Linux x64 build. Runs on macOS as well as
// Linux: ILC emits a linux-x64 object file on any host, and tools/linux-cross-cc.sh
// supplies the ELF linker and glibc sysroot the link needs. Cross-building therefore wants
// `brew install zig llvm`. Push the result to a Steam Deck with tools/deploy-deck.sh.
//
// Usage: node tools/publish-linux.mjs [--skip-fmod-mtime-check] [--skip-check-rooms]
//
// CI passes --skip-check-rooms: the hosted runner renders in software (minutes, not seconds),
// and the macOS publish already checks the same rooms and content.

import { execFileSync } from "node:child_process";
import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { checkFmodBankMtimes, copyStagedAssets, stageContent } from "./publish-common.mjs";

const world = process.env.MYTH_WORLD ?? "Myth";
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
run(process.execPath, ["tools/check-world-map-bake.mjs", world]);

// Content.pak carries the target's compiled shaders, so the SPIR-V has to exist before it
// is packed. The steps that follow run the game as a host tool, so they build for the host
// and pick up the .spv purely as content — the two shader flavors coexist in Compiled/.
run("dotnet", ["msbuild", "Myth.csproj", "-t:CompileShaders", "-p:ShaderPlatform=linux"]);

// --check-rooms creates real GPU pipelines, so it can only read a pak built for the host's
// shader flavor -- the Linux pak's SPIR-V is unusable on a macOS host. The content graph is
// target-independent (only the Shaders/Compiled extension differs), so a host-flavored pak
// validates the same rooms, actors and neural weights the Linux pak carries. Cross-building
// without this is how a room referencing an uncollected NCA model reached the Deck.
const checkRooms = !process.argv.includes("--skip-check-rooms");
const hostTarget = process.platform === "linux" ? "linux" : "mac";
const [stagedAssets, hostAssets = stagedAssets] = stageContent(root, checkRooms && hostTarget !== "linux" ? ["linux", hostTarget] : ["linux"], world);
if (checkRooms)
  run("dotnet", ["run", "--project", "Myth.csproj", "-p:MythTestShip=true", "--",
    "--check-rooms",
    "--content-pak", path.join(hostAssets, "Content.pak"),
    "--asset-pack",
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
// publish also drops Myth.dbg (26 MB of symbols); it stays behind. Timestamps carry over so
// deploy-deck.sh only resends what actually changed.
const copy = (src, dest) => fs.cpSync(src, dest, { preserveTimestamps: true });
fs.rmSync(gameDir, { recursive: true, force: true });
for (const entry of fs.readdirSync(publishDir))
  if (entry === "Myth" || entry.includes(".so"))
    copy(path.join(publishDir, entry), path.join(gameDir, entry));
copy(acknowledgements, path.join(gameDir, "ACKNOWLEDGEMENTS.txt"));
copyStagedAssets(stagedAssets, path.join(gameDir, "Assets"));

console.log(`\nBuilt unsigned Linux x64 release: ${gameDir}`);
