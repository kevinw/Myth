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
var<private> global: vec4<f32>;
var<private> out_u002e_var_u002e_SV_Target0_: vec4<f32>;

fn fragment_main_1() {
    var phi_69_: f32;

    let _e18 = in_u002e_var_u002e_TEXCOORD0_1;
    let _e19 = global;
    let _e20 = _e19.xy;
    let _e23 = FragmentUniformBlock.TextureSize[1i];
    let _e30 = FragmentUniformBlock.LightPositionRadiusHeight;
    let _e31 = _e30.xy;
    let _e34 = FragmentUniformBlock.LightPositionRadiusHeight[2i];
    let _e37 = FragmentUniformBlock.LightPositionRadiusHeight[3i];
    let _e38 = (_e31 - vec2<f32>(_e20.x, (_e23 - _e19.y)));
    let _e42 = (select(0f, 1f, (_e34 < 0f)) > 0.5f);
    if _e42 {
        phi_69_ = 1f;
    } else {
        phi_69_ = clamp((1f - (length(_e38) / _e34)), 0f, 1f);
    }
    let _e47 = phi_69_;
    let _e48 = (_e47 * _e47);
    let _e54 = textureSample(Texture, Sampler, vec2<f32>(_e18.x, (1f - _e18.y)));
    let _e64 = -(_e31);
    let _e77 = FragmentUniformBlock.LightColorIntensity;
    let _e81 = FragmentUniformBlock.LightColorIntensity[3i];
    let _e85 = clamp((((_e77.xyz * _e81) * _e48) * mix(1f, (0.12f + (clamp(dot(normalize(((_e54.xyz * 2f) - vec3<f32>(1f, 1f, 1f))), select(normalize(vec3<f32>(_e38.x, _e38.y, _e37)), normalize(vec3<f32>(_e64.x, _e64.y, _e37)), vec3(_e42))), 0f, 1f) * 2.15f)), _e54.w)), vec3<f32>(0f, 0f, 0f), vec3<f32>(1f, 1f, 1f));
    out_u002e_var_u002e_SV_Target0_ = vec4<f32>(_e85.x, _e85.y, _e85.z, _e48);
    return;
}

@fragment 
fn fragment_main(@location(0) in_u002e_var_u002e_TEXCOORD0_: vec2<f32>, @builtin(position) param: vec4<f32>) -> @location(0) vec4<f32> {
    in_u002e_var_u002e_TEXCOORD0_1 = in_u002e_var_u002e_TEXCOORD0_;
    global = param;
    fragment_main_1();
    let _e5 = out_u002e_var_u002e_SV_Target0_;
    return _e5;
}
