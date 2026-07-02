struct type_u002e_FragmentUniformBlock {
    TextureSize: vec4<f32>,
    LightPositionRadiusHeight: vec4<f32>,
    LightColorIntensity: vec4<f32>,
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
var<private> global: vec4<f32>;
var<private> out_u002e_var_u002e_SV_Target0_: vec4<f32>;

fn fragment_main_1() {
    var phi_56_: bool;
    var phi_78_: f32;

    let _e21 = in_u002e_var_u002e_TEXCOORD0_1;
    let _e22 = in_u002e_var_u002e_TEXCOORD1_1;
    let _e23 = in_u002e_var_u002e_TEXCOORD4_1;
    let _e24 = global;
    phi_56_ = false;
    if (_e22.x < -1000f) {
        phi_56_ = (_e23.x < -1000f);
    }
    let _e30 = phi_56_;
    if _e30 {
        discard;
    }
    let _e33 = FragmentUniformBlock.LightPositionRadiusHeight;
    let _e34 = _e33.xy;
    let _e37 = FragmentUniformBlock.LightPositionRadiusHeight[2i];
    let _e40 = FragmentUniformBlock.LightPositionRadiusHeight[3i];
    let _e41 = (_e34 - _e24.xy);
    let _e45 = (select(0f, 1f, (_e37 < 0f)) > 0.5f);
    if _e45 {
        phi_78_ = 1f;
    } else {
        phi_78_ = clamp((1f - (length(_e41) / _e37)), 0f, 1f);
    }
    let _e50 = phi_78_;
    let _e51 = (_e50 * _e50);
    let _e52 = textureSample(Texture, Sampler, _e21);
    let _e62 = -(_e34);
    let _e75 = FragmentUniformBlock.LightColorIntensity;
    let _e79 = FragmentUniformBlock.LightColorIntensity[3i];
    let _e83 = clamp((((_e75.xyz * _e79) * _e51) * mix(1f, (0.12f + (clamp(dot(normalize(((_e52.xyz * 2f) - vec3<f32>(1f, 1f, 1f))), select(normalize(vec3<f32>(_e41.x, _e41.y, _e40)), normalize(vec3<f32>(_e62.x, _e62.y, _e40)), vec3(_e45))), 0f, 1f) * 2.15f)), _e52.w)), vec3<f32>(0f, 0f, 0f), vec3<f32>(1f, 1f, 1f));
    out_u002e_var_u002e_SV_Target0_ = vec4<f32>(_e83.x, _e83.y, _e83.z, _e51);
    return;
}

@fragment 
fn fragment_main(@location(0) in_u002e_var_u002e_TEXCOORD0_: vec2<f32>, @location(1) in_u002e_var_u002e_TEXCOORD1_: vec4<f32>, @location(2) in_u002e_var_u002e_TEXCOORD4_: vec4<f32>, @builtin(position) param: vec4<f32>) -> @location(0) vec4<f32> {
    in_u002e_var_u002e_TEXCOORD0_1 = in_u002e_var_u002e_TEXCOORD0_;
    in_u002e_var_u002e_TEXCOORD1_1 = in_u002e_var_u002e_TEXCOORD1_;
    in_u002e_var_u002e_TEXCOORD4_1 = in_u002e_var_u002e_TEXCOORD4_;
    global = param;
    fragment_main_1();
    let _e9 = out_u002e_var_u002e_SV_Target0_;
    return _e9;
}
