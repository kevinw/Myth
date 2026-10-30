#include "gfx.h"
#include <cstddef>

// `mode` is Foster's batcher type: x = texture * color, y = texture alpha * color, z = color only.
struct Vertex { Vector2 pos, uv; Color color, mode; };
struct Batch { SDL_GPUTexture* texture; u32 start, count; };
struct Pass {
    Texture target;
    Color clear;
    bool keep;
    Blend blend;
    Shader* shader;
    u8 uniforms[1024];
    u32 uniform_count;
    Texture extra[7];
    s32 first_batch;
};
struct Matrix { Vector2 translate, scale{1, 1}; };

static SDL_Window* window;
static SDL_GPUDevice* device;
static Shader batcher;
static SDL_GPUSampler* sampler;
static SDL_GPUSampler* repeat_sampler;
static SDL_GPUBuffer* vertex_buffer;
static SDL_GPUTransferBuffer* upload;
static Texture white;

static Vertex vertices[1 << 16];
static u32 vertex_count;
static Batch batches[4096];  // runs of vertices sharing a texture
static s32 batch_count;
static Pass passes[64];
static s32 pass_count;
static Matrix matrices[16];
static s32 matrix_top;

static constexpr Color FILL_MODE{0, 0, 255, 0};
Color silhouette;
bool render_offscreen;

static SDL_GPUShader* load_stage(const char* name, SDL_GPUShaderStage stage, u32 samplers, u32 uniforms) {
    bool vertex = stage == SDL_GPU_SHADERSTAGE_VERTEX;
    auto code = read_file(tprint("%sshaders/%s.%s.msl", SDL_GetBasePath(), name, vertex ? "vertex" : "fragment"));
    if (code.empty()) return nullptr;
    SDL_GPUShaderCreateInfo info{
        .code_size = code.size(), .code = code.data(), .entrypoint = vertex ? "vertex_main" : "fragment_main",
        .format = SDL_GPU_SHADERFORMAT_MSL, .stage = stage, .num_samplers = samplers, .num_uniform_buffers = uniforms,
    };
    return SDL_CreateGPUShader(device, &info);
}

static SDL_GPUGraphicsPipeline* make_pipeline(const Shader& shader, SDL_GPUTextureFormat format, Blend blend) {
    SDL_GPUShader* vs = load_stage("batcher", SDL_GPU_SHADERSTAGE_VERTEX, 0, 1);
    SDL_GPUShader* fs = load_stage(shader.name, SDL_GPU_SHADERSTAGE_FRAGMENT, shader.samplers, shader.uniforms);
    SDL_GPUGraphicsPipeline* pipeline = nullptr;
    if (vs && fs) {
        SDL_GPUVertexBufferDescription buffer{.slot = 0, .pitch = sizeof(Vertex), .input_rate = SDL_GPU_VERTEXINPUTRATE_VERTEX};
        SDL_GPUVertexAttribute attributes[] = {
            {0, 0, SDL_GPU_VERTEXELEMENTFORMAT_FLOAT2, offsetof(Vertex, pos)},
            {1, 0, SDL_GPU_VERTEXELEMENTFORMAT_FLOAT2, offsetof(Vertex, uv)},
            {2, 0, SDL_GPU_VERTEXELEMENTFORMAT_UBYTE4_NORM, offsetof(Vertex, color)},
            {3, 0, SDL_GPU_VERTEXELEMENTFORMAT_UBYTE4_NORM, offsetof(Vertex, mode)},
        };
        // Premultiplied alpha, like Foster, unless the pass wants otherwise.
        bool max = blend == Blend::Max;
        SDL_GPUColorTargetDescription target{.format = format, .blend_state = {
            .src_color_blendfactor = SDL_GPU_BLENDFACTOR_ONE,
            .dst_color_blendfactor = max ? SDL_GPU_BLENDFACTOR_ONE : SDL_GPU_BLENDFACTOR_ONE_MINUS_SRC_ALPHA,
            .color_blend_op = max ? SDL_GPU_BLENDOP_MAX : SDL_GPU_BLENDOP_ADD,
            .src_alpha_blendfactor = SDL_GPU_BLENDFACTOR_ONE,
            .dst_alpha_blendfactor = max ? SDL_GPU_BLENDFACTOR_ONE : SDL_GPU_BLENDFACTOR_ONE_MINUS_SRC_ALPHA,
            .alpha_blend_op = max ? SDL_GPU_BLENDOP_MAX : SDL_GPU_BLENDOP_ADD,
            .enable_blend = blend != Blend::Replace,
        }};
        SDL_GPUGraphicsPipelineCreateInfo info{
            .vertex_shader = vs, .fragment_shader = fs,
            .vertex_input_state = {&buffer, 1, attributes, 4},
            .primitive_type = SDL_GPU_PRIMITIVETYPE_TRIANGLELIST,
            .target_info = {.color_target_descriptions = &target, .num_color_targets = 1},
        };
        pipeline = SDL_CreateGPUGraphicsPipeline(device, &info);
    }
    if (vs) SDL_ReleaseGPUShader(device, vs);
    if (fs) SDL_ReleaseGPUShader(device, fs);
    return pipeline;
}

