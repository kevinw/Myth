# World map

From `Myth/`:

```sh
dotnet run -- --write-all-rooms
python3 -m venv Production/.world-map-venv
Production/.world-map-venv/bin/pip install "pyvips[binary]"
Production/.world-map-venv/bin/python tools/world-map/build.py
node tools/world-map/server.mjs
```

Open <http://127.0.0.1:8485>. The room export updates the existing `Production/Room Screenshots` assets; its manifest and the generated `Production/World Map` tile pyramids are ignored by git.
