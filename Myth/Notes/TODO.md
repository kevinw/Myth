- a pig head variant with floppy ear costume
- a spell check pass on user visible strings
- localized strings
- ori-like glittering discoball wall; faking specular highlights with slight view angle noise
- animate any rigged model with text https://github.com/Friedrich-M/UniMate
- try Jev for a "similarity stamp" or "tile fill" in the editor
- idea: YOU place the save points. you have a limited amount. secret mechanic to tunnel into them to warp.
- do some automated research on https://steamdb.info/charts/?tagid=1628&sort=peak
- try picking an mid jump attack direction by doing an actor scan (could be gated by time...) to address a particular T&P pain point
scripting console: Roslyn CSharpScript (Microsoft.CodeAnalysis.CSharp.Scripting) with the live Game as host object -- evaluate C# against the running room to poke actors, spawn things, run one-off measurements. No EnC needed, so it's a fraction of the machinery of live code reload and probably scratches most of the same itch.
can we be smarter than dotnet watch? behaviour edits (not just tunable values) are where live reload earns its keep, and the round trip is ~1.1-1.8s. Measured facts before re-deriving them:
- watch is ALREADY an in-process Roslyn host: no MSBuild, no csc in the edit loop. It keeps a Solution in memory, patches the document text, emits an EnC delta, calls MetadataUpdater.ApplyUpdate. Re-implementing it buys only the IPC hop.
- consecutive edits warm up: 1758ms then 1110ms. RunAnalyzers=false is already set; all three source generators are already IIncrementalGenerator with clean pipelines.
- unmeasured: where the remaining second goes (project-wide re-bind for diagnostics? rude-edit analysis? delta emit?). Cheap experiment first: time an edit to a small leaf file vs 2600-line Player.cs. If small files win, splitting hot-iteration code into smaller partials is a free win with no new machinery.
decoration layer tiles; dual grid can feed it for underhangs for example
play all the metroidbrania games here https://note.com/opticalglass/n/n7de04c1e56c3
bud and player both do a lot of blinking if you duck. make them signal to each other in morse code.
Demo polish
- need a "wall push walk"
- bringing up the steam overlay pauses the game
- demo has links to socials/support from the main menu (discord, steam discussions, etc)
- demo doesn't soft-lock non querty players (e.g., azerty or qwertz)
- sfx on button hover + button click
- windows are tweened, not instant
- demo has an ambient sound layer 
- toggling vsync on the deck crashes the game
- hitting tab to go editor mode on a zoomed in room doesn't preserve that zoom---we get zoomed out
- make ropes that span room boundaries more reliable
- compress orthogonal burrow to launch or railgun 
- put an evolving world map online with a timeline scrubber https://leafletjs.com
- investigate gpu perf
  - https://developer.apple.com/documentation/xcode/debugging-with-interactive-command-line-tools
  - https://developer.apple.com/documentation/xcode/investigating-gpu-issues-with-ai-agents
