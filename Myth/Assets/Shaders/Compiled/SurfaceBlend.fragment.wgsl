struct type_u002e_FragmentUniformBlock {
    SurfaceMeta: vec4<f32>,
    LayerConfig: array<vec4<f32>, 6>,
    LayerTint: array<vec4<f32>, 6>,
    LayerMotion: array<vec4<f32>, 6>,
    LayerBlack: array<vec4<f32>, 6>,
    LayerTransform: array<vec4<f32>, 6>,
    LayerAdjust: array<vec4<f32>, 6>,
}

@group(3) @binding(0) 
var<uniform> FragmentUniformBlock: type_u002e_FragmentUniformBlock;
@group(2) @binding(0)
var Layer0_: texture_2d<f32>;
@group(2) @binding(100)
var Layer0Sampler: sampler;
@group(2) @binding(1)
var Layer1_: texture_2d<f32>;
@group(2) @binding(101)
var Layer1Sampler: sampler;
@group(2) @binding(2)
var Layer2_: texture_2d<f32>;
@group(2) @binding(102)
var Layer2Sampler: sampler;
@group(2) @binding(3)
var Layer3_: texture_2d<f32>;
@group(2) @binding(103)
var Layer3Sampler: sampler;
@group(2) @binding(4)
var Layer4_: texture_2d<f32>;
@group(2) @binding(104)
var Layer4Sampler: sampler;
@group(2) @binding(5)
var Layer5_: texture_2d<f32>;
@group(2) @binding(105)
var Layer5Sampler: sampler;
var<private> in_u002e_var_u002e_TEXCOORD0_1: vec2<f32>;
var<private> in_u002e_var_u002e_TEXCOORD1_1: vec4<f32>;
var<private> out_u002e_var_u002e_SV_Target0_: vec4<f32>;

