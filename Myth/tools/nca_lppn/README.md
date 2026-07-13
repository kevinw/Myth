# NCA + LPPN Tools

Tools for the high-resolution dynamic-texture NCA used by `NeuralTextureActor`.
This is separate from `../nca_train`, whose model and binary layout match the
growing NCA actors.

## Importing the reference model

Download `chequered_0050-circular.json` from the Cells2Pixels web demo, then run:

```sh
python3 import_model.py chequered_0050-circular.json \
    ../../Assets/NCA/DynamicTexture/chequered_0050_circular.bin
```

The importer validates every tensor name, shape, type, and byte count before
writing the packed runtime weights.

The checked-in runtime set contains the `circular` variants of `brick`,
`chequered_0050`, `fire`, `lichen`, `slime`, and `water` from the reference
[model directory](https://github.com/Cells2Pixels/Cells2Pixels.github.io/tree/main/dynamic_texture_demo/json_models).

## Training a dynamic texture

Create an isolated environment and prepare the pinned official trainer plus its
`bubbly_0101.jpg` reference target:

```sh
python3.12 -m venv .venv
.venv/bin/pip install -r requirements.txt
.venv/bin/python train.py --prepare-only
```

Run the full circular-motion experiment. The first run also downloads VGG16
weights and the official optical-flow checkpoint:

```sh
.venv/bin/python train.py --motion circular
```

Checkpoints are written to `experiments/bubbly_0101_circular`; the final image
and rollout video are written to `outputs/bubbly_0101_circular`. A reduced
one-epoch toolchain check is available with `--smoke`.

Motion is trained into the model. Use another target name to train a separate
variant, for example:

```sh
.venv/bin/python train.py --motion diverge
.venv/bin/python train.py --motion converge
.venv/bin/python train.py --motion hyperbolic
.venv/bin/python train.py --motion 90
```

Use another opaque texture image with `--target path/to/texture.jpg`.

Prepare downloaded PBR texture sets into paired 512x512 color and OpenGL-normal
PNGs, then train the color target:

```sh
.venv/bin/python prepare_textures.py
.venv/bin/python train.py \
    --target targets/prepared/onyx011_color.png \
    --motion circular --profile screen
```

By default the preparer scans `~/Downloads/NCA_TEXTURES`. It recognizes common
color/diffuse/albedo and normal-map names, prefers an OpenGL normal map, and
converts a DirectX normal map when needed. Pass another source directory as the
first argument, or use `--output`, `--size`, and `--overwrite` as needed.

Pass the paired normal map with `--normal` to train a single model whose LPPN
outputs six channels: albedo in 0-2 and the normal map in 3-5. Both maps are
driven by the same cell states, so they move together at runtime, and the
shared rollout makes one paired run roughly 1.13x the cost of a color-only run
instead of the 2x of two runs:

```sh
.venv/bin/python train.py \
    --target targets/prepared/onyx011_color.png \
    --normal targets/prepared/onyx011_normal.png \
    --motion circular --profile screen
```

## Myth screen profile

The `screen` profile targets Myth's 320x180 presentation rather than decoding
eight samples per NCA cell for the training loss:

```sh
.venv/bin/python train.py --motion circular --profile screen
```

It uses an LPPN scale of 3, 160x160 VGG patches, two appearance scales, a
512x512 loaded target, and no channel-permutation pseudo-targets. It retains the
reference batch size, state pool, and 32-128 step rollout so motion stability
and recovery are still trained. Results are kept separate under
`experiments/bubbly_0101_circular_screen` and
`outputs/bubbly_0101_circular_screen`.

The official motion loss also supports `circle`, `2block_x`, `2block_y`,
`3block`, `4block`, and `grad_<motion-degrees>_<gradient-degrees>`.

Export a completed checkpoint into the packed runtime layout (the exporter
detects three-channel color and six-channel color+normal checkpoints):

```sh
.venv/bin/python export_model.py experiments/bubbly_0101_circular \
    ../../Assets/NCA/DynamicTexture/bubbly_0101_circular.bin
```

`train.py` selects CUDA, then MPS, then CPU. Override that choice with
`--device`. CUDA uses float16; MPS and CPU use float32. MPS also enables
PyTorch's CPU fallback for the antialiased-resize gradient used by the motion
loss. The upstream source and downloaded training artifacts stay ignored,
while the revision and reference image URL are pinned in `train.py`.

The pinned trainer is the `myth` branch of the
[kevinw/Cells2Pixels](https://github.com/kevinw/Cells2Pixels) fork. On top of
the upstream revision it freezes the pretrained VGG and optical-flow loss
network parameters (their operations still propagate gradients into the
generated texture) and carries three changes that keep training numerics
identical while trimming overhead: PENCA's positional-encoding grid is
actually cached across steps (the upstream cache condition never hit), the
optical-flow net's symmetric-padding index maps are built once per shape
instead of per call, and the per-epoch loss log is materialized with a single
device sync instead of one `.item()` per sub-loss. Measured together on MPS
they are worth a few percent; the loop is GPU-bound in the 32-128 step rollout
backward, and `torch.compile` / fp16 autocast were measured slower or neutral
there, so they are intentionally not used.

## Normal-map support

Training with `--normal` produces a six-channel LPPN checkpoint (albedo in
channels 0-2, normal in 3-5). `export_model.py` packs either variant, and
`NeuralTextureActor` detects the format from the weight count: paired models
decode into a second normal texture that the actor draws during the game's
normal-buffer pass, so the animated background responds to normal lighting.
The decoded normals keep the trained OpenGL (+Y up) convention; if lighting
reads vertically inverted against hand-authored normal sprites, flip the
green channel where `NeuralTextureDecode.hlsl` writes `NormalOutput`.
`import_model.py` still handles only the three-channel reference JSON models.
