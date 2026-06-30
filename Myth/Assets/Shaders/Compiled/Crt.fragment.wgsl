struct type_u002e_FragmentUniformBlock {
    TextureSize: vec2<f32>,
    OutputSize: vec2<f32>,
    BloomAmount: f32,
    VignetteAmount: f32,
    RgbOffset: f32,
    CrtShader: f32,
}

@group(3) @binding(0) 
var<uniform> FragmentUniformBlock: type_u002e_FragmentUniformBlock;
@group(2) @binding(0) 
var Texture: texture_2d<f32>;
@group(2) @binding(100) 
var Sampler: sampler;
var<private> in_u002e_var_u002e_TEXCOORD0_1: vec2<f32>;
var<private> in_u002e_var_u002e_TEXCOORD1_1: vec4<f32>;
var<private> out_u002e_var_u002e_SV_Target0_: vec4<f32>;

fn fragment_main_1() {
    var phi_233_: vec3<f32>;
    var phi_611_: vec4<f32>;

    let _e81 = in_u002e_var_u002e_TEXCOORD0_1;
    let _e82 = in_u002e_var_u002e_TEXCOORD1_1;
    switch bitcast<i32>(0u) {
        default: {
            let _e85 = FragmentUniformBlock.CrtShader;
            if (_e85 >= 0.5f) {
                let _e87 = (_e81 * 1.0001f);
                let _e89 = ((_e87 * 2f) - vec2<f32>(1f, 1f));
                let _e99 = FragmentUniformBlock.TextureSize;
                let _e100 = (vec2<f32>(1f, 1f) / _e99);
                let _e101 = ((((_e89 * vec2<f32>(1f, (1f + ((_e89.x * _e89.x) * 0.05f)))) * 0.5f) + vec2<f32>(0.5f, 0.5f)) * _e99);
                let _e102 = fract(_e101);
                let _e106 = ((floor(_e101) * _e100) + (_e100 * 0.5f));
                let _e107 = clamp(_e106, vec2<f32>(0f, 0f), vec2<f32>(1f, 1f));
                let _e108 = textureSample(Texture, Sampler, _e107);
                let _e115 = textureSample(Texture, Sampler, clamp((_e106 + vec2<f32>(_e100.x, 0f)), vec2<f32>(0f, 0f), vec2<f32>(1f, 1f)));
                let _e122 = textureSample(Texture, Sampler, clamp((_e106 + vec2<f32>(0f, _e100.y)), vec2<f32>(0f, 0f), vec2<f32>(1f, 1f)));
                let _e126 = clamp((_e106 + _e100), vec2<f32>(0f, 0f), vec2<f32>(1f, 1f));
                let _e127 = textureSample(Texture, Sampler, _e126);
                let _e132 = pow((1f - _e102.x), 2.5f);
                let _e133 = pow(_e102.x, 2.5f);
                let _e138 = vec3((_e132 + _e133));
                let _e139 = (((pow(_e115.xyz, vec3<f32>(2.2f, 2.2f, 2.2f)) * _e133) + (pow(_e108.xyz, vec3<f32>(2.2f, 2.2f, 2.2f)) * _e132)) / _e138);
                let _e143 = (((pow(_e127.xyz, vec3<f32>(2.2f, 2.2f, 2.2f)) * _e133) + (pow(_e122.xyz, vec3<f32>(2.2f, 2.2f, 2.2f)) * _e132)) / _e138);
                let _e144 = dot(_e139, vec3<f32>(0.3f, 0.6f, 0.1f));
                let _e145 = dot(_e143, vec3<f32>(0.3f, 0.6f, 0.1f));
                let _e155 = FragmentUniformBlock.OutputSize;
                let _e163 = select(vec3<f32>(0f, 0f, 0f), vec3<f32>(1f, 1f, 1f), vec3((fract((floor((_e87 * _e155)).x * 0.33333334f)) > 0.333f)));
                let _e167 = (((pow(_e139, vec3<f32>(2.8f, 2.8f, 2.8f)) * 2f) - pow(_e139, vec3<f32>(3.6f, 3.6f, 3.6f))) * mix(_e163, vec3<f32>(1f, 1f, 1f), vec3((_e144 * 0.4f))));
                let _e171 = (((pow(_e143, vec3<f32>(2.8f, 2.8f, 2.8f)) * 2f) - pow(_e143, vec3<f32>(3.6f, 3.6f, 3.6f))) * mix(_e163, vec3<f32>(1f, 1f, 1f), vec3((_e145 * 0.4f))));
                let _e187 = FragmentUniformBlock.TextureSize[1i];
                phi_233_ = ((_e167 * exp2((-10f * pow(_e102.y, mix(1.5f, 2f, _e144))))) + (_e171 * exp2((-10f * pow((1f - _e102.y), mix(1.5f, 2f, _e145))))));
                if (_e187 >= 400f) {
                    phi_233_ = ((_e167 + _e171) * 0.5f);
                }
                let _e192 = phi_233_;
                let _e194 = pow(min(_e192, vec3<f32>(1f, 1f, 1f)), vec3<f32>(0.45454547f, 0.45454547f, 0.45454547f));
                let _e196 = textureSample(Texture, Sampler, _e107);
                let _e203 = textureSample(Texture, Sampler, clamp((_e106 + (vec2<f32>(-1f, 0f) / _e99)), vec2<f32>(0f, 0f), vec2<f32>(1f, 1f)));
                let _e209 = textureSample(Texture, Sampler, clamp((_e106 + (vec2<f32>(1f, 0f) / _e99)), vec2<f32>(0f, 0f), vec2<f32>(1f, 1f)));
                let _e216 = textureSample(Texture, Sampler, clamp((_e106 + (vec2<f32>(-1f, -1f) / _e99)), vec2<f32>(0f, 0f), vec2<f32>(1f, 1f)));
                let _e220 = textureSample(Texture, Sampler, _e126);
                let _e227 = textureSample(Texture, Sampler, clamp((_e106 + (vec2<f32>(-1f, 1f) / _e99)), vec2<f32>(0f, 0f), vec2<f32>(1f, 1f)));
                let _e234 = textureSample(Texture, Sampler, clamp((_e106 + (vec2<f32>(1f, -1f) / _e99)), vec2<f32>(0f, 0f), vec2<f32>(1f, 1f)));
                let _e241 = textureSample(Texture, Sampler, clamp((_e106 + (vec2<f32>(0f, -1f) / _e99)), vec2<f32>(0f, 0f), vec2<f32>(1f, 1f)));
                let _e248 = textureSample(Texture, Sampler, clamp((_e106 + (vec2<f32>(0f, 1f) / _e99)), vec2<f32>(0f, 0f), vec2<f32>(1f, 1f)));
                let _e257 = textureSample(Texture, Sampler, clamp((_e106 + (vec2<f32>(-2f, 0f) / _e99)), vec2<f32>(0f, 0f), vec2<f32>(1f, 1f)));
                let _e263 = textureSample(Texture, Sampler, clamp((_e106 + (vec2<f32>(2f, 0f) / _e99)), vec2<f32>(0f, 0f), vec2<f32>(1f, 1f)));
                let _e270 = textureSample(Texture, Sampler, clamp((_e106 + (vec2<f32>(-2f, -1f) / _e99)), vec2<f32>(0f, 0f), vec2<f32>(1f, 1f)));
                let _e277 = textureSample(Texture, Sampler, clamp((_e106 + (vec2<f32>(-1f, -2f) / _e99)), vec2<f32>(0f, 0f), vec2<f32>(1f, 1f)));
                let _e284 = textureSample(Texture, Sampler, clamp((_e106 + (vec2<f32>(1f, 2f) / _e99)), vec2<f32>(0f, 0f), vec2<f32>(1f, 1f)));
                let _e291 = textureSample(Texture, Sampler, clamp((_e106 + (vec2<f32>(2f, 1f) / _e99)), vec2<f32>(0f, 0f), vec2<f32>(1f, 1f)));
                let _e298 = textureSample(Texture, Sampler, clamp((_e106 + (vec2<f32>(-2f, 1f) / _e99)), vec2<f32>(0f, 0f), vec2<f32>(1f, 1f)));
                let _e305 = textureSample(Texture, Sampler, clamp((_e106 + (vec2<f32>(-1f, 2f) / _e99)), vec2<f32>(0f, 0f), vec2<f32>(1f, 1f)));
                let _e312 = textureSample(Texture, Sampler, clamp((_e106 + (vec2<f32>(1f, -2f) / _e99)), vec2<f32>(0f, 0f), vec2<f32>(1f, 1f)));
                let _e319 = textureSample(Texture, Sampler, clamp((_e106 + (vec2<f32>(2f, -1f) / _e99)), vec2<f32>(0f, 0f), vec2<f32>(1f, 1f)));
                let _e326 = textureSample(Texture, Sampler, clamp((_e106 + (vec2<f32>(0f, -2f) / _e99)), vec2<f32>(0f, 0f), vec2<f32>(1f, 1f)));
                let _e333 = textureSample(Texture, Sampler, clamp((_e106 + (vec2<f32>(0f, 2f) / _e99)), vec2<f32>(0f, 0f), vec2<f32>(1f, 1f)));
                let _e343 = ((_e194 + (((((pow(_e196.xyz, vec3<f32>(2.2f, 2.2f, 2.2f)) * 3f) + ((((((((pow(_e203.xyz, vec3<f32>(2.2f, 2.2f, 2.2f)) + pow(_e209.xyz, vec3<f32>(2.2f, 2.2f, 2.2f))) + pow(_e216.xyz, vec3<f32>(2.2f, 2.2f, 2.2f))) + pow(_e220.xyz, vec3<f32>(2.2f, 2.2f, 2.2f))) + pow(_e227.xyz, vec3<f32>(2.2f, 2.2f, 2.2f))) + pow(_e234.xyz, vec3<f32>(2.2f, 2.2f, 2.2f))) + pow(_e241.xyz, vec3<f32>(2.2f, 2.2f, 2.2f))) + pow(_e248.xyz, vec3<f32>(2.2f, 2.2f, 2.2f))) * 2.5f)) + ((((((((((((pow(_e257.xyz, vec3<f32>(2.2f, 2.2f, 2.2f)) + pow(_e263.xyz, vec3<f32>(2.2f, 2.2f, 2.2f))) + pow(_e270.xyz, vec3<f32>(2.2f, 2.2f, 2.2f))) + pow(_e277.xyz, vec3<f32>(2.2f, 2.2f, 2.2f))) + pow(_e284.xyz, vec3<f32>(2.2f, 2.2f, 2.2f))) + pow(_e291.xyz, vec3<f32>(2.2f, 2.2f, 2.2f))) + pow(_e298.xyz, vec3<f32>(2.2f, 2.2f, 2.2f))) + pow(_e305.xyz, vec3<f32>(2.2f, 2.2f, 2.2f))) + pow(_e312.xyz, vec3<f32>(2.2f, 2.2f, 2.2f))) + pow(_e319.xyz, vec3<f32>(2.2f, 2.2f, 2.2f))) + pow(_e326.xyz, vec3<f32>(2.2f, 2.2f, 2.2f))) + pow(_e333.xyz, vec3<f32>(2.2f, 2.2f, 2.2f))) * 1.5f)) * vec3<f32>(0.022222223f, 0.022222223f, 0.022222223f)) * 0.35f)) * mix(1f, 1.2f, dot(_e194, vec3<f32>(0.3f, 0.6f, 0.1f))));
                let _e353 = (_e87 * (vec2<f32>(1f, 1f) - _e87));
                let _e362 = pow(clamp((mix(vec3(dot(_e343, select(vec3<f32>(0.3f, 0.6f, 0.1f), vec3<f32>(0.18f, 0.72f, 0.020000001f), vec3(((length(_e343) * 0.5775f) < 0.5f))))), _e343, vec3<f32>(1.2f, 1.2f, 1.2f)) * min(pow(((_e353.x * _e353.y) * 45f), 0.15f), 1f)), vec3<f32>(0f, 0f, 0f), vec3<f32>(1f, 1f, 1f)), vec3<f32>(0.45454547f, 0.45454547f, 0.45454547f));
                phi_611_ = (vec4<f32>(_e362.x, _e362.y, _e362.z, 1f) * _e82);
                break;
            }
            let _e369 = ((_e81 * 2f) - vec2<f32>(1f, 1f));
            let _e372 = FragmentUniformBlock.TextureSize;
            let _e373 = (vec2<f32>(1f, 1f) / _e372);
            let _e375 = FragmentUniformBlock.RgbOffset;
            let _e377 = (vec2<f32>(_e375, 0f) * _e373);
            let _e380 = textureSample(Texture, Sampler, clamp((_e81 + _e377), vec2<f32>(0f, 0f), vec2<f32>(1f, 1f)));
            let _e383 = textureSample(Texture, Sampler, clamp(_e81, vec2<f32>(0f, 0f), vec2<f32>(1f, 1f)));
            let _e387 = textureSample(Texture, Sampler, clamp((_e81 - _e377), vec2<f32>(0f, 0f), vec2<f32>(1f, 1f)));
            let _e393 = textureSample(Texture, Sampler, clamp((_e81 + (vec2<f32>(1f, 0f) / _e372)), vec2<f32>(0f, 0f), vec2<f32>(1f, 1f)));
            let _e398 = textureSample(Texture, Sampler, clamp((_e81 + (vec2<f32>(-1f, 0f) / _e372)), vec2<f32>(0f, 0f), vec2<f32>(1f, 1f)));
            let _e404 = textureSample(Texture, Sampler, clamp((_e81 + (vec2<f32>(0f, 1f) / _e372)), vec2<f32>(0f, 0f), vec2<f32>(1f, 1f)));
            let _e410 = textureSample(Texture, Sampler, clamp((_e81 + (vec2<f32>(0f, -1f) / _e372)), vec2<f32>(0f, 0f), vec2<f32>(1f, 1f)));
            let _e415 = textureSample(Texture, Sampler, clamp((_e81 + _e373), vec2<f32>(0f, 0f), vec2<f32>(1f, 1f)));
            let _e422 = textureSample(Texture, Sampler, clamp((_e81 + (vec2<f32>(-1f, 1f) / _e372)), vec2<f32>(0f, 0f), vec2<f32>(1f, 1f)));
            let _e429 = textureSample(Texture, Sampler, clamp((_e81 + (vec2<f32>(1f, -1f) / _e372)), vec2<f32>(0f, 0f), vec2<f32>(1f, 1f)));
            let _e436 = textureSample(Texture, Sampler, clamp((_e81 + (vec2<f32>(-1f, -1f) / _e372)), vec2<f32>(0f, 0f), vec2<f32>(1f, 1f)));
            let _e440 = ((((((((_e393.xyz + _e398.xyz) + _e404.xyz) + _e410.xyz) + (_e415.xyz * 0.5f)) + (_e422.xyz * 0.5f)) + (_e429.xyz * 0.5f)) + (_e436.xyz * 0.5f)) * vec3<f32>(0.22222222f, 0.22222222f, 0.22222222f));
            let _e449 = FragmentUniformBlock.BloomAmount;
            let _e454 = FragmentUniformBlock.VignetteAmount;
            let _e460 = FragmentUniformBlock.TextureSize[1i];
            let _e469 = clamp((((vec3<f32>(_e380.x, _e383.y, _e387.z) + ((_e440 * smoothstep(0.35f, 1f, max(max(_e440.x, _e440.y), _e440.z))) * _e449)) * mix(1f, smoothstep(1.25f, 0.35f, dot(_e369, _e369)), _e454)) * (1f - ((0.5f + (0.5f * cos(((_e81.y * _e460) * 6.2831855f)))) * 0.1f))), vec3<f32>(0f, 0f, 0f), vec3<f32>(1f, 1f, 1f));
            phi_611_ = (vec4<f32>(_e469.x, _e469.y, _e469.z, 1f) * _e82);
            break;
        }
    }
    let _e476 = phi_611_;
    out_u002e_var_u002e_SV_Target0_ = _e476;
    return;
}

@fragment 
fn fragment_main(@location(0) in_u002e_var_u002e_TEXCOORD0_: vec2<f32>, @location(1) in_u002e_var_u002e_TEXCOORD1_: vec4<f32>) -> @location(0) vec4<f32> {
    in_u002e_var_u002e_TEXCOORD0_1 = in_u002e_var_u002e_TEXCOORD0_;
    in_u002e_var_u002e_TEXCOORD1_1 = in_u002e_var_u002e_TEXCOORD1_;
    fragment_main_1();
    let _e5 = out_u002e_var_u002e_SV_Target0_;
    return _e5;
}
