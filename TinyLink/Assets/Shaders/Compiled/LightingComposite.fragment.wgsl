struct type_u002e_FragmentUniformBlock {
    TextureSizeCamera: vec4<f32>,
    DitherSettings: vec4<f32>,
    DitherGlitch: vec4<f32>,
    DitherGlitchAxis: vec4<f32>,
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
var<private> in_u002e_var_u002e_TEXCOORD0_1: vec2<f32>;
var<private> in_u002e_var_u002e_TEXCOORD1_1: vec4<f32>;
var<private> global: vec4<f32>;
var<private> out_u002e_var_u002e_SV_Target0_: vec4<f32>;

fn fragment_main_1() {
    var phi_244_: f32;
    var phi_279_: vec2<f32>;
    var phi_280_: vec2<f32>;
    var phi_281_: vec2<f32>;
    var phi_282_: vec2<f32>;
    var phi_312_: f32;
    var phi_321_: f32;
    var phi_331_: f32;
    var phi_341_: f32;
    var phi_348_: f32;
    var phi_349_: f32;
    var phi_350_: f32;
    var phi_437_: f32;
    var phi_438_: f32;
    var phi_453_: f32;
    var phi_454_: f32;
    var phi_469_: f32;
    var phi_470_: f32;
    var phi_481_: f32;
    var phi_482_: f32;
    var phi_483_: f32;
    var phi_484_: f32;
    var phi_485_: f32;
    var phi_493_: f32;
    var phi_552_: f32;
    var phi_553_: f32;
    var phi_568_: f32;
    var phi_569_: f32;
    var phi_584_: f32;
    var phi_585_: f32;
    var phi_596_: f32;
    var phi_597_: f32;
    var phi_598_: f32;
    var phi_599_: f32;
    var phi_600_: f32;
    var phi_602_: f32;
    var phi_623_: vec3<f32>;

    let _e82 = in_u002e_var_u002e_TEXCOORD0_1;
    let _e83 = in_u002e_var_u002e_TEXCOORD1_1;
    let _e84 = global;
    let _e89 = vec2<f32>(_e82.x, (1f - _e82.y));
    let _e90 = textureSample(Texture, Sampler, _e89);
    let _e91 = (_e90 * _e83);
    let _e92 = textureSample(LightTexture, LightSampler, _e89);
    let _e93 = textureSample(TileFieldTexture, TileFieldSampler, _e89);
    let _e94 = textureSample(DitherMaskTexture, DitherMaskSampler, _e89);
    let _e96 = textureSample(EmissiveTexture, EmissiveSampler, _e89);
    let _e98 = FragmentUniformBlock.TextureSizeCamera;
    let _e101 = (_e98.zw + _e84.xy);
    let _e102 = _e92.xyz;
    switch bitcast<i32>(0u) {
        default: {
            let _e106 = FragmentUniformBlock.DitherSettings[0i];
            if (_e106 <= 0f) {
                phi_623_ = _e102;
                break;
            }
            let _e112 = max(max(_e92.x, _e92.y), _e92.z);
            if (_e112 <= 0.0001f) {
                phi_623_ = _e102;
                break;
            }
            let _e117 = floor(((_e93.y * 4f) + 0.5f));
            let _e121 = floor(((_e93.x * 255f) + 0.5f));
            let _e124 = FragmentUniformBlock.DitherSettings[3i];
            let _e128 = ((_e124 * _e124) * (3f - (2f * _e124)));
            let _e132 = FragmentUniformBlock.DitherGlitch[2i];
            let _e134 = mix(24f, (_e132 * 1.45f), _e128);
            let _e136 = FragmentUniformBlock.DitherGlitch;
            let _e139 = FragmentUniformBlock.DitherGlitchAxis;
            let _e141 = normalize(_e139.xy);
            let _e146 = (_e101 - _e136.xy);
            let _e149 = (dot(_e146, _e141) / max(_e134, 1f));
            let _e152 = (dot(_e146, vec2<f32>(-(_e141.y), _e141.x)) / max((_e134 * 0.38f), 1f));
            let _e163 = (((_e128 * smoothstep(1f, 0.25f, sqrt(((_e149 * _e149) + (_e152 * _e152))))) * 0.82f) + ((_e128 * smoothstep(0.6f, 1f, _e128)) * 0.18f));
            let _e166 = FragmentUniformBlock.DitherGlitch[3i];
            let _e168 = floor((_e166 * 9f));
            let _e170 = floor((_e101 * vec2<f32>(0.0625f, 0.0625f)));
            let _e192 = fract((sin(((((_e121 * 19.19f) + (_e170.x * 7.13f)) + (_e170.y * 3.71f)) + (_e168 * 5.17f))) * 43758.547f));
            let _e193 = (fract((sin(((((_e170.x * 17f) + (_e170.y * 31f)) + (_e168 * 13f)) + (_e121 * 0.23f))) * 43758.547f)) - 0.5f);
            let _e199 = (_e117 == 2f);
            if _e199 {
                phi_244_ = 2f;
            } else {
                phi_244_ = select(1f, 3f, (_e117 == 3f));
            }
            let _e203 = phi_244_;
            let _e204 = (_e121 % 3f);
            let _e209 = FragmentUniformBlock.DitherSettings[1i];
            let _e215 = floor(((_e101 + ((vec2<f32>(_e193, (_e192 - 0.5f)) * _e163) * 9f)) / vec2(max(((_e209 * _e203) * (1f + (_e204 * 0.08f))), 1f))));
            let _e216 = (_e117 == 1f);
            if _e216 {
                phi_282_ = vec2<f32>(-(_e215.x), _e215.y);
            } else {
                if _e199 {
                    phi_281_ = _e215.yx;
                } else {
                    if (_e117 == 3f) {
                        phi_280_ = vec2<f32>(_e215.x, -(_e215.y));
                    } else {
                        phi_279_ = _e215;
                        if (_e117 >= 4f) {
                            phi_279_ = -(_e215.yx);
                        }
                        let _e233 = phi_279_;
                        phi_280_ = _e233;
                    }
                    let _e235 = phi_280_;
                    phi_281_ = _e235;
                }
                let _e237 = phi_281_;
                phi_282_ = _e237;
            }
            let _e239 = phi_282_;
            let _e252 = (_e239 + (vec2<f32>(((_e117 * 2f) % 4f), ((_e117 * 3f) % 4f)) + (vec2<f32>((_e121 % 4f), (floor((_e121 * 0.25f)) % 4f)) * 0.35f)));
            if (_e163 > 0.65f) {
                phi_350_ = floor((_e192 * 11f));
            } else {
                switch bitcast<i32>(0u) {
                    default: {
                        if _e216 {
                            if (_e204 == 0f) {
                                phi_312_ = 8f;
                            } else {
                                phi_312_ = select(5f, 6f, (_e204 == 1f));
                            }
                            let _e261 = phi_312_;
                            phi_349_ = _e261;
                            break;
                        }
                        if _e199 {
                            if (_e204 == 0f) {
                                phi_321_ = 2f;
                            } else {
                                phi_321_ = select(10f, 9f, (_e204 == 1f));
                            }
                            let _e266 = phi_321_;
                            phi_349_ = _e266;
                            break;
                        }
                        if (_e117 == 3f) {
                            if (_e204 == 0f) {
                                phi_331_ = 3f;
                            } else {
                                phi_331_ = select(1f, 5f, (_e204 == 1f));
                            }
                            let _e272 = phi_331_;
                            phi_349_ = _e272;
                            break;
                        }
                        if (_e117 >= 4f) {
                            if (_e204 == 0f) {
                                phi_341_ = 4f;
                            } else {
                                phi_341_ = select(10f, 7f, (_e204 == 1f));
                            }
                            let _e278 = phi_341_;
                            phi_349_ = _e278;
                            break;
                        }
                        if (_e204 == 0f) {
                            phi_348_ = 0f;
                        } else {
                            phi_348_ = select(7f, 4f, (_e204 == 1f));
                        }
                        let _e283 = phi_348_;
                        phi_349_ = _e283;
                        break;
                    }
                }
                let _e285 = phi_349_;
                phi_350_ = _e285;
            }
            let _e287 = phi_350_;
            switch bitcast<i32>(0u) {
                default: {
                    if (_e287 == 1f) {
                        phi_602_ = ((((_e252.x + (_e252.y * 2f)) % 6f) + 1f) * 0.14285715f);
                        break;
                    }
                    if (_e287 == 2f) {
                        let _e299 = ((_e252 % vec2<f32>(4f, 4f)) - vec2<f32>(1.5f, 1.5f));
                        phi_602_ = clamp(((dot(_e299, _e299) + 0.5f) * 0.2f), 0f, 1f);
                        break;
                    }
                    if (_e287 == 3f) {
                        phi_602_ = (((_e252.x % 4f) + 1f) * 0.2f);
                        break;
                    }
                    if (_e287 == 4f) {
                        phi_602_ = fract((sin(dot(floor(_e252), vec2<f32>(127.1f, 311.7f))) * 43758.547f));
                        break;
                    }
                    if (_e287 == 5f) {
                        phi_602_ = (((_e252.y % 4f) + 1f) * 0.2f);
                        break;
                    }
                    if (_e287 == 6f) {
                        phi_602_ = ((min(((_e252.x + _e252.y) % 4f), (((_e252.x - _e252.y) + 8f) % 4f)) + 1f) * 0.2f);
                        break;
                    }
                    if (_e287 == 7f) {
                        let _e332 = (_e252 % vec2<f32>(4f, 4f));
                        let _e341 = i32((_e252.x % 4f));
                        let _e344 = i32((_e252.y % 4f));
                        if (_e341 == 0i) {
                            if (_e344 == 0i) {
                                phi_438_ = 16f;
                            } else {
                                if (_e344 == 1i) {
                                    phi_437_ = 5f;
                                } else {
                                    phi_437_ = select(1f, 13f, (_e344 == 2i));
                                }
                                let _e351 = phi_437_;
                                phi_438_ = _e351;
                            }
                            let _e353 = phi_438_;
                            phi_485_ = _e353;
                        } else {
                            if (_e341 == 1i) {
                                if (_e344 == 0i) {
                                    phi_454_ = 8f;
                                } else {
                                    if (_e344 == 1i) {
                                        phi_453_ = 12f;
                                    } else {
                                        phi_453_ = select(9f, 4f, (_e344 == 2i));
                                    }
                                    let _e360 = phi_453_;
                                    phi_454_ = _e360;
                                }
                                let _e362 = phi_454_;
                                phi_484_ = _e362;
                            } else {
                                if (_e341 == 2i) {
                                    if (_e344 == 0i) {
                                        phi_470_ = 14f;
                                    } else {
                                        if (_e344 == 1i) {
                                            phi_469_ = 2f;
                                        } else {
                                            phi_469_ = select(3f, 15f, (_e344 == 2i));
                                        }
                                        let _e369 = phi_469_;
                                        phi_470_ = _e369;
                                    }
                                    let _e371 = phi_470_;
                                    phi_483_ = _e371;
                                } else {
                                    if (_e344 == 0i) {
                                        phi_482_ = 6f;
                                    } else {
                                        if (_e344 == 1i) {
                                            phi_481_ = 10f;
                                        } else {
                                            phi_481_ = select(11f, 7f, (_e344 == 2i));
                                        }
                                        let _e377 = phi_481_;
                                        phi_482_ = _e377;
                                    }
                                    let _e379 = phi_482_;
                                    phi_483_ = _e379;
                                }
                                let _e381 = phi_483_;
                                phi_484_ = _e381;
                            }
                            let _e383 = phi_484_;
                            phi_485_ = _e383;
                        }
                        let _e385 = phi_485_;
                        if (((floor(_e332.x) + floor(_e332.y)) % 2f) == 0f) {
                            phi_493_ = (_e385 * 0.04411765f);
                        } else {
                            phi_493_ = (0.35f + (_e385 * 0.03823529f));
                        }
                        let _e391 = phi_493_;
                        phi_602_ = _e391;
                        break;
                    }
                    if (_e287 == 8f) {
                        phi_602_ = clamp(((min(((_e252.x + ((floor((_e252.y * 0.5f)) % 2f) * 2f)) % 4f), (_e252.y % 4f)) + 1f) * 0.2f), 0f, 1f);
                        break;
                    }
                    if (_e287 == 9f) {
                        phi_602_ = (((((_e252.x * 2f) + _e252.y) % 8f) + 1f) * 0.11111111f);
                        break;
                    }
                    if (_e287 >= 10f) {
                        let _e417 = abs(((_e252 % vec2<f32>(8f, 8f)) - vec2<f32>(3.5f, 3.5f)));
                        phi_602_ = clamp(((max(_e417.x, _e417.y) - 0.5f) * 0.2857143f), 0f, 1f);
                        break;
                    }
                    let _e426 = i32((_e252.x % 4f));
                    let _e429 = i32((_e252.y % 4f));
                    if (_e426 == 0i) {
                        if (_e429 == 0i) {
                            phi_553_ = 16f;
                        } else {
                            if (_e429 == 1i) {
                                phi_552_ = 5f;
                            } else {
                                phi_552_ = select(1f, 13f, (_e429 == 2i));
                            }
                            let _e436 = phi_552_;
                            phi_553_ = _e436;
                        }
                        let _e438 = phi_553_;
                        phi_600_ = _e438;
                    } else {
                        if (_e426 == 1i) {
                            if (_e429 == 0i) {
                                phi_569_ = 8f;
                            } else {
                                if (_e429 == 1i) {
                                    phi_568_ = 12f;
                                } else {
                                    phi_568_ = select(9f, 4f, (_e429 == 2i));
                                }
                                let _e445 = phi_568_;
                                phi_569_ = _e445;
                            }
                            let _e447 = phi_569_;
                            phi_599_ = _e447;
                        } else {
                            if (_e426 == 2i) {
                                if (_e429 == 0i) {
                                    phi_585_ = 14f;
                                } else {
                                    if (_e429 == 1i) {
                                        phi_584_ = 2f;
                                    } else {
                                        phi_584_ = select(3f, 15f, (_e429 == 2i));
                                    }
                                    let _e454 = phi_584_;
                                    phi_585_ = _e454;
                                }
                                let _e456 = phi_585_;
                                phi_598_ = _e456;
                            } else {
                                if (_e429 == 0i) {
                                    phi_597_ = 6f;
                                } else {
                                    if (_e429 == 1i) {
                                        phi_596_ = 10f;
                                    } else {
                                        phi_596_ = select(11f, 7f, (_e429 == 2i));
                                    }
                                    let _e462 = phi_596_;
                                    phi_597_ = _e462;
                                }
                                let _e464 = phi_597_;
                                phi_598_ = _e464;
                            }
                            let _e466 = phi_598_;
                            phi_599_ = _e466;
                        }
                        let _e468 = phi_599_;
                        phi_600_ = _e468;
                    }
                    let _e470 = phi_600_;
                    phi_602_ = (_e470 * 0.05882353f);
                    break;
                }
            }
            let _e473 = phi_602_;
            let _e490 = FragmentUniformBlock.DitherSettings[2i];
            phi_623_ = (_e102 * mix(1f, (select(1f, 0f, (clamp(_e112, 0f, 1f) < clamp((clamp((_e473 + ((fract((_e121 * 0.618034f)) - 0.5f) * 0.1f)), 0f, 1f) + ((_e193 * _e163) * 0.18f)), 0f, 1f))) / _e112), clamp((_e490 * _e94.x), 0f, 1f)));
            break;
        }
    }
    let _e496 = phi_623_;
    let _e509 = _e496.xyz;
    let _e517 = clamp(mix(((_e91.xyz * _e509) + (_e509 * ((_e92.w * clamp((1f - (max(max(_e91.x, _e91.y), _e91.z) * 2.2f)), 0f, 1f)) * 0.18f))), _e96.xyz, vec3(_e96.w)), vec3<f32>(0f, 0f, 0f), vec3<f32>(1f, 1f, 1f));
    out_u002e_var_u002e_SV_Target0_ = vec4<f32>(_e517.x, _e517.y, _e517.z, _e91.w);
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
