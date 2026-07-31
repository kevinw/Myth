struct Light {
    Position: vec2<f32>,
    Radius: f32,
    Height: f32,
    ColorIntensity: vec4<f32>,
}

struct type_u002e_StructuredBuffer_u002e_Light {
    member: array<Light>,
}

struct type_u002e_StructuredBuffer_u002e_v2float {
    member: array<vec2<f32>>,
}

struct type_u002e_Params {
    Width: u32,
    Height: u32,
    LightCount: u32,
    SunEnabled: u32,
    Camera: vec2<f32>,
    Zoom: f32,
    ShadowBias: f32,
    Ambient: vec4<f32>,
    SunDirection: vec2<f32>,
    SunStrength: f32,
    SoftShadowSamples: f32,
    SunColor: vec4<f32>,
    SunPerpendicular: vec2<f32>,
    SunPerpendicularMin: f32,
    SunPerpendicularMax: f32,
    SunAlongMin: f32,
    SunAlongMax: f32,
    SunSoftness: f32,
    MinimumSoftness: f32,
    SpriteShadowStrength: f32,
    SpriteShadowMinLength: f32,
    SpriteShadowMaxLength: f32,
    SpriteShadowPadding: f32,
    SporeFogTint: vec4<f32>,
    SporeFogDrift: vec2<f32>,
    Time: f32,
    SporeFogStrength: f32,
    SporeFogScale: f32,
    SporeFogContrast: f32,
    SporeFogPointStrength: f32,
    SporeFogSunStrength: f32,
}

@group(0) @binding(0)
var NormalTexture: texture_2d<f32>;
@group(0) @binding(100)
var NormalSampler: sampler;
@group(0) @binding(1)
var SporeNoise: texture_2d<f32>;
@group(0) @binding(101)
var SporeNoiseSampler: sampler;
@group(0) @binding(2)
var<storage> Lights: type_u002e_StructuredBuffer_u002e_Light;
@group(0) @binding(3)
var<storage> ShadowDepth: type_u002e_StructuredBuffer_u002e_v2float;
@group(1) @binding(0)
var BackgroundMask: texture_storage_2d<rgba8unorm,write>;
@group(1) @binding(1)
var ForegroundMask: texture_storage_2d<rgba8unorm,write>;
@group(1) @binding(2)
var SporeFogMask: texture_storage_2d<rgba8unorm,write>;
@group(2) @binding(0)
var<uniform> Params: type_u002e_Params;
var<private> global: vec3<u32>;