fn fragment_main_1() {
    var phi_103_: vec2<f32>;
    var phi_111_: vec2<f32>;
    var phi_133_: vec2<f32>;
    var phi_134_: vec2<f32>;
    var phi_135_: vec2<f32>;
    var phi_162_: f32;
    var phi_186_: vec3<f32>;
    var phi_193_: vec4<f32>;
    var phi_268_: vec4<f32>;
    var phi_269_: vec4<f32>;
    var phi_304_: vec2<f32>;
    var phi_312_: vec2<f32>;
    var phi_334_: vec2<f32>;
    var phi_335_: vec2<f32>;
    var phi_336_: vec2<f32>;
    var phi_363_: f32;
    var phi_387_: vec3<f32>;
    var phi_394_: vec4<f32>;
    var phi_509_: vec4<f32>;
    var phi_510_: vec4<f32>;
    var phi_545_: vec2<f32>;
    var phi_553_: vec2<f32>;
    var phi_575_: vec2<f32>;
    var phi_576_: vec2<f32>;
    var phi_577_: vec2<f32>;
    var phi_604_: f32;
    var phi_628_: vec3<f32>;
    var phi_635_: vec4<f32>;
    var phi_750_: vec4<f32>;
    var phi_751_: vec4<f32>;
    var phi_786_: vec2<f32>;
    var phi_794_: vec2<f32>;
    var phi_816_: vec2<f32>;
    var phi_817_: vec2<f32>;
    var phi_818_: vec2<f32>;
    var phi_845_: f32;
    var phi_869_: vec3<f32>;
    var phi_876_: vec4<f32>;
    var phi_991_: vec4<f32>;
    var phi_992_: vec4<f32>;
    var phi_1027_: vec2<f32>;
    var phi_1035_: vec2<f32>;
    var phi_1057_: vec2<f32>;
    var phi_1058_: vec2<f32>;
    var phi_1059_: vec2<f32>;
    var phi_1086_: f32;
    var phi_1110_: vec3<f32>;
    var phi_1117_: vec4<f32>;
    var phi_1232_: vec4<f32>;
    var phi_1233_: vec4<f32>;
    var phi_1268_: vec2<f32>;
    var phi_1276_: vec2<f32>;
    var phi_1298_: vec2<f32>;
    var phi_1299_: vec2<f32>;
    var phi_1300_: vec2<f32>;
    var phi_1327_: f32;
    var phi_1351_: vec3<f32>;
    var phi_1358_: vec4<f32>;
    var phi_1473_: vec4<f32>;
    var phi_1474_: vec4<f32>;

    let _e38 = in_u002e_var_u002e_TEXCOORD0_1;
    let _e39 = in_u002e_var_u002e_TEXCOORD1_1;
    let _e42 = FragmentUniformBlock.SurfaceMeta[0i];
    let _e43 = i32(_e42);
    phi_269_ = vec4<f32>(0f, 0f, 0f, 0f);
    if (_e43 > 0i) {
        let _e47 = FragmentUniformBlock.LayerConfig[0i];
        let _e50 = FragmentUniformBlock.LayerTint[0i];
        let _e53 = FragmentUniformBlock.LayerMotion[0i];
        let _e56 = FragmentUniformBlock.LayerBlack[0i];
        let _e59 = FragmentUniformBlock.LayerTransform[0i];
        let _e62 = FragmentUniformBlock.LayerAdjust[0i];
        let _e68 = FragmentUniformBlock.SurfaceMeta[1i];
        let _e73 = (((_e38 + _e53.zw) - (_e53.xy * _e68)) / max(_e47.xy, vec2<f32>(1f, 1f)));
        phi_103_ = _e73;
        if (_e59.y != 0f) {
            phi_103_ = vec2<f32>(-(_e73.x), _e73.y);
        }
        let _e82 = phi_103_;
        phi_111_ = _e82;
        if (_e59.z != 0f) {
            phi_111_ = vec2<f32>(_e82.x, -(_e82.y));
        }
        let _e91 = phi_111_;
        if (_e59.x == 1f) {
            phi_135_ = vec2<f32>(_e91.y, -(_e91.x));
        } else {
            if (_e59.x == 2f) {
                phi_134_ = -(_e91);
            } else {
                phi_133_ = _e91;
                if (_e59.x == 3f) {
                    phi_133_ = vec2<f32>(-(_e91.y), _e91.x);
                }
                let _e106 = phi_133_;
                phi_134_ = _e106;
            }
            let _e108 = phi_134_;
            phi_135_ = _e108;
        }
        let _e110 = phi_135_;
        let _e111 = textureSample(Layer0_, Layer0Sampler, _e110);
        if (_e47.w == 1f) {
            let _e122 = clamp(((((_e111.x + _e62.z) - 0.5f) * max(_e62.y, 0f)) + 0.5f), 0f, 1f);
            phi_162_ = _e122;
            if (_e62.w >= 2f) {
                let _e126 = (floor(_e62.w) - 1f);
                phi_162_ = (floor(((_e122 * _e126) + 0.5f)) / _e126);
            }
            let _e132 = phi_162_;
            phi_193_ = mix(_e56, _e50, vec4(_e132));
        } else {
            let _e144 = clamp(((((_e111.xyz + vec3(_e62.z)) - vec3<f32>(0.5f, 0.5f, 0.5f)) * max(_e62.y, 0f)) + vec3<f32>(0.5f, 0.5f, 0.5f)), vec3<f32>(0f, 0f, 0f), vec3<f32>(1f, 1f, 1f));
            phi_186_ = _e144;
            if (_e62.w >= 2f) {
                let _e148 = (floor(_e62.w) - 1f);
                phi_186_ = (floor(((_e144 * _e148) + vec3<f32>(0.5f, 0.5f, 0.5f))) / vec3(_e148));
            }
            let _e155 = phi_186_;
            phi_193_ = (vec4<f32>(_e155.x, _e155.y, _e155.z, _e111.w) * _e50);
        }
        let _e163 = phi_193_;
        let _e166 = (_e163.w * _e62.x);
        let _e170 = FragmentUniformBlock.LayerConfig[0i][2i];
        switch bitcast<i32>(0u) {
            default: {
                if (_e170 == 1f) {
                    phi_268_ = vec4<f32>(0f, 0f, 0f, clamp(_e166, 0f, 1f));
                    break;
                }
                if (_e170 == 2f) {
                    let _e177 = (_e163.xyz * _e166);
                    phi_268_ = vec4<f32>(_e177.x, _e177.y, _e177.z, clamp(_e166, 0f, 1f));
                    break;
                }
                if (_e170 == 3f) {
                    let _e185 = (_e163.xyz * _e166);
                    phi_268_ = vec4<f32>(_e185.x, _e185.y, _e185.z, clamp(_e166, 0f, 1f));
                    break;
                }
                if (_e170 == 4f) {
                    let _e195 = mix(vec3<f32>(0f, 0f, 0f), min(vec3<f32>(0f, 0f, 0f), _e163.xyz), vec3(_e166));
                    phi_268_ = vec4<f32>(_e195.x, _e195.y, _e195.z, clamp(_e166, 0f, 1f));
                    break;
                }
                if (_e170 == 5f) {
                    let _e205 = mix(vec3<f32>(0f, 0f, 0f), max(vec3<f32>(0f, 0f, 0f), _e163.xyz), vec3(_e166));
                    phi_268_ = vec4<f32>(_e205.x, _e205.y, _e205.z, clamp(_e166, 0f, 1f));
                    break;
                }
                if (_e170 == 6f) {
                    let _e215 = (abs(-(_e163.xyz)) * _e166);
                    phi_268_ = vec4<f32>(_e215.x, _e215.y, _e215.z, clamp(_e166, 0f, 1f));
                    break;
                }
                let _e222 = (_e163.xyz * _e166);
                phi_268_ = vec4<f32>(_e222.x, _e222.y, _e222.z, _e166);
                break;
            }
        }
        let _e228 = phi_268_;
        phi_269_ = _e228;
    }
    let _e230 = phi_269_;
    phi_510_ = _e230;
    if (_e43 > 1i) {
        let _e234 = FragmentUniformBlock.LayerConfig[1i];
        let _e237 = FragmentUniformBlock.LayerTint[1i];
        let _e240 = FragmentUniformBlock.LayerMotion[1i];
        let _e243 = FragmentUniformBlock.LayerBlack[1i];
        let _e246 = FragmentUniformBlock.LayerTransform[1i];
        let _e249 = FragmentUniformBlock.LayerAdjust[1i];
        let _e255 = FragmentUniformBlock.SurfaceMeta[1i];
        let _e260 = (((_e38 + _e240.zw) - (_e240.xy * _e255)) / max(_e234.xy, vec2<f32>(1f, 1f)));
        phi_304_ = _e260;
        if (_e246.y != 0f) {
            phi_304_ = vec2<f32>(-(_e260.x), _e260.y);
        }
        let _e269 = phi_304_;
        phi_312_ = _e269;
        if (_e246.z != 0f) {
            phi_312_ = vec2<f32>(_e269.x, -(_e269.y));
        }
        let _e278 = phi_312_;
        if (_e246.x == 1f) {
            phi_336_ = vec2<f32>(_e278.y, -(_e278.x));
        } else {
            if (_e246.x == 2f) {
                phi_335_ = -(_e278);
            } else {
                phi_334_ = _e278;
                if (_e246.x == 3f) {
                    phi_334_ = vec2<f32>(-(_e278.y), _e278.x);
                }
                let _e293 = phi_334_;
                phi_335_ = _e293;
            }
            let _e295 = phi_335_;
            phi_336_ = _e295;
        }
        let _e297 = phi_336_;
        let _e298 = textureSample(Layer1_, Layer1Sampler, _e297);
        if (_e234.w == 1f) {
            let _e309 = clamp(((((_e298.x + _e249.z) - 0.5f) * max(_e249.y, 0f)) + 0.5f), 0f, 1f);
            phi_363_ = _e309;
            if (_e249.w >= 2f) {
                let _e313 = (floor(_e249.w) - 1f);
                phi_363_ = (floor(((_e309 * _e313) + 0.5f)) / _e313);
            }
            let _e319 = phi_363_;
            phi_394_ = mix(_e243, _e237, vec4(_e319));
        } else {
            let _e331 = clamp(((((_e298.xyz + vec3(_e249.z)) - vec3<f32>(0.5f, 0.5f, 0.5f)) * max(_e249.y, 0f)) + vec3<f32>(0.5f, 0.5f, 0.5f)), vec3<f32>(0f, 0f, 0f), vec3<f32>(1f, 1f, 1f));
            phi_387_ = _e331;
            if (_e249.w >= 2f) {
                let _e335 = (floor(_e249.w) - 1f);
                phi_387_ = (floor(((_e331 * _e335) + vec3<f32>(0.5f, 0.5f, 0.5f))) / vec3(_e335));
            }
            let _e342 = phi_387_;
            phi_394_ = (vec4<f32>(_e342.x, _e342.y, _e342.z, _e298.w) * _e237);
        }
        let _e350 = phi_394_;
        let _e353 = (_e350.w * _e249.x);
        let _e357 = FragmentUniformBlock.LayerConfig[1i][2i];
        switch bitcast<i32>(0u) {
            default: {
                if (_e357 == 1f) {
                    let _e364 = (_e230.xyz * mix(vec3<f32>(1f, 1f, 1f), _e350.xyz, vec3(_e353)));
                    phi_509_ = vec4<f32>(_e364.x, _e364.y, _e364.z, clamp((_e230.w + _e353), 0f, 1f));
                    break;
                }
                if (_e357 == 2f) {
                    let _e376 = (_e230.xyz + (_e350.xyz * _e353));
                    phi_509_ = vec4<f32>(_e376.x, _e376.y, _e376.z, clamp((_e230.w + _e353), 0f, 1f));
                    break;
                }
                if (_e357 == 3f) {
                    let _e391 = (vec3<f32>(1f, 1f, 1f) - ((vec3<f32>(1f, 1f, 1f) - _e230.xyz) * (vec3<f32>(1f, 1f, 1f) - (_e350.xyz * _e353))));
                    phi_509_ = vec4<f32>(_e391.x, _e391.y, _e391.z, clamp((_e230.w + _e353), 0f, 1f));
                    break;
                }
                if (_e357 == 4f) {
                    let _e400 = _e230.xyz;
                    let _e404 = mix(_e400, min(_e400, _e350.xyz), vec3(_e353));
                    phi_509_ = vec4<f32>(_e404.x, _e404.y, _e404.z, clamp((_e230.w + _e353), 0f, 1f));
                    break;
                }
                if (_e357 == 5f) {
                    let _e413 = _e230.xyz;
                    let _e417 = mix(_e413, max(_e413, _e350.xyz), vec3(_e353));
                    phi_509_ = vec4<f32>(_e417.x, _e417.y, _e417.z, clamp((_e230.w + _e353), 0f, 1f));
                    break;
                }
                if (_e357 == 6f) {
                    let _e426 = _e230.xyz;
                    let _e433 = ((abs((_e426 - _e350.xyz)) * _e353) + (_e426 * (1f - _e353)));
                    phi_509_ = vec4<f32>(_e433.x, _e433.y, _e433.z, clamp((_e230.w + _e353), 0f, 1f));
                    break;
                }
                let _e442 = (1f - _e353);
                let _e449 = ((_e350.xyz * _e353) + (_e230.xyz * _e442));
                phi_509_ = vec4<f32>(_e449.x, _e449.y, _e449.z, (_e353 + (_e230.w * _e442)));
                break;
            }
        }
        let _e455 = phi_509_;
        phi_510_ = _e455;
    }
    let _e457 = phi_510_;
    phi_751_ = _e457;
    if (_e43 > 2i) {
        let _e461 = FragmentUniformBlock.LayerConfig[2i];
        let _e464 = FragmentUniformBlock.LayerTint[2i];
        let _e467 = FragmentUniformBlock.LayerMotion[2i];
        let _e470 = FragmentUniformBlock.LayerBlack[2i];
        let _e473 = FragmentUniformBlock.LayerTransform[2i];
        let _e476 = FragmentUniformBlock.LayerAdjust[2i];
        let _e482 = FragmentUniformBlock.SurfaceMeta[1i];
        let _e487 = (((_e38 + _e467.zw) - (_e467.xy * _e482)) / max(_e461.xy, vec2<f32>(1f, 1f)));
        phi_545_ = _e487;
        if (_e473.y != 0f) {
            phi_545_ = vec2<f32>(-(_e487.x), _e487.y);
        }
        let _e496 = phi_545_;
        phi_553_ = _e496;
        if (_e473.z != 0f) {
            phi_553_ = vec2<f32>(_e496.x, -(_e496.y));
        }
        let _e505 = phi_553_;
        if (_e473.x == 1f) {
            phi_577_ = vec2<f32>(_e505.y, -(_e505.x));
        } else {
            if (_e473.x == 2f) {
                phi_576_ = -(_e505);
            } else {
                phi_575_ = _e505;
                if (_e473.x == 3f) {
                    phi_575_ = vec2<f32>(-(_e505.y), _e505.x);
                }
                let _e520 = phi_575_;
                phi_576_ = _e520;
            }
            let _e522 = phi_576_;
            phi_577_ = _e522;
        }
        let _e524 = phi_577_;
        let _e525 = textureSample(Layer2_, Layer2Sampler, _e524);
        if (_e461.w == 1f) {
            let _e536 = clamp(((((_e525.x + _e476.z) - 0.5f) * max(_e476.y, 0f)) + 0.5f), 0f, 1f);
            phi_604_ = _e536;
            if (_e476.w >= 2f) {
                let _e540 = (floor(_e476.w) - 1f);
                phi_604_ = (floor(((_e536 * _e540) + 0.5f)) / _e540);
            }
            let _e546 = phi_604_;
            phi_635_ = mix(_e470, _e464, vec4(_e546));
        } else {
            let _e558 = clamp(((((_e525.xyz + vec3(_e476.z)) - vec3<f32>(0.5f, 0.5f, 0.5f)) * max(_e476.y, 0f)) + vec3<f32>(0.5f, 0.5f, 0.5f)), vec3<f32>(0f, 0f, 0f), vec3<f32>(1f, 1f, 1f));
            phi_628_ = _e558;
            if (_e476.w >= 2f) {
                let _e562 = (floor(_e476.w) - 1f);
                phi_628_ = (floor(((_e558 * _e562) + vec3<f32>(0.5f, 0.5f, 0.5f))) / vec3(_e562));
            }
            let _e569 = phi_628_;
            phi_635_ = (vec4<f32>(_e569.x, _e569.y, _e569.z, _e525.w) * _e464);
        }
        let _e577 = phi_635_;
        let _e580 = (_e577.w * _e476.x);
        let _e584 = FragmentUniformBlock.LayerConfig[2i][2i];
        switch bitcast<i32>(0u) {
            default: {
                if (_e584 == 1f) {
                    let _e591 = (_e457.xyz * mix(vec3<f32>(1f, 1f, 1f), _e577.xyz, vec3(_e580)));
                    phi_750_ = vec4<f32>(_e591.x, _e591.y, _e591.z, clamp((_e457.w + _e580), 0f, 1f));
                    break;
                }
                if (_e584 == 2f) {
                    let _e603 = (_e457.xyz + (_e577.xyz * _e580));
                    phi_750_ = vec4<f32>(_e603.x, _e603.y, _e603.z, clamp((_e457.w + _e580), 0f, 1f));
                    break;
                }
                if (_e584 == 3f) {
                    let _e618 = (vec3<f32>(1f, 1f, 1f) - ((vec3<f32>(1f, 1f, 1f) - _e457.xyz) * (vec3<f32>(1f, 1f, 1f) - (_e577.xyz * _e580))));
                    phi_750_ = vec4<f32>(_e618.x, _e618.y, _e618.z, clamp((_e457.w + _e580), 0f, 1f));
                    break;
                }
                if (_e584 == 4f) {
                    let _e627 = _e457.xyz;
                    let _e631 = mix(_e627, min(_e627, _e577.xyz), vec3(_e580));
                    phi_750_ = vec4<f32>(_e631.x, _e631.y, _e631.z, clamp((_e457.w + _e580), 0f, 1f));
                    break;
                }
                if (_e584 == 5f) {
                    let _e640 = _e457.xyz;
                    let _e644 = mix(_e640, max(_e640, _e577.xyz), vec3(_e580));
                    phi_750_ = vec4<f32>(_e644.x, _e644.y, _e644.z, clamp((_e457.w + _e580), 0f, 1f));
                    break;
                }
                if (_e584 == 6f) {
                    let _e653 = _e457.xyz;
                    let _e660 = ((abs((_e653 - _e577.xyz)) * _e580) + (_e653 * (1f - _e580)));
                    phi_750_ = vec4<f32>(_e660.x, _e660.y, _e660.z, clamp((_e457.w + _e580), 0f, 1f));
                    break;
                }
                let _e669 = (1f - _e580);
                let _e676 = ((_e577.xyz * _e580) + (_e457.xyz * _e669));
                phi_750_ = vec4<f32>(_e676.x, _e676.y, _e676.z, (_e580 + (_e457.w * _e669)));
                break;
            }
        }
        let _e682 = phi_750_;
        phi_751_ = _e682;
    }
    let _e684 = phi_751_;
    phi_992_ = _e684;
    if (_e43 > 3i) {
        let _e688 = FragmentUniformBlock.LayerConfig[3i];
        let _e691 = FragmentUniformBlock.LayerTint[3i];
        let _e694 = FragmentUniformBlock.LayerMotion[3i];
        let _e697 = FragmentUniformBlock.LayerBlack[3i];
        let _e700 = FragmentUniformBlock.LayerTransform[3i];
        let _e703 = FragmentUniformBlock.LayerAdjust[3i];
        let _e709 = FragmentUniformBlock.SurfaceMeta[1i];
        let _e714 = (((_e38 + _e694.zw) - (_e694.xy * _e709)) / max(_e688.xy, vec2<f32>(1f, 1f)));
        phi_786_ = _e714;
        if (_e700.y != 0f) {
            phi_786_ = vec2<f32>(-(_e714.x), _e714.y);
        }
        let _e723 = phi_786_;
        phi_794_ = _e723;
        if (_e700.z != 0f) {
            phi_794_ = vec2<f32>(_e723.x, -(_e723.y));
        }
        let _e732 = phi_794_;
        if (_e700.x == 1f) {
            phi_818_ = vec2<f32>(_e732.y, -(_e732.x));
        } else {
            if (_e700.x == 2f) {
                phi_817_ = -(_e732);
            } else {
                phi_816_ = _e732;
                if (_e700.x == 3f) {
                    phi_816_ = vec2<f32>(-(_e732.y), _e732.x);
                }
                let _e747 = phi_816_;
                phi_817_ = _e747;
            }
            let _e749 = phi_817_;
            phi_818_ = _e749;
        }
        let _e751 = phi_818_;
        let _e752 = textureSample(Layer3_, Layer3Sampler, _e751);
        if (_e688.w == 1f) {
            let _e763 = clamp(((((_e752.x + _e703.z) - 0.5f) * max(_e703.y, 0f)) + 0.5f), 0f, 1f);
            phi_845_ = _e763;
            if (_e703.w >= 2f) {
                let _e767 = (floor(_e703.w) - 1f);
                phi_845_ = (floor(((_e763 * _e767) + 0.5f)) / _e767);
            }
            let _e773 = phi_845_;
            phi_876_ = mix(_e697, _e691, vec4(_e773));
        } else {
            let _e785 = clamp(((((_e752.xyz + vec3(_e703.z)) - vec3<f32>(0.5f, 0.5f, 0.5f)) * max(_e703.y, 0f)) + vec3<f32>(0.5f, 0.5f, 0.5f)), vec3<f32>(0f, 0f, 0f), vec3<f32>(1f, 1f, 1f));
            phi_869_ = _e785;
            if (_e703.w >= 2f) {
                let _e789 = (floor(_e703.w) - 1f);
                phi_869_ = (floor(((_e785 * _e789) + vec3<f32>(0.5f, 0.5f, 0.5f))) / vec3(_e789));
            }
            let _e796 = phi_869_;
            phi_876_ = (vec4<f32>(_e796.x, _e796.y, _e796.z, _e752.w) * _e691);
        }
        let _e804 = phi_876_;
        let _e807 = (_e804.w * _e703.x);
        let _e811 = FragmentUniformBlock.LayerConfig[3i][2i];
        switch bitcast<i32>(0u) {
            default: {
                if (_e811 == 1f) {
                    let _e818 = (_e684.xyz * mix(vec3<f32>(1f, 1f, 1f), _e804.xyz, vec3(_e807)));
                    phi_991_ = vec4<f32>(_e818.x, _e818.y, _e818.z, clamp((_e684.w + _e807), 0f, 1f));
                    break;
                }
                if (_e811 == 2f) {
                    let _e830 = (_e684.xyz + (_e804.xyz * _e807));
                    phi_991_ = vec4<f32>(_e830.x, _e830.y, _e830.z, clamp((_e684.w + _e807), 0f, 1f));
                    break;
                }
                if (_e811 == 3f) {
                    let _e845 = (vec3<f32>(1f, 1f, 1f) - ((vec3<f32>(1f, 1f, 1f) - _e684.xyz) * (vec3<f32>(1f, 1f, 1f) - (_e804.xyz * _e807))));
                    phi_991_ = vec4<f32>(_e845.x, _e845.y, _e845.z, clamp((_e684.w + _e807), 0f, 1f));
                    break;
                }
                if (_e811 == 4f) {
                    let _e854 = _e684.xyz;
                    let _e858 = mix(_e854, min(_e854, _e804.xyz), vec3(_e807));
                    phi_991_ = vec4<f32>(_e858.x, _e858.y, _e858.z, clamp((_e684.w + _e807), 0f, 1f));
                    break;
                }
                if (_e811 == 5f) {
                    let _e867 = _e684.xyz;
                    let _e871 = mix(_e867, max(_e867, _e804.xyz), vec3(_e807));
                    phi_991_ = vec4<f32>(_e871.x, _e871.y, _e871.z, clamp((_e684.w + _e807), 0f, 1f));
                    break;
                }
                if (_e811 == 6f) {
                    let _e880 = _e684.xyz;
                    let _e887 = ((abs((_e880 - _e804.xyz)) * _e807) + (_e880 * (1f - _e807)));
                    phi_991_ = vec4<f32>(_e887.x, _e887.y, _e887.z, clamp((_e684.w + _e807), 0f, 1f));
                    break;
                }
                let _e896 = (1f - _e807);
                let _e903 = ((_e804.xyz * _e807) + (_e684.xyz * _e896));
                phi_991_ = vec4<f32>(_e903.x, _e903.y, _e903.z, (_e807 + (_e684.w * _e896)));
                break;
            }
        }
        let _e909 = phi_991_;
        phi_992_ = _e909;
    }
    let _e911 = phi_992_;
    phi_1233_ = _e911;
    if (_e43 > 4i) {
        let _e915 = FragmentUniformBlock.LayerConfig[4i];
        let _e918 = FragmentUniformBlock.LayerTint[4i];
        let _e921 = FragmentUniformBlock.LayerMotion[4i];
        let _e924 = FragmentUniformBlock.LayerBlack[4i];
        let _e927 = FragmentUniformBlock.LayerTransform[4i];
        let _e930 = FragmentUniformBlock.LayerAdjust[4i];
        let _e936 = FragmentUniformBlock.SurfaceMeta[1i];
        let _e941 = (((_e38 + _e921.zw) - (_e921.xy * _e936)) / max(_e915.xy, vec2<f32>(1f, 1f)));
        phi_1027_ = _e941;
        if (_e927.y != 0f) {
            phi_1027_ = vec2<f32>(-(_e941.x), _e941.y);
        }
        let _e950 = phi_1027_;
        phi_1035_ = _e950;
        if (_e927.z != 0f) {
            phi_1035_ = vec2<f32>(_e950.x, -(_e950.y));
        }
        let _e959 = phi_1035_;
        if (_e927.x == 1f) {
            phi_1059_ = vec2<f32>(_e959.y, -(_e959.x));
        } else {
            if (_e927.x == 2f) {
                phi_1058_ = -(_e959);
            } else {
                phi_1057_ = _e959;
                if (_e927.x == 3f) {
                    phi_1057_ = vec2<f32>(-(_e959.y), _e959.x);
                }
                let _e974 = phi_1057_;
                phi_1058_ = _e974;
            }
            let _e976 = phi_1058_;
            phi_1059_ = _e976;
        }
        let _e978 = phi_1059_;
        let _e979 = textureSample(Layer4_, Layer4Sampler, _e978);
        if (_e915.w == 1f) {
            let _e990 = clamp(((((_e979.x + _e930.z) - 0.5f) * max(_e930.y, 0f)) + 0.5f), 0f, 1f);
            phi_1086_ = _e990;
            if (_e930.w >= 2f) {
                let _e994 = (floor(_e930.w) - 1f);
                phi_1086_ = (floor(((_e990 * _e994) + 0.5f)) / _e994);
            }
            let _e1000 = phi_1086_;
            phi_1117_ = mix(_e924, _e918, vec4(_e1000));
        } else {
            let _e1012 = clamp(((((_e979.xyz + vec3(_e930.z)) - vec3<f32>(0.5f, 0.5f, 0.5f)) * max(_e930.y, 0f)) + vec3<f32>(0.5f, 0.5f, 0.5f)), vec3<f32>(0f, 0f, 0f), vec3<f32>(1f, 1f, 1f));
            phi_1110_ = _e1012;
            if (_e930.w >= 2f) {
                let _e1016 = (floor(_e930.w) - 1f);
                phi_1110_ = (floor(((_e1012 * _e1016) + vec3<f32>(0.5f, 0.5f, 0.5f))) / vec3(_e1016));
            }
            let _e1023 = phi_1110_;
            phi_1117_ = (vec4<f32>(_e1023.x, _e1023.y, _e1023.z, _e979.w) * _e918);
        }
        let _e1031 = phi_1117_;
        let _e1034 = (_e1031.w * _e930.x);
        let _e1038 = FragmentUniformBlock.LayerConfig[4i][2i];
        switch bitcast<i32>(0u) {
            default: {
                if (_e1038 == 1f) {
                    let _e1045 = (_e911.xyz * mix(vec3<f32>(1f, 1f, 1f), _e1031.xyz, vec3(_e1034)));
                    phi_1232_ = vec4<f32>(_e1045.x, _e1045.y, _e1045.z, clamp((_e911.w + _e1034), 0f, 1f));
                    break;
                }
                if (_e1038 == 2f) {
                    let _e1057 = (_e911.xyz + (_e1031.xyz * _e1034));
                    phi_1232_ = vec4<f32>(_e1057.x, _e1057.y, _e1057.z, clamp((_e911.w + _e1034), 0f, 1f));
                    break;
                }
                if (_e1038 == 3f) {
                    let _e1072 = (vec3<f32>(1f, 1f, 1f) - ((vec3<f32>(1f, 1f, 1f) - _e911.xyz) * (vec3<f32>(1f, 1f, 1f) - (_e1031.xyz * _e1034))));
                    phi_1232_ = vec4<f32>(_e1072.x, _e1072.y, _e1072.z, clamp((_e911.w + _e1034), 0f, 1f));
                    break;
                }
                if (_e1038 == 4f) {
                    let _e1081 = _e911.xyz;
                    let _e1085 = mix(_e1081, min(_e1081, _e1031.xyz), vec3(_e1034));
                    phi_1232_ = vec4<f32>(_e1085.x, _e1085.y, _e1085.z, clamp((_e911.w + _e1034), 0f, 1f));
                    break;
                }
                if (_e1038 == 5f) {
                    let _e1094 = _e911.xyz;
                    let _e1098 = mix(_e1094, max(_e1094, _e1031.xyz), vec3(_e1034));
                    phi_1232_ = vec4<f32>(_e1098.x, _e1098.y, _e1098.z, clamp((_e911.w + _e1034), 0f, 1f));
                    break;
                }
                if (_e1038 == 6f) {
                    let _e1107 = _e911.xyz;
                    let _e1114 = ((abs((_e1107 - _e1031.xyz)) * _e1034) + (_e1107 * (1f - _e1034)));
                    phi_1232_ = vec4<f32>(_e1114.x, _e1114.y, _e1114.z, clamp((_e911.w + _e1034), 0f, 1f));
                    break;
                }
                let _e1123 = (1f - _e1034);
                let _e1130 = ((_e1031.xyz * _e1034) + (_e911.xyz * _e1123));
                phi_1232_ = vec4<f32>(_e1130.x, _e1130.y, _e1130.z, (_e1034 + (_e911.w * _e1123)));
                break;
            }
        }
        let _e1136 = phi_1232_;
        phi_1233_ = _e1136;
    }
    let _e1138 = phi_1233_;
    phi_1474_ = _e1138;
    if (_e43 > 5i) {
        let _e1142 = FragmentUniformBlock.LayerConfig[5i];
        let _e1145 = FragmentUniformBlock.LayerTint[5i];
        let _e1148 = FragmentUniformBlock.LayerMotion[5i];
        let _e1151 = FragmentUniformBlock.LayerBlack[5i];
        let _e1154 = FragmentUniformBlock.LayerTransform[5i];
        let _e1157 = FragmentUniformBlock.LayerAdjust[5i];
        let _e1163 = FragmentUniformBlock.SurfaceMeta[1i];
        let _e1168 = (((_e38 + _e1148.zw) - (_e1148.xy * _e1163)) / max(_e1142.xy, vec2<f32>(1f, 1f)));
        phi_1268_ = _e1168;
        if (_e1154.y != 0f) {
            phi_1268_ = vec2<f32>(-(_e1168.x), _e1168.y);
        }
        let _e1177 = phi_1268_;
        phi_1276_ = _e1177;
        if (_e1154.z != 0f) {
            phi_1276_ = vec2<f32>(_e1177.x, -(_e1177.y));
        }
        let _e1186 = phi_1276_;
        if (_e1154.x == 1f) {
            phi_1300_ = vec2<f32>(_e1186.y, -(_e1186.x));
        } else {
            if (_e1154.x == 2f) {
                phi_1299_ = -(_e1186);
            } else {
                phi_1298_ = _e1186;
                if (_e1154.x == 3f) {
                    phi_1298_ = vec2<f32>(-(_e1186.y), _e1186.x);
                }
                let _e1201 = phi_1298_;
                phi_1299_ = _e1201;
            }
            let _e1203 = phi_1299_;
            phi_1300_ = _e1203;
        }
        let _e1205 = phi_1300_;
        let _e1206 = textureSample(Layer5_, Layer5Sampler, _e1205);
        if (_e1142.w == 1f) {
            let _e1217 = clamp(((((_e1206.x + _e1157.z) - 0.5f) * max(_e1157.y, 0f)) + 0.5f), 0f, 1f);
            phi_1327_ = _e1217;
            if (_e1157.w >= 2f) {
                let _e1221 = (floor(_e1157.w) - 1f);
                phi_1327_ = (floor(((_e1217 * _e1221) + 0.5f)) / _e1221);
            }
            let _e1227 = phi_1327_;
            phi_1358_ = mix(_e1151, _e1145, vec4(_e1227));
        } else {
            let _e1239 = clamp(((((_e1206.xyz + vec3(_e1157.z)) - vec3<f32>(0.5f, 0.5f, 0.5f)) * max(_e1157.y, 0f)) + vec3<f32>(0.5f, 0.5f, 0.5f)), vec3<f32>(0f, 0f, 0f), vec3<f32>(1f, 1f, 1f));
            phi_1351_ = _e1239;
            if (_e1157.w >= 2f) {
                let _e1243 = (floor(_e1157.w) - 1f);
                phi_1351_ = (floor(((_e1239 * _e1243) + vec3<f32>(0.5f, 0.5f, 0.5f))) / vec3(_e1243));
            }
            let _e1250 = phi_1351_;
            phi_1358_ = (vec4<f32>(_e1250.x, _e1250.y, _e1250.z, _e1206.w) * _e1145);
        }
        let _e1258 = phi_1358_;
        let _e1261 = (_e1258.w * _e1157.x);
        let _e1265 = FragmentUniformBlock.LayerConfig[5i][2i];
        switch bitcast<i32>(0u) {
            default: {
                if (_e1265 == 1f) {
                    let _e1272 = (_e1138.xyz * mix(vec3<f32>(1f, 1f, 1f), _e1258.xyz, vec3(_e1261)));
                    phi_1473_ = vec4<f32>(_e1272.x, _e1272.y, _e1272.z, clamp((_e1138.w + _e1261), 0f, 1f));
                    break;
                }
                if (_e1265 == 2f) {
                    let _e1284 = (_e1138.xyz + (_e1258.xyz * _e1261));
                    phi_1473_ = vec4<f32>(_e1284.x, _e1284.y, _e1284.z, clamp((_e1138.w + _e1261), 0f, 1f));
                    break;
                }
                if (_e1265 == 3f) {
                    let _e1299 = (vec3<f32>(1f, 1f, 1f) - ((vec3<f32>(1f, 1f, 1f) - _e1138.xyz) * (vec3<f32>(1f, 1f, 1f) - (_e1258.xyz * _e1261))));
                    phi_1473_ = vec4<f32>(_e1299.x, _e1299.y, _e1299.z, clamp((_e1138.w + _e1261), 0f, 1f));
                    break;
                }
                if (_e1265 == 4f) {
                    let _e1308 = _e1138.xyz;
                    let _e1312 = mix(_e1308, min(_e1308, _e1258.xyz), vec3(_e1261));
                    phi_1473_ = vec4<f32>(_e1312.x, _e1312.y, _e1312.z, clamp((_e1138.w + _e1261), 0f, 1f));
                    break;
                }
                if (_e1265 == 5f) {
                    let _e1321 = _e1138.xyz;
                    let _e1325 = mix(_e1321, max(_e1321, _e1258.xyz), vec3(_e1261));
                    phi_1473_ = vec4<f32>(_e1325.x, _e1325.y, _e1325.z, clamp((_e1138.w + _e1261), 0f, 1f));
                    break;
                }
                if (_e1265 == 6f) {
                    let _e1334 = _e1138.xyz;
                    let _e1341 = ((abs((_e1334 - _e1258.xyz)) * _e1261) + (_e1334 * (1f - _e1261)));
                    phi_1473_ = vec4<f32>(_e1341.x, _e1341.y, _e1341.z, clamp((_e1138.w + _e1261), 0f, 1f));
                    break;
                }
                let _e1350 = (1f - _e1261);
                let _e1357 = ((_e1258.xyz * _e1261) + (_e1138.xyz * _e1350));
                phi_1473_ = vec4<f32>(_e1357.x, _e1357.y, _e1357.z, (_e1261 + (_e1138.w * _e1350)));
                break;
            }
        }
        let _e1363 = phi_1473_;
        phi_1474_ = _e1363;
    }
    let _e1365 = phi_1474_;
    out_u002e_var_u002e_SV_Target0_ = (_e1365 * _e39);
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
