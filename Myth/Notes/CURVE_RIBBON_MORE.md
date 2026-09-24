# CurveRibbon: more

Ideas for extending `Source/Actors/CurveRibbon.cs`, started 2026-09-24. Plan: do the structural work first so the rest lands on a shared foundation, then pursue the gameplay and artistic ideas.

## What it did before the foundation work

- A smooth spline through the authored points, turned into a tapering strip (`Width` → `EndWidth`).
- Root mode: seeded procedural branches (`BranchSpacing`, `BranchLength`, `BranchWidth`).
- Cable mode: constant width, a highlight line, and collars every `CollarSpacing` (now the `cable` style).
- Animated wobble noise (`WobbleFrequency/Intensity/Speed`), zero at every authored point.
- Generated normals (`Roundness`, now split with `Bulge`) so it reads as round under the lighting; `SurfaceContent` material with `FollowCurveTexture`.

## 1. Foundation (done 2026-09-24)

- **Geometry in Kit** (`Kit/Util/Ribbon.cs`): `Ribbon.AppendSpline` (Catmull-Rom samples with tangent and per-segment T), `AppendEdges` (tapered cross-sections with arc-length distance), `TryEdgeAt` (interpolated cross-section at a distance, walked with a cursor). Trails, tethers and tentacles can build on these.
- **Styles as content** (`Source/Assets/RibbonStyle.cs`, files in `Assets/Sources/RibbonStyles/<id>.json`, hot-reloaded). The actor picks `Style` (default `root`; `cable` also exists). `SurfaceOverride` replaces the style's surface for one instance. Per-instance knobs (width, branches, wobble, seed, tint) stay on the actor.
  - `Surface`: surface ID.
  - `Taper` (default true): false keeps the width constant and hides `EndWidth`.
  - `Branches` (default true): false hides the branch knobs.
  - `Stripes`: lines along the ribbon. `Offset` (ribbon-widths from the spine, positive toward the left edge), `Thickness` (widths, min 1px), `Color`, `Opacity`.
  - `Stamps`: placed every `Spacing` px (± `Jitter` × Spacing). `Kind`: `Band` (bar across, `Size` widths long, `Thickness`, optional `Outline` in `OutlineColor`) or `Sprite` (first frame of `Sprite`, scaled by `Size`). `Side`: `Center`, `Left`, `Right`, `Alternate`. `Align` rotates sprites to point away from the spine.
  - Actor `Tint` multiplies stripe and stamp colors.
- **Rebuild check**: one `BuildKey` record struct instead of fifteen `built*` fields; the resolved style object is part of the key, so a hot reload rebuilds.
- Not yet: sprite stamps don't draw into the normal pass (rotated normal maps need their normals rotated), and there's no stamp `From`/`To` range.

## 2. Ideas that connect to other systems

### `Grow` (0..1) (done 2026-09-24)
`Grow` shows the ribbon from its base out to that fraction; `GrowSpeed` is the front's px/second. The ribbon is `IActivatable`: point a Button or Switch at it and it grows fully while active, then returns to `Grow`. `GrowTo(value)` drives it anywhere (for cutscenes and triggers, once they can call it). A room loads with the ribbon already at its target, with no animation.
- Taper is measured over the grown length, so the tip is always tapered. Branches sprout to full size over `BranchLength` behind the front, and stamps appear once the front passes them.
- Style `Cap: Round` puts round caps on both ends (the cable style uses it).
- Feedback at the front: style `GrowParticles` (root: `elp_ground_dust`, cable: `sparks`) and `GrowSound`, a looping FMOD event with a `speed` parameter (px/s), positioned at the front. No sound is authored yet.
- Open: a latched (persistent) button may re-grow the ribbon on every visit if it activates after the ribbon's first update. Collision along the grown path ("root bridge") is still to come.

