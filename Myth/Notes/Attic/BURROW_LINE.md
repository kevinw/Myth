# Burrow Lines / Rails

Status: **idea, parked for more thought.** Leaning toward the 2-endpoint `BurrowRail` actor to start.

## The goal

Author explicit burrow paths — "rails" — that run **through solid matter**, so the player can
burrow along a designed line and **cannot pop out** mid-path. Enables:

- Tunnels through solid rock that commit the player to a route (no emerging until an endpoint).
- Hand-authored secret paths (e.g. the `burrow_secret` room: around an overhang and back into a block).
- A reusable **visual language** — a faint always-on line that an observant player learns to read as
  "you can burrow here."

This is distinct from today's burrowing, which follows the **solid/air boundary** only. A rail is a
path where solid can be on *both* sides.

## How burrowing works today (grounding)

Burrow traversal is a **pixel-level boundary walk** (`Game.SurfaceCollision.cs`):

- `WalkSurface` marches a point along the solid/air boundary.
- `PickBoundaryDir` chooses the next unit edge with a left/right-hand rule (`burrowSolidLeft` = handedness).
- `BoundaryEdgeValid` only permits an edge that has **solid on one side and air on the other**.
- `SurfaceNormal` derives "into solid" from heading + handedness; the glow (`TraceSurfaceFromState`),
  room transitions, and the squash all hang off that state.
- `PopOutOfBurrow` / emerge needs adjacent air to eject into.

Key sentence: **a tunnel is a surface whose "air side" is a lie you author.**

## Options considered

### A. Per-tile-edge flags — `TileFlags.Burrow{Top,Bottom,Left,Right}`
Four bits per tile marking its four unit grid-edges as rails. (`Top(x,y) ≡ Bottom(x,y-1)`, etc. —
shared edges are redundant; canonicalize by "a rail if either neighbor flags it.")

- **Traversal: nearly free.** One extra permission in `BoundaryEdgeValid` (`|| IsRailEdge(...)`) and the
  existing walker, glow tracer, transitions, and squash all just work.
- **Authoring: cheap.** Rides the tile-painting path (like `TileFlags.Breakable`, painted with a hotkey).
- **Cost: grid-locked.** Axis-aligned, unit-length, blocky. Less expressive.

### B. Free polylines (pen tool, N vertices)
Place arbitrary multi-segment (even curved) lines by choosing vertices.

- **Most expressive:** any angle, long organic curves, branching, as single objects.
- **Cost: highest tooling.** The editor has **no polyline-authoring precedent** — occluder/slope polygons
  are *derived*, never hand-authored. Would require building pen placement, per-vertex drag handles,
  insert/delete, hit-testing, undo from scratch. This is the real bill, and it's mostly *editor* work.

### C. 2-endpoint `BurrowRail` actor, chainable — **current pick**
A straight rail defined by two draggable endpoints (A, B). Chain several end-to-end for multi-segment
tunnels; junctions are where endpoints coincide.

- **Reuses existing infra:** basically a two-handle resizable (`IResizable` / `ResizeHandle` family that
  MovingPlatform, hidden walls use) — selection, undo, placement all come for free. No pen tool.
- **~90% of the expressivity:** arbitrary diagonals and lengths, curves approximated by chaining,
  branching at shared endpoints.
- **Smoother motion bonus:** Vector2 path-following gives fluid diagonal burrowing (contrast to the
  blocky surface walk).
- **Trivial rendering** of the visual-language line — you have the endpoints.

## Why C (the deciding insight)

Any *free* line (B or C) needs the **same** new runtime: a parametric path-follower (project onto
segment, advance by arc-length) plus a **surface↔rail handoff** at endpoints. That work is identical
whether the rail has 2 points or 20. So the runtime cost of "some freedom" vs "full freedom" is nearly
the same — **the difference is almost entirely editor tooling.** C buys the freedom that matters at a
fraction of the tooling cost, and its path-follower is exactly what B would need, so we can graduate to
B later without throwing traversal away.

Edge flags (A) remain the choice if grid-aligned/blocky tunnels are aesthetically fine and we want it
nearly free.

## Technical shape (for C, when we build it)

- **Actor:** `BurrowRail : Actor` (`IResizable`-style with two endpoint handles), `List`-free — just `A`, `B`
  (world points). Renders a faint line. No collision change (solid stays solid; only burrow traverses).
- **Traversal (new "rail mode"):** while burrowing, when the point reaches/where a rail endpoint meets the
  surface, snap onto the rail and follow it parametrically A→B (or B→A). Speed/accel reuse the existing
  burrow tuning.
- **Handoff (the tricky seam):** entry when a surface-burrowing player hits a rail endpoint sitting on the
  solid surface (the "hole"); exit at the other endpoint, which should also sit on a surface to hand back
  to surface-following (or emerge). Endpoints-at-surfaces is the contract that makes tunnels enterable.
- **Chaining:** at an endpoint that coincides with another rail's endpoint, continue onto it. A small
  "find the rail whose endpoint is here" query. Multiple → junction (see open questions).
- **Cannot pop out:** suppress emerge / jump-eject when the exit cell is solid. Only endpoints (at surfaces)
  let you emerge. Interior dead ends should reverse the player (treat as authoring error).
- **Normal on an interior rail:** ambiguous (solid both sides) but only cosmetic — take perpendicular-to-
  travel on the `solidLeft` side for the squash/glow.

## Open questions (before building)

1. **Curve organically as one object, or is chaining straight segments enough?** → B vs C.
2. **Motion feel:** smooth diagonal burrow inside tunnels (free lines), or blocky like surface-burrowing
   (argues back toward edge flags)?
3. **Junctions:** allow branching (needs a tiebreak — prefer straight, else player steering) or keep
   simple non-branching paths (which "just work")?
4. **Visibility:** faint always-on line as the burrow visual language, or only visible while burrowing?
   (Leaning always-on — that's the whole "teach the player" point.)
5. **Does the rail *gate* burrowing or just *hint* it?** Keep cosmetic to start; the rail is where you
   *can* tunnel, not a restriction on ordinary surface burrowing.

## Related

- Visual-language / "burrowable hole" discussion — the hole decal that reads as an entrance, and why a
  `RevealDoor` fake wall (skinned air) hid the decal (coverage clip + draw order + reveal fade). Rails are
  the "make it a persistent, authored language" answer to that.
- `TileFlags.Breakable` is the template for any per-cell-flag route (option A).
