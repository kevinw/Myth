#!/usr/bin/env node
// Regenerates golden.json: runs every corpus.txt expression through Strudel itself and records the
// exact haps. Strudel (AGPL-3.0) is fetched into ~/.cache and only run as a reference; none of it is
// copied into this repo. Its npm bundle needs a browser, so this loads the package sources.
// Usage: node Kit/Patterns/Tests/strudel-oracle.mjs

import { execFileSync } from "node:child_process";
import fs from "node:fs";
import os from "node:os";
import path from "node:path";
import { fileURLToPath, pathToFileURL } from "node:url";

const strudelCommit = "8f81463b9cb5ddd5f117ed7baef6a1fde9445dc2";
const here = path.dirname(fileURLToPath(import.meta.url));
const checkout = path.join(os.homedir(), ".cache", "strudel-oracle", strudelCommit);

const run = (cmd, args, cwd) => execFileSync(cmd, args, { cwd, stdio: ["ignore", "ignore", "inherit"] });
if (!fs.existsSync(path.join(checkout, "packages", "core", "pattern.mjs"))) {
  fs.mkdirSync(checkout, { recursive: true });
  run("git", ["init", "-q"], checkout);
  run("git", ["fetch", "-q", "--depth", "1", "https://codeberg.org/uzu/strudel.git", strudelCommit], checkout);
  run("git", ["checkout", "-q", "FETCH_HEAD"], checkout);
}
// Strudel's own package.json uses pnpm workspaces, which npm can't install into, so its one runtime
// dependency goes in a sibling folder and gets linked beside @strudel/core.
const deps = `${checkout}-deps`;
if (!fs.existsSync(path.join(deps, "node_modules", "fraction.js"))) {
  fs.mkdirSync(deps, { recursive: true });
  run("npm", ["install", "--no-save", "--no-package-lock", "--ignore-scripts", "--prefix", deps, "fraction.js@5.2.1"], deps);
}
const link = (target, name) => {
  const at = path.join(checkout, "node_modules", name);
  if (!fs.existsSync(at)) {
    fs.mkdirSync(path.dirname(at), { recursive: true });
    fs.symlinkSync(target, at);
  }
};
link(path.join(deps, "node_modules", "fraction.js"), "fraction.js");
link(path.join(checkout, "packages", "core"), "@strudel/core");

const log = console.log;
console.log = () => {}; // Strudel logs its banner and browser warnings on import
const core = await import(pathToFileURL(path.join(checkout, "packages", "core", "index.mjs")));
const { mini } = await import(pathToFileURL(path.join(checkout, "packages", "mini", "mini.mjs")));
console.log = log;

const scope = { mini, stack: core.stack, cat: core.cat, slowcat: core.slowcat, seq: core.seq, sequence: core.sequence, fastcat: core.fastcat, silence: core.silence, pure: core.pure };
const evaluate = (expression) => {
  const js = expression.replace(/"([^"]*)"/g, (_, text) => `mini(${JSON.stringify(text)})`);
  return new Function(...Object.keys(scope), `return ${js};`)(...Object.values(scope));
};

const fraction = (f) => (f.d == 1 ? `${f.s * f.n}` : `${f.s * f.n}/${f.d}`);
const arc = (a) => `${fraction(a.begin)}-${fraction(a.end)}`;
const value = (v) => JSON.stringify(v);
const query = (pattern, begin, end) => pattern.query(new core.State(new core.TimeSpan(begin, end)));
const haps = (pattern, begin, end) =>
  query(pattern, begin, end).map((h) => `${h.whole ? arc(h.whole) : "~"} ${arc(h.part)} ${value(h.value)}`).sort();

// Keep in sync with PatternTests: the same spans, fragment sizes, and formats.
const ratio = (n, d) => core.Fraction(n).div(d); // Strudel's Fraction() ignores a second argument
const spans = [[ratio(1, 3), ratio(7, 5)], [ratio(5, 4), ratio(13, 4)]];
const frameCount = 240;
const cases = [];
for (const line of fs.readFileSync(path.join(here, "corpus.txt"), "utf8").split("\n")) {
  const expression = line.trim();
  if (!expression || expression.startsWith("#"))
    continue;
  const pattern = evaluate(expression);
  const triggers = [];
  for (let frame = 0; frame < frameCount; frame++)
    for (const h of query(pattern, ratio(frame, 60), ratio(frame + 1, 60)))
      if (h.whole && h.whole.begin.equals(h.part.begin))
        triggers.push(`${arc(h.whole)} ${value(h.value)}`);
  cases.push({
    expression,
    cycles: haps(pattern, ratio(0, 1), ratio(8, 1)),
    spans: spans.map(([begin, end]) => haps(pattern, begin, end)),
    triggers: triggers.sort(),
  });
}

fs.writeFileSync(path.join(here, "golden.json"), JSON.stringify({ strudel: strudelCommit, cases }, null, 1) + "\n");
log(`Wrote ${cases.length} cases to golden.json`);
