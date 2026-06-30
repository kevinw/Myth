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
    var phi_232_: vec3<f32>;
    var phi_599_: vec4<f32>;

    let _e80 = in_u002e_var_u002e_TEXCOORD0_1;
    let _e81 = in_u002e_var_u002e_TEXCOORD1_1;
    switch bitcast<i32>(0u) {
        default: {
            let _e84 = FragmentUniformBlock.CrtShader;
            if (_e84 >= 0.5f) {
                let _e86 = (_e80 * 1.0001f);
                let _e88 = ((_e86 * 2f) - vec2<f32>(1f, 1f));
                let _e98 = FragmentUniformBlock.TextureSize;
                let _e99 = (vec2<f32>(1f, 1f) / _e98);
                let _e100 = ((((_e88 * vec2<f32>(1f, (1f + ((_e88.x * _e88.x) * 0.05f)))) * 0.5f) + vec2<f32>(0.5f, 0.5f)) * _e98);
                let _e101 = fract(_e100);
                let _e105 = ((floor(_e100) * _e99) + (_e99 * 0.5f));
                let _e106 = clamp(_e105, vec2<f32>(0f, 0f), vec2<f32>(1f, 1f));
                let _e107 = textureSample(Texture, Sampler, _e106);
                let _e114 = textureSample(Texture, Sampler, clamp((_e105 + vec2<f32>(_e99.x, 0f)), vec2<f32>(0f, 0f), vec2<f32>(1f, 1f)));
                let _e121 = textureSample(Texture, Sampler, clamp((_e105 + vec2<f32>(0f, _e99.y)), vec2<f32>(0f, 0f), vec2<f32>(1f, 1f)));
                let _e125 = clamp((_e105 + _e99), vec2<f32>(0f, 0f), vec2<f32>(1f, 1f));
                let _e126 = textureSample(Texture, Sampler, _e125);
                let _e131 = pow((1f - _e101.x), 2.5f);
                let _e132 = pow(_e101.x, 2.5f);
                let _e137 = vec3((_e131 + _e132));
                let _e138 = (((pow(_e114.xyz, vec3<f32>(2.2f, 2.2f, 2.2f)) * _e132) + (pow(_e107.xyz, vec3<f32>(2.2f, 2.2f, 2.2f)) * _e131)) / _e137);
                let _e142 = (((pow(_e126.xyz, vec3<f32>(2.2f, 2.2f, 2.2f)) * _e132) + (pow(_e121.xyz, vec3<f32>(2.2f, 2.2f, 2.2f)) * _e131)) / _e137);
                let _e143 = dot(_e138, vec3<f32>(0.3f, 0.6f, 0.1f));
                let _e144 = dot(_e142, vec3<f32>(0.3f, 0.6f, 0.1f));
                let _e154 = FragmentUniformBlock.OutputSize;
                let _e162 = select(vec3<f32>(0f, 0f, 0f), vec3<f32>(1f, 1f, 1f), vec3((fract((floor((_e86 * _e154)).x * 0.33333334f)) > 0.333f)));
                let _e166 = (((pow(_e138, vec3<f32>(2.8f, 2.8f, 2.8f)) * 2f) - pow(_e138, vec3<f32>(3.6f, 3.6f, 3.6f))) * mix(_e162, vec3<f32>(1f, 1f, 1f), vec3((_e143 * 0.4f))));
                let _e170 = (((pow(_e142, vec3<f32>(2.8f, 2.8f, 2.8f)) * 2f) - pow(_e142, vec3<f32>(3.6f, 3.6f, 3.6f))) * mix(_e162, vec3<f32>(1f, 1f, 1f), vec3((_e144 * 0.4f))));
                let _e186 = FragmentUniformBlock.TextureSize[1i];
                phi_232_ = ((_e166 * exp2((-10f * pow(_e101.y, mix(1.5f, 2f, _e143))))) + (_e170 * exp2((-10f * pow((1f - _e101.y), mix(1.5f, 2f, _e144))))));
                if (_e186 >= 400f) {
                    phi_232_ = ((_e166 + _e170) * 0.5f);
                }
                let _e191 = phi_232_;
                let _e193 = pow(min(_e191, vec3<f32>(1f, 1f, 1f)), vec3<f32>(0.45454547f, 0.45454547f, 0.45454547f));
                let _e195 = textureSample(Texture, Sampler, _e106);
                let _e202 = textureSample(Texture, Sampler, clamp((_e105 + (vec2<f32>(-1f, 0f) / _e98)), vec2<f32>(0f, 0f), vec2<f32>(1f, 1f)));
                let _e208 = textureSample(Texture, Sampler, clamp((_e105 + (vec2<f32>(1f, 0f) / _e98)), vec2<f32>(0f, 0f), vec2<f32>(1f, 1f)));
                let _e215 = textureSample(Texture, Sampler, clamp((_e105 + (vec2<f32>(-1f, -1f) / _e98)), vec2<f32>(0f, 0f), vec2<f32>(1f, 1f)));
                let _e219 = textureSample(Texture, Sampler, _e125);
                let _e226 = textureSample(Texture, Sampler, clamp((_e105 + (vec2<f32>(-1f, 1f) / _e98)), vec2<f32>(0f, 0f), vec2<f32>(1f, 1f)));
                let _e233 = textureSample(Texture, Sampler, clamp((_e105 + (vec2<f32>(1f, -1f) / _e98)), vec2<f32>(0f, 0f), vec2<f32>(1f, 1f)));
                let _e240 = textureSample(Texture, Sampler, clamp((_e105 + (vec2<f32>(0f, -1f) / _e98)), vec2<f32>(0f, 0f), vec2<f32>(1f, 1f)));
                let _e247 = textureSample(Texture, Sampler, clamp((_e105 + (vec2<f32>(0f, 1f) / _e98)), vec2<f32>(0f, 0f), vec2<f32>(1f, 1f)));
                let _e256 = textureSample(Texture, Sampler, clamp((_e105 + (vec2<f32>(-2f, 0f) / _e98)), vec2<f32>(0f, 0f), vec2<f32>(1f, 1f)));
                let _e262 = textureSample(Texture, Sampler, clamp((_e105 + (vec2<f32>(2f, 0f) / _e98)), vec2<f32>(0f, 0f), vec2<f32>(1f, 1f)));
                let _e269 = textureSample(Texture, Sampler, clamp((_e105 + (vec2<f32>(-2f, -1f) / _e98)), vec2<f32>(0f, 0f), vec2<f32>(1f, 1f)));
                let _e276 = textureSample(Texture, Sampler, clamp((_e105 + (vec2<f32>(-1f, -2f) / _e98)), vec2<f32>(0f, 0f), vec2<f32>(1f, 1f)));
                let _e283 = textureSample(Texture, Sampler, clamp((_e105 + (vec2<f32>(1f, 2f) / _e98)), vec2<f32>(0f, 0f), vec2<f32>(1f, 1f)));
                let _e290 = textureSample(Texture, Sampler, clamp((_e105 + (vec2<f32>(2f, 1f) / _e98)), vec2<f32>(0f, 0f), vec2<f32>(1f, 1f)));
                let _e297 = textureSample(Texture, Sampler, clamp((_e105 + (vec2<f32>(-2f, 1f) / _e98)), vec2<f32>(0f, 0f), vec2<f32>(1f, 1f)));
                let _e304 = textureSample(Texture, Sampler, clamp((_e105 + (vec2<f32>(-1f, 2f) / _e98)), vec2<f32>(0f, 0f), vec2<f32>(1f, 1f)));
                let _e311 = textureSample(Texture, Sampler, clamp((_e105 + (vec2<f32>(1f, -2f) / _e98)), vec2<f32>(0f, 0f), vec2<f32>(1f, 1f)));
                let _e318 = textureSample(Texture, Sampler, clamp((_e105 + (vec2<f32>(2f, -1f) / _e98)), vec2<f32>(0f, 0f), vec2<f32>(1f, 1f)));
                let _e325 = textureSample(Texture, Sampler, clamp((_e105 + (vec2<f32>(0f, -2f) / _e98)), vec2<f32>(0f, 0f), vec2<f32>(1f, 1f)));
                let _e332 = textureSample(Texture, Sampler, clamp((_e105 + (vec2<f32>(0f, 2f) / _e98)), vec2<f32>(0f, 0f), vec2<f32>(1f, 1f)));
                let _e342 = ((_e193 + (((((pow(_e195.xyz, vec3<f32>(2.2f, 2.2f, 2.2f)) * 3f) + ((((((((pow(_e202.xyz, vec3<f32>(2.2f, 2.2f, 2.2f)) + pow(_e208.xyz, vec3<f32>(2.2f, 2.2f, 2.2f))) + pow(_e215.xyz, vec3<f32>(2.2f, 2.2f, 2.2f))) + pow(_e219.xyz, vec3<f32>(2.2f, 2.2f, 2.2f))) + pow(_e226.xyz, vec3<f32>(2.2f, 2.2f, 2.2f))) + pow(_e233.xyz, vec3<f32>(2.2f, 2.2f, 2.2f))) + pow(_e240.xyz, vec3<f32>(2.2f, 2.2f, 2.2f))) + pow(_e247.xyz, vec3<f32>(2.2f, 2.2f, 2.2f))) * 2.5f)) + ((((((((((((pow(_e256.xyz, vec3<f32>(2.2f, 2.2f, 2.2f)) + pow(_e262.xyz, vec3<f32>(2.2f, 2.2f, 2.2f))) + pow(_e269.xyz, vec3<f32>(2.2f, 2.2f, 2.2f))) + pow(_e276.xyz, vec3<f32>(2.2f, 2.2f, 2.2f))) + pow(_e283.xyz, vec3<f32>(2.2f, 2.2f, 2.2f))) + pow(_e290.xyz, vec3<f32>(2.2f, 2.2f, 2.2f))) + pow(_e297.xyz, vec3<f32>(2.2f, 2.2f, 2.2f))) + pow(_e304.xyz, vec3<f32>(2.2f, 2.2f, 2.2f))) + pow(_e311.xyz, vec3<f32>(2.2f, 2.2f, 2.2f))) + pow(_e318.xyz, vec3<f32>(2.2f, 2.2f, 2.2f))) + pow(_e325.xyz, vec3<f32>(2.2f, 2.2f, 2.2f))) + pow(_e332.xyz, vec3<f32>(2.2f, 2.2f, 2.2f))) * 1.5f)) * vec3<f32>(0.022222223f, 0.022222223f, 0.022222223f)) * 0.35f)) * mix(1f, 1.2f, dot(_e193, vec3<f32>(0.3f, 0.6f, 0.1f))));
                let _e352 = (_e86 * (vec2<f32>(1f, 1f) - _e86));
                let _e361 = pow(clamp((mix(vec3(dot(_e342, select(vec3<f32>(0.3f, 0.6f, 0.1f), vec3<f32>(0.18f, 0.72f, 0.020000001f), vec3(((length(_e342) * 0.5775f) < 0.5f))))), _e342, vec3<f32>(1.2f, 1.2f, 1.2f)) * min(pow(((_e352.x * _e352.y) * 45f), 0.15f), 1f)), vec3<f32>(0f, 0f, 0f), vec3<f32>(1f, 1f, 1f)), vec3<f32>(0.45454547f, 0.45454547f, 0.45454547f));
                phi_599_ = (vec4<f32>(_e361.x, _e361.y, _e361.z, 1f) * _e81);
                break;
            }
            let _e368 = ((_e80 * 2f) - vec2<f32>(1f, 1f));
            let _e371 = FragmentUniformBlock.TextureSize;
            let _e372 = (vec2<f32>(1f, 1f) / _e371);
            let _e374 = FragmentUniformBlock.RgbOffset;
            let _e376 = (vec2<f32>(_e374, 0f) * _e372);
            let _e379 = textureSample(Texture, Sampler, clamp((_e80 + _e376), vec2<f32>(0f, 0f), vec2<f32>(1f, 1f)));
            let _e382 = textureSample(Texture, Sampler, clamp(_e80, vec2<f32>(0f, 0f), vec2<f32>(1f, 1f)));
            let _e386 = textureSample(Texture, Sampler, clamp((_e80 - _e376), vec2<f32>(0f, 0f), vec2<f32>(1f, 1f)));
            let _e392 = textureSample(Texture, Sampler, clamp((_e80 + (vec2<f32>(1f, 0f) / _e371)), vec2<f32>(0f, 0f), vec2<f32>(1f, 1f)));
            let _e397 = textureSample(Texture, Sampler, clamp((_e80 + (vec2<f32>(-1f, 0f) / _e371)), vec2<f32>(0f, 0f), vec2<f32>(1f, 1f)));
            let _e403 = textureSample(Texture, Sampler, clamp((_e80 + (vec2<f32>(0f, 1f) / _e371)), vec2<f32>(0f, 0f), vec2<f32>(1f, 1f)));
            let _e409 = textureSample(Texture, Sampler, clamp((_e80 + (vec2<f32>(0f, -1f) / _e371)), vec2<f32>(0f, 0f), vec2<f32>(1f, 1f)));
            let _e414 = textureSample(Texture, Sampler, clamp((_e80 + _e372), vec2<f32>(0f, 0f), vec2<f32>(1f, 1f)));
            let _e421 = textureSample(Texture, Sampler, clamp((_e80 + (vec2<f32>(-1f, 1f) / _e371)), vec2<f32>(0f, 0f), vec2<f32>(1f, 1f)));
            let _e428 = textureSample(Texture, Sampler, clamp((_e80 + (vec2<f32>(1f, -1f) / _e371)), vec2<f32>(0f, 0f), vec2<f32>(1f, 1f)));
            let _e435 = textureSample(Texture, Sampler, clamp((_e80 + (vec2<f32>(-1f, -1f) / _e371)), vec2<f32>(0f, 0f), vec2<f32>(1f, 1f)));
            let _e439 = ((((((((_e392.xyz + _e397.xyz) + _e403.xyz) + _e409.xyz) + (_e414.xyz * 0.5f)) + (_e421.xyz * 0.5f)) + (_e428.xyz * 0.5f)) + (_e435.xyz * 0.5f)) * vec3<f32>(0.22222222f, 0.22222222f, 0.22222222f));
            let _e448 = FragmentUniformBlock.BloomAmount;
            let _e453 = FragmentUniformBlock.VignetteAmount;
            let _e456 = clamp(((vec3<f32>(_e379.x, _e382.y, _e386.z) + ((_e439 * smoothstep(0.35f, 1f, max(max(_e439.x, _e439.y), _e439.z))) * _e448)) * mix(1f, smoothstep(1.25f, 0.35f, dot(_e368, _e368)), _e453)), vec3<f32>(0f, 0f, 0f), vec3<f32>(1f, 1f, 1f));
            phi_599_ = (vec4<f32>(_e456.x, _e456.y, _e456.z, 1f) * _e81);
            break;
        }
    }
    let _e463 = phi_599_;
    out_u002e_var_u002e_SV_Target0_ = _e463;
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
