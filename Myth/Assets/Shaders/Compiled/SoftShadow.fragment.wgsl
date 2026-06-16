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
    var phi_288_: vec4<f32>;

    let _e24 = in_u002e_var_u002e_TEXCOORD0_1;
    let _e25 = in_u002e_var_u002e_TEXCOORD1_1;
    let _e26 = in_u002e_var_u002e_TEXCOORD4_1;
    switch bitcast<i32>(0u) {
        default: {
            if (_e26.x < -1000f) {
                discard;
            }
            let _e35 = textureSample(Texture, Sampler, vec2<f32>(_e24.x, (1f - _e24.y)));
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
                phi_288_ = (_e35 * _e25);
                break;
            }
            let _e68 = FragmentUniformBlock.BlurParams;
            let _e71 = FragmentUniformBlock.TexelSize;
            let _e73 = (_e68.xy * _e71.xy);
            let _e74 = (_e65 * 0.5f);
            let _e75 = max(_e74, 0.0001f);
            let _e77 = ((2f * _e75) * _e75);
            let _e79 = (0.16666667f * _e65);
            let _e83 = exp((-((_e79 * _e79)) / _e77));
            let _e84 = (_e73 * _e79);
            let _e85 = (_e24 + _e84);
            let _e91 = textureSample(Texture, Sampler, vec2<f32>(_e85.x, (1f - _e85.y)));
            let _e93 = (_e24 - _e84);
            let _e99 = textureSample(Texture, Sampler, vec2<f32>(_e93.x, (1f - _e93.y)));
            let _e106 = (0.33333334f * _e65);
            let _e110 = exp((-((_e106 * _e106)) / _e77));
            let _e111 = (_e73 * _e106);
            let _e112 = (_e24 + _e111);
            let _e118 = textureSample(Texture, Sampler, vec2<f32>(_e112.x, (1f - _e112.y)));
            let _e120 = (_e24 - _e111);
            let _e126 = textureSample(Texture, Sampler, vec2<f32>(_e120.x, (1f - _e120.y)));
            let _e136 = exp((-((_e74 * _e74)) / _e77));
            let _e137 = (_e73 * _e74);
            let _e138 = (_e24 + _e137);
            let _e144 = textureSample(Texture, Sampler, vec2<f32>(_e138.x, (1f - _e138.y)));
            let _e146 = (_e24 - _e137);
            let _e152 = textureSample(Texture, Sampler, vec2<f32>(_e146.x, (1f - _e146.y)));
            let _e159 = (0.6666667f * _e65);
            let _e163 = exp((-((_e159 * _e159)) / _e77));
            let _e164 = (_e73 * _e159);
            let _e165 = (_e24 + _e164);
            let _e171 = textureSample(Texture, Sampler, vec2<f32>(_e165.x, (1f - _e165.y)));
            let _e173 = (_e24 - _e164);
            let _e179 = textureSample(Texture, Sampler, vec2<f32>(_e173.x, (1f - _e173.y)));
            let _e186 = (0.8333333f * _e65);
            let _e190 = exp((-((_e186 * _e186)) / _e77));
            let _e191 = (_e73 * _e186);
            let _e192 = (_e24 + _e191);
            let _e198 = textureSample(Texture, Sampler, vec2<f32>(_e192.x, (1f - _e192.y)));
            let _e200 = (_e24 - _e191);
            let _e206 = textureSample(Texture, Sampler, vec2<f32>(_e200.x, (1f - _e200.y)));
            let _e216 = exp((-((_e65 * _e65)) / _e77));
            let _e217 = (_e73 * _e65);
            let _e218 = (_e24 + _e217);
            let _e224 = textureSample(Texture, Sampler, vec2<f32>(_e218.x, (1f - _e218.y)));
            let _e226 = (_e24 - _e217);
            let _e232 = textureSample(Texture, Sampler, vec2<f32>(_e226.x, (1f - _e226.y)));
            let _e240 = (((((((_e35.xyz + ((_e91.xyz + _e99.xyz) * _e83)) + ((_e118.xyz + _e126.xyz) * _e110)) + ((_e144.xyz + _e152.xyz) * _e136)) + ((_e171.xyz + _e179.xyz) * _e163)) + ((_e198.xyz + _e206.xyz) * _e190)) + ((_e224.xyz + _e232.xyz) * _e216)) / vec3(((((((1f + (2f * _e83)) + (2f * _e110)) + (2f * _e136)) + (2f * _e163)) + (2f * _e190)) + (2f * _e216))));
            phi_288_ = (vec4<f32>(_e240.x, _e240.y, _e240.z, _e35.w) * _e25);
            break;
        }
    }
    let _e248 = phi_288_;
    out_u002e_var_u002e_SV_Target0_ = _e248;
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
