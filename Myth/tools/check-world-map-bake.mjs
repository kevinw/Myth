#!/usr/bin/env node

import { execFileSync } from "node:child_process";
import { createHash } from "node:crypto";
import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
const world = process.argv[2]?.trim();
if (!world || process.argv.length !== 3)
  throw new Error("Usage: node tools/check-world-map-bake.mjs <world>");
if (/[\\/]/.test(world) || !fs.existsSync(path.join(root, "Assets", "Rooms", world)))
  throw new Error(`Unknown world '${world}'.`);
// Keep in sync with WorldMap.ImageFile/BoundsFile and Manager.WriteWorldMapBake.
const maps = path.join(root, "Assets", "Packed", "Visuals", "world-maps");
const image = path.join(maps, `${world}.qoi`);
const bounds = path.join(maps, `${world}.json`);
const fingerprintFile = path.join(maps, `${world}.inputs.sha256`);

// Conservative list of inputs which can change a baked room's appearance. Generated packs,
// compiled shaders, audio, editor UI, and gameplay-only systems deliberately stay out.
const inputs = [
  "Assets/Actors",
  "Assets/NCA",
  "Assets/Particles",
  "Assets/Rigs",
  `Assets/Rooms/${world}`,
  "Assets/Shaders",
  "Assets/Sources/Sprites",
  "Assets/Sources/Surfaces",
  "Assets/Sources/Textures",
  "Assets/Sources/Tilesets",
  "Assets/Sprites",
  "Source/Actors",
  "Source/Assets",
  "Source/Lighting",
  "Source/Depths.cs",
  "Source/Factory.cs",
  "Source/Game.Rendering.cs",
  "Source/GameView.cs",
  "Source/DevTools/Manager.WorldMapBake.cs",
  "../Kit",
  "../Foster/Framework/Graphics",
  "../Foster/Framework/Images",
  "../Foster/Framework/Content/Shaders",
  "tools/check-world-map-bake.mjs",
];

const files = [];
function collect(relative) {
  const absolute = path.resolve(root, relative);
  if (!fs.existsSync(absolute)) return;
  const stat = fs.statSync(absolute);
  if (stat.isFile()) {
    files.push(absolute);
    return;
  }
  for (const entry of fs.readdirSync(absolute, { withFileTypes: true })) {
    if (entry.name === "bin" || entry.name === "obj" || entry.name === "Compiled" || entry.name.startsWith(".")) continue;
    collect(path.join(relative, entry.name));
  }
}

for (const input of inputs) collect(input);
// Keys and contents are normalized (/ separators, LF line endings) so every OS's checkout of
// a commit fingerprints the same; CI bakes once on the Mac and the other platforms reuse it.
const key = (file) => path.relative(root, file).replaceAll(path.sep, "/");
files.sort((a, b) => key(a) < key(b) ? -1 : key(a) > key(b) ? 1 : 0);

const hash = createHash("sha256");
hash.update("myth-world-map-inputs-v2\0");
hash.update(`${world}\0`);
for (const file of files) {
  hash.update(key(file));
  hash.update("\0");
  hash.update(fs.readFileSync(file, "latin1").replaceAll("\r\n", "\n"), "latin1");
  hash.update("\0");
}
const fingerprint = hash.digest("hex");
const previous = fs.existsSync(fingerprintFile) ? fs.readFileSync(fingerprintFile, "utf8").trim() : "";

if (fs.existsSync(image) && fs.existsSync(bounds) && previous === fingerprint) {
  console.log(`World map bake for ${world} is current.`);
  process.exit(0);
}

console.log(previous ? "World map inputs changed; rebaking." : "World map bake or fingerprint is missing; baking.");
execFileSync("dotnet", ["run", "--project", "Myth.csproj", "--", "--bake-world-map", "--world", world], {
  cwd: root,
  stdio: "inherit",
});

if (!fs.existsSync(image) || !fs.existsSync(bounds))
  throw new Error(`World map bake completed without producing ${image} and ${bounds}.`);

fs.writeFileSync(fingerprintFile, `${fingerprint}\n`);
console.log("World map bake updated.");
