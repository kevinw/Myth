#!/usr/bin/env python3
# Writes asset-manifest.json into the AppBundle: byte sizes for everything main.js
# downloads (game assets + .NET framework resources), so the loading bar can show
# real download progress. Sizes are the uncompressed on-disk bytes — exactly what a
# streaming fetch reader counts after transfer decompression, unlike Content-Length,
# which is absent under chunked encoding and reflects compressed bytes under
# Content-Encoding.
import json
import sys
from pathlib import Path


def framework_is_fetched(path: Path) -> bool:
    # Resources dotnet.js downloads via loadBootResource: the native wasm, webcil
    # assemblies (.wasm), debug symbols, icu data, and the runtime/native JS modules.
    # dotnet.js / dotnet.boot.js load before our resource hook exists, .map/.symbols
    # are devtools-only, and Content/ holds ES modules imported directly by main.js.
    name = path.name
    if name in ("dotnet.js", "dotnet.boot.js"):
        return False
    if name.endswith((".map", ".symbols")):
        return False
    return name.endswith((".wasm", ".dat", ".pdb")) or name in ("dotnet.runtime.js", "dotnet.native.js")


def main() -> int:
    if len(sys.argv) != 2:
        print("Usage: generate-browser-asset-manifest.py APP_BUNDLE_DIR", file=sys.stderr)
        return 2

    app_bundle = Path(sys.argv[1])
    assets_dir = app_bundle / "Assets"
    framework_dir = app_bundle / "_framework"
    manifest_path = app_bundle / "asset-manifest.json"

    if not assets_dir.is_dir():
        print(f"Assets directory not found: {assets_dir}", file=sys.stderr)
        return 1

    assets = []
    for path in sorted(assets_dir.rglob("*")):
        if not path.is_file() or path.name == ".DS_Store":
            continue
        relative = path.relative_to(app_bundle).as_posix()
        assets.append({"path": relative, "bytes": path.stat().st_size})

    framework = {}
    if framework_dir.is_dir():
        for path in sorted(framework_dir.iterdir()):
            if path.is_file() and framework_is_fetched(path):
                framework[path.name] = path.stat().st_size

    manifest = {"assets": assets, "framework": framework}
    manifest_path.write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8")
    total = sum(entry["bytes"] for entry in assets) + sum(framework.values())
    print(f"Wrote {manifest_path}: {len(assets)} assets + {len(framework)} framework files, {total / (1024 * 1024):.1f} MB expected.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
