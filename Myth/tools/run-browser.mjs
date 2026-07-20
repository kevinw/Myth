#!/usr/bin/env node
// @ts-check
import { createServer } from "node:http";
import { createReadStream, statSync, existsSync } from "node:fs";
import { join, extname, resolve, dirname, sep } from "node:path";
import { fileURLToPath } from "node:url";
import { spawn, spawnSync } from "node:child_process";
import { createConnection } from "node:net";
import { platform } from "node:os";

const isWindows = platform() === "win32";
const scriptDir = dirname(fileURLToPath(import.meta.url));
const repoDir = resolve(scriptDir, "..");
const browserDotnetScript = join(scriptDir, "browser-dotnet.mjs");

function usage() {
  process.stdout
    .write(`Usage: node tools/run-browser.mjs [options] [-- dotnet-args...]

Builds the visual asset pack, builds or publishes Myth for browser-wasm, starts the local dev server, and opens the browser.

Options:
  --host HOST          Host for the dev server (default: 127.0.0.1)
  --port PORT          Port for the dev server (default: 8082)
  -c, --configuration  Build configuration (default: Debug)
  --publish            Run dotnet publish instead of dotnet build
  --aot                Enable .NET WebAssembly AOT compilation
  --watch              Keep running dotnet watch and notify the browser after successful rebuilds
  --no-open            Do not open the browser automatically
  -h, --help           Show this help
`);
}

let host = "127.0.0.1";
let port = 8082;
let configuration = "Debug";
let publish = false;
let aot = false;
let openBrowser = true;
let watch = false;
const dotnetArgs = [];

