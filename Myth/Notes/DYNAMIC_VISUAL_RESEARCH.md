# Dynamic Visual Research

The useful question is not "what effect can we add?" but "which currently-static
visual facts could become signals?" Mycelon already has a lot of writable visual
surfaces: room color fields, tile/dither field, dither mask, normal buffer,
emissive buffers, light masks, spore fog, reflection masks, particle collision
masks, neural textures, and ripple states. The cheapest wild experiments are
probably actors writing into those existing fields.

## Good First Experiments

These look high-leverage because they reuse existing passes and have a clear
visual identity payoff.

1. **Animated tile-field stamps**

   `DrawTileField` already composites cached room tile data, then dynamic actor
   stamps for hidden covers, neural textures, and moving platforms. The field is
   currently `R8G8`: red is a stable hash variant, green packs material family
   and dither anchor.

   Let actors write animated perturbations into this same field before lighting:

   - A breathing fungus can locally rotate material family over time.
   - A projectile can leave a dither wake, like the light quantizer remembers its
     path for half a second.
   - A boss room can have slow "weather fronts" that move through the dither
     families while tile art stays still.
   - Player states can disturb dither grammar: burrow makes earth dither crawl,
     damage makes pattern families misclassify near the wound, save warp makes
     anchors detach from geometry.

   The important bit: do not animate the final pixels directly. Animate the
   grammar that chooses how light breaks into pixels.

2. **Second tile-field target for living dither metadata**

   The current tile field is tight. Instead of overloading `R8G8` further, add a
   sibling low-res target with ephemeral data:

   - `R`: dither phase/offset
   - `G`: pattern-family bias
   - `B`: glitch/instability
   - `A`: wetness/aliveness

   Then `LightingComposite.hlsl` can keep stable material identity in the cached
   field while sampling the dynamic field to perturb threshold selection, scale,
   quantization, anchor offsets, or normal-dither boost.

   This gives actors an expressive visual bus: "I do not draw myself differently;
   I make the room's lighting language different around me."

3. **Dynamic dither mask as attention, not just exemption**

   `DrawDitherMask` currently writes mostly "how much lighting dithering applies
   to this actor silhouette." Flip the idea: let actors write attraction,
   immunity, or infection into the mask around themselves.

   Examples:

   - Enemies repel dither so they read crisply, but their shadows dither harder.
   - Bud attracts softer dither so the world visually nests around them.
   - Hidden regions become visible not by fading alpha, but by relaxing dither
     density until forms can be read.
   - Rewind paints a temporary "undo grain" into the dither mask along the
     player's past positions.

4. **Surface layers sampled through world fields**

   `SurfaceBlend.hlsl` already supports per-layer tint, black endpoint, scroll,
   contrast, bias, quantize, transform, and blend. Those values are authored in
   JSON and driven only by time.

   Interesting dynamic inputs:

   - Sample room color fields to alter layer bias/quantization locally.
   - Sample light intensity to change layer blend: illuminated fungus screens in
     pale fibers; darkness multiplies in bruised fibers.
   - Sample player distance to change tiling scale or UV offset so surfaces
     "lean away" without moving collision.
   - Use a small flow field to bend layer UVs, not as water distortion but as a
     material nervous system.
   - Let surface normals strengthen in active areas and flatten elsewhere,
     changing how light catches without changing color art.

5. **Gameplay constants as visible local physics**

   A lot of actor constants are invisible scalar rules: magnet radius, surface
   attachment distance, enemy alert range, jump windows, water roll catch height,
   Bud follow spacing. Some of these could leak into visuals as fields.

   Examples:

   - Pickup magnet radius is not an invisible circle; it is a faint dither
     pressure field that bends nearby particles and lighting grain.
   - Enemy wake range slowly stains the tile field before the enemy wakes.
   - Bouncer stiffness emits normal-buffer ripples into nearby terrain when
     compressed.
   - Burrow line attach distance draws a thin searching halo into the dither
     field, not the color buffer.
   - Surface follower orientation modulates local normal tint, making crawlers
     magnetize the surface they are attached to.

## Static Things Worth Making Dynamic

### Tile/Dither Grammar

Current anchors:

- `TileFieldColor(char tile, Point2 worldTile, int anchor)` encodes stable hash
  and material family.
- `LightingComposite.hlsl` chooses dither pattern from family + variant.
- Moving platforms write anchor offsets so their dither stays locked to original
  position.
- `ditherGlitch` is global-ish, player-centered, and decays over time.

Research directions:

- **Dither organisms**: actors that have no sprite, only a tile-field brush.
  They crawl under the lighting pipeline as patches of variant/family mutation.
- **Temporal families**: a tile keeps its material family, but a dynamic field
  biases the pattern family selection over seconds.
- **Anchor weather**: instead of 32 actor anchors, have a room-scale vector field
  that warps `pattern_pixel` before thresholding.
- **Dither scars**: collisions and damage write a decaying history target that
  makes lighting quantization coarser where violence happened.
- **Mood-specific threshold tables**: `threshold_for_pattern` is hardcoded
  functions. Feed a small 1D/2D threshold texture per area, per room, or per
  living entity.
- **Material dialects**: the same tile family has different dither grammar based
  on region, music area, fungal state, or room color-field value.

### White, Black, Flat Normals, and "Default" Colors

Many render calls use `Color.White` as "unmodified." That is a missed hook if
white becomes an authored response texture or a sampled field.

Research directions:

- **White as paper stock**: final sprite color multiplies by a subtle material
  texture sampled in world space, so even flat sprite whites inherit room fiber.
- **Flat normal is not flat**: `0x8080ff` could become a room material normal
  field. Flat things pick up a shallow local grain instead of being perfectly
  inert.
- **Black as absorption**: black outlines could sample local ambient/reverb/fog
  state and become colored darkness rather than literal black.
- **Palette pressure**: `Color.White` in actors becomes "actor local color =
  sprite color * room palette field * state impulse."
- **Emissive white lies**: emissive sprites sample a local emission LUT, so white
  glow changes hue according to nearby material or story state.

This does not require replacing every `Color.White`. Pick one pass or one actor
family and test whether "white is a texture" creates a richer base.

### Room Settings as Fields

Room settings are mostly scalars:

- backdrop colors and backdrop surface
- ambient color/intensity
- light noise
- player light enabled
- spore fog strength
- sun color/strength/direction
- dither enable/scale/strength/slope boost/quantization

Research directions:

- **Gradient room settings**: let room color fields also modulate ambient,
  dither strength, light noise, spore density, or sun contribution per pixel.
- **Room settings with inertia**: transitions already lerp between rooms. Let
  a room's lighting state arrive as a slow front instead of a uniform fade.
- **Weather cells**: rectangular or painted areas that locally override room
  settings: one corner has high dither scale, another has cold ambient, another
  has no player light.
- **Gameplay-driven sun**: sun direction/strength changes with world mechanisms,
  not time-of-day realism. Pulling a switch rotates shadow grammar.
- **Spore fog as carrier**: fog currently has noise, drift, contrast, tint,
  point/sun response. Let it carry other values: infection, memory, speech,
  recent movement.

### Light Personality

`Light` already has radius, color, intensity, height, flicker, cookies, cookie
speed/scale/strength/feather, shadow source radius, softness, noise, and noise
speed.

Research directions:

- **Lights write fields**: lights do not only illuminate; they stamp dither
  instability, normal gain, or surface-layer bias.
- **Cookie output as input**: reuse light cookie value to decide particle color,
  tile-field mutation, or spore density, so a mushroom-shaped light also makes
  mushroom-shaped material behavior.
- **Flicker not sinusoidal**: drive flicker from room field samples, nearby actor
  state, or audio/reverb. Fire should not just blink; it should negotiate with
  the room.
- **Shadow source as emotion**: soft shadows for calm, sharp shadows for danger,
  noisy shadows for infected areas.
- **Light height as material selector**: low lights reveal surface grain, high
  lights reveal silhouette/normal detail.

### Normal Buffer

Normals currently come from sprites, surfaces, generated surface normals, and
procedural actor normals. The composite uses normal alpha as strength and can
boost/quantize dither based on normal slope.

Research directions:

- **Normal-only actors**: invisible actors that write normals but no color,
  bending light over empty air or across a wall.
- **Stateful normals**: the terrain normal buffer gets a transient dent from
  landing, explosion, bouncer compression, lightning, or burrow exit.
- **Normal moss**: NCA/neural textures generate normals that can be stamped over
  normal terrain without changing base color.
- **Slope speech**: surface-following enemies and Bud write local normal
  anisotropy into the surface under their feet.
- **Normal dither weather**: `NormalDitherSettings` becomes locally modulated,
  so steep normal features can suddenly quantize, sparkle, or go smooth.

### Emissive and Occlusion

There are separate background/foreground emissive buffers. `light_scene` lerps
scene color to emissive by emissive alpha after lighting/dither.

