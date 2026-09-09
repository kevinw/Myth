# More Cellular Automata Exploration

The largest unexplored territory is not merely more texture targets. It is
allowing information to cross all four boundaries of the system:

```text
game world -> cell state -> learned dynamics -> decoded world signals
```

At present, input is mostly erasure and output is mostly color plus normals.

## Highest-leverage additions

### More perturbation verbs

Current perturbations multiply all 16 state channels toward zero. This gives us
damage and healing, but only one verb.

Add a small set of stamp operations:

- erase;
- inject noise;
- add a learned seed state;
- push selected latent channels;
- copy or transplant state;
- paint model ownership.

This enables infection, fertilization, burning, freezing, poisoning, and
inoculation without six bespoke visual-effect systems. Carrying a living sample
from one wall and planting it elsewhere is an especially good fit for the game.

Do not assume arbitrary constants form useful seed states. Capture known-good
states from trained rollouts, or train explicit perturbation and seed channels.

### Make model ownership gameplay-writable

The difficult machinery already exists: four-channel ownership masks, blended
update rules, blended decoders, and curl advection. Let actors paint ownership
during play.

Then the room can remember actions in its material identity:

- footsteps slowly convert moss to stone;
- damage introduces smoke or infection;
- light grows one model while darkness grows another;
- rival organisms compete for territory;
- a boss converts the arena and the player converts it back;
- switches change what a surface heals into rather than merely recoloring it.

This is more consequential than latent erasure because it changes which
attractor the damaged region returns to.

### Decode render-control fields

Train or derive additional outputs that are not ordinary visible color:

- emissive intensity and hue;
- opacity or density;
- dither family, threshold, and phase;
- surface-layer displacement;
- reflection distortion;
- fog density;
- roughness, height, or ambient occlusion;
- a 2D flow vector for particles and shader distortion;
- material identity or palette pressure.

The Cells²Pixels paper already demonstrates nine-channel PBR output: albedo,
normal, height, roughness, and ambient occlusion. It also demonstrates volumetric
color and density.

In Myth, height could drive parallax, silhouette bevel, shadow displacement, or
surface-layer depth. Density gives us foreground neural smoke, including letting
the player disappear progressively inside it rather than simply being drawn
behind a rectangle.

An NCA need not draw itself at all. An invisible ecology could write only into
normal, fog, dither, emissive, or tile metadata buffers.

### Feed world fields into the update

The automaton currently knows position and neighbours but almost nothing about
the room. Give it slowly varying external inputs such as:

- illumination and shadow;
- wind;
- gravity direction;
- sound or music energy;
- temperature, infection, or story state;
- distance to the player;
- nearby solid geometry;
- water, fog, or room color fields.

The most immediately legible experiment is phototropism: living material leans
toward light, changes state in shadow, and recoils when a light is extinguished.

DyNCA shows that rotating the Sobel perception basis changes motion direction
after training. Local wind, gravity, radial waves, or magnetic actors could
therefore rotate the material's perception. The texture would not merely be
displaced by wind; its learned dynamics would reinterpret which way is forward.

## Gameplay directions

### Living traversal

A surface grows footholds, safe zones, or openings after being seeded. Damage
causes those affordances to reorganize. Collision should remain CPU-authored;
the automaton reveals and animates the authoritative state.

Possibilities:

- temporary paths grow outward from player-planted seeds;
- climbable areas heal behind the player but collapse under repeated use;
- a safe material competes with a harmful one across the arena;
- the player must damage a field to expose a short-lived route while it regrows.

### Signal-routing puzzles

Inject an impulse and let it propagate through a living circuit. Doors and
machines respond to coarse regional activity rather than a particular pixel.

Branches could split, interfere, heal after interruption, or change propagation
rules when their model ownership changes. Authored blocker masks can give a room
stable puzzle topology while the NCA supplies motion and recovery.

### Regenerative enemies and bosses

Decode an enemy's skin, armour, organs, or silhouette from state. Attacks destroy
local state; disabling authored organs changes which model governs regrowth.

