# Fingerprint assets + cache the web build (future work)

The release browser build is in good shape on the things that usually matter — the
one real win left is HTTP caching of the framework files on returning loads.

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
- **`Assets/` content** (FMOD `.bank`, `.wgsl`, sprites, the generated asset manifest) also wants
  a deliberate caching policy; decide per-type (the manifest is the "boot" of the asset layer and
  should revalidate; the banks/sprites can cache longer if their names or the manifest bust them).
- Prototype a local `--aot` publish first and read the real emitted filenames before writing rules.

## Side note (separate, smaller)

`ImGui.NET.wasm` ships in the Release bundle (dev UI in prod). Gated on `MythBrowserImGui` /
the `cimgui.a` archive existing — could be turned off for the CI publish to trim a bit.

## Compatibility floor (not a caching issue, just don't forget)

`--enable-simd` and `--enable-exception-handling` set the minimum browser (SIMD ≈ iOS Safari
16.4+, native WASM EH needs a recent Safari). Couldn't verify the oldest target device remotely —
test on real old hardware if pre-16.4 iOS matters; don't disable blindly.
