struct Segment {
    Coordinates: vec4<f32>,
    Properties: vec4<f32>,
}

struct type_u002e_StructuredBuffer_u002e_Segment {
    member: array<Segment>,
}

struct Light {
    Position: vec2<f32>,
    Radius: f32,
    Height: f32,
    ColorIntensity: vec4<f32>,
}

struct type_u002e_StructuredBuffer_u002e_Light {
    member: array<Light>,
}

struct type_u002e_RWStructuredBuffer_u002e_v2float {
    member: array<vec2<f32>>,
}

struct type_u002e_Params {
    SegmentCount: u32,
    LightCount: u32,
    SunEnabled: u32,
    ShadowSamples: u32,
    SunDirection: vec2<f32>,
    SunPerpendicular: vec2<f32>,
    SunPerpendicularMin: f32,
    SunPerpendicularMax: f32,
    SunAlongMin: f32,
    SunAlongMax: f32,
    Camera: vec2<f32>,
    TargetSize: vec2<f32>,
    ZoomPadding: vec4<f32>,
}

@group(0) @binding(0) 
var SpriteShadowMask: texture_2d<f32>;
@group(0) @binding(100) 
var SpriteShadowSampler: sampler;
@group(0) @binding(1) 
var<storage> Segments: type_u002e_StructuredBuffer_u002e_Segment;
@group(0) @binding(2) 
var<storage> Lights: type_u002e_StructuredBuffer_u002e_Light;
@group(1) @binding(0) 
var<storage, read_write> ShadowDepth: type_u002e_RWStructuredBuffer_u002e_v2float;
@group(2) @binding(0) 
var<uniform> Params: type_u002e_Params;
var<private> global: vec3<u32>;

