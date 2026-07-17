#!/usr/bin/env node
import { execFileSync, spawnSync } from "node:child_process";
import fs from "node:fs";
import path from "node:path";

const [output, artifacts, sha, publishedAt, previousSha, fallbackSha] = process.argv.slice(2);
if (!output || !artifacts || !sha || !publishedAt)
  throw new Error("Usage: write-desktop-release.mjs <output> <artifacts> <sha> <published-at> [previous-sha] [fallback-sha]");

function isAncestor(candidate) {
  return candidate && !/^0+$/.test(candidate) &&
    spawnSync("git", ["merge-base", "--is-ancestor", candidate, sha]).status === 0;
}

const base = isAncestor(previousSha) ? previousSha : isAncestor(fallbackSha) ? fallbackSha : null;
const range = base ? `${base}..${sha}` : sha;
const commits = execFileSync("git", ["log", "--no-merges", "--format=%s", range], { encoding: "utf8" })
  .trim().split("\n").filter(Boolean);
const shortSha = sha.slice(0, 8);
const downloads = [
  { platform: "macOS", architecture: "Apple silicon", file: `Myth-${shortSha}-mac.zip` },
  { platform: "Windows", architecture: "x64", file: `Myth-${shortSha}-windows-x64.zip`, unsigned: true },
].filter(({ file }) => fs.existsSync(path.join(artifacts, file)));
if (!downloads.length)
  throw new Error(`No desktop archives found in ${artifacts}`);

fs.writeFileSync(output, JSON.stringify({
  sha,
  shortSha,
  publishedAt,
  downloads,
  commits,
}, null, 2) + "\n");