### Collision (first slice done 2026-09-24)
Actor `Collision`: `None`, `Top` (jumpthru: stand on it, jump up through it), `Solid`, `Hazard` (contact damage through the player's existing Enemy|Hazard overlap check; a hazard can also be struck).
- `Hitbox.Shapes.Pixels` with `Kit/Util/PixelMask.cs`: the ribbon fills a per-pixel mask from its own main-strip triangles, so what you see is what you stand on. Branches, caps and stamps don't collide.
- The mask comes from the unanimated shape (wobble at phase 0, no twist) and rebuilds only when shape inputs or the growth front change, so growing bridges work.
- `Actor.Move` steps a grounded walker back up onto a jumpthru it walked into (`TryStepOntoJumpthru`), which makes one-way slopes walkable up to 45°. This applies to every jumpthru.
- The walkable top edge is lit, brightest when level and fading out toward 45° (the step-up limit), and drawn in the emissive pass too. The color is style `WalkableEdge`.
- Style `Substance` sets footstep material (root: Wood, cable: Metal).
- Burrowing on ribbons (2026-09-24): the burrow boundary walk's pixel tests (`SolidPixel`, `JumpthruSolid` in `Game.SurfaceCollision.cs`) use `Hitbox.Contains`, so pixel-mask ribbons count. A Solid ribbon is burrowable around its whole outline. A Top ribbon is burrowable along its top: for per-pixel jumpthrus, the risers of the top's pixel staircase count as well as the flat steps (rectangular jumpthrus keep the strict "air directly above" rule). Open caveat: burrowing sinks 4px into solids, so a Solid ribbon thinner than about 8px (near a tapered tip) may look odd.
- Next: the ribbon's local normal in `TryGetSurfaceAt` so rolling downhill works on roots; Solid ribbons that grow into a pawn don't push it yet; a Climb (rope-style riding) mode.

### Tunnel: burrow *through* a ribbon (idea)
Dive in at one end, travel inside the root along its spine (even where it arcs through open air), and pop out at the far end along the tip's direction.
- Cheapest route: the ribbon owns a hidden, runtime-only `BurrowLine` whose points are sampled from the spine. The burrow code already enters burrow lines at their endpoints and handles junctions, doors, switches and the exit pop, so it needs no changes.
- A separate `Tunnel` bool, not a Collision mode: a root can be both something to stand on and a tunnel.
- Play: roots as routes (burrow along the ground, ride a root over a gap, get launched from its tip, so an upturned root is an aimable spring); with `Grow`, a root only becomes a route once it has grown to its ledge.
- Feedback: the ribbon bulges where the player is inside it (the Peristalsis knob, driven by position); the burrow glow and sound come along for free.


### Sag under riders (idea)
Things standing on a Collision ribbon bend it a little, and it springs back.
- Bend the sampled spine, not the authored control points: a smooth falloff around each contact, so authored data and the editor view never change. Moving a control point would drag whole Catmull-Rom segments and their neighbours.
- Anchor-aware shape, as a style `Anchors: Ends | Base` plus actor `Stiffness` (0 = rigid): Ends acts like a string under load (a soft V, deepest mid-span, barely moving at the anchors); Base acts like a cantilever (stiff at the root, drooping at the tip).
- The hard part is keeping the picture, the collision mask and the riders in agreement:
  - the mask must follow the bend (today it's built from the unanimated shape), so it rebuilds while the spring moves; reuse the mask buffer rather than allocating each frame;
  - riders must be collected before each change and carried by the bend at their own x, as `MovingPlatform` does with `MovePixels`. Springing back *up* is the dangerous case: a jumpthru ignores anything overlapping it, so a rising surface would pass through the rider's feet;
  - collision moves in whole pixels, so round the visual bend to whole pixels too and the picture never disagrees with where feet are.
- Feel: a spring per contact (`Kit/Util/Spring.cs`) toward weight × local bendiness; landing velocity seeds it (deep dip, bounce, settle); jumping off leaves a springy rebound. Later: a jump boost timed at the bottom of the bounce, per-actor weights (a crate sags a root far enough to reach something), creak audio with a depth parameter, leaves or dust shaken off on landing. The lit walkable edge follows the bend automatically.
- Interactions: the 45° step-up rule handles the changing slope (a deep V may trap you at the bottom, which could be fun); wobble stays visual on top; burrowing along the top shouldn't count as riding; only the grown part of a growing ribbon bends; Solid ribbons bend only from riders on top.
- First slice: per-contact spring + anchor-aware falloff; mask follows the rounded bend; riders carried; `Stiffness` and style `Anchors`, spring constants as tweakable getters. Risks: rider carrying (`MovingPlatform` has its own TODO about re-deriving riders each step, so write a small ribbon version rather than share it), and deep bends looking rubbery rather than woody (cap the bend per style).

### Signal pulses along cables
A bright pulse moves along the path, drawn in the emissive pass and casting real light through `ForEachAutomaticLight` (like emissive props).
- The pulse's travel time is the actual delay between a switch and what it opens. Players learn to follow the cable to see what a switch does.
- Next step: a cable that references source and target actors and routes itself between them with some droop, so wiring a puzzle draws its own explanation.
- Roots can carry sap or bioluminescence the same way.
- Audio: a hum that travels with the pulse, or an FMOD parameter tied to it.

### Branches that react to light
Branch directions are random right now. Bias them toward the nearest light (vines), or have roots curl away from torchlight.
- Puzzle: carry a torch to push roots back. Torch flicker would make the root tips quiver.
- Roots near a burrowing player (`Pawn.Burrow`) shake.

## 3. Artistic knobs

### Shape
- **Width profile**: a curve in place of the straight `Width` → `EndWidth` taper, plus periodic knots (root joints, cable bulges) and an end bulb or cap (round cable end, root tip, plug).
- **Bulge** (done 2026-09-24): `Roundness` is now only the strength of the generated shading (0–1); actor `Bulge` sets how steeply the cross-section curves toward the edges (1 ≈ 45° at the edge, higher approaches a cylinder). Normals are encoded normalized, with extra shading columns at ±½ width so high values stay round across the face.
- **Twist** (done 2026-09-24): actor `TwistPeriod` (px per full turn, 0 = off) and `TwistSpeed` (turns/second). Width narrows to |cos| of the turn angle (floor 0.1), the texture squashes and mirrors on the back face, and normals tilt flat across the face. Branches don't twist. Not yet: a back-face tint (a surface takes one tint, so it needs a per-vertex color or a second pass).
- **Sag / slack** for cables: droop between authored points so they visibly hang.
- **Strands**: N offset copies braided around the spine, for rope, bundled cables, clumped roots.
- **Recursive branches**: sub-branches, branches thinner and further apart toward the tip, and a curl amount.
- **Wobble pinning**: choose between pinning every authored point (today) and pinning only the ends, for looser organic shapes.

### Color and lighting
- A tip color, giving a gradient along the length.
- Per-branch color jitter from `Seed`.
- Emissive ribbons (glowing roots, lit cables) with automatic lights sampled along the path.
- Optional shadow casting for foreground ribbons (`RenderShadow`/`RenderReflection` are no-ops right now).

### Motion
- **Wind sway** from a global wind value shared with grass, particles and an FMOD wind loop: one number feeding three systems.
- **Peristalsis**: a width pulse travelling along organic tubes.
- **Contact**: a spring displacement where Pawn or Bud brushes through, settling over time, with a rustle sound whose volume follows speed.
- **Rope simulation** in cable mode with authored points pinned; leads to swingable ropes.

## 4. Content these enable

| Idea | Needs |
|---|---|
| Hanging vines and ropes to climb or swing on | Path as a grab-able line; rope simulation |
| Kelp in underwater rooms | Twist + current sway |
| Signal wires for puzzles | Pulse + source/target routing |
| Growing root bridges | `Grow` + collision along the path |
| Tentacles, eye stalks, creature parts | Animated points + width profile |
| Energy arcs / lightning | High-frequency fast wobble + emissive + flicker |
| Tether between the player and Bud | Ribbon between two moving points, with sag |
| Dash trails, projectile tails | Kit strip builder fed recent positions |
