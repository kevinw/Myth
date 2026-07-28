import fs from "node:fs";
import path from "node:path";

export function checkFmodBankMtimes(root, platform) {
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
