// A Foster-ish batcher over SDL_GPU: textured, tinted quads in pixel space,
// drawn with Foster's own Batcher shader (see build.jai).

Point2 :: struct { x, y: s32; }
operator + :: (a: Point2, b: Point2) -> Point2 { return .{a.x + b.x, a.y + b.y}; }
operator - :: (a: Point2, b: Point2) -> Point2 { return .{a.x - b.x, a.y - b.y}; }
operator * :: (a: Point2, k: s32) -> Point2 #symmetric { return .{a.x * k, a.y * k}; }
operator == :: (a: Point2, b: Point2) -> bool { return a.x == b.x && a.y == b.y; }
to_vector :: (p: Point2) -> Vector2 { return .{xx p.x, xx p.y}; }

RectInt :: struct { x, y, w, h: s32; }

Rect :: struct { pos, size: Vector2; }
inflate :: (r: Rect, by: float) -> Rect { return .{r.pos - Vector2.{by, by}, r.size + Vector2.{2*by, 2*by}}; }

Color :: struct { r, g, b, a: u8; }
rgb :: (hex: u32) -> Color { return .{xx (hex >> 16) & 0xff, xx (hex >> 8) & 0xff, xx hex & 0xff, 255}; }
// "1A0A2E" -> Color
rgb :: (hex: string, default := Color.{}) -> Color {
    value, ok := string_to_int(hex, base = 16, T = u32);
    return ifx ok && hex.count == 6 then rgb(value) else default;
}
WHITE :: Color.{255, 255, 255, 255};
approach :: (value: float, target: float, amount: float) -> float {
    return ifx value < target then min(value + amount, target) else max(value - amount, target);
}
approach :: (value: Vector2, target: Vector2, amount: float) -> Vector2 {
    return .{approach(value.x, target.x, amount), approach(value.y, target.y, amount)};
}
sign :: (v: float) -> s32 { return cast(s32) (v > 0) - cast(s32) (v < 0); }

// Premultiplied, like everything we draw.
fade :: (c: Color, alpha: float) -> Color { return .{xx (c.r * alpha), xx (c.g * alpha), xx (c.b * alpha), xx (c.a * alpha)}; }

Texture :: struct { gpu: *SDL_GPUTexture; size: Vector2; }