static SDL_GPUGraphicsPipeline* pipeline_for(Shader& shader, SDL_GPUTextureFormat format, Blend blend) {
    for (auto& it : shader.pipelines) {
        if (it.pipeline && it.format == format && it.blend == blend) return it.pipeline;
        if (!it.pipeline) {
            it = {format, blend, make_pipeline(shader, format, blend)};
            return it.pipeline;
        }
    }
    SDL_Log("shader %s needs too many pipelines", shader.name);
    return nullptr;
}

std::optional<Shader> load_shader(const char* name, u32 samplers, u32 uniforms) {
    Shader shader{.name = SDL_strdup(name), .samplers = samplers, .uniforms = uniforms};
    // The usual two up front, so a shader that won't load says so now.
    if (!pipeline_for(shader, SDL_GetGPUSwapchainTextureFormat(device, window), Blend::Premultiplied) ||
        !pipeline_for(shader, SDL_GPU_TEXTUREFORMAT_R8G8B8A8_UNORM, Blend::Premultiplied)) {
        SDL_Log("can't load shader %s", name);
        return {};
    }
    return shader;
}

bool gfx_init(SDL_Window* w) {
    window = w;
    device = SDL_CreateGPUDevice(SDL_GPU_SHADERFORMAT_MSL, true, nullptr);
    if (!device || !SDL_ClaimWindowForGPUDevice(device, window)) return false;
    auto loaded = load_shader("batcher");
    if (!loaded) return false;
    batcher = *loaded;

    SDL_GPUSamplerCreateInfo clamp_info{
        .min_filter = SDL_GPU_FILTER_NEAREST, .mag_filter = SDL_GPU_FILTER_NEAREST,
        .address_mode_u = SDL_GPU_SAMPLERADDRESSMODE_CLAMP_TO_EDGE, .address_mode_v = SDL_GPU_SAMPLERADDRESSMODE_CLAMP_TO_EDGE,
        .address_mode_w = SDL_GPU_SAMPLERADDRESSMODE_CLAMP_TO_EDGE,
    };
    SDL_GPUSamplerCreateInfo repeat_info = clamp_info;
    repeat_info.address_mode_u = repeat_info.address_mode_v = repeat_info.address_mode_w = SDL_GPU_SAMPLERADDRESSMODE_REPEAT;
    sampler = SDL_CreateGPUSampler(device, &clamp_info);
    repeat_sampler = SDL_CreateGPUSampler(device, &repeat_info);
    SDL_GPUBufferCreateInfo buffer_info{.usage = SDL_GPU_BUFFERUSAGE_VERTEX, .size = sizeof(vertices)};
    vertex_buffer = SDL_CreateGPUBuffer(device, &buffer_info);
    SDL_GPUTransferBufferCreateInfo upload_info{.usage = SDL_GPU_TRANSFERBUFFERUSAGE_UPLOAD, .size = sizeof(vertices)};
    upload = SDL_CreateGPUTransferBuffer(device, &upload_info);
    white = make_texture(&WHITE, 1, 1);  // rects draw it in fill mode, which ignores it
    return sampler && vertex_buffer && upload && white.gpu;
}

