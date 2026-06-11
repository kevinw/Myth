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
var Output: texture_storage_2d<rgba8unorm,write>;
@group(2) @binding(0) 
var<uniform> Params: type_u002e_Params;
var<private> global: vec3<u32>;

fn compute_main_1() {
    var phi_58_: bool;

    let _e15 = global;
    switch bitcast<i32>(0u) {
        default: {
            let _e18 = global[0i];
            let _e20 = Params.Width;
            phi_58_ = true;
            if !((_e18 >= _e20)) {
                let _e24 = global[1i];
                let _e26 = Params.Height;
                phi_58_ = (_e24 >= _e26);
            }
            let _e29 = phi_58_;
            if _e29 {
                break;
            }
            let _e31 = global[1i];
            let _e36 = SourceState.member[((_e31 * _e20) + _e18)];
            let _e45 = clamp(_e36.c[3], 0f, 1f);
            let _e49 = clamp(vec3<f32>(_e36.c[4], _e36.c[5], _e36.c[6]), vec3<f32>(0f, 0f, 0f), vec3<f32>(1f, 1f, 1f));
            let _e57 = ((clamp(vec3<f32>(_e36.c[0], _e36.c[1], _e36.c[2]), vec3<f32>(0f, 0f, 0f), vec3<f32>(1f, 1f, 1f)) * _e45) + (_e49 * 0.35f));
            textureStore(Output, _e15.xy, vec4<f32>(_e57.x, _e57.y, _e57.z, clamp((_e45 + (max(_e49.x, max(_e49.y, _e49.z)) * 0.15f)), 0f, 1f)));
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
