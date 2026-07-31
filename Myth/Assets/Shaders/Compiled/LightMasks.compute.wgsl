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
}

@group(0) @binding(0) 
var NormalTexture: texture_2d<f32>;
@group(0) @binding(100) 
var NormalSampler: sampler;
@group(0) @binding(1) 
var<storage> Lights: type_u002e_StructuredBuffer_u002e_Light;
@group(0) @binding(2) 
var<storage> ShadowDepth: type_u002e_StructuredBuffer_u002e_v2float;
@group(1) @binding(0) 
var BackgroundMask: texture_storage_2d<rgba8unorm,write>;
@group(1) @binding(1) 
var ForegroundMask: texture_storage_2d<rgba8unorm,write>;
@group(2) @binding(0) 
var<uniform> Params: type_u002e_Params;
var<private> global: vec3<u32>;

fn compute_main_1() {
    var weights: array<f32, 5>;
    var offsets: array<i32, 5>;
    var weights_1: array<f32, 5>;
    var offsets_1: array<i32, 5>;
    var phi_122_: bool;
    var phi_217_: f32;
    var phi_220_: u32;
    var phi_241_: f32;
    var phi_244_: vec4<f32>;
    var phi_245_: vec4<f32>;
    var phi_247_: bool;
    var phi_250_: bool;
    var phi_252_: vec4<f32>;
    var phi_254_: vec4<f32>;
    var phi_256_: u32;
    var phi_307_: f32;
    var phi_322_: i32;
    var phi_344_: bool;
    var phi_365_: f32;
    var phi_381_: f32;
    var phi_384_: u32;
    var phi_398_: i32;
    var phi_422_: bool;
    var phi_443_: f32;
    var phi_445_: bool;
    var phi_446_: bool;
    var phi_447_: f32;
    var phi_248_: bool;
    var phi_251_: bool;
    var phi_253_: vec4<f32>;
    var phi_255_: vec4<f32>;
    var local: vec4<f32>;
    var local_1: vec4<f32>;
    var local_2: f32;
    var local_3: f32;
    var local_4: f32;

    let _e62 = global;
    switch bitcast<i32>(0u) {
        default: {
            let _e65 = global[0i];
            let _e67 = Params.Width;
            phi_122_ = true;
            if !((_e65 >= _e67)) {
                let _e71 = global[1i];
                let _e73 = Params.Height;
                phi_122_ = (_e71 >= _e73);
            }
            let _e76 = phi_122_;
            if _e76 {
                break;
            }
            let _e77 = _e62.xy;
            let _e79 = (vec2<f32>(_e77) + vec2<f32>(0.5f, 0.5f));
            let _e82 = Params.Height;
            let _e87 = Params.Camera;
            let _e89 = Params.Zoom;
            let _e92 = (_e87 + (_e79 / vec2(_e89)));
            let _e93 = textureSampleLevel(NormalTexture, NormalSampler, (_e79 / vec2<f32>(f32(_e67), f32(_e82))), 0f);
            let _e98 = normalize(((_e93.xyz * 2f) - vec3<f32>(1f, 1f, 1f)));
            let _e100 = Params.Ambient;
            let _e102 = Params.SunEnabled;
            phi_244_ = _e100;
            phi_245_ = _e100;
            if (_e102 != 0u) {
                let _e105 = Params.SunDirection;
                let _e106 = -(_e105);
                let _e117 = Params.SunColor;
                let _e120 = Params.SunStrength;
                let _e122 = ((_e117.xyz * _e120) * mix(1f, (0.12f + (clamp(dot(_e98, normalize(vec3<f32>(_e106.x, _e106.y, 1f))), 0f, 1f) * 2.15f)), _e93.w));
                let _e126 = vec4<f32>(_e122.x, _e122.y, _e122.z, 1f);
                switch bitcast<i32>(0u) {
                    default: {
                        let _e130 = Params.SunPerpendicular;
                        let _e133 = Params.SunPerpendicularMin;
                        let _e136 = Params.SunPerpendicularMax;
                        let _e142 = i32(round((clamp(((dot(_e92, _e130) - _e133) / (_e136 - _e133)), 0f, 1f) * 511f)));
                        let _e144 = Params.SunSoftness;
                        if (_e144 <= 0f) {
                            let _e151 = ShadowDepth.member[bitcast<u32>((32768i + _e142))][0i];
                            let _e154 = Params.ShadowBias;
                            phi_241_ = select(0f, 1f, (dot(_e92, _e105) <= (_e151 + _e154)));
                            break;
                        }
                        offsets_1 = array<i32, 5>(-2i, -1i, 0i, 1i, 2i);
                        weights_1 = array<f32, 5>(0.1f, 0.2f, 0.4f, 0.2f, 0.1f);
                        phi_217_ = 0f;
                        phi_220_ = 0u;
                        loop {
                            let _e163 = phi_217_;
                            let _e165 = phi_220_;
                            local_2 = _e163;
                            if (_e165 < 5u) {
                                continue;
                            } else {
                                break;
                            }
                            continuing {
                                let _e168 = offsets_1[_e165];
                                let _e177 = ShadowDepth.member[bitcast<u32>((32768i + clamp((_e142 + (_e168 * max(1i, i32(round(_e144))))), 0i, 511i)))][0i];
                                let _e179 = weights_1[_e165];
                                let _e181 = Params.ShadowBias;
                                phi_217_ = (_e163 + (_e179 * select(0f, 1f, (dot(_e92, _e105) <= (_e177 + _e181)))));
                                phi_220_ = (_e165 + 1u);
                            }
                        }
                        let _e398 = local_2;
                        phi_241_ = _e398;
                        break;
                    }
                }
                let _e189 = phi_241_;
                phi_244_ = (_e100 + _e126);
                phi_245_ = (_e100 + (_e126 * _e189));
            }
            let _e193 = phi_244_;
            let _e195 = phi_245_;
            phi_247_ = false;
            phi_250_ = false;
            phi_252_ = _e193;
            phi_254_ = _e195;
            phi_256_ = 0u;
            loop {
                let _e197 = phi_247_;
                let _e199 = phi_250_;
                let _e201 = phi_252_;
                let _e203 = phi_254_;
                let _e205 = phi_256_;
                let _e207 = Params.LightCount;
                local = _e203;
                local_1 = _e201;
                if (_e205 < _e207) {
                    let _e211 = Lights.member[_e205];
                    let _e216 = (_e211.Position - _e92);
                    let _e217 = length(_e216);
                    let _e218 = (_e217 / _e211.Radius);
                    let _e220 = clamp((1f - _e218), 0f, 1f);
                    let _e221 = (_e220 * _e220);
                    if (_e221 <= 0f) {
                        phi_248_ = _e197;
                        phi_251_ = _e199;
                        phi_253_ = _e201;
                        phi_255_ = _e203;
                        continue;
                    }
                    let _e236 = (((_e211.ColorIntensity.xyz * _e211.ColorIntensity.w) * _e221) * mix(1f, (0.12f + (clamp(dot(_e98, normalize(vec3<f32>(_e216.x, _e216.y, _e211.Height))), 0f, 1f) * 2.15f)), _e93.w));
                    let _e240 = vec4<f32>(_e236.x, _e236.y, _e236.z, _e221);
                    let _e242 = -(_e216);
                    switch bitcast<i32>(0u) {
                        default: {
                            let _e246 = atan2(_e242.y, _e242.x);
                            phi_307_ = _e246;
                            if (_e246 < 0f) {
                                phi_307_ = (_e246 + 6.2831855f);
                            }
                            let _e250 = phi_307_;
                            let _e254 = i32(round(((_e250 * 81.48733f) - 0.5f)));
                            let _e256 = Params.SoftShadowSamples;
                            if (_e256 <= 0f) {
                                let _e258 = (_e254 % 512i);
                                phi_322_ = _e258;
                                if (_e258 < 0i) {
                                    phi_322_ = (_e258 + 512i);
                                }
                                let _e262 = phi_322_;
                                switch bitcast<i32>(0u) {
                                    default: {
                                        let _e269 = ShadowDepth.member[((_e205 * 512u) + bitcast<u32>(_e262))];
                                        let _e272 = Params.ShadowBias;
                                        if (_e217 > (_e269.x + _e272)) {
                                            phi_365_ = 0f;
                                            break;
                                        }
                                        phi_344_ = true;
                                        if !((_e269.y >= _e211.Radius)) {
                                            phi_344_ = (_e217 <= (_e269.y + _e272));
                                        }
                                        let _e281 = phi_344_;
                                        if _e281 {
                                            phi_365_ = 1f;
                                            break;
                                        }
                                        let _e286 = Params.SpriteShadowMinLength;
                                        let _e288 = Params.SpriteShadowMaxLength;
                                        let _e296 = Params.SpriteShadowStrength;
                                        phi_365_ = (1f - (clamp(_e296, 0f, 1f) * clamp((1f - ((_e217 - _e269.y) / max(mix(_e286, _e288, clamp((1f - (_e269.y / _e211.Radius)), 0f, 1f)), 0.0001f))), 0f, 1f)));
                                        break;
                                    }
                                }
                                let _e301 = phi_365_;
                                phi_445_ = _e197;
                                phi_446_ = _e199;
                                phi_447_ = _e301;
                                break;
                            }
                            let _e303 = Params.MinimumSoftness;
                            if _e199 {
                            } else {
                                offsets = array<i32, 5>(-2i, -1i, 0i, 1i, 2i);
                            }
                            if _e197 {
                            } else {
                                weights = array<f32, 5>(0.1f, 0.2f, 0.4f, 0.2f, 0.1f);
                            }
                            phi_381_ = 0f;
                            phi_384_ = 0u;
                            loop {
                                let _e313 = phi_381_;
                                let _e315 = phi_384_;
                                local_4 = _e313;
                                if (_e315 < 5u) {
                                    let _e318 = offsets[_e315];
                                    let _e321 = ((_e254 + (_e318 * max(1i, i32(round((_e256 * mix(_e303, 1f, clamp(_e218, 0f, 1f)))))))) % 512i);
                                    phi_398_ = _e321;
                                    if (_e321 < 0i) {
                                        phi_398_ = (_e321 + 512i);
                                    }
                                    let _e325 = phi_398_;
                                    let _e327 = weights[_e315];
                                    switch bitcast<i32>(0u) {
                                        default: {
                                            let _e334 = ShadowDepth.member[((_e205 * 512u) + bitcast<u32>(_e325))];
                                            let _e337 = Params.ShadowBias;
                                            if (_e217 > (_e334.x + _e337)) {
                                                phi_443_ = 0f;
                                                break;
                                            }
                                            phi_422_ = true;
                                            if !((_e334.y >= _e211.Radius)) {
                                                phi_422_ = (_e217 <= (_e334.y + _e337));
                                            }
                                            let _e346 = phi_422_;
                                            if _e346 {
                                                phi_443_ = 1f;
                                                break;
                                            }
                                            let _e351 = Params.SpriteShadowMinLength;
                                            let _e353 = Params.SpriteShadowMaxLength;
                                            let _e361 = Params.SpriteShadowStrength;
                                            phi_443_ = (1f - (clamp(_e361, 0f, 1f) * clamp((1f - ((_e217 - _e334.y) / max(mix(_e351, _e353, clamp((1f - (_e334.y / _e211.Radius)), 0f, 1f)), 0.0001f))), 0f, 1f)));
                                            break;
                                        }
                                    }
                                    let _e366 = phi_443_;
                                    local_3 = (_e313 + (_e327 * _e366));
                                    continue;
                                } else {
                                    break;
                                }
                                continuing {
                                    let _e412 = local_3;
                                    phi_381_ = _e412;
                                    phi_384_ = (_e315 + 1u);
                                }
                            }
                            phi_445_ = select(true, _e197, _e197);
                            phi_446_ = select(true, _e199, _e199);
                            let _e421 = local_4;
                            phi_447_ = _e421;
                            break;
                        }
                    }
                    let _e371 = phi_445_;
                    let _e373 = phi_446_;
                    let _e375 = phi_447_;
                    phi_248_ = _e371;
                    phi_251_ = _e373;
                    phi_253_ = (_e201 + _e240);
                    phi_255_ = (_e203 + (_e240 * _e375));
                    continue;
                } else {
                    break;
                }
                continuing {
                    let _e379 = phi_248_;
                    let _e381 = phi_251_;
                    let _e383 = phi_253_;
                    let _e385 = phi_255_;
                    phi_247_ = _e379;
                    phi_250_ = _e381;
                    phi_252_ = _e383;
                    phi_254_ = _e385;
                    phi_256_ = (_e205 + 1u);
                }
            }
            let _e388 = local;
            textureStore(BackgroundMask, _e77, clamp(_e388, vec4<f32>(0f, 0f, 0f, 0f), vec4<f32>(1f, 1f, 1f, 1f)));
            let _e391 = local_1;
            textureStore(ForegroundMask, _e77, clamp(_e391, vec4<f32>(0f, 0f, 0f, 0f), vec4<f32>(1f, 1f, 1f, 1f)));
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
