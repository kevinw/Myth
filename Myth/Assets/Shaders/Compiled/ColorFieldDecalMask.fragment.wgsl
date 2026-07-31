struct type_u002e_FragmentUniformBlock {
    TargetSize: vec4<f32>,
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
@group(2) @binding(2)
var Field: texture_2d<f32>;
@group(2) @binding(102)
var FieldSampler: sampler;
var<private> in_u002e_var_u002e_TEXCOORD0_1: vec2<f32>;
var<private> in_u002e_var_u002e_TEXCOORD1_1: vec4<f32>;
var<private> in_u002e_var_u002e_TEXCOORD4_1: vec4<f32>;
var<private> global: vec4<f32>;
var<private> out_u002e_var_u002e_SV_Target0_: vec4<f32>;

fn fragment_main_1() {
    let _e15 = in_u002e_var_u002e_TEXCOORD0_1;
    let _e16 = in_u002e_var_u002e_TEXCOORD1_1;
    let _e17 = in_u002e_var_u002e_TEXCOORD4_1;
    let _e18 = global;
    let _e19 = textureSample(Texture, Sampler, _e15);
    let _e33 = FragmentUniformBlock.TargetSize;
    let _e35 = (_e18.xy / _e33.xy);
    let _e36 = textureSample(Coverage, CoverageSampler, _e35);
    let _e38 = (((((_e19 * _e17.x) * _e16) + (_e16 * (_e17.y * _e19.w))) + (_e16 * _e17.z)) * _e36.x);
    let _e39 = textureSample(Field, FieldSampler, _e35);
    let _e45 = (_e38.xyz * ((vec3<f32>(1f, 1f, 1f) - _e39.www) + _e39.xyz));
    out_u002e_var_u002e_SV_Target0_ = vec4<f32>(_e45.x, _e45.y, _e45.z, _e38.w);
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