Texture make_target(s32 w, s32 h, SDL_GPUTextureFormat format) {
    SDL_GPUTextureCreateInfo info{
        .type = SDL_GPU_TEXTURETYPE_2D, .format = format, .usage = SDL_GPU_TEXTUREUSAGE_SAMPLER | SDL_GPU_TEXTUREUSAGE_COLOR_TARGET,
        .width = (u32)w, .height = (u32)h, .layer_count_or_depth = 1, .num_levels = 1,
    };
    return {SDL_CreateGPUTexture(device, &info), {(float)w, (float)h}, format};
}

Texture make_texture(const void* pixels, s32 w, s32 h, SDL_GPUTextureFormat format) {
    SDL_GPUTextureCreateInfo info{
        .type = SDL_GPU_TEXTURETYPE_2D, .format = format, .usage = SDL_GPU_TEXTUREUSAGE_SAMPLER,
        .width = (u32)w, .height = (u32)h, .layer_count_or_depth = 1, .num_levels = 1,
    };
    Texture result{SDL_CreateGPUTexture(device, &info), {(float)w, (float)h}, format};
    update_texture(result, pixels);
    return result;
}

void update_texture(const Texture& t, const void* pixels) {
    u32 w = (u32)t.size.x, h = (u32)t.size.y;
    SDL_GPUTransferBufferCreateInfo info{.usage = SDL_GPU_TRANSFERBUFFERUSAGE_UPLOAD, .size = w * h * 4};
    SDL_GPUTransferBuffer* transfer = SDL_CreateGPUTransferBuffer(device, &info);
    memcpy(SDL_MapGPUTransferBuffer(device, transfer, false), pixels, w * h * 4);
    SDL_UnmapGPUTransferBuffer(device, transfer);
    SDL_GPUCommandBuffer* cmd = SDL_AcquireGPUCommandBuffer(device);
    SDL_GPUCopyPass* copy = SDL_BeginGPUCopyPass(cmd);
    SDL_GPUTextureTransferInfo source{.transfer_buffer = transfer};
    SDL_GPUTextureRegion region{.texture = t.gpu, .w = w, .h = h, .d = 1};
    SDL_UploadToGPUTexture(copy, &source, &region, false);
    SDL_EndGPUCopyPass(copy);
    SDL_SubmitGPUCommandBuffer(cmd);
    SDL_ReleaseGPUTransferBuffer(device, transfer);
}

void release_texture(const Texture& t) { SDL_ReleaseGPUTexture(device, t.gpu); }

