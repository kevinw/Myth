#!/usr/bin/env node

import { execFileSync } from "node:child_process";
import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

const scriptDir = path.dirname(fileURLToPath(import.meta.url));
const mythDir = path.resolve(scriptDir, "../..");
const buildDir = path.resolve(process.argv[2] ?? path.join(mythDir, "obj", "ozz-runtime"));
const windows = process.platform === "win32";
// Optional cross target, from Myth.csproj when publishing the Linux game on macOS.
// llvm's ar/ranlib are required, not preferred: Apple's index ELF archives as empty, and
// the linker then silently drops most of Ozz, leaving a .so that only fails at dlopen.
const llvm = "/opt/homebrew/opt/llvm/bin";
const cross = process.argv[3] === "linux-x64" ? [
	"-DCMAKE_SYSTEM_NAME=Linux",
	"-DCMAKE_SYSTEM_PROCESSOR=x86_64",
	`-DCMAKE_C_COMPILER=${path.join(mythDir, "tools", "linux-cross-cc.sh")}`,
	`-DCMAKE_CXX_COMPILER=${path.join(mythDir, "tools", "linux-cross-cxx.sh")}`,
	`-DCMAKE_AR=${llvm}/llvm-ar`,
	`-DCMAKE_RANLIB=${llvm}/llvm-ranlib`,
] : [];

if (cross.length && !fs.existsSync(`${llvm}/llvm-ar`))
	throw new Error(`Cross-building the Ozz runtime needs llvm-ar in ${llvm} — run: brew install llvm`);

function run(args) {
	execFileSync("cmake", args, { stdio: "inherit" });
}

run([
	"-S", scriptDir,
	"-B", buildDir,
	...(windows ? ["-G", "Visual Studio 17 2022", "-A", "x64"] : ["-G", "Ninja", "-DCMAKE_BUILD_TYPE=MinSizeRel"]),
	...cross,
	"-Dozz_build_tools=OFF",
	"-Dozz_build_gltf=OFF",
	"-Dozz_build_fbx=OFF",
	"-Dozz_build_data=OFF",
	"-Dozz_build_samples=OFF",
	"-Dozz_build_howtos=OFF",
	"-Dozz_build_tests=OFF",
	"-Dozz_build_postfix=OFF",
]);
run(["--build", buildDir, "--target", "myth_ozz", ...(windows ? ["--config", "MinSizeRel"] : [])]);
