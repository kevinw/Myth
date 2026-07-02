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
    var phi_315_: vec2<f32>;
    var phi_349_: f32;
    var phi_352_: vec3<f32>;
    var phi_354_: i32;
    var phi_366_: bool;
    var phi_395_: bool;
    var phi_400_: bool;
    var phi_350_: f32;
    var phi_353_: vec3<f32>;
    var local: vec3<f32>;
    var phi_454_: vec4<f32>;

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
                phi_454_ = _e125;
                break;
            }
            let _e127 = textureSampleLevel(TileFieldTexture, TileFieldSampler, _e123, 0f);
            let _e131 = floor(((_e127.y * 255f) + 0.5f));
            let _e133 = floor((_e131 * 0.03125f));
            let _e134 = (_e133 * 0.25f);
            let _e138 = FragmentUniformBlock.TimePadding[3i];
            let _e148 = (_e99.zw + (vec2<f32>(_e94.x, (_e99.y - _e94.y)) / vec2(select(1f, _e138, (_e138 > 0f)))));
            let _e153 = FragmentUniformBlock.DitherAnchorOffsets[i32((_e131 % 32f))];
            let _e155 = (_e148 + _e153.xy);
            let _e157 = floor((_e155 * vec2<f32>(0.125f, 0.125f)));
            let _e159 = floor((_e115 * 7f));
            let _e160 = vec2(_e159);
            let _e182 = fract((sin(dot(floor(((floor((_e155 * vec2<f32>(0.0625f, 0.0625f))) + vec2((_e159 * 3f))) + vec2((_e133 * 4.75f)))), vec2<f32>(127.1f, 311.7f))) * 43758.547f));
            let _e214 = ((mix(42f, (_e111 * 1.75f), smoothstep(0f, 0.75f, _e106)) * mix(0.7f, 1.45f, fract((sin(dot(floor(((floor((_e155 * vec2<f32>(0.035714287f, 0.035714287f))) + vec2((_e159 * 1.7f))) + vec2((_e133 * 11.75f)))), vec2<f32>(127.1f, 311.7f))) * 43758.547f)))) + ((fract((sin(dot(floor(((_e157 + vec2((_e159 * 0.5f))) + vec2((_e133 * 17.75f)))), vec2<f32>(127.1f, 311.7f))) * 43758.547f)) - 0.5f) * 30f));
            let _e218 = normalize(normalize(_e116.yz));
            let _e223 = (_e148 - _e107.yz);
            let _e226 = (dot(_e223, _e218) / max(_e214, 1f));
            let _e229 = (dot(_e223, vec2<f32>(-(_e218.y), _e218.x)) / max(max((_e214 * mix(0.28f, 0.46f, _e182)), 10f), 1f));
            let _e259 = clamp((((_e106 * smoothstep(0.22f, 1f, _e106)) * 0.3f) + (((_e106 * (pow(smoothstep(0f, 0.95f, clamp((1f - sqrt(((_e226 * _e226) + (_e229 * _e229)))), 0f, 1f)), 0.58f) * mix(0.42f, 1f, fract((sin(dot(floor((floor((_e155 * vec2<f32>(0.05f, 0.05f))) + vec2((_e159 * 2f)))), vec2<f32>(127.1f, 311.7f))) * 43758.547f))))) * mix(0.28f, 0.9f, fract((sin(dot(floor(((_e157 + _e160) + vec2((_e127.x * 31f)))), vec2<f32>(127.1f, 311.7f))) * 43758.547f)))) * mix(0.45f, 1f, _e182))), 0f, 1f);
            if (_e259 <= 0.002f) {
                phi_454_ = _e125;
                break;
            }
            switch bitcast<i32>(0u) {
                default: {
                    if (_e134 < 0.125f) {
                        phi_315_ = normalize(vec2<f32>(1f, -1f));
                        break;
                    }
                    if (_e134 < 0.375f) {
                        phi_315_ = vec2<f32>(0f, 1f);
                        break;
                    }
                    if (_e134 < 0.625f) {
                        phi_315_ = normalize(vec2<f32>(1f, 1f));
                        break;
                    }
                    if (_e134 < 0.875f) {
                        phi_315_ = vec2<f32>(1f, 0f);
                        break;
                    }
                    phi_315_ = normalize(select(vec2<f32>(1f, -1f), vec2<f32>(-1f, 1f), vec2((_e127.x < 0.5f))));
                    break;
                }
            }
            let _e273 = phi_315_;
            let _e303 = step(0.5f, fract((sin(dot(floor(((_e157 + vec2((_e133 * 10.75f))) + vec2((_e159 * 0.25f)))), vec2<f32>(127.1f, 311.7f))) * 43758.547f)));
            let _e304 = _e125.xyz;
            phi_349_ = dot(_e304, vec3<f32>(0.2126f, 0.7152f, 0.0722f));
            phi_352_ = _e304;
            phi_354_ = -18i;
            loop {
                let _e307 = phi_349_;
                let _e309 = phi_352_;
                let _e311 = phi_354_;
                local = _e309;
                if (_e311 <= 18i) {
                    let _e313 = f32(_e311);
                    phi_366_ = true;
                    if !((abs(_e313) > floor(mix(2f, 18f, clamp((_e259 * mix(0.75f, 1.35f, fract((sin(dot(floor(vec2<f32>(floor((_e155.y * 0.33333334f)), (_e159 + floor((_e155.x * 0.025f))))), vec2<f32>(127.1f, 311.7f))) * 43758.547f)))), 0f, 1f))))) {
                        phi_366_ = (_e311 == 0i);
                    }
                    let _e319 = phi_366_;
                    if _e319 {
                        phi_350_ = _e307;
                        phi_353_ = _e309;
                        continue;
                    }
                    let _e336 = textureSampleLevel(Texture, Sampler, (_e123 + ((_e273 * (_e313 + (((fract((sin(dot(floor(((_e157 + vec2((_e313 * 17f))) + _e160)), vec2<f32>(127.1f, 311.7f))) * 43758.547f)) - 0.5f) * _e259) * 3f))) / _e100)), 0f);
                    let _e337 = _e336.xyz;
                    let _e338 = dot(_e337, vec3<f32>(0.2126f, 0.7152f, 0.0722f));
                    phi_395_ = false;
                    if (_e303 > 0.5f) {
                        phi_395_ = (_e338 > _e307);
                    }
                    let _e342 = phi_395_;
                    phi_400_ = false;
                    if (_e303 <= 0.5f) {
                        phi_400_ = (_e338 < _e307);
                    }
                    let _e346 = phi_400_;
                    let _e348 = select(true, _e346, !(_e342));
                    phi_350_ = select(_e307, _e338, _e348);
                    phi_353_ = select(_e309, _e337, vec3(_e348));
                    continue;
                } else {
                    break;
                }
                continuing {
                    let _e353 = phi_350_;
                    let _e355 = phi_353_;
                    phi_349_ = _e353;
                    phi_352_ = _e355;
                    phi_354_ = (_e311 + 1i);
                }
            }
            let _e389 = textureSampleLevel(Texture, Sampler, (_e123 + (((_e273.yx * vec2<f32>(-1f, 1f)) * (((fract((sin(dot(floor((floor((_e155 * vec2<f32>(0.041666668f, 0.041666668f))) + vec2((_e159 * 5f)))), vec2<f32>(127.1f, 311.7f))) * 43758.547f)) - 0.5f) * _e259) * 7f)) / _e100)), 0f);
            let _e395 = local;
            let _e398 = mix(_e304, mix(_e395, _e389.xyz, vec3(clamp((_e259 * 0.38f), 0f, 1f))), vec3((clamp((_e259 * 1.35f), 0f, 1f) * mix(0.78f, 1f, fract((sin(dot(floor(vec2<f32>(floor((_e155.y * 0.5f)), (_e159 + (_e133 * 5.75f)))), vec2<f32>(127.1f, 311.7f))) * 43758.547f))))));
            phi_454_ = vec4<f32>(_e398.x, _e398.y, _e398.z, _e125.w);
            break;
        }
    }
    let _e405 = phi_454_;
    out_u002e_var_u002e_SV_Target0_ = _e405;
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
