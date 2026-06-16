struct type_u002e_FragmentUniformBlock {
    TextureSizeCamera: vec4<f32>,
    DitherSettings: vec4<f32>,
    DitherGlitch: vec4<f32>,
    DitherGlitchAxis: vec4<f32>,
    AoSettings: vec4<f32>,
}

@group(3) @binding(0) 
var<uniform> FragmentUniformBlock: type_u002e_FragmentUniformBlock;
@group(2) @binding(0) 
var Texture: texture_2d<f32>;
@group(2) @binding(100) 
var Sampler: sampler;
@group(2) @binding(1) 
var LightTexture: texture_2d<f32>;
@group(2) @binding(101) 
var LightSampler: sampler;
@group(2) @binding(2) 
var TileFieldTexture: texture_2d<f32>;
@group(2) @binding(102) 
var TileFieldSampler: sampler;
@group(2) @binding(3) 
var DitherMaskTexture: texture_2d<f32>;
@group(2) @binding(103) 
var DitherMaskSampler: sampler;
@group(2) @binding(4) 
var EmissiveTexture: texture_2d<f32>;
@group(2) @binding(104) 
var EmissiveSampler: sampler;
@group(2) @binding(5) 
var AoTexture: texture_2d<f32>;
@group(2) @binding(105) 
var AoSampler: sampler;
@group(2) @binding(6) 
var BackgroundCoverageTexture: texture_2d<f32>;
@group(2) @binding(106) 
var BackgroundCoverageSampler: sampler;
var<private> in_u002e_var_u002e_TEXCOORD0_1: vec2<f32>;
var<private> in_u002e_var_u002e_TEXCOORD1_1: vec4<f32>;
var<private> in_u002e_var_u002e_TEXCOORD4_1: vec4<f32>;
var<private> global: vec4<f32>;
var<private> out_u002e_var_u002e_SV_Target0_: vec4<f32>;

