struct type_u002e_FragmentUniformBlock {
    TextureSizeTimeAmplitude: vec4<f32>,
    FrequencySpeedFlatOnlyPad: vec4<f32>,
}

@group(3) @binding(0) 
var<uniform> FragmentUniformBlock: type_u002e_FragmentUniformBlock;
@group(2) @binding(0) 
var Texture: texture_2d<f32>;
@group(2) @binding(100) 
var Sampler: sampler;
@group(2) @binding(1) 
var DistortMask: texture_2d<f32>;
@group(2) @binding(101) 
var DistortMaskSampler: sampler;
var<private> in_u002e_var_u002e_TEXCOORD0_1: vec2<f32>;
var<private> in_u002e_var_u002e_TEXCOORD1_1: vec4<f32>;
var<private> in_u002e_var_u002e_TEXCOORD4_1: vec4<f32>;
var<private> global: vec4<f32>;
var<private> out_u002e_var_u002e_SV_Target0_: vec4<f32>;

fn fragment_main_1() {
    let _e22 = in_u002e_var_u002e_TEXCOORD0_1;
    let _e23 = in_u002e_var_u002e_TEXCOORD1_1;
    let _e24 = in_u002e_var_u002e_TEXCOORD4_1;
    let _e25 = global;
    if (_e24.x < -1000f) {
        discard;
    }
    let _e29 = FragmentUniformBlock.TextureSizeTimeAmplitude;
    let _e30 = _e29.xy;
    let _e33 = FragmentUniformBlock.TextureSizeTimeAmplitude[2i];
    let _e36 = FragmentUniformBlock.TextureSizeTimeAmplitude[3i];
    let _e39 = FragmentUniformBlock.FrequencySpeedFlatOnlyPad[0i];
    let _e42 = FragmentUniformBlock.FrequencySpeedFlatOnlyPad[1i];
    let _e45 = FragmentUniformBlock.FrequencySpeedFlatOnlyPad[2i];
    let _e46 = textureSample(DistortMask, DistortMaskSampler, _e22);
    let _e49 = (_e25.y * _e39);
    let _e50 = (_e33 * _e42);
    let _e75 = textureSample(Texture, Sampler, mix(((floor((_e22 * _e30)) + vec2<f32>(0.5f, 0.5f)) / _e30), ((floor(((_e22 + vec2<f32>((((sin((_e49 + _e50)) * _e36) + ((sin(((_e49 * 2.1f) + (_e50 * 1.7f))) * _e36) * 0.35f)) / _e29.x), 0f)) * _e30)) + vec2<f32>(0.5f, 0.5f)) / _e30), vec2(_e46.x)));
    let _e76 = (_e75 * _e23);
    out_u002e_var_u002e_SV_Target0_ = vec4<f32>(_e76.x, _e76.y, _e76.z, (_e76.w * mix(1f, (1f - _e46.x), _e45)));
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
