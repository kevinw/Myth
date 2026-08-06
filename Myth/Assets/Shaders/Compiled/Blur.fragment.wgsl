struct type_u002e_FragmentUniformBlock {
    BlurParams: vec4<f32>,
    TexelSize: vec4<f32>,
    ChannelMask: vec4<f32>,
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
    let _e21 = in_u002e_var_u002e_TEXCOORD0_1;
    let _e22 = in_u002e_var_u002e_TEXCOORD1_1;
    let _e23 = in_u002e_var_u002e_TEXCOORD4_1;
    if (_e23.x < -1000f) {
        discard;
    }
    let _e27 = FragmentUniformBlock.BlurParams;
    let _e30 = FragmentUniformBlock.TexelSize;
    let _e32 = (_e27.xy * _e30.xy);
    let _e35 = FragmentUniformBlock.BlurParams[2i];
    let _e36 = max(_e35, 0.0001f);
    let _e38 = max((_e36 * 0.5f), 0.0001f);
    let _e40 = ((2f * _e38) * _e38);
    let _e41 = textureSample(Texture, Sampler, _e21);
    let _e43 = FragmentUniformBlock.ChannelMask;
    let _e45 = (0.125f * _e36);
    let _e49 = exp((-((_e45 * _e45)) / _e40));
    let _e50 = (_e32 * _e45);
    let _e52 = textureSample(Texture, Sampler, (_e21 + _e50));
    let _e55 = textureSample(Texture, Sampler, (_e21 - _e50));
    let _e62 = (0.25f * _e36);
    let _e66 = exp((-((_e62 * _e62)) / _e40));
    let _e67 = (_e32 * _e62);
    let _e69 = textureSample(Texture, Sampler, (_e21 + _e67));
    let _e72 = textureSample(Texture, Sampler, (_e21 - _e67));
    let _e79 = (0.375f * _e36);
    let _e83 = exp((-((_e79 * _e79)) / _e40));
    let _e84 = (_e32 * _e79);
    let _e86 = textureSample(Texture, Sampler, (_e21 + _e84));
    let _e89 = textureSample(Texture, Sampler, (_e21 - _e84));
    let _e96 = (0.5f * _e36);
    let _e100 = exp((-((_e96 * _e96)) / _e40));
    let _e101 = (_e32 * _e96);
    let _e103 = textureSample(Texture, Sampler, (_e21 + _e101));
    let _e106 = textureSample(Texture, Sampler, (_e21 - _e101));
    let _e113 = (0.625f * _e36);
    let _e117 = exp((-((_e113 * _e113)) / _e40));
    let _e118 = (_e32 * _e113);
    let _e120 = textureSample(Texture, Sampler, (_e21 + _e118));
    let _e123 = textureSample(Texture, Sampler, (_e21 - _e118));
    let _e130 = (0.75f * _e36);
    let _e134 = exp((-((_e130 * _e130)) / _e40));
    let _e135 = (_e32 * _e130);
    let _e137 = textureSample(Texture, Sampler, (_e21 + _e135));
    let _e140 = textureSample(Texture, Sampler, (_e21 - _e135));
    let _e147 = (0.875f * _e36);
    let _e151 = exp((-((_e147 * _e147)) / _e40));
    let _e152 = (_e32 * _e147);
    let _e154 = textureSample(Texture, Sampler, (_e21 + _e152));
    let _e157 = textureSample(Texture, Sampler, (_e21 - _e152));
    let _e167 = exp((-((_e36 * _e36)) / _e40));
    let _e168 = (_e32 * _e36);
    let _e170 = textureSample(Texture, Sampler, (_e21 + _e168));
    let _e173 = textureSample(Texture, Sampler, (_e21 - _e168));
    out_u002e_var_u002e_SV_Target0_ = (vec4((((((((((dot(_e41, _e43) + ((dot(_e52, _e43) + dot(_e55, _e43)) * _e49)) + ((dot(_e69, _e43) + dot(_e72, _e43)) * _e66)) + ((dot(_e86, _e43) + dot(_e89, _e43)) * _e83)) + ((dot(_e103, _e43) + dot(_e106, _e43)) * _e100)) + ((dot(_e120, _e43) + dot(_e123, _e43)) * _e117)) + ((dot(_e137, _e43) + dot(_e140, _e43)) * _e134)) + ((dot(_e154, _e43) + dot(_e157, _e43)) * _e151)) + ((dot(_e170, _e43) + dot(_e173, _e43)) * _e167)) / ((((((((1f + (2f * _e49)) + (2f * _e66)) + (2f * _e83)) + (2f * _e100)) + (2f * _e117)) + (2f * _e134)) + (2f * _e151)) + (2f * _e167)))) * _e22);
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
