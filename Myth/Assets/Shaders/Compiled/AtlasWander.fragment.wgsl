struct type_u002e_FragmentUniformBlock {
    Atlas: vec4<f32>,
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
    let _e8 = in_u002e_var_u002e_TEXCOORD0_1;
    let _e9 = in_u002e_var_u002e_TEXCOORD1_1;
    let _e10 = in_u002e_var_u002e_TEXCOORD4_1;
    let _e11 = textureSample(Texture, Sampler, _e8);
    let _e13 = FragmentUniformBlock.Atlas;
    let _e19 = textureSample(Texture, Sampler, fract((_e8 + (_e13.zw / _e13.xy))));
    out_u002e_var_u002e_SV_Target0_ = ((((vec4<f32>(_e19.x, _e19.y, _e19.z, _e11.w) * _e10.x) * _e9) + (_e9 * (_e10.y * _e11.w))) + (_e9 * _e10.z));
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
