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
    @builtin(position) member_1: vec4<f32>,
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
var<private> global: u32;
var<private> out_u002e_var_u002e_TEXCOORD0_: vec4<f32>;
var<private> global_1: vec4<f32> = vec4<f32>(0f, 0f, 0f, 1f);

fn vertex_main_1() {
    var phi_82_: f32;

    let _e23 = in_u002e_var_u002e_TEXCOORD0_1;
    let _e24 = global;
    let _e28 = Particles.member[_e24].pos;
    let _e30 = Particles.member[_e24].age;
    let _e32 = Particles.member[_e24].lifetime;
    let _e34 = Particles.member[_e24].effect;
    let _e35 = (_e34 + 0.5f);
    let _e40 = Effects.member[u32(_e35)].sizeStart;
    let _e42 = Effects.member[u32(_e35)].sizeEnd;
    let _e43 = (_e32 > 0f);
    let _e44 = select(0f, 1f, _e43);
    if _e43 {
        phi_82_ = clamp((_e30 / _e32), 0f, 1f);
    } else {
        phi_82_ = 1f;
    }
    let _e48 = phi_82_;
    let _e54 = VertexParams.Camera;
    let _e57 = VertexParams.Zoom;
    let _e60 = VertexParams.TargetSize;
    let _e63 = ((((((_e28 + (_e23 * (mix(_e40, _e42, _e48) * _e44))) - _e54) * _e57) / _e60) * 2f) - vec2<f32>(1f, 1f));
    let _e69 = VertexParams.MaxEffects;
    let _e72 = textureSampleLevel(ColorLut, ColorLutSampler, vec2<f32>(_e48, (_e35 / _e69)), 0f);
    out_u002e_var_u002e_TEXCOORD0_ = vec4<f32>(_e72.x, _e72.y, _e72.z, (_e72.w * _e44));
    global_1 = vec4<f32>(_e63.x, -(_e63.y), 0f, 1f);
    return;
}

@vertex 
fn vertex_main(@location(0) in_u002e_var_u002e_TEXCOORD0_: vec2<f32>, @builtin(instance_index) param: u32) -> VertexOutput {
    in_u002e_var_u002e_TEXCOORD0_1 = in_u002e_var_u002e_TEXCOORD0_;
    global = param;
    vertex_main_1();
    let _e7 = global_1.y;
    global_1.y = -(_e7);
    let _e9 = out_u002e_var_u002e_TEXCOORD0_;
    let _e10 = global_1;
    return VertexOutput(_e9, _e10);
}
