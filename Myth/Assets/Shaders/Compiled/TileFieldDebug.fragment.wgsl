struct type_u002e_FragmentUniformBlock {
    ChannelMinMaxPadding: vec4<f32>,
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
    var phi_59_: f32;
    var phi_60_: f32;

    let _e13 = in_u002e_var_u002e_TEXCOORD0_1;
    let _e14 = in_u002e_var_u002e_TEXCOORD1_1;
    let _e20 = textureSample(Texture, Sampler, vec2<f32>(_e13.x, (1f - _e13.y)));
    let _e23 = FragmentUniformBlock.ChannelMinMaxPadding[0i];
    let _e25 = i32((_e23 + 0.5f));
    if (_e25 == 0i) {
        phi_60_ = _e20.x;
    } else {
        if (_e25 == 1i) {
            phi_59_ = _e20.y;
        } else {
            phi_59_ = _e20.z;
        }
        let _e32 = phi_59_;
        phi_60_ = _e32;
    }
    let _e34 = phi_60_;
    let _e37 = FragmentUniformBlock.ChannelMinMaxPadding[1i];
    let _e40 = FragmentUniformBlock.ChannelMinMaxPadding[2i];
    let _e45 = clamp(((_e34 - _e37) / max((_e40 - _e37), 0.0001f)), 0f, 1f);
    out_u002e_var_u002e_SV_Target0_ = (vec4<f32>(_e45, _e45, _e45, _e20.w) * _e14);
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
