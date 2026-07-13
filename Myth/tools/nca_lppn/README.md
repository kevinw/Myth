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
writing the packed runtime weights. Training support will live here after the
reference runtime has been validated.

The checked-in runtime set contains the `circular` variants of `brick`,
`chequered_0050`, `fire`, `lichen`, `slime`, and `water` from the reference
[model directory](https://github.com/Cells2Pixels/Cells2Pixels.github.io/tree/main/dynamic_texture_demo/json_models).