// QOI (https://qoiformat.org), decoded straight to premultiplied RGBA8 in the scratch arena.
std::optional<Texture> load_qoi(const char* path) {
    auto data = read_file(path);
    if (data.size() < 22 || memcmp(data.data(), "qoif", 4) != 0) return {};
    auto be32 = [](const u8* p) { return s32(p[0]) << 24 | s32(p[1]) << 16 | s32(p[2]) << 8 | p[3]; };
    s32 w = be32(&data[4]), h = be32(&data[8]);
    if (w <= 0 || h <= 0) return {};

    auto pixels = temp.array<Color>(size_t(w) * h);
    Color seen[64] = {};
    Color px{0, 0, 0, 255};
    size_t p = 14, end = data.size() - 8;  // the stream ends with 7 zero bytes and a 1
    s32 run = 0;
    for (Color& out : pixels) {
        if (run > 0) {
            run--;
        } else if (p < end) {
            u8 b = data[p++];
            if (b == 0xfe) {             // RGB
                px.r = data[p]; px.g = data[p + 1]; px.b = data[p + 2];
                p += 3;
            } else if (b == 0xff) {      // RGBA
                px = {data[p], data[p + 1], data[p + 2], data[p + 3]};
                p += 4;
            } else if (b >> 6 == 0) {    // INDEX
                px = seen[b];
            } else if (b >> 6 == 1) {    // DIFF
                px.r += ((b >> 4) & 3) - 2;
                px.g += ((b >> 2) & 3) - 2;
                px.b += (b & 3) - 2;
            } else if (b >> 6 == 2) {    // LUMA
                u8 b2 = data[p++];
                s32 dg = (b & 0x3f) - 32;
                px.r += dg - 8 + ((b2 >> 4) & 0xf);
                px.g += dg;
                px.b += dg - 8 + (b2 & 0xf);
            } else {                     // RUN
                run = b & 0x3f;
            }
            seen[(px.r * 3 + px.g * 5 + px.b * 7 + px.a * 11) % 64] = px;
        }
        out = {u8((px.r * px.a + 127) / 255), u8((px.g * px.a + 127) / 255), u8((px.b * px.a + 127) / 255), px.a};
    }
    return make_texture(pixels.data(), w, h);
}

void begin_pass(const PassInfo& info) {
    if (pass_count == SDL_arraysize(passes)) return;
    Pass& p = passes[pass_count++];
    p = {.target = info.target, .clear = info.clear, .keep = info.keep, .blend = info.blend, .shader = info.shader, .first_batch = batch_count};
    p.uniform_count = (u32)min(info.uniforms.size(), sizeof(p.uniforms));
    memcpy(p.uniforms, info.uniforms.data(), p.uniform_count);
    for (size_t i = 0; i < info.extra.size() && i < SDL_arraysize(p.extra); i++) p.extra[i] = info.extra[i];
}

Vector2 screen_size() {
    int w, h;
    SDL_GetWindowSizeInPixels(window, &w, &h);
    return {(float)w, (float)h};
}

// The batch to add vertices to: the current one unless the texture changes or a pass starts.
static bool batch_for(SDL_GPUTexture* texture, u32 vertices_needed) {
    if (vertex_count + vertices_needed > SDL_arraysize(vertices)) return false;
    bool pass_start = pass_count && passes[pass_count - 1].first_batch == batch_count;
    if (!batch_count || pass_start || batches[batch_count - 1].texture != texture) {
        if (batch_count == SDL_arraysize(batches)) return false;
        batches[batch_count++] = {texture, vertex_count, 0};
    }
    batches[batch_count - 1].count += vertices_needed;
    return true;
}

static Vector2 transform(Vector2 p) { return p * matrices[matrix_top].scale + matrices[matrix_top].translate; }

static void quad(Rect r, Vector2 uv0, Vector2 uv1, Color color, Color mode, SDL_GPUTexture* texture) {
    if (!batch_for(texture, 6)) return;
    if (silhouette.a) { color = silhouette; if (mode.r) mode = {0, 255, 0, 0}; }
    Vector2 p0 = transform(r.pos), p1 = transform(r.pos + r.size);
    Vertex a{p0, uv0, color, mode}, b{{p1.x, p0.y}, {uv1.x, uv0.y}, color, mode};
    Vertex c{p1, uv1, color, mode}, d{{p0.x, p1.y}, {uv0.x, uv1.y}, color, mode};
    for (const Vertex& v : {a, b, c, a, c, d}) vertices[vertex_count++] = v;
}

void rect(Rect r, Color color) { quad(r, {}, {1, 1}, color, FILL_MODE, white.gpu); }

