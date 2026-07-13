struct CellState {
    c: array<vec4<f32>, 4>,
}

struct type_u002e_StructuredBuffer_u002e_CellState {
    member: array<CellState>,
}

struct PerturbStamp {
    From: vec2<f32>,
    To: vec2<f32>,
    Radius: f32,
    Strength: f32,
}

struct type_u002e_StructuredBuffer_u002e_PerturbStamp {
    member: array<PerturbStamp>,
}

struct type_u002e_RWStructuredBuffer_u002e_CellState {
    member: array<CellState>,
}

struct type_u002e_Params {
    Width: u32,
    Height: u32,
    StampCount: u32,
    Padding: u32,
}

@group(0) @binding(0) 
var<storage> SourceState: type_u002e_StructuredBuffer_u002e_CellState;
@group(0) @binding(1) 
var<storage> Stamps: type_u002e_StructuredBuffer_u002e_PerturbStamp;
@group(1) @binding(0) 
var<storage, read_write> DestState: type_u002e_RWStructuredBuffer_u002e_CellState;
@group(2) @binding(0) 
var<uniform> Params: type_u002e_Params;
var<private> global: vec3<u32>;

fn compute_main_1() {
    var phi_62_: bool;
    var phi_66_: f32;
    var phi_69_: u32;
    var local: f32;

    let _e17 = global;
    switch bitcast<i32>(0u) {
        default: {
            let _e21 = Params.Width;
            phi_62_ = true;
            if !((_e17.x >= _e21)) {
                let _e26 = Params.Height;
                phi_62_ = (_e17.y >= _e26);
            }
            let _e29 = phi_62_;
            if _e29 {
                break;
            }
            phi_66_ = 0f;
            phi_69_ = 0u;
            loop {
                let _e31 = phi_66_;
                let _e33 = phi_69_;
                let _e35 = Params.StampCount;
                local = _e31;
                if (_e33 < _e35) {
                    continue;
                } else {
                    break;
                }
                continuing {
                    let _e39 = Stamps.member[_e33];
                    let _e45 = vec2<f32>(_e17.xy);
                    let _e46 = (_e39.To - _e39.From);
                    let _e60 = clamp((1f - (length((_e45 - (_e39.From + (_e46 * clamp((dot((_e45 - _e39.From), _e46) / max(dot(_e46, _e46), 0.0001f)), 0f, 1f))))) / max(_e39.Radius, 0.0001f))), 0f, 1f);
                    phi_66_ = max(_e31, (clamp(_e39.Strength, 0f, 1f) * ((_e60 * _e60) * (3f - (2f * _e60)))));
                    phi_69_ = (_e33 + 1u);
                }
            }
            let _e71 = ((_e17.y * _e21) + _e17.x);
            let _e74 = SourceState.member[_e71];
            let _e81 = local;
            let _e82 = (1f - _e81);
            DestState.member[_e71] = CellState(array<vec4<f32>, 4>((_e74.c[0] * _e82), (_e74.c[1] * _e82), (_e74.c[2] * _e82), (_e74.c[3] * _e82)));
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
