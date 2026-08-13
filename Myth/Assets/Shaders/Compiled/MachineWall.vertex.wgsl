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
    var phi_106_: f32;
    var phi_107_: vec3<f32>;
    var phi_108_: f32;
    var phi_109_: vec3<f32>;
    var phi_110_: f32;
    var phi_111_: vec3<f32>;
    var phi_112_: f32;
    var phi_150_: vec3<f32>;
    var phi_151_: vec3<f32>;
    var phi_152_: vec3<f32>;
    var phi_195_: vec4<f32>;

    let _e30 = in_u002e_var_u002e_TEXCOORD0_1;
    let _e31 = in_u002e_var_u002e_TEXCOORD1_1;
    let _e32 = in_u002e_var_u002e_TEXCOORD2_1;
    let _e33 = in_u002e_var_u002e_TEXCOORD3_1;
    let _e34 = global;
    let _e37 = Instances.member[_e34];
    let _e45 = VertexParams.Time;
    let _e51 = sin((((_e45 * _e37.DepthMotion.w) * 6.2831855f) + _e37.DepthMotion.z));
    if (_e37.DepthMotion.y == 1f) {
        phi_111_ = vec3<f32>(vec3<f32>(0f, 0f, 0f).x, vec3<f32>(0f, 0f, 0f).y, (_e51 * _e37.AxisAmount.w));
        phi_112_ = 0f;
    } else {
        if (_e37.DepthMotion.y == 2f) {
            phi_109_ = vec3<f32>((_e51 * _e37.AxisAmount.w), vec3<f32>(0f, 0f, 0f).y, vec3<f32>(0f, 0f, 0f).z);
            phi_110_ = 0f;
        } else {
            if (_e37.DepthMotion.y == 3f) {
                phi_107_ = vec3<f32>(vec3<f32>(0f, 0f, 0f).x, (_e51 * _e37.AxisAmount.w), vec3<f32>(0f, 0f, 0f).z);
                phi_108_ = 0f;
            } else {
                if (_e37.DepthMotion.y == 4f) {
                    phi_106_ = (_e51 * _e37.AxisAmount.w);
                } else {
                    phi_106_ = select(0f, _e37.AxisAmount.w, (_e37.DepthMotion.y == 5f));
                }
                let _e77 = phi_106_;
                phi_107_ = vec3<f32>(0f, 0f, 0f);
                phi_108_ = _e77;
            }
            let _e79 = phi_107_;
            let _e81 = phi_108_;
            phi_109_ = _e79;
            phi_110_ = _e81;
        }
        let _e83 = phi_109_;
        let _e85 = phi_110_;
        phi_111_ = _e83;
        phi_112_ = _e85;
    }
    let _e87 = phi_111_;
    let _e89 = phi_112_;
    let _e94 = (_e30 * vec3<f32>(_e37.Rect.z, _e37.Rect.w, _e37.DepthMotion.x));
    phi_150_ = _e33;
    phi_151_ = _e31;
    phi_152_ = _e94;
    if (_e89 != 0f) {
        let _e97 = normalize(_e37.AxisAmount.xyz);
        let _e98 = sin(_e89);
        let _e99 = cos(_e89);
        let _e106 = (1f - _e99);
        phi_150_ = (((_e33 * _e99) + (cross(_e97, _e33) * _e98)) + ((_e97 * dot(_e97, _e33)) * _e106));
        phi_151_ = (((_e31 * _e99) + (cross(_e97, _e31) * _e98)) + ((_e97 * dot(_e97, _e31)) * _e106));
        phi_152_ = (((_e94 * _e99) + (cross(_e97, _e94) * _e98)) + ((_e97 * dot(_e97, _e94)) * _e106));
    }
    let _e126 = phi_150_;
    let _e128 = phi_151_;
    let _e130 = phi_152_;
    let _e136 = ((_e130 + vec3<f32>(_e37.Rect.x, _e37.Rect.y, (_e37.DepthMotion.x * 0.5f))) + _e87);
    let _e139 = VertexParams.ProjectionSkew;
    let _e144 = VertexParams.Camera;
    let _e147 = VertexParams.Zoom;
    let _e150 = VertexParams.TargetSize;
    let _e153 = ((((((_e136.xy + (_e139 * _e136.z)) - _e144) * _e147) / _e150) * 2f) - vec2<f32>(1f, 1f));
    phi_195_ = _e37.Flags;
    if (_e37.Flags.w > 0f) {
        phi_195_ = vec4<f32>((_e37.Flags.x * (0.72f + (sin((((_e45 * _e37.Flags.w) * 3f) + _e37.DepthMotion.z)) * 0.28f))), _e37.Flags.y, _e37.Flags.z, _e37.Flags.w);
    }
    let _e176 = phi_195_;
    out_u002e_var_u002e_TEXCOORD0_ = _e128;
    out_u002e_var_u002e_TEXCOORD1_ = _e126;
    out_u002e_var_u002e_TEXCOORD2_ = (_e37.Uv.xy + (_e32 * _e37.Uv.zw));
    out_u002e_var_u002e_TEXCOORD3_ = _e37.TintRoughness;
    out_u002e_var_u002e_TEXCOORD4_ = _e176;
    global_1 = vec4<f32>(_e153.x, -(_e153.y), 0f, 1f);
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