- gameplay idea: jump to parry pops you into a burrow
- profile the remaining cold-install hitch in the big first fall room: neural pipelines warm at boot, but room-specific weights, buffers, and seed simulation still initialize on entry
- ACTOR_UPDATE_LIFECYCLE.md
- FRAME_RATE_LIMIT.md
- BROWSER_MUSIC_ENCODING.md: try Vorbis for music on the FMOD Mobile platform to halve the browser download, once a browser room plays music
COSTUMES (rig puppet adornment, 2026-09-14): a shared Costume asset like Surface (Sources/Costumes/<id>.json + its own editor window; actors get one CostumeId picker plus seed/palette/tint). Small: per-part surfaces/tints (boots, gloves, vest); split a bone at a fraction for cuffs, sleeves, boot tops; bone-space wrapping UVs for stripes/bandages; width profiles (flared cuffs, boot bulge, sole quad). Medium: generalize the ruff into rings at any joint (cuffs, belt, skirt); author wire layouts as straps, tassels, antennae, ankle chains (allow free-hanging ends); rigid plates/pauldrons and sprite stamps (rivets, buckles) as depth-sorted accessories; neural costume (per-part mask into the skin NCA's 4 model slots; hits erode armor to reveal skin). Bigger: verlet cloth cape/coattails pinned to the shoulders (Wind pushes it, drapes on death); hair/tendrils/ribbons from the same solver. Infra: accessory list (Draw takes one), generic record-list inspector, click joints/bones in the viewport, turntable preview, attribute-based inspector drawers; check every knob in the big_rig_puppet gallery.

- no pits, seamless loading
 USE LATEX AND BEAMER TO MAKE THEM LOL https://www.overleaf.com/learn/latex/Beamer
- gameplay NCA ideas
  - an otherwise emtpy room with a toxic barrel. you strike it, and it explodes, emitting swimmable yet poison gas.
- draw one new pixel art sprite a day; find an exercise list, an intuition pump, or a walkthrough
- the blob behaviour still needs to change a lot, and its look
- redraw hurt sprite, bubble
- a "fast motion" NCA impact frame for boss death
- lore rewards alongside chests (MAKE BUD react to computer text, too)
SFX
  - burrow time 
  - Bosonic Time + upgrade needs SFX
  - really listen and pay attention to lack of landing sound effects on grass etc
- NCA conference talks https://www.youtube.com/watch?v=mjHh2-HB58M&list=PLEt5M_lIDJdlVYeByo116YMdMPD-qcCak&index=4

TALK AND PLAY FEEDBACK
- show a dot on the map early
- consider NOT cancelling the attack direction
- the player character art reads as unfinished vs the backgrounds
- use the NCA for gameplay
- which pits are navigable and which are death is unclear 
- PROTOTYPE/MEASURE: 
  - slow burrow clock a bit until you move. or make moving cost "more" than "idling" and tune that.
  - dash button and then jump into a wall 
  - separate burrow button
  - NCA poison cloud
  - NCA unstable collider/platform (can you burrow along its fractal edge? pushing off scatters it?)
  - surface slam: slamming down sends a shockwave along the surface's contour (reuse burrow's contour follower), knocking off whatever stands or clings further along. fall height/roll speed sets strength; ride your own wave by burrowing behind it; stops at water
- if i can't "one wall jump" somehow it's a bit unclear; it almost feels possible
- fer's feedback, echoed: the shadow in the floater room looks "safe" but it is not. one idea that comes to mind: the floater itself could cast a very big warning light ahead of time. then the shadows WOULD be safe

LATER
- what would it look like to be able to create your own burrow line? 
- Game should own "death", not Player. Today Player.HurtState() mass-destroys every actor except itself/light on death; Transition() does the same on room change. It's fragile. Make death an atomic Game-level reset that notices the player died, instead of the player iterating and destroying actors. (The TimeScale strand-a-frozen-modal symptom of this is already handled: see Game.TimeScale.cs -- time bending is now a registry of composable factors, each optionally owned by an actor and auto-dropped when that actor is destroyed, so a mass-destroy can no longer leave the sim frozen. Same pattern is worth reaching for anytime a future death/reset path needs to be robust against half-torn-down state.)
- the moving platform puzzle is too slow
- health pots as a minor diversion
- wires on static rig puppets
- use Dropper; draw a sprite; more "easy" enemy variety wins
- show mycelon count and duck count at the end
- handstand animation
- a burrow door which activates a swarm of text--partially chain of thought partially journal junk
- bezier curves
- train a model which reproduces doodles
- measure the map connectivity by some different graph theoretic measures--visualize rooms to get a sense of their relation to others
map shouldn't show the squares of room boundaries--instead an expanded SDF of their solid edges
what instead of computes?
- a rig puppet in every lore spot instead of a computer
- animate tutorial text a bit on room load
- foster logo at startup
- POSSIBLY use alien bug for one of our existing behaviours?
- wall jump teach room early where Bud tells you
- RigPuppet: size and size of limbs as animated effect. see if there's a way to attach lfos or target values to any field.
- for builds for fer
  - screenshot button
  - tilde console
  - add an itch page
