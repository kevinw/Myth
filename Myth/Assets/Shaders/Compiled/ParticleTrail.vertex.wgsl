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
@group(0) @binding(3) 
var<storage> Trail: type_u002e_StructuredBuffer_u002e_v2float;
@group(1) @binding(0) 
var<uniform> VertexParams: type_u002e_VertexParams;
var<private> in_u002e_var_u002e_TEXCOORD0_1: vec2<f32>;
var<private> global: u32;
var<private> out_u002e_var_u002e_TEXCOORD0_: vec4<f32>;
var<private> out_u002e_var_u002e_TEXCOORD1_: vec2<f32>;
var<private> out_u002e_var_u002e_TEXCOORD2_: f32;
var<private> global_1: vec4<f32> = vec4<f32>(0f, 0f, 0f, 1f);

fn vertex_main_1() {
    var phi_113_: u32;
    var phi_143_: f32;
    var phi_148_: bool;
    var phi_164_: vec2<f32>;

    let _e36 = in_u002e_var_u002e_TEXCOORD0_1;
    let _e37 = global;
    let _e38 = (_e37 / 15u);
    let _e43 = Particles.member[_e38].lifetime;
    let _e45 = Particles.member[_e38].effect;
    let _e47 = Particles.member[_e38].trailHead;
    let _e49 = Particles.member[_e38].trailTimer;
    let _e55 = Effects.member[u32((_e45 + 0.5f))].trailSeconds;
    let _e57 = Effects.member[u32((_e45 + 0.5f))].trailWidthStart;
    let _e59 = Effects.member[u32((_e45 + 0.5f))].trailWidthEnd;
    let _e61 = Effects.member[u32((_e45 + 0.5f))].lightResponse;
    let _e62 = (_e38 * 16u);
    let _e68 = ((_e37 % 15u) + u32((_e36.x + 1f)));
    let _e69 = (_e68 == 0u);
    if _e69 {
        phi_113_ = 0u;
    } else {
        phi_113_ = (_e68 - 1u);
    }
    let _e72 = phi_113_;
    let _e75 = (u32((_e47 + 0.5f)) + 16u);
    let _e81 = Trail.member[(_e62 + ((_e75 - _e68) % 16u))];
    let _e87 = Trail.member[(_e62 + ((_e75 - _e72) % 16u))];
    let _e93 = Trail.member[(_e62 + ((_e75 - min((_e68 + 1u), 15u)) % 16u))];
    if _e69 {
        phi_143_ = 0f;
    } else {
        phi_143_ = clamp((((f32((_e68 - 1u)) * (_e55 * 0.06666667f)) + _e49) / max(_e55, 0.00001f)), 0f, 1f);
    }
    let _e103 = phi_143_;
    phi_148_ = false;
    if (_e43 > 0f) {
        phi_148_ = (_e55 > 0f);
    }
    let _e107 = phi_148_;
    let _e108 = select(0f, 1f, _e107);
    let _e111 = (_e87 - _e93);
    let _e112 = length(_e111);
    if (_e112 > 0.0001f) {
        phi_164_ = (vec2<f32>(-(_e111.y), _e111.x) / vec2(_e112));
    } else {
        phi_164_ = vec2<f32>(0f, 0f);
    }
    let _e121 = phi_164_;
    let _e127 = (_e81 + (((_e121 * (_e36.y * 2f)) * (mix(_e57, _e59, _e103) * _e108)) * 0.5f));
    let _e129 = VertexParams.Matrix;
    let _e136 = VertexParams.MaxEffects;
    let _e140 = VertexParams.LutHeight;
    let _e143 = textureSampleLevel(ColorLut, ColorLutSampler, vec2<f32>(_e103, (((_e136 + _e45) + 0.5f) / _e140)), 0f);
    out_u002e_var_u002e_TEXCOORD0_ = vec4<f32>(_e143.x, _e143.y, _e143.z, (_e143.w * _e108));
    out_u002e_var_u002e_TEXCOORD1_ = vec2<f32>(_e103, (_e36.y + 0.5f));
    out_u002e_var_u002e_TEXCOORD2_ = _e61;
    global_1 = (vec4<f32>(_e127.x, _e127.y, 0f, 1f) * transpose(_e129));
    return;
}

@vertex 
fn vertex_main(@location(0) in_u002e_var_u002e_TEXCOORD0_: vec2<f32>, @builtin(instance_index) param: u32) -> VertexOutput {
    in_u002e_var_u002e_TEXCOORD0_1 = in_u002e_var_u002e_TEXCOORD0_;
    global = param;
    vertex_main_1();
    let _e9 = global_1.y;
    global_1.y = -(_e9);
    let _e11 = out_u002e_var_u002e_TEXCOORD0_;
    let _e12 = out_u002e_var_u002e_TEXCOORD1_;
    let _e13 = out_u002e_var_u002e_TEXCOORD2_;
    let _e14 = global_1;
    return VertexOutput(_e11, _e12, _e13, _e14);
}