void image(const Texture& texture, Rect source, Vector2 pos, Color color, bool wash) {
    Color mode = wash ? Color{0, 255, 0, 0} : Color{255, 0, 0, 0};
    quad({pos, source.size}, source.pos / texture.size, (source.pos + source.size) / texture.size, color, mode, texture.gpu);
}

void triangle(Vector2 a, Vector2 b, Vector2 c, Color ca, Color cb, Color cc) {
    if (!batch_for(white.gpu, 3)) return;
    if (silhouette.a) ca = cb = cc = silhouette;
    vertices[vertex_count++] = {transform(a), {}, ca, FILL_MODE};
    vertices[vertex_count++] = {transform(b), {}, cb, FILL_MODE};
    vertices[vertex_count++] = {transform(c), {}, cc, FILL_MODE};
}

void circle(Vector2 center, float radius, int steps, Color color) {
    Vector2 previous = center + Vector2{radius, 0};
    for (int i = 1; i <= steps; i++) {
        float angle = i * TAU / steps;
        Vector2 next = center + Vector2{cosf(angle), sinf(angle)} * radius;
        triangle(center, previous, next, color);
        previous = next;
    }
}

void capsule(Vector2 from, Vector2 to, float from_radius, float to_radius, int steps, Color color, bool from_cap, bool to_cap) {
    Vector2 along = to - from;
    float span = along.length();
    along = span > 0.0001f ? along / span : Vector2{0, 1};
    Vector2 across{along.y, -along.x};
    polygon(from + across * from_radius, from - across * from_radius, to - across * to_radius, to + across * to_radius, color);
    if (from_cap) circle(from, from_radius, steps, color);
    if (to_cap) circle(to, to_radius, steps, color);
}

bool save_png(const Texture& t, const char* path) {
    u32 w = (u32)t.size.x, h = (u32)t.size.y;
    SDL_GPUTransferBufferCreateInfo info{.usage = SDL_GPU_TRANSFERBUFFERUSAGE_DOWNLOAD, .size = w * h * 4};
    SDL_GPUTransferBuffer* transfer = SDL_CreateGPUTransferBuffer(device, &info);
    SDL_GPUCommandBuffer* cmd = SDL_AcquireGPUCommandBuffer(device);
    SDL_GPUCopyPass* copy = SDL_BeginGPUCopyPass(cmd);
    SDL_GPUTextureRegion region{.texture = t.gpu, .w = w, .h = h, .d = 1};
    SDL_GPUTextureTransferInfo to{.transfer_buffer = transfer};
    SDL_DownloadFromGPUTexture(copy, &region, &to);
    SDL_EndGPUCopyPass(copy);
    SDL_GPUFence* fence = SDL_SubmitGPUCommandBufferAndAcquireFence(cmd);
    SDL_WaitForGPUFences(device, true, &fence, 1);
    SDL_ReleaseGPUFence(device, fence);
    void* pixels = SDL_MapGPUTransferBuffer(device, transfer, false);
    SDL_Surface* surface = SDL_CreateSurfaceFrom(w, h, SDL_PIXELFORMAT_ABGR8888, pixels, w * 4);
    bool ok = surface && SDL_SavePNG(surface, path);
    SDL_DestroySurface(surface);
    SDL_UnmapGPUTransferBuffer(device, transfer);
    SDL_ReleaseGPUTransferBuffer(device, transfer);
    return ok;
}

void push_matrix(Vector2 translate, Vector2 scale) {
    Matrix top = matrices[matrix_top];
    matrices[++matrix_top] = {top.translate + translate * top.scale, top.scale * scale};
}
void pop_matrix() { matrix_top--; }

