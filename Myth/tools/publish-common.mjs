import { execFileSync } from "node:child_process";
import fs from "node:fs";
import path from "node:path";

// Builds the visual pack and stages each target's shipped Assets folder (Content.pak + loose
// FMOD banks) under obj/Content/<target>/Assets, audits each pak, and returns those folders.
export function stageContent(root, targets, world, dotnetProps = []) {
  const run = (args) => {
    console.log(`\n> ${args.join(" ")}`);
    execFileSync(args[0], args.slice(1), { cwd: root, stdio: "inherit" });
  };
  run(["dotnet", "run", "--project", "Myth.csproj", ...dotnetProps, "--", "--stage-content", targets.join(","), "--world", world]);
  return targets.map((target) => {
    const assets = path.join(root, "obj", "Content", target, "Assets");
    run([process.execPath, "tools/check-content-pak.mjs", path.join(assets, "Content.pak"), "--target", target, "--world", world]);
    return assets;
  });
}

// Timestamps carry over: tools/deploy-deck.sh rsyncs the result, and fresh mtimes would
// resend every bank on every publish.
export function copyStagedAssets(stagedAssets, destAssets) {
  fs.cpSync(stagedAssets, destAssets, { recursive: true, preserveTimestamps: true });
}

export function checkFmodBankMtimes(root, platform) {
  return; // This needs a rethink. Not all banks get rebuilt; FMOD is "smart" about changes.

  if (process.argv.includes("--skip-fmod-mtime-check")) {
    console.warn("Skipping FMOD bank freshness check.");
    return;
  }

  const project = path.join(root, "Assets", "Sources", "Sounds", "FMOD", "Myth.fspro");
  const bankDir = path.join(root, "Assets", "Sounds", "FMOD", "Build", platform);
  const projectMtime = fs.statSync(project).mtimeMs;
  const banks = fs.readdirSync(bankDir).filter((file) => file.endsWith(".bank"));
  if (banks.length === 0)
    throw new Error(`No FMOD banks found in ${bankDir}. Build the FMOD project before publishing.`);

  const stale = banks.filter((bank) => fs.statSync(path.join(bankDir, bank)).mtimeMs <= projectMtime);
  if (stale.length > 0)
    throw new Error(`FMOD banks are not newer than Myth.fspro: ${stale.join(", ")}. Build the FMOD project before publishing, or pass --skip-fmod-mtime-check.`);
}
