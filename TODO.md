# TODO

- [ ] Add WebGPU error-scope + label instrumentation to the interop layer so the next browser run prints the exact offending draw. Targeting the `createBindGroup` validation error (`minBindingSize(80) > entrySize(48)`) — likely the LightingComposite AO composite pipeline (80-byte fragment block) being bound with a 48-byte uniform slice. Wrap each render pass / `PushMaterial`+draw in `device.pushErrorScope('validation')` / `popErrorScope()` and label bind groups/buffers so the error names the resource.
