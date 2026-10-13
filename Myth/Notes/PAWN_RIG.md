# Pawn rig (Player / Bud drawn with HumanoidRigVisual)

## Lay of the land

- `Source/Actors/Pawn.Rig.cs`: all the glue. The **sprite state machine keeps running unseen**. Gameplay reads its animation names and durations, so never remove `Play(...)` calls. `PlayPawnAnimation(name)` plays the sprite animation and records a rig request via `RigClipFor(name)`. Each frame, `UpdateRig()` (called at the end of `Pawn.Update`) turns that request into what the rig actually shows.
- `Source/Animation/HumanoidRigVisual.cs`: the shared humanoid rig, also used by `Stalker`, `RigPuppet` and `EyeLaserPuppet`. Every new behaviour there is **opt-in through a `HumanoidRigSettings` field that defaults to off**, so the puppets look the same. Keep it that way: ELP is hand-tuned.
- `HumanoidRigSettings`: serialized, inspector-editable. `[NoSerialize]` flags (`TurnWhileMoving`, `GaitFeetFollowTerrain`) are set in code by Pawn.
- Pawn defaults: `Pawn.DefaultRig()`. Bud uses `Bud.BudRig()`, which starts from it and swaps the palette (derived from `Bud.Tint`) and the costume. Player and Bud are **spawned, not authored**, so inspector edits on them are live-only. Change the code defaults to keep anything.
- Sprite mode must keep working. Every rig path is guarded by `rigVisual != null` / `RigDrawing`. Toggle with `RigDraw` in the inspector.

## How a frame picks what to show (`UpdateRig`)

`shown = rigPreview ?? rigLead ?? clip`

- `clip` is the sprite request after substitutions: the dive (`PlayerAir` + jump held + `Velocity.Y >= 0` + `CanBurrow()`), and sprint (`Jog` above 1.15× `MaxGroundSpeed`).
- `rigLead` is a one-shot *transition* take chosen in `RigLeadFor(from, to)` when the request changes (jump start, jump land, roll start). It plays to its end, then the request resumes. The skid also "holds" through run/idle once `Facing` has flipped.
- `rigPreview` is forced by the Rig Animation Viewer.
- A name is either a **take** (looked up in `RigTakes`) or a **raw clip**. Takes are only `Play`ed on change, because `Play(take)` restarts the take. Raw clips are idempotent.
- Playback rate = `take.Speed ×` a state factor: gait clips scale with ground speed, the roll with roll speed, the climb only advances while climbing up.

## Takes (`Assets/Rigs/ual2.takes.json`)

- Named clip ranges plus Speed/Loop/FootIK/lean. The `Player*` takes are the pawn's vocabulary. Deleting a transition take simply disables that transition (`TryGet` fails, so it's skipped).
- **Tune live:** play, double-click the Player, open Rig Animations. You get "Playing: X → Edit", plus "Preview on pawn". Edits go to `RigTakes.Set` (in memory) and the pawn picks them up the next frame. **Save take** writes the JSON. Hand edits to the JSON need a restart (takes load once).
- The viewer's puppet branch (`RigPuppet`) and pawn branch share the take controls. `rig == null` means "set live" instead of "preview on puppet".

## Adding a Mixamo clip

