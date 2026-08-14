struct type_u002e_FragmentUniformBlock {
    NormalConfig: vec4<f32>,
    NormalMotion: vec4<f32>,
}

@group(3) @binding(0) 
var<uniform> FragmentUniformBlock: type_u002e_FragmentUniformBlock;
@group(2) @binding(0)
var NormalMap: texture_2d<f32>;
@group(2) @binding(100)
var NormalSampler: sampler;
var<private> in_u002e_var_u002e_TEXCOORD0_1: vec2<f32>;
var<private> out_u002e_var_u002e_SV_Target0_: vec4<f32>;

fn fragment_main_1() {
    let _e16 = in_u002e_var_u002e_TEXCOORD0_1;
    let _e18 = FragmentUniformBlock.NormalMotion;
    let _e23 = FragmentUniformBlock.NormalConfig[3i];
    let _e26 = FragmentUniformBlock.NormalConfig;
    let _e30 = textureSample(NormalMap, NormalSampler, ((_e16 - (_e18.xy * _e23)) / max(_e26.xy, vec2<f32>(1f, 1f))));
    let _e33 = ((_e30.xyz * 2f) - vec3<f32>(1f, 1f, 1f));
    let _e36 = FragmentUniformBlock.NormalConfig[2i];
    let _e39 = (_e33.xy * max(_e36, 0f));
    let _e46 = ((normalize(vec3<f32>(_e39.x, _e39.y, _e33.z)) * 0.5f) + vec3<f32>(0.5f, 0.5f, 0.5f));
    out_u002e_var_u002e_SV_Target0_ = vec4<f32>(_e46.x, _e46.y, _e46.z, 1f);
    return;
}

@fragment 
fn fragment_main(@location(0) in_u002e_var_u002e_TEXCOORD0_: vec2<f32>) -> @location(0) vec4<f32> {
    in_u002e_var_u002e_TEXCOORD0_1 = in_u002e_var_u002e_TEXCOORD0_;
    fragment_main_1();
    let _e3 = out_u002e_var_u002e_SV_Target0_;
    return _e3;
}
