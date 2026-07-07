# TODO

pre-friend build
- publish build: get stuck in wall after dying in Bud room.
- publish: default fullscreen
- menu
- a snow variant for "pollen"
- turn around needs particles
- a save state
  - durable chests
  - explored rooms
  - save spots/warps

- have local notarize-publish process upload to cloudflare storage under a date-git-hash folder
- after roll under a 1-tile tall tunnel, you stand stuck in the wall
- ./tools/run-browser.mjs doens't start the game at "start"
- wall slide sounds
- entering a room from the top or bottom edge probably shouldn't make a "fresh player start"
- a "wildlife" system which draws flies, hoppers, fish and more. they aren't more than 3x3 pixels, and render as a bit transparent. it's "ambient" wildlife.
- a main menu

- a section at the beggining where you learn
  - to jump
  - to roll (a closing door?)
  - to burrow up a wall

- build Detect; use it for ShieldKnight. make him (and other actors?) understand ledges.
- if the git diff contains only markdown, don't run the browser workflow?
- ScanDiff: Error reading file: /Users/kev/src/Myth/Foster
- git diff checker on bg thread
- wall jump in one tile wide well doesn't work
- use 16:10 screen fully on tall rooms. make letterboxing fully based on the room size
- draw "block rect" from shieldknight in C debug view (track extra "block rects this frame")
- Better revealdoor sound
- Death is weird, you can fall through jumpthrus into water; maybe simplify
- Attenuate landing sound by y velocity
- idea to try: Store fall height in burrow; pop out with that height
- Rolling down a 45degree slope needs to "stick" more
- make Bud's "fallback_walk" more interesting. maybe we walks slowly. investigates different points.
- make rim_light a uniform; investigate less harsh ways of lighting things
- a player frame for "holding down before a roll"
- wall jump too difficult (go back to "just jump"?)
- mobile web:  instead of tap to zoom out, respect pinch in and out to adjust zoom level
- save checkpoints
- bug: dying and respawning elsewhere goes back one room at a time. (but maybe its a feature? if I remove the hit stun at each room entry?)
- pick a non LLM-brained color palette for the web ui
- try a big low color image in the background of the boss room
- boss room red light looks bad
- copy rooms I made from frog world over
- how do famous paintings break up gradients
- oklab for background (and any) gradient https://www.shadertoy.com/view/ttcyRS
- srgb to linear, all shaders in linear, then final pass linear to srgb
- absolutely improve player respawn behaviour when editing
- vector art collection -> stampable tile patterns
- wall jump slide sound
- subtle light overlay for any room exit leading to another room
- add coins/crystals/doodads for hiding in secret areas
- an MP hiding in the dual grid, activated by something
- LayerOverride (save-game wide toggle for any entity value)
- groups of blocks get a reflection direction
- create decals for breakable walls
- move the Program.cs out of 'Desktop' since it's also for the browser
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
- limit two wall jumps? stamina meter?
- boss rush storylets: like Hades, each time you face off against one, you get new dialog. but winning doesn't mean they won't come back and that you are "punished" with less dialog; maybe they come back stronger!

## iOS

- Build a custom SDL3 iOS xcframework with unused camera and Bluetooth/HIDAPI support disabled, then replace the bundled SDL3.xcframework so App Store uploads no longer need privacy purpose strings for APIs Myth does not use.

## sky-flavoured pie

- every pixel gets soft pillowy normals in a full-res lighting pass
- Player folds out a piano and plays it every time they need to speak to someone.
- finding love in a datacenter
  - what's in a datacenter?
    - racks
    - cooling fans
    - ducts

## resources

- [PixelLab](https://www.pixellab.ai) - ai pixel art generator
- [bitmapflow](https://github.com/Bauxitedev/bitmapflow)
- [coolors](https://coolors.co) - color palette generator
- [lospec](https://lospec.com/palette-list) - color palette collection
- [chevyray's pixel fonts](https://chevyray.itch.io)