1. `tools/rig-baker/retarget-mixamo.sh ~/Downloads/"Clip Name.fbx"` writes `tools/rig-baker/mixamo/Clip_Name.glb`. It prints "Loop delta": pelvis cm plus the worst bone rotation between the first and last frames.
2. Strip root motion in `retarget-mixamo.json` (the game's physics moves the body):
   - `"InPlace": "Linear"`: removes net horizontal drift and keeps surges (use for loops).
   - `"InPlace": "Lock"`: pins horizontal.
   - `"InPlaceVertical": "Linear" | "Lock"`: the same for height (climbs, leaps).
   - `"StripYaw": true`: removes the whole-body turn (turn clips; the game owns facing).
   - `"Ground": true`: aligns the toes to the reference ground.
3. `tools/rig-baker/bake-ual2.sh` rebuilds `Assets/Rigs/ual2.ozzpack`. Check the clip count in the last line.
4. Add a take, and map the sprite state in `RigClipFor`. Names without `_Loop` need a take with `Loop: true` to loop.
- Finding ranges: parse the `.glb` directly with small Python scripts (struct + JSON chunk). Look at pelvis pitch over time to find rolls, dives and horizontal moments. Compare joint rotations frame-to-frame to find clean loop points.
- Mixamo speeds rarely match the game's (climb would need 23×, crawl about 3×). Pick a readable rate rather than an exact distance match.

## Traps (all hit today)

- **Fast clips looked flattened** because every joint is a ragdoll spring (~2 Hz) chasing the animation. Pawns use `RagdollCarry = 1`, so joints ride the animation and the springs only add reactions. ELP is at 0.
- **Foot IK snapped lifted toes to the ground** and flattened the stride. With `GaitFeetFollowTerrain`, gait feet only shift by the terrain delta.
- **Double turns:** a clip that turns 180° plus the rig's facing flip ends up backwards. Bake with `StripYaw` and drive yaw procedurally (`YawOverride`); the skid does this.
- **Fixed rotation angles undershoot** when the take's pose isn't what you assumed. Measure and aim instead (`ArmAim` rotates the figure so neck→hands points where asked).
- **Visual FPS at or above the frame rate** now does one update per frame using the real delta. Previously it ran several fixed steps per frame and threw away all but the last. Pawns use 240, meaning every frame.
- **Rig-vs-sim alignment:** the rope pins at the rig's hands (`RopeGrip`), and the rig slides so its hand is on the rope. The crawl lifts the rig 2px (`UpdateRigOffset`). Expect similar per-pose offsets.
- **Live selection of spawned actors** goes through `liveInspectedSpawned`. `TryGetSelectedLiveActor` only knows actors authored in room JSON.
- **Hot reload** rejects new fields and methods. Restart after structural changes.
- **Costumes at 12 px/m:** pieces under ~2px vanish, and the neck is nearly invisible. Shoulders and trunk get the pixels; use value contrast, not hue.
- **Don't compare against a clean tree with `git stash`** (see memory). Back the file up in the scratchpad and restore it.

## Verifying without eyes on the game

- Dev host: `dotnet bin/Debug/net10.0/Myth.dll --dev-host` (background), then `--dev-command` with `run`, `play [World/room]`, `warp x y`, `input <left|right|up|jump-up|jump-right|roll-right|none>`, `state`, `shot path.png`, `stop`. Each command takes ~0.3s, so short actions (a jump) are hard to screenshot.
- Better: add a temporary `Console.WriteLine("[TAG] ...")` in `UpdateRig` (head bob range, hand position, roll angle), run, grep the host log, then restore. Numbers beat 19px screenshots.
- Screenshots are 6016×3320. Crop around the player with PIL and resize with `NEAREST`. Frame 0 is often the camera before it settles, so poll `state` a dozen times first.
- Ropes to test on: `Playground/start` (warp `363 460` with `input up` grabs one).
- Check ship builds too: `dotnet build -p:MythShip=true -o <scratch>`. Editor-only APIs (`RigTakes.Set`) must stay behind `#if EDITOR`.

## Tunables (live via getters in `Pawn.Rig.cs`)

Gait/sprint rates, `RigRollMinRate`, `RigLandingScale`, head-look turn/rate, `RigSkidYawRelease`, `RigRopeGripHeight` (meters), `RigCrawlLift`, `RigDiveFullAngleSpeed` / `RigDiveAngleResponse`. Settings that are worth opening in the inspector: `VelocityLean`, `SquishResponse`, `TurnDuration`, `RagdollCarry`, `VisualFPS`.

## Open ideas

Tilt the rig with the rope's angle on a swing. Distance-matched gait rates. Prone duck (avoids the duck↔crawl pop). `Leaning` / `Walking_Turn_180` are baked but unused. Footstep audio for Bud. Full clip preview on pawns (refactor `RigPuppet`'s preview into a shared helper).