Research directions:

- **Emissive as override grammar**: emissive alpha does not only replace color;
  it can reduce dither, alter threshold pattern, or mark pixels that should
  contaminate neighboring tile-field state.
- **Emissive memory**: a ping-pong history of emissive events creates fading
  biological afterimages.
- **Reverse emissive**: bright actors make nearby dark materials more absorptive,
  not brighter.
- **Hidden emissive**: hidden cover reveal could happen in emissive first, then
  color, so secrets announce as non-light.

### Reflection and Distortion

Reflection uses reflection rects, a mask, and `ReflectionDistort.hlsl` with
time/amplitude. Reflection opacity and distorted/flat are tile-derived.

Research directions:

- **Distortion field ownership**: water, wind, lightning, and neural actors all
  write to a shared reflection distortion field. Reflection stops being a water
  trick and becomes "the world has surfaces that remember motion."
- **Material-specific reflection grammar**: metal reflects with dither-locked
  jitter, water with wave displacement, fungal membrane with delayed smear.
- **Reflections lie by state**: wounded player reflection lags; Bud reflection
  anticipates; enemies reflect as their wake range instead of their body.
- **No-reflection as signal**: some actors cast shadows and light but cannot be
  reflected, or reflect only into dither/normal fields.

### Particles as Field Writers

Particles already have GPU update/render paths, collision masks, light response,
trails, color LUTs, and effects authored in JSON.

Research directions:

- **Particles write tile-field residue**: sparks, blood, pollen, splash, and
  plasma leave different dither dialects.
- **Particles write color fields**: pollen slowly tints background color fields,
  rain erases them, blood darkens them.
- **Particle collision mask as material**: the solid mask is currently for
  collision. Sample it in visuals as "where particles believe solidity is."
- **Light-responsive particles feed back**: lit particles brighten spore fog or
  emissive history, so light reveals motes and motes then alter light.
- **Trail as brush**: particle trails are not just rendered; they are brush
  strokes into dither, normal, or fog fields.

### Neural/NCA Textures

`NeuralTextureActor` and `NCALayer` already run stateful GPU simulations with
perturb stamps, masks, ripples, tint, brightness/contrast/saturation, generated
normals, and material tile stamping.

Research directions:

- **NCA as a control field**: decode hidden channels not just color/normal, but
  dither family bias, fog density, or surface-layer offset.
- **World field feeding NCA**: feed light masks, dither instability, player
  proximity, or music/reverb into update params or perturb stamps.
- **Neural texture as invisible ecology**: an NCA actor can be invisible but
  still write tile-field, normal, or fog data.
- **Ripple bridge**: ripple state can perturb lighting, not just NCA present
  texture. Shockwaves make the dither grammar ripple outward.
- **Model blending as place memory**: NCT slot masks already blend models.
  Gameplay can paint slots over time, so the room grows a different visual
  model where the player acted.

### Actor Constants as Visual Emitters

Examples found across actors:

- Player light color/intensity changes during burrow.
- Player aura, dash FX, hat spring, water splash, attack, hurt, and rewind all
  carry useful state.
- Bud has follow/burrow/mirror behavior and a distinct tint.
- Spider, MagneticCrawler, Drone, Floater, LightningTrap, WindReliquary, Bouncer,
  SavePoint, pickups, wildlife, burrow lines, hidden covers, and moving platforms
  expose many authored colors, ranges, intensities, and timing constants.

Research directions:

- **Every radius leaks**: if code has a radius, render a field version of it
  somewhere. Not always visibly, but available to shaders.
- **Every cooldown decays visually**: cooldowns can drive dither relaxation,
  normal flattening, or emissive history.
- **Every hit writes a material event**: hit, blocked attack, parry, bounce,
  lightning reflection, water crossing, landing, pickup magnetization.
- **Every AI awareness state stains space**: alert, hunt, dormant, telegraph,
  recover, follow, flee.
- **Every companion relation has a visual field**: Bud's spacing, mimic delay,
  burrow ride radius, and player/Bud crosstalk can show up in subtle field
  pressure rather than explicit UI.

## Wilder Directions

### Visual Debt Ledger

Create a low-res ping-pong "debt" texture. Any intense event writes debt:
damage, dash, lightning, heavy landing, save warp, enemy wake, hidden reveal.
Each frame debt decays and advects. Shaders sample it to decide dither
instability, fog density, normal strength, and surface contrast.

This makes the room carry visual consequences without changing level geometry.

### Room Mood as a Living Texture

