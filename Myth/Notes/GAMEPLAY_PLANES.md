# Gameplay Planes, Caves, and Glass Pipes

Status: **design notes and implementation plan; prove with an incremental glass-pipe vertical slice.**

## Goal

Let one logical 2D room contain overlapping gameplay spaces which share screen/world
coordinates but have different collision topology. Examples:

- A glass pipe crossing the main room. Actors inside and outside remain alive and visible,
  but collide only with geometry and actors in their current space. The pipe ends transfer
  actors between spaces.
- A back cave entered through a door. The cave becomes the active space while the main
  space remains visible as a blurred background.
- Player, Bud, and eligible enemies can move between spaces. This is not just a player-only
  presentation trick.

Use **space** or **plane** in code rather than `Layer`: `Factory.TileLayer` already means the
foreground/background rendering strata inside one collision space.

The central architectural idea is that a gameplay plane must not become one label controlling
everything. Keep these concepts independent:

- **Space:** what geometry and actors an actor can touch.
- **Render domain:** which spaces are drawn together and in what order.
- **Lighting domain:** which ambient environment and lights illuminate a render contribution.
- **Portal:** how actors move between spaces.

That separation makes the motivating cases straightforward:

| Gameplay space | Collision | Render domain | Lighting domain | Visibility |
|---|---|---|---|---|
| Main | Main | Physical room | Physical room | Always |
| Glass pipe | Pipe | Physical room | Physical room | Always |
| Back cave | Cave | Cave | Cave | While active |
| Main behind cave | Main | Background composite | Physical room | While cave is active |

## Core decisions

### Sibling `Room` assets are the initial authoring format, not the runtime abstraction

Initially author each gameplay space as a normal `Room` rather than nesting several new
tile/collision models inside `Room`. A secondary room points at its logical parent:

```json
{
  "planeOf": "glass_pipe_test",
  "planeId": "pipe",
  "position": [0, 0],
  "size": [40, 23]
}
```

The parent room is the implicit `main` plane. Plane rooms initially have the same position
and size as their parent. This restriction keeps positions, camera bounds, portal mapping,
and rendering simple; relax it only for a concrete room that needs it.

This storage representation reuses, per plane:

- Foreground/background tiles and tile flags.
- Collision rectangles, slopes, water, breakables, and crumble state.
- Actor specs, cloning, hot reload, and stable room IDs.
- Lighting and render caches.
- Existing room editor serialization and undo targets.
- Save/session destruction identity.

Plane rooms must be excluded from world adjacency searches and displayed as part of their
parent on the minimap/world editor rather than as overlapping physical rooms.

The loader resolves these files into runtime `Space` objects. Gameplay code should depend on
`Space`, render code on render/lighting domains, and neither should care whether the editor later
stores planes as sibling rooms, nested JSON, or another format. This avoids making a storage
shortcut into a permanent engine boundary.

### Ownership remains flat; spatial topology is a graph

Do not build an actor scene tree or DAG for this. `Game.Actors` remains the ownership/update
list. Every spatial actor gains a runtime reference to its current plane, and portals form a
graph between planes.

Conceptually:

```text
RoomScene
  main Space ── portal ── pipe Space
       │                       │
       └────── portal ─────────┘

Game.Actors: Player(main), Bud(main), Enemy(pipe), Light(main), ...
```

An actor transfers by changing its current plane and position. It is not destroyed and
recreated. Its original room/actor ID remains its persistence identity.

### Simulation membership and presentation are separate

An actor's plane determines:

- Room geometry it collides with.
- Other actors it can collide with, damage, perceive, or target.
- Water, surface, hazard, and navigation queries.

It does **not** by itself determine whether the actor is drawn. Several planes may be visible
and updating simultaneously.

Presentation determines:

- Which planes are visible.
- Their front-to-back composite order.
- Whether another plane is blurred, dimmed, tinted, masked, or drawn normally.
- Which plane supplies the active foreground treatment and audio perspective.

This separation is what permits an enemy outside a glass pipe to keep moving visibly without
colliding with the player inside it.

