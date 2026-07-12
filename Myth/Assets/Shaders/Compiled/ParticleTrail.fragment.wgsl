var<private> in_u002e_var_u002e_TEXCOORD0_1: vec4<f32>;
var<private> in_u002e_var_u002e_TEXCOORD1_1: vec2<f32>;
var<private> out_u002e_var_u002e_SV_Target0_: vec4<f32>;

fn fragment_main_1() {
    let _e6 = in_u002e_var_u002e_TEXCOORD0_1;
    let _e7 = in_u002e_var_u002e_TEXCOORD1_1;
    out_u002e_var_u002e_SV_Target0_ = (_e6 * (1f - smoothstep(0.3f, 1f, abs(((_e7.y * 2f) - 1f)))));
    return;
}

@fragment 
fn fragment_main(@location(0) in_u002e_var_u002e_TEXCOORD0_: vec4<f32>, @location(1) in_u002e_var_u002e_TEXCOORD1_: vec2<f32>) -> @location(0) vec4<f32> {
    in_u002e_var_u002e_TEXCOORD0_1 = in_u002e_var_u002e_TEXCOORD0_;
    in_u002e_var_u002e_TEXCOORD1_1 = in_u002e_var_u002e_TEXCOORD1_;
    fragment_main_1();
    let _e5 = out_u002e_var_u002e_SV_Target0_;
    return _e5;
}
