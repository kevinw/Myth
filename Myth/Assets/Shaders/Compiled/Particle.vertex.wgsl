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
    TargetSize: vec2<f32>,
    Camera: vec2<f32>,
    Zoom: f32,
    MaxEffects: f32,
    _pad0_: f32,
    _pad1_: f32,
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
    var phi_91_: f32;

    let _e27 = in_u002e_var_u002e_TEXCOORD0_1;
    let _e28 = in_u002e_var_u002e_TEXCOORD1_1;
    let _e29 = global;
    let _e33 = Particles.member[_e29].pos;
    let _e35 = Particles.member[_e29].age;
    let _e37 = Particles.member[_e29].lifetime;
    let _e39 = Particles.member[_e29].effect;
    let _e40 = (_e39 + 0.5f);
    let _e45 = Effects.member[u32(_e40)].sizeStart;
    let _e47 = Effects.member[u32(_e40)].sizeEnd;
    let _e49 = Effects.member[u32(_e40)].style;
    let _e50 = (_e37 > 0f);
    let _e51 = select(0f, 1f, _e50);
    if _e50 {
        phi_91_ = clamp((_e35 / _e37), 0f, 1f);
    } else {
        phi_91_ = 1f;
    }
    let _e55 = phi_91_;
    let _e61 = VertexParams.Camera;
    let _e64 = VertexParams.Zoom;
    let _e67 = VertexParams.TargetSize;
    let _e70 = ((((((_e33 + (_e27 * (mix(_e45, _e47, _e55) * _e51))) - _e61) * _e64) / _e67) * 2f) - vec2<f32>(1f, 1f));
    let _e76 = VertexParams.MaxEffects;
    let _e79 = textureSampleLevel(ColorLut, ColorLutSampler, vec2<f32>(_e55, (_e40 / _e76)), 0f);
    out_u002e_var_u002e_TEXCOORD0_ = vec4<f32>(_e79.x, _e79.y, _e79.z, (_e79.w * _e51));
    out_u002e_var_u002e_TEXCOORD1_ = _e28;
    out_u002e_var_u002e_TEXCOORD2_ = _e49;
    global_1 = vec4<f32>(_e70.x, -(_e70.y), 0f, 1f);
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
