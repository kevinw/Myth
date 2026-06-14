struct type_u002e_FragmentUniformBlock {
    TextureSizeCamera: vec4<f32>,
    GlitchSettings: vec4<f32>,
    TimePadding: vec4<f32>,
}

@group(3) @binding(0) 
var<uniform> FragmentUniformBlock: type_u002e_FragmentUniformBlock;
@group(2) @binding(0) 
var Texture: texture_2d<f32>;
@group(2) @binding(100) 
var Sampler: sampler;
@group(2) @binding(1) 
var TileFieldTexture: texture_2d<f32>;
@group(2) @binding(101) 
var TileFieldSampler: sampler;
var<private> in_u002e_var_u002e_TEXCOORD0_1: vec2<f32>;
var<private> in_u002e_var_u002e_TEXCOORD1_1: vec4<f32>;
var<private> global: vec4<f32>;
var<private> out_u002e_var_u002e_SV_Target0_: vec4<f32>;

fn fragment_main_1() {
    var phi_287_: vec2<f32>;
    var phi_321_: f32;
    var phi_324_: vec3<f32>;
    var phi_326_: i32;
    var phi_338_: bool;
    var phi_365_: bool;
    var phi_370_: bool;
    var phi_322_: f32;
    var phi_325_: vec3<f32>;
    var local: vec3<f32>;
    var phi_422_: vec4<f32>;

    let _e85 = in_u002e_var_u002e_TEXCOORD0_1;
    let _e86 = in_u002e_var_u002e_TEXCOORD1_1;
    let _e87 = global;
    switch bitcast<i32>(0u) {
        default: {
            let _e90 = FragmentUniformBlock.TextureSizeCamera;
            let _e91 = _e90.xy;
            let _e96 = FragmentUniformBlock.GlitchSettings[0i];
            let _e97 = clamp(_e96, 0f, 1f);
            let _e98 = FragmentUniformBlock.GlitchSettings;
            let _e102 = FragmentUniformBlock.GlitchSettings[3i];
            let _e106 = FragmentUniformBlock.TimePadding[0i];
            let _e107 = FragmentUniformBlock.TimePadding;
            let _e114 = vec2<f32>(_e85.x, (1f - _e85.y));
            let _e115 = textureSampleLevel(Texture, Sampler, _e114, 0f);
            let _e116 = (_e115 * _e86);
            if (_e97 <= 0.001f) {
                phi_422_ = _e116;
                break;
            }
            let _e118 = textureSampleLevel(TileFieldTexture, TileFieldSampler, _e114, 0f);
            let _e123 = FragmentUniformBlock.TimePadding[3i];
            let _e129 = (_e90.zw + (_e87.xy / vec2(select(1f, _e123, (_e123 > 0f)))));
            let _e131 = floor((_e129 * vec2<f32>(0.125f, 0.125f)));
            let _e133 = floor((_e106 * 7f));
            let _e134 = vec2(_e133);
            let _e156 = fract((sin(dot(floor(((floor((_e129 * vec2<f32>(0.0625f, 0.0625f))) + vec2((_e133 * 3f))) + vec2((_e118.y * 19f)))), vec2<f32>(127.1f, 311.7f))) * 43758.547f));
            let _e188 = ((mix(42f, (_e102 * 1.75f), smoothstep(0f, 0.75f, _e97)) * mix(0.7f, 1.45f, fract((sin(dot(floor(((floor((_e129 * vec2<f32>(0.035714287f, 0.035714287f))) + vec2((_e133 * 1.7f))) + vec2((_e118.y * 47f)))), vec2<f32>(127.1f, 311.7f))) * 43758.547f)))) + ((fract((sin(dot(floor(((_e131 + vec2((_e133 * 0.5f))) + vec2((_e118.y * 71f)))), vec2<f32>(127.1f, 311.7f))) * 43758.547f)) - 0.5f) * 30f));
            let _e192 = normalize(normalize(_e107.yz));
            let _e197 = (_e129 - _e98.yz);
            let _e200 = (dot(_e197, _e192) / max(_e188, 1f));
            let _e203 = (dot(_e197, vec2<f32>(-(_e192.y), _e192.x)) / max(max((_e188 * mix(0.28f, 0.46f, _e156)), 10f), 1f));
            let _e233 = clamp((((_e97 * smoothstep(0.22f, 1f, _e97)) * 0.3f) + (((_e97 * (pow(smoothstep(0f, 0.95f, clamp((1f - sqrt(((_e200 * _e200) + (_e203 * _e203)))), 0f, 1f)), 0.58f) * mix(0.42f, 1f, fract((sin(dot(floor((floor((_e129 * vec2<f32>(0.05f, 0.05f))) + vec2((_e133 * 2f)))), vec2<f32>(127.1f, 311.7f))) * 43758.547f))))) * mix(0.28f, 0.9f, fract((sin(dot(floor(((_e131 + _e134) + vec2((_e118.x * 31f)))), vec2<f32>(127.1f, 311.7f))) * 43758.547f)))) * mix(0.45f, 1f, _e156))), 0f, 1f);
            if (_e233 <= 0.002f) {
                phi_422_ = _e116;
                break;
            }
            switch bitcast<i32>(0u) {
                default: {
                    if (_e118.y < 0.125f) {
                        phi_287_ = normalize(vec2<f32>(1f, -1f));
                        break;
                    }
                    if (_e118.y < 0.375f) {
                        phi_287_ = vec2<f32>(0f, 1f);
                        break;
                    }
                    if (_e118.y < 0.625f) {
                        phi_287_ = normalize(vec2<f32>(1f, 1f));
                        break;
                    }
                    if (_e118.y < 0.875f) {
                        phi_287_ = vec2<f32>(1f, 0f);
                        break;
                    }
                    phi_287_ = normalize(select(vec2<f32>(1f, -1f), vec2<f32>(-1f, 1f), vec2((_e118.x < 0.5f))));
                    break;
                }
            }
            let _e247 = phi_287_;
            let _e277 = step(0.5f, fract((sin(dot(floor(((_e131 + vec2((_e118.y * 43f))) + vec2((_e133 * 0.25f)))), vec2<f32>(127.1f, 311.7f))) * 43758.547f)));
            let _e278 = _e116.xyz;
            phi_321_ = dot(_e278, vec3<f32>(0.2126f, 0.7152f, 0.0722f));
            phi_324_ = _e278;
            phi_326_ = -18i;
            loop {
                let _e281 = phi_321_;
                let _e283 = phi_324_;
                let _e285 = phi_326_;
                local = _e283;
                if (_e285 <= 18i) {
                    let _e287 = f32(_e285);
                    phi_338_ = true;
                    if !((abs(_e287) > floor(mix(2f, 18f, clamp((_e233 * mix(0.75f, 1.35f, fract((sin(dot(floor(vec2<f32>(floor((_e129.y * 0.33333334f)), (_e133 + floor((_e129.x * 0.025f))))), vec2<f32>(127.1f, 311.7f))) * 43758.547f)))), 0f, 1f))))) {
                        phi_338_ = (_e285 == 0i);
                    }
                    let _e293 = phi_338_;
                    if _e293 {
                        phi_322_ = _e281;
                        phi_325_ = _e283;
                        continue;
                    }
                    let _e310 = textureSampleLevel(Texture, Sampler, (_e114 + ((_e247 * (_e287 + (((fract((sin(dot(floor(((_e131 + vec2((_e287 * 17f))) + _e134)), vec2<f32>(127.1f, 311.7f))) * 43758.547f)) - 0.5f) * _e233) * 3f))) / _e91)), 0f);
                    let _e311 = _e310.xyz;
                    let _e312 = dot(_e311, vec3<f32>(0.2126f, 0.7152f, 0.0722f));
                    phi_365_ = false;
                    if (_e277 > 0.5f) {
                        phi_365_ = (_e312 > _e281);
                    }
                    let _e316 = phi_365_;
                    phi_370_ = false;
                    if (_e277 <= 0.5f) {
                        phi_370_ = (_e312 < _e281);
                    }
                    let _e320 = phi_370_;
                    let _e322 = select(true, _e320, !(_e316));
                    phi_322_ = select(_e281, _e312, _e322);
                    phi_325_ = select(_e283, _e311, vec3(_e322));
                    continue;
                } else {
                    break;
                }
                continuing {
                    let _e327 = phi_322_;
                    let _e329 = phi_325_;
                    phi_321_ = _e327;
                    phi_324_ = _e329;
                    phi_326_ = (_e285 + 1i);
                }
            }
            let _e363 = textureSampleLevel(Texture, Sampler, (_e114 + (((_e247.yx * vec2<f32>(-1f, 1f)) * (((fract((sin(dot(floor((floor((_e129 * vec2<f32>(0.041666668f, 0.041666668f))) + vec2((_e133 * 5f)))), vec2<f32>(127.1f, 311.7f))) * 43758.547f)) - 0.5f) * _e233) * 7f)) / _e91)), 0f);
            let _e369 = local;
            let _e372 = mix(_e278, mix(_e369, _e363.xyz, vec3(clamp((_e233 * 0.38f), 0f, 1f))), vec3((clamp((_e233 * 1.35f), 0f, 1f) * mix(0.78f, 1f, fract((sin(dot(floor(vec2<f32>(floor((_e129.y * 0.5f)), (_e133 + (_e118.y * 23f)))), vec2<f32>(127.1f, 311.7f))) * 43758.547f))))));
            phi_422_ = vec4<f32>(_e372.x, _e372.y, _e372.z, _e116.w);
            break;
        }
    }
    let _e379 = phi_422_;
    out_u002e_var_u002e_SV_Target0_ = _e379;
    return;
}

@fragment 
fn fragment_main(@location(0) in_u002e_var_u002e_TEXCOORD0_: vec2<f32>, @location(1) in_u002e_var_u002e_TEXCOORD1_: vec4<f32>, @builtin(position) param: vec4<f32>) -> @location(0) vec4<f32> {
    in_u002e_var_u002e_TEXCOORD0_1 = in_u002e_var_u002e_TEXCOORD0_;
    in_u002e_var_u002e_TEXCOORD1_1 = in_u002e_var_u002e_TEXCOORD1_;
    global = param;
    fragment_main_1();
    let _e7 = out_u002e_var_u002e_SV_Target0_;
    return _e7;
}
