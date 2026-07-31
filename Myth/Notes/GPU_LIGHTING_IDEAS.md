# GPU Lighting Ideas

The GPU lighting pipeline is useful as more than a final scene multiplier. Its
polar point-light shadow maps, orthographic sun shadow map, and consolidated
light masks provide reusable visibility and illumination fields for other
effects.

## Illuminated spore fog

Status: initial prototype implemented.

Add a third output to `LightMasks` which accumulates:

```text
light contribution * visibility * animated spore density
```

Render it at half resolution and composite it behind foreground sprites. A
scrolling noise texture could provide density variation, with dithering instead
of blur to fit the game's visual style. This would make pools and shafts of
light reveal drifting spores without calculating shadows again.

This is the best first prototype because the existing mask pass has already
calculated attenuation, color, normals, and visibility for every light and
pixel.

The initial implementation is full-resolution, world-anchored, direct-light
only, and additively composited between the background and foreground. It uses
two differently transformed samples of the existing seamless lighting noise.
The live-editable controls are strength, scale, drift, contrast, tint, point
strength, and sun strength.

## Phototropic fungal simulation

Feed the previous frame's `BackgroundLightMask` into the neural cellular
automata update:

```text
growth += illumination * phototropic bias
decay  += darkness * dormancy
color   = lerp(shadow color, light color, illumination)
```

Fungal growth could lean toward light, retreat into shadow, change color, or
become active at dusk. Using the previous frame avoids CPU readback and awkward
render scheduling; one frame of latency is immaterial for a visual simulation.

Authoritative gameplay should continue to use a CPU approximation unless
GPU-dependent, one-frame-delayed behavior is explicitly acceptable.

## Living shadow edges

Detect light-mask gradients or polar-depth discontinuities and draw a thin
bioluminescent fringe where light meets darkness. The fringe could pulse, crawl
along surfaces, or shed particles, making shadows feel biologically active.

## Shadow memory

Ping-pong a history light texture:

```text
history = max(current, history * decay)
```

Moving lamps, projectiles, or fireflies would leave fading illumination trails,
and shadows would slowly heal behind them. Different decay rates could make
particular regions feel dreamlike, infected, or electrically charged.

## Organic light cookies

The point-light shader already calculates polar angle and light-local
coordinates. Modulate attenuation with an angular/radial texture or procedural
function to produce:

- Mushroom-cap silhouettes
- Rotating tendrils
- Irregular flame lobes
- Flashlight and searchlight cones
- Broken or flickering lamps

Cookie orientation and animation belong on each `GpuLight`.

## Per-light penumbra personality

Move source radius and softness onto `GpuLight`. Different emitters could then
have distinct shadow behavior:

- Fire: broad, unstable penumbrae
- Spores: small, tight halos
- Alien or infected lights: asymmetric noisy shadows
- Sunlight: stable wide softness

Blue-noise angular taps or temporally rotating taps could reduce banding while
matching the existing dithered aesthetic.

## Transmissive fungal membranes

Extend the shadow representation with blocker material or transmission data.
Thin fungal membranes, fluids, crystals, or organic windows could cast colored
or partially attenuated shadows instead of binary visibility.

Segment occluders could store a material index alongside depth. Sprite shadow
masks could encode RGB transmission directly. Supporting multiple overlapping
transmitters would require more than the current nearest-depth representation.

## Light-field distortion

Warp the polar lookup angle or the sun's perpendicular coordinate with a
low-frequency vector field. This could create refracted, nervous, or infected
light around specific organisms without moving their actual occluders.

Possible sources include NCA vector fields, room-authored flow textures, shock
waves, or a transient field around the player.

## Additional possibilities

- Lit particles sample the background mask so dust and spores sparkle only
  inside illuminated regions.
- Sample distance behind the sun's first blocker to shape crepuscular rays or
  darkness density rather than using binary visibility.
- Feed illumination into purely visual enemy, plant, or environmental shaders.
- Use visibility gradients to place procedural motes or crawling highlights at
  shadow boundaries.
- Increase the number of cheap moving lights for fireflies, glowing projectiles,
  fungal nodes, and electrical arcs.

## Pipeline constraints

The final background and foreground masks consolidate all lights. Effects which
only need total illumination can sample `BackgroundLightMask` or
`ForegroundLightMask` afterward. Effects needing individual light identity,
such as cookies, material transmission, or unique softness, must be implemented
inside `LightMasks` or written to an additional per-light representation.

Current scaling limits are:

- 64 point lights
- 512 angular samples per point light
- 512 parallel samples for sunlight
- Shadow-depth work proportional to lights, samples, and segment count
- Light-mask work proportional to pixels and light count
- No tiled light culling or segment acceleration structure
- RGBA8 consolidated light masks

If denser scenes eventually exceed these limits, likely next steps are
screen-tiled light lists and a uniform grid or BVH for segment tracing.
