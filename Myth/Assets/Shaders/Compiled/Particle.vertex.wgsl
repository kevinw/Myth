struct Particle {
    pos: vec2<f32>,
    vel: vec2<f32>,
    age: f32,
    lifetime: f32,
    seed: f32,
    pad: f32,
}

struct type_u002e_StructuredBuffer_u002e_Particle {
    member: array<Particle>,
}

struct type_u002e_VertexParams {
    TargetSize: vec2<f32>,
    ParticleSize: f32,
    _pad: f32,
}

struct VertexOutput {
    @location(0) member: vec4<f32>,
    @builtin(position) member_1: vec4<f32>,
}

@group(0) @binding(0) 
var<storage> Particles: type_u002e_StructuredBuffer_u002e_Particle;
@group(1) @binding(0) 
var<uniform> VertexParams: type_u002e_VertexParams;
var<private> in_u002e_var_u002e_TEXCOORD0_1: vec2<f32>;
var<private> global: u32;
var<private> out_u002e_var_u002e_TEXCOORD0_: vec4<f32>;
var<private> global_1: vec4<f32> = vec4<f32>(0f, 0f, 0f, 1f);

fn vertex_main_1() {
    var phi_63_: f32;

    let _e22 = in_u002e_var_u002e_TEXCOORD0_1;
    let _e23 = global;
    let _e27 = Particles.member[_e23].pos;
    let _e29 = Particles.member[_e23].age;
    let _e31 = Particles.member[_e23].lifetime;
    let _e32 = (_e31 > 0f);
    let _e33 = select(0f, 1f, _e32);
    if _e32 {
        phi_63_ = clamp((_e29 / _e31), 0f, 1f);
    } else {
        phi_63_ = 1f;
    }
    let _e37 = phi_63_;
    let _e39 = VertexParams.ParticleSize;
    let _e44 = VertexParams.TargetSize;
    let _e47 = ((((_e27 + (_e22 * (_e39 * _e33))) / _e44) * 2f) - vec2<f32>(1f, 1f));
    let _e53 = mix(vec3<f32>(1f, 0.9f, 0.4f), vec3<f32>(0.8f, 0.12f, 0.05f), vec3(_e37));
    out_u002e_var_u002e_TEXCOORD0_ = vec4<f32>(_e53.x, _e53.y, _e53.z, (_e33 * (1f - _e37)));
    global_1 = vec4<f32>(_e47.x, -(_e47.y), 0f, 1f);
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
