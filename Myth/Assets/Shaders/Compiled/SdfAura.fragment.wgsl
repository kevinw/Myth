struct type_u002e_FragmentUniformBlock {
    Params: vec4<f32>,
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
    var phi_73_: bool;

    let _e25 = in_u002e_var_u002e_TEXCOORD0_1;
    let _e26 = in_u002e_var_u002e_TEXCOORD1_1;
    let _e27 = in_u002e_var_u002e_TEXCOORD4_1;
    if (_e27.x < -1000f) {
        discard;
    }
    let _e35 = textureSample(Texture, Sampler, vec2<f32>(_e25.x, (1f - _e25.y)));
    let _e39 = FragmentUniformBlock.Params[3i];
    let _e40 = (_e35.x * _e39);
    phi_73_ = true;
    if !((_e40 < 0.5f)) {
        let _e45 = FragmentUniformBlock.Params[1i];
        phi_73_ = (_e40 > _e45);
    }
    let _e48 = phi_73_;
    if _e48 {
        discard;
    }
    let _e51 = FragmentUniformBlock.Params[0i];
    let _e55 = FragmentUniformBlock.Params[2i];
    let _e68 = (clamp((abs(((fract((vec3(fract((floor(((_e40 - _e51) / _e55)) * 0.16666667f))) + vec3<f32>(0f, 0.6666667f, 0.33333334f))) * 6f) - vec3<f32>(3f, 3f, 3f))) - vec3<f32>(1f, 1f, 1f)), vec3<f32>(0f, 0f, 0f), vec3<f32>(1f, 1f, 1f)) * 1f);
    let _e71 = FragmentUniformBlock.Params[1i];
    out_u002e_var_u002e_SV_Target0_ = (vec4<f32>(_e68.x, _e68.y, _e68.z, clamp(((_e71 - _e40) * 0.33333334f), 0f, 1f)) * _e26);
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