- burrow pop in a corner: PopOutOfBurrow launches along -exitDir without checking the destination
  is clear, so a vertical wall meeting the ground fires you into the solid. Pawn's per-frame
  TryUnstick now rescues it, but the pop should pick an unblocked direction itself.
- dev-watch: Foster edits never hot reload -- dev-watch runs dotnet watch with UseFosterDll=true,
  which references a prebuilt Foster.Framework.dll instead of the project, so Foster sources aren't
  in the watch graph and nothing rebuilds the dll. Wants a --foster-project mode for sessions
  spent in Foster/.
- a textual undo log
- have rope repaint itself on editor drag
- the introduction of a new enemy is itself a payoff; new area, new enemies
- downloaded horse model on 
- door is rendering normals behind the hidden wall cover
- nine-slice or procedurally draw doors. Door.RenderSized stretches the sprite by Size/Frame.Size,
  and door.ase is 16x16 while DefaultSize is 12x16 -- so every door is nearest-sampled at 0.75x
  and loses one pixel column in four (burrow_switch_test's Size=[12,25] loses rows too). Which
  columns get dropped even depends on the atlas dimensions, so a repack visibly shifts door art.
  Fixed caps + tiled middle, or authoring the sprite at its real size, would end the resampling.
  - let's brainstorm some enemy-specific pops, or effects can we can tweak and resuse
- the generic Pop is starting to feel a bit overused/generic
- find a way to limit Bud's presence. his abilities don't naturally follow you into many areas.
- players arms when he jumps are down and look weird
- import the bg brick as our custom tile kit and make it more varied
- dig into VirtualAction.buffer and see if we could use it anywhere to make the game feel better
- when loading barks from a baked asset that is older than Game.BudStory.cs 
- lightning trap landing spot could a) paint black in the foreground mask and/or b) have more impact sprites
- make the eyelaserpuppet sit down and talk to you after you beat him. use a deep voice.
- an enemy or area which speeds up or slows down time
- put some inbetween frames in the player sprite
- make our Aseprite loader able to load indexed color files
- OnWasHit is very repetitive across our actors. we should refactor it into a shared method with a knockback param
- try some sound effects from the BOOM library https://artlist.io/sfx/artist/boom-library/845
- improve editor spawn pick player location: not in a reveal door (burrow_secret)
- dotnet 11 (slower compile, but maybe something is improved?)
- chain roll attack into a hop 
- try a variant of HitStun which is more of a timescale ramp

# pseudoregalia
- switch which moves platforms is fun
- more 3D in and out of spaces

# type-safe actor spec access (source-gen spec views)

Editor + pre-instantiation code reads actor data from JsonObject specs via stringly-typed
`ActorSpecs.ReadResolved*(spec, "Key")` (~65 sites / 14 files), losing compile-time + rename safety.
Extend `Myth.SourceGen` to emit a typed read-only view per `[GameActor]` type (e.g. a
`readonly struct CameraBlockerSpec(JsonObject)` exposing `Point2 Size => ...`), so spec reads are
checked by the compiler with no per-read reflection, and scale to every registered actor.
Interim mitigation already applied on the blocker path: prefer `ActorSpecs.GetTypeName`, `nameof`
the keys, and hoist cross-cutting spec reads into a typed helper next to the type
(`CameraBlocker.WorldRectFromSpec`). A broader `nameof` sweep of the remaining ~60 sites can ride
along with this.


# fer playtest