fn compute_main_1() {
    var phi_84_: bool;
    var phi_113_: f32;
    var phi_116_: u32;
    var phi_155_: bool;
    var phi_159_: bool;
    var phi_160_: f32;
    var phi_161_: bool;
    var phi_114_: f32;
    var phi_170_: bool;
    var phi_173_: f32;
    var local: f32;
    var phi_195_: bool;
    var phi_208_: bool;
    var phi_171_: bool;
    var phi_215_: f32;
    var local_1: bool;
    var phi_219_: bool;
    var local_2: f32;
    var local_3: f32;
    var phi_254_: f32;
    var phi_257_: u32;
    var phi_303_: bool;
    var phi_307_: bool;
    var phi_308_: f32;
    var phi_309_: bool;
    var phi_313_: f32;
    var phi_255_: f32;
    var local_4: f32;
    var local_5: f32;

    let _e33 = global;
    switch bitcast<i32>(0u) {
        default: {
            let _e37 = Params.ShadowSamples;
            phi_84_ = true;
            if !((_e33.x >= _e37)) {
                phi_84_ = (_e33.y > 64u);
            }
            let _e43 = phi_84_;
            if _e43 {
                break;
            }
            let _e46 = ((_e33.y * _e37) + _e33.x);
            if (_e33.y < 64u) {
                let _e49 = Params.LightCount;
                if (_e33.y >= _e49) {
                    ShadowDepth.member[_e46] = vec2<f32>(0f, 0f);
                    break;
                }
                let _e56 = Lights.member[_e33.y].Position;
                let _e58 = Lights.member[_e33.y].Radius;
                let _e63 = (((f32(_e33.x) + 0.5f) / f32(_e37)) * 6.2831855f);
                let _e64 = cos(_e63);
                let _e65 = sin(_e63);
                phi_113_ = _e58;
                phi_116_ = 0u;
                loop {
                    let _e68 = phi_113_;
                    let _e70 = phi_116_;
                    let _e72 = Params.SegmentCount;
                    local = _e68;
                    local_2 = _e68;
                    local_3 = _e68;
                    if (_e70 < _e72) {
                        let _e77 = Segments.member[_e70].Coordinates;
                        switch bitcast<i32>(0u) {
                            default: {
                                let _e79 = _e77.xy;
                                let _e81 = (_e77.zw - _e79);
                                let _e86 = ((_e64 * _e81.y) - (_e65 * _e81.x));
                                if (abs(_e86) <= 0.0001f) {
                                    phi_160_ = 0f;
                                    phi_161_ = false;
                                    break;
                                }
                                let _e89 = (_e79 - _e56);
                                let _e95 = (((_e89.x * _e81.y) - (_e89.y * _e81.x)) / _e86);
                                let _e99 = (((_e89.x * _e65) - (_e89.y * _e64)) / _e86);
                                phi_155_ = false;
                                if (_e95 >= 0f) {
                                    phi_155_ = (_e99 >= 0f);
                                }
                                let _e103 = phi_155_;
                                phi_159_ = false;
                                if _e103 {
                                    phi_159_ = (_e99 <= 1f);
                                }
                                let _e106 = phi_159_;
                                phi_160_ = _e95;
                                phi_161_ = _e106;
                                break;
                            }
                        }
                        let _e108 = phi_160_;
                        let _e110 = phi_161_;
                        phi_114_ = _e68;
                        if _e110 {
                            phi_114_ = min(_e68, _e108);
                        }
                        let _e113 = phi_114_;
                        local_5 = _e113;
                        continue;
                    } else {
                        break;
                    }
                    continuing {
                        let _e268 = local_5;
                        phi_113_ = _e268;
                        phi_116_ = (_e70 + 1u);
                    }
                }
                let _e117 = Params.ZoomPadding[0i];
                let _e119 = max(0.5f, (0.5f / _e117));
                phi_170_ = false;
                phi_173_ = _e119;
                loop {
                    let _e121 = phi_170_;
                    let _e123 = phi_173_;
                    let _e125 = local;
                    phi_215_ = _e58;
                    local_1 = _e121;
                    if (_e123 < _e125) {
                        switch bitcast<i32>(0u) {
                            default: {
                                let _e131 = Params.Camera;
                                let _e133 = (((_e56 + (vec2<f32>(_e64, _e65) * _e123)) - _e131) * _e117);
                                phi_195_ = true;
                                if !(any((_e133 < vec2<f32>(0f, 0f)))) {
                                    let _e138 = Params.TargetSize;
                                    phi_195_ = any((_e133 >= _e138));
                                }
                                let _e142 = phi_195_;
                                if _e142 {
                                    phi_208_ = false;
                                    break;
                                }
                                let _e145 = Params.TargetSize;
                                let _e147 = textureSampleLevel(SpriteShadowMask, SpriteShadowSampler, ((_e133 + vec2<f32>(0.5f, 0.5f)) / _e145), 0f);
                                phi_208_ = (_e147.x > 0.1f);
                                break;
                            }
                        }
                        let _e151 = phi_208_;
                        if !(_e121) {
                            phi_171_ = _e151;
                            continue;
                        }
                        if !(_e151) {
                            phi_215_ = _e123;
                            break;
                        }
                        phi_171_ = _e121;
                        continue;
                    } else {
                        break;
                    }
                    continuing {
                        let _e155 = phi_171_;
                        phi_170_ = _e155;
                        phi_173_ = (_e123 + _e119);
                    }
                }
                let _e158 = phi_215_;
                let _e160 = local_1;
                phi_219_ = false;
                if _e160 {
                    phi_219_ = (_e158 == _e58);
                }
                let _e163 = phi_219_;
                let _e165 = local_2;
                let _e168 = local_3;
                ShadowDepth.member[_e46] = vec2<f32>(_e168, select(_e158, _e165, _e163));
                break;
            }
            let _e173 = Params.SunEnabled;
            if (_e173 == 0u) {
                let _e176 = Params.SunAlongMax;
                ShadowDepth.member[_e46] = vec2(_e176);
                break;
            }
            let _e181 = Params.SunPerpendicularMin;
            let _e183 = Params.SunPerpendicularMax;
            let _e190 = Params.SunPerpendicular;
            let _e193 = Params.SunDirection;
            let _e195 = Params.SunAlongMin;
            let _e199 = Params.SunAlongMax;
            phi_254_ = (_e199 - _e195);
            phi_257_ = 0u;
            loop {
                let _e202 = phi_254_;
                let _e204 = phi_257_;
                let _e206 = Params.SegmentCount;
                local_4 = _e202;
                if (_e204 < _e206) {
                    let _e212 = Segments.member[_e204].Properties[0i];
                    if (_e212 <= 0f) {
                        phi_255_ = _e202;
                        continue;
                    }
                    let _e217 = Segments.member[_e204].Coordinates;
                    switch bitcast<i32>(0u) {
                        default: {
                            let _e219 = _e217.xy;
                            let _e221 = (_e217.zw - _e219);
                            let _e228 = ((_e193.x * _e221.y) - (_e193.y * _e221.x));
                            if (abs(_e228) <= 0.0001f) {
                                phi_308_ = 0f;
                                phi_309_ = false;
                                break;
                            }
                            let _e231 = (_e219 - ((_e190 * mix(_e181, _e183, ((f32(_e33.x) + 0.5f) / f32(_e37)))) + (_e193 * _e195)));
                            let _e237 = (((_e231.x * _e221.y) - (_e231.y * _e221.x)) / _e228);
                            let _e241 = (((_e231.x * _e193.y) - (_e231.y * _e193.x)) / _e228);
                            phi_303_ = false;
                            if (_e237 >= 0f) {
                                phi_303_ = (_e241 >= 0f);
                            }
                            let _e245 = phi_303_;
                            phi_307_ = false;
                            if _e245 {
                                phi_307_ = (_e241 <= 1f);
                            }
                            let _e248 = phi_307_;
                            phi_308_ = _e237;
                            phi_309_ = _e248;
                            break;
                        }
                    }
                    let _e250 = phi_308_;
                    let _e252 = phi_309_;
                    phi_313_ = _e202;
                    if _e252 {
                        phi_313_ = min(_e202, _e250);
                    }
                    let _e255 = phi_313_;
                    phi_255_ = _e255;
                    continue;
                } else {
                    break;
                }
                continuing {
                    let _e257 = phi_255_;
                    phi_254_ = _e257;
                    phi_257_ = (_e204 + 1u);
                }
            }
            let _e260 = local_4;
            ShadowDepth.member[_e46] = vec2<f32>((_e195 + _e260), _e199);
            break;
        }
    }
    return;
}

@compute @workgroup_size(64, 1, 1) 
fn compute_main(@builtin(global_invocation_id) param: vec3<u32>) {
    global = param;
    compute_main_1();
}
