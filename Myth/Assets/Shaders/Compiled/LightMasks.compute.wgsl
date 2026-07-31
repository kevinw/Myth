struct Light {
    Position: vec2<f32>,
    Radius: f32,
    Height: f32,
    ColorIntensity: vec4<f32>,
    Cookie: vec4<f32>,
    CookieShape: vec4<f32>,
    Shadow: vec4<f32>,
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
    PointShadowPadding: f32,
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
    var phi_176_: bool;
    var phi_270_: f32;
    var phi_273_: u32;
    var phi_294_: f32;
    var phi_302_: vec3<f32>;
    var phi_303_: vec4<f32>;
    var phi_304_: vec4<f32>;
    var phi_306_: vec3<f32>;
    var phi_309_: bool;
    var phi_311_: bool;
    var phi_313_: vec4<f32>;
    var phi_315_: vec4<f32>;
    var phi_317_: u32;
    var phi_350_: bool;
    var phi_422_: f32;
    var phi_431_: f32;
    var phi_452_: f32;
    var phi_463_: f32;
    var phi_482_: f32;
    var phi_486_: f32;
    var phi_519_: f32;
    var phi_532_: bool;
    var phi_538_: bool;
    var phi_546_: i32;
    var phi_568_: bool;
    var phi_589_: f32;
    var phi_625_: i32;
    var phi_633_: f32;
    var phi_636_: u32;
    var phi_650_: i32;
    var phi_674_: bool;
    var phi_695_: f32;
    var phi_697_: bool;
    var phi_698_: bool;
    var phi_699_: f32;
    var phi_307_: vec3<f32>;
    var phi_310_: bool;
    var phi_312_: bool;
    var phi_314_: vec4<f32>;
    var phi_316_: vec4<f32>;
    var phi_755_: f32;
    var local: f32;
    var local_1: vec3<f32>;
    var local_2: vec4<f32>;
    var local_3: vec4<f32>;
    var local_4: f32;
    var local_5: f32;
    var local_6: f32;

    let _e116 = global;
    switch bitcast<i32>(0u) {
        default: {
            let _e119 = global[0i];
            let _e121 = Params.Width;
            phi_176_ = true;
            if !((_e119 >= _e121)) {
                let _e125 = global[1i];
                let _e127 = Params.Height;
                phi_176_ = (_e125 >= _e127);
            }
            let _e130 = phi_176_;
            if _e130 {
                break;
            }
            let _e131 = _e116.xy;
            let _e133 = (vec2<f32>(_e131) + vec2<f32>(0.5f, 0.5f));
            let _e136 = Params.Height;
            let _e141 = Params.Camera;
            let _e143 = Params.Zoom;
            let _e146 = (_e141 + (_e133 / vec2(_e143)));
            let _e147 = textureSampleLevel(NormalTexture, NormalSampler, (_e133 / vec2<f32>(f32(_e121), f32(_e136))), 0f);
            let _e152 = normalize(((_e147.xyz * 2f) - vec3<f32>(1f, 1f, 1f)));
            let _e154 = Params.Ambient;
            let _e156 = Params.SunEnabled;
            phi_302_ = vec3<f32>(0f, 0f, 0f);
            phi_303_ = _e154;
            phi_304_ = _e154;
            if (_e156 != 0u) {
                let _e159 = Params.SunDirection;
                let _e160 = -(_e159);
                let _e171 = Params.SunColor;
                let _e174 = Params.SunStrength;
                let _e175 = (_e171.xyz * _e174);
                let _e176 = (_e175 * mix(1f, (0.12f + (clamp(dot(_e152, normalize(vec3<f32>(_e160.x, _e160.y, 1f))), 0f, 1f) * 2.15f)), _e147.w));
                let _e180 = vec4<f32>(_e176.x, _e176.y, _e176.z, 1f);
                switch bitcast<i32>(0u) {
                    default: {
                        let _e183 = Params.SunPerpendicular;
                        let _e186 = Params.SunPerpendicularMin;
                        let _e189 = Params.SunPerpendicularMax;
                        let _e195 = i32(round((clamp(((dot(_e146, _e183) - _e186) / (_e189 - _e186)), 0f, 1f) * 511f)));
                        let _e197 = Params.SunSoftness;
                        if (_e197 <= 0f) {
                            let _e204 = ShadowDepth.member[bitcast<u32>((32768i + _e195))][0i];
                            let _e207 = Params.ShadowBias;
                            phi_294_ = select(0f, 1f, (dot(_e146, _e159) <= (_e204 + _e207)));
                            break;
                        }
                        offsets_1 = array<i32, 5>(-2i, -1i, 0i, 1i, 2i);
                        weights_1 = array<f32, 5>(0.1f, 0.2f, 0.4f, 0.2f, 0.1f);
                        phi_270_ = 0f;
                        phi_273_ = 0u;
                        loop {
                            let _e216 = phi_270_;
                            let _e218 = phi_273_;
                            local_4 = _e216;
                            if (_e218 < 5u) {
                                continue;
                            } else {
                                break;
                            }
                            continuing {
                                let _e221 = offsets_1[_e218];
                                let _e230 = ShadowDepth.member[bitcast<u32>((32768i + clamp((_e195 + (_e221 * max(1i, i32(round(_e197))))), 0i, 511i)))][0i];
                                let _e232 = weights_1[_e218];
                                let _e234 = Params.ShadowBias;
                                phi_270_ = (_e216 + (_e232 * select(0f, 1f, (dot(_e146, _e159) <= (_e230 + _e234)))));
                                phi_273_ = (_e218 + 1u);
                            }
                        }
                        let _e705 = local_4;
                        phi_294_ = _e705;
                        break;
                    }
                }
                let _e242 = phi_294_;
                let _e247 = Params.SporeFogSunStrength;
                phi_302_ = ((_e175 * _e247) * _e242);
                phi_303_ = (_e154 + _e180);
                phi_304_ = (_e154 + (_e180 * _e242));
            }
            let _e251 = phi_302_;
            let _e253 = phi_303_;
            let _e255 = phi_304_;
            phi_306_ = _e251;
            phi_309_ = false;
            phi_311_ = false;
            phi_313_ = _e253;
            phi_315_ = _e255;
            phi_317_ = 0u;
            loop {
                let _e257 = phi_306_;
                let _e259 = phi_309_;
                let _e261 = phi_311_;
                let _e263 = phi_313_;
                let _e265 = phi_315_;
                let _e267 = phi_317_;
                let _e269 = Params.LightCount;
                local_1 = _e257;
                local_2 = _e265;
                local_3 = _e263;
                if (_e267 < _e269) {
                    let _e273 = Lights.member[_e267];
                    let _e281 = (_e273.Position - _e146);
                    let _e282 = length(_e281);
                    let _e283 = (_e282 / _e273.Radius);
                    let _e286 = -(_e281);
                    switch bitcast<i32>(0u) {
                        default: {
                            let _e290 = i32(round(_e273.Cookie.x));
                            phi_350_ = true;
                            if !((_e290 == 0i)) {
                                phi_350_ = (_e273.CookieShape.x <= 0f);
                            }
                            let _e296 = phi_350_;
                            if _e296 {
                                phi_486_ = 1f;
                                break;
                            }
                            let _e300 = Params.Time;
                            let _e302 = (_e273.Cookie.y + (_e273.Cookie.z * _e300));
                            let _e303 = sin(_e302);
                            let _e304 = cos(_e302);
                            let _e319 = abs(_e273.Cookie.w);
                            let _e320 = max(_e319, 0.01f);
                            let _e321 = ((vec2<f32>(((_e304 * _e286.x) + (_e303 * _e286.y)), ((-(_e303) * _e286.x) + (_e304 * _e286.y))) / vec2(max(_e273.Radius, 0.0001f))) * _e320);
                            let _e322 = length(_e321);
                            let _e325 = atan2(_e321.y, _e321.x);
                            let _e327 = clamp(_e273.CookieShape.y, 0.001f, 0.5f);
                            if (_e290 == 1i) {
                                let _e404 = (abs((_e321 + vec2<f32>(0.28f, 0f))) - vec2<f32>(0.34f, 0.13f));
                                phi_482_ = (1f - smoothstep(-(_e327), _e327, min((length(((_e321 - vec2<f32>(0.18f, 0f)) * vec2<f32>(1f, 1.75f))) - 0.62f), (length(max(_e404, vec2<f32>(0f, 0f))) + min(max(_e404.x, _e404.y), 0f)))));
                            } else {
                                if (_e290 == 2i) {
                                    phi_463_ = smoothstep((0.45f - _e327), (0.55f + _e327), abs(sin(((_e325 * max(1f, (5f * _e319))) + (_e322 * 18f)))));
                                } else {
                                    if (_e290 == 3i) {
                                        phi_452_ = (1f - smoothstep(-(_e327), _e327, max((abs(_e321.y) - ((0.42f * (1f - (clamp(_e321.x, 0f, 1f) * 0.7f))) * (0.82f + (0.18f * sin(((_e321.x * 22f) + (_e325 * 3f))))))), max((-0.22f - _e321.x), (_e321.x - 1f)))));
                                    } else {
                                        if (_e290 == 4i) {
                                            let _e358 = cos(clamp((0.78f / _e320), 0.08f, 1.5f));
                                            phi_431_ = smoothstep((_e358 - _e327), (_e358 + _e327), (_e321.x / max(_e322, 0.0001f)));
                                        } else {
                                            phi_422_ = 1f;
                                            if (_e290 == 5i) {
                                                let _e333 = max(1f, _e319);
                                                phi_422_ = smoothstep(-(_e327), _e327, (((sin((((_e325 * 3.1f) * _e333) + (sin((_e325 * 7.3f)) * 1.2f))) * 0.65f) + (sin((((_e322 * 14f) * _e333) - (_e325 * 2f))) * 0.35f)) - 0.1f));
                                            }
                                            let _e353 = phi_422_;
                                            phi_431_ = _e353;
                                        }
                                        let _e363 = phi_431_;
                                        phi_452_ = _e363;
                                    }
                                    let _e385 = phi_452_;
                                    phi_463_ = _e385;
                                }
                                let _e397 = phi_463_;
                                phi_482_ = _e397;
                            }
                            let _e417 = phi_482_;
                            phi_486_ = mix(1f, _e417, clamp(_e273.CookieShape.x, 0f, 1f));
                            break;
                        }
                    }
                    let _e422 = phi_486_;
                    let _e423 = (clamp((1f - _e283), 0f, 1f) * _e422);
                    if (_e423 <= 0f) {
                        phi_307_ = _e257;
                        phi_310_ = _e259;
                        phi_312_ = _e261;
                        phi_314_ = _e263;
                        phi_316_ = _e265;
                        continue;
                    }
                    let _e425 = (_e423 * _e423);
                    let _e437 = (_e273.ColorIntensity.xyz * _e273.ColorIntensity.w);
                    let _e439 = ((_e437 * _e425) * mix(1f, (0.12f + (clamp(dot(_e152, normalize(vec3<f32>(_e281.x, _e281.y, _e273.Height))), 0f, 1f) * 2.15f)), _e147.w));
                    let _e443 = vec4<f32>(_e439.x, _e439.y, _e439.z, _e425);
                    switch bitcast<i32>(0u) {
                        default: {
                            let _e447 = atan2(_e286.y, _e286.x);
                            phi_519_ = _e447;
                            if (_e447 < 0f) {
                                phi_519_ = (_e447 + 6.2831855f);
                            }
                            let _e451 = phi_519_;
                            let _e453 = ((_e451 * 81.48733f) - 0.5f);
                            let _e455 = i32(round(_e453));
                            let _e457 = Params.SoftShadowSamples;
                            phi_532_ = true;
                            if !((_e457 <= 0f)) {
                                phi_532_ = (_e273.Shadow.x <= 0f);
                            }
                            let _e463 = phi_532_;
                            phi_538_ = true;
                            if !(_e463) {
                                phi_538_ = (_e273.Shadow.y <= 0f);
                            }
                            let _e468 = phi_538_;
                            if _e468 {
                                let _e469 = (_e455 % 512i);
                                phi_546_ = _e469;
                                if (_e469 < 0i) {
                                    phi_546_ = (_e469 + 512i);
                                }
                                let _e473 = phi_546_;
                                switch bitcast<i32>(0u) {
                                    default: {
                                        let _e480 = ShadowDepth.member[((_e267 * 512u) + bitcast<u32>(_e473))];
                                        let _e483 = Params.ShadowBias;
                                        if (_e282 > (_e480.x + _e483)) {
                                            phi_589_ = 0f;
                                            break;
                                        }
                                        phi_568_ = true;
                                        if !((_e480.y >= _e273.Radius)) {
                                            phi_568_ = (_e282 <= (_e480.y + _e483));
                                        }
                                        let _e492 = phi_568_;
                                        if _e492 {
                                            phi_589_ = 1f;
                                            break;
                                        }
                                        let _e497 = Params.SpriteShadowMinLength;
                                        let _e499 = Params.SpriteShadowMaxLength;
                                        let _e507 = Params.SpriteShadowStrength;
                                        phi_589_ = (1f - (clamp(_e507, 0f, 1f) * clamp((1f - ((_e282 - _e480.y) / max(mix(_e497, _e499, clamp((1f - (_e480.y / _e273.Radius)), 0f, 1f)), 0.0001f))), 0f, 1f)));
                                        break;
                                    }
                                }
                                let _e512 = phi_589_;
                                phi_697_ = _e259;
                                phi_698_ = _e261;
                                phi_699_ = _e512;
                                break;
                            }
                            let _e520 = max(1i, i32(round(((_e273.Shadow.x * _e273.Shadow.y) * clamp(_e283, 0f, 1f)))));
                            phi_625_ = _e455;
                            if (_e273.Shadow.z > 0f) {
                                let _e524 = Params.Time;
                                phi_625_ = i32(round((_e453 + (((fract((52.982918f * fract((dot(floor((_e286 * _e143)), vec2<f32>(0.06711056f, 0.00583715f)) + ((f32(_e267) + floor((_e524 * max(0f, _e273.Shadow.w)))) * 0.7548777f))))) - 0.5f) * _e273.Shadow.z) * f32(_e520)))));
                            }
                            let _e547 = phi_625_;
                            if _e261 {
                            } else {
                                offsets = array<i32, 5>(-2i, -1i, 0i, 1i, 2i);
                            }
                            if _e259 {
                            } else {
                                weights = array<f32, 5>(0.1f, 0.2f, 0.4f, 0.2f, 0.1f);
                            }
                            phi_633_ = 0f;
                            phi_636_ = 0u;
                            loop {
                                let _e551 = phi_633_;
                                let _e553 = phi_636_;
                                local_6 = _e551;
                                if (_e553 < 5u) {
                                    let _e556 = offsets[_e553];
                                    let _e559 = ((_e547 + (_e556 * _e520)) % 512i);
                                    phi_650_ = _e559;
                                    if (_e559 < 0i) {
                                        phi_650_ = (_e559 + 512i);
                                    }
                                    let _e563 = phi_650_;
                                    let _e565 = weights[_e553];
                                    switch bitcast<i32>(0u) {
                                        default: {
                                            let _e572 = ShadowDepth.member[((_e267 * 512u) + bitcast<u32>(_e563))];
                                            let _e575 = Params.ShadowBias;
                                            if (_e282 > (_e572.x + _e575)) {
                                                phi_695_ = 0f;
                                                break;
                                            }
                                            phi_674_ = true;
                                            if !((_e572.y >= _e273.Radius)) {
                                                phi_674_ = (_e282 <= (_e572.y + _e575));
                                            }
                                            let _e584 = phi_674_;
                                            if _e584 {
                                                phi_695_ = 1f;
                                                break;
                                            }
                                            let _e589 = Params.SpriteShadowMinLength;
                                            let _e591 = Params.SpriteShadowMaxLength;
                                            let _e599 = Params.SpriteShadowStrength;
                                            phi_695_ = (1f - (clamp(_e599, 0f, 1f) * clamp((1f - ((_e282 - _e572.y) / max(mix(_e589, _e591, clamp((1f - (_e572.y / _e273.Radius)), 0f, 1f)), 0.0001f))), 0f, 1f)));
                                            break;
                                        }
                                    }
                                    let _e604 = phi_695_;
                                    local_5 = (_e551 + (_e565 * _e604));
                                    continue;
                                } else {
                                    break;
                                }
                                continuing {
                                    let _e731 = local_5;
                                    phi_633_ = _e731;
                                    phi_636_ = (_e553 + 1u);
                                }
                            }
                            phi_697_ = select(true, _e259, _e259);
                            phi_698_ = select(true, _e261, _e261);
                            let _e740 = local_6;
                            phi_699_ = _e740;
                            break;
                        }
                    }
                    let _e609 = phi_697_;
                    let _e611 = phi_698_;
                    let _e613 = phi_699_;
                    let _e619 = Params.SporeFogPointStrength;
                    phi_307_ = (_e257 + (((_e437 * _e423) * _e619) * _e613));
                    phi_310_ = _e609;
                    phi_312_ = _e611;
                    phi_314_ = (_e263 + _e443);
                    phi_316_ = (_e265 + (_e443 * _e613));
                    continue;
                } else {
                    break;
                }
                continuing {
                    let _e624 = phi_307_;
                    let _e626 = phi_310_;
                    let _e628 = phi_312_;
                    let _e630 = phi_314_;
                    let _e632 = phi_316_;
                    phi_306_ = _e624;
                    phi_309_ = _e626;
                    phi_311_ = _e628;
                    phi_313_ = _e630;
                    phi_315_ = _e632;
                    phi_317_ = (_e267 + 1u);
                }
            }
            let _e635 = Params.SporeFogTint;
            switch bitcast<i32>(0u) {
                default: {
                    let _e639 = Params.SporeFogStrength;
                    local = _e639;
                    if (_e639 <= 0f) {
                        phi_755_ = 0f;
                        break;
                    }
                    let _e642 = Params.SporeFogDrift;
                    let _e644 = Params.Time;
                    let _e645 = (_e642 * _e644);
                    let _e648 = Params.SporeFogScale;
                    let _e650 = textureSampleLevel(SporeNoise, SporeNoiseSampler, ((_e146 + _e645) * _e648), 0f);
                    let _e661 = textureSampleLevel(SporeNoise, SporeNoiseSampler, ((((vec2<f32>(-(_e146.y), _e146.x) - (_e645 * 0.4f)) * _e648) * 3.17f) + vec2<f32>(0.37f, 0.61f)), 0f);
                    let _e667 = Params.SporeFogContrast;
                    phi_755_ = clamp(((((_e650.x * mix(0.75f, 1.25f, _e661.y)) - 0.5f) * _e667) + 0.5f), 0f, 1f);
                    break;
                }
            }
            let _e672 = phi_755_;
            let _e674 = local;
            let _e678 = local_1;
            let _e682 = (vec3<f32>(1f, 1f, 1f) - exp(-((_e678 * (_e635.xyz * (_e672 * _e674))))));
            let _e684 = local_2;
            textureStore(BackgroundMask, _e131, clamp(_e684, vec4<f32>(0f, 0f, 0f, 0f), vec4<f32>(1f, 1f, 1f, 1f)));
            let _e687 = local_3;
            textureStore(ForegroundMask, _e131, clamp(_e687, vec4<f32>(0f, 0f, 0f, 0f), vec4<f32>(1f, 1f, 1f, 1f)));
            let _e689 = clamp(_e682, vec3<f32>(0f, 0f, 0f), vec3<f32>(1f, 1f, 1f));
            textureStore(SporeFogMask, _e131, vec4<f32>(_e689.x, _e689.y, _e689.z, clamp(max(max(_e682.x, _e682.y), _e682.z), 0f, 1f)));
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