- what is the guy responding to?
- feels weird to leave the guy behind.
- hidden/breakable walls confusion, we need a more consistent visual design for secrets
- water droplets would be nice
- spitter room with water at the bottom is ugly
- left stick on the map is not moving
- move / zoom the map during save
- could use arrows next to your location on the map indicating where you can go
- making sure that either all gaps which lead to an adjacent room are obvious, or that there are no pits? not sure
- carry is confusing
- should we be able to burrow on a jumpthru?
- he thought the duck would hurt you
- the bg NCT should disintegrate when you kill the duck
- start menu maybe needs "return to checkpoint"
- lighting trap sfx stuck when the map opens; what is a general solution for this
- make untaken map exits more obvious than the current pulsating 1px line
- doors in the floater room are unclear
- floater room shadows look like safe spots but they are not
- save point after the floater "boss"
- thought tower was a new room because he came down and the hidden wall was revealed immediately
- remove LinkedDoors that warp you across the map; respect the player's understanding of spatial relations
- ambient server sfx

# TODO

- an upwards tube with wind that is initially one way
- draw arms up for carrying
- idea: invisible tiles which you touch for NCA
- duck on tower needs SFX setup
- hitting a wall needs particles
- mark gettable items with a color or pattern in the editor map; add an editor map feature for showing colors or patterns over rooms showing what is accessible with a given item set
- burrow sound effect
- axiom verge thoughts
  - upgrades to projectile size, distance
- try render-target-space screenshake on gameplay beats: hits, landings, chest opens, boss impacts, burrow pops, door/portal transfers.
- quake lessons:
  - frenetic.
  - secrets help you level up immediately.
  - trap rooms. moving platforms. monster closets.
- maybe burrowing underneath enemies freezes or stuns them
- make a glass tube. the lights go out. all you illuminate is the tube.
- add a shortcut for opening in the xcode graphics debugger; have a look and see if we're doing anything dumb.
- go back to the original two inspo images (multicolored dither cliff and cathedral) and try to make a room more like them; color variety!
- use the chair prop as an existence proof and as a pump for a drawing exercise: new props!
  - church pews
  - a pulpit which tumbles when you step on it
- maybe you start in a treehouse, makeshift, there is some incredible danger flowing like a river beneath you in intervals
- how many petabytes per cubit? (writing)
- a note in every room describing the new thing it introduces
- One of the characters speaks with curated chain of thought messages from an open model
- burrow sound needs to be the best sound in the entire game
- memory pulse instead of a map; only in the radius around you?
- Spend an entire day making bud more interesting
  - More barks.
  - Fix idle animation.
- save points are more fun in hubs where you have somewhere else to go than where you died
- Make the burrow moving platform have a mode where when you're on it, it moves, so it's more fun if you miss it
- take all Blobs/Foster sample actors out; comment out the code
- Not enough reasons to burrow outside of combat
  - Burrow switch
  - Some further interaction with dash
- duck needs a quack
- rope unfurl sfx
- rope unfurl connected to pile, pile needs to go away
- respawning at a teleporter after death needs a small animation
- maybe dying is not what this game is about
- the map shows a blinking line for places that are actually unimportant, but you just haven't pushed the camera up; like the tall room you have to burrow up the left of, with a drone. the line could probably just be omitted in places we know there's nothing
- burrow could leave a temporary foreground texture paint 
- introduce a burrow line faster
- maybe you always respawn where bud was?
  - maybe bud clones himself and makes you
- props like server could have a "speed randomness" as well as a "random offset"
- first rope in big rope room needs to be longer, and needs to show that jumping left is not the way
- grabbing a rope and sliding down it needs sfx
- big stutter on NCT room; need shader preload?
- flames should flicker; blue lights should be bulbs
- first tall room has a camera stutter when you go down
- make tower door a sublayer
- make save point pulse better, sfx
- quick menu for adding actors during play
- create a room by going left or right during play
- a new sprite added to the folder gets pasted in during play as a prop
- a bloom knob
- reachable Assets get packed
- better Bud idle behavior.
- if you go up, there's only exploration, if you go down, there's also combat
- idea: one type of smoky NCT could write over other actors, but have the alpha depend on a simulation value.
- lightning traps need sound fx
- region based background settings?

