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
@group(2) @binding(1)
var DetailNormalTexture: texture_2d<f32>;
@group(2) @binding(101)
var DetailNormalSampler: sampler;
@group(3) @binding(0)
var<uniform> FragmentParams: type_u002e_FragmentParams;
var<private> in_u002e_var_u002e_TEXCOORD0_1: vec3<f32>;
var<private> in_u002e_var_u002e_TEXCOORD1_1: vec3<f32>;
var<private> in_u002e_var_u002e_TEXCOORD2_1: vec2<f32>;
var<private> in_u002e_var_u002e_TEXCOORD3_1: vec4<f32>;
var<private> in_u002e_var_u002e_TEXCOORD4_1: f32;
var<private> out_u002e_var_u002e_SV_Target0_: vec4<f32>;

fn fragment_main_1() {
    var phi_242_: vec4<f32>;

    let _e54 = in_u002e_var_u002e_TEXCOORD0_1;
    let _e55 = in_u002e_var_u002e_TEXCOORD1_1;
    let _e56 = in_u002e_var_u002e_TEXCOORD2_1;
    let _e57 = in_u002e_var_u002e_TEXCOORD3_1;
    let _e58 = in_u002e_var_u002e_TEXCOORD4_1;
    switch bitcast<i32>(0u) {
        default: {
            let _e60 = normalize(_e54);
            let _e62 = FragmentParams.ViewNormalPass;
            let _e64 = normalize(_e62.xyz);
            if (dot(_e60, _e64) <= 0.001f) {
                discard;
            }
            let _e70 = FragmentParams.NormalSettings[1i];
            let _e71 = ((_e56 * vec2<f32>(1.4f, 3f)) * _e70);
            let _e72 = textureSample(DetailTexture, DetailSampler, _e71);
            let _e74 = textureSample(DetailNormalTexture, DetailNormalSampler, _e71);
            let _e77 = ((_e74.xyz * 2f) - vec3<f32>(1f, 1f, 1f));
            let _e78 = normalize(_e55);
            let _e85 = FragmentParams.NormalSettings[0i];
            let _e95 = normalize(((((_e78 * _e77.x) * _e85) + ((normalize(cross(_e60, _e78)) * _e77.y) * _e85)) + (_e60 * max(_e77.z, 0.3f))));
            let _e98 = FragmentParams.ViewNormalPass[3i];
            if (_e98 > 2.5f) {
                let _e124 = FragmentParams.ActorFieldSettings[2i];
                let _e135 = FragmentParams.ActorFieldSettings[0i];
                let _e141 = FragmentParams.ActorFieldSettings[1i];
                phi_242_ = vec4<f32>((((_e135 * 32f) + (floor(((clamp(_e58, 0f, 1f) * 255f) + 0.5f)) % 30f)) * 0.003921569f), (((_e141 * 8f) + floor(((clamp((0.5f + (((clamp(((((length(_e77.xy) * 0.55f) + (pow(abs(sin((_e56.x * 15.707963f))), 20f) * 0.3f)) + (abs(cos((_e56.y * 25.132742f))) * 0.1f)) + (abs((_e72.x - 0.5f)) * 0.25f)), 0f, 1f) - 0.5f) * _e124) * 0.7f)), 0f, 1f) * 7f) + 0.5f))) * 0.003921569f), 0f, 1f);
                break;
            }
            if (_e98 > 0.5f) {
                let _e152 = ((_e95 * 0.5f) + vec3<f32>(0.5f, 0.5f, 0.5f));
                phi_242_ = vec4<f32>(_e152.x, _e152.y, _e152.z, 1f);
                break;
            }
            let _e161 = clamp((_e57.w + ((_e72.x - 0.5f) * 0.12f)), 0f, 1f);
            let _e163 = FragmentParams.LightSpecular;
            let _e165 = normalize(_e163.xyz);
            let _e178 = FragmentParams.LightSpecular[3i];
            let _e204 = clamp((((_e57.xyz * (((0.94f + (pow(abs(sin((_e56.x * 15.707963f))), 20f) * 0.08f)) * (0.97f + (cos((_e56.y * 25.132742f)) * 0.03f))) * mix(0.88f, 1.1f, _e72.x))) * (0.42f + (clamp(dot(_e95, _e165), 0f, 1f) * 0.58f))) + vec3((pow(clamp(dot(_e95, normalize((_e165 + _e64))), 0f, 1f), mix(88f, 24f, _e161)) * ((_e178 * 1.25f) * (1f - (_e161 * 0.55f)))))), vec3<f32>(0f, 0f, 0f), vec3<f32>(1f, 1f, 1f));
            phi_242_ = vec4<f32>(_e204.x, _e204.y, _e204.z, 1f);
            break;
        }
    }
    let _e210 = phi_242_;
    out_u002e_var_u002e_SV_Target0_ = _e210;
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