fn compute_main_1() {
    var weights: array<f32, 5>;
    var offsets: array<i32, 5>;
    var weights_1: array<f32, 5>;
    var offsets_1: array<i32, 5>;
    var phi_140_: bool;
    var phi_234_: f32;
    var phi_237_: u32;
    var phi_258_: f32;
    var phi_266_: vec3<f32>;
    var phi_267_: vec4<f32>;
    var phi_268_: vec4<f32>;
    var phi_270_: vec3<f32>;
    var phi_273_: bool;
    var phi_275_: bool;
    var phi_277_: vec4<f32>;
    var phi_279_: vec4<f32>;
    var phi_281_: u32;
    var phi_331_: f32;
    var phi_346_: i32;
    var phi_368_: bool;
    var phi_389_: f32;
    var phi_405_: f32;
    var phi_408_: u32;
    var phi_422_: i32;
    var phi_446_: bool;
    var phi_467_: f32;
    var phi_469_: bool;
    var phi_470_: bool;
    var phi_471_: f32;
    var phi_271_: vec3<f32>;
    var phi_274_: bool;
    var phi_276_: bool;
    var phi_278_: vec4<f32>;
    var phi_280_: vec4<f32>;
    var phi_527_: f32;
    var local: f32;
    var local_1: vec3<f32>;
    var local_2: vec4<f32>;
    var local_3: vec4<f32>;
    var local_4: f32;
    var local_5: f32;
    var local_6: f32;

    let _e80 = global;
    switch bitcast<i32>(0u) {
        default: {
            let _e83 = global[0i];
            let _e85 = Params.Width;
            phi_140_ = true;
            if !((_e83 >= _e85)) {
                let _e89 = global[1i];
                let _e91 = Params.Height;
                phi_140_ = (_e89 >= _e91);
            }
            let _e94 = phi_140_;
            if _e94 {
                break;
            }
            let _e95 = _e80.xy;
            let _e97 = (vec2<f32>(_e95) + vec2<f32>(0.5f, 0.5f));
            let _e100 = Params.Height;
            let _e105 = Params.Camera;
            let _e107 = Params.Zoom;
            let _e110 = (_e105 + (_e97 / vec2(_e107)));
            let _e111 = textureSampleLevel(NormalTexture, NormalSampler, (_e97 / vec2<f32>(f32(_e85), f32(_e100))), 0f);
            let _e116 = normalize(((_e111.xyz * 2f) - vec3<f32>(1f, 1f, 1f)));
            let _e118 = Params.Ambient;
            let _e120 = Params.SunEnabled;
            phi_266_ = vec3<f32>(0f, 0f, 0f);
            phi_267_ = _e118;
            phi_268_ = _e118;
            if (_e120 != 0u) {
                let _e123 = Params.SunDirection;
                let _e124 = -(_e123);
                let _e135 = Params.SunColor;
                let _e138 = Params.SunStrength;
                let _e139 = (_e135.xyz * _e138);
                let _e140 = (_e139 * mix(1f, (0.12f + (clamp(dot(_e116, normalize(vec3<f32>(_e124.x, _e124.y, 1f))), 0f, 1f) * 2.15f)), _e111.w));
                let _e144 = vec4<f32>(_e140.x, _e140.y, _e140.z, 1f);
                switch bitcast<i32>(0u) {
                    default: {
                        let _e147 = Params.SunPerpendicular;
                        let _e150 = Params.SunPerpendicularMin;
                        let _e153 = Params.SunPerpendicularMax;
                        let _e159 = i32(round((clamp(((dot(_e110, _e147) - _e150) / (_e153 - _e150)), 0f, 1f) * 511f)));
                        let _e161 = Params.SunSoftness;
                        if (_e161 <= 0f) {
                            let _e168 = ShadowDepth.member[bitcast<u32>((32768i + _e159))][0i];
                            let _e171 = Params.ShadowBias;
                            phi_258_ = select(0f, 1f, (dot(_e110, _e123) <= (_e168 + _e171)));
                            break;
                        }
                        offsets_1 = array<i32, 5>(-2i, -1i, 0i, 1i, 2i);
                        weights_1 = array<f32, 5>(0.1f, 0.2f, 0.4f, 0.2f, 0.1f);
                        phi_234_ = 0f;
                        phi_237_ = 0u;
                        loop {
                            let _e180 = phi_234_;
                            let _e182 = phi_237_;
                            local_4 = _e180;
                            if (_e182 < 5u) {
                                continue;
                            } else {
                                break;
                            }
                            continuing {
                                let _e185 = offsets_1[_e182];
                                let _e194 = ShadowDepth.member[bitcast<u32>((32768i + clamp((_e159 + (_e185 * max(1i, i32(round(_e161))))), 0i, 511i)))][0i];
                                let _e196 = weights_1[_e182];
                                let _e198 = Params.ShadowBias;
                                phi_234_ = (_e180 + (_e196 * select(0f, 1f, (dot(_e110, _e123) <= (_e194 + _e198)))));
                                phi_237_ = (_e182 + 1u);
                            }
                        }
                        let _e492 = local_4;
                        phi_258_ = _e492;
                        break;
                    }
                }
                let _e206 = phi_258_;
                let _e211 = Params.SporeFogSunStrength;
                phi_266_ = ((_e139 * _e211) * _e206);
                phi_267_ = (_e118 + _e144);
                phi_268_ = (_e118 + (_e144 * _e206));
            }
            let _e215 = phi_266_;
            let _e217 = phi_267_;
            let _e219 = phi_268_;
            phi_270_ = _e215;
            phi_273_ = false;
            phi_275_ = false;
            phi_277_ = _e217;
            phi_279_ = _e219;
            phi_281_ = 0u;
            loop {
                let _e221 = phi_270_;
                let _e223 = phi_273_;
                let _e225 = phi_275_;
                let _e227 = phi_277_;
                let _e229 = phi_279_;
                let _e231 = phi_281_;
                let _e233 = Params.LightCount;
                local_1 = _e221;
                local_2 = _e229;
                local_3 = _e227;
                if (_e231 < _e233) {
                    let _e237 = Lights.member[_e231];
                    let _e242 = (_e237.Position - _e110);
                    let _e243 = length(_e242);
                    let _e244 = (_e243 / _e237.Radius);
                    let _e246 = clamp((1f - _e244), 0f, 1f);
                    if (_e246 <= 0f) {
                        phi_271_ = _e221;
                        phi_274_ = _e223;
                        phi_276_ = _e225;
                        phi_278_ = _e227;
                        phi_280_ = _e229;
                        continue;
                    }
                    let _e248 = (_e246 * _e246);
                    let _e260 = (_e237.ColorIntensity.xyz * _e237.ColorIntensity.w);
                    let _e262 = ((_e260 * _e248) * mix(1f, (0.12f + (clamp(dot(_e116, normalize(vec3<f32>(_e242.x, _e242.y, _e237.Height))), 0f, 1f) * 2.15f)), _e111.w));
                    let _e266 = vec4<f32>(_e262.x, _e262.y, _e262.z, _e248);
                    let _e267 = -(_e242);
                    switch bitcast<i32>(0u) {
                        default: {
                            let _e271 = atan2(_e267.y, _e267.x);
                            phi_331_ = _e271;
                            if (_e271 < 0f) {
                                phi_331_ = (_e271 + 6.2831855f);
                            }
                            let _e275 = phi_331_;
                            let _e279 = i32(round(((_e275 * 81.48733f) - 0.5f)));
                            let _e281 = Params.SoftShadowSamples;
                            if (_e281 <= 0f) {
                                let _e283 = (_e279 % 512i);
                                phi_346_ = _e283;
                                if (_e283 < 0i) {
                                    phi_346_ = (_e283 + 512i);
                                }
                                let _e287 = phi_346_;
                                switch bitcast<i32>(0u) {
                                    default: {
                                        let _e294 = ShadowDepth.member[((_e231 * 512u) + bitcast<u32>(_e287))];
                                        let _e297 = Params.ShadowBias;
                                        if (_e243 > (_e294.x + _e297)) {
                                            phi_389_ = 0f;
                                            break;
                                        }
                                        phi_368_ = true;
                                        if !((_e294.y >= _e237.Radius)) {
                                            phi_368_ = (_e243 <= (_e294.y + _e297));
                                        }
                                        let _e306 = phi_368_;
                                        if _e306 {
                                            phi_389_ = 1f;
                                            break;
                                        }
                                        let _e311 = Params.SpriteShadowMinLength;
                                        let _e313 = Params.SpriteShadowMaxLength;
                                        let _e321 = Params.SpriteShadowStrength;
                                        phi_389_ = (1f - (clamp(_e321, 0f, 1f) * clamp((1f - ((_e243 - _e294.y) / max(mix(_e311, _e313, clamp((1f - (_e294.y / _e237.Radius)), 0f, 1f)), 0.0001f))), 0f, 1f)));
                                        break;
                                    }
                                }
                                let _e326 = phi_389_;
                                phi_469_ = _e223;
                                phi_470_ = _e225;
                                phi_471_ = _e326;
                                break;
                            }
                            let _e328 = Params.MinimumSoftness;
                            if _e225 {
                            } else {
                                offsets = array<i32, 5>(-2i, -1i, 0i, 1i, 2i);
                            }
                            if _e223 {
                            } else {
                                weights = array<f32, 5>(0.1f, 0.2f, 0.4f, 0.2f, 0.1f);
                            }
                            phi_405_ = 0f;
                            phi_408_ = 0u;
                            loop {
                                let _e338 = phi_405_;
                                let _e340 = phi_408_;
                                local_6 = _e338;
                                if (_e340 < 5u) {
                                    let _e343 = offsets[_e340];
                                    let _e346 = ((_e279 + (_e343 * max(1i, i32(round((_e281 * mix(_e328, 1f, clamp(_e244, 0f, 1f)))))))) % 512i);
                                    phi_422_ = _e346;
                                    if (_e346 < 0i) {
                                        phi_422_ = (_e346 + 512i);
                                    }
                                    let _e350 = phi_422_;
                                    let _e352 = weights[_e340];
                                    switch bitcast<i32>(0u) {
                                        default: {
                                            let _e359 = ShadowDepth.member[((_e231 * 512u) + bitcast<u32>(_e350))];
                                            let _e362 = Params.ShadowBias;
                                            if (_e243 > (_e359.x + _e362)) {
                                                phi_467_ = 0f;
                                                break;
                                            }
                                            phi_446_ = true;
                                            if !((_e359.y >= _e237.Radius)) {
                                                phi_446_ = (_e243 <= (_e359.y + _e362));
                                            }
                                            let _e371 = phi_446_;
                                            if _e371 {
                                                phi_467_ = 1f;
                                                break;
                                            }
                                            let _e376 = Params.SpriteShadowMinLength;
                                            let _e378 = Params.SpriteShadowMaxLength;
                                            let _e386 = Params.SpriteShadowStrength;
                                            phi_467_ = (1f - (clamp(_e386, 0f, 1f) * clamp((1f - ((_e243 - _e359.y) / max(mix(_e376, _e378, clamp((1f - (_e359.y / _e237.Radius)), 0f, 1f)), 0.0001f))), 0f, 1f)));
                                            break;
                                        }
                                    }
                                    let _e391 = phi_467_;
                                    local_5 = (_e338 + (_e352 * _e391));
                                    continue;
                                } else {
                                    break;
                                }
                                continuing {
                                    let _e508 = local_5;
                                    phi_405_ = _e508;
                                    phi_408_ = (_e340 + 1u);
                                }
                            }
                            phi_469_ = select(true, _e223, _e223);
                            phi_470_ = select(true, _e225, _e225);
                            let _e517 = local_6;
                            phi_471_ = _e517;
                            break;
                        }
                    }
                    let _e396 = phi_469_;
                    let _e398 = phi_470_;
                    let _e400 = phi_471_;
                    let _e406 = Params.SporeFogPointStrength;
                    phi_271_ = (_e221 + (((_e260 * _e246) * _e406) * _e400));
                    phi_274_ = _e396;
                    phi_276_ = _e398;
                    phi_278_ = (_e227 + _e266);
                    phi_280_ = (_e229 + (_e266 * _e400));
                    continue;
                } else {
                    break;
                }
                continuing {
                    let _e411 = phi_271_;
                    let _e413 = phi_274_;
                    let _e415 = phi_276_;
                    let _e417 = phi_278_;
                    let _e419 = phi_280_;
                    phi_270_ = _e411;
                    phi_273_ = _e413;
                    phi_275_ = _e415;
                    phi_277_ = _e417;
                    phi_279_ = _e419;
                    phi_281_ = (_e231 + 1u);
                }
            }
            let _e422 = Params.SporeFogTint;
            switch bitcast<i32>(0u) {
                default: {
                    let _e426 = Params.SporeFogStrength;
                    local = _e426;
                    if (_e426 <= 0f) {
                        phi_527_ = 0f;
                        break;
                    }
                    let _e429 = Params.SporeFogDrift;
                    let _e431 = Params.Time;
                    let _e432 = (_e429 * _e431);
                    let _e435 = Params.SporeFogScale;
                    let _e437 = textureSampleLevel(SporeNoise, SporeNoiseSampler, ((_e110 + _e432) * _e435), 0f);
                    let _e448 = textureSampleLevel(SporeNoise, SporeNoiseSampler, ((((vec2<f32>(-(_e110.y), _e110.x) - (_e432 * 0.4f)) * _e435) * 3.17f) + vec2<f32>(0.37f, 0.61f)), 0f);
                    let _e454 = Params.SporeFogContrast;
                    phi_527_ = clamp(((((_e437.x * mix(0.75f, 1.25f, _e448.y)) - 0.5f) * _e454) + 0.5f), 0f, 1f);
                    break;
                }
            }
            let _e459 = phi_527_;
            let _e461 = local;
            let _e465 = local_1;
            let _e469 = (vec3<f32>(1f, 1f, 1f) - exp(-((_e465 * (_e422.xyz * (_e459 * _e461))))));
            let _e471 = local_2;
            textureStore(BackgroundMask, _e95, clamp(_e471, vec4<f32>(0f, 0f, 0f, 0f), vec4<f32>(1f, 1f, 1f, 1f)));
            let _e474 = local_3;
            textureStore(ForegroundMask, _e95, clamp(_e474, vec4<f32>(0f, 0f, 0f, 0f), vec4<f32>(1f, 1f, 1f, 1f)));
            let _e476 = clamp(_e469, vec3<f32>(0f, 0f, 0f), vec3<f32>(1f, 1f, 1f));
            textureStore(SporeFogMask, _e95, vec4<f32>(_e476.x, _e476.y, _e476.z, clamp(max(max(_e469.x, _e469.y), _e469.z), 0f, 1f)));
            break;
        }
    }
    return;
}

@compute @workgroup_size(8, 8, 1)
fn compute_main(@builtin(global_invocation_id) param: vec3<u32>) {
    global = param;
    compute_main_1();
}
