# Fingerprint assets + cache the web build (future work)

The release browser build is in good shape on the things that usually matter — the
one real win left is HTTP caching of the framework files on returning loads.

*(Updated 2026-07-13: reconciled with the Content.zip consolidation and the
byte-accurate loading bar in main.js — see "Interactions with the loading bar" below,
one of which is a must-fix if this plan ships.)*

## Pros / cons

**Pros**

- **Warm loads make ~zero framework requests.** Today a returning player pays ~28
  conditional GETs (304s) per load even when nothing changed — pure round-trip latency,
  worst on mobile. With hashed names + `immutable`, the browser serves everything from
  cache without asking. This is the entire remaining startup win for repeat players.
- **Deploys are atomic from the client's view.** New build ⇒ new URLs; a returning
  client either has the old complete set or fetches the new complete set. The
  stale-mix failure mode (fresh boot config + old cached wasm) becomes impossible —
  it's exactly the hazard that forces `must-revalidate` today.
- **Integrity comes back for free.** The loading-bar resource loader fetches without
  integrity metadata (streaming and integrity-fetch are mutually exclusive — see
  main.js createResourceLoader). With the content hash in the URL, the URL itself pins
  the bytes — equivalent assurance, no buffering.
- **Zero maintenance once wired.** The hash moves with every build automatically, and
  `generate-browser-asset-manifest.py` reads the real AppBundle filenames, so the
  loading-bar manifest adapts with no changes.

**Cons**

- **Cache-policy split has sharp edges.** `dotnet.js` / `dotnet.boot.js` / `main.js` /
  `index.html` / `asset-manifest.json` must stay revalidating while hashed files go
  immutable; Cloudflare Pages `_headers` is glob-only, so the rules need real
  verification (see caveats below). Getting this wrong serves stale entry points —
  strictly worse than today.
- **Client fetch modes currently defeat it:** both dotnet.js's default loader *and*
  our custom resource loader request with `cache: "no-cache"`, which forces
  revalidation regardless of `immutable` headers. Fingerprinting buys nothing until
  that's switched off (must-fix in "Interactions" below).
- **Every deploy invalidates whole files.** Any build change renames
  `dotnet.native.wasm` (and each touched assembly), so returning players re-download
  those files after a deploy. Same bytes-over-wire as today (a changed file 304→200s
  anyway) — just know hashing gives no within-file delta updates.
- **Debugging indirection.** Network tabs, stack traces, and player screenshots show
  `dotnet.native.abc123.wasm`; correlating a report to a build means consulting
  `dotnet.boot.js`. Minor, permanent tax.
- **Only returning players benefit.** Cold loads are unchanged (~4.7 MB compressed
  either way). If analytics say most sessions are first visits, this page stays low
  priority.

## Interactions with the loading bar / Content.zip work (2026-07)

- **Must-fix if this ships:** `main.js`'s `createResourceLoader` mirrors dotnet.js and
  fetches framework resources with `cache: "no-cache"`, which *always* revalidates —
  neutering `immutable`. When fingerprinting lands: fetch hashed names with default
  cache mode in our loader, and set `withConfig({ disableNoCacheFetch: true })` so any
  resource still going through dotnet.js's default path behaves the same. Keep
  revalidation for the unhashed entry files.
- `asset-manifest.json` is the "boot config" of the asset layer (it now carries byte
  sizes for the progress meter) — it must stay `must-revalidate`, like `dotnet.boot.js`.
- **Assets are now one zip + loose FMOD banks** (not a file tree). `Content.zip` isn't
  fingerprinted; one conditional GET per load for it is cheap. If we ever want it
  immutable too, emit `Content.<hash>.zip` from `--build-content-zip` and let the
  manifest carry the name — the loader already takes paths from the manifest.
- Bank files change rarely; they could take a long `max-age` (not `immutable`) with
  the manifest acting as the version signal, since the FMOD bridge now receives bytes
  downloaded via manifest-listed paths.

## What's already fine (measured against https://myth-game.pages.dev)

