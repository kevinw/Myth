# Myth

Desktop screenshots capture the completed window at its pixel resolution, including HUD,
CRT, menus, overlays, and letterboxing:

```sh
dotnet run -- --world Myth --screenshot /tmp/myth.png
dotnet run -- --world Myth --screenshot /tmp/myth-clean.png --screenshot-opts hud=false,postprocessing=false
```

Both options default to `true` and can be set independently. They control gameplay HUD and
the final CRT treatment during the screenshot run; world lighting and effects remain enabled.
Captures always use window resolution. Use `--screenshot-frame` (default 60),
`--screenshot-count` (default 1), and `--screenshot-interval` (default 60) for capture timing.
