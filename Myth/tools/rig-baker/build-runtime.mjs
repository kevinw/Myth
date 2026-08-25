#!/usr/bin/env node

import { execFileSync } from "node:child_process";
import path from "node:path";
import { fileURLToPath } from "node:url";

const scriptDir = path.dirname(fileURLToPath(import.meta.url));
const mythDir = path.resolve(scriptDir, "../..");
const buildDir = path.resolve(process.argv[2] ?? path.join(mythDir, "obj", "ozz-runtime"));
const windows = process.platform === "win32";

function run(args) {
	execFileSync("cmake", args, { stdio: "inherit" });
}

run([
	"-S", scriptDir,
	"-B", buildDir,
	...(windows ? ["-G", "Visual Studio 17 2022", "-A", "x64"] : ["-G", "Ninja", "-DCMAKE_BUILD_TYPE=MinSizeRel"]),
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
