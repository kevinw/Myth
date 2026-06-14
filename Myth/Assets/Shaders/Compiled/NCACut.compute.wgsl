struct CellState {
    c: array<f32, 16>,
}

struct type_u002e_StructuredBuffer_u002e_CellState {
    member: array<CellState>,
}

struct type_u002e_RWStructuredBuffer_u002e_CellState {
    member: array<CellState>,
}

struct type_u002e_Params {
    Width: u32,
    Height: u32,
    CutX: u32,
    CutY: u32,
    CutWidth: u32,
    CutHeight: u32,
    Seed: u32,
}

@group(0) @binding(0) 
var<storage> SourceState: type_u002e_StructuredBuffer_u002e_CellState;
@group(1) @binding(0) 
var<storage, read_write> DestState: type_u002e_RWStructuredBuffer_u002e_CellState;
@group(2) @binding(0) 
var<uniform> Params: type_u002e_Params;
var<private> global: vec3<u32>;

fn compute_main_1() {
    var phi_65_: bool;

    let _e28 = global;
    switch bitcast<i32>(0u) {
        default: {
            let _e32 = Params.Width;
            phi_65_ = true;
            if !((_e28.x >= _e32)) {
                let _e37 = Params.Height;
                phi_65_ = (_e28.y >= _e37);
            }
            let _e40 = phi_65_;
            if _e40 {
                break;
            }
            let _e43 = ((_e28.y * _e32) + _e28.x);
            let _e45 = Params.CutX;
            let _e46 = f32(_e45);
            let _e47 = f32(_e28.x);
            let _e51 = Params.CutWidth;
            let _e57 = Params.CutY;
            let _e58 = f32(_e57);
            let _e59 = f32(_e28.y);
            let _e63 = Params.CutHeight;
            let _e70 = Params.Seed;
            let _e75 = (((_e28.x * 374761393u) + (_e28.y * 668265263u)) + (_e70 * 2654435761u));
            let _e79 = ((_e75 ^ (_e75 >> bitcast<u32>(13u))) * 1274126177u);
            if ((max(max(((_e46 - _e47) - 1f), (_e47 - (_e46 + f32(_e51)))), max(((_e58 - _e59) - 1f), (_e59 - (_e58 + f32(_e63))))) + (((f32(((_e79 ^ (_e79 >> bitcast<u32>(16u))) & 16777215u)) * 0.000000059604645f) - 0.5f) * 4f)) < 0f) {
                DestState.member[_e43] = CellState(array<f32, 16>(f32(), f32(), f32(), f32(), f32(), f32(), f32(), f32(), f32(), f32(), f32(), f32(), f32(), f32(), f32(), f32()));
            } else {
                let _e94 = SourceState.member[_e43];
                DestState.member[_e43] = _e94;
            }
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