Interesting distinctions:

- damage that erases state but permits normal healing;
- poison that changes the attractor;
- cauterization that changes ownership and prevents healing;
- severed state transplanted elsewhere to grow a hazard;
- armour whose normals and emissive state reveal how close it is to failure.

### Portable ecology

Let the player take a latent patch from one biome and graft it into another. The
state itself becomes an inventory item rather than an abstract key.

Compatible models could exchange a small state tile directly. Incompatible
models should exchange a named seed or genome input rather than assuming their
latent channels have matching meanings.

### Persistent scars and place memory

Persist a sparse history of perturbations or ownership changes instead of the
entire state grid. Replay them on room entry and let the automaton reconstruct
the scar.

Possible persistent facts:

- where the player fought;
- which organism last controlled a region;
- paths repeatedly travelled;
- light exposure or darkness over time;
- traces brought in from another biome.

A full `128×128×16` float state is roughly 1 MiB before compression, per
snapshot. Sparse authored events, a low-resolution ownership field, or a few
seed patches are cheaper and more stable save data.

### Coupled colonies

Separate `NeuralTextureActor` instances currently own isolated state buffers.
Allow compatible fields touching at their boundaries to exchange cells or
signals.

This could let material cross:

- room geometry and props;
- the floor and an enemy standing on it;
- both sides of a portal;
- adjacent rooms;
- a player-carried sample and its destination.

A simpler first version is to exchange perturbation stamps at boundaries rather
than copying raw latent state.

### Time mechanics

Locally freeze, accelerate, or desynchronize update rates. True reversal will
not generally work because the learned dynamics are dissipative and stochastic,
but pause, dormancy, delayed response, and accelerated healing will.

Possible mechanics:

- a region heals only while observed or only while off-screen;
- music sets the update cadence;
- a projectile leaves slow-time cells in its wake;
- one material runs faster than its neighbour and invades it;
- freeze preserves damage that resumes healing when thawed.

### The NCA as a sensor or control field

Hidden or explicitly decoded channels could drive:

- enemy alertness;
- particle spawning;
- light color;
- fog and reverb;
- switches based on regional coverage;
- spawn locations;
- player occlusion inside smoke;
- animation or IK reactions on actors touching the field.

Prefer region-scale signals and slow transitions. Exact latent values are not a
good gameplay language unless they were explicitly trained to be one.

## Visual directions

### Foreground volume and occlusion

Render neural smoke in front of actors with learned or derived density. Sample
that density when compositing the player so the body disappears progressively
inside the material. Different density thresholds could affect silhouettes,
lights, particles, and reflections independently.

### Dynamic coordinate transforms

The decoder already exposes detail scale, phase, patch frequency, SIREN
frequency, latent gain, and sampling mode. Drive these locally instead of only
per actor:

- waves compress and expand detail;
- wind shears local decoder coordinates;
- gravity rotates the local basis;
- damage detaches fine detail from coarse motion;
- a portal changes scale continuously across its boundary;
- music quantizes or phase-locks the texture.

These controls can leave the underlying organism untouched while changing how
we observe it. Large excursions may leave the distribution on which the decoder
was trained and should be treated as expressive distortion, not guaranteed
model behaviour.

### Learned material lighting

Extend paired color-and-normal training to height, roughness, emissive, or
opacity. Keep all maps driven by the same recurrent cell state so detail stays
aligned while moving.

Rendering-aware training is another step: instead of supervising each map only
against its target, also supervise the final lit result under varied lights.
This may reduce the patch artifacts that small errors can amplify under strong
specular lighting.

### Cross-system ripples

Neural perturbation rings should be able to write into the rest of the visual
language:

- lighting and dither ripple outward with the damaged state;
- normal strength collapses and heals behind a wave;
- particles follow the NCA's decoded flow;
- fog remembers a perturbation after the color has recovered;
- reflection distortion exposes otherwise invisible cell motion.

### Alternative domains and topology

The paper applies the same coarse-state/local-decoder idea to 2D grids, 3D
volumes, and meshes. Useful 2D-game adaptations include:

