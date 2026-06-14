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
var<private> in_u002e_var_u002e_TEXCOORD4_1: vec4<f32>;
var<private> global: vec4<f32>;
var<private> out_u002e_var_u002e_SV_Target0_: vec4<f32>;

fn fragment_main_1() {
    var phi_294_: vec2<f32>;
    var phi_328_: f32;
    var phi_331_: vec3<f32>;
    var phi_333_: i32;
    var phi_345_: bool;
    var phi_372_: bool;
    var phi_377_: bool;
    var phi_329_: f32;
    var phi_332_: vec3<f32>;
    var local: vec3<f32>;
    var phi_429_: vec4<f32>;

    let _e87 = in_u002e_var_u002e_TEXCOORD0_1;
    let _e88 = in_u002e_var_u002e_TEXCOORD1_1;
    let _e89 = in_u002e_var_u002e_TEXCOORD4_1;
    let _e90 = global;
    switch bitcast<i32>(0u) {
        default: {
            if (_e89.x < -1000f) {
                discard;
            }
            let _e95 = FragmentUniformBlock.TextureSizeCamera;
            let _e96 = _e95.xy;
            let _e101 = FragmentUniformBlock.GlitchSettings[0i];
            let _e102 = clamp(_e101, 0f, 1f);
            let _e103 = FragmentUniformBlock.GlitchSettings;
            let _e107 = FragmentUniformBlock.GlitchSettings[3i];
            let _e111 = FragmentUniformBlock.TimePadding[0i];
            let _e112 = FragmentUniformBlock.TimePadding;
            let _e119 = vec2<f32>(_e87.x, (1f - _e87.y));
            let _e120 = textureSampleLevel(Texture, Sampler, _e119, 0f);
            let _e121 = (_e120 * _e88);
            if (_e102 <= 0.001f) {
                phi_429_ = _e121;
                break;
            }
            let _e123 = textureSampleLevel(TileFieldTexture, TileFieldSampler, _e119, 0f);
            let _e128 = FragmentUniformBlock.TimePadding[3i];
            let _e134 = (_e95.zw + (_e90.xy / vec2(select(1f, _e128, (_e128 > 0f)))));
            let _e136 = floor((_e134 * vec2<f32>(0.125f, 0.125f)));
            let _e138 = floor((_e111 * 7f));
            let _e139 = vec2(_e138);
            let _e161 = fract((sin(dot(floor(((floor((_e134 * vec2<f32>(0.0625f, 0.0625f))) + vec2((_e138 * 3f))) + vec2((_e123.y * 19f)))), vec2<f32>(127.1f, 311.7f))) * 43758.547f));
            let _e193 = ((mix(42f, (_e107 * 1.75f), smoothstep(0f, 0.75f, _e102)) * mix(0.7f, 1.45f, fract((sin(dot(floor(((floor((_e134 * vec2<f32>(0.035714287f, 0.035714287f))) + vec2((_e138 * 1.7f))) + vec2((_e123.y * 47f)))), vec2<f32>(127.1f, 311.7f))) * 43758.547f)))) + ((fract((sin(dot(floor(((_e136 + vec2((_e138 * 0.5f))) + vec2((_e123.y * 71f)))), vec2<f32>(127.1f, 311.7f))) * 43758.547f)) - 0.5f) * 30f));
            let _e197 = normalize(normalize(_e112.yz));
            let _e202 = (_e134 - _e103.yz);
            let _e205 = (dot(_e202, _e197) / max(_e193, 1f));
            let _e208 = (dot(_e202, vec2<f32>(-(_e197.y), _e197.x)) / max(max((_e193 * mix(0.28f, 0.46f, _e161)), 10f), 1f));
            let _e238 = clamp((((_e102 * smoothstep(0.22f, 1f, _e102)) * 0.3f) + (((_e102 * (pow(smoothstep(0f, 0.95f, clamp((1f - sqrt(((_e205 * _e205) + (_e208 * _e208)))), 0f, 1f)), 0.58f) * mix(0.42f, 1f, fract((sin(dot(floor((floor((_e134 * vec2<f32>(0.05f, 0.05f))) + vec2((_e138 * 2f)))), vec2<f32>(127.1f, 311.7f))) * 43758.547f))))) * mix(0.28f, 0.9f, fract((sin(dot(floor(((_e136 + _e139) + vec2((_e123.x * 31f)))), vec2<f32>(127.1f, 311.7f))) * 43758.547f)))) * mix(0.45f, 1f, _e161))), 0f, 1f);
            if (_e238 <= 0.002f) {
                phi_429_ = _e121;
                break;
            }
            switch bitcast<i32>(0u) {
                default: {
                    if (_e123.y < 0.125f) {
                        phi_294_ = normalize(vec2<f32>(1f, -1f));
                        break;
                    }
                    if (_e123.y < 0.375f) {
                        phi_294_ = vec2<f32>(0f, 1f);
                        break;
                    }
                    if (_e123.y < 0.625f) {
                        phi_294_ = normalize(vec2<f32>(1f, 1f));
                        break;
                    }
                    if (_e123.y < 0.875f) {
                        phi_294_ = vec2<f32>(1f, 0f);
                        break;
                    }
                    phi_294_ = normalize(select(vec2<f32>(1f, -1f), vec2<f32>(-1f, 1f), vec2((_e123.x < 0.5f))));
                    break;
                }
            }
            let _e252 = phi_294_;
            let _e282 = step(0.5f, fract((sin(dot(floor(((_e136 + vec2((_e123.y * 43f))) + vec2((_e138 * 0.25f)))), vec2<f32>(127.1f, 311.7f))) * 43758.547f)));
            let _e283 = _e121.xyz;
            phi_328_ = dot(_e283, vec3<f32>(0.2126f, 0.7152f, 0.0722f));
            phi_331_ = _e283;
            phi_333_ = -18i;
            loop {
                let _e286 = phi_328_;
                let _e288 = phi_331_;
                let _e290 = phi_333_;
                local = _e288;
                if (_e290 <= 18i) {
                    let _e292 = f32(_e290);
                    phi_345_ = true;
                    if !((abs(_e292) > floor(mix(2f, 18f, clamp((_e238 * mix(0.75f, 1.35f, fract((sin(dot(floor(vec2<f32>(floor((_e134.y * 0.33333334f)), (_e138 + floor((_e134.x * 0.025f))))), vec2<f32>(127.1f, 311.7f))) * 43758.547f)))), 0f, 1f))))) {
                        phi_345_ = (_e290 == 0i);
                    }
                    let _e298 = phi_345_;
                    if _e298 {
                        phi_329_ = _e286;
                        phi_332_ = _e288;
                        continue;
                    }
                    let _e315 = textureSampleLevel(Texture, Sampler, (_e119 + ((_e252 * (_e292 + (((fract((sin(dot(floor(((_e136 + vec2((_e292 * 17f))) + _e139)), vec2<f32>(127.1f, 311.7f))) * 43758.547f)) - 0.5f) * _e238) * 3f))) / _e96)), 0f);
                    let _e316 = _e315.xyz;
                    let _e317 = dot(_e316, vec3<f32>(0.2126f, 0.7152f, 0.0722f));
                    phi_372_ = false;
                    if (_e282 > 0.5f) {
                        phi_372_ = (_e317 > _e286);
                    }
                    let _e321 = phi_372_;
                    phi_377_ = false;
                    if (_e282 <= 0.5f) {
                        phi_377_ = (_e317 < _e286);
                    }
                    let _e325 = phi_377_;
                    let _e327 = select(true, _e325, !(_e321));
                    phi_329_ = select(_e286, _e317, _e327);
                    phi_332_ = select(_e288, _e316, vec3(_e327));
                    continue;
                } else {
                    break;
                }
                continuing {
                    let _e332 = phi_329_;
                    let _e334 = phi_332_;
                    phi_328_ = _e332;
                    phi_331_ = _e334;
                    phi_333_ = (_e290 + 1i);
                }
            }
            let _e368 = textureSampleLevel(Texture, Sampler, (_e119 + (((_e252.yx * vec2<f32>(-1f, 1f)) * (((fract((sin(dot(floor((floor((_e134 * vec2<f32>(0.041666668f, 0.041666668f))) + vec2((_e138 * 5f)))), vec2<f32>(127.1f, 311.7f))) * 43758.547f)) - 0.5f) * _e238) * 7f)) / _e96)), 0f);
            let _e374 = local;
            let _e377 = mix(_e283, mix(_e374, _e368.xyz, vec3(clamp((_e238 * 0.38f), 0f, 1f))), vec3((clamp((_e238 * 1.35f), 0f, 1f) * mix(0.78f, 1f, fract((sin(dot(floor(vec2<f32>(floor((_e134.y * 0.5f)), (_e138 + (_e123.y * 23f)))), vec2<f32>(127.1f, 311.7f))) * 43758.547f))))));
            phi_429_ = vec4<f32>(_e377.x, _e377.y, _e377.z, _e121.w);
            break;
        }
    }
    let _e384 = phi_429_;
    out_u002e_var_u002e_SV_Target0_ = _e384;
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
