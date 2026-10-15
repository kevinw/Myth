# Pawn rig (Player / Bud drawn with HumanoidRigVisual)

## Lay of the land

- `Source/Actors/Pawn.Rig.cs`: all the glue. The **sprite state machine keeps running unseen**. Gameplay reads its animation names and durations, so never remove `Play(...)` calls. `PlayPawnAnimation(name)` plays the sprite animation and records a rig request via `RigClipFor(name)`. Each frame, `UpdateRig()` (called at the end of `Pawn.Update`) turns that request into what the rig actually shows.
- `Source/Animation/HumanoidRigVisual.cs`: the shared humanoid rig, also used by `Stalker`, `RigPuppet` and `EyeLaserPuppet`. Every new behaviour there is **opt-in through a `HumanoidRigSettings` field that defaults to off**, so the puppets look the same. Keep it that way: ELP is hand-tuned.
- **Units:** body sizes (`HeadSize`, `TorsoSize`, … `FootSize`, `NeckExtension`) are meters, scaled by `PixelsPerMeter`, so zooming keeps proportions. Outlines, eye and joint-radius floor stay in pixels.
- `HumanoidRigSettings`: serialized, inspector-editable. `[NoSerialize]` flags (`TurnWhileMoving`, `GaitFeetFollowTerrain`) are set in code by Pawn.
- Pawn rig settings are data: `Assets/Actors/humanoid.json` (shared body, motion, outline; no `Type`, so it never spawns) ← `player.json` / `bud.json` (costume, Bud's palette). `Game.SpawnPlayer` and `EnsureBudForPlayer` spawn from those prefabs. In the live inspector, **Apply to Prefab** writes each edited value to the prefab that sets it (e.g. `HeadSize` → Humanoid), else to the actor's own prefab, and saves. Other live actors pick it up on respawn.
- Sprite mode must keep working. Every rig path is guarded by `rigVisual != null` / `RigDrawing`. Toggle with `RigDraw` in the inspector.

## How a frame picks what to show (`UpdateRig`)

`shown = rigPreview ?? rigLead ?? clip`

- `clip` is the sprite request after substitutions: the dive (`PlayerAir` + jump held + `Velocity.Y >= 0` + `CanBurrow()`), and sprint (`Jog` above 1.15× `MaxGroundSpeed`).
- `rigLead` is a one-shot *transition* take chosen in `RigLeadFor(from, to)` when the request changes (jump start, jump land, roll start, crouch↔crawl, crouch to stand). It plays to its end, then the request resumes. The skid also "holds" through run/idle once `Facing` has flipped.
- `rigPreview` is forced by the Rig Animation Viewer.
- A name is either a **take** (looked up in `RigTakes`) or a **raw clip**. Takes are only `Play`ed on change, because `Play(take)` restarts the take. Raw clips are idempotent.
- Playback rate = `take.Speed ×` a state factor: gait clips scale with ground speed, the roll with roll speed, the climb only advances while climbing up.

## Takes (`Assets/Rigs/ual2.takes.json`)

- Named clip ranges plus Speed/Loop/FootIK/lean. The `Player*` takes are the pawn's vocabulary. Deleting a transition take simply disables that transition (`TryGet` fails, so it's skipped).
- **Tune live:** play, double-click the Player, open Rig Animations. You get "Playing: X → Edit", plus "Preview on pawn". Edits go to `RigTakes.Set` (in memory) and the pawn picks them up the next frame. **Save take** writes the JSON. Hand edits to the JSON hot-reload too (editor builds).
- The viewer's puppet branch (`RigPuppet`) and pawn branch share the take controls. `rig == null` means "set live" instead of "preview on puppet".

## Adding a Mixamo clip

