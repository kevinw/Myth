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
    let _e46 = textureSample(Texture, Sampler, vec2<f32>(_e21.x, (1f - _e21.y)));
    let _e48 = FragmentUniformBlock.ChannelMask;
    let _e50 = (0.125f * _e36);
    let _e54 = exp((-((_e50 * _e50)) / _e40));
    let _e55 = (_e32 * _e50);
    let _e56 = (_e21 + _e55);
    let _e62 = textureSample(Texture, Sampler, vec2<f32>(_e56.x, (1f - _e56.y)));
    let _e64 = (_e21 - _e55);
    let _e70 = textureSample(Texture, Sampler, vec2<f32>(_e64.x, (1f - _e64.y)));
    let _e77 = (0.25f * _e36);
    let _e81 = exp((-((_e77 * _e77)) / _e40));
    let _e82 = (_e32 * _e77);
    let _e83 = (_e21 + _e82);
    let _e89 = textureSample(Texture, Sampler, vec2<f32>(_e83.x, (1f - _e83.y)));
    let _e91 = (_e21 - _e82);
    let _e97 = textureSample(Texture, Sampler, vec2<f32>(_e91.x, (1f - _e91.y)));
    let _e104 = (0.375f * _e36);
    let _e108 = exp((-((_e104 * _e104)) / _e40));
    let _e109 = (_e32 * _e104);
    let _e110 = (_e21 + _e109);
    let _e116 = textureSample(Texture, Sampler, vec2<f32>(_e110.x, (1f - _e110.y)));
    let _e118 = (_e21 - _e109);
    let _e124 = textureSample(Texture, Sampler, vec2<f32>(_e118.x, (1f - _e118.y)));
    let _e134 = exp((-((_e37 * _e37)) / _e40));
    let _e135 = (_e32 * _e37);
    let _e136 = (_e21 + _e135);
    let _e142 = textureSample(Texture, Sampler, vec2<f32>(_e136.x, (1f - _e136.y)));
    let _e144 = (_e21 - _e135);
    let _e150 = textureSample(Texture, Sampler, vec2<f32>(_e144.x, (1f - _e144.y)));
    let _e157 = (0.625f * _e36);
    let _e161 = exp((-((_e157 * _e157)) / _e40));
    let _e162 = (_e32 * _e157);
    let _e163 = (_e21 + _e162);
    let _e169 = textureSample(Texture, Sampler, vec2<f32>(_e163.x, (1f - _e163.y)));
    let _e171 = (_e21 - _e162);
    let _e177 = textureSample(Texture, Sampler, vec2<f32>(_e171.x, (1f - _e171.y)));
    let _e184 = (0.75f * _e36);
    let _e188 = exp((-((_e184 * _e184)) / _e40));
    let _e189 = (_e32 * _e184);
    let _e190 = (_e21 + _e189);
    let _e196 = textureSample(Texture, Sampler, vec2<f32>(_e190.x, (1f - _e190.y)));
    let _e198 = (_e21 - _e189);
    let _e204 = textureSample(Texture, Sampler, vec2<f32>(_e198.x, (1f - _e198.y)));
    let _e211 = (0.875f * _e36);
    let _e215 = exp((-((_e211 * _e211)) / _e40));
    let _e216 = (_e32 * _e211);
    let _e217 = (_e21 + _e216);
    let _e223 = textureSample(Texture, Sampler, vec2<f32>(_e217.x, (1f - _e217.y)));
    let _e225 = (_e21 - _e216);
    let _e231 = textureSample(Texture, Sampler, vec2<f32>(_e225.x, (1f - _e225.y)));
    let _e241 = exp((-((_e36 * _e36)) / _e40));
    let _e242 = (_e32 * _e36);
    let _e243 = (_e21 + _e242);
    let _e249 = textureSample(Texture, Sampler, vec2<f32>(_e243.x, (1f - _e243.y)));
    let _e251 = (_e21 - _e242);
    let _e257 = textureSample(Texture, Sampler, vec2<f32>(_e251.x, (1f - _e251.y)));
    out_u002e_var_u002e_SV_Target0_ = (vec4((((((((((dot(_e46, _e48) + ((dot(_e62, _e48) + dot(_e70, _e48)) * _e54)) + ((dot(_e89, _e48) + dot(_e97, _e48)) * _e81)) + ((dot(_e116, _e48) + dot(_e124, _e48)) * _e108)) + ((dot(_e142, _e48) + dot(_e150, _e48)) * _e134)) + ((dot(_e169, _e48) + dot(_e177, _e48)) * _e161)) + ((dot(_e196, _e48) + dot(_e204, _e48)) * _e188)) + ((dot(_e223, _e48) + dot(_e231, _e48)) * _e215)) + ((dot(_e249, _e48) + dot(_e257, _e48)) * _e241)) / ((((((((1f + (2f * _e54)) + (2f * _e81)) + (2f * _e108)) + (2f * _e134)) + (2f * _e161)) + (2f * _e188)) + (2f * _e215)) + (2f * _e241)))) * _e22);
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
