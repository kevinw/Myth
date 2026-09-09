#!/usr/bin/env node

import { execFileSync } from "node:child_process";
import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

const scriptDir = path.dirname(fileURLToPath(import.meta.url));
const mythDir = path.resolve(scriptDir, "../..");
const buildDir = path.resolve(process.argv[2] ?? path.join(mythDir, "obj", "ozz-runtime"));
const windows = process.platform === "win32";
// Optional cross target (a RID), passed by Myth.csproj when the host isn't the target.
// ar/ranlib have to be llvm's: Apple's index ELF archives as empty, and the linker then
// drops most of Ozz into a .so that only fails at dlopen.
const target = process.argv[3];
if (target && target !== "linux-x64")
	throw new Error(`No cross toolchain for ${target}; tools/linux-cross-cc.sh targets linux-x64.`);
const brewLlvm = "/opt/homebrew/opt/llvm/bin";
const llvmTool = (name) => fs.existsSync(`${brewLlvm}/${name}`) ? `${brewLlvm}/${name}` : name;
const cross = target ? [
	"-DCMAKE_SYSTEM_NAME=Linux",
	"-DCMAKE_SYSTEM_PROCESSOR=x86_64",
	`-DCMAKE_C_COMPILER=${path.join(mythDir, "tools", "linux-cross-cc.sh")}`,
	`-DCMAKE_CXX_COMPILER=${path.join(mythDir, "tools", "linux-cross-cxx.sh")}`,
	`-DCMAKE_AR=${llvmTool("llvm-ar")}`,
	`-DCMAKE_RANLIB=${llvmTool("llvm-ranlib")}`,
] : [];

function run(args, quietStdout = false) {
	try {
		execFileSync("cmake", args, {
			stdio: quietStdout ? ["inherit", "pipe", "inherit"] : "inherit",
			maxBuffer: 16 * 1024 * 1024,
		});
	} catch (error) {
		if (quietStdout && error.stdout) process.stdout.write(error.stdout);
		throw error;
	}
}

run([
	"--log-level=WARNING",
	"-S", scriptDir,
	"-B", buildDir,
	...(windows ? ["-A", "x64"] : ["-G", "Ninja", "-DCMAKE_BUILD_TYPE=MinSizeRel"]),
	...cross,
	"-Dozz_build_tools=OFF",
	"-Dozz_build_gltf=OFF",
	"-Dozz_build_fbx=OFF",
	"-Dozz_build_data=OFF",
	"-Dozz_build_samples=OFF",
	"-Dozz_build_howtos=OFF",
	"-Dozz_build_tests=OFF",
	"-Dozz_build_postfix=OFF",
], true);
run([
	"--build", buildDir,
	"--target", "myth_ozz",
	...(windows ? ["--config", "MinSizeRel", "--", "/nologo", "/verbosity:quiet"] : ["--", "--quiet"]),
]);
