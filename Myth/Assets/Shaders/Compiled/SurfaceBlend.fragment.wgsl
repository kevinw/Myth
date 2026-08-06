struct type_u002e_FragmentUniformBlock {
    SurfaceMeta: vec4<f32>,
    LayerConfig: array<vec4<f32>, 6>,
    LayerTint: array<vec4<f32>, 6>,
    LayerMotion: array<vec4<f32>, 6>,
    LayerBlack: array<vec4<f32>, 6>,
}

@group(3) @binding(0) 
var<uniform> FragmentUniformBlock: type_u002e_FragmentUniformBlock;
@group(2) @binding(1) 
var Layer0_: texture_2d<f32>;
@group(2) @binding(101) 
var Layer0Sampler: sampler;
@group(2) @binding(2) 
var Layer1_: texture_2d<f32>;
@group(2) @binding(102) 
var Layer1Sampler: sampler;
@group(2) @binding(3) 
var Layer2_: texture_2d<f32>;
@group(2) @binding(103) 
var Layer2Sampler: sampler;
@group(2) @binding(4) 
var Layer3_: texture_2d<f32>;
@group(2) @binding(104) 
var Layer3Sampler: sampler;
@group(2) @binding(5) 
var Layer4_: texture_2d<f32>;
@group(2) @binding(105) 
var Layer4Sampler: sampler;
@group(2) @binding(6) 
var Layer5_: texture_2d<f32>;
@group(2) @binding(106) 
var Layer5Sampler: sampler;
var<private> in_u002e_var_u002e_TEXCOORD0_1: vec2<f32>;
var<private> in_u002e_var_u002e_TEXCOORD1_1: vec4<f32>;
var<private> out_u002e_var_u002e_SV_Target0_: vec4<f32>;

