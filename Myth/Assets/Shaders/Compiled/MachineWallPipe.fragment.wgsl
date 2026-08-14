struct type_u002e_FragmentParams {
    LightSpecular: vec4<f32>,
    ViewNormalPass: vec4<f32>,
    NormalSettings: vec4<f32>,
    ActorFieldSettings: vec4<f32>,
}

@group(2) @binding(0) 
var DetailTexture: texture_2d<f32>;
@group(2) @binding(100) 
var DetailSampler: sampler;
@group(3) @binding(0) 
var<uniform> FragmentParams: type_u002e_FragmentParams;
var<private> in_u002e_var_u002e_TEXCOORD0_1: vec3<f32>;
var<private> in_u002e_var_u002e_TEXCOORD1_1: vec3<f32>;
var<private> in_u002e_var_u002e_TEXCOORD2_1: vec2<f32>;
var<private> in_u002e_var_u002e_TEXCOORD3_1: vec4<f32>;
var<private> in_u002e_var_u002e_TEXCOORD4_1: f32;
var<private> out_u002e_var_u002e_SV_Target0_: vec4<f32>;

fn fragment_main_1() {
    var phi_236_: vec4<f32>;

    let _e52 = in_u002e_var_u002e_TEXCOORD0_1;
    let _e53 = in_u002e_var_u002e_TEXCOORD1_1;
    let _e54 = in_u002e_var_u002e_TEXCOORD2_1;
    let _e55 = in_u002e_var_u002e_TEXCOORD3_1;
    let _e56 = in_u002e_var_u002e_TEXCOORD4_1;
    switch bitcast<i32>(0u) {
        default: {
            let _e58 = normalize(_e52);
            let _e60 = FragmentParams.ViewNormalPass;
            let _e62 = normalize(_e60.xyz);
            if (dot(_e58, _e62) <= 0.001f) {
                discard;
            }
            let _e68 = FragmentParams.NormalSettings[1i];
            let _e70 = textureSample(DetailTexture, DetailSampler, ((_e54 * vec2<f32>(1.4f, 3f)) * _e68));
            let _e74 = ((_e70.yzw * 2f) - vec3<f32>(1f, 1f, 1f));
            let _e75 = normalize(_e53);
            let _e82 = FragmentParams.NormalSettings[0i];
            let _e92 = normalize(((((_e75 * _e74.x) * _e82) + ((normalize(cross(_e58, _e75)) * _e74.y) * _e82)) + (_e58 * max(_e74.z, 0.3f))));
            let _e95 = FragmentParams.ViewNormalPass[3i];
            if (_e95 > 2.5f) {
                let _e121 = FragmentParams.ActorFieldSettings[2i];
                let _e132 = FragmentParams.ActorFieldSettings[0i];
                let _e138 = FragmentParams.ActorFieldSettings[1i];
                phi_236_ = vec4<f32>((((_e132 * 32f) + (floor(((clamp(_e56, 0f, 1f) * 255f) + 0.5f)) % 30f)) * 0.003921569f), (((_e138 * 8f) + floor(((clamp((0.5f + (((clamp(((((length(_e74.xy) * 0.55f) + (pow(abs(sin((_e54.x * 15.707963f))), 20f) * 0.3f)) + (abs(cos((_e54.y * 25.132742f))) * 0.1f)) + (abs((_e70.x - 0.5f)) * 0.25f)), 0f, 1f) - 0.5f) * _e121) * 0.7f)), 0f, 1f) * 7f) + 0.5f))) * 0.003921569f), 0f, 1f);
                break;
            }
            if (_e95 > 0.5f) {
                let _e149 = ((_e92 * 0.5f) + vec3<f32>(0.5f, 0.5f, 0.5f));
                phi_236_ = vec4<f32>(_e149.x, _e149.y, _e149.z, 1f);
                break;
            }
            let _e158 = clamp((_e55.w + ((_e70.x - 0.5f) * 0.12f)), 0f, 1f);
            let _e160 = FragmentParams.LightSpecular;
            let _e162 = normalize(_e160.xyz);
            let _e175 = FragmentParams.LightSpecular[3i];
            let _e201 = clamp((((_e55.xyz * (((0.94f + (pow(abs(sin((_e54.x * 15.707963f))), 20f) * 0.08f)) * (0.97f + (cos((_e54.y * 25.132742f)) * 0.03f))) * mix(0.88f, 1.1f, _e70.x))) * (0.42f + (clamp(dot(_e92, _e162), 0f, 1f) * 0.58f))) + vec3((pow(clamp(dot(_e92, normalize((_e162 + _e62))), 0f, 1f), mix(88f, 24f, _e158)) * ((_e175 * 1.25f) * (1f - (_e158 * 0.55f)))))), vec3<f32>(0f, 0f, 0f), vec3<f32>(1f, 1f, 1f));
            phi_236_ = vec4<f32>(_e201.x, _e201.y, _e201.z, 1f);
            break;
        }
    }
    let _e207 = phi_236_;
    out_u002e_var_u002e_SV_Target0_ = _e207;
    return;
}

@fragment 
fn fragment_main(@location(0) in_u002e_var_u002e_TEXCOORD0_: vec3<f32>, @location(1) in_u002e_var_u002e_TEXCOORD1_: vec3<f32>, @location(2) in_u002e_var_u002e_TEXCOORD2_: vec2<f32>, @location(3) in_u002e_var_u002e_TEXCOORD3_: vec4<f32>, @location(4) in_u002e_var_u002e_TEXCOORD4_: f32) -> @location(0) vec4<f32> {
    in_u002e_var_u002e_TEXCOORD0_1 = in_u002e_var_u002e_TEXCOORD0_;
    in_u002e_var_u002e_TEXCOORD1_1 = in_u002e_var_u002e_TEXCOORD1_;
    in_u002e_var_u002e_TEXCOORD2_1 = in_u002e_var_u002e_TEXCOORD2_;
    in_u002e_var_u002e_TEXCOORD3_1 = in_u002e_var_u002e_TEXCOORD3_;
    in_u002e_var_u002e_TEXCOORD4_1 = in_u002e_var_u002e_TEXCOORD4_;
    fragment_main_1();
    let _e11 = out_u002e_var_u002e_SV_Target0_;
    return _e11;
}
