struct RippleState {
    H: f32,
    V: f32,
}

struct type_u002e_StructuredBuffer_u002e_RippleState {
    member: array<RippleState>,
}

struct type_u002e_RWStructuredBuffer_u002e_RippleState {
    member: array<RippleState>,
}

struct type_u002e_Params {
    Width: u32,
    Height: u32,
    Speed: f32,
    Damping: f32,
}

@group(0) @binding(0) 
var<storage> SourceState: type_u002e_StructuredBuffer_u002e_RippleState;
@group(1) @binding(0) 
var<storage, read_write> DestState: type_u002e_RWStructuredBuffer_u002e_RippleState;
@group(2) @binding(0) 
var<uniform> Params: type_u002e_Params;
var<private> global: vec3<u32>;

fn compute_main_1() {
    var phi_54_: bool;
    var phi_70_: bool;
    var phi_76_: bool;
    var phi_84_: bool;
    var phi_95_: f32;
    var phi_96_: f32;
    var phi_107_: bool;
    var phi_113_: bool;
    var phi_122_: bool;
    var phi_133_: f32;
    var phi_144_: bool;
    var phi_150_: bool;
    var phi_159_: bool;
    var phi_170_: f32;
    var phi_182_: bool;
    var phi_188_: bool;
    var phi_197_: bool;
    var phi_208_: f32;
    var phi_220_: bool;
    var phi_226_: bool;
    var phi_235_: bool;
    var phi_246_: f32;

    let _e17 = global;
    switch bitcast<i32>(0u) {
        default: {
            let _e21 = Params.Width;
            phi_54_ = true;
            if !((_e17.x >= _e21)) {
                let _e26 = Params.Height;
                phi_54_ = (_e17.y >= _e26);
            }
            let _e29 = phi_54_;
            if _e29 {
                break;
            }
            let _e30 = bitcast<i32>(_e17.x);
            let _e32 = bitcast<i32>(_e17.y);
            let _e33 = vec2<i32>(_e30, _e32);
            switch bitcast<i32>(0u) {
                default: {
                    phi_70_ = true;
                    if !((_e30 < 0i)) {
                        phi_70_ = (_e32 < 0i);
                    }
                    let _e41 = phi_70_;
                    phi_76_ = true;
                    if !(_e41) {
                        phi_76_ = (_e30 >= bitcast<i32>(_e21));
                    }
                    let _e46 = phi_76_;
                    phi_84_ = true;
                    if !(_e46) {
                        let _e49 = Params.Height;
                        phi_84_ = (_e32 >= bitcast<i32>(_e49));
                    }
                    let _e53 = phi_84_;
                    if _e53 {
                        phi_95_ = f32();
                        phi_96_ = f32();
                        break;
                    }
                    let _e60 = SourceState.member[((bitcast<u32>(_e32) * _e21) + bitcast<u32>(_e30))];
                    phi_95_ = _e60.H;
                    phi_96_ = _e60.V;
                    break;
                }
            }
            let _e64 = phi_95_;
            let _e66 = phi_96_;
            let _e67 = (_e33 + vec2<i32>(-1i, 0i));
            switch bitcast<i32>(0u) {
                default: {
                    phi_107_ = true;
                    if !((_e67.x < 0i)) {
                        phi_107_ = (_e67.y < 0i);
                    }
                    let _e75 = phi_107_;
                    phi_113_ = true;
                    if !(_e75) {
                        phi_113_ = (_e67.x >= bitcast<i32>(_e21));
                    }
                    let _e80 = phi_113_;
                    phi_122_ = true;
                    if !(_e80) {
                        let _e84 = Params.Height;
                        phi_122_ = (_e67.y >= bitcast<i32>(_e84));
                    }
                    let _e88 = phi_122_;
                    if _e88 {
                        phi_133_ = f32();
                        break;
                    }
                    let _e97 = SourceState.member[((bitcast<u32>(_e67.y) * _e21) + bitcast<u32>(_e67.x))].H;
                    phi_133_ = _e97;
                    break;
                }
            }
            let _e99 = phi_133_;
            let _e100 = (_e33 + vec2<i32>(1i, 0i));
            switch bitcast<i32>(0u) {
                default: {
                    phi_144_ = true;
                    if !((_e100.x < 0i)) {
                        phi_144_ = (_e100.y < 0i);
                    }
                    let _e108 = phi_144_;
                    phi_150_ = true;
                    if !(_e108) {
                        phi_150_ = (_e100.x >= bitcast<i32>(_e21));
                    }
                    let _e113 = phi_150_;
                    phi_159_ = true;
                    if !(_e113) {
                        let _e117 = Params.Height;
                        phi_159_ = (_e100.y >= bitcast<i32>(_e117));
                    }
                    let _e121 = phi_159_;
                    if _e121 {
                        phi_170_ = f32();
                        break;
                    }
                    let _e130 = SourceState.member[((bitcast<u32>(_e100.y) * _e21) + bitcast<u32>(_e100.x))].H;
                    phi_170_ = _e130;
                    break;
                }
            }
            let _e132 = phi_170_;
            let _e134 = (_e33 + vec2<i32>(0i, -1i));
            switch bitcast<i32>(0u) {
                default: {
                    phi_182_ = true;
                    if !((_e134.x < 0i)) {
                        phi_182_ = (_e134.y < 0i);
                    }
                    let _e142 = phi_182_;
                    phi_188_ = true;
                    if !(_e142) {
                        phi_188_ = (_e134.x >= bitcast<i32>(_e21));
                    }
                    let _e147 = phi_188_;
                    phi_197_ = true;
                    if !(_e147) {
                        let _e151 = Params.Height;
                        phi_197_ = (_e134.y >= bitcast<i32>(_e151));
                    }
                    let _e155 = phi_197_;
                    if _e155 {
                        phi_208_ = f32();
                        break;
                    }
                    let _e164 = SourceState.member[((bitcast<u32>(_e134.y) * _e21) + bitcast<u32>(_e134.x))].H;
                    phi_208_ = _e164;
                    break;
                }
            }
            let _e166 = phi_208_;
            let _e168 = (_e33 + vec2<i32>(0i, 1i));
            switch bitcast<i32>(0u) {
                default: {
                    phi_220_ = true;
                    if !((_e168.x < 0i)) {
                        phi_220_ = (_e168.y < 0i);
                    }
                    let _e176 = phi_220_;
                    phi_226_ = true;
                    if !(_e176) {
                        phi_226_ = (_e168.x >= bitcast<i32>(_e21));
                    }
                    let _e181 = phi_226_;
                    phi_235_ = true;
                    if !(_e181) {
                        let _e185 = Params.Height;
                        phi_235_ = (_e168.y >= bitcast<i32>(_e185));
                    }
                    let _e189 = phi_235_;
                    if _e189 {
                        phi_246_ = f32();
                        break;
                    }
                    let _e198 = SourceState.member[((bitcast<u32>(_e168.y) * _e21) + bitcast<u32>(_e168.x))].H;
                    phi_246_ = _e198;
                    break;
                }
            }
            let _e200 = phi_246_;
            let _e205 = Params.Speed;
            let _e210 = Params.Damping;
            let _e211 = ((_e66 + ((_e205 * _e205) * ((((_e99 + _e132) + _e166) + _e200) - (4f * _e64)))) * _e210);
            DestState.member[((_e17.y * _e21) + _e17.x)] = RippleState((_e64 + _e211), _e211);
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