const argv = process.argv.slice(2);
let i = 0;
outer: while (i < argv.length) {
  switch (argv[i]) {
    case "--host":
      host = argv[++i];
      break;
    case "--port":
      port = Number(argv[++i]);
      break;
    case "-c":
    case "--configuration":
      configuration = argv[++i];
      break;
    case "--publish":
      publish = true;
      break;
    case "--aot":
      aot = true;
      break;
    case "--watch":
      watch = true;
      break;
    case "--no-open":
      openBrowser = false;
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

function buildOnce() {
  const cmd = publish ? "publish" : "build";
  const result = spawnSync(
    "node",
    [
      browserDotnetScript,
      cmd,
      "-c",
      configuration,
      ...(aot ? ["--aot"] : []),
      "--",
      ...dotnetArgs,
    ],
    { stdio: "inherit", cwd: repoDir },
  );
  if (result.status !== 0) process.exit(result.status ?? 1);
}

function buildAssets() {
  const result = spawnSync(
    "dotnet",
    [
      "run",
      "--project",
      "Myth.csproj",
      "-c",
      configuration,
      "-p:ShaderPlatform=web",
      "--",
      "--build-asset-pack",
      "Assets/Packed/Visuals",
      "--build-content-zip",
      "Assets/Content.zip",
      "--content-target",
      "browser",
      "--content-world",
      "Myth",
    ],
    { stdio: "inherit", cwd: repoDir },
  );
  if (result.status !== 0) process.exit(result.status ?? 1);
}

buildAssets();
buildOnce();

const appBundle = resolve(
  repoDir,
  "bin",
  configuration,
  "net10.0",
  "browser-wasm",
  "AppBundle",
);
if (!existsSync(appBundle)) {
  console.error(`Browser app bundle not found: ${appBundle}`);
  process.exit(1);
}

const url = `http://${host}:${port}/`;

function openUrl() {
  if (!openBrowser) return;
  if (isWindows) {
    spawnSync("cmd", ["/c", "start", "", url], { stdio: "ignore" });
  } else if (platform() === "darwin") {
    spawnSync("open", [url], { stdio: "ignore" });
  } else {
    spawnSync("xdg-open", [url], { stdio: "ignore" });
  }
}

function isPortInUse() {
  return new Promise((resolve) => {
    const client = createConnection({ host, port }, () => {
      client.destroy();
      resolve(true);
    });
    client.on("error", () => resolve(false));
  });
}

const MIME = {
  ".html": "text/html; charset=utf-8",
  ".js": "application/javascript",
  ".mjs": "application/javascript",
  ".wasm": "application/wasm",
  ".json": "application/json",
  ".css": "text/css",
  ".png": "image/png",
  ".jpg": "image/jpeg",
  ".svg": "image/svg+xml",
  ".ico": "image/x-icon",
  ".pdb": "application/octet-stream",
  ".symbols": "text/plain",
  ".wgsl": "text/plain",
  ".dat": "application/octet-stream",
  ".bank": "application/octet-stream",
};

const devHeaders = {
  "Cache-Control": "no-cache",
  "Cross-Origin-Opener-Policy": "same-origin",
  "Cross-Origin-Embedder-Policy": "require-corp",
  "Cross-Origin-Resource-Policy": "same-origin",
  "Timing-Allow-Origin": "*",
};

function serveFile(filePath, res) {
  let stat;
  try {
    stat = statSync(filePath);
  } catch {
    return false;
  }
  if (stat.isDirectory()) return serveFile(join(filePath, "index.html"), res);
  const ct =
    MIME[extname(filePath).toLowerCase()] ?? "application/octet-stream";
  res.writeHead(200, {
    "Content-Type": ct,
    "Content-Length": stat.size,
    ...devHeaders,
  });
  createReadStream(filePath).pipe(res);
  return true;
}

const reloadClients = new Set();

function handleReloadEvents(req, res) {
  res.writeHead(200, {
    "Content-Type": "text/event-stream",
    "Cache-Control": "no-cache",
    Connection: "keep-alive",
    ...devHeaders,
  });
  res.write(": connected\n\n");
  reloadClients.add(res);
  req.on("close", () => reloadClients.delete(res));
}

function broadcastReloadAvailable() {
  const data = JSON.stringify({ builtAt: Date.now() });
  for (const res of reloadClients) res.write(`event: built\ndata: ${data}\n\n`);
}

function handleRequest(req, res) {
  const pathname = new URL(req.url ?? "/", `http://${host}`).pathname;
  if (pathname === "/__myth/events") {
    handleReloadEvents(req, res);
    return;
  }

  const filePath = resolve(join(appBundle, pathname));
  if (!filePath.startsWith(appBundle + sep) && filePath !== appBundle) {
    res.writeHead(403, devHeaders);
    res.end();
    return;
  }
  if (!serveFile(filePath, res)) {
    res.writeHead(404, devHeaders);
    res.end("Not found");
  }
}

let watchProcess = null;

function startWatch() {
  const cmd = publish ? "publish" : "build";
  const args = [
    "watch",
    cmd,
    "Myth.csproj",
    "-c",
    configuration,
    "-r",
    "browser-wasm",
    ...(aot ? ["/p:RunAOTCompilation=true", "/p:MythBrowserAot=true"] : []),
    ...dotnetArgs,
  ];

  console.log(`Starting dotnet ${args.join(" ")}`);
  watchProcess = spawn("dotnet", args, {
    cwd: repoDir,
    stdio: ["ignore", "pipe", "pipe"],
  });
  let skippedInitialSuccess = false;

  const handleOutput = (chunk, stream) => {
    const text = chunk.toString();
    stream.write(text);
    if (!/Build\s+succeeded/i.test(stripAnsi(text))) return;

    if (!skippedInitialSuccess) {
      skippedInitialSuccess = true;
      return;
    }

    broadcastReloadAvailable();
  };

  watchProcess.stdout.on("data", (chunk) =>
    handleOutput(chunk, process.stdout),
  );
  watchProcess.stderr.on("data", (chunk) =>
    handleOutput(chunk, process.stderr),
  );
  watchProcess.on("exit", (code, signal) => {
    watchProcess = null;
    if (signal) console.log(`dotnet watch exited from ${signal}`);
    else if (code !== 0) console.error(`dotnet watch exited with code ${code}`);
  });
}

function stripAnsi(text) {
  return text.replace(/\x1b\[[0-?]*[ -/]*[@-~]/g, "");
}

if (await isPortInUse()) {
  console.log(
    `Port ${host}:${port} is already in use; assuming a dev server is running.`,
  );
  openUrl();
  console.log(url);
  process.exit(0);
}

const server = createServer(handleRequest);
server.listen(port, host, () => {
  console.log(`Serving ${appBundle}`);
  console.log(url);
  openUrl();
  if (watch) startWatch();
});

process.on("SIGINT", () => {
  watchProcess?.kill("SIGINT");
  server.close();
  process.exit(0);
});
