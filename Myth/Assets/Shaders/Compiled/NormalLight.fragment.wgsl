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
    var phi_57_: bool;
    var phi_84_: f32;

    let _e22 = in_u002e_var_u002e_TEXCOORD0_1;
    let _e23 = in_u002e_var_u002e_TEXCOORD1_1;
    let _e24 = in_u002e_var_u002e_TEXCOORD4_1;
    let _e25 = global;
    phi_57_ = false;
    if (_e23.x < -1000f) {
        phi_57_ = (_e24.x < -1000f);
    }
    let _e31 = phi_57_;
    if _e31 {
        discard;
    }
    let _e34 = FragmentUniformBlock.TextureSize[1i];
    let _e40 = FragmentUniformBlock.LightPositionRadiusHeight;
    let _e41 = _e40.xy;
    let _e44 = FragmentUniformBlock.LightPositionRadiusHeight[2i];
    let _e47 = FragmentUniformBlock.LightPositionRadiusHeight[3i];
    let _e48 = (_e41 - vec2<f32>(_e25.x, (_e34 - _e25.y)));
    let _e52 = (select(0f, 1f, (_e44 < 0f)) > 0.5f);
    if _e52 {
        phi_84_ = 1f;
    } else {
        phi_84_ = clamp((1f - (length(_e48) / _e44)), 0f, 1f);
    }
    let _e57 = phi_84_;
    let _e58 = (_e57 * _e57);
    let _e64 = textureSample(Texture, Sampler, vec2<f32>(_e22.x, (1f - _e22.y)));
    let _e74 = -(_e41);
    let _e87 = FragmentUniformBlock.LightColorIntensity;
    let _e91 = FragmentUniformBlock.LightColorIntensity[3i];
    let _e95 = clamp((((_e87.xyz * _e91) * _e58) * mix(1f, (0.12f + (clamp(dot(normalize(((_e64.xyz * 2f) - vec3<f32>(1f, 1f, 1f))), select(normalize(vec3<f32>(_e48.x, _e48.y, _e47)), normalize(vec3<f32>(_e74.x, _e74.y, _e47)), vec3(_e52))), 0f, 1f) * 2.15f)), _e64.w)), vec3<f32>(0f, 0f, 0f), vec3<f32>(1f, 1f, 1f));
    out_u002e_var_u002e_SV_Target0_ = vec4<f32>(_e95.x, _e95.y, _e95.z, _e58);
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
