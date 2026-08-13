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
    var phi_102_: f32;
    var phi_103_: vec3<f32>;
    var phi_104_: f32;
    var phi_105_: vec3<f32>;
    var phi_106_: f32;
    var phi_107_: vec3<f32>;
    var phi_108_: f32;
    var phi_146_: vec3<f32>;
    var phi_147_: vec3<f32>;
    var phi_148_: vec3<f32>;
    var phi_191_: vec4<f32>;

    let _e29 = in_u002e_var_u002e_TEXCOORD0_1;
    let _e30 = in_u002e_var_u002e_TEXCOORD1_1;
    let _e31 = in_u002e_var_u002e_TEXCOORD2_1;
    let _e32 = in_u002e_var_u002e_TEXCOORD3_1;
    let _e33 = global;
    let _e36 = Instances.member[_e33];
    let _e44 = VertexParams.Time;
    let _e50 = sin((((_e44 * _e36.DepthMotion.w) * 6.2831855f) + _e36.DepthMotion.z));
    if (_e36.DepthMotion.y == 1f) {
        phi_107_ = vec3<f32>(vec3<f32>(0f, 0f, 0f).x, vec3<f32>(0f, 0f, 0f).y, (_e50 * _e36.AxisAmount.w));
        phi_108_ = 0f;
    } else {
        if (_e36.DepthMotion.y == 2f) {
            phi_105_ = vec3<f32>((_e50 * _e36.AxisAmount.w), vec3<f32>(0f, 0f, 0f).y, vec3<f32>(0f, 0f, 0f).z);
            phi_106_ = 0f;
        } else {
            if (_e36.DepthMotion.y == 3f) {
                phi_103_ = vec3<f32>(vec3<f32>(0f, 0f, 0f).x, (_e50 * _e36.AxisAmount.w), vec3<f32>(0f, 0f, 0f).z);
                phi_104_ = 0f;
            } else {
                phi_102_ = 0f;
                if (_e36.DepthMotion.y == 4f) {
                    phi_102_ = (_e50 * _e36.AxisAmount.w);
                }
                let _e74 = phi_102_;
                phi_103_ = vec3<f32>(0f, 0f, 0f);
                phi_104_ = _e74;
            }
            let _e76 = phi_103_;
            let _e78 = phi_104_;
            phi_105_ = _e76;
            phi_106_ = _e78;
        }
        let _e80 = phi_105_;
        let _e82 = phi_106_;
        phi_107_ = _e80;
        phi_108_ = _e82;
    }
    let _e84 = phi_107_;
    let _e86 = phi_108_;
    let _e91 = (_e29 * vec3<f32>(_e36.Rect.z, _e36.Rect.w, _e36.DepthMotion.x));
    phi_146_ = _e32;
    phi_147_ = _e30;
    phi_148_ = _e91;
    if (_e86 != 0f) {
        let _e94 = normalize(_e36.AxisAmount.xyz);
        let _e95 = sin(_e86);
        let _e96 = cos(_e86);
        let _e103 = (1f - _e96);
        phi_146_ = (((_e32 * _e96) + (cross(_e94, _e32) * _e95)) + ((_e94 * dot(_e94, _e32)) * _e103));
        phi_147_ = (((_e30 * _e96) + (cross(_e94, _e30) * _e95)) + ((_e94 * dot(_e94, _e30)) * _e103));
        phi_148_ = (((_e91 * _e96) + (cross(_e94, _e91) * _e95)) + ((_e94 * dot(_e94, _e91)) * _e103));
    }
    let _e123 = phi_146_;
    let _e125 = phi_147_;
    let _e127 = phi_148_;
    let _e133 = ((_e127 + vec3<f32>(_e36.Rect.x, _e36.Rect.y, (_e36.DepthMotion.x * 0.5f))) + _e84);
    let _e136 = VertexParams.ProjectionSkew;
    let _e141 = VertexParams.Camera;
    let _e144 = VertexParams.Zoom;
    let _e147 = VertexParams.TargetSize;
    let _e150 = ((((((_e133.xy + (_e136 * _e133.z)) - _e141) * _e144) / _e147) * 2f) - vec2<f32>(1f, 1f));
    phi_191_ = _e36.Flags;
    if (_e36.Flags.w > 0f) {
        phi_191_ = vec4<f32>((_e36.Flags.x * (0.72f + (sin((((_e44 * _e36.Flags.w) * 3f) + _e36.DepthMotion.z)) * 0.28f))), _e36.Flags.y, _e36.Flags.z, _e36.Flags.w);
    }
    let _e173 = phi_191_;
    out_u002e_var_u002e_TEXCOORD0_ = _e125;
    out_u002e_var_u002e_TEXCOORD1_ = _e123;
    out_u002e_var_u002e_TEXCOORD2_ = (_e36.Uv.xy + (_e31 * _e36.Uv.zw));
    out_u002e_var_u002e_TEXCOORD3_ = _e36.TintRoughness;
    out_u002e_var_u002e_TEXCOORD4_ = _e173;
    global_1 = vec4<f32>(_e150.x, -(_e150.y), 0f, 1f);
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