## Runtime model

### Loaded room scene

Replace the assumption that `Game` has one loaded runtime `Room` with a logical scene:

```text
RoomScene
  ParentId
  Spaces[]
  RenderDomains[]
  LightingDomains[]
  Portals[]
  PlayerSpace
```

Each `Space` may be populated from a cloned sibling `Room` and owns its collision geometry,
navigation, water, and current actor membership. `Game.CurrentRoom` can continue to mean the
player's source plane room during migration, but code which means the physical/logical room should
use `RoomScene` or its parent ID explicitly. Edge and linked-door transitions unload/load a whole
scene. A portal within a scene does neither.

All loaded planes update every frame. Only visible planes render. Hidden planes may eventually
sleep as an optimization, but correctness comes first.

### Actor plane membership

Add a non-serialized runtime plane reference to `Actor`, assigned when its actor spec is spawned.
Keep a distinct immutable spawn-plane/source identity for save keys; an enemy walking through a
pipe must still count as the same enemy it was before transferring.

Non-spatial/system actors should opt into a clearly named global scope. Do not make `null` mean
both "main plane" and "all planes."

Useful invariants:

- Player always has exactly one current plane.
- Bud and enemies have exactly one current plane while spatially active.
- Tile/grid actors belong to the plane whose room generated them.
- Two spatial actors interact only when their plane references match.
- An actor's spawn identity does not change when its current plane changes.

Avoid string comparisons in per-frame queries. Resolve serialized IDs to runtime plane references
when the group loads.

### Collision and actor queries

The safest migration is to make the querying actor the source of spatial context:

```csharp
OverlapsRoomCollision(actor, offset)       // uses actor's current plane geometry
OverlapsFirst(actor, rect, mask)           // filters candidates to actor's plane
OverlapsAll(actor, rect, mask, results)    // likewise
```

Rect-only queries must eventually take an explicit plane. A global `ActivePlane` default is useful
only as a temporary compatibility bridge: enemies updating in a visible inactive plane would
otherwise accidentally collide with the player's geometry.

Audit at least:

- Pawn movement and solid actors.
- Player attacks, hazards, pickups, and enemy damage.
- Enemy perception and projectile targeting.
- Burrow surface/line queries.
- Water and surface-material queries.
- Moving platforms, ropes, switches, and doors.
- Hidden-cover and destructible geometry.
- Camera blockers and room-boundary checks.

Filtering must happen in the shared query primitives where possible, not independently in every
enemy. This is both smaller and harder to get wrong.

### Portals

A `PlanePortal` actor connects two portal actors by target actor ID, similar to `LinkedDoor`:

- Source and target plane are implied by the actors containing each portal spec.
- Position maps relative to the paired portal, allowing paired endpoints without requiring identical
  coordinates.
- Velocity and movement state are preserved by default.
- A short per-actor portal cooldown prevents immediate ping-pong.
- Portal style controls whether transfer is seamless, interactive, or animated.
- Eligibility starts with Player, Bud, and selected enemies; projectiles/props can be added when a
  room needs them.

Two initial styles cover the motivating cases:

- `Seam`: crossing a pipe endpoint transfers immediately with no room transition.
- `Door`: interacting transfers with a short visual/audio transition.

The portal should expose `CanTransfer(Actor)` and `Transfer(Actor)` rather than containing special
cases for every actor type. Actors may veto transfer when their state cannot survive it.

For the first version, transfer Bud with the player if he cannot reach the portal reliably. The
desired end state is for Bud to navigate to and cross the same portal himself.

## Rendering and lighting

### Render domains collect spaces; spaces do not automatically get separate passes

The current renderer builds background/foreground color, emissive, normal, lighting, tile-field,
and reflection results around one active room. A `RenderDomain` generalizes that pass by collecting
ordered visual contributions from one or more spaces.

For the glass case, render main and pipe into the same domain:

```text
PhysicalRoom render domain
  main background
  main terrain and actors
  pipe interior and actors
  glass shell/refraction
  shared lighting composite
```

