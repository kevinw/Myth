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

struct type_u002e_StructuredBuffer_u002e_v2float {
    member: array<vec2<f32>>,
}

struct type_u002e_VertexParams {
    Matrix: mat4x4<f32>,
    LutHeight: f32,
    MaxEffects: f32,
    _pad0_: f32,
    _pad1_: f32,
}

struct VertexOutput {
    @location(0) member: vec4<f32>,
    @location(1) member_1: vec2<f32>,
    @builtin(position) member_2: vec4<f32>,
}

@group(0) @binding(0) 
var ColorLut: texture_2d<f32>;
@group(0) @binding(100) 
var ColorLutSampler: sampler;
@group(0) @binding(1) 
var<storage> Particles: type_u002e_StructuredBuffer_u002e_Particle;
@group(0) @binding(2) 
var<storage> Effects: type_u002e_StructuredBuffer_u002e_EffectParams;
@group(0) @binding(3) 
var<storage> Trail: type_u002e_StructuredBuffer_u002e_v2float;
@group(1) @binding(0) 
var<uniform> VertexParams: type_u002e_VertexParams;
var<private> in_u002e_var_u002e_TEXCOORD0_1: vec2<f32>;
var<private> global: u32;
var<private> out_u002e_var_u002e_TEXCOORD0_: vec4<f32>;
var<private> out_u002e_var_u002e_TEXCOORD1_: vec2<f32>;
var<private> global_1: vec4<f32> = vec4<f32>(0f, 0f, 0f, 1f);

fn vertex_main_1() {
    var phi_108_: u32;
    var phi_138_: f32;
    var phi_143_: bool;
    var phi_159_: vec2<f32>;

    let _e34 = in_u002e_var_u002e_TEXCOORD0_1;
    let _e35 = global;
    let _e36 = (_e35 / 15u);
    let _e41 = Particles.member[_e36].lifetime;
    let _e43 = Particles.member[_e36].effect;
    let _e45 = Particles.member[_e36].trailHead;
    let _e47 = Particles.member[_e36].trailTimer;
    let _e53 = Effects.member[u32((_e43 + 0.5f))].trailSeconds;
    let _e55 = Effects.member[u32((_e43 + 0.5f))].trailWidthStart;
    let _e57 = Effects.member[u32((_e43 + 0.5f))].trailWidthEnd;
    let _e58 = (_e36 * 16u);
    let _e64 = ((_e35 % 15u) + u32((_e34.x + 1f)));
    let _e65 = (_e64 == 0u);
    if _e65 {
        phi_108_ = 0u;
    } else {
        phi_108_ = (_e64 - 1u);
    }
    let _e68 = phi_108_;
    let _e71 = (u32((_e45 + 0.5f)) + 16u);
    let _e77 = Trail.member[(_e58 + ((_e71 - _e64) % 16u))];
    let _e83 = Trail.member[(_e58 + ((_e71 - _e68) % 16u))];
    let _e89 = Trail.member[(_e58 + ((_e71 - min((_e64 + 1u), 15u)) % 16u))];
    if _e65 {
        phi_138_ = 0f;
    } else {
        phi_138_ = clamp((((f32((_e64 - 1u)) * (_e53 * 0.06666667f)) + _e47) / max(_e53, 0.00001f)), 0f, 1f);
    }
    let _e99 = phi_138_;
    phi_143_ = false;
    if (_e41 > 0f) {
        phi_143_ = (_e53 > 0f);
    }
    let _e103 = phi_143_;
    let _e104 = select(0f, 1f, _e103);
    let _e107 = (_e83 - _e89);
    let _e108 = length(_e107);
    if (_e108 > 0.0001f) {
        phi_159_ = (vec2<f32>(-(_e107.y), _e107.x) / vec2(_e108));
    } else {
        phi_159_ = vec2<f32>(0f, 0f);
    }
    let _e117 = phi_159_;
    let _e123 = (_e77 + (((_e117 * (_e34.y * 2f)) * (mix(_e55, _e57, _e99) * _e104)) * 0.5f));
    let _e125 = VertexParams.Matrix;
    let _e132 = VertexParams.MaxEffects;
    let _e136 = VertexParams.LutHeight;
    let _e139 = textureSampleLevel(ColorLut, ColorLutSampler, vec2<f32>(_e99, (((_e132 + _e43) + 0.5f) / _e136)), 0f);
    out_u002e_var_u002e_TEXCOORD0_ = vec4<f32>(_e139.x, _e139.y, _e139.z, (_e139.w * _e104));
    out_u002e_var_u002e_TEXCOORD1_ = vec2<f32>(_e99, (_e34.y + 0.5f));
    global_1 = (vec4<f32>(_e123.x, _e123.y, 0f, 1f) * transpose(_e125));
    return;
}

@vertex 
fn vertex_main(@location(0) in_u002e_var_u002e_TEXCOORD0_: vec2<f32>, @builtin(instance_index) param: u32) -> VertexOutput {
    in_u002e_var_u002e_TEXCOORD0_1 = in_u002e_var_u002e_TEXCOORD0_;
    global = param;
    vertex_main_1();
    let _e8 = global_1.y;
    global_1.y = -(_e8);
    let _e10 = out_u002e_var_u002e_TEXCOORD0_;
    let _e11 = out_u002e_var_u002e_TEXCOORD1_;
    let _e12 = global_1;
    return VertexOutput(_e10, _e11, _e12);
}
