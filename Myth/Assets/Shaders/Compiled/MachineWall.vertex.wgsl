struct Instance {
    Rect: vec4<f32>,
    DepthMotion: vec4<f32>,
    AxisAmount: vec4<f32>,
    TintRoughness: vec4<f32>,
    Uv: vec4<f32>,
    Flags: vec4<f32>,
}

struct type_u002e_StructuredBuffer_u002e_Instance {
    member: array<Instance>,
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
    @location(4) member_4: vec4<f32>,
    @builtin(position) member_5: vec4<f32>,
}

@group(0) @binding(0) 
var<storage> Instances: type_u002e_StructuredBuffer_u002e_Instance;
@group(1) @binding(0) 
var<uniform> VertexParams: type_u002e_VertexParams;
var<private> in_u002e_var_u002e_TEXCOORD0_1: vec3<f32>;
var<private> in_u002e_var_u002e_TEXCOORD1_1: vec3<f32>;
var<private> in_u002e_var_u002e_TEXCOORD2_1: vec2<f32>;
var<private> in_u002e_var_u002e_TEXCOORD3_1: vec3<f32>;
var<private> global: u32;
var<private> out_u002e_var_u002e_TEXCOORD0_: vec3<f32>;
var<private> out_u002e_var_u002e_TEXCOORD1_: vec3<f32>;
var<private> out_u002e_var_u002e_TEXCOORD2_: vec2<f32>;
var<private> out_u002e_var_u002e_TEXCOORD3_: vec4<f32>;
var<private> out_u002e_var_u002e_TEXCOORD4_: vec4<f32>;
var<private> global_1: vec4<f32> = vec4<f32>(0f, 0f, 0f, 1f);

fn vertex_main_1() {
    var phi_100_: f32;
    var phi_101_: vec3<f32>;
    var phi_102_: f32;
    var phi_103_: vec3<f32>;
    var phi_104_: f32;
    var phi_105_: vec3<f32>;
    var phi_106_: f32;
    var phi_144_: vec3<f32>;
    var phi_145_: vec3<f32>;
    var phi_146_: vec3<f32>;

    let _e27 = in_u002e_var_u002e_TEXCOORD0_1;
    let _e28 = in_u002e_var_u002e_TEXCOORD1_1;
    let _e29 = in_u002e_var_u002e_TEXCOORD2_1;
    let _e30 = in_u002e_var_u002e_TEXCOORD3_1;
    let _e31 = global;
    let _e34 = Instances.member[_e31];
    let _e42 = VertexParams.Time;
    let _e48 = sin((((_e42 * _e34.DepthMotion.w) * 6.2831855f) + _e34.DepthMotion.z));
    if (_e34.DepthMotion.y == 1f) {
        phi_105_ = vec3<f32>(vec3<f32>(0f, 0f, 0f).x, vec3<f32>(0f, 0f, 0f).y, (_e48 * _e34.AxisAmount.w));
        phi_106_ = 0f;
    } else {
        if (_e34.DepthMotion.y == 2f) {
            phi_103_ = vec3<f32>((_e48 * _e34.AxisAmount.w), vec3<f32>(0f, 0f, 0f).y, vec3<f32>(0f, 0f, 0f).z);
            phi_104_ = 0f;
        } else {
            if (_e34.DepthMotion.y == 3f) {
                phi_101_ = vec3<f32>(vec3<f32>(0f, 0f, 0f).x, (_e48 * _e34.AxisAmount.w), vec3<f32>(0f, 0f, 0f).z);
                phi_102_ = 0f;
            } else {
                phi_100_ = 0f;
                if (_e34.DepthMotion.y == 4f) {
                    phi_100_ = (_e48 * _e34.AxisAmount.w);
                }
                let _e72 = phi_100_;
                phi_101_ = vec3<f32>(0f, 0f, 0f);
                phi_102_ = _e72;
            }
            let _e74 = phi_101_;
            let _e76 = phi_102_;
            phi_103_ = _e74;
            phi_104_ = _e76;
        }
        let _e78 = phi_103_;
        let _e80 = phi_104_;
        phi_105_ = _e78;
        phi_106_ = _e80;
    }
    let _e82 = phi_105_;
    let _e84 = phi_106_;
    let _e89 = (_e27 * vec3<f32>(_e34.Rect.z, _e34.Rect.w, _e34.DepthMotion.x));
    phi_144_ = _e30;
    phi_145_ = _e28;
    phi_146_ = _e89;
    if (_e84 != 0f) {
        let _e92 = normalize(_e34.AxisAmount.xyz);
        let _e93 = sin(_e84);
        let _e94 = cos(_e84);
        let _e101 = (1f - _e94);
        phi_144_ = (((_e30 * _e94) + (cross(_e92, _e30) * _e93)) + ((_e92 * dot(_e92, _e30)) * _e101));
        phi_145_ = (((_e28 * _e94) + (cross(_e92, _e28) * _e93)) + ((_e92 * dot(_e92, _e28)) * _e101));
        phi_146_ = (((_e89 * _e94) + (cross(_e92, _e89) * _e93)) + ((_e92 * dot(_e92, _e89)) * _e101));
    }
    let _e121 = phi_144_;
    let _e123 = phi_145_;
    let _e125 = phi_146_;
    let _e131 = ((_e125 + vec3<f32>(_e34.Rect.x, _e34.Rect.y, (_e34.DepthMotion.x * 0.5f))) + _e82);
    let _e134 = VertexParams.ProjectionSkew;
    let _e139 = VertexParams.Camera;
    let _e142 = VertexParams.Zoom;
    let _e145 = VertexParams.TargetSize;
    let _e148 = ((((((_e131.xy + (_e134 * _e131.z)) - _e139) * _e142) / _e145) * 2f) - vec2<f32>(1f, 1f));
    out_u002e_var_u002e_TEXCOORD0_ = _e123;
    out_u002e_var_u002e_TEXCOORD1_ = _e121;
    out_u002e_var_u002e_TEXCOORD2_ = (_e34.Uv.xy + (_e29 * _e34.Uv.zw));
    out_u002e_var_u002e_TEXCOORD3_ = _e34.TintRoughness;
    out_u002e_var_u002e_TEXCOORD4_ = _e34.Flags;
    global_1 = vec4<f32>(_e148.x, -(_e148.y), 0f, 1f);
    return;
}

@vertex 
fn vertex_main(@location(0) in_u002e_var_u002e_TEXCOORD0_: vec3<f32>, @location(1) in_u002e_var_u002e_TEXCOORD1_: vec3<f32>, @location(2) in_u002e_var_u002e_TEXCOORD2_: vec2<f32>, @location(3) in_u002e_var_u002e_TEXCOORD3_: vec3<f32>, @builtin(instance_index) param: u32) -> VertexOutput {
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
