struct Light {
    Position: vec2<f32>,
    Radius: f32,
    Height: f32,
    ColorIntensity: vec4<f32>,
}

struct type_u002e_StructuredBuffer_u002e_Light {
    member: array<Light>,
}

struct type_u002e_StructuredBuffer_u002e_float {
    member: array<f32>,
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
}

@group(0) @binding(0) 
var NormalTexture: texture_2d<f32>;
@group(0) @binding(100) 
var NormalSampler: sampler;
@group(0) @binding(1) 
var<storage> Lights: type_u002e_StructuredBuffer_u002e_Light;
@group(0) @binding(2) 
var<storage> ShadowDepth: type_u002e_StructuredBuffer_u002e_float;
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
    var phi_118_: bool;
    var phi_213_: f32;
    var phi_216_: u32;
    var phi_237_: f32;
    var phi_240_: vec4<f32>;
    var phi_241_: vec4<f32>;
    var phi_243_: bool;
    var phi_246_: bool;
    var phi_248_: vec4<f32>;
    var phi_250_: vec4<f32>;
    var phi_252_: u32;
    var phi_303_: f32;
    var phi_318_: i32;
    var phi_344_: f32;
    var phi_347_: u32;
    var phi_361_: i32;
    var phi_375_: bool;
    var phi_376_: bool;
    var phi_377_: f32;
    var phi_244_: bool;
    var phi_247_: bool;
    var phi_249_: vec4<f32>;
    var phi_251_: vec4<f32>;
    var local: vec4<f32>;
    var local_1: vec4<f32>;
    var local_2: f32;
    var local_3: f32;
    var local_4: f32;

    let _e58 = global;
    switch bitcast<i32>(0u) {
        default: {
            let _e61 = global[0i];
            let _e63 = Params.Width;
            phi_118_ = true;
            if !((_e61 >= _e63)) {
                let _e67 = global[1i];
                let _e69 = Params.Height;
                phi_118_ = (_e67 >= _e69);
            }
            let _e72 = phi_118_;
            if _e72 {
                break;
            }
            let _e73 = _e58.xy;
            let _e75 = (vec2<f32>(_e73) + vec2<f32>(0.5f, 0.5f));
            let _e78 = Params.Height;
            let _e83 = Params.Camera;
            let _e85 = Params.Zoom;
            let _e88 = (_e83 + (_e75 / vec2(_e85)));
            let _e89 = textureSampleLevel(NormalTexture, NormalSampler, (_e75 / vec2<f32>(f32(_e63), f32(_e78))), 0f);
            let _e94 = normalize(((_e89.xyz * 2f) - vec3<f32>(1f, 1f, 1f)));
            let _e96 = Params.Ambient;
            let _e98 = Params.SunEnabled;
            phi_240_ = _e96;
            phi_241_ = _e96;
            if (_e98 != 0u) {
                let _e101 = Params.SunDirection;
                let _e102 = -(_e101);
                let _e113 = Params.SunColor;
                let _e116 = Params.SunStrength;
                let _e118 = ((_e113.xyz * _e116) * mix(1f, (0.12f + (clamp(dot(_e94, normalize(vec3<f32>(_e102.x, _e102.y, 1f))), 0f, 1f) * 2.15f)), _e89.w));
                let _e122 = vec4<f32>(_e118.x, _e118.y, _e118.z, 1f);
                switch bitcast<i32>(0u) {
                    default: {
                        let _e126 = Params.SunPerpendicular;
                        let _e129 = Params.SunPerpendicularMin;
                        let _e132 = Params.SunPerpendicularMax;
                        let _e138 = i32(round((clamp(((dot(_e88, _e126) - _e129) / (_e132 - _e129)), 0f, 1f) * 511f)));
                        let _e140 = Params.SunSoftness;
                        if (_e140 <= 0f) {
                            let _e146 = ShadowDepth.member[bitcast<u32>((32768i + _e138))];
                            let _e149 = Params.ShadowBias;
                            phi_237_ = select(0f, 1f, (dot(_e88, _e101) <= (_e146 + _e149)));
                            break;
                        }
                        offsets_1 = array<i32, 5>(-2i, -1i, 0i, 1i, 2i);
                        weights_1 = array<f32, 5>(0.1f, 0.2f, 0.4f, 0.2f, 0.1f);
                        phi_213_ = 0f;
                        phi_216_ = 0u;
                        loop {
                            let _e158 = phi_213_;
                            let _e160 = phi_216_;
                            local_2 = _e158;
                            if (_e160 < 5u) {
                                continue;
                            } else {
                                break;
                            }
                            continuing {
                                let _e163 = offsets_1[_e160];
                                let _e171 = ShadowDepth.member[bitcast<u32>((32768i + clamp((_e138 + (_e163 * max(1i, i32(round(_e140))))), 0i, 511i)))];
                                let _e173 = weights_1[_e160];
                                let _e175 = Params.ShadowBias;
                                phi_213_ = (_e158 + (_e173 * select(0f, 1f, (dot(_e88, _e101) <= (_e171 + _e175)))));
                                phi_216_ = (_e160 + 1u);
                            }
                        }
                        let _e336 = local_2;
                        phi_237_ = _e336;
                        break;
                    }
                }
                let _e183 = phi_237_;
                phi_240_ = (_e96 + _e122);
                phi_241_ = (_e96 + (_e122 * _e183));
            }
            let _e187 = phi_240_;
            let _e189 = phi_241_;
            phi_243_ = false;
            phi_246_ = false;
            phi_248_ = _e187;
            phi_250_ = _e189;
            phi_252_ = 0u;
            loop {
                let _e191 = phi_243_;
                let _e193 = phi_246_;
                let _e195 = phi_248_;
                let _e197 = phi_250_;
                let _e199 = phi_252_;
                let _e201 = Params.LightCount;
                local = _e197;
                local_1 = _e195;
                if (_e199 < _e201) {
                    let _e205 = Lights.member[_e199];
                    let _e210 = (_e205.Position - _e88);
                    let _e211 = length(_e210);
                    let _e212 = (_e211 / _e205.Radius);
                    let _e214 = clamp((1f - _e212), 0f, 1f);
                    let _e215 = (_e214 * _e214);
                    if (_e215 <= 0f) {
                        phi_244_ = _e191;
                        phi_247_ = _e193;
                        phi_249_ = _e195;
                        phi_251_ = _e197;
                        continue;
                    }
                    let _e230 = (((_e205.ColorIntensity.xyz * _e205.ColorIntensity.w) * _e215) * mix(1f, (0.12f + (clamp(dot(_e94, normalize(vec3<f32>(_e210.x, _e210.y, _e205.Height))), 0f, 1f) * 2.15f)), _e89.w));
                    let _e234 = vec4<f32>(_e230.x, _e230.y, _e230.z, _e215);
                    let _e236 = -(_e210);
                    switch bitcast<i32>(0u) {
                        default: {
                            let _e240 = atan2(_e236.y, _e236.x);
                            phi_303_ = _e240;
                            if (_e240 < 0f) {
                                phi_303_ = (_e240 + 6.2831855f);
                            }
                            let _e244 = phi_303_;
                            let _e248 = i32(round(((_e244 * 81.48733f) - 0.5f)));
                            let _e250 = Params.SoftShadowSamples;
                            if (_e250 <= 0f) {
                                let _e252 = (_e248 % 512i);
                                phi_318_ = _e252;
                                if (_e252 < 0i) {
                                    phi_318_ = (_e252 + 512i);
                                }
                                let _e256 = phi_318_;
                                let _e262 = ShadowDepth.member[((_e199 * 512u) + bitcast<u32>(_e256))];
                                let _e264 = Params.ShadowBias;
                                phi_375_ = _e191;
                                phi_376_ = _e193;
                                phi_377_ = select(0f, 1f, (_e211 <= (_e262 + _e264)));
                                break;
                            }
                            let _e269 = Params.MinimumSoftness;
                            if _e193 {
                            } else {
                                offsets = array<i32, 5>(-2i, -1i, 0i, 1i, 2i);
                            }
                            if _e191 {
                            } else {
                                weights = array<f32, 5>(0.1f, 0.2f, 0.4f, 0.2f, 0.1f);
                            }
                            phi_344_ = 0f;
                            phi_347_ = 0u;
                            loop {
                                let _e279 = phi_344_;
                                let _e281 = phi_347_;
                                local_4 = _e279;
                                if (_e281 < 5u) {
                                    let _e284 = offsets[_e281];
                                    let _e287 = ((_e248 + (_e284 * max(1i, i32(round((_e250 * mix(_e269, 1f, clamp(_e212, 0f, 1f)))))))) % 512i);
                                    phi_361_ = _e287;
                                    if (_e287 < 0i) {
                                        phi_361_ = (_e287 + 512i);
                                    }
                                    let _e291 = phi_361_;
                                    let _e297 = ShadowDepth.member[((_e199 * 512u) + bitcast<u32>(_e291))];
                                    let _e299 = weights[_e281];
                                    let _e301 = Params.ShadowBias;
                                    local_3 = (_e279 + (_e299 * select(0f, 1f, (_e211 <= (_e297 + _e301)))));
                                    continue;
                                } else {
                                    break;
                                }
                                continuing {
                                    let _e348 = local_3;
                                    phi_344_ = _e348;
                                    phi_347_ = (_e281 + 1u);
                                }
                            }
                            phi_375_ = select(true, _e191, _e191);
                            phi_376_ = select(true, _e193, _e193);
                            let _e355 = local_4;
                            phi_377_ = _e355;
                            break;
                        }
                    }
                    let _e309 = phi_375_;
                    let _e311 = phi_376_;
                    let _e313 = phi_377_;
                    phi_244_ = _e309;
                    phi_247_ = _e311;
                    phi_249_ = (_e195 + _e234);
                    phi_251_ = (_e197 + (_e234 * _e313));
                    continue;
                } else {
                    break;
                }
                continuing {
                    let _e317 = phi_244_;
                    let _e319 = phi_247_;
                    let _e321 = phi_249_;
                    let _e323 = phi_251_;
                    phi_243_ = _e317;
                    phi_246_ = _e319;
                    phi_248_ = _e321;
                    phi_250_ = _e323;
                    phi_252_ = (_e199 + 1u);
                }
            }
            let _e326 = local;
            textureStore(BackgroundMask, _e73, clamp(_e326, vec4<f32>(0f, 0f, 0f, 0f), vec4<f32>(1f, 1f, 1f, 1f)));
            let _e329 = local_1;
            textureStore(ForegroundMask, _e73, clamp(_e329, vec4<f32>(0f, 0f, 0f, 0f), vec4<f32>(1f, 1f, 1f, 1f)));
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
