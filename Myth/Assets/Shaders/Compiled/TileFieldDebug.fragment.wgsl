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
    var phi_68_: f32;
    var phi_69_: f32;

    let _e16 = in_u002e_var_u002e_TEXCOORD0_1;
    let _e17 = in_u002e_var_u002e_TEXCOORD1_1;
    let _e23 = textureSample(Texture, Sampler, vec2<f32>(_e16.x, (1f - _e16.y)));
    let _e26 = FragmentUniformBlock.ChannelMinMaxPadding[0i];
    let _e28 = i32((_e26 + 0.5f));
    if (_e28 == 0i) {
        phi_69_ = _e23.x;
    } else {
        if (_e28 == 1i) {
            phi_68_ = (floor((floor(((_e23.y * 255f) + 0.5f)) * 0.03125f)) * 0.25f);
        } else {
            phi_68_ = _e23.z;
        }
        let _e41 = phi_68_;
        phi_69_ = _e41;
    }
    let _e43 = phi_69_;
    let _e46 = FragmentUniformBlock.ChannelMinMaxPadding[1i];
    let _e49 = FragmentUniformBlock.ChannelMinMaxPadding[2i];
    let _e54 = clamp(((_e43 - _e46) / max((_e49 - _e46), 0.0001f)), 0f, 1f);
    out_u002e_var_u002e_SV_Target0_ = (vec4<f32>(_e54, _e54, _e54, _e23.w) * _e17);
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
