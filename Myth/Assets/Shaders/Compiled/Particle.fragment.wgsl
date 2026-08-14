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
var<private> in_u002e_var_u002e_TEXCOORD3_1: f32;
var<private> global: vec4<f32>;
var<private> out_u002e_var_u002e_SV_Target0_: vec4<f32>;

fn fragment_main_1() {
    var phi_77_: vec4<f32>;
    var phi_114_: vec4<f32>;

    let _e24 = in_u002e_var_u002e_TEXCOORD0_1;
    let _e25 = in_u002e_var_u002e_TEXCOORD1_1;
    let _e26 = in_u002e_var_u002e_TEXCOORD2_1;
    let _e27 = in_u002e_var_u002e_TEXCOORD3_1;
    let _e28 = global;
    phi_77_ = _e24;
    if (_e26 > 0.5f) {
        let _e32 = (length((_e25 - vec2<f32>(0.5f, 0.5f))) * 2f);
        let _e36 = (1f - smoothstep(0f, 0.38f, _e32));
        let _e40 = mix(_e24.xyz, vec3<f32>(1f, 1f, 1f), vec3((_e36 * 0.85f)));
        phi_77_ = vec4<f32>(_e40.x, _e40.y, _e40.z, (_e24.w * max(((1f - smoothstep(0.35f, 1f, _e32)) * 0.45f), _e36)));
    }
    let _e50 = phi_77_;
    let _e52 = FragmentParams.LightingEnabled;
    let _e54 = clamp((_e27 * _e52), 0f, 1f);
    phi_114_ = _e50;
    if (_e54 > 0f) {
        let _e58 = FragmentParams.TargetSize;
        let _e60 = textureSampleLevel(LightTexture, LightSampler, (_e28.xy / _e58), 0f);
        let _e63 = FragmentParams.AmbientLight;
        let _e65 = max((_e60.xyz - _e63), vec3<f32>(0f, 0f, 0f));
        let _e71 = _e50.xyz;
        let _e75 = mix(_e71, ((_e71 * _e65) * 2f), vec3(_e54));
        let _e80 = vec4<f32>(_e75.x, _e75.y, _e75.z, _e50.w);
        phi_114_ = vec4<f32>(_e80.x, _e80.y, _e80.z, (_e50.w * mix(1f, smoothstep(0.02f, 0.45f, max(_e65.x, max(_e65.y, _e65.z))), _e54)));
    }
    let _e91 = phi_114_;
    out_u002e_var_u002e_SV_Target0_ = _e91;
    return;
}

@fragment 
fn fragment_main(@location(0) in_u002e_var_u002e_TEXCOORD0_: vec4<f32>, @location(1) in_u002e_var_u002e_TEXCOORD1_: vec2<f32>, @location(2) in_u002e_var_u002e_TEXCOORD2_: f32, @location(3) in_u002e_var_u002e_TEXCOORD3_: f32, @builtin(position) param: vec4<f32>) -> @location(0) vec4<f32> {
    in_u002e_var_u002e_TEXCOORD0_1 = in_u002e_var_u002e_TEXCOORD0_;
    in_u002e_var_u002e_TEXCOORD1_1 = in_u002e_var_u002e_TEXCOORD1_;
    in_u002e_var_u002e_TEXCOORD2_1 = in_u002e_var_u002e_TEXCOORD2_;
    in_u002e_var_u002e_TEXCOORD3_1 = in_u002e_var_u002e_TEXCOORD3_;
    global = param;
    fragment_main_1();
    let _e11 = out_u002e_var_u002e_SV_Target0_;
    return _e11;
}