This keeps all actors live, gives them coherent lighting, and need not cost two complete world
renders. Collision membership is irrelevant to this draw order.

For the cave case, main and cave use separate render domains. Render the live main domain, blur/dim
its finished result, then composite the cave domain over it. Render domains—not gameplay spaces—are
the unit which receives an offscreen target and later composition.

Reuse targets and scratch actor lists; do not allocate them per frame. Measure the two-domain cave
case on browser and Apple Silicon before generalizing further. Static geometry caches remain per
source `Room`; a domain with no dynamic visual changes can later cache more aggressively.

### Sharing lighting is configurable and has three meanings

"Share lighting" can mean independently:

1. Share ambient color, intensity, sun, and noise.
2. Let the same dynamic lights illuminate both spaces.
3. Let geometry in one space cast shadows/occlusion onto the other.

The glass pipe likely wants the first two. The third may produce false shadows because main and pipe
geometry overlap only in projection. Start with main and pipe contributing to one lighting pass; if
the occlusion looks wrong, separate the light list/environment from the occlusion contributors
rather than separating collision spaces again.

Useful implementation choices, from simplest to most isolated:

- Draw main and pipe into the same albedo/normal/emissive buffers and light once. Fast and coherent;
  all shadowing is shared.
- Render separate geometry buffers but apply the same resolved light list and ambient environment.
  More control, approaching two lighting passes.
- Render the main light field once and sample it while compositing the pipe. Cheap and plausible for
  pixel art, but pipe-local normals and shadows are approximate.

Do not put a `LightingPlaneId` on every actor unless content proves it necessary. Prefer actors to
inherit their space's render and lighting domains, with rare explicit overrides.

### Presentation data

Start with concrete fields instead of an elaborate material graph. A secondary plane needs enough
information to describe:

- Composite order.
- Visibility while inactive.
- Backdrop/source plane when active.
- Backdrop blur/dim amount.
- Whether its backdrop is transparent.

Likely first presets:

- **Glass:** main and pipe always visible; pipe shell/interior composites over main without blur.
- **Back cave:** cave hidden while inactive; while active, main is visible blurred/dimmed behind it.

Presentation belongs to the room scene's render domains, not portal behavior. Entering the same pipe
through either end should produce the same rendering.

### Actors, particles, light, and audio

- Render actors with their current plane, not their spawn plane.
- Particle emissions need plane membership so sparks do not appear in every composite pass. Until
  particles carry it, route plane-specific effects through per-plane particle systems or tag emission
  batches with a resolved plane reference.
- Lights belong to a lighting domain. Main and glass pipe share one by default; the cave can use its
  own. Occlusion contributors may later be filtered independently if shared shadows look spatially
  false.
- The listener belongs to the player's plane. Inactive visible planes can begin muted; later add a
  presentation-level audio gain/muffle value for glass if hearing outside activity improves the effect.

## Navigation and AI

Build one `PlatformNavGraph` per runtime plane. Add portal edges above those graphs rather than
merging all tile geometry into one collision graph.

A navigation location becomes `(plane, node)`. A cross-plane route is:

```text
local path to portal -> portal edge -> local path in target plane
```

Incremental behavior:

1. Enemies continue simulating and using their local plane graph, but do not deliberately pursue
   targets through portals. They may transfer when their ordinary movement crosses an eligible seam.
2. Bud receives a reliable fallback transfer with the player.
3. Add portal edges so Bud can independently follow the player between planes.
4. Enable cross-plane enemy pursuit only for enemy types whose design benefits from it.

Perception, attack, and target-selection queries must reject actors in another plane even when their
screen coordinates overlap. Cross-plane awareness should be explicit—e.g. an enemy may see the
player through glass and decide to seek a portal, but cannot strike through the glass unless a later
mechanic says it can.

## Persistence, transitions, and world systems

- Save/respawn records store the current plane room ID. Because planes are ordinary rooms, this can
  largely reuse the existing `RoomId` representation.