gfx_init :: (w: *SDL_Window) -> bool {
    window = w;
    device = SDL_CreateGPUDevice(.MSL, true, null);
    if !device || !SDL_ClaimWindowForGPUDevice(device, window) return false;

    dir := path_strip_filename(get_path_of_running_executable());
    vs := load_shader(tprint("%shaders/batcher.vertex.msl", dir), .VERTEX, samplers = 0, uniforms = 1);
    fs := load_shader(tprint("%shaders/batcher.fragment.msl", dir), .FRAGMENT, samplers = 1, uniforms = 0);
    if !vs || !fs return false;
    defer { SDL_ReleaseGPUShader(device, vs); SDL_ReleaseGPUShader(device, fs); }

    buffer := SDL_GPUVertexBufferDescription.{slot = 0, pitch = size_of(Vertex), input_rate = .VERTEX};
    attributes := SDL_GPUVertexAttribute.[
        .{0, 0, .FLOAT2,      #run field_offset("pos")},
        .{1, 0, .FLOAT2,      #run field_offset("uv")},
        .{2, 0, .UBYTE4_NORM, #run field_offset("color")},
        .{3, 0, .UBYTE4_NORM, #run field_offset("mode")},
    ];
    // Premultiplied alpha, like Foster.
    target := SDL_GPUColorTargetDescription.{
        format = SDL_GetGPUSwapchainTextureFormat(device, window),
        blend_state = .{
            src_color_blendfactor = .ONE, dst_color_blendfactor = .ONE_MINUS_SRC_ALPHA, color_blend_op = .ADD,
            src_alpha_blendfactor = .ONE, dst_alpha_blendfactor = .ONE_MINUS_SRC_ALPHA, alpha_blend_op = .ADD,
            enable_blend = true,
        },
    };
    info: SDL_GPUGraphicsPipelineCreateInfo;
    info.vertex_shader = vs;
    info.fragment_shader = fs;
    info.vertex_input_state = .{*buffer, 1, attributes.data, attributes.count};
    info.primitive_type = .TRIANGLELIST;
    info.target_info = .{color_target_descriptions = *target, num_color_targets = 1};
    pipeline = SDL_CreateGPUGraphicsPipeline(device, *info);

    sampler = SDL_CreateGPUSampler(device, *(SDL_GPUSamplerCreateInfo.{
        min_filter = .NEAREST, mag_filter = .NEAREST,
        address_mode_u = .CLAMP_TO_EDGE, address_mode_v = .CLAMP_TO_EDGE, address_mode_w = .CLAMP_TO_EDGE,
    }));
    vertex_buffer = SDL_CreateGPUBuffer(device, *(SDL_GPUBufferCreateInfo.{usage = .VERTEX, size = size_of(type_of(vertices))}));
    upload = SDL_CreateGPUTransferBuffer(device, *(SDL_GPUTransferBufferCreateInfo.{usage = .UPLOAD, size = size_of(type_of(vertices))}));
    pixel := WHITE;
    white = make_texture(xx *pixel, 1, 1);  // rects draw it in fill mode, which ignores it
    return pipeline && sampler && vertex_buffer && upload && white.gpu;
}

// pixels: w*h premultiplied RGBA8
make_texture :: (pixels: *u8, w: s32, h: s32) -> Texture {
    bytes := cast(u32) (w * h * 4);
    texture := SDL_CreateGPUTexture(device, *(SDL_GPUTextureCreateInfo.{
        type = ._2D, format = .R8G8B8A8_UNORM, usage = .SAMPLER, width = xx w, height = xx h, layer_count_or_depth = 1, num_levels = 1,
    }));
    transfer := SDL_CreateGPUTransferBuffer(device, *(SDL_GPUTransferBufferCreateInfo.{usage = .UPLOAD, size = bytes}));
    defer SDL_ReleaseGPUTransferBuffer(device, transfer);
    memcpy(SDL_MapGPUTransferBuffer(device, transfer, false), pixels, bytes);
    SDL_UnmapGPUTransferBuffer(device, transfer);

    cmd := SDL_AcquireGPUCommandBuffer(device);
    copy := SDL_BeginGPUCopyPass(cmd);
    SDL_UploadToGPUTexture(copy, *(SDL_GPUTextureTransferInfo.{transfer_buffer = transfer}), *(SDL_GPUTextureRegion.{texture = texture, w = xx w, h = xx h, d = 1}), false);
    SDL_EndGPUCopyPass(copy);
    SDL_SubmitGPUCommandBuffer(cmd);
    return .{texture, .{xx w, xx h}};
}

release_texture :: (t: Texture) { SDL_ReleaseGPUTexture(device, t.gpu); }

screen_size :: () -> Vector2 {
    w, h: s32;
    SDL_GetWindowSizeInPixels(window, *w, *h);
    return .{xx w, xx h};
}

rect :: (r: Rect, color: Color) {
    quad(r, .{}, .{1, 1}, color, .{0, 0, 255, 0}, white.gpu);
}

// `source` in texels; drawn 1:1 at `pos` (top-left).
image :: (texture: Texture, source: Rect, pos: Vector2, color := WHITE) {
    quad(.{pos, source.size}, source.pos / texture.size, (source.pos + source.size) / texture.size, color, .{255, 0, 0, 0}, texture.gpu);
}

// Translate, then scale, relative to the current matrix. A negative scale flips.
push_matrix :: (translate: Vector2, scale: Vector2) {
    top := matrices[matrix_top];
    matrix_top += 1;
    matrices[matrix_top] = .{top.translate + translate * top.scale, top.scale * scale};
}
push_matrix :: (translate: Vector2, scale: float) { push_matrix(translate, Vector2.{scale, scale}); }
pop_matrix :: () { matrix_top -= 1; }

present :: (clear: Color) {
    defer { vertex_count = 0; batches.count = 0; }
    cmd := SDL_AcquireGPUCommandBuffer(device);
    swapchain: *SDL_GPUTexture;
    w, h: u32;
    if !SDL_WaitAndAcquireGPUSwapchainTexture(cmd, window, *swapchain, *w, *h) || !swapchain {
        SDL_SubmitGPUCommandBuffer(cmd);
        return;
    }

    bytes := cast(u32) (vertex_count * size_of(Vertex));
    if bytes {
        memcpy(SDL_MapGPUTransferBuffer(device, upload, true), vertices.data, bytes);
        SDL_UnmapGPUTransferBuffer(device, upload);
        copy := SDL_BeginGPUCopyPass(cmd);
        SDL_UploadToGPUBuffer(copy, *(SDL_GPUTransferBufferLocation.{transfer_buffer = upload}), *(SDL_GPUBufferRegion.{buffer = vertex_buffer, size = bytes}), true);
        SDL_EndGPUCopyPass(copy);
    }

    k := 1.0 / 255;
    target := SDL_GPUColorTargetInfo.{texture = swapchain, clear_color = .{clear.r * k, clear.g * k, clear.b * k, clear.a * k}, load_op = .CLEAR, store_op = .STORE};
    pass := SDL_BeginGPURenderPass(cmd, *target, 1, null);
    SDL_BindGPUGraphicsPipeline(pass, pipeline);
    SDL_BindGPUVertexBuffers(pass, 0, *(SDL_GPUBufferBinding.{buffer = vertex_buffer}), 1);
    // Pixels to clip space, column-major.
    ortho := float.[2.0 / w, 0, 0, 0,  0, -2.0 / h, 0, 0,  0, 0, 1, 0,  -1, 1, 0, 1];
    SDL_PushGPUVertexUniformData(cmd, 0, ortho.data, size_of(type_of(ortho)));
    for batches {
        SDL_BindGPUFragmentSamplers(pass, 0, *(SDL_GPUTextureSamplerBinding.{it.texture, sampler}), 1);
        SDL_DrawGPUPrimitives(pass, it.count, 1, it.start, 0);
    }
    SDL_EndGPURenderPass(pass);
    SDL_SubmitGPUCommandBuffer(cmd);
}

#scope_file

// `mode` is Foster's batcher type: x = texture * color, y = texture alpha * color, z = color only.
Vertex :: struct { pos: Vector2; uv: Vector2; color: Color; mode: Color; }
field_offset :: (name: string) -> u32 { _, offset := get_field(type_info(Vertex), name); return xx offset; }
Batch :: struct { texture: *SDL_GPUTexture; start, count: u32; }
Matrix :: struct { translate: Vector2; scale := Vector2.{1, 1}; }

window: *SDL_Window;
device: *SDL_GPUDevice;
pipeline: *SDL_GPUGraphicsPipeline;
sampler: *SDL_GPUSampler;
vertex_buffer: *SDL_GPUBuffer;
upload: *SDL_GPUTransferBuffer;
white: Texture;

vertices: [1 << 16] Vertex;
vertex_count: u32;
batches: [..] Batch;     // runs of vertices sharing a texture
matrices: [16] Matrix;
matrix_top: int;

load_shader :: (path: string, stage: SDL_GPUShaderStage, samplers: u32, uniforms: u32) -> *SDL_GPUShader {
    code, ok := read_entire_file(path);
    if !ok return null;
    defer free(code);
    return SDL_CreateGPUShader(device, *(SDL_GPUShaderCreateInfo.{
        code_size = xx code.count, code = code.data, entrypoint = ifx stage == .VERTEX then "vertex_main".data else "fragment_main".data,
        format = .MSL, stage = stage, num_samplers = samplers, num_uniform_buffers = uniforms,
    }));
}

quad :: (r: Rect, uv0: Vector2, uv1: Vector2, color: Color, mode: Color, texture: *SDL_GPUTexture) {
    if vertex_count + 6 > vertices.count return;
    if !batches.count || peek(batches).texture != texture array_add(*batches, .{texture, vertex_count, 0});
    m := matrices[matrix_top];
    p0 := r.pos * m.scale + m.translate;
    p1 := (r.pos + r.size) * m.scale + m.translate;
    a := Vertex.{p0, uv0, color, mode};
    b := Vertex.{.{p1.x, p0.y}, .{uv1.x, uv0.y}, color, mode};
    c := Vertex.{p1, uv1, color, mode};
    d := Vertex.{.{p0.x, p1.y}, .{uv0.x, uv1.y}, color, mode};
    for Vertex.[a, b, c, a, c, d] { vertices[vertex_count] = it; vertex_count += 1; }
    peek_pointer(batches).count += 6;
}

#import "Basic";
#import "File";
#import "Math";
#import "String";
#import "System";
#import "SDL3";
