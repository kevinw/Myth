struct type_u002e_FragmentUniformBlock {
    FieldSize: vec4<f32>,
}

@group(3) @binding(0) 
var<uniform> FragmentUniformBlock: type_u002e_FragmentUniformBlock;
@group(2) @binding(0) 
var Texture: texture_2d<f32>;
@group(2) @binding(100) 
var Sampler: sampler;
@group(2) @binding(1) 
var Field: texture_2d<f32>;
@group(2) @binding(101) 
var FieldSampler: sampler;
var<private> in_u002e_var_u002e_TEXCOORD0_1: vec2<f32>;
var<private> in_u002e_var_u002e_TEXCOORD1_1: vec4<f32>;
var<private> in_u002e_var_u002e_TEXCOORD4_1: vec4<f32>;
var<private> global: vec4<f32>;
var<private> out_u002e_var_u002e_SV_Target0_: vec4<f32>;

fn fragment_main_1() {
    let _e13 = in_u002e_var_u002e_TEXCOORD0_1;
    let _e14 = in_u002e_var_u002e_TEXCOORD1_1;
    let _e15 = in_u002e_var_u002e_TEXCOORD4_1;
    let _e16 = global;
    let _e17 = textureSample(Texture, Sampler, _e13);
    let _e28 = ((((_e17 * _e15.x) * _e14) + (_e14 * (_e15.y * _e17.w))) + (_e14 * _e15.z));
    let _e31 = FragmentUniformBlock.FieldSize;
    let _e34 = textureSample(Field, FieldSampler, (_e16.xy / _e31.xy));
    let _e40 = (_e28.xyz * ((vec3<f32>(1f, 1f, 1f) - _e34.www) + _e34.xyz));
    out_u002e_var_u002e_SV_Target0_ = vec4<f32>(_e40.x, _e40.y, _e40.z, _e28.w);
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