fn fragment_main_1() {
    var phi_89_: vec4<f32>;
    var local: f32;
    var phi_118_: vec4<f32>;
    var local_1: f32;
    var phi_147_: vec4<f32>;
    var local_2: f32;
    var phi_176_: vec4<f32>;
    var local_3: f32;
    var phi_205_: vec4<f32>;
    var local_4: f32;
    var phi_234_: vec4<f32>;
    var phi_280_: vec4<f32>;
    var phi_281_: vec4<f32>;
    var phi_352_: vec4<f32>;
    var phi_353_: vec4<f32>;
    var phi_424_: vec4<f32>;
    var phi_425_: vec4<f32>;
    var phi_496_: vec4<f32>;
    var phi_497_: vec4<f32>;
    var phi_568_: vec4<f32>;
    var phi_569_: vec4<f32>;
    var phi_640_: vec4<f32>;
    var phi_641_: vec4<f32>;

    let _e31 = in_u002e_var_u002e_TEXCOORD0_1;
    let _e32 = in_u002e_var_u002e_TEXCOORD1_1;
    let _e35 = FragmentUniformBlock.LayerConfig[0i];
    let _e38 = FragmentUniformBlock.LayerTint[0i];
    let _e41 = FragmentUniformBlock.LayerMotion[0i];
    let _e45 = FragmentUniformBlock.LayerBlack[0i];
    switch bitcast<i32>(0u) {
        default: {
            let _e49 = FragmentUniformBlock.SurfaceMeta[1i];
            let _e55 = textureSample(Layer0_, Layer0Sampler, ((_e31 - (_e41.xy * _e49)) / max(_e35.xy, vec2<f32>(1f, 1f))));
            local = _e49;
            local_1 = _e49;
            local_2 = _e49;
            local_3 = _e49;
            local_4 = _e49;
            if (_e35.w == 1f) {
                phi_89_ = mix(_e45, _e38, vec4(_e55.x));
                break;
            }
            phi_89_ = (_e55 * _e38);
            break;
        }
    }
    let _e63 = phi_89_;
    let _e66 = FragmentUniformBlock.LayerConfig[1i];
    let _e69 = FragmentUniformBlock.LayerTint[1i];
    let _e72 = FragmentUniformBlock.LayerMotion[1i];
    let _e76 = FragmentUniformBlock.LayerBlack[1i];
    switch bitcast<i32>(0u) {
        default: {
            let _e79 = local;
            let _e85 = textureSample(Layer1_, Layer1Sampler, ((_e31 - (_e72.xy * _e79)) / max(_e66.xy, vec2<f32>(1f, 1f))));
            if (_e66.w == 1f) {
                phi_118_ = mix(_e76, _e69, vec4(_e85.x));
                break;
            }
            phi_118_ = (_e85 * _e69);
            break;
        }
    }
    let _e93 = phi_118_;
    let _e96 = FragmentUniformBlock.LayerConfig[2i];
    let _e99 = FragmentUniformBlock.LayerTint[2i];
    let _e102 = FragmentUniformBlock.LayerMotion[2i];
    let _e106 = FragmentUniformBlock.LayerBlack[2i];
    switch bitcast<i32>(0u) {
        default: {
            let _e109 = local_1;
            let _e115 = textureSample(Layer2_, Layer2Sampler, ((_e31 - (_e102.xy * _e109)) / max(_e96.xy, vec2<f32>(1f, 1f))));
            if (_e96.w == 1f) {
                phi_147_ = mix(_e106, _e99, vec4(_e115.x));
                break;
            }
            phi_147_ = (_e115 * _e99);
            break;
        }
    }
    let _e123 = phi_147_;
    let _e126 = FragmentUniformBlock.LayerConfig[3i];
    let _e129 = FragmentUniformBlock.LayerTint[3i];
    let _e132 = FragmentUniformBlock.LayerMotion[3i];
    let _e136 = FragmentUniformBlock.LayerBlack[3i];
    switch bitcast<i32>(0u) {
        default: {
            let _e139 = local_2;
            let _e145 = textureSample(Layer3_, Layer3Sampler, ((_e31 - (_e132.xy * _e139)) / max(_e126.xy, vec2<f32>(1f, 1f))));
            if (_e126.w == 1f) {
                phi_176_ = mix(_e136, _e129, vec4(_e145.x));
                break;
            }
            phi_176_ = (_e145 * _e129);
            break;
        }
    }
    let _e153 = phi_176_;
    let _e156 = FragmentUniformBlock.LayerConfig[4i];
    let _e159 = FragmentUniformBlock.LayerTint[4i];
    let _e162 = FragmentUniformBlock.LayerMotion[4i];
    let _e166 = FragmentUniformBlock.LayerBlack[4i];
    switch bitcast<i32>(0u) {
        default: {
            let _e169 = local_3;
            let _e175 = textureSample(Layer4_, Layer4Sampler, ((_e31 - (_e162.xy * _e169)) / max(_e156.xy, vec2<f32>(1f, 1f))));
            if (_e156.w == 1f) {
                phi_205_ = mix(_e166, _e159, vec4(_e175.x));
                break;
            }
            phi_205_ = (_e175 * _e159);
            break;
        }
    }
    let _e183 = phi_205_;
    let _e186 = FragmentUniformBlock.LayerConfig[5i];
    let _e189 = FragmentUniformBlock.LayerTint[5i];
    let _e192 = FragmentUniformBlock.LayerMotion[5i];
    let _e196 = FragmentUniformBlock.LayerBlack[5i];
    switch bitcast<i32>(0u) {
        default: {
            let _e199 = local_4;
            let _e205 = textureSample(Layer5_, Layer5Sampler, ((_e31 - (_e192.xy * _e199)) / max(_e186.xy, vec2<f32>(1f, 1f))));
            if (_e186.w == 1f) {
                phi_234_ = mix(_e196, _e189, vec4(_e205.x));
                break;
            }
            phi_234_ = (_e205 * _e189);
            break;
        }
    }
    let _e213 = phi_234_;
    let _e216 = FragmentUniformBlock.SurfaceMeta[0i];
    let _e217 = i32(_e216);
    phi_281_ = vec4<f32>(0f, 0f, 0f, 0f);
    if (0i < _e217) {
        let _e222 = FragmentUniformBlock.LayerConfig[0i][2i];
        switch bitcast<i32>(0u) {
            default: {
                if (_e222 == 1f) {
                    phi_280_ = vec4<f32>(0f, 0f, 0f, clamp(_e63.w, 0f, 1f));
                    break;
                }
                if (_e222 == 2f) {
                    let _e231 = (_e63.xyz * _e63.w);
                    phi_280_ = vec4<f32>(_e231.x, _e231.y, _e231.z, clamp(_e63.w, 0f, 1f));
                    break;
                }
                if (_e222 == 3f) {
                    let _e240 = (_e63.xyz * _e63.w);
                    phi_280_ = vec4<f32>(_e240.x, _e240.y, _e240.z, clamp(_e63.w, 0f, 1f));
                    break;
                }
                let _e248 = (_e63.xyz * _e63.w);
                phi_280_ = vec4<f32>(_e248.x, _e248.y, _e248.z, _e63.w);
                break;
            }
        }
        let _e254 = phi_280_;
        phi_281_ = _e254;
    }
    let _e256 = phi_281_;
    phi_353_ = _e256;
    if (1i < _e217) {
        let _e261 = FragmentUniformBlock.LayerConfig[1i][2i];
        switch bitcast<i32>(0u) {
            default: {
                if (_e261 == 1f) {
                    let _e269 = (_e256.xyz * mix(vec3<f32>(1f, 1f, 1f), _e93.xyz, vec3(_e93.w)));
                    phi_352_ = vec4<f32>(_e269.x, _e269.y, _e269.z, clamp((_e256.w + _e93.w), 0f, 1f));
                    break;
                }
                if (_e261 == 2f) {
                    let _e282 = (_e256.xyz + (_e93.xyz * _e93.w));
                    phi_352_ = vec4<f32>(_e282.x, _e282.y, _e282.z, clamp((_e256.w + _e93.w), 0f, 1f));
                    break;
                }
                if (_e261 == 3f) {
                    let _e298 = (vec3<f32>(1f, 1f, 1f) - ((vec3<f32>(1f, 1f, 1f) - _e256.xyz) * (vec3<f32>(1f, 1f, 1f) - (_e93.xyz * _e93.w))));
                    phi_352_ = vec4<f32>(_e298.x, _e298.y, _e298.z, clamp((_e256.w + _e93.w), 0f, 1f));
                    break;
                }
                let _e308 = (1f - _e93.w);
                let _e315 = ((_e93.xyz * _e93.w) + (_e256.xyz * _e308));
                phi_352_ = vec4<f32>(_e315.x, _e315.y, _e315.z, (_e93.w + (_e256.w * _e308)));
                break;
            }
        }
        let _e321 = phi_352_;
        phi_353_ = _e321;
    }
    let _e323 = phi_353_;
    phi_425_ = _e323;
    if (2i < _e217) {
        let _e328 = FragmentUniformBlock.LayerConfig[2i][2i];
        switch bitcast<i32>(0u) {
            default: {
                if (_e328 == 1f) {
                    let _e336 = (_e323.xyz * mix(vec3<f32>(1f, 1f, 1f), _e123.xyz, vec3(_e123.w)));
                    phi_424_ = vec4<f32>(_e336.x, _e336.y, _e336.z, clamp((_e323.w + _e123.w), 0f, 1f));
                    break;
                }
                if (_e328 == 2f) {
                    let _e349 = (_e323.xyz + (_e123.xyz * _e123.w));
                    phi_424_ = vec4<f32>(_e349.x, _e349.y, _e349.z, clamp((_e323.w + _e123.w), 0f, 1f));
                    break;
                }
                if (_e328 == 3f) {
                    let _e365 = (vec3<f32>(1f, 1f, 1f) - ((vec3<f32>(1f, 1f, 1f) - _e323.xyz) * (vec3<f32>(1f, 1f, 1f) - (_e123.xyz * _e123.w))));
                    phi_424_ = vec4<f32>(_e365.x, _e365.y, _e365.z, clamp((_e323.w + _e123.w), 0f, 1f));
                    break;
                }
                let _e375 = (1f - _e123.w);
                let _e382 = ((_e123.xyz * _e123.w) + (_e323.xyz * _e375));
                phi_424_ = vec4<f32>(_e382.x, _e382.y, _e382.z, (_e123.w + (_e323.w * _e375)));
                break;
            }
        }
        let _e388 = phi_424_;
        phi_425_ = _e388;
    }
    let _e390 = phi_425_;
    phi_497_ = _e390;
    if (3i < _e217) {
        let _e395 = FragmentUniformBlock.LayerConfig[3i][2i];
        switch bitcast<i32>(0u) {
            default: {
                if (_e395 == 1f) {
                    let _e403 = (_e390.xyz * mix(vec3<f32>(1f, 1f, 1f), _e153.xyz, vec3(_e153.w)));
                    phi_496_ = vec4<f32>(_e403.x, _e403.y, _e403.z, clamp((_e390.w + _e153.w), 0f, 1f));
                    break;
                }
                if (_e395 == 2f) {
                    let _e416 = (_e390.xyz + (_e153.xyz * _e153.w));
                    phi_496_ = vec4<f32>(_e416.x, _e416.y, _e416.z, clamp((_e390.w + _e153.w), 0f, 1f));
                    break;
                }
                if (_e395 == 3f) {
                    let _e432 = (vec3<f32>(1f, 1f, 1f) - ((vec3<f32>(1f, 1f, 1f) - _e390.xyz) * (vec3<f32>(1f, 1f, 1f) - (_e153.xyz * _e153.w))));
                    phi_496_ = vec4<f32>(_e432.x, _e432.y, _e432.z, clamp((_e390.w + _e153.w), 0f, 1f));
                    break;
                }
                let _e442 = (1f - _e153.w);
                let _e449 = ((_e153.xyz * _e153.w) + (_e390.xyz * _e442));
                phi_496_ = vec4<f32>(_e449.x, _e449.y, _e449.z, (_e153.w + (_e390.w * _e442)));
                break;
            }
        }
        let _e455 = phi_496_;
        phi_497_ = _e455;
    }
    let _e457 = phi_497_;
    phi_569_ = _e457;
    if (4i < _e217) {
        let _e462 = FragmentUniformBlock.LayerConfig[4i][2i];
        switch bitcast<i32>(0u) {
            default: {
                if (_e462 == 1f) {
                    let _e470 = (_e457.xyz * mix(vec3<f32>(1f, 1f, 1f), _e183.xyz, vec3(_e183.w)));
                    phi_568_ = vec4<f32>(_e470.x, _e470.y, _e470.z, clamp((_e457.w + _e183.w), 0f, 1f));
                    break;
                }
                if (_e462 == 2f) {
                    let _e483 = (_e457.xyz + (_e183.xyz * _e183.w));
                    phi_568_ = vec4<f32>(_e483.x, _e483.y, _e483.z, clamp((_e457.w + _e183.w), 0f, 1f));
                    break;
                }
                if (_e462 == 3f) {
                    let _e499 = (vec3<f32>(1f, 1f, 1f) - ((vec3<f32>(1f, 1f, 1f) - _e457.xyz) * (vec3<f32>(1f, 1f, 1f) - (_e183.xyz * _e183.w))));
                    phi_568_ = vec4<f32>(_e499.x, _e499.y, _e499.z, clamp((_e457.w + _e183.w), 0f, 1f));
                    break;
                }
                let _e509 = (1f - _e183.w);
                let _e516 = ((_e183.xyz * _e183.w) + (_e457.xyz * _e509));
                phi_568_ = vec4<f32>(_e516.x, _e516.y, _e516.z, (_e183.w + (_e457.w * _e509)));
                break;
            }
        }
        let _e522 = phi_568_;
        phi_569_ = _e522;
    }
    let _e524 = phi_569_;
    phi_641_ = _e524;
    if (5i < _e217) {
        let _e529 = FragmentUniformBlock.LayerConfig[5i][2i];
        switch bitcast<i32>(0u) {
            default: {
                if (_e529 == 1f) {
                    let _e537 = (_e524.xyz * mix(vec3<f32>(1f, 1f, 1f), _e213.xyz, vec3(_e213.w)));
                    phi_640_ = vec4<f32>(_e537.x, _e537.y, _e537.z, clamp((_e524.w + _e213.w), 0f, 1f));
                    break;
                }
                if (_e529 == 2f) {
                    let _e550 = (_e524.xyz + (_e213.xyz * _e213.w));
                    phi_640_ = vec4<f32>(_e550.x, _e550.y, _e550.z, clamp((_e524.w + _e213.w), 0f, 1f));
                    break;
                }
                if (_e529 == 3f) {
                    let _e566 = (vec3<f32>(1f, 1f, 1f) - ((vec3<f32>(1f, 1f, 1f) - _e524.xyz) * (vec3<f32>(1f, 1f, 1f) - (_e213.xyz * _e213.w))));
                    phi_640_ = vec4<f32>(_e566.x, _e566.y, _e566.z, clamp((_e524.w + _e213.w), 0f, 1f));
                    break;
                }
                let _e576 = (1f - _e213.w);
                let _e583 = ((_e213.xyz * _e213.w) + (_e524.xyz * _e576));
                phi_640_ = vec4<f32>(_e583.x, _e583.y, _e583.z, (_e213.w + (_e524.w * _e576)));
                break;
            }
        }
        let _e589 = phi_640_;
        phi_641_ = _e589;
    }
    let _e591 = phi_641_;
    out_u002e_var_u002e_SV_Target0_ = (_e591 * _e32);
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
