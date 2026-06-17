struct type_u002e_FragmentUniformBlock {
    BlurParams: vec4<f32>,
    LightInfo: vec4<f32>,
    TexelSize: vec4<f32>,
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
var<private> out_u002e_var_u002e_SV_Target0_: vec4<f32>;

fn fragment_main_1() {
    var phi_86_: f32;
    var phi_265_: vec4<f32>;

    let _e24 = in_u002e_var_u002e_TEXCOORD0_1;
    let _e25 = in_u002e_var_u002e_TEXCOORD1_1;
    let _e26 = in_u002e_var_u002e_TEXCOORD4_1;
    switch bitcast<i32>(0u) {
        default: {
            if (_e26.x < -1000f) {
                discard;
            }
            let _e35 = textureSampleLevel(Texture, Sampler, vec2<f32>(_e24.x, (1f - _e24.y)), 0f);
            let _e39 = FragmentUniformBlock.BlurParams[3i];
            if (_e39 < 0.5f) {
                let _e42 = FragmentUniformBlock.TexelSize;
                let _e47 = FragmentUniformBlock.LightInfo;
                let _e52 = FragmentUniformBlock.LightInfo[2i];
                phi_86_ = clamp((distance((_e24 / max(_e42.xy, vec2<f32>(0.000001f, 0.000001f))), _e47.xy) / max(_e52, 1f)), 0f, 1f);
            } else {
                phi_86_ = 1f;
            }
            let _e57 = phi_86_;
            let _e60 = FragmentUniformBlock.LightInfo[3i];
            let _e64 = FragmentUniformBlock.BlurParams[2i];
            let _e65 = (max(_e57, _e60) * _e64);
            if (_e65 < 0.5f) {
                phi_265_ = (_e35 * _e25);
                break;
            }
            let _e68 = FragmentUniformBlock.BlurParams;
            let _e71 = FragmentUniformBlock.TexelSize;
            let _e73 = (_e68.xy * _e71.xy);
            let _e75 = max((_e65 * 0.5f), 0.0001f);
            let _e77 = ((2f * _e75) * _e75);
            let _e79 = (0.16666667f * _e65);
            let _e83 = exp((-((_e79 * _e79)) / _e77));
            let _e84 = (_e73 * _e79);
            let _e85 = (_e24 + _e84);
            let _e91 = textureSampleLevel(Texture, Sampler, vec2<f32>(_e85.x, (1f - _e85.y)), 0f);
            let _e93 = (_e24 - _e84);
            let _e99 = textureSampleLevel(Texture, Sampler, vec2<f32>(_e93.x, (1f - _e93.y)), 0f);
            let _e106 = (0.33333334f * _e65);
            let _e110 = exp((-((_e106 * _e106)) / _e77));
            let _e111 = (_e73 * _e106);
            let _e112 = (_e24 + _e111);
            let _e118 = textureSampleLevel(Texture, Sampler, vec2<f32>(_e112.x, (1f - _e112.y)), 0f);
            let _e120 = (_e24 - _e111);
            let _e126 = textureSampleLevel(Texture, Sampler, vec2<f32>(_e120.x, (1f - _e120.y)), 0f);
            let _e133 = (0.5f * _e65);
            let _e137 = exp((-((_e133 * _e133)) / _e77));
            let _e138 = (_e73 * _e133);
            let _e139 = (_e24 + _e138);
            let _e145 = textureSampleLevel(Texture, Sampler, vec2<f32>(_e139.x, (1f - _e139.y)), 0f);
            let _e147 = (_e24 - _e138);
            let _e153 = textureSampleLevel(Texture, Sampler, vec2<f32>(_e147.x, (1f - _e147.y)), 0f);
            let _e160 = (0.6666667f * _e65);
            let _e164 = exp((-((_e160 * _e160)) / _e77));
            let _e165 = (_e73 * _e160);
            let _e166 = (_e24 + _e165);
            let _e172 = textureSampleLevel(Texture, Sampler, vec2<f32>(_e166.x, (1f - _e166.y)), 0f);
            let _e174 = (_e24 - _e165);
            let _e180 = textureSampleLevel(Texture, Sampler, vec2<f32>(_e174.x, (1f - _e174.y)), 0f);
            let _e187 = (0.8333333f * _e65);
            let _e191 = exp((-((_e187 * _e187)) / _e77));
            let _e192 = (_e73 * _e187);
            let _e193 = (_e24 + _e192);
            let _e199 = textureSampleLevel(Texture, Sampler, vec2<f32>(_e193.x, (1f - _e193.y)), 0f);
            let _e201 = (_e24 - _e192);
            let _e207 = textureSampleLevel(Texture, Sampler, vec2<f32>(_e201.x, (1f - _e201.y)), 0f);
            let _e217 = exp((-((_e65 * _e65)) / _e77));
            let _e218 = (_e73 * _e65);
            let _e219 = (_e24 + _e218);
            let _e225 = textureSampleLevel(Texture, Sampler, vec2<f32>(_e219.x, (1f - _e219.y)), 0f);
            let _e227 = (_e24 - _e218);
            let _e233 = textureSampleLevel(Texture, Sampler, vec2<f32>(_e227.x, (1f - _e227.y)), 0f);
            let _e241 = (((((((_e35.xyz + ((_e91.xyz + _e99.xyz) * _e83)) + ((_e118.xyz + _e126.xyz) * _e110)) + ((_e145.xyz + _e153.xyz) * _e137)) + ((_e172.xyz + _e180.xyz) * _e164)) + ((_e199.xyz + _e207.xyz) * _e191)) + ((_e225.xyz + _e233.xyz) * _e217)) / vec3(((((((1f + (2f * _e83)) + (2f * _e110)) + (2f * _e137)) + (2f * _e164)) + (2f * _e191)) + (2f * _e217))));
            phi_265_ = (vec4<f32>(_e241.x, _e241.y, _e241.z, _e35.w) * _e25);
            break;
        }
    }
    let _e249 = phi_265_;
    out_u002e_var_u002e_SV_Target0_ = _e249;
    return;
}

@fragment 
fn fragment_main(@location(0) in_u002e_var_u002e_TEXCOORD0_: vec2<f32>, @location(1) in_u002e_var_u002e_TEXCOORD1_: vec4<f32>, @location(2) in_u002e_var_u002e_TEXCOORD4_: vec4<f32>) -> @location(0) vec4<f32> {
    in_u002e_var_u002e_TEXCOORD0_1 = in_u002e_var_u002e_TEXCOORD0_;
    in_u002e_var_u002e_TEXCOORD1_1 = in_u002e_var_u002e_TEXCOORD1_;
    in_u002e_var_u002e_TEXCOORD4_1 = in_u002e_var_u002e_TEXCOORD4_;
    fragment_main_1();
    let _e7 = out_u002e_var_u002e_SV_Target0_;
    return _e7;
}
