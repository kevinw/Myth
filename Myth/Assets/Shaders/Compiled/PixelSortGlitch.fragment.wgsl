struct type_u002e_FragmentUniformBlock {
    TextureSizeCamera: vec4<f32>,
    GlitchSettings: vec4<f32>,
    TimePadding: vec4<f32>,
    DitherAnchorOffsets: array<vec4<f32>, 32>,
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
    var phi_308_: vec2<f32>;
    var phi_342_: f32;
    var phi_345_: vec3<f32>;
    var phi_347_: i32;
    var phi_359_: bool;
    var phi_388_: bool;
    var phi_393_: bool;
    var phi_343_: f32;
    var phi_346_: vec3<f32>;
    var local: vec3<f32>;
    var phi_447_: vec4<f32>;

    let _e91 = in_u002e_var_u002e_TEXCOORD0_1;
    let _e92 = in_u002e_var_u002e_TEXCOORD1_1;
    let _e93 = in_u002e_var_u002e_TEXCOORD4_1;
    let _e94 = global;
    switch bitcast<i32>(0u) {
        default: {
            if (_e93.x < -1000f) {
                discard;
            }
            let _e99 = FragmentUniformBlock.TextureSizeCamera;
            let _e100 = _e99.xy;
            let _e105 = FragmentUniformBlock.GlitchSettings[0i];
            let _e106 = clamp(_e105, 0f, 1f);
            let _e107 = FragmentUniformBlock.GlitchSettings;
            let _e111 = FragmentUniformBlock.GlitchSettings[3i];
            let _e115 = FragmentUniformBlock.TimePadding[0i];
            let _e116 = FragmentUniformBlock.TimePadding;
            let _e119 = textureSampleLevel(Texture, Sampler, _e91, 0f);
            let _e120 = (_e119 * _e92);
            if (_e106 <= 0.001f) {
                phi_447_ = _e120;
                break;
            }
            let _e122 = textureSampleLevel(TileFieldTexture, TileFieldSampler, _e91, 0f);
            let _e126 = floor(((_e122.y * 255f) + 0.5f));
            let _e128 = floor((_e126 * 0.03125f));
            let _e129 = (_e128 * 0.25f);
            let _e133 = FragmentUniformBlock.TimePadding[3i];
            let _e139 = (_e99.zw + (_e94.xy / vec2(select(1f, _e133, (_e133 > 0f)))));
            let _e144 = FragmentUniformBlock.DitherAnchorOffsets[i32((_e126 % 32f))];
            let _e146 = (_e139 + _e144.xy);
            let _e148 = floor((_e146 * vec2<f32>(0.125f, 0.125f)));
            let _e150 = floor((_e115 * 7f));
            let _e151 = vec2(_e150);
            let _e173 = fract((sin(dot(floor(((floor((_e146 * vec2<f32>(0.0625f, 0.0625f))) + vec2((_e150 * 3f))) + vec2((_e128 * 4.75f)))), vec2<f32>(127.1f, 311.7f))) * 43758.547f));
            let _e205 = ((mix(42f, (_e111 * 1.75f), smoothstep(0f, 0.75f, _e106)) * mix(0.7f, 1.45f, fract((sin(dot(floor(((floor((_e146 * vec2<f32>(0.035714287f, 0.035714287f))) + vec2((_e150 * 1.7f))) + vec2((_e128 * 11.75f)))), vec2<f32>(127.1f, 311.7f))) * 43758.547f)))) + ((fract((sin(dot(floor(((_e148 + vec2((_e150 * 0.5f))) + vec2((_e128 * 17.75f)))), vec2<f32>(127.1f, 311.7f))) * 43758.547f)) - 0.5f) * 30f));
            let _e209 = normalize(normalize(_e116.yz));
            let _e214 = (_e139 - _e107.yz);
            let _e217 = (dot(_e214, _e209) / max(_e205, 1f));
            let _e220 = (dot(_e214, vec2<f32>(-(_e209.y), _e209.x)) / max(max((_e205 * mix(0.28f, 0.46f, _e173)), 10f), 1f));
            let _e250 = clamp((((_e106 * smoothstep(0.22f, 1f, _e106)) * 0.3f) + (((_e106 * (pow(smoothstep(0f, 0.95f, clamp((1f - sqrt(((_e217 * _e217) + (_e220 * _e220)))), 0f, 1f)), 0.58f) * mix(0.42f, 1f, fract((sin(dot(floor((floor((_e146 * vec2<f32>(0.05f, 0.05f))) + vec2((_e150 * 2f)))), vec2<f32>(127.1f, 311.7f))) * 43758.547f))))) * mix(0.28f, 0.9f, fract((sin(dot(floor(((_e148 + _e151) + vec2((_e122.x * 31f)))), vec2<f32>(127.1f, 311.7f))) * 43758.547f)))) * mix(0.45f, 1f, _e173))), 0f, 1f);
            if (_e250 <= 0.002f) {
                phi_447_ = _e120;
                break;
            }
            switch bitcast<i32>(0u) {
                default: {
                    if (_e129 < 0.125f) {
                        phi_308_ = normalize(vec2<f32>(1f, -1f));
                        break;
                    }
                    if (_e129 < 0.375f) {
                        phi_308_ = vec2<f32>(0f, 1f);
                        break;
                    }
                    if (_e129 < 0.625f) {
                        phi_308_ = normalize(vec2<f32>(1f, 1f));
                        break;
                    }
                    if (_e129 < 0.875f) {
                        phi_308_ = vec2<f32>(1f, 0f);
                        break;
                    }
                    phi_308_ = normalize(select(vec2<f32>(1f, -1f), vec2<f32>(-1f, 1f), vec2((_e122.x < 0.5f))));
                    break;
                }
            }
            let _e264 = phi_308_;
            let _e294 = step(0.5f, fract((sin(dot(floor(((_e148 + vec2((_e128 * 10.75f))) + vec2((_e150 * 0.25f)))), vec2<f32>(127.1f, 311.7f))) * 43758.547f)));
            let _e295 = _e120.xyz;
            phi_342_ = dot(_e295, vec3<f32>(0.2126f, 0.7152f, 0.0722f));
            phi_345_ = _e295;
            phi_347_ = -18i;
            loop {
                let _e298 = phi_342_;
                let _e300 = phi_345_;
                let _e302 = phi_347_;
                local = _e300;
                if (_e302 <= 18i) {
                    let _e304 = f32(_e302);
                    phi_359_ = true;
                    if !((abs(_e304) > floor(mix(2f, 18f, clamp((_e250 * mix(0.75f, 1.35f, fract((sin(dot(floor(vec2<f32>(floor((_e146.y * 0.33333334f)), (_e150 + floor((_e146.x * 0.025f))))), vec2<f32>(127.1f, 311.7f))) * 43758.547f)))), 0f, 1f))))) {
                        phi_359_ = (_e302 == 0i);
                    }
                    let _e310 = phi_359_;
                    if _e310 {
                        phi_343_ = _e298;
                        phi_346_ = _e300;
                        continue;
                    }
                    let _e327 = textureSampleLevel(Texture, Sampler, (_e91 + ((_e264 * (_e304 + (((fract((sin(dot(floor(((_e148 + vec2((_e304 * 17f))) + _e151)), vec2<f32>(127.1f, 311.7f))) * 43758.547f)) - 0.5f) * _e250) * 3f))) / _e100)), 0f);
                    let _e328 = _e327.xyz;
                    let _e329 = dot(_e328, vec3<f32>(0.2126f, 0.7152f, 0.0722f));
                    phi_388_ = false;
                    if (_e294 > 0.5f) {
                        phi_388_ = (_e329 > _e298);
                    }
                    let _e333 = phi_388_;
                    phi_393_ = false;
                    if (_e294 <= 0.5f) {
                        phi_393_ = (_e329 < _e298);
                    }
                    let _e337 = phi_393_;
                    let _e339 = select(true, _e337, !(_e333));
                    phi_343_ = select(_e298, _e329, _e339);
                    phi_346_ = select(_e300, _e328, vec3(_e339));
                    continue;
                } else {
                    break;
                }
                continuing {
                    let _e344 = phi_343_;
                    let _e346 = phi_346_;
                    phi_342_ = _e344;
                    phi_345_ = _e346;
                    phi_347_ = (_e302 + 1i);
                }
            }
            let _e380 = textureSampleLevel(Texture, Sampler, (_e91 + (((_e264.yx * vec2<f32>(-1f, 1f)) * (((fract((sin(dot(floor((floor((_e146 * vec2<f32>(0.041666668f, 0.041666668f))) + vec2((_e150 * 5f)))), vec2<f32>(127.1f, 311.7f))) * 43758.547f)) - 0.5f) * _e250) * 7f)) / _e100)), 0f);
            let _e386 = local;
            let _e389 = mix(_e295, mix(_e386, _e380.xyz, vec3(clamp((_e250 * 0.38f), 0f, 1f))), vec3((clamp((_e250 * 1.35f), 0f, 1f) * mix(0.78f, 1f, fract((sin(dot(floor(vec2<f32>(floor((_e146.y * 0.5f)), (_e150 + (_e128 * 5.75f)))), vec2<f32>(127.1f, 311.7f))) * 43758.547f))))));
            phi_447_ = vec4<f32>(_e389.x, _e389.y, _e389.z, _e120.w);
            break;
        }
    }
    let _e396 = phi_447_;
    out_u002e_var_u002e_SV_Target0_ = _e396;
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