fn fragment_main_1() {
    var phi_189_: vec4<f32>;
    var phi_293_: f32;
    var phi_328_: vec2<f32>;
    var phi_329_: vec2<f32>;
    var phi_330_: vec2<f32>;
    var phi_331_: vec2<f32>;
    var phi_361_: f32;
    var phi_370_: f32;
    var phi_380_: f32;
    var phi_390_: f32;
    var phi_397_: f32;
    var phi_398_: f32;
    var phi_399_: f32;
    var phi_486_: f32;
    var phi_487_: f32;
    var phi_502_: f32;
    var phi_503_: f32;
    var phi_518_: f32;
    var phi_519_: f32;
    var phi_530_: f32;
    var phi_531_: f32;
    var phi_532_: f32;
    var phi_533_: f32;
    var phi_534_: f32;
    var phi_542_: f32;
    var phi_601_: f32;
    var phi_602_: f32;
    var phi_617_: f32;
    var phi_618_: f32;
    var phi_633_: f32;
    var phi_634_: f32;
    var phi_645_: f32;
    var phi_646_: f32;
    var phi_647_: f32;
    var phi_648_: f32;
    var phi_649_: f32;
    var phi_651_: f32;
    var phi_672_: vec3<f32>;

    let _e89 = in_u002e_var_u002e_TEXCOORD0_1;
    let _e90 = in_u002e_var_u002e_TEXCOORD1_1;
    let _e91 = in_u002e_var_u002e_TEXCOORD4_1;
    let _e92 = global;
    if (_e91.x < -1000f) {
        discard;
    }
    let _e99 = vec2<f32>(_e89.x, (1f - _e89.y));
    let _e100 = textureSample(Texture, Sampler, _e99);
    let _e101 = (_e100 * _e90);
    let _e102 = textureSample(LightTexture, LightSampler, _e99);
    let _e103 = textureSample(TileFieldTexture, TileFieldSampler, _e99);
    let _e104 = textureSample(DitherMaskTexture, DitherMaskSampler, _e99);
    let _e106 = textureSample(EmissiveTexture, EmissiveSampler, _e99);
    let _e110 = FragmentUniformBlock.DitherGlitchAxis[2i];
    let _e114 = FragmentUniformBlock.TextureSizeCamera;
    let _e119 = (_e114.zw + (_e92.xy / vec2(select(1f, _e110, (_e110 > 0f)))));
    let _e123 = FragmentUniformBlock.AoSettings[0i];
    phi_189_ = _e102;
    if (_e123 > 0f) {
        let _e125 = FragmentUniformBlock.AoSettings;
        let _e130 = textureSample(AoTexture, AoSampler, (_e99 + (_e125.yz / _e114.xy)));
        let _e134 = textureSample(BackgroundCoverageTexture, BackgroundCoverageSampler, _e99);
        let _e138 = FragmentUniformBlock.AoSettings[3i];
        let _e142 = (_e102.xyz * mix(1f, _e138, (clamp((_e130.x * _e123), 0f, 1f) * _e134.w)));
        phi_189_ = vec4<f32>(_e142.x, _e142.y, _e142.z, _e102.w);
    }
    let _e149 = phi_189_;
    let _e150 = _e149.xyz;
    switch bitcast<i32>(0u) {
        default: {
            let _e154 = FragmentUniformBlock.DitherSettings[0i];
            if (_e154 <= 0f) {
                phi_672_ = _e150;
                break;
            }
            let _e160 = max(max(_e149.x, _e149.y), _e149.z);
            if (_e160 <= 0.0001f) {
                phi_672_ = _e150;
                break;
            }
            let _e165 = floor(((_e103.y * 4f) + 0.5f));
            let _e169 = floor(((_e103.x * 255f) + 0.5f));
            let _e172 = FragmentUniformBlock.DitherSettings[3i];
            let _e176 = ((_e172 * _e172) * (3f - (2f * _e172)));
            let _e180 = FragmentUniformBlock.DitherGlitch[2i];
            let _e182 = mix(24f, (_e180 * 1.45f), _e176);
            let _e184 = FragmentUniformBlock.DitherGlitch;
            let _e186 = FragmentUniformBlock.DitherGlitchAxis;
            let _e188 = normalize(_e186.xy);
            let _e193 = (_e119 - _e184.xy);
            let _e196 = (dot(_e193, _e188) / max(_e182, 1f));
            let _e199 = (dot(_e193, vec2<f32>(-(_e188.y), _e188.x)) / max((_e182 * 0.38f), 1f));
            let _e210 = (((_e176 * smoothstep(1f, 0.25f, sqrt(((_e196 * _e196) + (_e199 * _e199))))) * 0.82f) + ((_e176 * smoothstep(0.6f, 1f, _e176)) * 0.18f));
            let _e213 = FragmentUniformBlock.DitherGlitch[3i];
            let _e215 = floor((_e213 * 9f));
            let _e217 = floor((_e119 * vec2<f32>(0.0625f, 0.0625f)));
            let _e239 = fract((sin(((((_e169 * 19.19f) + (_e217.x * 7.13f)) + (_e217.y * 3.71f)) + (_e215 * 5.17f))) * 43758.547f));
            let _e240 = (fract((sin(((((_e217.x * 17f) + (_e217.y * 31f)) + (_e215 * 13f)) + (_e169 * 0.23f))) * 43758.547f)) - 0.5f);
            let _e246 = (_e165 == 2f);
            if _e246 {
                phi_293_ = 2f;
            } else {
                phi_293_ = select(1f, 3f, (_e165 == 3f));
            }
            let _e250 = phi_293_;
            let _e251 = (_e169 % 3f);
            let _e256 = FragmentUniformBlock.DitherSettings[1i];
            let _e262 = floor(((_e119 + ((vec2<f32>(_e240, (_e239 - 0.5f)) * _e210) * 9f)) / vec2(max(((_e256 * _e250) * (1f + (_e251 * 0.08f))), 1f))));
            let _e263 = (_e165 == 1f);
            if _e263 {
                phi_331_ = vec2<f32>(-(_e262.x), _e262.y);
            } else {
                if _e246 {
                    phi_330_ = _e262.yx;
                } else {
                    if (_e165 == 3f) {
                        phi_329_ = vec2<f32>(_e262.x, -(_e262.y));
                    } else {
                        phi_328_ = _e262;
                        if (_e165 >= 4f) {
                            phi_328_ = -(_e262.yx);
                        }
                        let _e280 = phi_328_;
                        phi_329_ = _e280;
                    }
                    let _e282 = phi_329_;
                    phi_330_ = _e282;
                }
                let _e284 = phi_330_;
                phi_331_ = _e284;
            }
            let _e286 = phi_331_;
            let _e299 = (_e286 + (vec2<f32>(((_e165 * 2f) % 4f), ((_e165 * 3f) % 4f)) + (vec2<f32>((_e169 % 4f), (floor((_e169 * 0.25f)) % 4f)) * 0.35f)));
            if (_e210 > 0.65f) {
                phi_399_ = floor((_e239 * 11f));
            } else {
                switch bitcast<i32>(0u) {
                    default: {
                        if _e263 {
                            if (_e251 == 0f) {
                                phi_361_ = 8f;
                            } else {
                                phi_361_ = select(5f, 6f, (_e251 == 1f));
                            }
                            let _e308 = phi_361_;
                            phi_398_ = _e308;
                            break;
                        }
                        if _e246 {
                            if (_e251 == 0f) {
                                phi_370_ = 2f;
                            } else {
                                phi_370_ = select(10f, 9f, (_e251 == 1f));
                            }
                            let _e313 = phi_370_;
                            phi_398_ = _e313;
                            break;
                        }
                        if (_e165 == 3f) {
                            if (_e251 == 0f) {
                                phi_380_ = 3f;
                            } else {
                                phi_380_ = select(1f, 5f, (_e251 == 1f));
                            }
                            let _e319 = phi_380_;
                            phi_398_ = _e319;
                            break;
                        }
                        if (_e165 >= 4f) {
                            if (_e251 == 0f) {
                                phi_390_ = 4f;
                            } else {
                                phi_390_ = select(10f, 7f, (_e251 == 1f));
                            }
                            let _e325 = phi_390_;
                            phi_398_ = _e325;
                            break;
                        }
                        if (_e251 == 0f) {
                            phi_397_ = 0f;
                        } else {
                            phi_397_ = select(7f, 4f, (_e251 == 1f));
                        }
                        let _e330 = phi_397_;
                        phi_398_ = _e330;
                        break;
                    }
                }
                let _e332 = phi_398_;
                phi_399_ = _e332;
            }
            let _e334 = phi_399_;
            switch bitcast<i32>(0u) {
                default: {
                    if (_e334 == 1f) {
                        phi_651_ = ((((_e299.x + (_e299.y * 2f)) % 6f) + 1f) * 0.14285715f);
                        break;
                    }
                    if (_e334 == 2f) {
                        let _e346 = ((_e299 % vec2<f32>(4f, 4f)) - vec2<f32>(1.5f, 1.5f));
                        phi_651_ = clamp(((dot(_e346, _e346) + 0.5f) * 0.2f), 0f, 1f);
                        break;
                    }
                    if (_e334 == 3f) {
                        phi_651_ = (((_e299.x % 4f) + 1f) * 0.2f);
                        break;
                    }
                    if (_e334 == 4f) {
                        phi_651_ = fract((sin(dot(floor(_e299), vec2<f32>(127.1f, 311.7f))) * 43758.547f));
                        break;
                    }
                    if (_e334 == 5f) {
                        phi_651_ = (((_e299.y % 4f) + 1f) * 0.2f);
                        break;
                    }
                    if (_e334 == 6f) {
                        phi_651_ = ((min(((_e299.x + _e299.y) % 4f), (((_e299.x - _e299.y) + 8f) % 4f)) + 1f) * 0.2f);
                        break;
                    }
                    if (_e334 == 7f) {
                        let _e379 = (_e299 % vec2<f32>(4f, 4f));
                        let _e388 = i32((_e299.x % 4f));
                        let _e391 = i32((_e299.y % 4f));
                        if (_e388 == 0i) {
                            if (_e391 == 0i) {
                                phi_487_ = 16f;
                            } else {
                                if (_e391 == 1i) {
                                    phi_486_ = 5f;
                                } else {
                                    phi_486_ = select(1f, 13f, (_e391 == 2i));
                                }
                                let _e398 = phi_486_;
                                phi_487_ = _e398;
                            }
                            let _e400 = phi_487_;
                            phi_534_ = _e400;
                        } else {
                            if (_e388 == 1i) {
                                if (_e391 == 0i) {
                                    phi_503_ = 8f;
                                } else {
                                    if (_e391 == 1i) {
                                        phi_502_ = 12f;
                                    } else {
                                        phi_502_ = select(9f, 4f, (_e391 == 2i));
                                    }
                                    let _e407 = phi_502_;
                                    phi_503_ = _e407;
                                }
                                let _e409 = phi_503_;
                                phi_533_ = _e409;
                            } else {
                                if (_e388 == 2i) {
                                    if (_e391 == 0i) {
                                        phi_519_ = 14f;
                                    } else {
                                        if (_e391 == 1i) {
                                            phi_518_ = 2f;
                                        } else {
                                            phi_518_ = select(3f, 15f, (_e391 == 2i));
                                        }
                                        let _e416 = phi_518_;
                                        phi_519_ = _e416;
                                    }
                                    let _e418 = phi_519_;
                                    phi_532_ = _e418;
                                } else {
                                    if (_e391 == 0i) {
                                        phi_531_ = 6f;
                                    } else {
                                        if (_e391 == 1i) {
                                            phi_530_ = 10f;
                                        } else {
                                            phi_530_ = select(11f, 7f, (_e391 == 2i));
                                        }
                                        let _e424 = phi_530_;
                                        phi_531_ = _e424;
                                    }
                                    let _e426 = phi_531_;
                                    phi_532_ = _e426;
                                }
                                let _e428 = phi_532_;
                                phi_533_ = _e428;
                            }
                            let _e430 = phi_533_;
                            phi_534_ = _e430;
                        }
                        let _e432 = phi_534_;
                        if (((floor(_e379.x) + floor(_e379.y)) % 2f) == 0f) {
                            phi_542_ = (_e432 * 0.04411765f);
                        } else {
                            phi_542_ = (0.35f + (_e432 * 0.03823529f));
                        }
                        let _e438 = phi_542_;
                        phi_651_ = _e438;
                        break;
                    }
                    if (_e334 == 8f) {
                        phi_651_ = clamp(((min(((_e299.x + ((floor((_e299.y * 0.5f)) % 2f) * 2f)) % 4f), (_e299.y % 4f)) + 1f) * 0.2f), 0f, 1f);
                        break;
                    }
                    if (_e334 == 9f) {
                        phi_651_ = (((((_e299.x * 2f) + _e299.y) % 8f) + 1f) * 0.11111111f);
                        break;
                    }
                    if (_e334 >= 10f) {
                        let _e464 = abs(((_e299 % vec2<f32>(8f, 8f)) - vec2<f32>(3.5f, 3.5f)));
                        phi_651_ = clamp(((max(_e464.x, _e464.y) - 0.5f) * 0.2857143f), 0f, 1f);
                        break;
                    }
                    let _e473 = i32((_e299.x % 4f));
                    let _e476 = i32((_e299.y % 4f));
                    if (_e473 == 0i) {
                        if (_e476 == 0i) {
                            phi_602_ = 16f;
                        } else {
                            if (_e476 == 1i) {
                                phi_601_ = 5f;
                            } else {
                                phi_601_ = select(1f, 13f, (_e476 == 2i));
                            }
                            let _e483 = phi_601_;
                            phi_602_ = _e483;
                        }
                        let _e485 = phi_602_;
                        phi_649_ = _e485;
                    } else {
                        if (_e473 == 1i) {
                            if (_e476 == 0i) {
                                phi_618_ = 8f;
                            } else {
                                if (_e476 == 1i) {
                                    phi_617_ = 12f;
                                } else {
                                    phi_617_ = select(9f, 4f, (_e476 == 2i));
                                }
                                let _e492 = phi_617_;
                                phi_618_ = _e492;
                            }
                            let _e494 = phi_618_;
                            phi_648_ = _e494;
                        } else {
                            if (_e473 == 2i) {
                                if (_e476 == 0i) {
                                    phi_634_ = 14f;
                                } else {
                                    if (_e476 == 1i) {
                                        phi_633_ = 2f;
                                    } else {
                                        phi_633_ = select(3f, 15f, (_e476 == 2i));
                                    }
                                    let _e501 = phi_633_;
                                    phi_634_ = _e501;
                                }
                                let _e503 = phi_634_;
                                phi_647_ = _e503;
                            } else {
                                if (_e476 == 0i) {
                                    phi_646_ = 6f;
                                } else {
                                    if (_e476 == 1i) {
                                        phi_645_ = 10f;
                                    } else {
                                        phi_645_ = select(11f, 7f, (_e476 == 2i));
                                    }
                                    let _e509 = phi_645_;
                                    phi_646_ = _e509;
                                }
                                let _e511 = phi_646_;
                                phi_647_ = _e511;
                            }
                            let _e513 = phi_647_;
                            phi_648_ = _e513;
                        }
                        let _e515 = phi_648_;
                        phi_649_ = _e515;
                    }
                    let _e517 = phi_649_;
                    phi_651_ = (_e517 * 0.05882353f);
                    break;
                }
            }
            let _e520 = phi_651_;
            let _e537 = FragmentUniformBlock.DitherSettings[2i];
            phi_672_ = (_e150 * mix(1f, (select(1f, 0f, (clamp(_e160, 0f, 1f) < clamp((clamp((_e520 + ((fract((_e169 * 0.618034f)) - 0.5f) * 0.1f)), 0f, 1f) + ((_e240 * _e210) * 0.18f)), 0f, 1f))) / _e160), clamp((_e537 * _e104.x), 0f, 1f)));
            break;
        }
    }
    let _e543 = phi_672_;
    let _e556 = _e543.xyz;
    let _e564 = clamp(mix(((_e101.xyz * _e556) + (_e556 * ((_e149.w * clamp((1f - (max(max(_e101.x, _e101.y), _e101.z) * 2.2f)), 0f, 1f)) * 0.18f))), _e106.xyz, vec3(_e106.w)), vec3<f32>(0f, 0f, 0f), vec3<f32>(1f, 1f, 1f));
    out_u002e_var_u002e_SV_Target0_ = vec4<f32>(_e564.x, _e564.y, _e564.z, _e101.w);
    return;
}

@fragment 
fn fragment_main(@location(0) in_u002e_var_u002e_TEXCOORD0_: vec2<f32>, @location(1) in_u002e_var_u002e_TEXCOORD1_: vec4<f32>, @location(2) in_u002e_var_u002e_TEXCOORD4_: vec4<f32>, @builtin(position) param: vec4<f32>) -> @location(0) vec4<f32> {
    in_u002e_var_u002e_TEXCOORD0_1 = in_u002e_var_u002e_TEXCOORD0_;
    in_u002e_var_u002e_TEXCOORD1_1 = in_u002e_var_u002e_TEXCOORD1_;
    in_u002e_var_u002e_TEXCOORD4_1 = in_u002e_var_u002e_TEXCOORD4_;
    global = param;
    fragment_main_1();
    let _e9 = out_u002e_var_u002e_SV_Target0_;
    return _e9;
}
