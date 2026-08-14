struct PipeInstance {
    OriginScale: vec4<f32>,
    Basis: vec4<f32>,
    DepthUvRoughness: vec4<f32>,
    Color: vec4<f32>,
}

struct type_u002e_StructuredBuffer_u002e_PipeInstance {
    member: array<PipeInstance>,
}

struct type_u002e_VertexParams {
    Camera: vec2<f32>,
    TargetSize: vec2<f32>,
    ProjectionSkew: vec2<f32>,
    Zoom: f32,
    Time: f32,
}

struct VertexOutput {
    @location(0) member: vec3<f32>,
    @location(1) member_1: vec3<f32>,
    @location(2) member_2: vec2<f32>,
    @location(3) member_3: vec4<f32>,
    @location(4) member_4: f32,
    @builtin(position) member_5: vec4<f32>,
}

@group(0) @binding(0) 
var<storage> Instances: type_u002e_StructuredBuffer_u002e_PipeInstance;
@group(1) @binding(0) 
var<uniform> VertexParams: type_u002e_VertexParams;
var<private> in_u002e_var_u002e_TEXCOORD0_1: vec2<f32>;
var<private> in_u002e_var_u002e_TEXCOORD1_1: vec3<f32>;
var<private> in_u002e_var_u002e_TEXCOORD2_1: vec2<f32>;
var<private> in_u002e_var_u002e_TEXCOORD3_1: vec2<f32>;
var<private> global: u32;
var<private> out_u002e_var_u002e_TEXCOORD0_: vec3<f32>;
var<private> out_u002e_var_u002e_TEXCOORD1_: vec3<f32>;
var<private> out_u002e_var_u002e_TEXCOORD2_: vec2<f32>;
var<private> out_u002e_var_u002e_TEXCOORD3_: vec4<f32>;
var<private> out_u002e_var_u002e_TEXCOORD4_: f32;
var<private> global_1: vec4<f32> = vec4<f32>(0f, 0f, 0f, 1f);

fn vertex_main_1() {
    let _e21 = in_u002e_var_u002e_TEXCOORD0_1;
    let _e22 = in_u002e_var_u002e_TEXCOORD1_1;
    let _e23 = in_u002e_var_u002e_TEXCOORD2_1;
    let _e24 = in_u002e_var_u002e_TEXCOORD3_1;
    let _e25 = global;
    let _e28 = Instances.member[_e25];
    let _e36 = mix(_e28.OriginScale.w, _e28.Color.w, _e24.x);
    let _e41 = ((_e21 * _e28.OriginScale.z) + (_e22.xy * _e36));
    let _e43 = _e28.Basis.xy;
    let _e46 = _e28.Basis.zw;
    let _e50 = (_e28.OriginScale.xy + ((_e43 * _e41.x) + (_e46 * _e41.y)));
    let _e54 = (_e28.DepthUvRoughness.x + (_e22.z * _e36));
    let _e60 = VertexParams.ProjectionSkew;
    let _e64 = VertexParams.Camera;
    let _e67 = VertexParams.Zoom;
    let _e70 = VertexParams.TargetSize;
    let _e73 = ((((((vec3<f32>(_e50.x, _e50.y, _e54).xy + (_e60 * _e54)) - _e64) * _e67) / _e70) * 2f) - vec2<f32>(1f, 1f));
    let _e80 = ((_e43 * _e22.x) + (_e46 * _e22.y));
    let _e89 = ((_e43 * _e23.x) + (_e46 * _e23.y));
    out_u002e_var_u002e_TEXCOORD0_ = normalize(vec3<f32>(_e80.x, _e80.y, _e22.z));
    out_u002e_var_u002e_TEXCOORD1_ = normalize(vec3<f32>(_e89.x, _e89.y, 0f));
    out_u002e_var_u002e_TEXCOORD2_ = vec2<f32>((_e28.DepthUvRoughness.y + (_e24.x * _e28.DepthUvRoughness.z)), _e24.y);
    out_u002e_var_u002e_TEXCOORD3_ = vec4<f32>(_e28.Color.x, _e28.Color.y, _e28.Color.z, _e28.DepthUvRoughness.w);
    out_u002e_var_u002e_TEXCOORD4_ = fract(_e28.DepthUvRoughness.y);
    global_1 = vec4<f32>(_e73.x, -(_e73.y), 0f, 1f);
    return;
}

@vertex 
fn vertex_main(@location(0) in_u002e_var_u002e_TEXCOORD0_: vec2<f32>, @location(1) in_u002e_var_u002e_TEXCOORD1_: vec3<f32>, @location(2) in_u002e_var_u002e_TEXCOORD2_: vec2<f32>, @location(3) in_u002e_var_u002e_TEXCOORD3_: vec2<f32>, @builtin(instance_index) param: u32) -> VertexOutput {
    in_u002e_var_u002e_TEXCOORD0_1 = in_u002e_var_u002e_TEXCOORD0_;
    in_u002e_var_u002e_TEXCOORD1_1 = in_u002e_var_u002e_TEXCOORD1_;
    in_u002e_var_u002e_TEXCOORD2_1 = in_u002e_var_u002e_TEXCOORD2_;
    in_u002e_var_u002e_TEXCOORD3_1 = in_u002e_var_u002e_TEXCOORD3_;
    global = param;
    vertex_main_1();
    let _e17 = global_1.y;
    global_1.y = -(_e17);
    let _e19 = out_u002e_var_u002e_TEXCOORD0_;
    let _e20 = out_u002e_var_u002e_TEXCOORD1_;
    let _e21 = out_u002e_var_u002e_TEXCOORD2_;
    let _e22 = out_u002e_var_u002e_TEXCOORD3_;
    let _e23 = out_u002e_var_u002e_TEXCOORD4_;
    let _e24 = global_1;
    return VertexOutput(_e19, _e20, _e21, _e22, _e23, _e24);
}
