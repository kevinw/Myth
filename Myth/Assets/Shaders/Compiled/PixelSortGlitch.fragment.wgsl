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
    var phi_311_: vec2<f32>;
    var phi_345_: f32;
    var phi_348_: vec3<f32>;
    var phi_350_: i32;
    var phi_362_: bool;
    var phi_389_: bool;
    var phi_394_: bool;
    var phi_346_: f32;
    var phi_349_: vec3<f32>;
    var local: vec3<f32>;
    var phi_446_: vec4<f32>;

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
            let _e123 = vec2<f32>(_e91.x, (1f - _e91.y));
            let _e124 = textureSampleLevel(Texture, Sampler, _e123, 0f);
            let _e125 = (_e124 * _e92);
            if (_e106 <= 0.001f) {
                phi_446_ = _e125;
                break;
            }
            let _e127 = textureSampleLevel(TileFieldTexture, TileFieldSampler, _e123, 0f);
            let _e131 = floor(((_e127.y * 255f) + 0.5f));
            let _e133 = floor((_e131 * 0.03125f));
            let _e134 = (_e133 * 0.25f);
            let _e138 = FragmentUniformBlock.TimePadding[3i];
            let _e144 = (_e99.zw + (_e94.xy / vec2(select(1f, _e138, (_e138 > 0f)))));
            let _e149 = FragmentUniformBlock.DitherAnchorOffsets[i32((_e131 % 32f))];
            let _e151 = (_e144 + _e149.xy);
            let _e153 = floor((_e151 * vec2<f32>(0.125f, 0.125f)));
            let _e155 = floor((_e115 * 7f));
            let _e156 = vec2(_e155);
            let _e178 = fract((sin(dot(floor(((floor((_e151 * vec2<f32>(0.0625f, 0.0625f))) + vec2((_e155 * 3f))) + vec2((_e133 * 4.75f)))), vec2<f32>(127.1f, 311.7f))) * 43758.547f));
            let _e210 = ((mix(42f, (_e111 * 1.75f), smoothstep(0f, 0.75f, _e106)) * mix(0.7f, 1.45f, fract((sin(dot(floor(((floor((_e151 * vec2<f32>(0.035714287f, 0.035714287f))) + vec2((_e155 * 1.7f))) + vec2((_e133 * 11.75f)))), vec2<f32>(127.1f, 311.7f))) * 43758.547f)))) + ((fract((sin(dot(floor(((_e153 + vec2((_e155 * 0.5f))) + vec2((_e133 * 17.75f)))), vec2<f32>(127.1f, 311.7f))) * 43758.547f)) - 0.5f) * 30f));
            let _e214 = normalize(normalize(_e116.yz));
            let _e219 = (_e144 - _e107.yz);
            let _e222 = (dot(_e219, _e214) / max(_e210, 1f));
            let _e225 = (dot(_e219, vec2<f32>(-(_e214.y), _e214.x)) / max(max((_e210 * mix(0.28f, 0.46f, _e178)), 10f), 1f));
            let _e255 = clamp((((_e106 * smoothstep(0.22f, 1f, _e106)) * 0.3f) + (((_e106 * (pow(smoothstep(0f, 0.95f, clamp((1f - sqrt(((_e222 * _e222) + (_e225 * _e225)))), 0f, 1f)), 0.58f) * mix(0.42f, 1f, fract((sin(dot(floor((floor((_e151 * vec2<f32>(0.05f, 0.05f))) + vec2((_e155 * 2f)))), vec2<f32>(127.1f, 311.7f))) * 43758.547f))))) * mix(0.28f, 0.9f, fract((sin(dot(floor(((_e153 + _e156) + vec2((_e127.x * 31f)))), vec2<f32>(127.1f, 311.7f))) * 43758.547f)))) * mix(0.45f, 1f, _e178))), 0f, 1f);
            if (_e255 <= 0.002f) {
                phi_446_ = _e125;
                break;
            }
            switch bitcast<i32>(0u) {
                default: {
                    if (_e134 < 0.125f) {
                        phi_311_ = normalize(vec2<f32>(1f, -1f));
                        break;
                    }
                    if (_e134 < 0.375f) {
                        phi_311_ = vec2<f32>(0f, 1f);
                        break;
                    }
                    if (_e134 < 0.625f) {
                        phi_311_ = normalize(vec2<f32>(1f, 1f));
                        break;
                    }
                    if (_e134 < 0.875f) {
                        phi_311_ = vec2<f32>(1f, 0f);
                        break;
                    }
                    phi_311_ = normalize(select(vec2<f32>(1f, -1f), vec2<f32>(-1f, 1f), vec2((_e127.x < 0.5f))));
                    break;
                }
            }
            let _e269 = phi_311_;
            let _e299 = step(0.5f, fract((sin(dot(floor(((_e153 + vec2((_e133 * 10.75f))) + vec2((_e155 * 0.25f)))), vec2<f32>(127.1f, 311.7f))) * 43758.547f)));
            let _e300 = _e125.xyz;
            phi_345_ = dot(_e300, vec3<f32>(0.2126f, 0.7152f, 0.0722f));
            phi_348_ = _e300;
            phi_350_ = -18i;
            loop {
                let _e303 = phi_345_;
                let _e305 = phi_348_;
                let _e307 = phi_350_;
                local = _e305;
                if (_e307 <= 18i) {
                    let _e309 = f32(_e307);
                    phi_362_ = true;
                    if !((abs(_e309) > floor(mix(2f, 18f, clamp((_e255 * mix(0.75f, 1.35f, fract((sin(dot(floor(vec2<f32>(floor((_e151.y * 0.33333334f)), (_e155 + floor((_e151.x * 0.025f))))), vec2<f32>(127.1f, 311.7f))) * 43758.547f)))), 0f, 1f))))) {
                        phi_362_ = (_e307 == 0i);
                    }
                    let _e315 = phi_362_;
                    if _e315 {
                        phi_346_ = _e303;
                        phi_349_ = _e305;
                        continue;
                    }
                    let _e332 = textureSampleLevel(Texture, Sampler, (_e123 + ((_e269 * (_e309 + (((fract((sin(dot(floor(((_e153 + vec2((_e309 * 17f))) + _e156)), vec2<f32>(127.1f, 311.7f))) * 43758.547f)) - 0.5f) * _e255) * 3f))) / _e100)), 0f);
                    let _e333 = _e332.xyz;
                    let _e334 = dot(_e333, vec3<f32>(0.2126f, 0.7152f, 0.0722f));
                    phi_389_ = false;
                    if (_e299 > 0.5f) {
                        phi_389_ = (_e334 > _e303);
                    }
                    let _e338 = phi_389_;
                    phi_394_ = false;
                    if (_e299 <= 0.5f) {
                        phi_394_ = (_e334 < _e303);
                    }
                    let _e342 = phi_394_;
                    let _e344 = select(true, _e342, !(_e338));
                    phi_346_ = select(_e303, _e334, _e344);
                    phi_349_ = select(_e305, _e333, vec3(_e344));
                    continue;
                } else {
                    break;
                }
                continuing {
                    let _e349 = phi_346_;
                    let _e351 = phi_349_;
                    phi_345_ = _e349;
                    phi_348_ = _e351;
                    phi_350_ = (_e307 + 1i);
                }
            }
            let _e385 = textureSampleLevel(Texture, Sampler, (_e123 + (((_e269.yx * vec2<f32>(-1f, 1f)) * (((fract((sin(dot(floor((floor((_e151 * vec2<f32>(0.041666668f, 0.041666668f))) + vec2((_e155 * 5f)))), vec2<f32>(127.1f, 311.7f))) * 43758.547f)) - 0.5f) * _e255) * 7f)) / _e100)), 0f);
            let _e391 = local;
            let _e394 = mix(_e300, mix(_e391, _e385.xyz, vec3(clamp((_e255 * 0.38f), 0f, 1f))), vec3((clamp((_e255 * 1.35f), 0f, 1f) * mix(0.78f, 1f, fract((sin(dot(floor(vec2<f32>(floor((_e151.y * 0.5f)), (_e155 + (_e133 * 5.75f)))), vec2<f32>(127.1f, 311.7f))) * 43758.547f))))));
            phi_446_ = vec4<f32>(_e394.x, _e394.y, _e394.z, _e125.w);
            break;
        }
    }
    let _e401 = phi_446_;
    out_u002e_var_u002e_SV_Target0_ = _e401;
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
