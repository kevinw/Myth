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
    var phi_65_: f32;
    var phi_66_: f32;
    var phi_81_: f32;

    let _e16 = in_u002e_var_u002e_TEXCOORD0_1;
    let _e17 = in_u002e_var_u002e_TEXCOORD1_1;
    let _e18 = textureSample(Texture, Sampler, _e16);
    let _e21 = FragmentUniformBlock.ChannelMinMaxPadding[0i];
    let _e23 = i32((_e21 + 0.5f));
    if (_e23 == 0i) {
        phi_66_ = _e18.x;
    } else {
        if (_e23 == 1i) {
            phi_65_ = (floor((floor(((_e18.y * 255f) + 0.5f)) * 0.03125f)) * 0.25f);
        } else {
            phi_65_ = _e18.z;
        }
        let _e36 = phi_65_;
        phi_66_ = _e36;
    }
    let _e38 = phi_66_;
    let _e41 = FragmentUniformBlock.ChannelMinMaxPadding[1i];
    let _e44 = FragmentUniformBlock.ChannelMinMaxPadding[2i];
    let _e49 = clamp(((_e38 - _e41) / max((_e44 - _e41), 0.0001f)), 0f, 1f);
    if (_e23 == 2i) {
        phi_81_ = 1f;
    } else {
        phi_81_ = _e18.w;
    }
    let _e53 = phi_81_;
    out_u002e_var_u002e_SV_Target0_ = (vec4<f32>(_e49, _e49, _e49, _e53) * _e17);
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