- Persistent destruction uses the actor's spawn plane and stable actor ID, not its current plane.
- Runtime tile destruction belongs to the affected plane room.
- Rewind snapshots must include player plane membership and handle crossing a portal in reverse.
- Story conditions should distinguish logical parent room from active plane where useful.
- The minimap initially reveals one logical room footprint. It may later show interior/pipe detail as
  a sub-layer, but secondary planes must not double-count exploration area.
- Edge transitions operate from the player's plane but load the adjacent logical room group. Secondary
  planes should not participate in ordinary world adjacency unless explicitly linked.
- Camera bounds are shared across the group initially. This is another reason to require matching
  plane bounds in the first version.

## Editor

The plane selector is orthogonal to the existing Background/Foreground tile selector:

```text
Plane: Main | Pipe | Back Cave | +
Tiles: Background | Foreground
```

Keep editing one ordinary `Room` at a time, which preserves existing painting, actor editing, undo,
copy/paste, and save paths. Add only the group-aware affordances:

- Switch active plane without moving the camera.
- Ghost one or more sibling planes behind the active edit plane.
- Toggle actor selection from inactive ghost planes.
- Create a plane by making a same-bounds child room with empty grids.
- Hide plane children as separate rectangles in the world-layout editor.
- Validate unique `planeId`, matching initial bounds, valid portal targets, and portal pairs.

Runtime play-from-editor must clone/load the whole plane group, not only the currently edited child.

## Alternative models considered

### Multiple collision grids directly inside one `Room`

One logical room could serialize `Main`, `Pipe`, and `Cave` collision grids directly. Runtime actor
membership and shared lighting become very natural, but `Room` currently owns one foreground
collision grid and all of its derived geometry. This route immediately refactors cloning, tile flags,
water, editor painting, undo, and render caches. Sibling room files reach the same runtime model with
a cheaper first migration.

### Phase masks

Actors and geometry could carry bitmasks such as `Main = 1`, `Pipe = 2`, and
`Shared = Main | Pipe`. Shared doors or hazards become easy, but actors can accidentally belong to
ambiguous combinations and navigation/water still need one authoritative space. Keep one current
space per actor; add an explicit interaction mask later only if shared collision objects become common.

### Pocket worlds rendered through portals

The pipe or cave could be an entirely separate simulation rendered through a window/mask. This gives
excellent isolation and arbitrary coordinate transforms, including impossible interiors, but makes
actor transfer, camera transforms, recursive views, and lighting substantially harder. It solves more
than these initial rooms require.

### Actual Z lanes

A discrete or continuous Z coordinate would make planes literal depth. Collision could require a
compatible Z and render order could derive from it. This becomes attractive only if moving through
depth is itself a central player verb; otherwise it leaks into nearly every currently 2D position,
hitbox, navigation, particle, and editor assumption.

### Bespoke pipe/cave actor modes

A `GlassPipeActor` could temporarily put Player or an enemy into a custom traversal mode. This remains
a useful disposable feel prototype, but does not generalize to ordinary terrain, water, moving actors,
or caves without accumulating one-off collision rules.

### Chosen hybrid

Use sibling `Room` assets to author collision spaces cheaply, normalize them into explicit runtime
`Space` objects, and let those spaces contribute independently to render and lighting domains. This
keeps storage replaceable, gameplay queries explicit, and glass/cave presentation composable.

## Migration plan

### Phase 0: lock terminology and invariants

- Use `Plane`/`Space` for collision domains and retain `TileLayer` for foreground/background.
- Require equal bounds and shared world coordinates within a group.
- No cross-space collisions or attacks by default.
- Lighting is inherited from a configured lighting domain; main and glass pipe share one.
- All loaded planes simulate; visibility is independent.

### Phase 1: make existing single rooms plane-aware

- Give every spatial actor a runtime plane reference.
- Assign the current room to all actors and generated grids during `LoadRoom`.
- Make room collision use the actor's plane.
- Add actor-context query overloads and central same-plane filtering.
- Audit gameplay calls until a one-plane room behaves identically.

This phase should produce no visible feature. It creates the invariant on which everything else rests.

### Phase 2: load and simulate a room group

