# Neural Cellular Texture Explainer

## Short version

It is not an animated texture or a generated video. It is a tiny learned living
system. A coarse grid continually evolves local hidden state, and a second neural
network turns that state into detailed pixels.

Every patch of the material contains a cell with an internal state. Each cell
repeatedly looks at itself and its immediate neighbours, then applies the same
learned rule to decide how to change. During training, those local rules are
rewarded for collectively producing both the appearance of a target material and
a desired kind of motion, such as circular flow.

Nobody programs "move this blob left" or "draw this curl." The network discovers
a local dynamical rule whose global behaviour looks right.

The clever part of Cells²Pixels is that the cells do not correspond directly to
the final pixels. The cellular automaton remains a relatively cheap, coarse
simulation; a second small network acts like a microscope, converting its hidden
state into sharp color and surface detail at whatever output resolution is
needed. This decoupling of simulation resolution from rendering resolution is the
paper's main contribution.

That also explains why interaction feels organic. When the player damages the
state, they are not playing a canned damage animation: they have knocked a
dynamical system away from its preferred behaviour, and we watch it reorganize.

## Thirty-second talk track

> This surface is a neural cellular automaton. It is a grid of tiny cells, each
> carrying hidden numbers and repeatedly running the same learned local rule.
> Together they settle into this moving material. We then use a second neural
> network to decode that coarse living state into detailed pixels and normals. So
> it is not looping footage: the material is continuously generating itself, and
> when the player disturbs it, the system genuinely has to recover.

## One level deeper

Myth normally uses a `128×128` grid. Each cell carries 16 floating-point values.
These are not RGB values but a learned latent state: an internal vocabulary whose
meaning was discovered during training.

A simulation step has two conceptual parts.

### 1. Perception

Each cell samples a `3×3` neighbourhood and computes four views of its 16
channels:

- its current state;
- horizontal Sobel gradient;
- vertical Sobel gradient;
- Laplacian.

These mean roughly: "what am I?", "what changes left-to-right?", "what changes
up-to-down?", and "how different am I from my surroundings?"

Myth also supplies normalized position because the dynamic models use a
positional-encoding NCA, or PENCA. The neural weights are shared everywhere, but
the cells are not entirely oblivious to where they are. With a fixed world cell
scale, Myth mirrors and repeats the trained coordinate domain so a model can
cover a region larger than its original `128×128` domain without a coordinate
discontinuity at each repeat.

### 2. Adaptation

The perception vector goes through a tiny neural update rule: 128 ReLU hidden
units produce a 16-value change. The result is added residually:

```text
new state = old state + learned local change
```

Only a random fraction of cells normally updates on each step--50% by default.
This asynchronous fire rate contributes to the texture's variation and makes the
learned rule tolerate cells advancing at slightly different times.

The state lives in two GPU buffers. Each compute dispatch reads one, writes the
other, and swaps them for the next step.

### 3. Decoding cells into pixels

After evolving the state, a separate Local Pattern Producing Network (LPPN)
renders it. For every output pixel, Myth:

1. Maps the pixel into the coarse state grid.
2. Bilinearly interpolates the surrounding 16-channel cell states.
3. Calculates sine/cosine coordinates inside the local cell.
4. Feeds those 20 values into a small 32-wide SIREN-style network.
5. Outputs RGB and, for Myth's paired models, a surface normal.

The local coordinates are important. Without them, interpolated coarse states
could only describe coarse detail. They let the decoder answer: "Given the
biological state of this neighbourhood, what should exist at this exact sub-cell
location?"

The trained model contains only about 13,500 learned scalar parameters. The
recurrent simulation remains coarse while only the relatively inexpensive
decoder scales with output resolution. Cells²Pixels demonstrates the same
`128×128` lattice decoded at `1024×1024` and `8192×8192` without retraining.

## What training teaches

Myth's reference training configuration rolls the automaton forward for a random
32--128 steps. It evaluates:

- appearance using multiscale VGG feature statistics and an optimal-transport
  texture loss;
- motion using optical-flow direction and strength;
- numerical stability with an overflow penalty;
- recovery and long-term behaviour using a pool of previously evolved states,
  with periodic fresh seed injection.

It is therefore not memorizing a sequence of frames. It learns a rule that can
continue indefinitely while producing frames with the desired appearance and
motion statistics. Appearance and motion can come from different references: in
the usual Myth setup, appearance comes from one image and motion from a named
vector field such as `circular`, `diverge`, or `hyperbolic`.

The dynamic-texture branch is presented as a bonus or exploratory mode by
Cells²Pixels and descends from the earlier DyNCA work. DyNCA introduced
appearance-plus-motion training and post-training controls such as speed,
direction, local coordinate transformations, and editing brushes.

## What Myth adds

The paper supplies the coarse automaton plus local decoder. Myth turns it into a
game material:

- Up to four independently trained models can coexist in one actor.
- A painted RGBA mask assigns model ownership spatially.
- Myth blends both the models' update rules and their decoded appearance.
- Ownership boundaries can themselves advect through a curl field while the
  authored silhouette remains fixed.
- Pawns, feet, hits, enemies, and radial waves erase latent state.
- The automaton repairs that erased state.
- Models can emit synchronized color and normals into Myth's lighting.
- The visible painted silhouette can cast shadows and receive an additional
  generated edge normal.
- Shutdown can morph the whole field toward another model--currently smoke--as a
  destructive wave travels through it.

The four-model blend is more than an image crossfade. At each cell, Myth blends
the learned state-change vectors produced by the active models; during rendering,
it also blends their decoders. This amounts to blending learned dynamical systems.

One important qualification is that independently trained models do not share a
semantically aligned latent space. There is no theoretical guarantee that one
model interprets another model's 16 hidden values usefully. The combined update
and decode happens to make a compelling art tool, but it is not the same thing as
interpolating within one deliberately shared latent space.

## Suggested attribution

> The architecture is based on *Neural Cellular Automata: From Cells to Pixels*
> by Ehsan Pajouheshgar, Yitao Xu, Ali Abbasi, Alexander Mordvintsev, Wenzel
> Jakob, and Sabine Süsstrunk, with dynamic-texture training descended from the
> authors' DyNCA work. We ported the learned update and decoder to game compute
> shaders, then added material blending, normals, masking, and gameplay
> interaction.

## Sources and relevant implementation

- [Neural Cellular Automata: From Cells to Pixels](https://arxiv.org/abs/2506.22899)
- [Cells²Pixels project and interactive demos](https://cells2pixels.github.io/)
- [DyNCA: Real-time Dynamic Texture Synthesis Using Neural Cellular Automata](https://arxiv.org/abs/2211.11417)
- `Source/Actors/NeuralTextureActor.cs`
- `Assets/Shaders/Compute/NeuralTextureUpdate.hlsl`
- `Assets/Shaders/Compute/NeuralTextureDecode.hlsl`
- `Assets/Shaders/Compute/NeuralTexturePerturb.hlsl`
- `Assets/Shaders/Compute/NeuralTextureCurlAdvection.hlsl`
- `tools/nca_lppn/README.md`
- `tools/nca_lppn/configs/bubbly_circular.yaml`

