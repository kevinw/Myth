struct type_u002e_FragmentUniformBlock {
    TextureSize: vec2<f32>,
    BloomAmount: f32,
    VignetteAmount: f32,
    RgbOffset: f32,
    Padding0_: f32,
    Padding1_: f32,
    Padding2_: f32,
}

@group(3) @binding(0) 
var<uniform> FragmentUniformBlock: type_u002e_FragmentUniformBlock;
@group(2) @binding(0) 
var Texture: texture_2d<f32>;
@group(2) @binding(100) 
var Sampler: sampler;
var<private> in_u002e_var_u002e_TEXCOORD0_1: vec2<f32>;
var<private> in_u002e_var_u002e_TEXCOORD1_1: vec4<f32>;
var<private> out_u002e_var_u002e_SV_Target0_: vec4<f32>;

fn fragment_main_1() {
    let _e32 = in_u002e_var_u002e_TEXCOORD0_1;
    let _e33 = in_u002e_var_u002e_TEXCOORD1_1;
    let _e35 = ((_e32 * 2f) - vec2<f32>(1f, 1f));
    let _e38 = FragmentUniformBlock.TextureSize;
    let _e39 = (vec2<f32>(1f, 1f) / _e38);
    let _e41 = FragmentUniformBlock.RgbOffset;
    let _e43 = (vec2<f32>(_e41, 0f) * _e39);
    let _e46 = textureSample(Texture, Sampler, clamp((_e32 + _e43), vec2<f32>(0f, 0f), vec2<f32>(1f, 1f)));
    let _e49 = textureSample(Texture, Sampler, clamp(_e32, vec2<f32>(0f, 0f), vec2<f32>(1f, 1f)));
    let _e53 = textureSample(Texture, Sampler, clamp((_e32 - _e43), vec2<f32>(0f, 0f), vec2<f32>(1f, 1f)));
    let _e59 = textureSample(Texture, Sampler, clamp((_e32 + (vec2<f32>(1f, 0f) / _e38)), vec2<f32>(0f, 0f), vec2<f32>(1f, 1f)));
    let _e64 = textureSample(Texture, Sampler, clamp((_e32 + (vec2<f32>(-1f, 0f) / _e38)), vec2<f32>(0f, 0f), vec2<f32>(1f, 1f)));
    let _e70 = textureSample(Texture, Sampler, clamp((_e32 + (vec2<f32>(0f, 1f) / _e38)), vec2<f32>(0f, 0f), vec2<f32>(1f, 1f)));
    let _e76 = textureSample(Texture, Sampler, clamp((_e32 + (vec2<f32>(0f, -1f) / _e38)), vec2<f32>(0f, 0f), vec2<f32>(1f, 1f)));
    let _e81 = textureSample(Texture, Sampler, clamp((_e32 + _e39), vec2<f32>(0f, 0f), vec2<f32>(1f, 1f)));
    let _e88 = textureSample(Texture, Sampler, clamp((_e32 + (vec2<f32>(-1f, 1f) / _e38)), vec2<f32>(0f, 0f), vec2<f32>(1f, 1f)));
    let _e95 = textureSample(Texture, Sampler, clamp((_e32 + (vec2<f32>(1f, -1f) / _e38)), vec2<f32>(0f, 0f), vec2<f32>(1f, 1f)));
    let _e102 = textureSample(Texture, Sampler, clamp((_e32 + (vec2<f32>(-1f, -1f) / _e38)), vec2<f32>(0f, 0f), vec2<f32>(1f, 1f)));
    let _e106 = ((((((((_e59.xyz + _e64.xyz) + _e70.xyz) + _e76.xyz) + (_e81.xyz * 0.5f)) + (_e88.xyz * 0.5f)) + (_e95.xyz * 0.5f)) + (_e102.xyz * 0.5f)) * vec3<f32>(0.22222222f, 0.22222222f, 0.22222222f));
    let _e115 = FragmentUniformBlock.BloomAmount;
    let _e120 = FragmentUniformBlock.VignetteAmount;
    let _e126 = FragmentUniformBlock.TextureSize[1i];
    let _e135 = clamp((((vec3<f32>(_e46.x, _e49.y, _e53.z) + ((_e106 * smoothstep(0.35f, 1f, max(max(_e106.x, _e106.y), _e106.z))) * _e115)) * mix(1f, smoothstep(1.25f, 0.35f, dot(_e35, _e35)), _e120)) * (1f - ((0.5f + (0.5f * cos(((_e32.y * _e126) * 6.2831855f)))) * 0.1f))), vec3<f32>(0f, 0f, 0f), vec3<f32>(1f, 1f, 1f));
    out_u002e_var_u002e_SV_Target0_ = (vec4<f32>(_e135.x, _e135.y, _e135.z, 1f) * _e33);
    return;
}

@fragment 
fn fragment_main(@location(0) in_u002e_var_u002e_TEXCOORD0_: vec2<f32>, @location(1) in_u002e_var_u002e_TEXCOORD1_: vec4<f32>) -> @location(0) vec4<f32> {
    in_u002e_var_u002e_TEXCOORD0_1 = in_u002e_var_u002e_TEXCOORD0_;
    in_u002e_var_u002e_TEXCOORD1_1 = in_u002e_var_u002e_TEXCOORD1_;
    fragment_main_1();
    let _e5 = out_u002e_var_u002e_SV_Target0_;
    return _e5;
}
