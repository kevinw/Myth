struct CellState {
    c: array<f32, 16>,
}

struct type_u002e_StructuredBuffer_u002e_CellState {
    member: array<CellState>,
}

struct RippleState {
    H: f32,
    V: f32,
}

struct type_u002e_StructuredBuffer_u002e_RippleState {
    member: array<RippleState>,
}

struct type_u002e_Params {
    Width: u32,
    Height: u32,
    RippleDisplacement: f32,
    RippleTint: f32,
}

@group(0) @binding(0) 
var<storage> SourceState: type_u002e_StructuredBuffer_u002e_CellState;
@group(0) @binding(1) 
var<storage> Ripple: type_u002e_StructuredBuffer_u002e_RippleState;
@group(1) @binding(0) 
var Output: texture_storage_2d<rgba8unorm,read_write>;
@group(2) @binding(0) 
var<uniform> Params: type_u002e_Params;
var<private> global: vec3<u32>;

fn compute_main_1() {
    var phi_75_: bool;
    var phi_103_: bool;
    var phi_109_: bool;
    var phi_118_: bool;
    var phi_128_: f32;
    var phi_139_: bool;
    var phi_145_: bool;
    var phi_154_: bool;
    var phi_164_: f32;
    var phi_176_: bool;
    var phi_182_: bool;
    var phi_191_: bool;
    var phi_201_: f32;
    var phi_212_: bool;
    var phi_218_: bool;
    var phi_227_: bool;
    var phi_237_: f32;
    var phi_257_: vec2<i32>;
    var phi_297_: bool;
    var phi_303_: bool;
    var phi_311_: bool;
    var phi_316_: f32;

    let _e26 = global;
    switch bitcast<i32>(0u) {
        default: {
            let _e29 = global[0i];
            let _e31 = Params.Width;
            phi_75_ = true;
            if !((_e29 >= _e31)) {
                let _e35 = global[1i];
                let _e37 = Params.Height;
                phi_75_ = (_e35 >= _e37);
            }
            let _e40 = phi_75_;
            if _e40 {
                break;
            }
            let _e41 = bitcast<i32>(_e29);
            let _e43 = global[1i];
            let _e44 = bitcast<i32>(_e43);
            let _e45 = vec2<i32>(_e41, _e44);
            let _e49 = ((bitcast<u32>(_e44) * _e31) + bitcast<u32>(_e41));
            let _e54 = SourceState.member[_e49].c[3i];
            phi_257_ = _e45;
            if (clamp(_e54, 0f, 1f) > 0.1f) {
                let _e57 = (_e45 + vec2<i32>(1i, 0i));
                switch bitcast<i32>(0u) {
                    default: {
                        phi_103_ = true;
                        if !((_e57.x < 0i)) {
                            phi_103_ = (_e57.y < 0i);
                        }
                        let _e65 = phi_103_;
                        phi_109_ = true;
                        if !(_e65) {
                            phi_109_ = (_e57.x >= bitcast<i32>(_e31));
                        }
                        let _e70 = phi_109_;
                        phi_118_ = true;
                        if !(_e70) {
                            let _e74 = Params.Height;
                            phi_118_ = (_e57.y >= bitcast<i32>(_e74));
                        }
                        let _e78 = phi_118_;
                        if _e78 {
                            phi_128_ = f32();
                            break;
                        }
                        let _e87 = Ripple.member[((bitcast<u32>(_e57.y) * _e31) + bitcast<u32>(_e57.x))].H;
                        phi_128_ = _e87;
                        break;
                    }
                }
                let _e89 = phi_128_;
                let _e90 = (_e45 + vec2<i32>(-1i, 0i));
                switch bitcast<i32>(0u) {
                    default: {
                        phi_139_ = true;
                        if !((_e90.x < 0i)) {
                            phi_139_ = (_e90.y < 0i);
                        }
                        let _e98 = phi_139_;
                        phi_145_ = true;
                        if !(_e98) {
                            phi_145_ = (_e90.x >= bitcast<i32>(_e31));
                        }
                        let _e103 = phi_145_;
                        phi_154_ = true;
                        if !(_e103) {
                            let _e107 = Params.Height;
                            phi_154_ = (_e90.y >= bitcast<i32>(_e107));
                        }
                        let _e111 = phi_154_;
                        if _e111 {
                            phi_164_ = f32();
                            break;
                        }
                        let _e120 = Ripple.member[((bitcast<u32>(_e90.y) * _e31) + bitcast<u32>(_e90.x))].H;
                        phi_164_ = _e120;
                        break;
                    }
                }
                let _e122 = phi_164_;
                let _e124 = (_e45 + vec2<i32>(0i, 1i));
                switch bitcast<i32>(0u) {
                    default: {
                        phi_176_ = true;
                        if !((_e124.x < 0i)) {
                            phi_176_ = (_e124.y < 0i);
                        }
                        let _e132 = phi_176_;
                        phi_182_ = true;
                        if !(_e132) {
                            phi_182_ = (_e124.x >= bitcast<i32>(_e31));
                        }
                        let _e137 = phi_182_;
                        phi_191_ = true;
                        if !(_e137) {
                            let _e141 = Params.Height;
                            phi_191_ = (_e124.y >= bitcast<i32>(_e141));
                        }
                        let _e145 = phi_191_;
                        if _e145 {
                            phi_201_ = f32();
                            break;
                        }
                        let _e154 = Ripple.member[((bitcast<u32>(_e124.y) * _e31) + bitcast<u32>(_e124.x))].H;
                        phi_201_ = _e154;
                        break;
                    }
                }
                let _e156 = phi_201_;
                let _e157 = (_e45 + vec2<i32>(0i, -1i));
                switch bitcast<i32>(0u) {
                    default: {
                        phi_212_ = true;
                        if !((_e157.x < 0i)) {
                            phi_212_ = (_e157.y < 0i);
                        }
                        let _e165 = phi_212_;
                        phi_218_ = true;
                        if !(_e165) {
                            phi_218_ = (_e157.x >= bitcast<i32>(_e31));
                        }
                        let _e170 = phi_218_;
                        phi_227_ = true;
                        if !(_e170) {
                            let _e174 = Params.Height;
                            phi_227_ = (_e157.y >= bitcast<i32>(_e174));
                        }
                        let _e178 = phi_227_;
                        if _e178 {
                            phi_237_ = f32();
                            break;
                        }
                        let _e187 = Ripple.member[((bitcast<u32>(_e157.y) * _e31) + bitcast<u32>(_e157.x))].H;
                        phi_237_ = _e187;
                        break;
                    }
                }
                let _e189 = phi_237_;
                let _e192 = Params.RippleDisplacement;
                let _e204 = Params.Height;
                phi_257_ = clamp((_e45 + vec2<i32>(i32(round(((_e89 - _e122) * _e192))), i32(round(((_e156 - _e189) * _e192))))), vec2<i32>(0i, 0i), vec2<i32>((bitcast<i32>(_e31) - 1i), (bitcast<i32>(_e204) - 1i)));
            }
            let _e210 = phi_257_;
            let _e219 = SourceState.member[((bitcast<u32>(_e210.y) * _e31) + bitcast<u32>(_e210.x))];
            let _e228 = clamp(_e219.c[3], 0f, 1f);
            let _e232 = clamp(vec3<f32>(_e219.c[4], _e219.c[5], _e219.c[6]), vec3<f32>(0f, 0f, 0f), vec3<f32>(1f, 1f, 1f));
            let _e243 = clamp((_e228 + (max(_e232.x, max(_e232.y, _e232.z)) * 0.15f)), 0f, 1f);
            switch bitcast<i32>(0u) {
                default: {
                    phi_297_ = true;
                    if !((_e41 < 0i)) {
                        phi_297_ = (_e44 < 0i);
                    }
                    let _e249 = phi_297_;
                    phi_303_ = true;
                    if !(_e249) {
                        phi_303_ = (_e41 >= bitcast<i32>(_e31));
                    }
                    let _e254 = phi_303_;
                    phi_311_ = true;
                    if !(_e254) {
                        let _e257 = Params.Height;
                        phi_311_ = (_e44 >= bitcast<i32>(_e257));
                    }
                    let _e261 = phi_311_;
                    if _e261 {
                        phi_316_ = f32();
                        break;
                    }
                    let _e265 = Ripple.member[_e49].H;
                    phi_316_ = _e265;
                    break;
                }
            }
            let _e267 = phi_316_;
            let _e270 = Params.RippleTint;
            let _e275 = mix(((clamp(vec3<f32>(_e219.c[0], _e219.c[1], _e219.c[2]), vec3<f32>(0f, 0f, 0f), vec3<f32>(1f, 1f, 1f)) * _e228) + (_e232 * 0.35f)), (vec3<f32>(1f, 1f, 1f) * _e243), vec3(clamp((abs(_e267) * _e270), 0f, 1f)));
            textureStore(Output, _e26.xy, vec4<f32>(_e275.x, _e275.y, _e275.z, _e243));
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