Instead of room ambient/dither/fog being scalar settings, keep a room-sized mood
texture. Editor painting writes base mood. Actors write temporary mood. Room
transition lerps mood textures. Lighting composite, surfaces, particles, and
NCA sample it.

Possible channels:

- `R`: tenderness/calm, lowers dither and raises ambient softness
- `G`: fungal activity, raises spore fog and NCA fire rate
- `B`: electrical charge, raises sharp normals and shadow noise
- `A`: memory/debt, increases history and afterimage effects

### Dither as Language

Treat dither families as phonemes. Actors do not have "effects"; they speak
pattern changes into the lighting pipeline. A room's visual style is the grammar
of how materials choose dither under stress.

Examples:

- Bud speaks rounded/clustered dither.
- Lightning speaks saw/stripe dither.
- Burrow speaks inverted diagonal dither.
- Save points speak stable Bayer.
- Stalkers speak hash/edge dither that refuses to anchor.

### The World Samples the Player's Body State

Expose a tiny struct/texture for player expressive state:

- grounded/airborne
- burrowing/rolling/dashing/hurt
- breath/water state
- attack phase
- rewind amount
- velocity direction
- recent path samples

Shaders can use this without coupling every effect to `Player`. Examples:

- The dither field is calmer under a walking player and turbulent under a dash.
- Backdrop surface scroll shifts against player velocity.
- Fog opens around breath and closes around hurt.
- Surface normals harden during attack frames.

### Field-Only Actors

Add a category of actor whose `Render` is empty but which contributes to one or
more visual fields. This is not a new rendering abstraction yet; it can start as
targeted hooks:

- `RenderTileFieldPerturb`
- `RenderMoodField`
- `RenderNormalField`
- `RenderFogField`

Field-only actors can be authorable room objects: dither wells, grain wind,
shadow roots, normal dents, fog mouths, palette bruises.

## Implementation Bias

Start with one extra target or one extra actor stamp path, not a whole visual
field framework. Good prototype order:

1. Add dynamic dither perturbation as a separate target sampled by
   `LightingComposite.hlsl`.
2. Give one actor a brush into that target.
3. Make the debug view show it.
4. Only then decide whether more actors need a shared interface.

Avoid CPU readback. Keep field writes on existing render/compute paths. Prefer
room/world-space fields over screen-space post effects so visuals feel embedded
in the world instead of layered over it.

## Pie In The Sky

These are less constrained by current systems. They are not "next steps"; they
are shapes the visual language could grow toward if the field-based approach
keeps working.

- **A world with visual metabolism**: every room has slow internal cycles:
  digestion, sleep, stress, bloom, scarring. The player does not trigger isolated
  effects; they alter the room's metabolism. A fight leaves the room anxious for
  minutes. A quiet return lets it soften.

- **Dither weather fronts**: large-scale moving systems pass through the world,
  changing how light quantizes, how particles behave, how silhouettes separate,
  and how surfaces breathe. Not rain/fog as content, but shifting rules of
  visibility.

- **Style as ecology**: different organisms occupy different render layers.
  Some live in color, some in normal, some in dither, some in emissive history,
  some only in the gap between foreground and reflection. Killing, helping, or
  carrying one changes which visual species dominate a room.

- **Memory geology**: the game records important actions into strata. Recent
  actions are fog/emissive. Older actions become dither scars. Very old actions
  become surface tint or tile-field bias. The room literally sediments the
  playthrough.

- **Shader instruments**: rooms contain invisible "instruments" that perform
  the renderer. One instrument plays thresholds, one plays normals, one plays
  fog drift, one plays palette. Gameplay events change their tempo, key, or
  syncopation.

- **A living palette immune system**: colors are not selected only by art assets.
  The world has antibodies that push back against overused hues, infected zones
  that pull colors toward a disease palette, and safe zones that restore local
  color diversity.

- **Non-Euclidean material response**: visual influence does not follow screen
  distance. A light might perturb every tile with the same material family
  anywhere in the room. A sound might affect all surfaces connected by burrow
  lines. A secret might be visible only through reflection or only in normal
  response.

- **Rendering as dialogue**: Bud, enemies, rooms, and the player each "speak" in
  a visual channel. The screen is the negotiated result. Bud calms dither, the
  player roughens it, lightning cuts through it, hidden rooms withhold it.

- **Local physics of seeing**: sight itself has rules. Darkness might pool like
  liquid. Brightness might have inertia. Edges might be attracted to music.
  Dither might crystallize under stillness and melt under motion.

