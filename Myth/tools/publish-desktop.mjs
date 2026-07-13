#!/usr/bin/env node
// Publishes Myth as a self-contained macOS app bundle:
//   - play-only (MythShip strips the editor; ImGui stays available for in-game UI)
//   - NativeAOT, optimized for speed (no JIT warmup hitches, instant cold start)
//   - packed visuals + no Assets/Sources, mirroring the browser bundle
//   - signed with the best identity available (Developer ID Application > Apple
//     Development > ad-hoc), hardened runtime either way
//
// Usage: node tools/publish-desktop.mjs [--open] [--notarize]
//
// --notarize (distribution to other machines) needs one-time setup:
//   1. A "Developer ID Application" certificate — created by the Account Holder at
//      developer.apple.com -> Certificates, then downloaded into the login keychain.
//   2. Stored notary credentials (app-specific password from appleid.apple.com):
//        xcrun notarytool store-credentials myth-notary \
//          --apple-id you@example.com --team-id 9VL2APQ6QE --password <app-specific>
//   3. An unlocked keychain (security unlock-keychain ~/Library/Keychains/login.keychain-db).
// It then submits, waits, staples, and verifies with spctl.

import { execFileSync } from "node:child_process";
import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
const rid = process.arch === "arm64" ? "osx-arm64" : "osx-x64";
const outRoot = path.join(root, "bin", "publish");
const publishDir = path.join(outRoot, rid);
const appDir = path.join(outRoot, "Myth.app");

function run(cmd, args, opts = {}) {
  console.log(`\n> ${cmd} ${args.join(" ")}`);
  execFileSync(cmd, args, { cwd: root, stdio: "inherit", ...opts });
}

function capture(cmd, args) {
  return execFileSync(cmd, args, { cwd: root, encoding: "utf8" });
}

// --- 1. Fresh visual asset pack (the bundle ships no Assets/Sources, so everything
// visual — including tile kit registrations — must be current in the pack), then a
// Content.zip of the shippable Assets slice built around it.
const contentZip = path.join(outRoot, "Content.zip");
run("dotnet", ["run", "--project", "Myth.csproj", "--",
  "--build-asset-pack", "Assets/Packed/Visuals",
  "--build-content-zip", contentZip,
]);

// --- 2. NativeAOT publish, play-only.
run("dotnet", [
  "publish", "Myth.csproj",
  "-c", "Release",
  "-r", rid,
  "--self-contained",
  "-p:MythShip=true", // also enables NativeAOT via Myth.csproj
  "-p:MythDesktopOnly=true", // restore Myth.Core as net10.0 only (no ios TFM against an osx RID)
  "-o", publishDir,
]);

// --- 3. Assemble the .app bundle.
fs.rmSync(appDir, { recursive: true, force: true });
const macos = path.join(appDir, "Contents", "MacOS");
const resources = path.join(appDir, "Contents", "Resources");
fs.mkdirSync(macos, { recursive: true });
fs.mkdirSync(resources, { recursive: true });

// Only code goes in Contents/MacOS: the executable + dylibs. Everything else in the
// publish output (pdb/dSYM debug artifacts, doc xml) stays behind — codesign validates
// every file under MacOS as a code object.
fs.cpSync(path.join(publishDir, "Myth"), path.join(macos, "Myth"));
for (const entry of fs.readdirSync(publishDir))
  if (entry.endsWith(".dylib"))
    fs.cpSync(path.join(publishDir, entry), path.join(macos, entry));

// Assets go in Contents/Resources (codesign --deep rejects non-code under MacOS; the
// game probes Resources/Assets from the executable dir). The bundled Assets folder is
// just Content.zip (everything the game reads through Assets.Content) plus the built
// Desktop FMOD banks as loose files — FMOD opens banks by real path, so they can't
// live inside the zip.
function assetShips(rel) {
  if (rel.split(path.sep).some((part) => part.startsWith("."))) return false;
  return rel.startsWith(path.join("Sounds", "FMOD", "Build", "Desktop") + path.sep) && rel.endsWith(".bank");
}

const assetsRoot = path.join(root, "Assets");
fs.mkdirSync(path.join(resources, "Assets"), { recursive: true });
fs.copyFileSync(contentZip, path.join(resources, "Assets", "Content.zip"));
for (const entry of fs.readdirSync(assetsRoot, { recursive: true, withFileTypes: true })) {
  if (!entry.isFile()) continue;
  const rel = path.relative(assetsRoot, path.join(entry.parentPath, entry.name));
  if (!assetShips(rel)) continue;
  const dest = path.join(resources, "Assets", rel);
  fs.mkdirSync(path.dirname(dest), { recursive: true });
  fs.copyFileSync(path.join(assetsRoot, rel), dest);
}

