# Myth as an Iteration Base

Fresh codebase review, 2026-07-14. This is based on static inspection of the editor, game loop, actors, asset pipeline, renderer, profiling tools, and desktop/iOS/browser build paths. It is not a substitute for profiling a representative heavy room on each target.

## Verdict

Myth is already a strong base for rapid gameplay and graphics work. Its main virtue is that most important behavior is reachable by following direct calls and reading ordinary mutable state. The flat actor list, concrete actor types, explicit update order, explicit render passes, integrated room editor, source-generated registration, and JSON room data all fit the game better than a general-purpose entity/component or service architecture would.

The current risk is not insufficient architecture. It is accretion: more editor workflows in one file, more render channels in every actor, more reload paths with subtly different behavior, and more small linear scans whose cost remains invisible until one particular room makes them matter. The best response is to improve visibility, preserve directness, and make only the smallest structural changes that delete repeated work or prevent an observed class of bug.

I would not rewrite the foundation.

## What is working especially well

### The simulation is boring in the good sense

`Game.Update` has an understandable order: global pre-update work, a linear actor update, then explicit post-update systems. Actors are concrete objects with direct fields and virtual behavior. Queries are mostly linear scans over `Actors`. At Myth's apparent scale this is likely faster to change, easier to debug, and plenty fast enough.

This is a better default than introducing an ECS, dependency injection, a message bus, or a generic scheduler. Those would turn local gameplay changes into multi-file protocol changes without evidence that iteration or runtime performance would improve.

The headless `Game` / GPU-owning `GameView` split is a good example of an abstraction earning its keep. It supports the gym and other simulation-only work while making graphics-resource lifetime explicit. It solves a concrete problem rather than anticipating one.

### The editor is part of the game, not a second application

The editor uses the same rooms, actor specs, factory registrations, game renderer, and preview simulation as play mode. That is a major iteration advantage. Undo, direct manipulation, room previews, actor palettes, particle tuning, NCA previews, and play-in-place are already close to the authored data.

`SyncEditorGame` reuses an editor-preview `Game` where possible, and `RenderTargetPool` prevents repeated construction from constantly reallocating the renderer's target set. These are targeted optimizations around measured workflow pain, which is exactly the right style.

### The feedback machinery is unusually good

There are focused hot-reload paths for art, shaders, particle effects, audio banks, actor specs, and rooms. `.T()` provides source-local live tuning in debug builds. The browser smoke/screenshot tools, headless gym, CPU/allocation scripts, render-target viewer, pass timers, and feature-disable toggles are all valuable force multipliers.

These tools are more important to iteration speed than another layer of runtime architecture. They should be treated as core engine features.

### Rendering is explicit and measurable

The renderer is a long sequence of concrete passes, but the sequence can be read, timed, disabled, and debug-labeled. That is preferable to a generic render graph while the game's visual vocabulary is still changing. On a 2D game with desktop, mobile, and WebGPU targets, explicit knowledge of target changes and composition is useful.

The uniform collection expressions passed to `ReadOnlySpan<float>` are not automatically evidence of heap allocation; their target type permits stack/static lowering. Keep measuring rather than mechanically replacing modern syntax with uglier code.

## Where iteration will get slower

### 1. Feedback latency exists, but is not treated as a first-class metric

There are many good reload paths, yet success and failure mostly appear in the console or as short in-game messages. The meaningful developer metric is not just frame time; it is time from save or edit to a trustworthy visible result.

The paths are also semantically different. Some mutate assets in place, some rebuild geometry, some recreate `Game`, some preserve state, and some invalidate cached previews. The recent NCA editor-preview issue after leaving play mode is exactly the kind of seam this creates: each individual path is reasonable, but it is easy for one transition not to replay the same settling behavior as another.

Small recommendation: add one compact "iteration status" section to the existing debug UI. Track the last requested/completed reload kind, duration, success/error, and preview sync reason. Do not build a job system. A few timestamps and a bounded error string would make stale previews and slow reloads obvious.

Also write down and manually verify a tiny transition matrix for editor preview behavior:

| Trigger | Expected result |
| --- | --- |
| Edit actor property | Live preview syncs and settles |
| Paint NCA influence | Selected preview simulates configured steps |
| Change room | Preview game switches or rebuilds and settles |
| Enter then leave play | Editor preview is recreated and settles |
| Reload asset/spec/shader | Relevant preview invalidates and visible state updates |

This is more useful than a generalized lifecycle/event framework.

### 2. `Manager.Editor.cs` is becoming a navigation tax

At roughly 5,800 lines it contains several distinct workflows: palette, actor/spec inspectors, particle editing, NCA model selection and painting, room geometry, tile selection and painting, cached previews, undo targets, navigation, and file watchers. Its size is not a runtime problem and the shared direct state is often convenient. It is, however, increasingly hard to answer "where does this editor behavior live?" and easy for unrelated merge conflicts to accumulate.

Split the partial class by stable workflow when one is next touched, without inventing controller/view-model objects:

- `Manager.Editor.Palette.cs`
- `Manager.Editor.Inspector.cs`
- `Manager.Editor.RoomPreview.cs`
- `Manager.Editor.World.cs`
- `Manager.Editor.Reload.cs`

This is a source-layout change, not an architecture. Move fields with their workflow when practical, retain direct calls, and do not perform a flag-day reorganization merely to reduce line counts.

### 3. The render-pass contract leaks into every actor