- **State-dependent art assets**: sprites and tiles are stable only in neutral
  conditions. Under stress, the renderer samples alternate atlas regions, neural
  decodes, or generated patches. The asset is a seed, not the final appearance.

- **Invisible architecture**: rooms have visual organs not represented by
  collision: lungs that breathe fog, nerves that carry dither impulses, glands
  that secrete palette, bones that define shadow behavior.

- **Creature-shaped postprocessing**: instead of global post effects, invisible
  organisms own local post rules. A worm in the backdrop bends only old light. A
  fungus cap remaps only emissive. A sleeping thing under the floor slows only
  dither phase.

- **Playthrough-specific visual genome**: the game mutates a small set of visual
  genes based on how the player moves and solves rooms. A cautious player grows
  fine stable grain. An aggressive player grows harsh shadow cuts. A curious
  player grows more reflection lies.

- **Dream logic buffers**: keep buffers whose meaning is intentionally unstable:
  guilt, warmth, hunger, attention, echo. Different shaders interpret the same
  channel differently. A high "hunger" value might darken surfaces, attract
  particles, sharpen normals, and make lights flicker.

- **A renderer that forgets**: not just history trails, but imperfect memory.
  Revisited rooms reconstruct their visuals from decayed state, sometimes
  misremembering colors, dither anchors, or background texture alignment.

- **Procedural visual grammar per area**: each area has a small grammar that
  maps gameplay concepts to visual consequences. Fire area: heat bends threshold
  tables. Root area: paths write normals. Ruin area: reflections are more honest
  than sprites. Deep area: color is delayed behind motion.

- **Field hauntings**: after an actor is gone, its contribution remains as a
  field-only presence. An enemy dies but its alert radius keeps staining dither.
  A companion leaves but their calming grain stays near favorite ledges.

- **Visual causality puzzles**: solve rooms by changing how the room is seen,
  not by moving geometry. Make shadows reveal platforms, dither stabilize hidden
  text, normals catch a light path, fog carry a signal, reflection show the
  truthful state.

- **Layer-native collectibles**: pickups that exist only in one render layer.
  A normal-map seed, an emissive spore, a dither fossil, a reflection coin. The
  player learns to read and manipulate layers as world substances.

- **The screen as a petri dish**: the final image is a living culture. Art,
  lighting, particles, and post effects are not separate categories; they are
  competing colonies in a shared visual medium.

- **Personal weather around relationships**: important relationships create
  local visual climates. The space between Player and Bud might have a different
  dither pressure than the space between Player and an enemy. Distance, trust,
  fear, or separation become visible as atmospheric tension.

- **World-state refraction**: facts the player cannot see directly still bend
  the image. A locked door, sleeping boss, remembered promise, or hidden room
  refracts color/normal/dither fields from offscreen, like gravity lensing for
  game state.

- **Material dreams**: when the player rests, saves, dies, or rewinds, materials
  briefly render what they "want" to become instead of what they are. Stone shows
  root paths. Water shows future ripples. Fungal tiles show possible growth.

- **Generational style drift**: repeated visits to an area subtly mutate its
  visual grammar. Not random noise: stable inherited changes. A room can have
  children, scars, dominant genes, recessive dither families, and local
  mutations seeded by play.

- **Attention economy**: visual clarity is finite. When one thing becomes
  sharply readable, something else loses certainty. Boss attacks steal clarity
  from the backdrop. Secrets become readable only when the rest of the room is
  visually quiet.

- **Contradictory layers**: different render layers disagree about the world.
  Color says a wall is solid, reflection says it is hollow, normals reveal a
  door seam, emissive says something is alive. The player learns which layer is
  truthful in which context.

- **Ritualized glitches**: glitch is not generic failure; it has rules, motifs,
  and etiquette. Each kind of impossible event has its own ceremonial visual
  signature: room transition, death, memory, forbidden movement, story rupture.

- **Visual composting**: old effects do not fade to zero; they break down into
  nutrients for later effects. A spent lightning strike becomes fog charge. A
  faded damage flash becomes palette bruise. Dead particles become surface grain.

- **Offscreen image pressure**: things outside the camera influence the visible
  frame before entering it. An approaching enemy compresses dither at the screen
  edge. A bright room ahead warms the current room's shadows. A secret below
  pulls normals downward.

- **The renderer has moods**: the rendering pipeline itself has state like calm,
  suspicious, startled, exhausted, hungry. These moods are not UI and not story
  flags; they are accumulated from play and alter how all lower-level visual
  rules resolve for a while.