void present(Color clear) {
    if (!pass_count || passes[0].first_batch > 0) {
        if (pass_count == SDL_arraysize(passes)) pass_count--;
        memmove(passes + 1, passes, sizeof(Pass) * pass_count++);
        passes[0] = {.clear = clear};
    }
    SDL_GPUCommandBuffer* cmd = SDL_AcquireGPUCommandBuffer(device);
    SDL_GPUTexture* swapchain = nullptr;
    u32 w, h;
    bool acquired = render_offscreen ? SDL_AcquireGPUSwapchainTexture(cmd, window, &swapchain, &w, &h)
                                     : SDL_WaitAndAcquireGPUSwapchainTexture(cmd, window, &swapchain, &w, &h);
    if (acquired && (swapchain || render_offscreen)) {
        if (u32 bytes = vertex_count * sizeof(Vertex)) {
            memcpy(SDL_MapGPUTransferBuffer(device, upload, true), vertices, bytes);
            SDL_UnmapGPUTransferBuffer(device, upload);
            SDL_GPUCopyPass* copy = SDL_BeginGPUCopyPass(cmd);
            SDL_GPUTransferBufferLocation from{.transfer_buffer = upload};
            SDL_GPUBufferRegion to{.buffer = vertex_buffer, .size = bytes};
            SDL_UploadToGPUBuffer(copy, &from, &to, true);
            SDL_EndGPUCopyPass(copy);
        }
        for (s32 index = 0; index < pass_count; index++) {
            Pass& p = passes[index];
            s32 last_batch = index + 1 < pass_count ? passes[index + 1].first_batch : batch_count;
            SDL_GPUTexture* texture = p.target.gpu ? p.target.gpu : swapchain;
            if (!texture) continue;
            Vector2 size = p.target.gpu ? p.target.size : Vector2{(float)w, (float)h};
            const float k = 1.0f / 255;
            SDL_GPUColorTargetInfo target{
                .texture = texture, .clear_color = {p.clear.r * k, p.clear.g * k, p.clear.b * k, p.clear.a * k},
                .load_op = p.keep ? SDL_GPU_LOADOP_LOAD : SDL_GPU_LOADOP_CLEAR, .store_op = SDL_GPU_STOREOP_STORE,
            };
            SDL_GPURenderPass* pass = SDL_BeginGPURenderPass(cmd, &target, 1, nullptr);
            Shader& shader = p.shader ? *p.shader : batcher;
            SDL_GPUTextureFormat format = p.target.gpu ? p.target.format : SDL_GetGPUSwapchainTextureFormat(device, window);
            if (SDL_GPUGraphicsPipeline* pipeline = pipeline_for(shader, format, p.blend)) {
                SDL_BindGPUGraphicsPipeline(pass, pipeline);
                SDL_GPUBufferBinding binding{.buffer = vertex_buffer};
                SDL_BindGPUVertexBuffers(pass, 0, &binding, 1);
                // Pixels to clip space, column-major.
                float ortho[16] = {2 / size.x, 0, 0, 0,  0, -2 / size.y, 0, 0,  0, 0, 1, 0,  -1, 1, 0, 1};
                SDL_PushGPUVertexUniformData(cmd, 0, ortho, sizeof(ortho));
                if (shader.uniforms) SDL_PushGPUFragmentUniformData(cmd, 0, p.uniforms, p.uniform_count);
                for (s32 b = p.first_batch; b < last_batch; b++) {
                    SDL_GPUTextureSamplerBinding bindings[8];
                    bindings[0] = {batches[b].texture, sampler};
                    for (u32 i = 1; i < shader.samplers; i++) bindings[i] = {p.extra[i - 1].gpu ? p.extra[i - 1].gpu : white.gpu, repeat_sampler};
                    SDL_BindGPUFragmentSamplers(pass, 0, bindings, shader.samplers);
                    SDL_DrawGPUPrimitives(pass, batches[b].count, 1, batches[b].start, 0);
                }
            }
            SDL_EndGPURenderPass(pass);
        }
    }
    SDL_SubmitGPUCommandBuffer(cmd);
    vertex_count = 0;
    batch_count = pass_count = 0;
}
