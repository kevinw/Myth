struct type_u002e_FragmentUniformBlock {
    TextureSizeTimeAmplitude: vec4<f32>,
    FrequencySpeedPadding: vec4<f32>,
}

@group(3) @binding(0) 
var<uniform> FragmentUniformBlock: type_u002e_FragmentUniformBlock;
@group(2) @binding(0) 
var Texture: texture_2d<f32>;
@group(2) @binding(100) 
var Sampler: sampler;
var<private> in_u002e_var_u002e_TEXCOORD0_1: vec2<f32>;
var<private> in_u002e_var_u002e_TEXCOORD1_1: vec4<f32>;
var<private> global: vec4<f32>;
var<private> out_u002e_var_u002e_SV_Target0_: vec4<f32>;

fn fragment_main_1() {
    let _e18 = in_u002e_var_u002e_TEXCOORD0_1;
    let _e19 = in_u002e_var_u002e_TEXCOORD1_1;
    let _e20 = global;
    let _e22 = FragmentUniformBlock.TextureSizeTimeAmplitude;
    let _e23 = _e22.xy;
    let _e26 = FragmentUniformBlock.TextureSizeTimeAmplitude[2i];
    let _e29 = FragmentUniformBlock.TextureSizeTimeAmplitude[3i];
    let _e32 = FragmentUniformBlock.FrequencySpeedPadding[0i];
    let _e35 = FragmentUniformBlock.FrequencySpeedPadding[1i];
    let _e37 = (_e20.y * _e32);
    let _e38 = (_e26 * _e35);
    let _e62 = textureSample(Texture, Sampler, ((floor(((vec2<f32>(_e18.x, (1f - _e18.y)) + vec2<f32>((((sin((_e37 + _e38)) * _e29) + ((sin(((_e37 * 2.1f) + (_e38 * 1.7f))) * _e29) * 0.35f)) / _e22.x), 0f)) * _e23)) + vec2<f32>(0.5f, 0.5f)) / _e23));
    out_u002e_var_u002e_SV_Target0_ = (_e62 * _e19);
    return;
}

@fragment 
fn fragment_main(@location(0) in_u002e_var_u002e_TEXCOORD0_: vec2<f32>, @location(1) in_u002e_var_u002e_TEXCOORD1_: vec4<f32>, @builtin(position) param: vec4<f32>) -> @location(0) vec4<f32> {
    in_u002e_var_u002e_TEXCOORD0_1 = in_u002e_var_u002e_TEXCOORD0_;
    in_u002e_var_u002e_TEXCOORD1_1 = in_u002e_var_u002e_TEXCOORD1_;
    global = param;
    fragment_main_1();
    let _e7 = out_u002e_var_u002e_SV_Target0_;
    return _e7;
}
