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

The official motion loss also supports `circle`, `2block_x`, `2block_y`,
`3block`, `4block`, and `grad_<motion-degrees>_<gradient-degrees>`.

Export a completed checkpoint into the exact three-channel runtime layout:

```sh
.venv/bin/python export_model.py experiments/bubbly_0101_circular \
    ../../Assets/NCA/DynamicTexture/bubbly_0101_circular.bin
```

`train.py` selects CUDA, then MPS, then CPU. Override that choice with
`--device`. CUDA uses float16; MPS and CPU use float32. MPS also enables
PyTorch's CPU fallback for the antialiased-resize gradient used by the motion
loss. The upstream source and downloaded training artifacts stay ignored,
while the revision and reference image URL are pinned in `train.py`.