## release attribution
- replace `609203__cupido-1__roll-apple-on-carpet.ogg`; it is CC BY-NC 4.0 and cannot be used in a commercial release
- obtain permission for or replace/retrain the imported Cells2Pixels NCA weights; the upstream repositories do not provide a license
- review the provenance and commercial-use rights of the NCA training textures, especially images derived from the Describable Textures Dataset
- add an audio source ledger containing the filename, creator, source URL, and license for every downloaded sound
- confirm the provenance of the generically named music, footstep, fly, sword, and voice files

- update fmod dylibs

neural PARTICLE automata https://selforg-npa.github.io/#growing and https://noisenca.github.io

steam notes
- once we have a real appid: copy steam_appid.txt next to the steam_api binary in packaged builds. the build writes it (MythWriteSteamAppId, from app.json steamAppId) but publish-{macos,linux,windows}.mjs only copy the exe and the libs, so no package gets it today and Steam init fails there. for the .app it has to sit beside Contents/MacOS, and codesign rejects non-code under MacOS -- so check whether the cwd of a Steam-launched .app makes it unnecessary (Steam hands the id to a process it launches itself; the file is only for running a build outside Steam)
- absolutely change the title to something weird for now
- consider deck's aspect ratio as "the one" and resize all rooms? research this, maybe i have it wrong

- i get a feeling watching metroidvania videos on twitter: bleh. boss fight. grapple hook. it's all so samey. we should do ___ instead.
- water ambience sfx for water tile rooms
- make a youtube account for the game. post the head bug as a youtube short.
- cimgui.dll is 1.4MB; can it be statically linked?
- integrate Dear ImGui Test Engine so automated editor scenarios can identify, drive, and assert docked, scrolling, and popup UI directly
- consider using a C++ build of Dear ImGui's SDL_GPU backend so we don't have to compile our ImGui backend each time
- a debug hotkey for switching back to the previous version of a shader after a change for A/B testing them live and visually
- windows Myth.exe doesn't have an icon
- see if there's a "free" or lightweight way to coalesce a bunch of ValueStringBuilder.Appends at compile time

- fmod 2.4 will have better browser perf

Environmental Station Alpha playthorugh notes:
  - alternate vertical and horizontal challenge
  - think about save points and optional challenging runs
  - tubes as visible layers on top of a room
  - or invisible layers on top of a room
  - some genuinely difficult optional traversal challenges with the grapple
  - pipes which can overlap the room
  - switch blocks recontextualize an area
  - light irridescence is nice
  - lots of small songs
  - every song intro is an area intro is something new (a new movement/enemy/both)

second song needs some automation curves
one tile crawl critter
computer that puts something on your map as a goal
an enemy that flies at you regardless of collision

NCA
  - perturb on tile edges, so it looks more integrated
  - jump around on the surface of them?
  - spawn enemies there?
  - player alpha based on sim value so it looks like you're behind smoke

download a lot of LUTs and try them
adjust rope springiness as a mechanic or challenge

## to triage
- rebuild fmod mobile; add warning if fmod project files are newer than the built asset banks? or just build them on desktop?

## mood: CONTENT
- bud "once per session" hints for rooms like the movingplatform room where you have to burrow.
- telegraph more (shieldknight and drone)
- Drone has too much movement
- more big rooms
- connected hubs and spokes?
- study silksong map
- build Detect; use it for ShieldKnight. make him (and other actors?) understand ledges.

## mood: MINUTIA
- separate "editingRoomId" and "playRoomId" and put them in editor.json and save.json

## mood: SOUND AND MUSIC
- shieldknight needs sound effects, detect, player awareness squish
- turret needs metal hit sound when you strike it
- wall jump and wall slide sounds
- Attenuate landing sound by y velocity
- Better revealdoor sound
- map open close sounds

- motion vectors and blur
  but make cartoony with colors and frames
