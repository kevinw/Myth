struct Particle {
    pos: vec2<f32>,
    vel: vec2<f32>,
    age: f32,
    lifetime: f32,
    seed: f32,
    effect: f32,
    flags: f32,
    trailHead: f32,
    trailTimer: f32,
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
    trailSeconds: f32,
    trailWidthStart: f32,
    trailWidthEnd: f32,
}

struct type_u002e_StructuredBuffer_u002e_EffectParams {
    member: array<EffectParams>,
}

struct type_u002e_VertexParams {
    Matrix: mat4x4<f32>,
    LutHeight: f32,
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
    var phi_87_: bool;
    var phi_94_: f32;

    let _e24 = in_u002e_var_u002e_TEXCOORD0_1;
    let _e25 = in_u002e_var_u002e_TEXCOORD1_1;
    let _e26 = global;
    let _e30 = Particles.member[_e26].pos;
    let _e32 = Particles.member[_e26].age;
    let _e34 = Particles.member[_e26].lifetime;
    let _e36 = Particles.member[_e26].effect;
    let _e37 = (_e36 + 0.5f);
    let _e42 = Effects.member[u32(_e37)].sizeStart;
    let _e44 = Effects.member[u32(_e37)].sizeEnd;
    let _e46 = Effects.member[u32(_e37)].style;
    let _e47 = (_e34 > 0f);
    phi_87_ = false;
    if _e47 {
        phi_87_ = (_e32 < _e34);
    }
    let _e50 = phi_87_;
    let _e51 = select(0f, 1f, _e50);
    if _e47 {
        phi_94_ = clamp((_e32 / _e34), 0f, 1f);
    } else {
        phi_94_ = 1f;
    }
    let _e55 = phi_94_;
    let _e59 = (_e30 + (_e24 * (mix(_e42, _e44, _e55) * _e51)));
    let _e61 = VertexParams.Matrix;
    let _e68 = VertexParams.LutHeight;
    let _e71 = textureSampleLevel(ColorLut, ColorLutSampler, vec2<f32>(_e55, (_e37 / _e68)), 0f);
    out_u002e_var_u002e_TEXCOORD0_ = vec4<f32>(_e71.x, _e71.y, _e71.z, (_e71.w * _e51));
    out_u002e_var_u002e_TEXCOORD1_ = _e25;
    out_u002e_var_u002e_TEXCOORD2_ = _e46;
    global_1 = (vec4<f32>(_e59.x, _e59.y, 0f, 1f) * transpose(_e61));
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
