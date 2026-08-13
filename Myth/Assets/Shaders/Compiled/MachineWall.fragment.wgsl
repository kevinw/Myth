struct type_u002e_FragmentParams {
    LightSpecular: vec4<f32>,
    ViewNormalPass: vec4<f32>,
    NormalSettings: vec4<f32>,
    ActorFieldSettings: vec4<f32>,
}

@group(2) @binding(0)
var PatternTexture: texture_2d<f32>;
@group(2) @binding(100)
var PatternSampler: sampler;
@group(2) @binding(1)
var DetailTexture: texture_2d<f32>;
@group(2) @binding(101)
var DetailSampler: sampler;
@group(3) @binding(0)
var<uniform> FragmentParams: type_u002e_FragmentParams;
var<private> in_u002e_var_u002e_TEXCOORD0_1: vec3<f32>;
var<private> in_u002e_var_u002e_TEXCOORD1_1: vec3<f32>;
var<private> in_u002e_var_u002e_TEXCOORD2_1: vec2<f32>;
var<private> in_u002e_var_u002e_TEXCOORD3_1: vec4<f32>;
var<private> in_u002e_var_u002e_TEXCOORD4_1: vec4<f32>;
var<private> out_u002e_var_u002e_SV_Target0_: vec4<f32>;

fn fragment_main_1() {
    var phi_245_: vec4<f32>;

    let _e47 = in_u002e_var_u002e_TEXCOORD0_1;
    let _e48 = in_u002e_var_u002e_TEXCOORD1_1;
    let _e49 = in_u002e_var_u002e_TEXCOORD2_1;
    let _e50 = in_u002e_var_u002e_TEXCOORD3_1;
    let _e51 = in_u002e_var_u002e_TEXCOORD4_1;
    switch bitcast<i32>(0u) {
        default: {
            let _e53 = normalize(_e47);
            let _e55 = FragmentParams.ViewNormalPass;
            let _e57 = normalize(_e55.xyz);
            if (dot(_e53, _e57) <= 0.001f) {
                discard;
            }
            let _e60 = normalize(_e48);
            let _e63 = textureSample(PatternTexture, PatternSampler, _e49);
            let _e66 = FragmentParams.NormalSettings[2i];
            let _e68 = textureSample(DetailTexture, DetailSampler, (_e49 * _e66));
            let _e71 = ((_e63.yzw * 2f) - vec3<f32>(1f, 1f, 1f));
            let _e74 = ((_e68.yzw * 2f) - vec3<f32>(1f, 1f, 1f));
            let _e75 = _e71.xy;
            let _e78 = FragmentParams.NormalSettings[0i];
            let _e80 = _e74.xy;
            let _e83 = FragmentParams.NormalSettings[1i];
            let _e88 = (((_e75 * _e78) + (_e80 * _e83)) * (1f - _e51.y));
            let _e96 = normalize(vec3<f32>(_e88.x, _e88.y, max((_e71.z * _e74.z), 0.2f)));
            let _e105 = normalize((((_e60 * _e96.x) + (normalize(cross(_e53, _e60)) * _e96.y)) + (_e53 * _e96.z)));
            let _e108 = FragmentParams.ViewNormalPass[3i];
            if (_e108 > 2.5f) {
                let _e125 = FragmentParams.ActorFieldSettings[2i];
                let _e137 = FragmentParams.ActorFieldSettings[0i];
                let _e143 = FragmentParams.ActorFieldSettings[1i];
                phi_245_ = vec4<f32>((((_e137 * 32f) + (floor(((clamp(_e51.z, 0f, 1f) * 255f) + 0.5f)) % 30f)) * 0.003921569f), (((_e143 * 8f) + floor(((clamp((0.5f + (((clamp((((length(_e75) * 0.55f) + (length(_e80) * 0.3f)) + (abs((_e63.x - _e68.x)) * 0.3f)), 0f, 1f) - 0.5f) * _e125) * 0.7f)), 0f, 1f) * 7f) + 0.5f))) * 0.003921569f), 0f, 1f);
                break;
            }
            if (_e108 > 0.5f) {
                if (_e108 > 1.5f) {
                    let _e156 = (_e50.xyz * _e51.x);
                    phi_245_ = vec4<f32>(_e156.x, _e156.y, _e156.z, clamp(_e51.x, 0f, 1f));
                    break;
                }
                let _e163 = ((_e105 * 0.5f) + vec3<f32>(0.5f, 0.5f, 0.5f));
                phi_245_ = vec4<f32>(_e163.x, _e163.y, _e163.z, 1f);
                break;
            }
            let _e171 = mix(_e68.x, 0.5f, _e51.y);
            let _e176 = clamp((_e50.w + ((_e171 - 0.5f) * 0.16f)), 0f, 1f);
            let _e178 = FragmentParams.LightSpecular;
            let _e180 = normalize(_e178.xyz);
            let _e193 = FragmentParams.LightSpecular[3i];
            let _e207 = clamp((((_e50.xyz * mix(0.76f, 1.12f, ((mix(_e63.x, 0.72f, _e51.y) * 0.68f) + (_e171 * 0.32f)))) * (0.48f + (clamp(dot(_e105, _e180), 0f, 1f) * 0.52f))) + vec3((pow(clamp(dot(_e105, normalize((_e180 + _e57))), 0f, 1f), mix(72f, 18f, _e176)) * (_e193 * (1f - (_e176 * 0.65f)))))), vec3<f32>(0f, 0f, 0f), vec3<f32>(1f, 1f, 1f));
            phi_245_ = vec4<f32>(_e207.x, _e207.y, _e207.z, 1f);
            break;
        }
    }
    let _e213 = phi_245_;
    out_u002e_var_u002e_SV_Target0_ = _e213;
    return;
}

@fragment
fn fragment_main(@location(0) in_u002e_var_u002e_TEXCOORD0_: vec3<f32>, @location(1) in_u002e_var_u002e_TEXCOORD1_: vec3<f32>, @location(2) in_u002e_var_u002e_TEXCOORD2_: vec2<f32>, @location(3) in_u002e_var_u002e_TEXCOORD3_: vec4<f32>, @location(4) in_u002e_var_u002e_TEXCOORD4_: vec4<f32>) -> @location(0) vec4<f32> {
    in_u002e_var_u002e_TEXCOORD0_1 = in_u002e_var_u002e_TEXCOORD0_;
    in_u002e_var_u002e_TEXCOORD1_1 = in_u002e_var_u002e_TEXCOORD1_;
    in_u002e_var_u002e_TEXCOORD2_1 = in_u002e_var_u002e_TEXCOORD2_;
    in_u002e_var_u002e_TEXCOORD3_1 = in_u002e_var_u002e_TEXCOORD3_;
    in_u002e_var_u002e_TEXCOORD4_1 = in_u002e_var_u002e_TEXCOORD4_;
    fragment_main_1();
    let _e11 = out_u002e_var_u002e_SV_Target0_;
    return _e11;
}
