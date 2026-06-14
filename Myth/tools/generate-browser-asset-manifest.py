#!/usr/bin/env python3
import json
import sys
from pathlib import Path


def main() -> int:
    if len(sys.argv) != 2:
        print("Usage: generate-browser-asset-manifest.py APP_BUNDLE_DIR", file=sys.stderr)
        return 2

    app_bundle = Path(sys.argv[1])
    assets_dir = app_bundle / "Assets"
    manifest_path = app_bundle / "asset-manifest.json"

    if not assets_dir.is_dir():
        print(f"Assets directory not found: {assets_dir}", file=sys.stderr)
        return 1

    files = []
    for path in assets_dir.rglob("*"):
        if not path.is_file() or path.name == ".DS_Store":
            continue

        relative = path.relative_to(app_bundle).as_posix()
        files.append(relative)

    files.sort()
    manifest_path.write_text(json.dumps(files, indent=2) + "\n", encoding="utf-8")
    print(f"Wrote {manifest_path} with {len(files)} assets.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
