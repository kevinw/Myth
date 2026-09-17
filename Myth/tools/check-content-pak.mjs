#!/usr/bin/env node
// Audits a built Content.pak against an explicit allowlist, so a content bug (authoring
// sources, another world's rooms, the wrong shader flavor, LFS pointers, stray build output)
// fails the build instead of shipping. A new content folder means adding it to `allowed`.
//
// Usage: node tools/check-content-pak.mjs <Content.pak> --target <mac|ios|windows|linux|browser>
//          --world <id> [--unscramble <out.zip>]

import fs from "node:fs";
import path from "node:path";
import zlib from "node:zlib";
import { fileURLToPath } from "node:url";

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
const args = process.argv.slice(2);
const option = (name) => {
  const i = args.indexOf(name);
  return i >= 0 ? args[i + 1] : undefined;
};
const pakPath = args[0];
const target = option("--target");
const world = option("--world");
const shaderExtension = { mac: "msl", ios: "msl", windows: "dxil", linux: "spv", browser: "wgsl" }[target];
if (!pakPath || !shaderExtension || !world)
  throw new Error("Usage: node tools/check-content-pak.mjs <Content.pak> --target <mac|ios|windows|linux|browser> --world <id> [--unscramble <out.zip>]");

const allowed = [
  [/^world\.json$/],
  [/^Actors\/[^/]+$/, "json"],
  [/^Attract\/[^/]+$/, "json"],
  [/^Attribution\/[^/]+$/, "png"],
  [/^Fonts\/[^/]+$/, "ttf", "txt"],
  [/^NCA\/(DynamicTexture\/)?[^/]+$/, "bin"],
  [/^Packed\/Visuals\//, "bin", "json", "png", "qoi", "sha256"],
  [/^Particles\/[^/]+$/, "json"],
  [/^Rigs\/[^/]+$/, "json", "ozzpack"],
  [new RegExp(`^Rooms/${world}\\.regions\\.json$`)],
  [new RegExp(`^Rooms/${world}/`), "json", "png"],
  [/^Shaders\/Compiled\/[^/]+$/, shaderExtension],
  [/^Storylets\/[^/]+$/, "json"],
  [/^Voice\/[^/]+$/, "json"],
];

// Mirrors Kit.ScrambleStream: each byte XORed with Hash32.Mix(key ^ position).
function unscramble(data) {
  const key = fs.readFileSync(path.join(root, "Source/Assets/Assets.cs"), "utf8")
    .match(/const uint contentPakKey = (0x[0-9a-fA-F]+);/)?.[1];
  if (!key) throw new Error("contentPakKey not found in Source/Assets/Assets.cs.");
  for (let i = 0; i < data.length; i++) {
    let v = (Number(key) ^ i) >>> 0;
    v ^= v >>> 16;
    v = Math.imul(v, 0x7feb352d);
    v ^= v >>> 15;
    v = Math.imul(v, 0x846ca68b);
    v ^= v >>> 16;
    data[i] ^= v & 0xff;
  }
}

const data = fs.readFileSync(pakPath);
const scrambled = data.readUInt32LE(0) !== 0x04034b50;
if (scrambled) unscramble(data);
if (data.readUInt32LE(0) !== 0x04034b50) throw new Error(`${pakPath} is not a content pak.`);

const unscrambledOut = option("--unscramble");
if (unscrambledOut) {
  fs.writeFileSync(unscrambledOut, data);
  console.log(`Wrote unscrambled zip: ${unscrambledOut}`);
}

let eocd = data.length - 22;
while (eocd >= 0 && data.readUInt32LE(eocd) !== 0x06054b50) eocd--;
if (eocd < 0) throw new Error(`${pakPath} has no zip central directory.`);

const entries = [];
for (let at = data.readUInt32LE(eocd + 16), n = data.readUInt16LE(eocd + 10); n > 0; n--) {
  if (data.readUInt32LE(at) !== 0x02014b50) throw new Error(`Corrupt central directory in ${pakPath}.`);
  const nameLength = data.readUInt16LE(at + 28);
  entries.push({
    name: data.toString("utf8", at + 46, at + 46 + nameLength),
    method: data.readUInt16LE(at + 10),
    compressedSize: data.readUInt32LE(at + 20),
    size: data.readUInt32LE(at + 24),
    offset: data.readUInt32LE(at + 42),
  });
  at += 46 + nameLength + data.readUInt16LE(at + 30) + data.readUInt16LE(at + 32);
}

function read(entry) {
  const start = entry.offset + 30 + data.readUInt16LE(entry.offset + 26) + data.readUInt16LE(entry.offset + 28);
  const raw = data.subarray(start, start + entry.compressedSize);
  return entry.method === 0 ? raw : zlib.inflateRawSync(raw);
}

const problems = [];
if ((target === "browser") === scrambled)
  problems.push(target === "browser" ? "browser pak is scrambled" : `${target} pak is not scrambled`);

const seen = new Map();
const folders = new Map();
for (const entry of entries) {
  const { name } = entry;
  const lower = name.toLowerCase();
  if (seen.has(lower)) problems.push(`${name}: collides with ${seen.get(lower)} (content lookups ignore case)`);
  seen.set(lower, name);

  const extension = name.includes(".") ? name.slice(name.lastIndexOf(".") + 1) : "";
  const rule = allowed.find(([pattern]) => pattern.test(name));
  if (name.split("/").some((part) => part.startsWith(".")))
    problems.push(`${name}: dot-prefixed file`);
  else if (!rule)
    problems.push(`${name}: not in an allowed location`);
  else if (rule.length > 1 && !rule.slice(1).includes(extension))
    problems.push(`${name}: .${extension} not allowed here (expected ${rule.slice(1).map((e) => `.${e}`).join(", ")})`);
  if (entry.size === 0) problems.push(`${name}: empty`);
  else if (entry.size < 200 && read(entry).toString("latin1").startsWith("version https://git-lfs"))
    problems.push(`${name}: Git LFS pointer, not content`);

  const folder = name.includes("/") ? name.slice(0, name.indexOf("/")) : "(root)";
  const total = folders.get(folder) ?? { count: 0, size: 0 };
  folders.set(folder, { count: total.count + 1, size: total.size + entry.size });
}

const worldEntry = entries.find((entry) => entry.name === "world.json");
const packedWorld = worldEntry && JSON.parse(read(worldEntry).toString("utf8")).world;
if (packedWorld !== world) problems.push(`world.json names '${packedWorld}', expected '${world}'`);
if (!entries.some((entry) => entry.name.startsWith(`Rooms/${world}/`))) problems.push(`no rooms for world '${world}'`);

console.log(`${pakPath} (${target}/${world}, ${scrambled ? "scrambled" : "plain zip"}): ${entries.length} entries`);
for (const [folder, { count, size }] of [...folders].sort((a, b) => b[1].size - a[1].size))
  console.log(`  ${String(Math.round(size / 1024)).padStart(7)} KB  ${String(count).padStart(4)}  ${folder}`);

if (problems.length > 0) {
  console.error(`\nContent pak check failed (${problems.length}):`);
  for (const problem of problems) console.error(`  ${problem}`);
  process.exit(1);
}
console.log("Content pak check passed.");
