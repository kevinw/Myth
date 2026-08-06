# TODO

- a bloom knob
- reachable Assets get packed
- better Bud idle behavior.
- more google doc writing https://docs.google.com/spreadsheets/d/1KKmU3uwT2x6hbNzBEyBdjEReGU-bd9zTvpJAd4vOTTg/edit?gid=0#gid=0
- it would be very fun to introduce a slow version of bud that kills you and hunts you across rooms.
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
- absolutely change the title to something weird for now
- consider deck's aspect ratio as "the one" and resize all rooms? research this, maybe i have it wrong

- i get a feeling watching metroidvania videos on twitter: bleh. boss fight. grapple hook. it's all so samey. we should do ___ instead.
- water ambience for water tile rooms
- make a youtube account for the game. post the head bug as a youtube short.
- cimgui.dll is 1.4MB; can it be statically linked?
- integrate Dear ImGui Test Engine so automated editor scenarios can identify, drive, and assert docked, scrolling, and popup UI directly
- consider using a C++ build of Dear ImGui's SDL_GPU backend so we don't have to compile our ImGui backend each time
- Change Content.zip extension and add some simple byte level change
- windows Myth.exe doesn't have an icon

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
- Build a custom SDL3 iOS xcframework with unused camera and Bluetooth/HIDAPI support disabled, then replace the bundled SDL3.xcframework so App Store uploads no longer need privacy purpose strings for APIs Myth does not use.

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