- **Brotli** is served on everything. `dotnet.native.wasm`: 11.97 MB → **3.66 MB** over the wire.
- **`-g` doesn't ship.** The CI `wasm-opt … -g` pass keeps debug info through optimization,
  but the SDK strips it afterward — the shipped `dotnet.native.wasm` has 0 bytes of DWARF and
  0 bytes of `name` section. Leave the `wasm-opt` flags alone.
- **Trimming is tight.** Release ships 24 assemblies; the heavy serialization/ICU stuff
  (System.Private.Xml, DataContract, Data.Common, CodePages) is gone. (It only shows up in the
  *Debug* bundle, which isn't trimmed — don't be fooled by `ls`-ing `bin/Debug`.)
- Cold load of `_framework` ≈ **4.67 MB** compressed across 28 files.

## The problem

Every asset is served `cache-control: public, max-age=0, must-revalidate`, and the filenames
are **not fingerprinted** (`dotnet.native.wasm`, not `dotnet.native.<hash>.wasm`). A warm load
therefore does a conditional GET on each file → `304` with a 0-byte body. So it's not
re-downloading the 4.67 MB, but it **is ~28 revalidation round-trips on every load**, even when
nothing changed. On mobile/high-latency that's pure startup latency for no benefit.

## Why we can't just mark `/_framework/*` immutable

`immutable` promises "the bytes at this URL never change." Today that's false: the filenames are
stable but the bytes change every build. Pinning `immutable` would serve returning players a
stale runtime/game until they hard-refresh.

Immutability has to come from the **hash being in the filename**, not in the bytes. The boot
config already carries `sha256-…` per resource, but that's only used by the loader's own
integrity / Cache-API check — the HTTP URL stays mutable. Only `WasmFingerprintAssets=true`
moves the hash into the filename, and then *that* URL is genuinely immutable.

Even after fingerprinting, part of `/_framework/` must stay mutable — the bootstrap can't be
content-hashed because it's the entry point:
- `dotnet.js` — `main.js` imports it by literal path (`./_framework/dotnet.js`)
- `dotnet.boot.js` — the manifest that maps virtual paths → the fingerprinted names

If those got cached long, clients would never learn about new hashes.

## The plan

1. Enable `WasmFingerprintAssets=true` (browser-wasm publish). Confirm the emitted filename
   patterns — assemblies and `dotnet.native.wasm` should become `*.<hash>.wasm`, and
   `dotnet.boot.js` should reference the hashed names.
2. Split cache policy in `Platforms/Web/wwwroot/_headers` (already used for COOP/COEP):

   | Path | Cache | Why |
   |---|---|---|
   | `dotnet.js`, `dotnet.boot.js`, `index.html`, `main.js` | `max-age=0, must-revalidate` | entry/manifest — must re-check every load |
   | fingerprinted `*.<hash>.wasm` / `.js` | `max-age=31536000, immutable` | hash in name ⇒ URL never changes |

   Net warm-load win: ~26 of the 28 round-trips disappear; only the hashed files get the
   immutable promise.

## Caveats to resolve before touching `_headers`

- **Cloudflare Pages `_headers` is glob-only, no regex.** Need to confirm rule ordering /
  specificity actually distinguishes `dotnet.boot.js` (mutable) from `dotnet.native.<hash>.js`
  (immutable). May require listing the mutable entry files explicitly *after* a broad
  `/_framework/*` immutable rule, or vice-versa — verify which wins.
- **`Assets/` content** is now `Content.zip` + loose FMOD banks + `asset-manifest.json`
  (see "Interactions" above for the per-file policy).
- Prototype a local `--aot` publish first and read the real emitted filenames before writing rules.

## Side note (separate, smaller)

`ImGui.NET.wasm` ships in the Release bundle (dev UI in prod). Gated on `MythBrowserImGui` /
the `cimgui.a` archive existing — could be turned off for the CI publish to trim a bit.

## Compatibility floor (not a caching issue, just don't forget)

`--enable-simd` and `--enable-exception-handling` set the minimum browser (SIMD ≈ iOS Safari
16.4+, native WASM EH needs a recent Safari). Couldn't verify the oldest target device remotely —
test on real old hardware if pre-16.4 iOS matters; don't disable blindly.
