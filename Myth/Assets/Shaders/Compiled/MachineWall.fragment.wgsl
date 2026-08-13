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
@group(2) @binding(2)
var PatternNormalTexture: texture_2d<f32>;
@group(2) @binding(102)
var PatternNormalSampler: sampler;
@group(2) @binding(3)
var DetailNormalTexture: texture_2d<f32>;
@group(2) @binding(103)
var DetailNormalSampler: sampler;
@group(3) @binding(0)
var<uniform> FragmentParams: type_u002e_FragmentParams;
var<private> in_u002e_var_u002e_TEXCOORD0_1: vec3<f32>;
var<private> in_u002e_var_u002e_TEXCOORD1_1: vec3<f32>;
var<private> in_u002e_var_u002e_TEXCOORD2_1: vec2<f32>;
var<private> in_u002e_var_u002e_TEXCOORD3_1: vec4<f32>;
var<private> in_u002e_var_u002e_TEXCOORD4_1: vec4<f32>;
var<private> out_u002e_var_u002e_SV_Target0_: vec4<f32>;

fn fragment_main_1() {
    var phi_265_: vec4<f32>;

    let _e51 = in_u002e_var_u002e_TEXCOORD0_1;
    let _e52 = in_u002e_var_u002e_TEXCOORD1_1;
    let _e53 = in_u002e_var_u002e_TEXCOORD2_1;
    let _e54 = in_u002e_var_u002e_TEXCOORD3_1;
    let _e55 = in_u002e_var_u002e_TEXCOORD4_1;
    switch bitcast<i32>(0u) {
        default: {
            let _e57 = normalize(_e51);
            let _e59 = FragmentParams.ViewNormalPass;
            let _e61 = normalize(_e59.xyz);
            if (dot(_e57, _e61) <= 0.001f) {
                discard;
            }
            let _e64 = normalize(_e52);
            let _e67 = textureSample(PatternNormalTexture, PatternNormalSampler, _e53);
            let _e70 = ((_e67.xyz * 2f) - vec3<f32>(1f, 1f, 1f));
            let _e73 = FragmentParams.NormalSettings[2i];
            let _e74 = (_e53 * _e73);
            let _e75 = textureSample(DetailNormalTexture, DetailNormalSampler, _e74);
            let _e78 = ((_e75.xyz * 2f) - vec3<f32>(1f, 1f, 1f));
            let _e79 = _e70.xy;
            let _e82 = FragmentParams.NormalSettings[0i];
            let _e84 = _e78.xy;
            let _e87 = FragmentParams.NormalSettings[1i];
            let _e92 = (((_e79 * _e82) + (_e84 * _e87)) * (1f - _e55.y));
            let _e100 = normalize(vec3<f32>(_e92.x, _e92.y, max((_e70.z * _e78.z), 0.2f)));
            let _e109 = normalize((((_e64 * _e100.x) + (normalize(cross(_e57, _e64)) * _e100.y)) + (_e57 * _e100.z)));
            let _e112 = FragmentParams.ViewNormalPass[3i];
            if (_e112 > 2.5f) {
                let _e114 = textureSample(PatternTexture, PatternSampler, _e53);
                let _e116 = textureSample(DetailTexture, DetailSampler, _e74);
                let _e131 = FragmentParams.ActorFieldSettings[2i];
                let _e143 = FragmentParams.ActorFieldSettings[0i];
                let _e149 = FragmentParams.ActorFieldSettings[1i];
                phi_265_ = vec4<f32>((((_e143 * 32f) + (floor(((clamp(_e55.z, 0f, 1f) * 255f) + 0.5f)) % 30f)) * 0.003921569f), (((_e149 * 8f) + floor(((clamp((0.5f + (((clamp((((length(_e79) * 0.55f) + (length(_e84) * 0.3f)) + (abs((_e114.x - _e116.x)) * 0.3f)), 0f, 1f) - 0.5f) * _e131) * 0.7f)), 0f, 1f) * 7f) + 0.5f))) * 0.003921569f), 0f, 1f);
                break;
            }
            if (_e112 > 0.5f) {
                if (_e112 > 1.5f) {
                    let _e162 = (_e54.xyz * _e55.x);
                    phi_265_ = vec4<f32>(_e162.x, _e162.y, _e162.z, clamp(_e55.x, 0f, 1f));
                    break;
                }
                let _e169 = ((_e109 * 0.5f) + vec3<f32>(0.5f, 0.5f, 0.5f));
                phi_265_ = vec4<f32>(_e169.x, _e169.y, _e169.z, 1f);
                break;
            }
            let _e174 = textureSample(PatternTexture, PatternSampler, _e53);
            let _e176 = textureSample(DetailTexture, DetailSampler, _e74);
            let _e179 = mix(_e176.x, 0.5f, _e55.y);
            let _e184 = clamp((_e54.w + ((_e179 - 0.5f) * 0.16f)), 0f, 1f);
            let _e186 = FragmentParams.LightSpecular;
            let _e188 = normalize(_e186.xyz);
            let _e201 = FragmentParams.LightSpecular[3i];
            let _e215 = clamp((((_e54.xyz * mix(0.76f, 1.12f, ((mix(_e174.x, 0.72f, _e55.y) * 0.68f) + (_e179 * 0.32f)))) * (0.48f + (clamp(dot(_e109, _e188), 0f, 1f) * 0.52f))) + vec3((pow(clamp(dot(_e109, normalize((_e188 + _e61))), 0f, 1f), mix(72f, 18f, _e184)) * (_e201 * (1f - (_e184 * 0.65f)))))), vec3<f32>(0f, 0f, 0f), vec3<f32>(1f, 1f, 1f));
            phi_265_ = vec4<f32>(_e215.x, _e215.y, _e215.z, 1f);
            break;
        }
    }
    let _e221 = phi_265_;
    out_u002e_var_u002e_SV_Target0_ = _e221;
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
