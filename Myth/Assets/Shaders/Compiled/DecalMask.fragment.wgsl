struct type_u002e_FragmentUniformBlock {
    CoverageSize: vec4<f32>,
}

@group(3) @binding(0) 
var<uniform> FragmentUniformBlock: type_u002e_FragmentUniformBlock;
@group(2) @binding(0) 
var Texture: texture_2d<f32>;
@group(2) @binding(100) 
var Sampler: sampler;
@group(2) @binding(1) 
var Coverage: texture_2d<f32>;
@group(2) @binding(101) 
var CoverageSampler: sampler;
var<private> in_u002e_var_u002e_TEXCOORD0_1: vec2<f32>;
var<private> in_u002e_var_u002e_TEXCOORD1_1: vec4<f32>;
var<private> in_u002e_var_u002e_TEXCOORD4_1: vec4<f32>;
var<private> global: vec4<f32>;
var<private> out_u002e_var_u002e_SV_Target0_: vec4<f32>;

fn fragment_main_1() {
    let _e11 = in_u002e_var_u002e_TEXCOORD0_1;
    let _e12 = in_u002e_var_u002e_TEXCOORD1_1;
    let _e13 = in_u002e_var_u002e_TEXCOORD4_1;
    let _e14 = global;
    let _e15 = textureSample(Texture, Sampler, _e11);
    let _e29 = FragmentUniformBlock.CoverageSize;
    let _e32 = textureSample(Coverage, CoverageSampler, (_e14.xy / _e29.xy));
    out_u002e_var_u002e_SV_Target0_ = (((((_e15 * _e13.x) * _e12) + (_e12 * (_e13.y * _e15.w))) + (_e12 * _e13.z)) * _e32.x);
    return;
}

@fragment 
fn fragment_main(@location(0) in_u002e_var_u002e_TEXCOORD0_: vec2<f32>, @location(1) in_u002e_var_u002e_TEXCOORD1_: vec4<f32>, @location(2) in_u002e_var_u002e_TEXCOORD4_: vec4<f32>, @builtin(position) param: vec4<f32>) -> @location(0) vec4<f32> {
    in_u002e_var_u002e_TEXCOORD0_1 = in_u002e_var_u002e_TEXCOORD0_;
    in_u002e_var_u002e_TEXCOORD1_1 = in_u002e_var_u002e_TEXCOORD1_;
    in_u002e_var_u002e_TEXCOORD4_1 = in_u002e_var_u002e_TEXCOORD4_;
    global = param;
    fragment_main_1();
    let _e9 = out_u002e_var_u002e_SV_Target0_;
    return _e9;
}
