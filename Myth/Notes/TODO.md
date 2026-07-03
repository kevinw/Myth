# TODO

- death is weird, you can fall through jumpthrus into water; maybe simplify
- attenuate landing sound by y velocity
- store fall height in burrow; pop out with that height
- rolling down a 45degree slope needs to "stick" more
- make Bud's "fallback_walk" more interesting. maybe we walks slowly. investigates different points.
- make rim_light a uniform; investigate less harsh ways of lighting things
- a player frame for "holding down before a roll"
- wall jump too difficult (go back to "just jump"?)
- mobile web:  instead of tap to zoom out, respect pinch in and out to adjust zoom level
- save checkpoints
- bug: dying and respawning elsewhere goes back one room at a time. (but maybe its a feature? if I remove the hit stun at each room entry?)
- pick a non LLM-brained color palette for the web ui
- breakable pots with coins (or pixels? like mina?)
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
