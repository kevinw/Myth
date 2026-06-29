struct type_u002e_UniformBlock {
    Matrix: mat4x4<f32>,
}

struct VertexOutput {
    @location(0) member: vec2<f32>,
    @location(1) member_1: vec4<f32>,
    @location(2) member_2: vec4<f32>,
    @builtin(position) member_3: vec4<f32>,
}

@group(1) @binding(0) 
var<uniform> UniformBlock: type_u002e_UniformBlock;
var<private> in_u002e_var_u002e_TEXCOORD0_1: vec2<f32>;
var<private> in_u002e_var_u002e_TEXCOORD1_1: vec2<f32>;
var<private> in_u002e_var_u002e_TEXCOORD2_1: vec4<f32>;
var<private> in_u002e_var_u002e_TEXCOORD4_1: vec4<f32>;
var<private> out_u002e_var_u002e_TEXCOORD0_: vec2<f32>;
var<private> out_u002e_var_u002e_TEXCOORD1_: vec4<f32>;
var<private> out_u002e_var_u002e_TEXCOORD4_: vec4<f32>;
var<private> global: vec4<f32> = vec4<f32>(0f, 0f, 0f, 1f);

fn vertex_main_1() {
    let _e12 = in_u002e_var_u002e_TEXCOORD0_1;
    let _e13 = in_u002e_var_u002e_TEXCOORD1_1;
    let _e14 = in_u002e_var_u002e_TEXCOORD2_1;
    let _e15 = in_u002e_var_u002e_TEXCOORD4_1;
    let _e17 = UniformBlock.Matrix;
    out_u002e_var_u002e_TEXCOORD0_ = _e13;
    out_u002e_var_u002e_TEXCOORD1_ = _e14;
    out_u002e_var_u002e_TEXCOORD4_ = _e15;
    global = (vec4<f32>(_e12.x, _e12.y, 0f, 1f) * transpose(_e17));
    return;
}

@vertex 
fn vertex_main(@location(0) in_u002e_var_u002e_TEXCOORD0_: vec2<f32>, @location(1) in_u002e_var_u002e_TEXCOORD1_: vec2<f32>, @location(2) in_u002e_var_u002e_TEXCOORD2_: vec4<f32>, @location(3) in_u002e_var_u002e_TEXCOORD4_: vec4<f32>) -> VertexOutput {
    in_u002e_var_u002e_TEXCOORD0_1 = in_u002e_var_u002e_TEXCOORD0_;
    in_u002e_var_u002e_TEXCOORD1_1 = in_u002e_var_u002e_TEXCOORD1_;
    in_u002e_var_u002e_TEXCOORD2_1 = in_u002e_var_u002e_TEXCOORD2_;
    in_u002e_var_u002e_TEXCOORD4_1 = in_u002e_var_u002e_TEXCOORD4_;
    vertex_main_1();
    let _e13 = global.y;
    global.y = -(_e13);
    let _e15 = out_u002e_var_u002e_TEXCOORD0_;
    let _e16 = out_u002e_var_u002e_TEXCOORD1_;
    let _e17 = out_u002e_var_u002e_TEXCOORD4_;
    let _e18 = global;
    return VertexOutput(_e15, _e16, _e17, _e18);
}
