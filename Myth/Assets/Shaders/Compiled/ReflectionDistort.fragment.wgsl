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
var<private> in_u002e_var_u002e_TEXCOORD4_1: vec4<f32>;
var<private> global: vec4<f32>;
var<private> out_u002e_var_u002e_SV_Target0_: vec4<f32>;

fn fragment_main_1() {
    let _e20 = in_u002e_var_u002e_TEXCOORD0_1;
    let _e21 = in_u002e_var_u002e_TEXCOORD1_1;
    let _e22 = in_u002e_var_u002e_TEXCOORD4_1;
    let _e23 = global;
    if (_e22.x < -1000f) {
        discard;
    }
    let _e27 = FragmentUniformBlock.TextureSizeTimeAmplitude;
    let _e28 = _e27.xy;
    let _e31 = FragmentUniformBlock.TextureSizeTimeAmplitude[2i];
    let _e34 = FragmentUniformBlock.TextureSizeTimeAmplitude[3i];
    let _e37 = FragmentUniformBlock.FrequencySpeedPadding[0i];
    let _e40 = FragmentUniformBlock.FrequencySpeedPadding[1i];
    let _e42 = (_e23.y * _e37);
    let _e43 = (_e31 * _e40);
    let _e67 = textureSample(Texture, Sampler, ((floor(((vec2<f32>(_e20.x, (1f - _e20.y)) + vec2<f32>((((sin((_e42 + _e43)) * _e34) + ((sin(((_e42 * 2.1f) + (_e43 * 1.7f))) * _e34) * 0.35f)) / _e27.x), 0f)) * _e28)) + vec2<f32>(0.5f, 0.5f)) / _e28));
    out_u002e_var_u002e_SV_Target0_ = (_e67 * _e21);
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
