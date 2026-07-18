# TODO

neuraltexturerippleactor could draw editor radius; it's editor hitbox doesn't show center
esa notes:
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
one way "wind" tiles <<<<
one tile crawl critter
computer that puts something on your map as a goal
an enemy that flies at you regardless of collision
flames that come out of a spot


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
- merge kenney font for controller buttons into SpriteFont  https://kenney.nl/assets/input-prompts and include button prompts in tutorial messages
- bud "once per session" hints for rooms like the movingplatform room where you have to burrow.
- telegraph more (shieldknight and drone)
- Drone has too much movement
- more big rooms
- connected hubs and spokes?
- study silksong map
- build Detect; use it for ShieldKnight. make him (and other actors?) understand ledges.

## mood: MINUTIA
- separate "editingRoomId" and "playRoomId" and put them in editor.json and save.json
- actors should be destroyed when they go off the bottom of the screen. (what happens? do their positions already get clamped? first: show their positions.)
- after roll under a 1-tile tall tunnel, you stand stuck in the wall
- Build a custom SDL3 iOS xcframework with unused camera and Bluetooth/HIDAPI support disabled, then replace the bundled SDL3.xcframework so App Store uploads no longer need privacy purpose strings for APIs Myth does not use.

## mood: SOUND AND MUSIC
- shieldknight needs sound effects, detect, player awareness squish
- turret needs metal hit sound when you strike it
- wall jump and wall slide sounds
- Attenuate landing sound by y velocity
- Better revealdoor sound


- someday: https://github.com/witnessmenow/ESP32-Cheap-Yellow-Display
- if the git diff contains only markdown, don't run the browser workflow?
- ScanDiff: Error reading file: /Users/kev/src/Myth/Foster
- wall jump in one tile wide well doesn't work
- use 16:10 screen fully on tall rooms. make letterboxing fully based on the room size
- Death is weird, you can fall through jumpthrus into water; maybe simplify
- idea to try: Store fall height in burrow; pop out with that height
- Rolling down a 45degree slope needs to "stick" more
- make Bud's "fallback_walk" more interesting. maybe we walks slowly. investigates different points.
- make rim_light a uniform; investigate less harsh ways of lighting things
- a player sprite frame for "holding down before a roll"
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
- bud gets hit, rolls away from danger
- double tap for 0-to-60 running
- up jump near bud gets a boost
- trying to put out all the torches in a room, but bugs are trying to light them
- a room where Bud gets stuck; you hear him from the other room, or talk on a radio
- boss rush storylets: like Hades, each time you face off against one, you get new dialog. but winning doesn't mean they won't come back and that you are "punished" with less dialog; maybe they come back stronger!


## resources

- [PixelLab](https://www.pixellab.ai) - ai pixel art generator
- [bitmapflow](https://github.com/Bauxitedev/bitmapflow)
- [coolors](https://coolors.co) - color palette generator
- [lospec](https://lospec.com/palette-list) - color palette collection
- [chevyray's pixel fonts](https://chevyray.itch.io)