- an automaton living along a one-dimensional rope or spline;
- state attached to a deforming character mesh;
- toroidal or portal-connected room topology;
- a surface-following field that wraps corners;
- a 3D density field rendered as a 2D volume;
- one logical field decoded through several disconnected windows.

## Training directions

### Conditional multi-material models

Instead of blending independently trained checkpoints, train one model with an
explicit genome or material-condition input. This gives it a deliberately shared
latent space and can make continuous transitions reliable:

- calm to agitated;
- wet to dry;
- healthy to infected;
- safe to hostile;
- moss to smoke;
- several materials coexisting and competing.

This is the principled version of Myth's current four-model blend. It should
also permit gameplay to move continuously through a material space rather than
selecting among four unrelated update rules.

### Train the interactions, not only the attractor

Current damage works because a robust system often recovers when state is
erased. More specific behaviours should appear in the training distribution:

- healing after scratches, holes, and moving cuts;
- responding to scalar environmental inputs;
- accepting transplanted seeds;
- stable borders between genomes;
- propagating readable signals;
- controlled dormancy and reawakening;
- morphing between explicitly paired attractors.

If a behaviour matters to gameplay, train for it rather than relying entirely
on a lucky out-of-distribution response.

### Separate evolution and appearance creatively

The NCA and decoder need not always come from the same checkpoint. Experiments
could deliberately swap:

- one material's dynamics with another's decoder;
- color from one decoder and normals from another;
- a shared NCA with room-specific render decoders;
- a shared decoder with differently conditioned dynamics.

Independent latent bases make arbitrary swaps unstable, but jointly trained
families could make this a real authoring axis: how something behaves versus how
it appears.

## Guardrails

Critical gameplay authority should stay off the raw GPU state.

The automaton is stochastic, floating-point behaviour can differ across GPU
backends, and reading a large state grid back to the CPU introduces latency and
potential stalls. Do not make a frame-perfect jump or collision depend on an
arbitrary latent channel crossing a precise threshold.

Use the NCA as the expressive visible organism around deterministic game rules.
If gameplay needs to observe it, reduce it on the GPU to a few coarse values--for
example regional activity, coverage, or dominant model--and tolerate delayed
readback. Another good pattern is a CPU-authored state machine that supplies the
truth while the NCA supplies its spatial expression.

Content compatibility also matters more than implementation compatibility. If
new training or packed formats replace old ones, migrate the model assets and
remove obsolete runtime paths rather than accumulating permanent fallback
formats.

## Best next experiments

### 1. Gameplay paints ownership

Implement an ownership-paint perturbation and let player movement convert one
learned material into another. Leave the conversion behind and let both sides
continue evolving.

This reuses almost all existing infrastructure and demonstrates the central
idea clearly: the player does not merely damage the living image; they change
what it believes it is.

### 2. Directional perception control

Add a global rotation for the Sobel perception basis, then drive it from wind or
a radial source. If it behaves well, generalize the rotation to a coarse vector
field.

This is cheap, legible, and grounded in DyNCA's demonstrated post-training
controls.

### 3. One additional decoded visual bus

Emissive is probably the smallest useful target because the lighting pipeline
already consumes it and the result is unmistakable. Density is the most
game-specific target because it unlocks foreground smoke and actor occlusion.

### 4. Seed transplantation

Add a safe captured-state stamp for models trained from the same family. Let an
actor pick up a small patch and inject it elsewhere. This tests whether latent
state can become a tangible gameplay object before building persistence or
cross-room coupling around it.

## Sources

- [Neural Cellular Automata: From Cells to Pixels](https://arxiv.org/abs/2506.22899)
- [Cells²Pixels project and interactive demos](https://cells2pixels.github.io/)
- [DyNCA: Real-time Dynamic Texture Synthesis Using Neural Cellular Automata](https://arxiv.org/abs/2211.11417)
- `Notes/Attic/DYNAMIC_VISUAL_RESEARCH.md`
- `Notes/Attic/GPU_LIGHTING_IDEAS.md`