The growing `Render`, `RenderNormal`, `RenderEmissive`, `RenderShadow`, and `RenderReflection` family makes an actor repeat visibility, frame, transform, and ordering decisions. It also means a new graphics channel becomes a cross-cutting actor API change. `VISUAL_COMMAND_BUFFER.md` correctly identifies the pressure, but its proposed full migration would be a substantial system change.

I would take a smaller first step when the next real duplication bug appears: compute the base sprite's frame and visual transform once per rendered frame, or introduce a narrow base-sprite snapshot consumed by the existing passes. Leave bespoke actor drawing bespoke. If three or more concrete bugs still stem from inconsistent multi-pass visual declarations, the command buffer has earned a staged migration. Until then, explicit virtual methods remain easy to understand.

`BATCHER_STATE_SCOPES.md` is a more immediately justified change because it follows an actual leaked-state bug. Debug assertions at `Batcher.Clear`, plus allocation-free scopes added only for commonly leaked stacks, give high leverage without redesigning rendering.

### 4. Performance cliffs are simple but currently unbudgeted

The flat actor representation is appropriate, but several operations scale linearly or worse:

- `GetFirst<T>` scans actors and calls `destroying.Contains` for candidates.
- Destroying several actors calls `Actors.Remove` once per actor.
- Actor overlap queries scan the whole actor list.
- Room geometry rebuild allocates a fresh `bool[,]` in each `BuildRects` call and invokes several passes in parallel on non-browser targets.
- Debug `.T()` calls perform a dictionary lookup each time a tweakable property is read.

None of these deserves replacement from static inspection alone. Player constants and actor counts are small; geometry rebuilding is authoring/loading work; a spatial index has maintenance costs. The danger is that no threshold states when the simple version has stopped being simple enough.

Add a representative "heavy room" scenario and record, at minimum:

- update and render milliseconds;
- managed bytes allocated per gameplay frame;
- actor count and collision-query count;
- render-pass/target count;
- room rebuild and editor-preview rebuild time.

Run it occasionally in Release on desktop, the browser, and a real iOS device. Budgets can initially be generous; their value is detecting a discontinuity. Optimize only the counter that moves. If actor lifecycle becomes visible, an `Actor.Destroying` bit plus one compaction pass is probably enough. If collision scans become visible, index only the masks that are hot. Do not begin with a universal scene index.

### 5. GPU bandwidth is a likelier shipping limit than C# dispatch

The graphics pipeline uses many full-size intermediate targets and passes for normals, emissive, reflections, lighting, shadows, post effects, and final composition. On desktop this can be cheap; on mobile and WebGPU, target switches and bandwidth can dominate even while CPU timings look healthy.

The existing pass toggles and timers are the right starting point. Add a named visual quality preset only after target profiling identifies a bundle of expensive passes. Avoid a generic render graph or automatic pass scheduler. A boring ordered list with explicit half-resolution targets or skipped effects is easier to tune and reason about.

Render-target pooling is useful, but its process-wide cache can retain substantial GPU memory across sizes. The per-key cap prevents an individual size from growing without bound, not the total set of sizes. This only needs action if device memory or room-size churn shows a problem; then expose pooled target count/bytes and add a total byte cap or trim point.

### 6. Asset reload is powerful but not fully transactional

The visual bake is prepared before replacing the atlas, which is good. Once `LoadVisualAssetPackManifest` starts, however, it mutates global dictionaries and live sprite/tileset objects before disposing old textures. A failure in the middle could leave a mixed generation. `Manager.ReloadAssets` catches and reports the exception, but catch-and-continue does not guarantee a coherent old state.

Do not build a general asset database. First make reload failures prominent and keep the last error visible. If partial failures are observed in practice, build the new atlas plus complete manifest-derived lookup state off to the side, then swap the small set of roots. Preserve stable `Sprite` identities only where live actors demonstrably require them.

## A proportional next-action list

In priority order:

1. Add visible timing/status/error reporting around existing reload and editor-preview sync paths.
2. Create one repeatable heavy-room performance scenario and record per-target baselines.
3. Add debug validation for unbalanced `Batcher` state; add scoped helpers only as call sites need them.
4. Split `Manager.Editor.cs` into partial files opportunistically by workflow, with no new object model.
5. Measure editor/play transition time and preview rebuild time before further lifecycle changes.
6. Fix verified per-frame allocations using owner-local reusable scratch collections. Do not introduce a global scratch allocator.
7. Profile pass bandwidth on browser and iOS before changing the render topology.
8. Revisit the visual command buffer only after another concrete multi-pass consistency problem, and migrate the base sprite path first.

## Things I would explicitly not build now

- An ECS or generic component registry.
- Dependency injection or service interfaces around concrete subsystems.
- A global event bus to replace readable direct calls.
- A generic render graph.
- A universal spatial database for actors.
- A background asset job system before reload duration proves it is needed.
- A data-driven behavior language. Keep authored configuration in JSON and behavior in C#.
- Unit-test abstractions around every actor. Prefer headless scenarios, screenshot comparisons, assertions, and reproducible rooms.

## Rule for future abstractions

Before adding one, ask:

1. Does it solve a bug or repeated edit that has happened at least twice?
2. Does the first useful change delete or simplify existing code, not just add a parallel path?
3. Does it shorten edit-to-visible time, make failure obvious, or improve a measured budget?
4. Can a gameplay programmer understand the resulting call path in one sitting?
5. Can it be removed locally if the experiment is wrong?

If the answer is mostly no, keep the direct code. Myth's current advantage is that the game is visible in the program. Protect that.
