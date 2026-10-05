# Pen decoration

Brainstorm, 2026-10-04: a Prop-like actor that draws scene detail through `Source/Animation/Pen.cs` (the procedural drawing `MagneticCrawler` uses) instead of a sprite, possibly composed from a tree of Pen draws. Nothing built yet.

## What Pen gets decoration for free

An `IPenDrawn` actor draws every pass from its one `Draw` (see `Actor.DrawWithPen`), so compared to a sprite Prop:

- **Lit properly**: chamfered rims and material grit go into the normal buffer, so detail catches room lights the way the crawler does.
- **Shadows, reflections and emissive** all come out of the same drawing.
- **`Pen.Depth` layers detail back**: one number fades color toward the backdrop, flattens normals and dims glow. Background machinery without painting three versions.
- **Grit that never swims**: a static piece can use world position as its material coordinate. Adjacent pieces would then share one continuous grit field across their seams.
- **Detail in pixels, not scale**: rims, rivets, teeth and seams are pixel-sized, so a bigger piece reads as more intricate, not blown up.

## What a tree would be for

A flat list of shapes draws fine. The tree only earns its place if its inner nodes do something:

- **Group**: a transform (offset, rotation, mirror) and a depth that its children inherit. A panel and its rivets move and recede together.
- **Repeat**: N copies along a line or arc, with per-copy jitter from an `ActorId`-seeded hash (the crawler's `ShapeUnit` trick). One node becomes a pipe run with brackets, a vent bank or a row of teeth. This is where the procedural payoff is.
- **Leaves**: the Pen primitives as they are: Plate, Ball, Tube, Cable, Facet, Teeth, Groove, Rivet, Dot, Glow, GlowLine.

Rough shape:

```json
{ "Type": "PenProp", "Position": [96, 40], "Nodes": [
  { "Group": { "Depth": 0.4 }, "Children": [
    { "Tube": [[0,0],[64,0]], "Width": 5, "Segment": 8 },
    { "Repeat": 4, "Along": [[8,0],[56,0]], "Jitter": 1.5, "Children": [
      { "Plate": "rect", "Size": [6,8] }, { "Rivet": [0,-2] } ] } ] },
  { "Cable": [[64,0],[90,30]], "Sag": [0,6] },
  { "Glow": [32,-4], "Radius": 3, "Color": "ff5a30" } ] }
```

## Authoring is the hard part

`ActorSpecs` serializes field types explicitly (e.g. `Point2[]` is special-cased), so a polymorphic node tree doesn't round-trip as an ordinary actor field. Options:

1. **The actor reads its own raw JSON field**: one `JsonNode` blob, parsed in `Added`. Editing it needs a node tree in the inspector (the Actor List's tree view is a pattern to reuse) plus in-world point handles like `PointPathActor`'s. Flexible data, real editor work.
2. **Recipes in C#**: `PenProp` has a `Recipe` (`Pipe`, `VentBank`, `Panel`, `CableRun`, ...) plus generic knobs: size, seed, depth, colors, grit texture. Each recipe is a C# method drawing through the Pen. Close to free to build, live under `dotnet watch`, full procedural power. Scenes are composed by placing and attaching several props (attachment already exists). The downside: new shapes mean code, not clicking.
3. **Hybrid**: tree nodes whose leaves are either primitives or recipe calls with parameters.
4. **Trees as content**, following the RibbonStyle precedent (`Assets/Sources/RibbonStyles/<id>.json`, hot-reloaded; see `CURVE_RIBBON_MORE.md`). Each tree is a named content file the actor picks by id, with per-instance knobs (seed, tint, depth, size) on the actor. That keeps room JSON small, makes a good composite reusable everywhere, and lets the tree be authored in a text editor (or a dedicated editor window later) before any inspector UI exists.

**Leaning:** start with recipes (2). It shows quickly whether Pen-drawn decoration looks good in real scenes, and recipes are the vocabulary a tree would want later. When recipes start to differ only in layout, move toward a tree, most likely as content (4) with recipe leaves (3).

## Tying it into the systems

- **Glows that mean something**: an `EnabledByPersistentButtonId` (as on `Light`) lights seams once a puzzle is solved, and glow strength can feed a real light through `ForEachLight` (as the crawler's eye does), so a lit seam actually lights the room.
- **Reacts to hits**: clang and jiggle a group on a spring, like `Light`'s hit flicker.
- **Cables that respond**: sway with `Wind`, tighten when the thing they're attached to moves. A cable strung to a Crusher could follow its `Menace`.
- **Prefabs**: save a good composite as a "From" spec and reuse it across rooms (or get the same reuse from content trees, option 4).

## Performance

Pen redraws every pass every frame. That's fine for a handful of props. A room full of static decoration should bake each pass into a texture once and redraw only on edit. Wait for the profiler to ask for it.

## Open questions

- **Background or foreground?** Background set dressing, foreground props, or both. Decides how much `Depth` and parallax matter and whether shapes ever need collision.
- **Sculpting or placing?** Sculpting shapes point by point in the editor, or mostly placing, sizing and seeding things that code makes interesting? The second favors recipes.
- **Does decoration ever move?** If it sways, spins or reacts, its material coordinates have to stay on each moving part, as they do on the crawler, rather than using world position.
