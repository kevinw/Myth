struct type_u002e_FragmentUniformBlock {
    Region: vec4<f32>,
    RTopLeft: vec4<f32>,
    P0_: vec4<f32>,
    P1_: vec4<f32>,
    B0_: vec4<f32>,
    B1_: vec4<f32>,
    A0_: vec4<f32>,
    A1_: vec4<f32>,
}

@group(3) @binding(0) 
var<uniform> FragmentUniformBlock: type_u002e_FragmentUniformBlock;
@group(2) @binding(0) 
var Texture: texture_2d<f32>;
@group(2) @binding(100) 
var Sampler: sampler;
var<private> in_u002e_var_u002e_TEXCOORD0_1: vec2<f32>;
var<private> in_u002e_var_u002e_TEXCOORD1_1: vec4<f32>;
var<private> in_u002e_var_u002e_TEXCOORD4_1: vec4<f32>;
var<private> global: vec4<f32>;
var<private> out_u002e_var_u002e_SV_Target0_: vec4<f32>;

fn fragment_main_1() {
    var local: array<i32, 16>;
    var phi_99_: vec2<f32>;
    var phi_118_: bool;
    var phi_124_: bool;
    var phi_131_: bool;
    var phi_142_: f32;
    var phi_155_: vec2<f32>;
    var phi_171_: bool;
    var phi_177_: bool;
    var phi_184_: bool;
    var phi_195_: f32;
    var phi_216_: bool;

    let _e46 = in_u002e_var_u002e_TEXCOORD0_1;
    let _e47 = in_u002e_var_u002e_TEXCOORD1_1;
    let _e48 = in_u002e_var_u002e_TEXCOORD4_1;
    let _e49 = global;
    if (_e48.x < -1000f) {
        discard;
    }
    let _e53 = FragmentUniformBlock.Region;
    let _e57 = FragmentUniformBlock.RTopLeft;
    let _e59 = ((_e46 * _e53.xy) + _e57.xy);
    let _e63 = FragmentUniformBlock.P1_[2i];
    phi_99_ = _e59;
    if (_e63 < 0f) {
        phi_99_ = vec2<f32>(-(_e59.x), _e59.y);
    }
    let _e71 = phi_99_;
    let _e73 = FragmentUniformBlock.P0_;
    let _e75 = FragmentUniformBlock.P1_;
    let _e77 = _e53.zw;
    let _e80 = FragmentUniformBlock.A0_[3i];
    switch bitcast<i32>(0u) {
        default: {
            let _e82 = (_e71 - _e73.xy);
            phi_118_ = true;
            if !((_e82.x < 0f)) {
                phi_118_ = (_e82.y < 0f);
            }
            let _e89 = phi_118_;
            phi_124_ = true;
            if !(_e89) {
                phi_124_ = (_e82.x >= _e73.z);
            }
            let _e94 = phi_124_;
            phi_131_ = true;
            if !(_e94) {
                phi_131_ = (_e82.y >= _e73.w);
            }
            let _e100 = phi_131_;
            if _e100 {
                phi_142_ = 9999f;
                break;
            }
            let _e103 = textureSampleLevel(Texture, Sampler, ((_e75.xy + _e82) / _e77), 0f);
            phi_142_ = (_e103.x * _e80);
            break;
        }
    }
    let _e107 = phi_142_;
    let _e111 = FragmentUniformBlock.B1_[2i];
    phi_155_ = _e59;
    if (_e111 < 0f) {
        let _e115 = FragmentUniformBlock.B1_[3i];
        phi_155_ = vec2<f32>(((2f * _e115) - _e59.x), _e59.y);
    }
    let _e123 = phi_155_;
    let _e125 = FragmentUniformBlock.B0_;
    let _e127 = FragmentUniformBlock.B1_;
    switch bitcast<i32>(0u) {
        default: {
            let _e130 = (_e123 - _e125.xy);
            phi_171_ = true;
            if !((_e130.x < 0f)) {
                phi_171_ = (_e130.y < 0f);
            }
            let _e137 = phi_171_;
            phi_177_ = true;
            if !(_e137) {
                phi_177_ = (_e130.x >= _e125.z);
            }
            let _e142 = phi_177_;
            phi_184_ = true;
            if !(_e142) {
                phi_184_ = (_e130.y >= _e125.w);
            }
            let _e148 = phi_184_;
            if _e148 {
                phi_195_ = 9999f;
                break;
            }
            let _e151 = textureSampleLevel(Texture, Sampler, ((_e127.xy + _e130) / _e77), 0f);
            phi_195_ = (_e151.x * _e80);
            break;
        }
    }
    let _e155 = phi_195_;
    let _e158 = FragmentUniformBlock.A1_[0i];
    let _e159 = max(_e158, 0.0001f);
    let _e164 = clamp((0.5f + ((0.5f * (_e155 - _e107)) / _e159)), 0f, 1f);
    let _e169 = (mix(_e155, _e107, _e164) - ((_e159 * _e164) * (1f - _e164)));
    phi_216_ = true;
    if !((_e169 < 0.5f)) {
        let _e174 = FragmentUniformBlock.A0_[1i];
        phi_216_ = (_e169 > _e174);
    }
    let _e177 = phi_216_;
    if _e177 {
        discard;
    }
    let _e180 = FragmentUniformBlock.A0_[1i];
    let _e186 = FragmentUniformBlock.A1_[1i];
    let _e192 = FragmentUniformBlock.A1_[2i];
    let _e195 = (_e57.zw + (_e49.xy / vec2(_e192)));
    local = array<i32, 16>(0i, 8i, 2i, 10i, 12i, 4i, 14i, 6i, 3i, 11i, 1i, 9i, 15i, 7i, 13i, 5i);
    let _e207 = local[(((i32(floor(_e195.y)) & 3i) * 4i) + (i32(floor(_e195.x)) & 3i))];
    if ((clamp(((_e180 - _e169) * 0.33333334f), 0f, 1f) * _e186) < ((f32(_e207) + 0.5f) * 0.0625f)) {
        discard;
    }
    let _e214 = FragmentUniformBlock.A0_[0i];
    let _e218 = FragmentUniformBlock.A0_[2i];
    let _e231 = (clamp((abs(((fract((vec3(fract((floor(((_e169 - _e214) / _e218)) * 0.16666667f))) + vec3<f32>(0f, 0.6666667f, 0.33333334f))) * 6f) - vec3<f32>(3f, 3f, 3f))) - vec3<f32>(1f, 1f, 1f)), vec3<f32>(0f, 0f, 0f), vec3<f32>(1f, 1f, 1f)) * 1f);
    out_u002e_var_u002e_SV_Target0_ = (vec4<f32>(_e231.x, _e231.y, _e231.z, 1f) * _e47);
    return;
}

@fragment 
fn fragment_main(@location(0) in_u002e_var_u002e_TEXCOORD0_: vec2<f32>, @location(1) in_u002e_var_u002e_TEXCOORD1_: vec4<f32>, @location(2) in_u002e_var_u002e_TEXCOORD4_: vec4<f32>, @builtin(position) param: vec4<f32>) -> @location(0) vec4<f32> {
    in_u002e_var_u002e_TEXCOORD0_1 = in_u002e_var_u002e_TEXCOORD0_;
    in_u002e_var_u002e_TEXCOORD1_1 = in_u002e_var_u002e_TEXCOORD1_;
    in_u002e_var_u002e_TEXCOORD4_1 = in_u002e_var_u002e_TEXCOORD4_;
    global = param;
    fragment_main_1();
    let _e9 = out_u002e_var_u002e_SV_Target0_;
    return _e9;
}
