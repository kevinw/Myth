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
import { checkFmodBankMtimes, copyDesktopFmodBanks } from "./publish-common.mjs";

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

// --check-rooms creates real GPU pipelines, so it can only read a zip built for the host's
// shader flavor -- the Linux zip's SPIR-V is unusable on a macOS host. The content graph is
// target-independent (only the Shaders/Compiled extension differs), so a host-flavored zip
// validates the same rooms, actors and neural weights the Linux zip carries. Cross-building
// without this is how a room referencing an uncollected NCA model reached the Deck.
const hostTarget = process.platform === "linux" ? "linux" : "mac";
const checkZip = hostTarget === "linux" ? contentZip : path.join(outRoot, "Content-check.zip");
if (checkZip !== contentZip)
  run("dotnet", ["run", "--project", "Myth.csproj", "--",
    "--build-content-zip", checkZip,
    "--content-target", hostTarget,
    "--content-world", "Myth",
  ]);
run("dotnet", ["run", "--project", "Myth.csproj", "-p:MythTestShip=true", "--",
  "--check-rooms",
  "--content-zip", checkZip,
  "--asset-pack",
]);
// Only reached when the check passed (run throws otherwise), so a failure leaves the zip
// on disk to inspect. Never the shipping zip -- on a Linux host checkZip is contentZip.
if (checkZip !== contentZip)
  fs.rmSync(checkZip, { force: true });

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
fs.mkdirSync(path.join(gameDir, "Assets"), { recursive: true });
for (const entry of fs.readdirSync(publishDir))
  if (entry === "Myth" || entry.includes(".so"))
    copy(path.join(publishDir, entry), path.join(gameDir, entry));
copy(contentZip, path.join(gameDir, "Assets", "Content.zip"));
copy(acknowledgements, path.join(gameDir, "ACKNOWLEDGEMENTS.txt"));
copyDesktopFmodBanks(root, path.join(gameDir, "Assets"));

console.log(`\nBuilt unsigned Linux x64 release: ${gameDir}`);
