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
    var phi_226_: vec4<f32>;

    let _e50 = in_u002e_var_u002e_TEXCOORD0_1;
    let _e51 = in_u002e_var_u002e_TEXCOORD1_1;
    let _e52 = in_u002e_var_u002e_TEXCOORD2_1;
    let _e53 = in_u002e_var_u002e_TEXCOORD3_1;
    let _e54 = in_u002e_var_u002e_TEXCOORD4_1;
    switch bitcast<i32>(0u) {
        default: {
            let _e56 = normalize(_e50);
            let _e58 = FragmentParams.ViewNormalPass;
            let _e60 = normalize(_e58.xyz);
            if (dot(_e56, _e60) <= 0.001f) {
                discard;
            }
            let _e66 = FragmentParams.NormalSettings[1i];
            let _e67 = ((_e52 * vec2<f32>(1.4f, 3f)) * _e66);
            let _e68 = textureSample(DetailTexture, DetailSampler, _e67);
            let _e70 = textureSample(DetailNormalTexture, DetailNormalSampler, _e67);
            let _e73 = ((_e70.xyz * 2f) - vec3<f32>(1f, 1f, 1f));
            let _e74 = normalize(_e51);
            let _e81 = FragmentParams.NormalSettings[0i];
            let _e91 = normalize(((((_e74 * _e73.x) * _e81) + ((normalize(cross(_e56, _e74)) * _e73.y) * _e81)) + (_e56 * max(_e73.z, 0.3f))));
            let _e94 = FragmentParams.ViewNormalPass[3i];
            if (_e94 > 2.5f) {
                let _e120 = FragmentParams.ActorFieldSettings[2i];
                let _e126 = FragmentParams.ActorFieldSettings[0i];
                let _e130 = FragmentParams.ActorFieldSettings[1i];
                phi_226_ = vec4<f32>(_e54, (((_e126 * 32f) + _e130) * 0.003921569f), (0.5f + (((clamp(((((length(_e73.xy) * 0.55f) + (pow(abs(sin((_e52.x * 15.707963f))), 20f) * 0.3f)) + (abs(cos((_e52.y * 25.132742f))) * 0.1f)) + (abs((_e68.x - 0.5f)) * 0.25f)), 0f, 1f) - 0.5f) * _e120) * 0.7f)), 1f);
                break;
            }
            if (_e94 > 0.5f) {
                let _e136 = ((_e91 * 0.5f) + vec3<f32>(0.5f, 0.5f, 0.5f));
                phi_226_ = vec4<f32>(_e136.x, _e136.y, _e136.z, 1f);
                break;
            }
            let _e145 = clamp((_e53.w + ((_e68.x - 0.5f) * 0.12f)), 0f, 1f);
            let _e147 = FragmentParams.LightSpecular;
            let _e149 = normalize(_e147.xyz);
            let _e162 = FragmentParams.LightSpecular[3i];
            let _e188 = clamp((((_e53.xyz * (((0.94f + (pow(abs(sin((_e52.x * 15.707963f))), 20f) * 0.08f)) * (0.97f + (cos((_e52.y * 25.132742f)) * 0.03f))) * mix(0.88f, 1.1f, _e68.x))) * (0.42f + (clamp(dot(_e91, _e149), 0f, 1f) * 0.58f))) + vec3((pow(clamp(dot(_e91, normalize((_e149 + _e60))), 0f, 1f), mix(88f, 24f, _e145)) * ((_e162 * 1.25f) * (1f - (_e145 * 0.55f)))))), vec3<f32>(0f, 0f, 0f), vec3<f32>(1f, 1f, 1f));
            phi_226_ = vec4<f32>(_e188.x, _e188.y, _e188.z, 1f);
            break;
        }
    }
    let _e194 = phi_226_;
    out_u002e_var_u002e_SV_Target0_ = _e194;
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
