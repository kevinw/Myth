struct Particle {
    pos: vec2<f32>,
    vel: vec2<f32>,
    age: f32,
    lifetime: f32,
    seed: f32,
    effect: f32,
    flags: f32,
    pad: f32,
}

struct type_u002e_StructuredBuffer_u002e_Particle {
    member: array<Particle>,
}

struct EffectParams {
    gravity: f32,
    drag: f32,
    noiseAmount: f32,
    noiseScale: f32,
    sizeStart: f32,
    sizeEnd: f32,
    collisionMode: f32,
    bounciness: f32,
    style: f32,
    pad0_: f32,
    pad1_: f32,
    pad2_: f32,
}

struct type_u002e_StructuredBuffer_u002e_EffectParams {
    member: array<EffectParams>,
}

struct type_u002e_VertexParams {
    Matrix: mat4x4<f32>,
    MaxEffects: f32,
    _pad0_: f32,
    _pad1_: f32,
    _pad2_: f32,
}

struct VertexOutput {
    @location(0) member: vec4<f32>,
    @location(1) member_1: vec2<f32>,
    @location(2) member_2: f32,
    @builtin(position) member_3: vec4<f32>,
}

@group(0) @binding(0) 
var ColorLut: texture_2d<f32>;
@group(0) @binding(100) 
var ColorLutSampler: sampler;
@group(0) @binding(1) 
var<storage> Particles: type_u002e_StructuredBuffer_u002e_Particle;
@group(0) @binding(2) 
var<storage> Effects: type_u002e_StructuredBuffer_u002e_EffectParams;
@group(1) @binding(0) 
var<uniform> VertexParams: type_u002e_VertexParams;
var<private> in_u002e_var_u002e_TEXCOORD0_1: vec2<f32>;
var<private> in_u002e_var_u002e_TEXCOORD1_1: vec2<f32>;
var<private> global: u32;
var<private> out_u002e_var_u002e_TEXCOORD0_: vec4<f32>;
var<private> out_u002e_var_u002e_TEXCOORD1_: vec2<f32>;
var<private> out_u002e_var_u002e_TEXCOORD2_: f32;
var<private> global_1: vec4<f32> = vec4<f32>(0f, 0f, 0f, 1f);

fn vertex_main_1() {
    var phi_89_: f32;

    let _e23 = in_u002e_var_u002e_TEXCOORD0_1;
    let _e24 = in_u002e_var_u002e_TEXCOORD1_1;
    let _e25 = global;
    let _e29 = Particles.member[_e25].pos;
    let _e31 = Particles.member[_e25].age;
    let _e33 = Particles.member[_e25].lifetime;
    let _e35 = Particles.member[_e25].effect;
    let _e36 = (_e35 + 0.5f);
    let _e41 = Effects.member[u32(_e36)].sizeStart;
    let _e43 = Effects.member[u32(_e36)].sizeEnd;
    let _e45 = Effects.member[u32(_e36)].style;
    let _e46 = (_e33 > 0f);
    let _e47 = select(0f, 1f, _e46);
    if _e46 {
        phi_89_ = clamp((_e31 / _e33), 0f, 1f);
    } else {
        phi_89_ = 1f;
    }
    let _e51 = phi_89_;
    let _e55 = (_e29 + (_e23 * (mix(_e41, _e43, _e51) * _e47)));
    let _e57 = VertexParams.Matrix;
    let _e64 = VertexParams.MaxEffects;
    let _e67 = textureSampleLevel(ColorLut, ColorLutSampler, vec2<f32>(_e51, (_e36 / _e64)), 0f);
    out_u002e_var_u002e_TEXCOORD0_ = vec4<f32>(_e67.x, _e67.y, _e67.z, (_e67.w * _e47));
    out_u002e_var_u002e_TEXCOORD1_ = _e24;
    out_u002e_var_u002e_TEXCOORD2_ = _e45;
    global_1 = (vec4<f32>(_e55.x, _e55.y, 0f, 1f) * transpose(_e57));
    return;
}

@vertex 
fn vertex_main(@location(0) in_u002e_var_u002e_TEXCOORD0_: vec2<f32>, @location(1) in_u002e_var_u002e_TEXCOORD1_: vec2<f32>, @builtin(instance_index) param: u32) -> VertexOutput {
    in_u002e_var_u002e_TEXCOORD0_1 = in_u002e_var_u002e_TEXCOORD0_;
    in_u002e_var_u002e_TEXCOORD1_1 = in_u002e_var_u002e_TEXCOORD1_;
    global = param;
    vertex_main_1();
    let _e11 = global_1.y;
    global_1.y = -(_e11);
    let _e13 = out_u002e_var_u002e_TEXCOORD0_;
    let _e14 = out_u002e_var_u002e_TEXCOORD1_;
    let _e15 = out_u002e_var_u002e_TEXCOORD2_;
    let _e16 = global_1;
    return VertexOutput(_e13, _e14, _e15, _e16);
}
