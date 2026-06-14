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
var<private> in_u002e_var_u002e_TEXCOORD4_1: vec4<f32>;
var<private> global: vec4<f32>;
var<private> out_u002e_var_u002e_SV_Target0_: vec4<f32>;

fn fragment_main_1() {
    var phi_257_: f32;
    var phi_292_: vec2<f32>;
    var phi_293_: vec2<f32>;
    var phi_294_: vec2<f32>;
    var phi_295_: vec2<f32>;
    var phi_325_: f32;
    var phi_334_: f32;
    var phi_344_: f32;
    var phi_354_: f32;
    var phi_361_: f32;
    var phi_362_: f32;
    var phi_363_: f32;
    var phi_450_: f32;
    var phi_451_: f32;
    var phi_466_: f32;
    var phi_467_: f32;
    var phi_482_: f32;
    var phi_483_: f32;
    var phi_494_: f32;
    var phi_495_: f32;
    var phi_496_: f32;
    var phi_497_: f32;
    var phi_498_: f32;
    var phi_506_: f32;
    var phi_565_: f32;
    var phi_566_: f32;
    var phi_581_: f32;
    var phi_582_: f32;
    var phi_597_: f32;
    var phi_598_: f32;
    var phi_609_: f32;
    var phi_610_: f32;
    var phi_611_: f32;
    var phi_612_: f32;
    var phi_613_: f32;
    var phi_615_: f32;
    var phi_636_: vec3<f32>;

    let _e84 = in_u002e_var_u002e_TEXCOORD0_1;
    let _e85 = in_u002e_var_u002e_TEXCOORD1_1;
    let _e86 = in_u002e_var_u002e_TEXCOORD4_1;
    let _e87 = global;
    if (_e86.x < -1000f) {
        discard;
    }
    let _e94 = vec2<f32>(_e84.x, (1f - _e84.y));
    let _e95 = textureSample(Texture, Sampler, _e94);
    let _e96 = (_e95 * _e85);
    let _e97 = textureSample(LightTexture, LightSampler, _e94);
    let _e98 = textureSample(TileFieldTexture, TileFieldSampler, _e94);
    let _e99 = textureSample(DitherMaskTexture, DitherMaskSampler, _e94);
    let _e101 = textureSample(EmissiveTexture, EmissiveSampler, _e94);
    let _e105 = FragmentUniformBlock.DitherGlitchAxis[2i];
    let _e109 = FragmentUniformBlock.TextureSizeCamera;
    let _e114 = (_e109.zw + (_e87.xy / vec2(select(1f, _e105, (_e105 > 0f)))));
    let _e115 = _e97.xyz;
    switch bitcast<i32>(0u) {
        default: {
            let _e119 = FragmentUniformBlock.DitherSettings[0i];
            if (_e119 <= 0f) {
                phi_636_ = _e115;
                break;
            }
            let _e125 = max(max(_e97.x, _e97.y), _e97.z);
            if (_e125 <= 0.0001f) {
                phi_636_ = _e115;
                break;
            }
            let _e130 = floor(((_e98.y * 4f) + 0.5f));
            let _e134 = floor(((_e98.x * 255f) + 0.5f));
            let _e137 = FragmentUniformBlock.DitherSettings[3i];
            let _e141 = ((_e137 * _e137) * (3f - (2f * _e137)));
            let _e145 = FragmentUniformBlock.DitherGlitch[2i];
            let _e147 = mix(24f, (_e145 * 1.45f), _e141);
            let _e149 = FragmentUniformBlock.DitherGlitch;
            let _e151 = FragmentUniformBlock.DitherGlitchAxis;
            let _e153 = normalize(_e151.xy);
            let _e158 = (_e114 - _e149.xy);
            let _e161 = (dot(_e158, _e153) / max(_e147, 1f));
            let _e164 = (dot(_e158, vec2<f32>(-(_e153.y), _e153.x)) / max((_e147 * 0.38f), 1f));
            let _e175 = (((_e141 * smoothstep(1f, 0.25f, sqrt(((_e161 * _e161) + (_e164 * _e164))))) * 0.82f) + ((_e141 * smoothstep(0.6f, 1f, _e141)) * 0.18f));
            let _e178 = FragmentUniformBlock.DitherGlitch[3i];
            let _e180 = floor((_e178 * 9f));
            let _e182 = floor((_e114 * vec2<f32>(0.0625f, 0.0625f)));
            let _e204 = fract((sin(((((_e134 * 19.19f) + (_e182.x * 7.13f)) + (_e182.y * 3.71f)) + (_e180 * 5.17f))) * 43758.547f));
            let _e205 = (fract((sin(((((_e182.x * 17f) + (_e182.y * 31f)) + (_e180 * 13f)) + (_e134 * 0.23f))) * 43758.547f)) - 0.5f);
            let _e211 = (_e130 == 2f);
            if _e211 {
                phi_257_ = 2f;
            } else {
                phi_257_ = select(1f, 3f, (_e130 == 3f));
            }
            let _e215 = phi_257_;
            let _e216 = (_e134 % 3f);
            let _e221 = FragmentUniformBlock.DitherSettings[1i];
            let _e227 = floor(((_e114 + ((vec2<f32>(_e205, (_e204 - 0.5f)) * _e175) * 9f)) / vec2(max(((_e221 * _e215) * (1f + (_e216 * 0.08f))), 1f))));
            let _e228 = (_e130 == 1f);
            if _e228 {
                phi_295_ = vec2<f32>(-(_e227.x), _e227.y);
            } else {
                if _e211 {
                    phi_294_ = _e227.yx;
                } else {
                    if (_e130 == 3f) {
                        phi_293_ = vec2<f32>(_e227.x, -(_e227.y));
                    } else {
                        phi_292_ = _e227;
                        if (_e130 >= 4f) {
                            phi_292_ = -(_e227.yx);
                        }
                        let _e245 = phi_292_;
                        phi_293_ = _e245;
                    }
                    let _e247 = phi_293_;
                    phi_294_ = _e247;
                }
                let _e249 = phi_294_;
                phi_295_ = _e249;
            }
            let _e251 = phi_295_;
            let _e264 = (_e251 + (vec2<f32>(((_e130 * 2f) % 4f), ((_e130 * 3f) % 4f)) + (vec2<f32>((_e134 % 4f), (floor((_e134 * 0.25f)) % 4f)) * 0.35f)));
            if (_e175 > 0.65f) {
                phi_363_ = floor((_e204 * 11f));
            } else {
                switch bitcast<i32>(0u) {
                    default: {
                        if _e228 {
                            if (_e216 == 0f) {
                                phi_325_ = 8f;
                            } else {
                                phi_325_ = select(5f, 6f, (_e216 == 1f));
                            }
                            let _e273 = phi_325_;
                            phi_362_ = _e273;
                            break;
                        }
                        if _e211 {
                            if (_e216 == 0f) {
                                phi_334_ = 2f;
                            } else {
                                phi_334_ = select(10f, 9f, (_e216 == 1f));
                            }
                            let _e278 = phi_334_;
                            phi_362_ = _e278;
                            break;
                        }
                        if (_e130 == 3f) {
                            if (_e216 == 0f) {
                                phi_344_ = 3f;
                            } else {
                                phi_344_ = select(1f, 5f, (_e216 == 1f));
                            }
                            let _e284 = phi_344_;
                            phi_362_ = _e284;
                            break;
                        }
                        if (_e130 >= 4f) {
                            if (_e216 == 0f) {
                                phi_354_ = 4f;
                            } else {
                                phi_354_ = select(10f, 7f, (_e216 == 1f));
                            }
                            let _e290 = phi_354_;
                            phi_362_ = _e290;
                            break;
                        }
                        if (_e216 == 0f) {
                            phi_361_ = 0f;
                        } else {
                            phi_361_ = select(7f, 4f, (_e216 == 1f));
                        }
                        let _e295 = phi_361_;
                        phi_362_ = _e295;
                        break;
                    }
                }
                let _e297 = phi_362_;
                phi_363_ = _e297;
            }
            let _e299 = phi_363_;
            switch bitcast<i32>(0u) {
                default: {
                    if (_e299 == 1f) {
                        phi_615_ = ((((_e264.x + (_e264.y * 2f)) % 6f) + 1f) * 0.14285715f);
                        break;
                    }
                    if (_e299 == 2f) {
                        let _e311 = ((_e264 % vec2<f32>(4f, 4f)) - vec2<f32>(1.5f, 1.5f));
                        phi_615_ = clamp(((dot(_e311, _e311) + 0.5f) * 0.2f), 0f, 1f);
                        break;
                    }
                    if (_e299 == 3f) {
                        phi_615_ = (((_e264.x % 4f) + 1f) * 0.2f);
                        break;
                    }
                    if (_e299 == 4f) {
                        phi_615_ = fract((sin(dot(floor(_e264), vec2<f32>(127.1f, 311.7f))) * 43758.547f));
                        break;
                    }
                    if (_e299 == 5f) {
                        phi_615_ = (((_e264.y % 4f) + 1f) * 0.2f);
                        break;
                    }
                    if (_e299 == 6f) {
                        phi_615_ = ((min(((_e264.x + _e264.y) % 4f), (((_e264.x - _e264.y) + 8f) % 4f)) + 1f) * 0.2f);
                        break;
                    }
                    if (_e299 == 7f) {
                        let _e344 = (_e264 % vec2<f32>(4f, 4f));
                        let _e353 = i32((_e264.x % 4f));
                        let _e356 = i32((_e264.y % 4f));
                        if (_e353 == 0i) {
                            if (_e356 == 0i) {
                                phi_451_ = 16f;
                            } else {
                                if (_e356 == 1i) {
                                    phi_450_ = 5f;
                                } else {
                                    phi_450_ = select(1f, 13f, (_e356 == 2i));
                                }
                                let _e363 = phi_450_;
                                phi_451_ = _e363;
                            }
                            let _e365 = phi_451_;
                            phi_498_ = _e365;
                        } else {
                            if (_e353 == 1i) {
                                if (_e356 == 0i) {
                                    phi_467_ = 8f;
                                } else {
                                    if (_e356 == 1i) {
                                        phi_466_ = 12f;
                                    } else {
                                        phi_466_ = select(9f, 4f, (_e356 == 2i));
                                    }
                                    let _e372 = phi_466_;
                                    phi_467_ = _e372;
                                }
                                let _e374 = phi_467_;
                                phi_497_ = _e374;
                            } else {
                                if (_e353 == 2i) {
                                    if (_e356 == 0i) {
                                        phi_483_ = 14f;
                                    } else {
                                        if (_e356 == 1i) {
                                            phi_482_ = 2f;
                                        } else {
                                            phi_482_ = select(3f, 15f, (_e356 == 2i));
                                        }
                                        let _e381 = phi_482_;
                                        phi_483_ = _e381;
                                    }
                                    let _e383 = phi_483_;
                                    phi_496_ = _e383;
                                } else {
                                    if (_e356 == 0i) {
                                        phi_495_ = 6f;
                                    } else {
                                        if (_e356 == 1i) {
                                            phi_494_ = 10f;
                                        } else {
                                            phi_494_ = select(11f, 7f, (_e356 == 2i));
                                        }
                                        let _e389 = phi_494_;
                                        phi_495_ = _e389;
                                    }
                                    let _e391 = phi_495_;
                                    phi_496_ = _e391;
                                }
                                let _e393 = phi_496_;
                                phi_497_ = _e393;
                            }
                            let _e395 = phi_497_;
                            phi_498_ = _e395;
                        }
                        let _e397 = phi_498_;
                        if (((floor(_e344.x) + floor(_e344.y)) % 2f) == 0f) {
                            phi_506_ = (_e397 * 0.04411765f);
                        } else {
                            phi_506_ = (0.35f + (_e397 * 0.03823529f));
                        }
                        let _e403 = phi_506_;
                        phi_615_ = _e403;
                        break;
                    }
                    if (_e299 == 8f) {
                        phi_615_ = clamp(((min(((_e264.x + ((floor((_e264.y * 0.5f)) % 2f) * 2f)) % 4f), (_e264.y % 4f)) + 1f) * 0.2f), 0f, 1f);
                        break;
                    }
                    if (_e299 == 9f) {
                        phi_615_ = (((((_e264.x * 2f) + _e264.y) % 8f) + 1f) * 0.11111111f);
                        break;
                    }
                    if (_e299 >= 10f) {
                        let _e429 = abs(((_e264 % vec2<f32>(8f, 8f)) - vec2<f32>(3.5f, 3.5f)));
                        phi_615_ = clamp(((max(_e429.x, _e429.y) - 0.5f) * 0.2857143f), 0f, 1f);
                        break;
                    }
                    let _e438 = i32((_e264.x % 4f));
                    let _e441 = i32((_e264.y % 4f));
                    if (_e438 == 0i) {
                        if (_e441 == 0i) {
                            phi_566_ = 16f;
                        } else {
                            if (_e441 == 1i) {
                                phi_565_ = 5f;
                            } else {
                                phi_565_ = select(1f, 13f, (_e441 == 2i));
                            }
                            let _e448 = phi_565_;
                            phi_566_ = _e448;
                        }
                        let _e450 = phi_566_;
                        phi_613_ = _e450;
                    } else {
                        if (_e438 == 1i) {
                            if (_e441 == 0i) {
                                phi_582_ = 8f;
                            } else {
                                if (_e441 == 1i) {
                                    phi_581_ = 12f;
                                } else {
                                    phi_581_ = select(9f, 4f, (_e441 == 2i));
                                }
                                let _e457 = phi_581_;
                                phi_582_ = _e457;
                            }
                            let _e459 = phi_582_;
                            phi_612_ = _e459;
                        } else {
                            if (_e438 == 2i) {
                                if (_e441 == 0i) {
                                    phi_598_ = 14f;
                                } else {
                                    if (_e441 == 1i) {
                                        phi_597_ = 2f;
                                    } else {
                                        phi_597_ = select(3f, 15f, (_e441 == 2i));
                                    }
                                    let _e466 = phi_597_;
                                    phi_598_ = _e466;
                                }
                                let _e468 = phi_598_;
                                phi_611_ = _e468;
                            } else {
                                if (_e441 == 0i) {
                                    phi_610_ = 6f;
                                } else {
                                    if (_e441 == 1i) {
                                        phi_609_ = 10f;
                                    } else {
                                        phi_609_ = select(11f, 7f, (_e441 == 2i));
                                    }
                                    let _e474 = phi_609_;
                                    phi_610_ = _e474;
                                }
                                let _e476 = phi_610_;
                                phi_611_ = _e476;
                            }
                            let _e478 = phi_611_;
                            phi_612_ = _e478;
                        }
                        let _e480 = phi_612_;
                        phi_613_ = _e480;
                    }
                    let _e482 = phi_613_;
                    phi_615_ = (_e482 * 0.05882353f);
                    break;
                }
            }
            let _e485 = phi_615_;
            let _e502 = FragmentUniformBlock.DitherSettings[2i];
            phi_636_ = (_e115 * mix(1f, (select(1f, 0f, (clamp(_e125, 0f, 1f) < clamp((clamp((_e485 + ((fract((_e134 * 0.618034f)) - 0.5f) * 0.1f)), 0f, 1f) + ((_e205 * _e175) * 0.18f)), 0f, 1f))) / _e125), clamp((_e502 * _e99.x), 0f, 1f)));
            break;
        }
    }
    let _e508 = phi_636_;
    let _e521 = _e508.xyz;
    let _e529 = clamp(mix(((_e96.xyz * _e521) + (_e521 * ((_e97.w * clamp((1f - (max(max(_e96.x, _e96.y), _e96.z) * 2.2f)), 0f, 1f)) * 0.18f))), _e101.xyz, vec3(_e101.w)), vec3<f32>(0f, 0f, 0f), vec3<f32>(1f, 1f, 1f));
    out_u002e_var_u002e_SV_Target0_ = vec4<f32>(_e529.x, _e529.y, _e529.z, _e96.w);
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
