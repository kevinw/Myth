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
    var phi_83_: f32;
    var phi_249_: vec4<f32>;

    let _e24 = in_u002e_var_u002e_TEXCOORD0_1;
    let _e25 = in_u002e_var_u002e_TEXCOORD1_1;
    let _e26 = in_u002e_var_u002e_TEXCOORD4_1;
    switch bitcast<i32>(0u) {
        default: {
            if (_e26.x < -1000f) {
                discard;
            }
            let _e30 = textureSampleLevel(Texture, Sampler, _e24, 0f);
            let _e34 = FragmentUniformBlock.BlurParams[3i];
            if (_e34 < 0.5f) {
                let _e37 = FragmentUniformBlock.TexelSize;
                let _e42 = FragmentUniformBlock.LightInfo;
                let _e47 = FragmentUniformBlock.LightInfo[2i];
                phi_83_ = clamp((distance((_e24 / max(_e37.xy, vec2<f32>(0.000001f, 0.000001f))), _e42.xy) / max(_e47, 1f)), 0f, 1f);
            } else {
                phi_83_ = 1f;
            }
            let _e52 = phi_83_;
            let _e55 = FragmentUniformBlock.LightInfo[3i];
            let _e59 = FragmentUniformBlock.BlurParams[2i];
            let _e60 = (max(_e52, _e55) * _e59);
            if (_e60 < 0.5f) {
                phi_249_ = (_e30 * _e25);
                break;
            }
            let _e63 = FragmentUniformBlock.BlurParams;
            let _e66 = FragmentUniformBlock.TexelSize;
            let _e68 = (_e63.xy * _e66.xy);
            let _e69 = (_e60 * 0.5f);
            let _e70 = max(_e69, 0.0001f);
            let _e72 = ((2f * _e70) * _e70);
            let _e74 = (0.16666667f * _e60);
            let _e78 = exp((-((_e74 * _e74)) / _e72));
            let _e79 = (_e68 * _e74);
            let _e81 = textureSampleLevel(Texture, Sampler, (_e24 + _e79), 0f);
            let _e84 = textureSampleLevel(Texture, Sampler, (_e24 - _e79), 0f);
            let _e91 = (0.33333334f * _e60);
            let _e95 = exp((-((_e91 * _e91)) / _e72));
            let _e96 = (_e68 * _e91);
            let _e98 = textureSampleLevel(Texture, Sampler, (_e24 + _e96), 0f);
            let _e101 = textureSampleLevel(Texture, Sampler, (_e24 - _e96), 0f);
            let _e111 = exp((-((_e69 * _e69)) / _e72));
            let _e112 = (_e68 * _e69);
            let _e114 = textureSampleLevel(Texture, Sampler, (_e24 + _e112), 0f);
            let _e117 = textureSampleLevel(Texture, Sampler, (_e24 - _e112), 0f);
            let _e124 = (0.6666667f * _e60);
            let _e128 = exp((-((_e124 * _e124)) / _e72));
            let _e129 = (_e68 * _e124);
            let _e131 = textureSampleLevel(Texture, Sampler, (_e24 + _e129), 0f);
            let _e134 = textureSampleLevel(Texture, Sampler, (_e24 - _e129), 0f);
            let _e141 = (0.8333333f * _e60);
            let _e145 = exp((-((_e141 * _e141)) / _e72));
            let _e146 = (_e68 * _e141);
            let _e148 = textureSampleLevel(Texture, Sampler, (_e24 + _e146), 0f);
            let _e151 = textureSampleLevel(Texture, Sampler, (_e24 - _e146), 0f);
            let _e161 = exp((-((_e60 * _e60)) / _e72));
            let _e162 = (_e68 * _e60);
            let _e164 = textureSampleLevel(Texture, Sampler, (_e24 + _e162), 0f);
            let _e167 = textureSampleLevel(Texture, Sampler, (_e24 - _e162), 0f);
            let _e175 = (((((((_e30.xyz + ((_e81.xyz + _e84.xyz) * _e78)) + ((_e98.xyz + _e101.xyz) * _e95)) + ((_e114.xyz + _e117.xyz) * _e111)) + ((_e131.xyz + _e134.xyz) * _e128)) + ((_e148.xyz + _e151.xyz) * _e145)) + ((_e164.xyz + _e167.xyz) * _e161)) / vec3(((((((1f + (2f * _e78)) + (2f * _e95)) + (2f * _e111)) + (2f * _e128)) + (2f * _e145)) + (2f * _e161))));
            phi_249_ = (vec4<f32>(_e175.x, _e175.y, _e175.z, _e30.w) * _e25);
            break;
        }
    }
    let _e183 = phi_249_;
    out_u002e_var_u002e_SV_Target0_ = _e183;
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
