var<private> in_u002e_var_u002e_TEXCOORD0_1: vec4<f32>;
var<private> in_u002e_var_u002e_TEXCOORD1_1: vec2<f32>;
var<private> in_u002e_var_u002e_TEXCOORD2_1: f32;
var<private> out_u002e_var_u002e_SV_Target0_: vec4<f32>;

fn fragment_main_1() {
    var phi_58_: vec4<f32>;

    let _e15 = in_u002e_var_u002e_TEXCOORD0_1;
    let _e16 = in_u002e_var_u002e_TEXCOORD1_1;
    let _e17 = in_u002e_var_u002e_TEXCOORD2_1;
    switch bitcast<i32>(0u) {
        default: {
            if (_e17 > 0.5f) {
                let _e22 = (length((_e16 - vec2<f32>(0.5f, 0.5f))) * 2f);
                let _e26 = (1f - smoothstep(0f, 0.38f, _e22));
                let _e30 = mix(_e15.xyz, vec3<f32>(1f, 1f, 1f), vec3((_e26 * 0.85f)));
                phi_58_ = vec4<f32>(_e30.x, _e30.y, _e30.z, (_e15.w * max(((1f - smoothstep(0.35f, 1f, _e22)) * 0.45f), _e26)));
                break;
            }
            phi_58_ = _e15;
            break;
        }
    }
    let _e40 = phi_58_;
    out_u002e_var_u002e_SV_Target0_ = _e40;
    return;
}

@fragment 
fn fragment_main(@location(0) in_u002e_var_u002e_TEXCOORD0_: vec4<f32>, @location(1) in_u002e_var_u002e_TEXCOORD1_: vec2<f32>, @location(2) in_u002e_var_u002e_TEXCOORD2_: f32) -> @location(0) vec4<f32> {
    in_u002e_var_u002e_TEXCOORD0_1 = in_u002e_var_u002e_TEXCOORD0_;
    in_u002e_var_u002e_TEXCOORD1_1 = in_u002e_var_u002e_TEXCOORD1_;
    in_u002e_var_u002e_TEXCOORD2_1 = in_u002e_var_u002e_TEXCOORD2_;
    fragment_main_1();
    let _e7 = out_u002e_var_u002e_SV_Target0_;
    return _e7;
}
