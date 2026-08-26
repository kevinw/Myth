import fs from "node:fs";
import path from "node:path";

// FMOD opens banks by real path, so the Desktop banks ship as loose files beside
// Content.zip rather than inside it. Timestamps carry over: tools/deploy-deck.sh rsyncs the
// result, and fresh mtimes would resend every bank on every publish.
export function copyDesktopFmodBanks(root, destAssets) {
  const rel = path.join("Sounds", "FMOD", "Build", "Desktop");
  const bankDir = path.join(root, "Assets", rel);
  fs.mkdirSync(path.join(destAssets, rel), { recursive: true });
  for (const bank of fs.readdirSync(bankDir))
    if (bank.endsWith(".bank") && !bank.startsWith("."))
      fs.cpSync(path.join(bankDir, bank), path.join(destAssets, rel, bank), { preserveTimestamps: true });
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
