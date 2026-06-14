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
    var phi_250_: f32;
    var phi_285_: vec2<f32>;
    var phi_286_: vec2<f32>;
    var phi_287_: vec2<f32>;
    var phi_288_: vec2<f32>;
    var phi_318_: f32;
    var phi_327_: f32;
    var phi_337_: f32;
    var phi_347_: f32;
    var phi_354_: f32;
    var phi_355_: f32;
    var phi_356_: f32;
    var phi_443_: f32;
    var phi_444_: f32;
    var phi_459_: f32;
    var phi_460_: f32;
    var phi_475_: f32;
    var phi_476_: f32;
    var phi_487_: f32;
    var phi_488_: f32;
    var phi_489_: f32;
    var phi_490_: f32;
    var phi_491_: f32;
    var phi_499_: f32;
    var phi_558_: f32;
    var phi_559_: f32;
    var phi_574_: f32;
    var phi_575_: f32;
    var phi_590_: f32;
    var phi_591_: f32;
    var phi_602_: f32;
    var phi_603_: f32;
    var phi_604_: f32;
    var phi_605_: f32;
    var phi_606_: f32;
    var phi_608_: f32;
    var phi_629_: vec3<f32>;

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
    let _e100 = FragmentUniformBlock.DitherGlitchAxis[2i];
    let _e104 = FragmentUniformBlock.TextureSizeCamera;
    let _e109 = (_e104.zw + (_e84.xy / vec2(select(1f, _e100, (_e100 > 0f)))));
    let _e110 = _e92.xyz;
    switch bitcast<i32>(0u) {
        default: {
            let _e114 = FragmentUniformBlock.DitherSettings[0i];
            if (_e114 <= 0f) {
                phi_629_ = _e110;
                break;
            }
            let _e120 = max(max(_e92.x, _e92.y), _e92.z);
            if (_e120 <= 0.0001f) {
                phi_629_ = _e110;
                break;
            }
            let _e125 = floor(((_e93.y * 4f) + 0.5f));
            let _e129 = floor(((_e93.x * 255f) + 0.5f));
            let _e132 = FragmentUniformBlock.DitherSettings[3i];
            let _e136 = ((_e132 * _e132) * (3f - (2f * _e132)));
            let _e140 = FragmentUniformBlock.DitherGlitch[2i];
            let _e142 = mix(24f, (_e140 * 1.45f), _e136);
            let _e144 = FragmentUniformBlock.DitherGlitch;
            let _e146 = FragmentUniformBlock.DitherGlitchAxis;
            let _e148 = normalize(_e146.xy);
            let _e153 = (_e109 - _e144.xy);
            let _e156 = (dot(_e153, _e148) / max(_e142, 1f));
            let _e159 = (dot(_e153, vec2<f32>(-(_e148.y), _e148.x)) / max((_e142 * 0.38f), 1f));
            let _e170 = (((_e136 * smoothstep(1f, 0.25f, sqrt(((_e156 * _e156) + (_e159 * _e159))))) * 0.82f) + ((_e136 * smoothstep(0.6f, 1f, _e136)) * 0.18f));
            let _e173 = FragmentUniformBlock.DitherGlitch[3i];
            let _e175 = floor((_e173 * 9f));
            let _e177 = floor((_e109 * vec2<f32>(0.0625f, 0.0625f)));
            let _e199 = fract((sin(((((_e129 * 19.19f) + (_e177.x * 7.13f)) + (_e177.y * 3.71f)) + (_e175 * 5.17f))) * 43758.547f));
            let _e200 = (fract((sin(((((_e177.x * 17f) + (_e177.y * 31f)) + (_e175 * 13f)) + (_e129 * 0.23f))) * 43758.547f)) - 0.5f);
            let _e206 = (_e125 == 2f);
            if _e206 {
                phi_250_ = 2f;
            } else {
                phi_250_ = select(1f, 3f, (_e125 == 3f));
            }
            let _e210 = phi_250_;
            let _e211 = (_e129 % 3f);
            let _e216 = FragmentUniformBlock.DitherSettings[1i];
            let _e222 = floor(((_e109 + ((vec2<f32>(_e200, (_e199 - 0.5f)) * _e170) * 9f)) / vec2(max(((_e216 * _e210) * (1f + (_e211 * 0.08f))), 1f))));
            let _e223 = (_e125 == 1f);
            if _e223 {
                phi_288_ = vec2<f32>(-(_e222.x), _e222.y);
            } else {
                if _e206 {
                    phi_287_ = _e222.yx;
                } else {
                    if (_e125 == 3f) {
                        phi_286_ = vec2<f32>(_e222.x, -(_e222.y));
                    } else {
                        phi_285_ = _e222;
                        if (_e125 >= 4f) {
                            phi_285_ = -(_e222.yx);
                        }
                        let _e240 = phi_285_;
                        phi_286_ = _e240;
                    }
                    let _e242 = phi_286_;
                    phi_287_ = _e242;
                }
                let _e244 = phi_287_;
                phi_288_ = _e244;
            }
            let _e246 = phi_288_;
            let _e259 = (_e246 + (vec2<f32>(((_e125 * 2f) % 4f), ((_e125 * 3f) % 4f)) + (vec2<f32>((_e129 % 4f), (floor((_e129 * 0.25f)) % 4f)) * 0.35f)));
            if (_e170 > 0.65f) {
                phi_356_ = floor((_e199 * 11f));
            } else {
                switch bitcast<i32>(0u) {
                    default: {
                        if _e223 {
                            if (_e211 == 0f) {
                                phi_318_ = 8f;
                            } else {
                                phi_318_ = select(5f, 6f, (_e211 == 1f));
                            }
                            let _e268 = phi_318_;
                            phi_355_ = _e268;
                            break;
                        }
                        if _e206 {
                            if (_e211 == 0f) {
                                phi_327_ = 2f;
                            } else {
                                phi_327_ = select(10f, 9f, (_e211 == 1f));
                            }
                            let _e273 = phi_327_;
                            phi_355_ = _e273;
                            break;
                        }
                        if (_e125 == 3f) {
                            if (_e211 == 0f) {
                                phi_337_ = 3f;
                            } else {
                                phi_337_ = select(1f, 5f, (_e211 == 1f));
                            }
                            let _e279 = phi_337_;
                            phi_355_ = _e279;
                            break;
                        }
                        if (_e125 >= 4f) {
                            if (_e211 == 0f) {
                                phi_347_ = 4f;
                            } else {
                                phi_347_ = select(10f, 7f, (_e211 == 1f));
                            }
                            let _e285 = phi_347_;
                            phi_355_ = _e285;
                            break;
                        }
                        if (_e211 == 0f) {
                            phi_354_ = 0f;
                        } else {
                            phi_354_ = select(7f, 4f, (_e211 == 1f));
                        }
                        let _e290 = phi_354_;
                        phi_355_ = _e290;
                        break;
                    }
                }
                let _e292 = phi_355_;
                phi_356_ = _e292;
            }
            let _e294 = phi_356_;
            switch bitcast<i32>(0u) {
                default: {
                    if (_e294 == 1f) {
                        phi_608_ = ((((_e259.x + (_e259.y * 2f)) % 6f) + 1f) * 0.14285715f);
                        break;
                    }
                    if (_e294 == 2f) {
                        let _e306 = ((_e259 % vec2<f32>(4f, 4f)) - vec2<f32>(1.5f, 1.5f));
                        phi_608_ = clamp(((dot(_e306, _e306) + 0.5f) * 0.2f), 0f, 1f);
                        break;
                    }
                    if (_e294 == 3f) {
                        phi_608_ = (((_e259.x % 4f) + 1f) * 0.2f);
                        break;
                    }
                    if (_e294 == 4f) {
                        phi_608_ = fract((sin(dot(floor(_e259), vec2<f32>(127.1f, 311.7f))) * 43758.547f));
                        break;
                    }
                    if (_e294 == 5f) {
                        phi_608_ = (((_e259.y % 4f) + 1f) * 0.2f);
                        break;
                    }
                    if (_e294 == 6f) {
                        phi_608_ = ((min(((_e259.x + _e259.y) % 4f), (((_e259.x - _e259.y) + 8f) % 4f)) + 1f) * 0.2f);
                        break;
                    }
                    if (_e294 == 7f) {
                        let _e339 = (_e259 % vec2<f32>(4f, 4f));
                        let _e348 = i32((_e259.x % 4f));
                        let _e351 = i32((_e259.y % 4f));
                        if (_e348 == 0i) {
                            if (_e351 == 0i) {
                                phi_444_ = 16f;
                            } else {
                                if (_e351 == 1i) {
                                    phi_443_ = 5f;
                                } else {
                                    phi_443_ = select(1f, 13f, (_e351 == 2i));
                                }
                                let _e358 = phi_443_;
                                phi_444_ = _e358;
                            }
                            let _e360 = phi_444_;
                            phi_491_ = _e360;
                        } else {
                            if (_e348 == 1i) {
                                if (_e351 == 0i) {
                                    phi_460_ = 8f;
                                } else {
                                    if (_e351 == 1i) {
                                        phi_459_ = 12f;
                                    } else {
                                        phi_459_ = select(9f, 4f, (_e351 == 2i));
                                    }
                                    let _e367 = phi_459_;
                                    phi_460_ = _e367;
                                }
                                let _e369 = phi_460_;
                                phi_490_ = _e369;
                            } else {
                                if (_e348 == 2i) {
                                    if (_e351 == 0i) {
                                        phi_476_ = 14f;
                                    } else {
                                        if (_e351 == 1i) {
                                            phi_475_ = 2f;
                                        } else {
                                            phi_475_ = select(3f, 15f, (_e351 == 2i));
                                        }
                                        let _e376 = phi_475_;
                                        phi_476_ = _e376;
                                    }
                                    let _e378 = phi_476_;
                                    phi_489_ = _e378;
                                } else {
                                    if (_e351 == 0i) {
                                        phi_488_ = 6f;
                                    } else {
                                        if (_e351 == 1i) {
                                            phi_487_ = 10f;
                                        } else {
                                            phi_487_ = select(11f, 7f, (_e351 == 2i));
                                        }
                                        let _e384 = phi_487_;
                                        phi_488_ = _e384;
                                    }
                                    let _e386 = phi_488_;
                                    phi_489_ = _e386;
                                }
                                let _e388 = phi_489_;
                                phi_490_ = _e388;
                            }
                            let _e390 = phi_490_;
                            phi_491_ = _e390;
                        }
                        let _e392 = phi_491_;
                        if (((floor(_e339.x) + floor(_e339.y)) % 2f) == 0f) {
                            phi_499_ = (_e392 * 0.04411765f);
                        } else {
                            phi_499_ = (0.35f + (_e392 * 0.03823529f));
                        }
                        let _e398 = phi_499_;
                        phi_608_ = _e398;
                        break;
                    }
                    if (_e294 == 8f) {
                        phi_608_ = clamp(((min(((_e259.x + ((floor((_e259.y * 0.5f)) % 2f) * 2f)) % 4f), (_e259.y % 4f)) + 1f) * 0.2f), 0f, 1f);
                        break;
                    }
                    if (_e294 == 9f) {
                        phi_608_ = (((((_e259.x * 2f) + _e259.y) % 8f) + 1f) * 0.11111111f);
                        break;
                    }
                    if (_e294 >= 10f) {
                        let _e424 = abs(((_e259 % vec2<f32>(8f, 8f)) - vec2<f32>(3.5f, 3.5f)));
                        phi_608_ = clamp(((max(_e424.x, _e424.y) - 0.5f) * 0.2857143f), 0f, 1f);
                        break;
                    }
                    let _e433 = i32((_e259.x % 4f));
                    let _e436 = i32((_e259.y % 4f));
                    if (_e433 == 0i) {
                        if (_e436 == 0i) {
                            phi_559_ = 16f;
                        } else {
                            if (_e436 == 1i) {
                                phi_558_ = 5f;
                            } else {
                                phi_558_ = select(1f, 13f, (_e436 == 2i));
                            }
                            let _e443 = phi_558_;
                            phi_559_ = _e443;
                        }
                        let _e445 = phi_559_;
                        phi_606_ = _e445;
                    } else {
                        if (_e433 == 1i) {
                            if (_e436 == 0i) {
                                phi_575_ = 8f;
                            } else {
                                if (_e436 == 1i) {
                                    phi_574_ = 12f;
                                } else {
                                    phi_574_ = select(9f, 4f, (_e436 == 2i));
                                }
                                let _e452 = phi_574_;
                                phi_575_ = _e452;
                            }
                            let _e454 = phi_575_;
                            phi_605_ = _e454;
                        } else {
                            if (_e433 == 2i) {
                                if (_e436 == 0i) {
                                    phi_591_ = 14f;
                                } else {
                                    if (_e436 == 1i) {
                                        phi_590_ = 2f;
                                    } else {
                                        phi_590_ = select(3f, 15f, (_e436 == 2i));
                                    }
                                    let _e461 = phi_590_;
                                    phi_591_ = _e461;
                                }
                                let _e463 = phi_591_;
                                phi_604_ = _e463;
                            } else {
                                if (_e436 == 0i) {
                                    phi_603_ = 6f;
                                } else {
                                    if (_e436 == 1i) {
                                        phi_602_ = 10f;
                                    } else {
                                        phi_602_ = select(11f, 7f, (_e436 == 2i));
                                    }
                                    let _e469 = phi_602_;
                                    phi_603_ = _e469;
                                }
                                let _e471 = phi_603_;
                                phi_604_ = _e471;
                            }
                            let _e473 = phi_604_;
                            phi_605_ = _e473;
                        }
                        let _e475 = phi_605_;
                        phi_606_ = _e475;
                    }
                    let _e477 = phi_606_;
                    phi_608_ = (_e477 * 0.05882353f);
                    break;
                }
            }
            let _e480 = phi_608_;
            let _e497 = FragmentUniformBlock.DitherSettings[2i];
            phi_629_ = (_e110 * mix(1f, (select(1f, 0f, (clamp(_e120, 0f, 1f) < clamp((clamp((_e480 + ((fract((_e129 * 0.618034f)) - 0.5f) * 0.1f)), 0f, 1f) + ((_e200 * _e170) * 0.18f)), 0f, 1f))) / _e120), clamp((_e497 * _e94.x), 0f, 1f)));
            break;
        }
    }
    let _e503 = phi_629_;
    let _e516 = _e503.xyz;
    let _e524 = clamp(mix(((_e91.xyz * _e516) + (_e516 * ((_e92.w * clamp((1f - (max(max(_e91.x, _e91.y), _e91.z) * 2.2f)), 0f, 1f)) * 0.18f))), _e96.xyz, vec3(_e96.w)), vec3<f32>(0f, 0f, 0f), vec3<f32>(1f, 1f, 1f));
    out_u002e_var_u002e_SV_Target0_ = vec4<f32>(_e524.x, _e524.y, _e524.z, _e91.w);
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
