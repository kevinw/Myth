struct type_u002e_FragmentUniformBlock {
    TextureSizeCamera: vec4<f32>,
    GlitchSettings: vec4<f32>,
    TimePadding: vec4<f32>,
}

@group(3) @binding(0) 
var<uniform> FragmentUniformBlock: type_u002e_FragmentUniformBlock;
@group(2) @binding(0) 
var Texture: texture_2d<f32>;
@group(2) @binding(100) 
var Sampler: sampler;
@group(2) @binding(1) 
var TileFieldTexture: texture_2d<f32>;
@group(2) @binding(101) 
var TileFieldSampler: sampler;
var<private> in_u002e_var_u002e_TEXCOORD0_1: vec2<f32>;
var<private> in_u002e_var_u002e_TEXCOORD1_1: vec4<f32>;
var<private> global: vec4<f32>;
var<private> out_u002e_var_u002e_SV_Target0_: vec4<f32>;

fn fragment_main_1() {
    var phi_281_: vec2<f32>;
    var phi_315_: f32;
    var phi_318_: vec3<f32>;
    var phi_320_: i32;
    var phi_332_: bool;
    var phi_361_: bool;
    var phi_366_: bool;
    var phi_316_: f32;
    var phi_319_: vec3<f32>;
    var local: vec3<f32>;
    var phi_420_: vec4<f32>;

    let _e85 = in_u002e_var_u002e_TEXCOORD0_1;
    let _e86 = in_u002e_var_u002e_TEXCOORD1_1;
    let _e87 = global;
    switch bitcast<i32>(0u) {
        default: {
            let _e90 = FragmentUniformBlock.TextureSizeCamera;
            let _e91 = _e90.xy;
            let _e96 = FragmentUniformBlock.GlitchSettings[0i];
            let _e97 = clamp(_e96, 0f, 1f);
            let _e98 = FragmentUniformBlock.GlitchSettings;
            let _e102 = FragmentUniformBlock.GlitchSettings[3i];
            let _e106 = FragmentUniformBlock.TimePadding[0i];
            let _e107 = FragmentUniformBlock.TimePadding;
            let _e114 = vec2<f32>(_e85.x, (1f - _e85.y));
            let _e115 = textureSampleLevel(Texture, Sampler, _e114, 0f);
            let _e116 = (_e115 * _e86);
            if (_e97 <= 0.001f) {
                phi_420_ = _e116;
                break;
            }
            let _e118 = textureSampleLevel(TileFieldTexture, TileFieldSampler, _e114, 0f);
            let _e122 = (_e90.zw + _e87.xy);
            let _e124 = floor((_e122 * vec2<f32>(0.125f, 0.125f)));
            let _e126 = floor((_e106 * 7f));
            let _e127 = vec2(_e126);
            let _e149 = fract((sin(dot(floor(((floor((_e122 * vec2<f32>(0.0625f, 0.0625f))) + vec2((_e126 * 3f))) + vec2((_e118.y * 19f)))), vec2<f32>(127.1f, 311.7f))) * 43758.547f));
            let _e181 = ((mix(42f, (_e102 * 1.75f), smoothstep(0f, 0.75f, _e97)) * mix(0.7f, 1.45f, fract((sin(dot(floor(((floor((_e122 * vec2<f32>(0.035714287f, 0.035714287f))) + vec2((_e126 * 1.7f))) + vec2((_e118.y * 47f)))), vec2<f32>(127.1f, 311.7f))) * 43758.547f)))) + ((fract((sin(dot(floor(((_e124 + vec2((_e126 * 0.5f))) + vec2((_e118.y * 71f)))), vec2<f32>(127.1f, 311.7f))) * 43758.547f)) - 0.5f) * 30f));
            let _e185 = normalize(normalize(_e107.yz));
            let _e190 = (_e122 - _e98.yz);
            let _e193 = (dot(_e190, _e185) / max(_e181, 1f));
            let _e196 = (dot(_e190, vec2<f32>(-(_e185.y), _e185.x)) / max(max((_e181 * mix(0.28f, 0.46f, _e149)), 10f), 1f));
            let _e226 = clamp((((_e97 * smoothstep(0.22f, 1f, _e97)) * 0.3f) + (((_e97 * (pow(smoothstep(0f, 0.95f, clamp((1f - sqrt(((_e193 * _e193) + (_e196 * _e196)))), 0f, 1f)), 0.58f) * mix(0.42f, 1f, fract((sin(dot(floor((floor((_e122 * vec2<f32>(0.05f, 0.05f))) + vec2((_e126 * 2f)))), vec2<f32>(127.1f, 311.7f))) * 43758.547f))))) * mix(0.28f, 0.9f, fract((sin(dot(floor(((_e124 + _e127) + vec2((_e118.x * 31f)))), vec2<f32>(127.1f, 311.7f))) * 43758.547f)))) * mix(0.45f, 1f, _e149))), 0f, 1f);
            if (_e226 <= 0.002f) {
                phi_420_ = _e116;
                break;
            }
            switch bitcast<i32>(0u) {
                default: {
                    if (_e118.y < 0.125f) {
                        phi_281_ = normalize(vec2<f32>(1f, -1f));
                        break;
                    }
                    if (_e118.y < 0.375f) {
                        phi_281_ = vec2<f32>(0f, 1f);
                        break;
                    }
                    if (_e118.y < 0.625f) {
                        phi_281_ = normalize(vec2<f32>(1f, 1f));
                        break;
                    }
                    if (_e118.y < 0.875f) {
                        phi_281_ = vec2<f32>(1f, 0f);
                        break;
                    }
                    phi_281_ = normalize(select(vec2<f32>(1f, -1f), vec2<f32>(-1f, 1f), vec2((_e118.x < 0.5f))));
                    break;
                }
            }
            let _e240 = phi_281_;
            let _e270 = step(0.5f, fract((sin(dot(floor(((_e124 + vec2((_e118.y * 43f))) + vec2((_e126 * 0.25f)))), vec2<f32>(127.1f, 311.7f))) * 43758.547f)));
            let _e271 = _e116.xyz;
            phi_315_ = dot(_e271, vec3<f32>(0.2126f, 0.7152f, 0.0722f));
            phi_318_ = _e271;
            phi_320_ = -18i;
            loop {
                let _e274 = phi_315_;
                let _e276 = phi_318_;
                let _e278 = phi_320_;
                local = _e276;
                if (_e278 <= 18i) {
                    let _e280 = f32(_e278);
                    phi_332_ = true;
                    if !((abs(_e280) > floor(mix(2f, 18f, clamp((_e226 * mix(0.75f, 1.35f, fract((sin(dot(floor(vec2<f32>(floor((_e122.y * 0.33333334f)), (_e126 + floor((_e122.x * 0.025f))))), vec2<f32>(127.1f, 311.7f))) * 43758.547f)))), 0f, 1f))))) {
                        phi_332_ = (_e278 == 0i);
                    }
                    let _e286 = phi_332_;
                    if _e286 {
                        phi_316_ = _e274;
                        phi_319_ = _e276;
                        continue;
                    }
                    let _e303 = textureSampleLevel(Texture, Sampler, (_e114 + ((_e240 * (_e280 + (((fract((sin(dot(floor(((_e124 + vec2((_e280 * 17f))) + _e127)), vec2<f32>(127.1f, 311.7f))) * 43758.547f)) - 0.5f) * _e226) * 3f))) / _e91)), 0f);
                    let _e304 = _e303.xyz;
                    let _e305 = dot(_e304, vec3<f32>(0.2126f, 0.7152f, 0.0722f));
                    phi_361_ = false;
                    if (_e270 > 0.5f) {
                        phi_361_ = (_e305 > _e274);
                    }
                    let _e309 = phi_361_;
                    phi_366_ = false;
                    if (_e270 <= 0.5f) {
                        phi_366_ = (_e305 < _e274);
                    }
                    let _e313 = phi_366_;
                    let _e315 = select(true, _e313, !(_e309));
                    phi_316_ = select(_e274, _e305, _e315);
                    phi_319_ = select(_e276, _e304, vec3(_e315));
                    continue;
                } else {
                    break;
                }
                continuing {
                    let _e320 = phi_316_;
                    let _e322 = phi_319_;
                    phi_315_ = _e320;
                    phi_318_ = _e322;
                    phi_320_ = (_e278 + 1i);
                }
            }
            let _e356 = textureSampleLevel(Texture, Sampler, (_e114 + (((_e240.yx * vec2<f32>(-1f, 1f)) * (((fract((sin(dot(floor((floor((_e122 * vec2<f32>(0.041666668f, 0.041666668f))) + vec2((_e126 * 5f)))), vec2<f32>(127.1f, 311.7f))) * 43758.547f)) - 0.5f) * _e226) * 7f)) / _e91)), 0f);
            let _e362 = local;
            let _e365 = mix(_e271, mix(_e362, _e356.xyz, vec3(clamp((_e226 * 0.38f), 0f, 1f))), vec3((clamp((_e226 * 1.35f), 0f, 1f) * mix(0.78f, 1f, fract((sin(dot(floor(vec2<f32>(floor((_e122.y * 0.5f)), (_e126 + (_e118.y * 23f)))), vec2<f32>(127.1f, 311.7f))) * 43758.547f))))));
            phi_420_ = vec4<f32>(_e365.x, _e365.y, _e365.z, _e116.w);
            break;
        }
    }
    let _e372 = phi_420_;
    out_u002e_var_u002e_SV_Target0_ = _e372;
    return;
}

@fragment 
fn fragment_main(@location(0) in_u002e_var_u002e_TEXCOORD0_: vec2<f32>, @location(1) in_u002e_var_u002e_TEXCOORD1_: vec4<f32>, @builtin(position) param: vec4<f32>) -> @location(0) vec4<f32> {
    in_u002e_var_u002e_TEXCOORD0_1 = in_u002e_var_u002e_TEXCOORD0_;
    in_u002e_var_u002e_TEXCOORD1_1 = in_u002e_var_u002e_TEXCOORD1_;
    global = param;
    fragment_main_1();
    let _e7 = out_u002e_var_u002e_SV_Target0_;
    return _e7;
}
