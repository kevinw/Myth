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
    OriginX: u32,
    OriginY: u32,
}

@group(0) @binding(0) 
var<storage> SourceState: type_u002e_StructuredBuffer_u002e_RippleState;
@group(1) @binding(0) 
var<storage, read_write> DestState: type_u002e_RWStructuredBuffer_u002e_RippleState;
@group(2) @binding(0) 
var<uniform> Params: type_u002e_Params;
var<private> global: vec3<u32>;

fn compute_main_1() {
    var phi_67_: bool;
    var phi_81_: bool;
    var phi_91_: bool;
    var phi_98_: f32;
    var phi_99_: f32;
    var phi_110_: bool;
    var phi_115_: bool;
    var phi_124_: bool;
    var phi_135_: f32;
    var phi_146_: bool;
    var phi_151_: bool;
    var phi_160_: bool;
    var phi_171_: f32;
    var phi_183_: bool;
    var phi_188_: bool;
    var phi_197_: bool;
    var phi_208_: f32;
    var phi_220_: bool;
    var phi_225_: bool;
    var phi_234_: bool;
    var phi_245_: f32;

    let _e19 = global;
    switch bitcast<i32>(0u) {
        default: {
            let _e22 = Params.OriginX;
            let _e25 = bitcast<i32>((_e22 + _e19.x));
            let _e27 = Params.OriginY;
            let _e30 = bitcast<i32>((_e27 + _e19.y));
            let _e31 = vec2<i32>(_e25, _e30);
            let _e33 = Params.Width;
            let _e34 = bitcast<i32>(_e33);
            let _e35 = (_e25 >= _e34);
            phi_67_ = true;
            if !(_e35) {
                let _e38 = Params.Height;
                phi_67_ = (_e30 >= bitcast<i32>(_e38));
            }
            let _e42 = phi_67_;
            if _e42 {
                break;
            }
            let _e46 = ((bitcast<u32>(_e30) * _e33) + bitcast<u32>(_e25));
            switch bitcast<i32>(0u) {
                default: {
                    phi_81_ = true;
                    if !((_e25 < 0i)) {
                        phi_81_ = (_e30 < 0i);
                    }
                    let _e52 = phi_81_;
                    phi_91_ = true;
                    if !(select(true, _e35, !(_e52))) {
                        let _e57 = Params.Height;
                        phi_91_ = (_e30 >= bitcast<i32>(_e57));
                    }
                    let _e61 = phi_91_;
                    if _e61 {
                        phi_98_ = f32();
                        phi_99_ = f32();
                        break;
                    }
                    let _e64 = SourceState.member[_e46];
                    phi_98_ = _e64.H;
                    phi_99_ = _e64.V;
                    break;
                }
            }
            let _e68 = phi_98_;
            let _e70 = phi_99_;
            let _e71 = (_e31 + vec2<i32>(-1i, 0i));
            switch bitcast<i32>(0u) {
                default: {
                    phi_110_ = true;
                    if !((_e71.x < 0i)) {
                        phi_110_ = (_e71.y < 0i);
                    }
                    let _e79 = phi_110_;
                    phi_115_ = true;
                    if !(_e79) {
                        phi_115_ = (_e71.x >= _e34);
                    }
                    let _e83 = phi_115_;
                    phi_124_ = true;
                    if !(_e83) {
                        let _e87 = Params.Height;
                        phi_124_ = (_e71.y >= bitcast<i32>(_e87));
                    }
                    let _e91 = phi_124_;
                    if _e91 {
                        phi_135_ = f32();
                        break;
                    }
                    let _e100 = SourceState.member[((bitcast<u32>(_e71.y) * _e33) + bitcast<u32>(_e71.x))].H;
                    phi_135_ = _e100;
                    break;
                }
            }
            let _e102 = phi_135_;
            let _e103 = (_e31 + vec2<i32>(1i, 0i));
            switch bitcast<i32>(0u) {
                default: {
                    phi_146_ = true;
                    if !((_e103.x < 0i)) {
                        phi_146_ = (_e103.y < 0i);
                    }
                    let _e111 = phi_146_;
                    phi_151_ = true;
                    if !(_e111) {
                        phi_151_ = (_e103.x >= _e34);
                    }
                    let _e115 = phi_151_;
                    phi_160_ = true;
                    if !(_e115) {
                        let _e119 = Params.Height;
                        phi_160_ = (_e103.y >= bitcast<i32>(_e119));
                    }
                    let _e123 = phi_160_;
                    if _e123 {
                        phi_171_ = f32();
                        break;
                    }
                    let _e132 = SourceState.member[((bitcast<u32>(_e103.y) * _e33) + bitcast<u32>(_e103.x))].H;
                    phi_171_ = _e132;
                    break;
                }
            }
            let _e134 = phi_171_;
            let _e136 = (_e31 + vec2<i32>(0i, -1i));
            switch bitcast<i32>(0u) {
                default: {
                    phi_183_ = true;
                    if !((_e136.x < 0i)) {
                        phi_183_ = (_e136.y < 0i);
                    }
                    let _e144 = phi_183_;
                    phi_188_ = true;
                    if !(_e144) {
                        phi_188_ = (_e136.x >= _e34);
                    }
                    let _e148 = phi_188_;
                    phi_197_ = true;
                    if !(_e148) {
                        let _e152 = Params.Height;
                        phi_197_ = (_e136.y >= bitcast<i32>(_e152));
                    }
                    let _e156 = phi_197_;
                    if _e156 {
                        phi_208_ = f32();
                        break;
                    }
                    let _e165 = SourceState.member[((bitcast<u32>(_e136.y) * _e33) + bitcast<u32>(_e136.x))].H;
                    phi_208_ = _e165;
                    break;
                }
            }
            let _e167 = phi_208_;
            let _e169 = (_e31 + vec2<i32>(0i, 1i));
            switch bitcast<i32>(0u) {
                default: {
                    phi_220_ = true;
                    if !((_e169.x < 0i)) {
                        phi_220_ = (_e169.y < 0i);
                    }
                    let _e177 = phi_220_;
                    phi_225_ = true;
                    if !(_e177) {
                        phi_225_ = (_e169.x >= _e34);
                    }
                    let _e181 = phi_225_;
                    phi_234_ = true;
                    if !(_e181) {
                        let _e185 = Params.Height;
                        phi_234_ = (_e169.y >= bitcast<i32>(_e185));
                    }
                    let _e189 = phi_234_;
                    if _e189 {
                        phi_245_ = f32();
                        break;
                    }
                    let _e198 = SourceState.member[((bitcast<u32>(_e169.y) * _e33) + bitcast<u32>(_e169.x))].H;
                    phi_245_ = _e198;
                    break;
                }
            }
            let _e200 = phi_245_;
            let _e205 = Params.Speed;
            let _e210 = Params.Damping;
            let _e211 = ((_e70 + ((_e205 * _e205) * ((((_e102 + _e134) + _e167) + _e200) - (4f * _e68)))) * _e210);
            DestState.member[_e46] = RippleState((_e68 + _e211), _e211);
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
