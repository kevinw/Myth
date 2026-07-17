#!/usr/bin/env node
import { execFileSync, spawnSync } from "node:child_process";
import fs from "node:fs";

const [output, sha, file, publishedAt, previousSha, fallbackSha] = process.argv.slice(2);
if (!output || !sha || !file || !publishedAt)
  throw new Error("Usage: write-desktop-release.mjs <output> <sha> <file> <published-at> [previous-sha] [fallback-sha]");

function isAncestor(candidate) {
  return candidate && !/^0+$/.test(candidate) &&
    spawnSync("git", ["merge-base", "--is-ancestor", candidate, sha]).status === 0;
}

const base = isAncestor(previousSha) ? previousSha : isAncestor(fallbackSha) ? fallbackSha : null;
const range = base ? `${base}..${sha}` : sha;
const commits = execFileSync("git", ["log", "--no-merges", "--format=%s", range], { encoding: "utf8" })
  .trim().split("\n").filter(Boolean);

fs.writeFileSync(output, JSON.stringify({
  sha,
  shortSha: sha.slice(0, 8),
  publishedAt,
  file,
  commits,
}, null, 2) + "\n");
