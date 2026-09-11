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

Train and export a target from `targets/` by name. The reference configuration
shipped by Cells2Pixels is the default; the first run also downloads VGG16
weights and the official optical-flow checkpoint:

```sh
./train.py circuit
```

Queue several targets in one invocation and they will be trained, tested, and
exported one after another using the same options:

```sh
./train.py circuit moss path/to/water.png
```

All target paths are checked before training begins, and the queue stops if any
run fails.

This writes the checkpoint to `experiments/circuit_circular`, the final image
and rollout video to `outputs/circuit_circular`, and the game-ready weights to
`../../Assets/NCA/DynamicTexture/circuit_circular.bin`. A reduced one-epoch
toolchain check is available with `./train.py circuit --smoke`. Use
`--no-test` or `--no-export` to skip those final steps.

Motion is trained into the model. Use another target name to train a separate
variant, for example:

```sh
./train.py circuit --motion diverge
./train.py circuit --motion converge
./train.py circuit --motion hyperbolic
./train.py circuit --motion 90
```

An explicit image path works too: `./train.py path/to/texture.jpg`.

Prepare downloaded PBR texture sets into paired 512x512 color and OpenGL-normal
PNGs, then train the color target:

```sh
.venv/bin/python prepare_textures.py
./train.py targets/prepared/onyx011_color.png --motion circular
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
./train.py targets/prepared/onyx011_color.png \
    --normal targets/prepared/onyx011_normal.png \
    --motion circular
```

## Generating normal maps from photos

On macOS, generate paired color and OpenGL-normal targets using Apple's local
Depth Anything V2 Small Core ML model. The model is downloaded and cached on
the first run:

```sh
./generate_normals.py targets/*.jpg
./train.py targets/generated/*_color.png --normal auto
```

The default uses the full estimated depth. Large-scale scene shape and local
surface relief can be controlled independently; setting the scene weight to
zero keeps only relief, while reducing detail emphasizes the scene:

```sh
./generate_normals.py circuit --scene-weight 0 --detail-weight 1
./generate_normals.py circuit --scene-weight 1 --detail-weight .25 --overwrite
```

`--strength` controls normal slope, `--detail-radius` controls the scene/detail
split, and `--smooth` suppresses depth noise. The depth model sees the whole
image at 518x392 and misses fine relief; `--detail-source luminance` takes the
detail band from the photo's brightness instead (CrazyBump-style), keeping the
depth estimate for scene shape. Albedo changes emboss as relief in that mode. Use `--invert` if the inferred
near/far direction is wrong for the image, `--seamless` for wrapping textures,
and `--save-depth` to inspect the normalized estimate. Generated color and
normal files use the exact same square crop. `--normal auto` pairs every
`*_color.png` target with its sibling `*_normal.png`, including batch runs.

## Myth screen profile

The optional `screen` profile targets Myth's 320x180 presentation rather than
decoding eight samples per NCA cell for the training loss:

```sh
./train.py circuit --motion circular --profile screen
```

It uses an LPPN scale of 3, 160x160 VGG patches, two appearance scales, a
512x512 loaded target, and no channel-permutation pseudo-targets. It retains the
reference batch size, state pool, and 32-128 step rollout so motion stability
and recovery are still trained. Results are kept separate under
`experiments/bubbly_0101_circular_screen` and
`outputs/bubbly_0101_circular_screen`.

The official motion loss also supports `circle`, `2block_x`, `2block_y`,
`3block`, `4block`, and `grad_<motion-degrees>_<gradient-degrees>`.

`train.py` exports automatically. To re-export a completed checkpoint manually,
use the exporter directly (it detects three-channel color and six-channel
color+normal checkpoints):

```sh
.venv/bin/python export_model.py experiments/bubbly_0101_circular \
    ../../Assets/NCA/DynamicTexture/bubbly_0101_circular.bin
```

`train.py` selects CUDA, then MPS, then CPU. Override that choice with
`--device`. CUDA uses float16; MPS and CPU use float32 by default. Opt into
MPS mixed precision with `--precision float16`; MPS also enables
PyTorch's CPU fallback for the antialiased-resize gradient used by the motion
loss. MPS keeps the recurrent NCA state in float32 to preserve its numeric
range while autocasting the convolution and decoder work to float16. The
upstream source and downloaded training artifacts stay ignored,
while the revision and reference image URL are pinned in `train.py`.
The pinned fork includes the mixed-precision and loss-sampling optimizations.

On CUDA, `--compile-siren` uses `torch.compile` for the SIREN decoder while
leaving the variable-length NCA rollout eager. Native Windows CUDA compilation
requires the matching [triton-windows](https://github.com/triton-lang/triton-windows)
wheel; PyTorch 2.13 uses Triton 3.7 (`pip install "triton-windows>=3.7,<3.8"`).

The pinned trainer is the `myth` branch of the
[kevinw/Cells2Pixels](https://github.com/kevinw/Cells2Pixels) fork. On top of
the upstream revision it freezes the pretrained VGG and optical-flow loss
network parameters (their operations still propagate gradients into the
generated texture) and carries four changes that keep training numerics
identical while trimming overhead: PENCA's positional-encoding grid is
actually cached across steps (the upstream cache condition never hit), the
optical-flow net's symmetric-padding index maps are built once per shape
instead of per call, the per-epoch loss log is materialized with a single
device sync instead of one `.item()` per sub-loss, and the optical-flow net's
two-frame `Conv3d` runs as the equivalent `Conv2d`. On MPS the first three
together are worth a few percent, while the `Conv3d` swap cuts the motion loss
from about 115 ms to 17 ms per epoch. The rest of the loop is GPU-bound in the
32-128 step rollout backward, and compiling the NCA rollout / fp16 autocast were measured slower or
neutral there. Compiling only the SIREN remains opt-in because its benefit is
CUDA- and workload-dependent.

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