0. On Mixamo, download FBX. For locomotion (runs, walks), tick **In Place** and step 2 is usually unnecessary.
1. `tools/rig-baker/retarget-mixamo.sh ~/Downloads/"Clip Name.fbx"` writes `tools/rig-baker/mixamo/Clip_Name.glb` (spaces and dashes become `_`; that's the clip name). It takes several files at once. It prints "Loop delta": pelvis cm plus the worst bone rotation between the first and last frames. Near 0 cm and 0°: skip step 2.
2. Strip root motion in `retarget-mixamo.json`, then rerun step 1 (the game's physics moves the body):
   - `"InPlace": "Linear"`: removes net horizontal drift and keeps surges (use for loops).
   - `"InPlace": "Lock"`: pins horizontal.
   - `"InPlaceVertical": "Linear" | "Lock"`: the same for height (climbs, leaps).
   - `"StripYaw": true`: removes the whole-body turn (turn clips; the game owns facing).
   - `"Ground": true`: aligns the toes to the reference ground.
3. `tools/rig-baker/bake-ual2.sh` rebuilds `Assets/Rigs/ual2.ozzpack` from every `.glb` in `mixamo/` (delete one and rebake to drop it). Check the clip count in the last line. The running game reloads the pack, but only while it's awake: an unfocused window parks it, so click in (or `--dev-command wake`).
4. Add a take, and map the sprite state in `RigClipFor`. Names without `_Loop` need a take with `Loop: true` to loop. Idle fidgets are one-shot takes listed in `RigFidgets`.
- Finding ranges: parse the `.glb` directly with small Python scripts (struct + JSON chunk). Look at pelvis pitch over time to find rolls, dives and horizontal moments. Compare joint rotations frame-to-frame to find clean loop points.
- Mixamo speeds rarely match the game's (climb would need 23×, crawl about 3×). Pick a readable rate rather than an exact distance match.

## Posing a clip in Blender

- `tools/rig-baker/pose.sh WallSlide --wall 0.33 --loop 40` opens `Assets/Sources/Models/UAL2_Standard.blend` on the action `Pose_WallSlide`, in Pose mode with auto-IK and auto-keying on. **Ctrl+S** saves and re-bakes the pack (every `Pose_*` action is exported). Restart the game to see it.
- `--loop 40`: a looping two-pose sway. Edit frame 1 (pose A) and frame 20 (pose B); each save copies frame 1 onto frame 40 so the loop closes. Pass it every time you open a loop.
- `--wall 0.33`: a wireframe wall, in meters, in front of the body (the rig faces -Y). 0.33m is the player's hitbox edge at 12 px/m. It's removed before every save.
- `--from Action`: a new pose starts from that action's first frame. `Pose_WallSlide` was seeded from `Idle_Rail_Loop`.
- The game shows the profile: work in the Right view, where he faces left toward the wall line.

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

- Dev host: `dotnet bin/Debug/net10.0/Myth.dll --dev-host` (background), then `--dev-command` with `run`, `play [World/room]`, `warp x y`, `input <left|right|up|jump-up|jump-right|roll-right|none>`, `state`, `set player Rig.PixelsPerMeter 40` (dotted paths reach nested settings), `shot path.png`, `stop` (stops the host). `play` saves the room the host booted into, so check `git status` afterwards. Each command takes ~0.3s, so short actions (a jump) are hard to screenshot.
- Better: add a temporary `Console.WriteLine("[TAG] ...")` in `UpdateRig` (head bob range, hand position, roll angle), run, grep the host log, then restore. Numbers beat 19px screenshots.
- Screenshots are 6016×3320. Crop around the player with PIL and resize with `NEAREST`. Frame 0 is often the camera before it settles, so poll `state` a dozen times first.
- Ropes to test on: `Playground/start` (warp `363 460` with `input up` grabs one).
- Check ship builds too: `dotnet build -p:MythShip=true -o <scratch>`. Editor-only APIs (`RigTakes.Set`) must stay behind `#if EDITOR`.

## Tunables (live via getters in `Pawn.Rig.cs`)

Gait/sprint rates, `RigRollMinRate`, `RigLandingScale`, head-look turn/rate, `RigSkidYawRelease`, `RigRopeGripHeight` (meters), `RigCrawlLift`, `RigDiveFullAngleSpeed` / `RigDiveAngleResponse`. Settings that are worth opening in the inspector: `OutlineMode` (pawns use `Silhouette`: one rim, plus `FrontLimbOutline` where a limb passes in front of the pelvis by `FrontLimbDepth`), `VelocityLean`, `SquishResponse`, `TurnDuration`, `RagdollCarry`, `VisualFPS`.

## Feel (Humanoid prefab)

- `InertialBlendTime` (0.1): a clip change shows the new clip at once, with the old pose as an offset eased out with a fast start (inertialization). Explicit blend durations in `Pawn.Rig` (dive 0.3, landing out of a dive 0.08) become the inertial duration. 0 falls back to the smoothstep cross-fade (puppets).
- `VisualFPS` 24 plus `KeyEvents`: poses step at 24 fps like keyed animation, but a clip change or a turn keys a pose on its own frame, so stepping never delays a reaction.
- `TurnDuration` 0.05 with no `TurnInertia`: close to a sprite's instant flip.
- `LandingHold` (0.06s at 8 m/s of impact, less for softer landings): the compressed contact pose holds, then recovers.
- `PixelSnap`: drawn joints round to whole pixels, so a small figure doesn't shimmer.
- Lean is weighted toward acceleration (`VelocityLeanFullSpeed`/`FullAcceleration` in the rig), so it reacts on the input frame.
- Measured at 60 fps, jump and turn: about 9px of visible pose change within 1–2 frames, where the old settings showed about 2px for the first 4 frames.

## Open ideas

Tilt the rig with the rope's angle on a swing. Distance-matched gait rates. Prone duck (avoids the duck↔crawl pop). `Leaning` / `Walking_Turn_180` are baked but unused. Footstep audio for Bud. Full clip preview on pawns (refactor `RigPuppet`'s preview into a shared helper).
