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
    var phi_177_: bool;
    var phi_278_: f32;
    var phi_281_: u32;
    var phi_302_: f32;
    var phi_310_: vec3<f32>;
    var phi_311_: vec4<f32>;
    var phi_312_: vec4<f32>;
    var phi_314_: vec3<f32>;
    var phi_317_: bool;
    var phi_319_: bool;
    var phi_321_: vec4<f32>;
    var phi_323_: vec4<f32>;
    var phi_325_: u32;
    var phi_358_: bool;
    var phi_430_: f32;
    var phi_439_: f32;
    var phi_460_: f32;
    var phi_471_: f32;
    var phi_490_: f32;
    var phi_494_: f32;
    var phi_527_: f32;
    var phi_540_: bool;
    var phi_546_: bool;
    var phi_554_: i32;
    var phi_576_: bool;
    var phi_597_: f32;
    var phi_633_: i32;
    var phi_641_: f32;
    var phi_644_: u32;
    var phi_658_: i32;
    var phi_682_: bool;
    var phi_703_: f32;
    var phi_705_: bool;
    var phi_706_: bool;
    var phi_707_: f32;
    var phi_315_: vec3<f32>;
    var phi_318_: bool;
    var phi_320_: bool;
    var phi_322_: vec4<f32>;
    var phi_324_: vec4<f32>;
    var phi_763_: f32;
    var local: f32;
    var local_1: vec3<f32>;
    var local_2: vec4<f32>;
    var local_3: vec4<f32>;
    var local_4: f32;
    var local_5: f32;
    var local_6: f32;

    let _e117 = global;
    switch bitcast<i32>(0u) {
        default: {
            let _e120 = global[0i];
            let _e122 = Params.Width;
            phi_177_ = true;
            if !((_e120 >= _e122)) {
                let _e126 = global[1i];
                let _e128 = Params.Height;
                phi_177_ = (_e126 >= _e128);
            }
            let _e131 = phi_177_;
            if _e131 {
                break;
            }
            let _e132 = _e117.xy;
            let _e134 = (vec2<f32>(_e132) + vec2<f32>(0.5f, 0.5f));
            let _e137 = Params.Height;
            let _e142 = Params.Camera;
            let _e144 = Params.Zoom;
            let _e147 = (_e142 + (_e134 / vec2(_e144)));
            let _e148 = textureSampleLevel(NormalTexture, NormalSampler, (_e134 / vec2<f32>(f32(_e122), f32(_e137))), 0f);
            let _e152 = ((_e148.xy * 2f) - vec2<f32>(1f, 1f));
            let _e160 = normalize(vec3<f32>(_e152.x, _e152.y, sqrt(clamp((1f - dot(_e152, _e152)), 0f, 1f))));
            let _e162 = Params.Ambient;
            let _e164 = Params.SunEnabled;
            phi_310_ = vec3<f32>(0f, 0f, 0f);
            phi_311_ = _e162;
            phi_312_ = _e162;
            if (_e164 != 0u) {
                let _e167 = Params.SunDirection;
                let _e168 = -(_e167);
                let _e179 = Params.SunColor;
                let _e182 = Params.SunStrength;
                let _e183 = (_e179.xyz * _e182);
                let _e184 = (_e183 * mix(1f, (0.12f + (clamp(dot(_e160, normalize(vec3<f32>(_e168.x, _e168.y, 1f))), 0f, 1f) * 2.15f)), _e148.w));
                let _e188 = vec4<f32>(_e184.x, _e184.y, _e184.z, 1f);
                switch bitcast<i32>(0u) {
                    default: {
                        let _e191 = Params.SunPerpendicular;
                        let _e194 = Params.SunPerpendicularMin;
                        let _e197 = Params.SunPerpendicularMax;
                        let _e203 = i32(round((clamp(((dot(_e147, _e191) - _e194) / (_e197 - _e194)), 0f, 1f) * 511f)));
                        let _e205 = Params.SunSoftness;
                        if (_e205 <= 0f) {
                            let _e212 = ShadowDepth.member[bitcast<u32>((32768i + _e203))][0i];
                            let _e215 = Params.ShadowBias;
                            phi_302_ = select(0f, 1f, (dot(_e147, _e167) <= (_e212 + _e215)));
                            break;
                        }
                        offsets_1 = array<i32, 5>(-2i, -1i, 0i, 1i, 2i);
                        weights_1 = array<f32, 5>(0.1f, 0.2f, 0.4f, 0.2f, 0.1f);
                        phi_278_ = 0f;
                        phi_281_ = 0u;
                        loop {
                            let _e224 = phi_278_;
                            let _e226 = phi_281_;
                            local_4 = _e224;
                            if (_e226 < 5u) {
                                continue;
                            } else {
                                break;
                            }
                            continuing {
                                let _e229 = offsets_1[_e226];
                                let _e238 = ShadowDepth.member[bitcast<u32>((32768i + clamp((_e203 + (_e229 * max(1i, i32(round(_e205))))), 0i, 511i)))][0i];
                                let _e240 = weights_1[_e226];
                                let _e242 = Params.ShadowBias;
                                phi_278_ = (_e224 + (_e240 * select(0f, 1f, (dot(_e147, _e167) <= (_e238 + _e242)))));
                                phi_281_ = (_e226 + 1u);
                            }
                        }
                        let _e713 = local_4;
                        phi_302_ = _e713;
                        break;
                    }
                }
                let _e250 = phi_302_;
                let _e255 = Params.SporeFogSunStrength;
                phi_310_ = ((_e183 * _e255) * _e250);
                phi_311_ = (_e162 + _e188);
                phi_312_ = (_e162 + (_e188 * _e250));
            }
            let _e259 = phi_310_;
            let _e261 = phi_311_;
            let _e263 = phi_312_;
            phi_314_ = _e259;
            phi_317_ = false;
            phi_319_ = false;
            phi_321_ = _e261;
            phi_323_ = _e263;
            phi_325_ = 0u;
            loop {
                let _e265 = phi_314_;
                let _e267 = phi_317_;
                let _e269 = phi_319_;
                let _e271 = phi_321_;
                let _e273 = phi_323_;
                let _e275 = phi_325_;
                let _e277 = Params.LightCount;
                local_1 = _e265;
                local_2 = _e273;
                local_3 = _e271;
                if (_e275 < _e277) {
                    let _e281 = Lights.member[_e275];
                    let _e289 = (_e281.Position - _e147);
                    let _e290 = length(_e289);
                    let _e291 = (_e290 / _e281.Radius);
                    let _e294 = -(_e289);
                    switch bitcast<i32>(0u) {
                        default: {
                            let _e298 = i32(round(_e281.Cookie.x));
                            phi_358_ = true;
                            if !((_e298 == 0i)) {
                                phi_358_ = (_e281.CookieShape.x <= 0f);
                            }
                            let _e304 = phi_358_;
                            if _e304 {
                                phi_494_ = 1f;
                                break;
                            }
                            let _e308 = Params.Time;
                            let _e310 = (_e281.Cookie.y + (_e281.Cookie.z * _e308));
                            let _e311 = sin(_e310);
                            let _e312 = cos(_e310);
                            let _e327 = abs(_e281.Cookie.w);
                            let _e328 = max(_e327, 0.01f);
                            let _e329 = ((vec2<f32>(((_e312 * _e294.x) + (_e311 * _e294.y)), ((-(_e311) * _e294.x) + (_e312 * _e294.y))) / vec2(max(_e281.Radius, 0.0001f))) * _e328);
                            let _e330 = length(_e329);
                            let _e333 = atan2(_e329.y, _e329.x);
                            let _e335 = clamp(_e281.CookieShape.y, 0.001f, 0.5f);
                            if (_e298 == 1i) {
                                let _e412 = (abs((_e329 + vec2<f32>(0.28f, 0f))) - vec2<f32>(0.34f, 0.13f));
                                phi_490_ = (1f - smoothstep(-(_e335), _e335, min((length(((_e329 - vec2<f32>(0.18f, 0f)) * vec2<f32>(1f, 1.75f))) - 0.62f), (length(max(_e412, vec2<f32>(0f, 0f))) + min(max(_e412.x, _e412.y), 0f)))));
                            } else {
                                if (_e298 == 2i) {
                                    phi_471_ = smoothstep((0.45f - _e335), (0.55f + _e335), abs(sin(((_e333 * max(1f, (5f * _e327))) + (_e330 * 18f)))));
                                } else {
                                    if (_e298 == 3i) {
                                        phi_460_ = (1f - smoothstep(-(_e335), _e335, max((abs(_e329.y) - ((0.42f * (1f - (clamp(_e329.x, 0f, 1f) * 0.7f))) * (0.82f + (0.18f * sin(((_e329.x * 22f) + (_e333 * 3f))))))), max((-0.22f - _e329.x), (_e329.x - 1f)))));
                                    } else {
                                        if (_e298 == 4i) {
                                            let _e366 = cos(clamp((0.78f / _e328), 0.08f, 1.5f));
                                            phi_439_ = smoothstep((_e366 - _e335), (_e366 + _e335), (_e329.x / max(_e330, 0.0001f)));
                                        } else {
                                            phi_430_ = 1f;
                                            if (_e298 == 5i) {
                                                let _e341 = max(1f, _e327);
                                                phi_430_ = smoothstep(-(_e335), _e335, (((sin((((_e333 * 3.1f) * _e341) + (sin((_e333 * 7.3f)) * 1.2f))) * 0.65f) + (sin((((_e330 * 14f) * _e341) - (_e333 * 2f))) * 0.35f)) - 0.1f));
                                            }
                                            let _e361 = phi_430_;
                                            phi_439_ = _e361;
                                        }
                                        let _e371 = phi_439_;
                                        phi_460_ = _e371;
                                    }
                                    let _e393 = phi_460_;
                                    phi_471_ = _e393;
                                }
                                let _e405 = phi_471_;
                                phi_490_ = _e405;
                            }
                            let _e425 = phi_490_;
                            phi_494_ = mix(1f, _e425, clamp(_e281.CookieShape.x, 0f, 1f));
                            break;
                        }
                    }
                    let _e430 = phi_494_;
                    let _e431 = (clamp((1f - _e291), 0f, 1f) * _e430);
                    if (_e431 <= 0f) {
                        phi_315_ = _e265;
                        phi_318_ = _e267;
                        phi_320_ = _e269;
                        phi_322_ = _e271;
                        phi_324_ = _e273;
                        continue;
                    }
                    let _e433 = (_e431 * _e431);
                    let _e445 = (_e281.ColorIntensity.xyz * _e281.ColorIntensity.w);
                    let _e447 = ((_e445 * _e433) * mix(1f, (0.12f + (clamp(dot(_e160, normalize(vec3<f32>(_e289.x, _e289.y, _e281.Height))), 0f, 1f) * 2.15f)), _e148.w));
                    let _e451 = vec4<f32>(_e447.x, _e447.y, _e447.z, _e433);
                    switch bitcast<i32>(0u) {
                        default: {
                            let _e455 = atan2(_e294.y, _e294.x);
                            phi_527_ = _e455;
                            if (_e455 < 0f) {
                                phi_527_ = (_e455 + 6.2831855f);
                            }
                            let _e459 = phi_527_;
                            let _e461 = ((_e459 * 81.48733f) - 0.5f);
                            let _e463 = i32(round(_e461));
                            let _e465 = Params.SoftShadowSamples;
                            phi_540_ = true;
                            if !((_e465 <= 0f)) {
                                phi_540_ = (_e281.Shadow.x <= 0f);
                            }
                            let _e471 = phi_540_;
                            phi_546_ = true;
                            if !(_e471) {
                                phi_546_ = (_e281.Shadow.y <= 0f);
                            }
                            let _e476 = phi_546_;
                            if _e476 {
                                let _e477 = (_e463 % 512i);
                                phi_554_ = _e477;
                                if (_e477 < 0i) {
                                    phi_554_ = (_e477 + 512i);
                                }
                                let _e481 = phi_554_;
                                switch bitcast<i32>(0u) {
                                    default: {
                                        let _e488 = ShadowDepth.member[((_e275 * 512u) + bitcast<u32>(_e481))];
                                        let _e491 = Params.ShadowBias;
                                        if (_e290 > (_e488.x + _e491)) {
                                            phi_597_ = 0f;
                                            break;
                                        }
                                        phi_576_ = true;
                                        if !((_e488.y >= _e281.Radius)) {
                                            phi_576_ = (_e290 <= (_e488.y + _e491));
                                        }
                                        let _e500 = phi_576_;
                                        if _e500 {
                                            phi_597_ = 1f;
                                            break;
                                        }
                                        let _e505 = Params.SpriteShadowMinLength;
                                        let _e507 = Params.SpriteShadowMaxLength;
                                        let _e515 = Params.SpriteShadowStrength;
                                        phi_597_ = (1f - (clamp(_e515, 0f, 1f) * clamp((1f - ((_e290 - _e488.y) / max(mix(_e505, _e507, clamp((1f - (_e488.y / _e281.Radius)), 0f, 1f)), 0.0001f))), 0f, 1f)));
                                        break;
                                    }
                                }
                                let _e520 = phi_597_;
                                phi_705_ = _e267;
                                phi_706_ = _e269;
                                phi_707_ = _e520;
                                break;
                            }
                            let _e528 = max(1i, i32(round(((_e281.Shadow.x * _e281.Shadow.y) * clamp(_e291, 0f, 1f)))));
                            phi_633_ = _e463;
                            if (_e281.Shadow.z > 0f) {
                                let _e532 = Params.Time;
                                phi_633_ = i32(round((_e461 + (((fract((52.982918f * fract((dot(floor((_e294 * _e144)), vec2<f32>(0.06711056f, 0.00583715f)) + ((f32(_e275) + floor((_e532 * max(0f, _e281.Shadow.w)))) * 0.7548777f))))) - 0.5f) * _e281.Shadow.z) * f32(_e528)))));
                            }
                            let _e555 = phi_633_;
                            if _e269 {
                            } else {
                                offsets = array<i32, 5>(-2i, -1i, 0i, 1i, 2i);
                            }
                            if _e267 {
                            } else {
                                weights = array<f32, 5>(0.1f, 0.2f, 0.4f, 0.2f, 0.1f);
                            }
                            phi_641_ = 0f;
                            phi_644_ = 0u;
                            loop {
                                let _e559 = phi_641_;
                                let _e561 = phi_644_;
                                local_6 = _e559;
                                if (_e561 < 5u) {
                                    let _e564 = offsets[_e561];
                                    let _e567 = ((_e555 + (_e564 * _e528)) % 512i);
                                    phi_658_ = _e567;
                                    if (_e567 < 0i) {
                                        phi_658_ = (_e567 + 512i);
                                    }
                                    let _e571 = phi_658_;
                                    let _e573 = weights[_e561];
                                    switch bitcast<i32>(0u) {
                                        default: {
                                            let _e580 = ShadowDepth.member[((_e275 * 512u) + bitcast<u32>(_e571))];
                                            let _e583 = Params.ShadowBias;
                                            if (_e290 > (_e580.x + _e583)) {
                                                phi_703_ = 0f;
                                                break;
                                            }
                                            phi_682_ = true;
                                            if !((_e580.y >= _e281.Radius)) {
                                                phi_682_ = (_e290 <= (_e580.y + _e583));
                                            }
                                            let _e592 = phi_682_;
                                            if _e592 {
                                                phi_703_ = 1f;
                                                break;
                                            }
                                            let _e597 = Params.SpriteShadowMinLength;
                                            let _e599 = Params.SpriteShadowMaxLength;
                                            let _e607 = Params.SpriteShadowStrength;
                                            phi_703_ = (1f - (clamp(_e607, 0f, 1f) * clamp((1f - ((_e290 - _e580.y) / max(mix(_e597, _e599, clamp((1f - (_e580.y / _e281.Radius)), 0f, 1f)), 0.0001f))), 0f, 1f)));
                                            break;
                                        }
                                    }
                                    let _e612 = phi_703_;
                                    local_5 = (_e559 + (_e573 * _e612));
                                    continue;
                                } else {
                                    break;
                                }
                                continuing {
                                    let _e739 = local_5;
                                    phi_641_ = _e739;
                                    phi_644_ = (_e561 + 1u);
                                }
                            }
                            phi_705_ = select(true, _e267, _e267);
                            phi_706_ = select(true, _e269, _e269);
                            let _e748 = local_6;
                            phi_707_ = _e748;
                            break;
                        }
                    }
                    let _e617 = phi_705_;
                    let _e619 = phi_706_;
                    let _e621 = phi_707_;
                    let _e627 = Params.SporeFogPointStrength;
                    phi_315_ = (_e265 + (((_e445 * _e431) * _e627) * _e621));
                    phi_318_ = _e617;
                    phi_320_ = _e619;
                    phi_322_ = (_e271 + _e451);
                    phi_324_ = (_e273 + (_e451 * _e621));
                    continue;
                } else {
                    break;
                }
                continuing {
                    let _e632 = phi_315_;
                    let _e634 = phi_318_;
                    let _e636 = phi_320_;
                    let _e638 = phi_322_;
                    let _e640 = phi_324_;
                    phi_314_ = _e632;
                    phi_317_ = _e634;
                    phi_319_ = _e636;
                    phi_321_ = _e638;
                    phi_323_ = _e640;
                    phi_325_ = (_e275 + 1u);
                }
            }
            let _e643 = Params.SporeFogTint;
            switch bitcast<i32>(0u) {
                default: {
                    let _e647 = Params.SporeFogStrength;
                    local = _e647;
                    if (_e647 <= 0f) {
                        phi_763_ = 0f;
                        break;
                    }
                    let _e650 = Params.SporeFogDrift;
                    let _e652 = Params.Time;
                    let _e653 = (_e650 * _e652);
                    let _e656 = Params.SporeFogScale;
                    let _e658 = textureSampleLevel(SporeNoise, SporeNoiseSampler, ((_e147 + _e653) * _e656), 0f);
                    let _e669 = textureSampleLevel(SporeNoise, SporeNoiseSampler, ((((vec2<f32>(-(_e147.y), _e147.x) - (_e653 * 0.4f)) * _e656) * 3.17f) + vec2<f32>(0.37f, 0.61f)), 0f);
                    let _e675 = Params.SporeFogContrast;
                    phi_763_ = clamp(((((_e658.x * mix(0.75f, 1.25f, _e669.y)) - 0.5f) * _e675) + 0.5f), 0f, 1f);
                    break;
                }
            }
            let _e680 = phi_763_;
            let _e682 = local;
            let _e686 = local_1;
            let _e690 = (vec3<f32>(1f, 1f, 1f) - exp(-((_e686 * (_e643.xyz * (_e680 * _e682))))));
            let _e692 = local_2;
            textureStore(BackgroundMask, _e132, clamp(_e692, vec4<f32>(0f, 0f, 0f, 0f), vec4<f32>(1f, 1f, 1f, 1f)));
            let _e695 = local_3;
            textureStore(ForegroundMask, _e132, clamp(_e695, vec4<f32>(0f, 0f, 0f, 0f), vec4<f32>(1f, 1f, 1f, 1f)));
            let _e697 = clamp(_e690, vec3<f32>(0f, 0f, 0f), vec3<f32>(1f, 1f, 1f));
            textureStore(SporeFogMask, _e132, vec4<f32>(_e697.x, _e697.y, _e697.z, clamp(max(max(_e690.x, _e690.y), _e690.z), 0f, 1f)));
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
