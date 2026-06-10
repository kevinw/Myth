struct CellState {
    c: array<f32, 16>,
}

struct type_u002e_StructuredBuffer_u002e_CellState {
    member: array<CellState>,
}

struct type_u002e_Params {
    Width: u32,
    Height: u32,
}

@group(0) @binding(0) 
var<storage> SourceState: type_u002e_StructuredBuffer_u002e_CellState;
@group(1) @binding(0) 
var Output: texture_storage_2d<rgba32float,read_write>;
@group(2) @binding(0) 
var<uniform> Params: type_u002e_Params;
var<private> global: vec3<u32>;

fn compute_main_1() {
    var phi_55_: bool;

    let _e15 = global;
    switch bitcast<i32>(0u) {
        default: {
            let _e19 = Params.Width;
            phi_55_ = true;
            if !((_e15.x >= _e19)) {
                let _e24 = Params.Height;
                phi_55_ = (_e15.y >= _e24);
            }
            let _e27 = phi_55_;
            if _e27 {
                break;
            }
            let _e33 = SourceState.member[((_e15.y * _e19) + _e15.x)];
            let _e42 = clamp(_e33.c[3], 0f, 1f);
            let _e46 = clamp(vec3<f32>(_e33.c[4], _e33.c[5], _e33.c[6]), vec3<f32>(0f, 0f, 0f), vec3<f32>(1f, 1f, 1f));
            let _e54 = ((clamp(vec3<f32>(_e33.c[0], _e33.c[1], _e33.c[2]), vec3<f32>(0f, 0f, 0f), vec3<f32>(1f, 1f, 1f)) * _e42) + (_e46 * 0.35f));
            textureStore(Output, _e15.xy, vec4<f32>(_e54.x, _e54.y, _e54.z, clamp((_e42 + (max(_e46.x, max(_e46.y, _e46.z)) * 0.15f)), 0f, 1f)));
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
