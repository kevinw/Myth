struct type_u002e_FragmentUniformBlock {
    TextureSize: vec2<f32>,
    Time: f32,
    BloomAmount: f32,
    ScanlineAmount: f32,
    VignetteAmount: f32,
    RgbOffset: f32,
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
    let _e30 = in_u002e_var_u002e_TEXCOORD0_1;
    let _e31 = in_u002e_var_u002e_TEXCOORD1_1;
    let _e33 = ((_e30 * 2f) - vec2<f32>(1f, 1f));
    let _e36 = FragmentUniformBlock.TextureSize;
    let _e37 = (vec2<f32>(1f, 1f) / _e36);
    let _e39 = FragmentUniformBlock.RgbOffset;
    let _e41 = (vec2<f32>(_e39, 0f) * _e37);
    let _e44 = textureSample(Texture, Sampler, clamp((_e30 + _e41), vec2<f32>(0f, 0f), vec2<f32>(1f, 1f)));
    let _e47 = textureSample(Texture, Sampler, clamp(_e30, vec2<f32>(0f, 0f), vec2<f32>(1f, 1f)));
    let _e51 = textureSample(Texture, Sampler, clamp((_e30 - _e41), vec2<f32>(0f, 0f), vec2<f32>(1f, 1f)));
    let _e57 = textureSample(Texture, Sampler, clamp((_e30 + (vec2<f32>(1f, 0f) / _e36)), vec2<f32>(0f, 0f), vec2<f32>(1f, 1f)));
    let _e62 = textureSample(Texture, Sampler, clamp((_e30 + (vec2<f32>(-1f, 0f) / _e36)), vec2<f32>(0f, 0f), vec2<f32>(1f, 1f)));
    let _e68 = textureSample(Texture, Sampler, clamp((_e30 + (vec2<f32>(0f, 1f) / _e36)), vec2<f32>(0f, 0f), vec2<f32>(1f, 1f)));
    let _e74 = textureSample(Texture, Sampler, clamp((_e30 + (vec2<f32>(0f, -1f) / _e36)), vec2<f32>(0f, 0f), vec2<f32>(1f, 1f)));
    let _e79 = textureSample(Texture, Sampler, clamp((_e30 + _e37), vec2<f32>(0f, 0f), vec2<f32>(1f, 1f)));
    let _e86 = textureSample(Texture, Sampler, clamp((_e30 + (vec2<f32>(-1f, 1f) / _e36)), vec2<f32>(0f, 0f), vec2<f32>(1f, 1f)));
    let _e93 = textureSample(Texture, Sampler, clamp((_e30 + (vec2<f32>(1f, -1f) / _e36)), vec2<f32>(0f, 0f), vec2<f32>(1f, 1f)));
    let _e100 = textureSample(Texture, Sampler, clamp((_e30 + (vec2<f32>(-1f, -1f) / _e36)), vec2<f32>(0f, 0f), vec2<f32>(1f, 1f)));
    let _e104 = ((((((((_e57.xyz + _e62.xyz) + _e68.xyz) + _e74.xyz) + (_e79.xyz * 0.5f)) + (_e86.xyz * 0.5f)) + (_e93.xyz * 0.5f)) + (_e100.xyz * 0.5f)) * vec3<f32>(0.22222222f, 0.22222222f, 0.22222222f));
    let _e113 = FragmentUniformBlock.BloomAmount;
    let _e118 = FragmentUniformBlock.VignetteAmount;
    let _e121 = clamp(((vec3<f32>(_e44.x, _e47.y, _e51.z) + ((_e104 * smoothstep(0.35f, 1f, max(max(_e104.x, _e104.y), _e104.z))) * _e113)) * mix(1f, smoothstep(1.25f, 0.35f, dot(_e33, _e33)), _e118)), vec3<f32>(0f, 0f, 0f), vec3<f32>(1f, 1f, 1f));
    out_u002e_var_u002e_SV_Target0_ = (vec4<f32>(_e121.x, _e121.y, _e121.z, 1f) * _e31);
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
