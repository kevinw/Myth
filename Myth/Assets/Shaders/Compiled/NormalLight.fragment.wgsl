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
    let _e32 = _e25.xy;
    let _e35 = FragmentUniformBlock.TextureSize[1i];
    let _e42 = FragmentUniformBlock.LightPositionRadiusHeight;
    let _e43 = _e42.xy;
    let _e46 = FragmentUniformBlock.LightPositionRadiusHeight[2i];
    let _e49 = FragmentUniformBlock.LightPositionRadiusHeight[3i];
    let _e50 = (_e43 - vec2<f32>(_e32.x, (_e35 - _e25.y)));
    let _e54 = (select(0f, 1f, (_e46 < 0f)) > 0.5f);
    if _e54 {
        phi_84_ = 1f;
    } else {
        phi_84_ = clamp((1f - (length(_e50) / _e46)), 0f, 1f);
    }
    let _e59 = phi_84_;
    let _e60 = (_e59 * _e59);
    let _e66 = textureSample(Texture, Sampler, vec2<f32>(_e22.x, (1f - _e22.y)));
    let _e76 = -(_e43);
    let _e89 = FragmentUniformBlock.LightColorIntensity;
    let _e93 = FragmentUniformBlock.LightColorIntensity[3i];
    let _e97 = clamp((((_e89.xyz * _e93) * _e60) * mix(1f, (0.12f + (clamp(dot(normalize(((_e66.xyz * 2f) - vec3<f32>(1f, 1f, 1f))), select(normalize(vec3<f32>(_e50.x, _e50.y, _e49)), normalize(vec3<f32>(_e76.x, _e76.y, _e49)), vec3(_e54))), 0f, 1f) * 2.15f)), _e66.w)), vec3<f32>(0f, 0f, 0f), vec3<f32>(1f, 1f, 1f));
    out_u002e_var_u002e_SV_Target0_ = vec4<f32>(_e97.x, _e97.y, _e97.z, _e60);
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
