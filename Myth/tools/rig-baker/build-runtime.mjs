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
if (target && target !== "linux-x64" && target !== "win-x64")
	throw new Error(`No cross toolchain for ${target}; tools/linux-cross-cc.sh targets linux-x64, win-x64 cross uses clang-cl + xwin.`);
const brewLlvm = "/opt/homebrew/opt/llvm/bin";
const llvmTool = (name) => fs.existsSync(`${brewLlvm}/${name}`) ? `${brewLlvm}/${name}` : name;
const winSdk = process.env.MYTH_WIN_SDK_ROOT ?? path.join(process.env.HOME ?? "", "win-sdk");
const winCrossIncludes = ["sdk/include/ucrt", "sdk/include/um", "sdk/include/shared", "crt/include"]
	.map((p) => path.join(winSdk, p)).join(";");
const winCrossLibs = ["crt/lib/x86_64", "sdk/lib/um/x86_64", "sdk/lib/ucrt/x86_64"]
	.map((p) => path.join(winSdk, p)).join(";");
const cross = target === "win-x64" ? [
	"-DCMAKE_SYSTEM_NAME=Windows",
	"-DCMAKE_SYSTEM_PROCESSOR=AMD64",
	`-DCMAKE_CXX_COMPILER=${llvmTool("clang-cl")}`,
	`-DCMAKE_C_COMPILER=${llvmTool("clang-cl")}`,
	"-DCMAKE_CXX_FLAGS=--target=x86_64-pc-windows-msvc",
	"-DCMAKE_C_FLAGS=--target=x86_64-pc-windows-msvc",
	// MSVC's rc/mt don't exist off-Windows; llvm's stand-ins do the manifest step.
	// Try-compiles default to Debug, which wants the debug CRT xwin doesn't carry.
	`-DCMAKE_TRY_COMPILE_CONFIGURATION=Release`,
	`-DCMAKE_RC_COMPILER=${llvmTool("llvm-rc")}`,
	`-DCMAKE_MT=${llvmTool("llvm-mt")}`,
	`-DCMAKE_AR=${llvmTool("llvm-lib")}`,
] : target ? [
	"-DCMAKE_SYSTEM_NAME=Linux",
	"-DCMAKE_SYSTEM_PROCESSOR=x86_64",
	`-DCMAKE_C_COMPILER=${path.join(mythDir, "tools", "linux-cross-cc.sh")}`,
	`-DCMAKE_CXX_COMPILER=${path.join(mythDir, "tools", "linux-cross-cxx.sh")}`,
	`-DCMAKE_AR=${llvmTool("llvm-ar")}`,
	`-DCMAKE_RANLIB=${llvmTool("llvm-ranlib")}`,
] : [];
// clang-cl and lld-link find the MSVC CRT + Windows SDK through INCLUDE/LIB.
const crossEnv = target === "win-x64" ? { INCLUDE: winCrossIncludes, LIB: winCrossLibs } : undefined;

function run(args, quietStdout = false) {
	try {
		execFileSync("cmake", args, {
			stdio: quietStdout ? ["inherit", "pipe", "inherit"] : "inherit",
			maxBuffer: 16 * 1024 * 1024,
			env: crossEnv ? { ...process.env, ...crossEnv } : undefined,
		});
	} catch (error) {
		if (quietStdout && error.stdout) process.stdout.write(error.stdout);
		throw error;
	}
}

// CMake build trees aren't relocatable: a CI cache restored at a different absolute
// path fails every reconfigure ("CMakeCache.txt directory is different..."). Discard it.
const cacheFile = path.join(buildDir, "CMakeCache.txt");
const cachedSource = fs.existsSync(cacheFile)
	? fs.readFileSync(cacheFile, "utf8").match(/^CMAKE_HOME_DIRECTORY(?::\w+)?=(.*)$/m)?.[1]?.trim()
	: null;
if (cachedSource && path.resolve(cachedSource) !== scriptDir) {
	console.log(`Discarding cmake build tree configured for ${cachedSource} (this tree is ${scriptDir}).`);
	fs.rmSync(buildDir, { recursive: true, force: true });
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
