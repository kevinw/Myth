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
    lightResponse: f32,
    _effectPad0_: f32,
    _effectPad1_: f32,
    _effectPad2_: f32,
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
    @location(3) member_3: f32,
    @builtin(position) member_4: vec4<f32>,
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
var<private> out_u002e_var_u002e_TEXCOORD3_: f32;
var<private> global_1: vec4<f32> = vec4<f32>(0f, 0f, 0f, 1f);

fn vertex_main_1() {
    var phi_91_: bool;
    var phi_98_: f32;

    let _e26 = in_u002e_var_u002e_TEXCOORD0_1;
    let _e27 = in_u002e_var_u002e_TEXCOORD1_1;
    let _e28 = global;
    let _e32 = Particles.member[_e28].pos;
    let _e34 = Particles.member[_e28].age;
    let _e36 = Particles.member[_e28].lifetime;
    let _e38 = Particles.member[_e28].effect;
    let _e39 = (_e38 + 0.5f);
    let _e44 = Effects.member[u32(_e39)].sizeStart;
    let _e46 = Effects.member[u32(_e39)].sizeEnd;
    let _e48 = Effects.member[u32(_e39)].style;
    let _e50 = Effects.member[u32(_e39)].lightResponse;
    let _e51 = (_e36 > 0f);
    phi_91_ = false;
    if _e51 {
        phi_91_ = (_e34 < _e36);
    }
    let _e54 = phi_91_;
    let _e55 = select(0f, 1f, _e54);
    if _e51 {
        phi_98_ = clamp((_e34 / _e36), 0f, 1f);
    } else {
        phi_98_ = 1f;
    }
    let _e59 = phi_98_;
    let _e63 = (_e32 + (_e26 * (mix(_e44, _e46, _e59) * _e55)));
    let _e65 = VertexParams.Matrix;
    let _e72 = VertexParams.LutHeight;
    let _e75 = textureSampleLevel(ColorLut, ColorLutSampler, vec2<f32>(_e59, (_e39 / _e72)), 0f);
    out_u002e_var_u002e_TEXCOORD0_ = vec4<f32>(_e75.x, _e75.y, _e75.z, (_e75.w * _e55));
    out_u002e_var_u002e_TEXCOORD1_ = _e27;
    out_u002e_var_u002e_TEXCOORD2_ = _e48;
    out_u002e_var_u002e_TEXCOORD3_ = _e50;
    global_1 = (vec4<f32>(_e63.x, _e63.y, 0f, 1f) * transpose(_e65));
    return;
}

@vertex 
fn vertex_main(@location(0) in_u002e_var_u002e_TEXCOORD0_: vec2<f32>, @location(1) in_u002e_var_u002e_TEXCOORD1_: vec2<f32>, @builtin(instance_index) param: u32) -> VertexOutput {
    in_u002e_var_u002e_TEXCOORD0_1 = in_u002e_var_u002e_TEXCOORD0_;
    in_u002e_var_u002e_TEXCOORD1_1 = in_u002e_var_u002e_TEXCOORD1_;
    global = param;
    vertex_main_1();
    let _e12 = global_1.y;
    global_1.y = -(_e12);
    let _e14 = out_u002e_var_u002e_TEXCOORD0_;
    let _e15 = out_u002e_var_u002e_TEXCOORD1_;
    let _e16 = out_u002e_var_u002e_TEXCOORD2_;
    let _e17 = out_u002e_var_u002e_TEXCOORD3_;
    let _e18 = global_1;
    return VertexOutput(_e14, _e15, _e16, _e17, _e18);
}