- Parse parent/plane metadata and discover sibling rooms.
- Clone sibling rooms and normalize them into runtime spaces in one `RoomScene`.
- Subscribe to geometry changes and update water/crumble systems per plane.
- Keep rendering only the main plane temporarily.
- Preserve existing whole-room edge and linked-door transitions by treating a group as the lifecycle unit.

### Phase 3: transfer actors through portals

- Add paired `PlanePortal` actors and per-actor transfer cooldown.
- Transfer Player, then Bud, then one simple walking enemy.
- Preserve position offset, velocity, facing, and safe movement states.
- Add plane membership to rewind snapshots.
- Verify saves and persistent destruction after actors have crossed planes.

### Phase 4: render two live planes

- Generalize the current render pass into a domain with ordered contributions from several spaces.
- Draw main plus pipe into one shared render/lighting domain with both actor sets moving.
- Put the procedural glass shell/refraction after the lit main/pipe contributions.
- Add plane-tagged particles or temporarily suppress effects that cannot yet be assigned correctly.
- Measure GPU cost and allocations in desktop and browser builds.

### Phase 5: minimum editor support

- Plane selector, ghost siblings, create plane, portal validation.
- World-layout/minimap grouping.
- Play mode loads the group.

### Phase 6: Bud and portal-aware navigation

- Build/cache a nav graph per plane.
- Add portal graph edges and `(plane, node)` routes.
- Remove the automatic Bud-transfer fallback once his portal following is dependable.
- Opt selected enemies into cross-plane pursuit.

### Phase 7: back-cave presentation

- Add inactive-hidden cave plane.
- Composite the live main plane behind it with blur/dim while the cave is active.
- Add audio perspective and transition polish only after the topology works.

## First vertical slice: `glass_plane_test`

Build one deliberately ugly test room before production art:

- Main plane with a floor, moving platform or patrolling enemy, Player, and Bud.
- Pipe plane crossing solid main geometry with two seamless portals.
- Main and pipe visible simultaneously.
- Main and pipe share an ambient environment and at least one dynamic light.
- Player enters one end and exits the other without colliding with main geometry while inside.
- Bud follows through both ends.
- An enemy continues moving visibly while the player is in the other plane.
- One eligible enemy crosses a portal and thereafter collides with pipe geometry, not main geometry.
- Player attacks cannot hit an overlapping enemy in another plane.
- Save/reload and rewind behave sensibly after a player portal crossing.

This slice is done when the spatial illusion works before glass shaders, blur, bespoke art, or broad
enemy navigation are added.

## Risks and deliberate limits

- **Rendering cost:** two fully lit live planes can approach two world renders. Measure early on the
  browser target. Prefer one shared glass domain; the separately composited cave is the expensive case.
- **Implicit-current-room bugs:** any unconverted global collision or target query can create invisible
  cross-plane interactions. Actor-context queries and a temporary `MYTH_PLANE_TRACE=1` diagnostic mode
  will make these failures legible.
- **Actor transfer edge cases:** moving platforms, ropes, bosses, and stateful projectiles may not be
  safe to transfer. Eligibility is opt-in until proven.
- **Authoring confusion:** plane and tile layer must remain visibly separate concepts in the editor.
- **Scope:** no arbitrary transforms, recursive portal views, cross-plane attacks, shared collision,
  or different plane bounds in the first implementation.

## Open design questions

1. Do main-space occluders cast shadows onto pipe actors, or do the spaces only share ambient and lights?
2. Through glass, should actors in another plane be audible normally, muffled, or silent?
3. Can an enemy perceive the player through glass and seek a portal before cross-plane navigation exists?
4. Should the pipe shell be authored in the pipe plane, the main plane, or as a shared presentation actor?
5. When Player enters a narrow pipe, does Bud always follow, wait outside, or depend on the specific portal?
6. Which objects beyond Player/Bud/enemies should eventually transfer: projectiles, pickups, movable props,
   particles, water?

None of these blocks the first three migration phases. Default to isolated interactions, explicit transfer
eligibility, and the smallest glass test that proves two live collision spaces can occupy the same screen.
