#!/usr/bin/env node
// @ts-check
import { spawnSync } from "node:child_process";
import { rmSync } from "node:fs";
import { dirname, resolve } from "node:path";
import { fileURLToPath } from "node:url";

const scriptDir = dirname(fileURLToPath(import.meta.url));
const repoDir = resolve(scriptDir, "..");
const project = resolve(repoDir, "Myth.csproj");

function usage() {
  process.stdout.write(`Usage: node tools/browser-dotnet.mjs restore|build|publish [options] [-- dotnet-args...]

Shared browser-wasm dotnet commands used by local tools and CI.

Options:
  -c, --configuration  Build configuration. Default: Debug
  --aot                Enable .NET WebAssembly AOT compilation
  --no-restore         Pass --no-restore to build/publish
  -h, --help           Show this help
`);
}

const argv = process.argv.slice(2);
if (argv.length < 1 || argv[0] === "-h" || argv[0] === "--help") {
  usage();
  process.exit(argv.length < 1 ? 1 : 0);
}

const command = argv.shift();
let configuration = "Debug";
let aot = false;
let noRestore = false;
const dotnetArgs = [];

let i = 0;
outer: while (i < argv.length) {
  switch (argv[i]) {
    case "-c":
    case "--configuration":
      configuration = argv[++i];
      break;
    case "--no-restore":
      noRestore = true;
      break;
    case "--aot":
      aot = true;
      break;
    case "-h":
    case "--help":
      usage();
      process.exit(0);
      break;
    case "--":
      dotnetArgs.push(...argv.slice(i + 1));
      break outer;
    default:
      dotnetArgs.push(argv[i]);
      break;
  }
  i++;
}

let args;
switch (command) {
  case "restore":
    args = [
      "restore",
      project,
      "-r",
      "browser-wasm",
      `/p:Configuration=${configuration}`,
      "/p:MythBrowser=true",
      "/p:ShaderPlatform=",
      ...(aot ? ["/p:RunAOTCompilation=true", "/p:MythBrowserAot=true"] : []),
      ...dotnetArgs,
    ];
    break;
  case "build":
  case "publish":
    if (command === "publish") {
      rmSync(resolve(repoDir, "bin", configuration, "net10.0", "browser-wasm", "AppBundle"), {
        recursive: true,
        force: true,
      });
    }

    args = [
      command,
      project,
      "-c",
      configuration,
      "-r",
      "browser-wasm",
      ...(noRestore ? ["--no-restore"] : []),
      "/p:MythBrowser=true",
      "/p:ShaderPlatform=",
      ...(aot ? ["/p:RunAOTCompilation=true", "/p:MythBrowserAot=true"] : []),
      ...dotnetArgs,
    ];
    break;
  default:
    usage();
    process.exit(1);
}

const result = spawnSync("dotnet", args, { stdio: "inherit", cwd: repoDir });
process.exit(result.status ?? 1);