// Icon: AppIcon.png -> AppIcon.icns via sips/iconutil.
const iconset = path.join(outRoot, "AppIcon.iconset");
fs.rmSync(iconset, { recursive: true, force: true });
fs.mkdirSync(iconset, { recursive: true });
for (const size of [16, 32, 128, 256, 512]) {
  execFileSync("sips", ["-z", `${size}`, `${size}`, "AppIcon.png", "--out", path.join(iconset, `icon_${size}x${size}.png`)], { cwd: root, stdio: "ignore" });
  execFileSync("sips", ["-z", `${size * 2}`, `${size * 2}`, "AppIcon.png", "--out", path.join(iconset, `icon_${size}x${size}@2x.png`)], { cwd: root, stdio: "ignore" });
}
run("iconutil", ["-c", "icns", iconset, "-o", path.join(resources, "AppIcon.icns")]);
fs.rmSync(iconset, { recursive: true, force: true });

fs.writeFileSync(path.join(appDir, "Contents", "Info.plist"), `<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
	<key>CFBundlePackageType</key><string>APPL</string>
	<key>CFBundleIdentifier</key><string>com.fostergames.myth</string>
	<key>CFBundleName</key><string>Myth</string>
	<key>CFBundleDisplayName</key><string>Myth</string>
	<key>CFBundleExecutable</key><string>Myth</string>
	<key>CFBundleIconFile</key><string>AppIcon</string>
	<key>CFBundleShortVersionString</key><string>0.1.0</string>
	<key>CFBundleVersion</key><string>1</string>
	<key>LSMinimumSystemVersion</key><string>13.0</string>
	<key>LSApplicationCategoryType</key><string>public.app-category.games</string>
	<key>NSHighResolutionCapable</key><true/>
</dict>
</plist>
`);

// --- 4. Sign, inside-out (dylibs first, then the bundle — --deep is deprecated), with
// hardened runtime (required for notarization; harmless otherwise). Timestamped only for
// real identities. Falls back toward ad-hoc when an identity exists but is unusable
// (locked keychain, expired cert — codesign reports errSecInternalComponent).
const notarize = process.argv.includes("--notarize");
const profile = process.env.MYTH_NOTARY_PROFILE ?? "myth-notary";

function findIdentity(kind) {
  try {
    const match = capture("security", ["find-identity", "-v", "-p", "codesigning"])
      .split("\n")
      .find((line) => line.includes(kind));
    return match ? match.trim().split(" ")[1] : null;
  } catch {
    return null;
  }
}

const developerId = findIdentity("Developer ID Application");
if (notarize && !developerId)
  throw new Error("--notarize needs a 'Developer ID Application' certificate (see header comment).");

function signWith(identity) {
  // Hardened runtime (+timestamp) only with a real identity: notarization requires it,
  // and its library validation demands one Team ID across the app and dylibs — which
  // ad-hoc signatures don't have, so a hardened ad-hoc app can't load its own dylibs.
  const flags = ["--force"];
  if (identity !== "-") flags.push("--options", "runtime", "--timestamp");
  for (const entry of fs.readdirSync(macos))
    if (entry.endsWith(".dylib"))
      run("codesign", [...flags, "--sign", identity, path.join(macos, entry)]);
  run("codesign", [...flags, "--sign", identity, appDir]);
}

const candidates = [developerId, findIdentity("Apple Development"), "-"].filter(Boolean);
let identity = "-";
for (const candidate of candidates) {
  try {
    signWith(candidate);
    identity = candidate;
    break;
  } catch (error) {
    if (candidate === "-") throw error;
    if (notarize)
      throw new Error(`Signing with ${candidate} failed — unlock the keychain (security unlock-keychain) and retry.`);
    console.warn(`Signing with ${candidate} failed (locked keychain?); trying next identity.`);
  }
}
run("codesign", ["--verify", "--strict", "--verbose", appDir]);

// --- 5. Notarize: zip, submit + wait, staple the ticket, and prove Gatekeeper is happy.
if (notarize) {
  const zip = path.join(outRoot, "Myth.zip");
  fs.rmSync(zip, { force: true });
  run("ditto", ["-c", "-k", "--keepParent", appDir, zip]);
  run("xcrun", ["notarytool", "submit", zip, "--keychain-profile", profile, "--wait"]);
  run("xcrun", ["stapler", "staple", appDir]);
  run("spctl", ["--assess", "--type", "execute", "--verbose", appDir]);
  // Re-zip the stapled app: this is the artifact to distribute.
  fs.rmSync(zip, { force: true });
  run("ditto", ["-c", "-k", "--keepParent", appDir, zip]);
  console.log(`\nNotarized and stapled. Distribute: ${zip}`);
}

const size = capture("du", ["-sh", appDir]).trim().split("\t")[0];
console.log(`\nSigned with: ${identity === "-" ? "ad-hoc (this machine only; use --notarize to distribute)" : identity}`);
console.log(`Built ${appDir} (${size})`);
if (process.argv.includes("--open")) run("open", [appDir]);
