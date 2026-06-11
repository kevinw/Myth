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
    HitX: f32,
    HitY: f32,
    HitRadius: f32,
    HitStrength: f32,
}

@group(0) @binding(0) 
var<storage> SourceState: type_u002e_StructuredBuffer_u002e_RippleState;
@group(1) @binding(0) 
var<storage, read_write> DestState: type_u002e_RWStructuredBuffer_u002e_RippleState;
@group(2) @binding(0) 
var<uniform> Params: type_u002e_Params;
var<private> global: vec3<u32>;

fn compute_main_1() {
    var phi_50_: bool;
    var phi_83_: f32;

    let _e13 = global;
    switch bitcast<i32>(0u) {
        default: {
            let _e17 = Params.Width;
            phi_50_ = true;
            if !((_e13.x >= _e17)) {
                let _e22 = Params.Height;
                phi_50_ = (_e13.y >= _e22);
            }
            let _e25 = phi_50_;
            if _e25 {
                break;
            }
            let _e28 = ((_e13.y * _e17) + _e13.x);
            let _e31 = SourceState.member[_e28];
            let _e36 = Params.HitX;
            let _e37 = (f32(_e13.x) - _e36);
            let _e40 = Params.HitY;
            let _e41 = (f32(_e13.y) - _e40);
            let _e45 = sqrt(((_e37 * _e37) + (_e41 * _e41)));
            let _e47 = Params.HitRadius;
            phi_83_ = _e31.H;
            if (_e45 < _e47) {
                let _e52 = Params.HitStrength;
                phi_83_ = (_e31.H + (_e52 * (1f - (_e45 / _e47))));
            }
            let _e56 = phi_83_;
            DestState.member[_e28] = RippleState(_e56, _e31.V);
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
