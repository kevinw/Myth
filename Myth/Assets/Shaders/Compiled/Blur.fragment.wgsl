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
    let _e37 = (_e36 * 0.5f);
    let _e38 = max(_e37, 0.0001f);
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
    let _e99 = exp((-((_e37 * _e37)) / _e40));
    let _e100 = (_e32 * _e37);
    let _e102 = textureSample(Texture, Sampler, (_e21 + _e100));
    let _e105 = textureSample(Texture, Sampler, (_e21 - _e100));
    let _e112 = (0.625f * _e36);
    let _e116 = exp((-((_e112 * _e112)) / _e40));
    let _e117 = (_e32 * _e112);
    let _e119 = textureSample(Texture, Sampler, (_e21 + _e117));
    let _e122 = textureSample(Texture, Sampler, (_e21 - _e117));
    let _e129 = (0.75f * _e36);
    let _e133 = exp((-((_e129 * _e129)) / _e40));
    let _e134 = (_e32 * _e129);
    let _e136 = textureSample(Texture, Sampler, (_e21 + _e134));
    let _e139 = textureSample(Texture, Sampler, (_e21 - _e134));
    let _e146 = (0.875f * _e36);
    let _e150 = exp((-((_e146 * _e146)) / _e40));
    let _e151 = (_e32 * _e146);
    let _e153 = textureSample(Texture, Sampler, (_e21 + _e151));
    let _e156 = textureSample(Texture, Sampler, (_e21 - _e151));
    let _e166 = exp((-((_e36 * _e36)) / _e40));
    let _e167 = (_e32 * _e36);
    let _e169 = textureSample(Texture, Sampler, (_e21 + _e167));
    let _e172 = textureSample(Texture, Sampler, (_e21 - _e167));
    out_u002e_var_u002e_SV_Target0_ = (vec4((((((((((dot(_e41, _e43) + ((dot(_e52, _e43) + dot(_e55, _e43)) * _e49)) + ((dot(_e69, _e43) + dot(_e72, _e43)) * _e66)) + ((dot(_e86, _e43) + dot(_e89, _e43)) * _e83)) + ((dot(_e102, _e43) + dot(_e105, _e43)) * _e99)) + ((dot(_e119, _e43) + dot(_e122, _e43)) * _e116)) + ((dot(_e136, _e43) + dot(_e139, _e43)) * _e133)) + ((dot(_e153, _e43) + dot(_e156, _e43)) * _e150)) + ((dot(_e169, _e43) + dot(_e172, _e43)) * _e166)) / ((((((((1f + (2f * _e49)) + (2f * _e66)) + (2f * _e83)) + (2f * _e99)) + (2f * _e116)) + (2f * _e133)) + (2f * _e150)) + (2f * _e166)))) * _e22);
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
