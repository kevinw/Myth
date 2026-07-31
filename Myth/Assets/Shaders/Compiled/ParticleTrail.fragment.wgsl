struct type_u002e_FragmentParams {
    TargetSize: vec2<f32>,
    LightingEnabled: f32,
    _fragmentPad0_: f32,
    AmbientLight: vec3<f32>,
    _fragmentPad1_: f32,
}

@group(2) @binding(0) 
var LightTexture: texture_2d<f32>;
@group(2) @binding(100) 
var LightSampler: sampler;
@group(3) @binding(0) 
var<uniform> FragmentParams: type_u002e_FragmentParams;
var<private> in_u002e_var_u002e_TEXCOORD0_1: vec4<f32>;
var<private> in_u002e_var_u002e_TEXCOORD1_1: vec2<f32>;
var<private> in_u002e_var_u002e_TEXCOORD2_1: f32;
var<private> global: vec4<f32>;
var<private> out_u002e_var_u002e_SV_Target0_: vec4<f32>;

fn fragment_main_1() {
    var phi_91_: vec4<f32>;

    let _e18 = in_u002e_var_u002e_TEXCOORD0_1;
    let _e19 = in_u002e_var_u002e_TEXCOORD1_1;
    let _e20 = in_u002e_var_u002e_TEXCOORD2_1;
    let _e21 = global;
    let _e28 = (_e18 * (1f - smoothstep(0.3f, 1f, abs(((_e19.y * 2f) - 1f)))));
    let _e30 = FragmentParams.LightingEnabled;
    let _e32 = clamp((_e20 * _e30), 0f, 1f);
    phi_91_ = _e28;
    if (_e32 > 0f) {
        let _e36 = FragmentParams.TargetSize;
        let _e38 = textureSample(LightTexture, LightSampler, (_e21.xy / _e36));
        let _e41 = FragmentParams.AmbientLight;
        let _e43 = max((_e38.xyz - _e41), vec3<f32>(0f, 0f, 0f));
        let _e49 = _e28.xyz;
        let _e53 = mix(_e49, ((_e49 * _e43) * 2f), vec3(_e32));
        let _e58 = vec4<f32>(_e53.x, _e53.y, _e53.z, _e28.w);
        phi_91_ = vec4<f32>(_e58.x, _e58.y, _e58.z, (_e28.w * mix(1f, smoothstep(0.02f, 0.45f, max(_e43.x, max(_e43.y, _e43.z))), _e32)));
    }
    let _e69 = phi_91_;
    out_u002e_var_u002e_SV_Target0_ = _e69;
    return;
}

@fragment 
fn fragment_main(@location(0) in_u002e_var_u002e_TEXCOORD0_: vec4<f32>, @location(1) in_u002e_var_u002e_TEXCOORD1_: vec2<f32>, @location(2) in_u002e_var_u002e_TEXCOORD2_: f32, @builtin(position) param: vec4<f32>) -> @location(0) vec4<f32> {
    in_u002e_var_u002e_TEXCOORD0_1 = in_u002e_var_u002e_TEXCOORD0_;
    in_u002e_var_u002e_TEXCOORD1_1 = in_u002e_var_u002e_TEXCOORD1_;
    in_u002e_var_u002e_TEXCOORD2_1 = in_u002e_var_u002e_TEXCOORD2_;
    global = param;
    fragment_main_1();
    let _e9 = out_u002e_var_u002e_SV_Target0_;
    return _e9;
}
