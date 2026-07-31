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

struct type_u002e_RWStructuredBuffer_u002e_float {
    member: array<f32>,
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
var<storage, read_write> ShadowDepth: type_u002e_RWStructuredBuffer_u002e_float;
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
    var phi_170_: f32;
    var local: f32;
    var phi_193_: bool;
    var phi_206_: bool;
    var phi_209_: f32;
    var phi_241_: f32;
    var phi_244_: u32;
    var phi_290_: bool;
    var phi_294_: bool;
    var phi_295_: f32;
    var phi_296_: bool;
    var phi_300_: f32;
    var phi_242_: f32;
    var local_1: f32;
    var local_2: f32;
    var local_3: f32;

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
                    ShadowDepth.member[_e46] = 0f;
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
                        local_2 = _e113;
                        continue;
                    } else {
                        break;
                    }
                    continuing {
                        let _e249 = local_2;
                        phi_113_ = _e249;
                        phi_116_ = (_e70 + 1u);
                    }
                }
                let _e117 = Params.ZoomPadding[0i];
                let _e119 = max(0.5f, (0.5f / _e117));
                phi_170_ = _e119;
                loop {
                    let _e121 = phi_170_;
                    let _e123 = local;
                    let _e262 = local_3;
                    phi_209_ = _e262;
                    if (_e121 < _e123) {
                        switch bitcast<i32>(0u) {
                            default: {
                                let _e129 = Params.Camera;
                                let _e131 = (((_e56 + (vec2<f32>(_e64, _e65) * _e121)) - _e129) * _e117);
                                phi_193_ = true;
                                if !(any((_e131 < vec2<f32>(0f, 0f)))) {
                                    let _e136 = Params.TargetSize;
                                    phi_193_ = any((_e131 >= _e136));
                                }
                                let _e140 = phi_193_;
                                if _e140 {
                                    phi_206_ = false;
                                    break;
                                }
                                let _e143 = Params.TargetSize;
                                let _e145 = textureSampleLevel(SpriteShadowMask, SpriteShadowSampler, ((_e131 + vec2<f32>(0.5f, 0.5f)) / _e143), 0f);
                                phi_206_ = (_e145.x > 0.1f);
                                break;
                            }
                        }
                        let _e149 = phi_206_;
                        if _e149 {
                            phi_209_ = _e121;
                            break;
                        }
                        continue;
                    } else {
                        break;
                    }
                    continuing {
                        phi_170_ = (_e121 + _e119);
                    }
                }
                let _e152 = phi_209_;
                ShadowDepth.member[_e46] = _e152;
                break;
            }
            let _e156 = Params.SunEnabled;
            if (_e156 == 0u) {
                let _e159 = Params.SunAlongMax;
                ShadowDepth.member[_e46] = _e159;
                break;
            }
            let _e163 = Params.SunPerpendicularMin;
            let _e165 = Params.SunPerpendicularMax;
            let _e172 = Params.SunPerpendicular;
            let _e175 = Params.SunDirection;
            let _e177 = Params.SunAlongMin;
            let _e181 = Params.SunAlongMax;
            phi_241_ = (_e181 - _e177);
            phi_244_ = 0u;
            loop {
                let _e184 = phi_241_;
                let _e186 = phi_244_;
                let _e188 = Params.SegmentCount;
                local_1 = _e184;
                if (_e186 < _e188) {
                    let _e194 = Segments.member[_e186].Properties[0i];
                    if (_e194 <= 0f) {
                        phi_242_ = _e184;
                        continue;
                    }
                    let _e199 = Segments.member[_e186].Coordinates;
                    switch bitcast<i32>(0u) {
                        default: {
                            let _e201 = _e199.xy;
                            let _e203 = (_e199.zw - _e201);
                            let _e210 = ((_e175.x * _e203.y) - (_e175.y * _e203.x));
                            if (abs(_e210) <= 0.0001f) {
                                phi_295_ = 0f;
                                phi_296_ = false;
                                break;
                            }
                            let _e213 = (_e201 - ((_e172 * mix(_e163, _e165, ((f32(_e33.x) + 0.5f) / f32(_e37)))) + (_e175 * _e177)));
                            let _e219 = (((_e213.x * _e203.y) - (_e213.y * _e203.x)) / _e210);
                            let _e223 = (((_e213.x * _e175.y) - (_e213.y * _e175.x)) / _e210);
                            phi_290_ = false;
                            if (_e219 >= 0f) {
                                phi_290_ = (_e223 >= 0f);
                            }
                            let _e227 = phi_290_;
                            phi_294_ = false;
                            if _e227 {
                                phi_294_ = (_e223 <= 1f);
                            }
                            let _e230 = phi_294_;
                            phi_295_ = _e219;
                            phi_296_ = _e230;
                            break;
                        }
                    }
                    let _e232 = phi_295_;
                    let _e234 = phi_296_;
                    phi_300_ = _e184;
                    if _e234 {
                        phi_300_ = min(_e184, _e232);
                    }
                    let _e237 = phi_300_;
                    phi_242_ = _e237;
                    continue;
                } else {
                    break;
                }
                continuing {
                    let _e239 = phi_242_;
                    phi_241_ = _e239;
                    phi_244_ = (_e186 + 1u);
                }
            }
            let _e242 = local_1;
            ShadowDepth.member[_e46] = (_e177 + _e242);
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