- someday: https://github.com/witnessmenow/ESP32-Cheap-Yellow-Display
- if the git diff contains only markdown, don't run the browser workflow?
- ScanDiff: Error reading file: /Users/kev/src/Myth/Foster
- use 16:10 screen fully on tall rooms. make letterboxing fully based on the room size
- Death is weird, you can fall through jumpthrus into water; maybe simplify
- idea to try: Store fall height in burrow; pop out with that height
- Rolling down a 45degree slope needs to "stick" more
- make Bud's "fallback_walk" more interesting. maybe we walks slowly. investigates different points.
- make rim_light a uniform; investigate less harsh ways of lighting things
- wall jump too difficult (go back to "just jump"?)
- mobile web:  instead of tap to zoom out, respect pinch in and out to adjust zoom level
- bug: dying and respawning elsewhere goes back one room at a time. (but maybe its a feature? if I remove the hit stun at each room entry?)
- oklab for background (and any) gradient https://www.shadertoy.com/view/ttcyRS
- srgb to linear, all shaders in linear, then final pass linear to srgb
- absolutely improve player respawn behaviour when editing
- vector art collection -> stampable tile patterns
- subtle light overlay for any room exit leading to another room
- add coins/crystals/doodads for hiding in secret areas
- an MP hiding in the dual grid, activated by something
- LayerOverride (save-game wide toggle for any entity value)
- groups of blocks get a reflection direction
- create decals for breakable walls
- think about trying to turn a real 3D complex space (a castle blueprint) into a series of layers connected by doors
- a room enclosed in grass shouldn't reverb as much.
- resume codex work on mapping touch/pinch events to Foster events and route them through ImGui
- Consider some [pixel art ideas](https://chatgpt.com/c/6a030b73-3564-83eb-b4ca-9ab1e9e787bb)

## ideas

- watch some videos about hiding secrets
- what would be fun to earn in a secret? levelling up your health. your burrow time (yes).
- the best secret is a whole new area and set of challenges.
- burrow in water: become a line in any direction
- roll on surface of water, become a skipping stone
- roll into vertical wall: fast wall climb
- burrow under bud: carry him
- carry a powered-up bud who gives you a balloon lift
- bud gets hit, rolls away from danger
- double tap for 0-to-60 running
- up jump near bud gets a boost
- trying to put out all the torches in a room, but bugs are trying to light them
- a room where Bud gets stuck; you hear him from the other room, or talk on a radio
- boss rush storylets: like Hades, each time you face off against one, you get new dialog. but winning doesn't mean they won't come back and that you are "punished" with less dialog; maybe they come back stronger!
- vision pro build: paint, place tiles, tweak multidimensional values, rig puppets...
- play pong with projectiles in a clear arkanoid room

### platformer elements
- fan block: solid tile projecting a toggleable short Wind column (updraft elevator; blow ropes sideways)
- swinging pendulum platform: platform hung from a rope pivot; landing momentum pumps it, wind pushes it
- crusher: slow rise, telegraphed pause, fast slam (build on MovingPlatform's MoveSolid push/squish)
- timed flip blocks: tiles phasing solid/ghost on a metronome or per-jump (ESA switch blocks in miniature)
- water spout / geyser: periodic vertical jet that carries you up while on; gives vertical rooms a rhythm
- falling icicle: trembles when you pass under, drops, sticks in as a temporary platform/spike
- one-way valve door: directional Jumpthru in any orientation; huge for loop-shaped room design
- pressure plate + counterweight gate: stays down only while weighted (crate, Bud, or a draped cut rope)
- sticky honey strip: inverse ConveyorBelt; halves speed and jump height, escaped by burrowing under it


## resources

- [PixelLab](https://www.pixellab.ai) - ai pixel art generator
- [bitmapflow](https://github.com/Bauxitedev/bitmapflow)
- [coolors](https://coolors.co) - color palette generator
- [lospec](https://lospec.com/palette-list) - color palette collection
- [chevyray's pixel fonts](https://chevyray.itch.io)
