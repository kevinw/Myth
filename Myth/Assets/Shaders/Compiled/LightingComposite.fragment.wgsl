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
@group(2) @binding(7)
var ForegroundTexture: texture_2d<f32>;
@group(2) @binding(107)
var ForegroundSampler: sampler;
@group(2) @binding(8)
var ForegroundLightTexture: texture_2d<f32>;
@group(2) @binding(108)
var ForegroundLightSampler: sampler;
@group(2) @binding(9)
var ForegroundEmissiveTexture: texture_2d<f32>;
@group(2) @binding(109)
var ForegroundEmissiveSampler: sampler;
var<private> in_u002e_var_u002e_TEXCOORD0_1: vec2<f32>;
var<private> in_u002e_var_u002e_TEXCOORD1_1: vec4<f32>;
var<private> in_u002e_var_u002e_TEXCOORD4_1: vec4<f32>;
var<private> global: vec4<f32>;
var<private> out_u002e_var_u002e_SV_Target0_: vec4<f32>;

fn fragment_main_1() {
    var phi_197_: vec4<f32>;
    var phi_301_: f32;
    var phi_336_: vec2<f32>;
    var phi_337_: vec2<f32>;
    var phi_338_: vec2<f32>;
    var phi_339_: vec2<f32>;
    var phi_369_: f32;
    var phi_378_: f32;
    var phi_388_: f32;
    var phi_398_: f32;
    var phi_405_: f32;
    var phi_406_: f32;
    var phi_407_: f32;
    var phi_494_: f32;
    var phi_495_: f32;
    var phi_510_: f32;
    var phi_511_: f32;
    var phi_526_: f32;
    var phi_527_: f32;
    var phi_538_: f32;
    var phi_539_: f32;
    var phi_540_: f32;
    var phi_541_: f32;
    var phi_542_: f32;
    var phi_550_: f32;
    var phi_609_: f32;
    var phi_610_: f32;
    var phi_625_: f32;
    var phi_626_: f32;
    var phi_641_: f32;
    var phi_642_: f32;
    var phi_653_: f32;
    var phi_654_: f32;
    var phi_655_: f32;
    var phi_656_: f32;
    var phi_657_: f32;
    var phi_659_: f32;
    var phi_680_: vec3<f32>;
    var local: bool;
    var phi_820_: f32;
    var phi_855_: vec2<f32>;
    var phi_856_: vec2<f32>;
    var phi_857_: vec2<f32>;
    var phi_858_: vec2<f32>;
    var phi_888_: f32;
    var phi_897_: f32;
    var phi_907_: f32;
    var phi_917_: f32;
    var phi_924_: f32;
    var phi_925_: f32;
    var phi_926_: f32;
    var phi_1013_: f32;
    var phi_1014_: f32;
    var phi_1029_: f32;
    var phi_1030_: f32;
    var phi_1045_: f32;
    var phi_1046_: f32;
    var phi_1057_: f32;
    var phi_1058_: f32;
    var phi_1059_: f32;
    var phi_1060_: f32;
    var phi_1061_: f32;
    var phi_1069_: f32;
    var phi_1128_: f32;
    var phi_1129_: f32;
    var phi_1144_: f32;
    var phi_1145_: f32;
    var phi_1160_: f32;
    var phi_1161_: f32;
    var phi_1172_: f32;
    var phi_1173_: f32;
    var phi_1174_: f32;
    var phi_1175_: f32;
    var phi_1176_: f32;
    var phi_1178_: f32;
    var phi_1199_: vec3<f32>;

    let _e97 = in_u002e_var_u002e_TEXCOORD0_1;
    let _e98 = in_u002e_var_u002e_TEXCOORD1_1;
    let _e99 = in_u002e_var_u002e_TEXCOORD4_1;
    let _e100 = global;
    if (_e99.x < -1000f) {
        discard;
    }
    let _e107 = vec2<f32>(_e97.x, (1f - _e97.y));
    let _e108 = textureSample(Texture, Sampler, _e107);
    let _e109 = (_e108 * _e98);
    let _e110 = textureSample(LightTexture, LightSampler, _e107);
    let _e111 = textureSample(TileFieldTexture, TileFieldSampler, _e107);
    let _e112 = textureSample(DitherMaskTexture, DitherMaskSampler, _e107);
    let _e114 = textureSample(EmissiveTexture, EmissiveSampler, _e107);
    let _e118 = FragmentUniformBlock.DitherGlitchAxis[2i];
    let _e122 = FragmentUniformBlock.TextureSizeCamera;
    let _e127 = (_e122.zw + (_e100.xy / vec2(select(1f, _e118, (_e118 > 0f)))));
    let _e131 = FragmentUniformBlock.AoSettings[0i];
    phi_197_ = _e110;
    if (_e131 > 0f) {
        let _e133 = FragmentUniformBlock.AoSettings;
        let _e138 = textureSample(AoTexture, AoSampler, (_e107 + (_e133.yz / _e122.xy)));
        let _e142 = textureSample(BackgroundCoverageTexture, BackgroundCoverageSampler, _e107);
        let _e146 = FragmentUniformBlock.AoSettings[3i];
        let _e150 = (_e110.xyz * mix(1f, _e146, (clamp((_e138.x * _e131), 0f, 1f) * _e142.x)));
        phi_197_ = vec4<f32>(_e150.x, _e150.y, _e150.z, _e110.w);
    }
    let _e157 = phi_197_;
    let _e158 = _e157.xyz;
    switch bitcast<i32>(0u) {
        default: {
            let _e162 = FragmentUniformBlock.DitherSettings[0i];
            let _e163 = (_e162 <= 0f);
            local = _e163;
            if _e163 {
                phi_680_ = _e158;
                break;
            }
            let _e168 = max(max(_e157.x, _e157.y), _e157.z);
            if (_e168 <= 0.0001f) {
                phi_680_ = _e158;
                break;
            }
            let _e173 = floor(((_e111.y * 4f) + 0.5f));
            let _e177 = floor(((_e111.x * 255f) + 0.5f));
            let _e180 = FragmentUniformBlock.DitherSettings[3i];
            let _e184 = ((_e180 * _e180) * (3f - (2f * _e180)));
            let _e188 = FragmentUniformBlock.DitherGlitch[2i];
            let _e190 = mix(24f, (_e188 * 1.45f), _e184);
            let _e192 = FragmentUniformBlock.DitherGlitch;
            let _e194 = FragmentUniformBlock.DitherGlitchAxis;
            let _e196 = normalize(_e194.xy);
            let _e201 = (_e127 - _e192.xy);
            let _e204 = (dot(_e201, _e196) / max(_e190, 1f));
            let _e207 = (dot(_e201, vec2<f32>(-(_e196.y), _e196.x)) / max((_e190 * 0.38f), 1f));
            let _e218 = (((_e184 * smoothstep(1f, 0.25f, sqrt(((_e204 * _e204) + (_e207 * _e207))))) * 0.82f) + ((_e184 * smoothstep(0.6f, 1f, _e184)) * 0.18f));
            let _e221 = FragmentUniformBlock.DitherGlitch[3i];
            let _e223 = floor((_e221 * 9f));
            let _e225 = floor((_e127 * vec2<f32>(0.0625f, 0.0625f)));
            let _e247 = fract((sin(((((_e177 * 19.19f) + (_e225.x * 7.13f)) + (_e225.y * 3.71f)) + (_e223 * 5.17f))) * 43758.547f));
            let _e248 = (fract((sin(((((_e225.x * 17f) + (_e225.y * 31f)) + (_e223 * 13f)) + (_e177 * 0.23f))) * 43758.547f)) - 0.5f);
            let _e254 = (_e173 == 2f);
            if _e254 {
                phi_301_ = 2f;
            } else {
                phi_301_ = select(1f, 3f, (_e173 == 3f));
            }
            let _e258 = phi_301_;
            let _e259 = (_e177 % 3f);
            let _e264 = FragmentUniformBlock.DitherSettings[1i];
            let _e270 = floor(((_e127 + ((vec2<f32>(_e248, (_e247 - 0.5f)) * _e218) * 9f)) / vec2(max(((_e264 * _e258) * (1f + (_e259 * 0.08f))), 1f))));
            let _e271 = (_e173 == 1f);
            if _e271 {
                phi_339_ = vec2<f32>(-(_e270.x), _e270.y);
            } else {
                if _e254 {
                    phi_338_ = _e270.yx;
                } else {
                    if (_e173 == 3f) {
                        phi_337_ = vec2<f32>(_e270.x, -(_e270.y));
                    } else {
                        phi_336_ = _e270;
                        if (_e173 >= 4f) {
                            phi_336_ = -(_e270.yx);
                        }
                        let _e288 = phi_336_;
                        phi_337_ = _e288;
                    }
                    let _e290 = phi_337_;
                    phi_338_ = _e290;
                }
                let _e292 = phi_338_;
                phi_339_ = _e292;
            }
            let _e294 = phi_339_;
            let _e307 = (_e294 + (vec2<f32>(((_e173 * 2f) % 4f), ((_e173 * 3f) % 4f)) + (vec2<f32>((_e177 % 4f), (floor((_e177 * 0.25f)) % 4f)) * 0.35f)));
            if (_e218 > 0.65f) {
                phi_407_ = floor((_e247 * 11f));
            } else {
                switch bitcast<i32>(0u) {
                    default: {
                        if _e271 {
                            if (_e259 == 0f) {
                                phi_369_ = 8f;
                            } else {
                                phi_369_ = select(5f, 6f, (_e259 == 1f));
                            }
                            let _e316 = phi_369_;
                            phi_406_ = _e316;
                            break;
                        }
                        if _e254 {
                            if (_e259 == 0f) {
                                phi_378_ = 2f;
                            } else {
                                phi_378_ = select(10f, 9f, (_e259 == 1f));
                            }
                            let _e321 = phi_378_;
                            phi_406_ = _e321;
                            break;
                        }
                        if (_e173 == 3f) {
                            if (_e259 == 0f) {
                                phi_388_ = 3f;
                            } else {
                                phi_388_ = select(1f, 5f, (_e259 == 1f));
                            }
                            let _e327 = phi_388_;
                            phi_406_ = _e327;
                            break;
                        }
                        if (_e173 >= 4f) {
                            if (_e259 == 0f) {
                                phi_398_ = 4f;
                            } else {
                                phi_398_ = select(10f, 7f, (_e259 == 1f));
                            }
                            let _e333 = phi_398_;
                            phi_406_ = _e333;
                            break;
                        }
                        if (_e259 == 0f) {
                            phi_405_ = 0f;
                        } else {
                            phi_405_ = select(7f, 4f, (_e259 == 1f));
                        }
                        let _e338 = phi_405_;
                        phi_406_ = _e338;
                        break;
                    }
                }
                let _e340 = phi_406_;
                phi_407_ = _e340;
            }
            let _e342 = phi_407_;
            switch bitcast<i32>(0u) {
                default: {
                    if (_e342 == 1f) {
                        phi_659_ = ((((_e307.x + (_e307.y * 2f)) % 6f) + 1f) * 0.14285715f);
                        break;
                    }
                    if (_e342 == 2f) {
                        let _e354 = ((_e307 % vec2<f32>(4f, 4f)) - vec2<f32>(1.5f, 1.5f));
                        phi_659_ = clamp(((dot(_e354, _e354) + 0.5f) * 0.2f), 0f, 1f);
                        break;
                    }
                    if (_e342 == 3f) {
                        phi_659_ = (((_e307.x % 4f) + 1f) * 0.2f);
                        break;
                    }
                    if (_e342 == 4f) {
                        phi_659_ = fract((sin(dot(floor(_e307), vec2<f32>(127.1f, 311.7f))) * 43758.547f));
                        break;
                    }
                    if (_e342 == 5f) {
                        phi_659_ = (((_e307.y % 4f) + 1f) * 0.2f);
                        break;
                    }
                    if (_e342 == 6f) {
                        phi_659_ = ((min(((_e307.x + _e307.y) % 4f), (((_e307.x - _e307.y) + 8f) % 4f)) + 1f) * 0.2f);
                        break;
                    }
                    if (_e342 == 7f) {
                        let _e387 = (_e307 % vec2<f32>(4f, 4f));
                        let _e396 = i32((_e307.x % 4f));
                        let _e399 = i32((_e307.y % 4f));
                        if (_e396 == 0i) {
                            if (_e399 == 0i) {
                                phi_495_ = 16f;
                            } else {
                                if (_e399 == 1i) {
                                    phi_494_ = 5f;
                                } else {
                                    phi_494_ = select(1f, 13f, (_e399 == 2i));
                                }
                                let _e406 = phi_494_;
                                phi_495_ = _e406;
                            }
                            let _e408 = phi_495_;
                            phi_542_ = _e408;
                        } else {
                            if (_e396 == 1i) {
                                if (_e399 == 0i) {
                                    phi_511_ = 8f;
                                } else {
                                    if (_e399 == 1i) {
                                        phi_510_ = 12f;
                                    } else {
                                        phi_510_ = select(9f, 4f, (_e399 == 2i));
                                    }
                                    let _e415 = phi_510_;
                                    phi_511_ = _e415;
                                }
                                let _e417 = phi_511_;
                                phi_541_ = _e417;
                            } else {
                                if (_e396 == 2i) {
                                    if (_e399 == 0i) {
                                        phi_527_ = 14f;
                                    } else {
                                        if (_e399 == 1i) {
                                            phi_526_ = 2f;
                                        } else {
                                            phi_526_ = select(3f, 15f, (_e399 == 2i));
                                        }
                                        let _e424 = phi_526_;
                                        phi_527_ = _e424;
                                    }
                                    let _e426 = phi_527_;
                                    phi_540_ = _e426;
                                } else {
                                    if (_e399 == 0i) {
                                        phi_539_ = 6f;
                                    } else {
                                        if (_e399 == 1i) {
                                            phi_538_ = 10f;
                                        } else {
                                            phi_538_ = select(11f, 7f, (_e399 == 2i));
                                        }
                                        let _e432 = phi_538_;
                                        phi_539_ = _e432;
                                    }
                                    let _e434 = phi_539_;
                                    phi_540_ = _e434;
                                }
                                let _e436 = phi_540_;
                                phi_541_ = _e436;
                            }
                            let _e438 = phi_541_;
                            phi_542_ = _e438;
                        }
                        let _e440 = phi_542_;
                        if (((floor(_e387.x) + floor(_e387.y)) % 2f) == 0f) {
                            phi_550_ = (_e440 * 0.04411765f);
                        } else {
                            phi_550_ = (0.35f + (_e440 * 0.03823529f));
                        }
                        let _e446 = phi_550_;
                        phi_659_ = _e446;
                        break;
                    }
                    if (_e342 == 8f) {
                        phi_659_ = clamp(((min(((_e307.x + ((floor((_e307.y * 0.5f)) % 2f) * 2f)) % 4f), (_e307.y % 4f)) + 1f) * 0.2f), 0f, 1f);
                        break;
                    }
                    if (_e342 == 9f) {
                        phi_659_ = (((((_e307.x * 2f) + _e307.y) % 8f) + 1f) * 0.11111111f);
                        break;
                    }
                    if (_e342 >= 10f) {
                        let _e472 = abs(((_e307 % vec2<f32>(8f, 8f)) - vec2<f32>(3.5f, 3.5f)));
                        phi_659_ = clamp(((max(_e472.x, _e472.y) - 0.5f) * 0.2857143f), 0f, 1f);
                        break;
                    }
                    let _e481 = i32((_e307.x % 4f));
                    let _e484 = i32((_e307.y % 4f));
                    if (_e481 == 0i) {
                        if (_e484 == 0i) {
                            phi_610_ = 16f;
                        } else {
                            if (_e484 == 1i) {
                                phi_609_ = 5f;
                            } else {
                                phi_609_ = select(1f, 13f, (_e484 == 2i));
                            }
                            let _e491 = phi_609_;
                            phi_610_ = _e491;
                        }
                        let _e493 = phi_610_;
                        phi_657_ = _e493;
                    } else {
                        if (_e481 == 1i) {
                            if (_e484 == 0i) {
                                phi_626_ = 8f;
                            } else {
                                if (_e484 == 1i) {
                                    phi_625_ = 12f;
                                } else {
                                    phi_625_ = select(9f, 4f, (_e484 == 2i));
                                }
                                let _e500 = phi_625_;
                                phi_626_ = _e500;
                            }
                            let _e502 = phi_626_;
                            phi_656_ = _e502;
                        } else {
                            if (_e481 == 2i) {
                                if (_e484 == 0i) {
                                    phi_642_ = 14f;
                                } else {
                                    if (_e484 == 1i) {
                                        phi_641_ = 2f;
                                    } else {
                                        phi_641_ = select(3f, 15f, (_e484 == 2i));
                                    }
                                    let _e509 = phi_641_;
                                    phi_642_ = _e509;
                                }
                                let _e511 = phi_642_;
                                phi_655_ = _e511;
                            } else {
                                if (_e484 == 0i) {
                                    phi_654_ = 6f;
                                } else {
                                    if (_e484 == 1i) {
                                        phi_653_ = 10f;
                                    } else {
                                        phi_653_ = select(11f, 7f, (_e484 == 2i));
                                    }
                                    let _e517 = phi_653_;
                                    phi_654_ = _e517;
                                }
                                let _e519 = phi_654_;
                                phi_655_ = _e519;
                            }
                            let _e521 = phi_655_;
                            phi_656_ = _e521;
                        }
                        let _e523 = phi_656_;
                        phi_657_ = _e523;
                    }
                    let _e525 = phi_657_;
                    phi_659_ = (_e525 * 0.05882353f);
                    break;
                }
            }
            let _e528 = phi_659_;
            let _e545 = FragmentUniformBlock.DitherSettings[2i];
            phi_680_ = (_e158 * mix(1f, (select(1f, 0f, (clamp(_e168, 0f, 1f) < clamp((clamp((_e528 + ((fract((_e177 * 0.618034f)) - 0.5f) * 0.1f)), 0f, 1f) + ((_e248 * _e218) * 0.18f)), 0f, 1f))) / _e168), clamp((_e545 * _e112.x), 0f, 1f)));
            break;
        }
    }
    let _e551 = phi_680_;
    let _e564 = _e551.xyz;
    let _e572 = clamp(mix(((_e109.xyz * _e564) + (_e564 * ((_e157.w * clamp((1f - (max(max(_e109.x, _e109.y), _e109.z) * 2.2f)), 0f, 1f)) * 0.18f))), _e114.xyz, vec3(_e114.w)), vec3<f32>(0f, 0f, 0f), vec3<f32>(1f, 1f, 1f));
    let _e578 = textureSample(ForegroundTexture, ForegroundSampler, _e107);
    let _e579 = (_e578 * _e98);
    let _e580 = textureSample(ForegroundLightTexture, ForegroundLightSampler, _e107);
    let _e581 = textureSample(ForegroundEmissiveTexture, ForegroundEmissiveSampler, _e107);
    let _e582 = _e580.xyz;
    switch bitcast<i32>(0u) {
        default: {
            let _e585 = local;
            if _e585 {
                phi_1199_ = _e582;
                break;
            }
            let _e590 = max(max(_e580.x, _e580.y), _e580.z);
            if (_e590 <= 0.0001f) {
                phi_1199_ = _e582;
                break;
            }
            let _e595 = floor(((_e111.y * 4f) + 0.5f));
            let _e599 = floor(((_e111.x * 255f) + 0.5f));
            let _e602 = FragmentUniformBlock.DitherSettings[3i];
            let _e606 = ((_e602 * _e602) * (3f - (2f * _e602)));
            let _e610 = FragmentUniformBlock.DitherGlitch[2i];
            let _e612 = mix(24f, (_e610 * 1.45f), _e606);
            let _e614 = FragmentUniformBlock.DitherGlitch;
            let _e616 = FragmentUniformBlock.DitherGlitchAxis;
            let _e618 = normalize(_e616.xy);
            let _e623 = (_e127 - _e614.xy);
            let _e626 = (dot(_e623, _e618) / max(_e612, 1f));
            let _e629 = (dot(_e623, vec2<f32>(-(_e618.y), _e618.x)) / max((_e612 * 0.38f), 1f));
            let _e640 = (((_e606 * smoothstep(1f, 0.25f, sqrt(((_e626 * _e626) + (_e629 * _e629))))) * 0.82f) + ((_e606 * smoothstep(0.6f, 1f, _e606)) * 0.18f));
            let _e643 = FragmentUniformBlock.DitherGlitch[3i];
            let _e645 = floor((_e643 * 9f));
            let _e647 = floor((_e127 * vec2<f32>(0.0625f, 0.0625f)));
            let _e669 = fract((sin(((((_e599 * 19.19f) + (_e647.x * 7.13f)) + (_e647.y * 3.71f)) + (_e645 * 5.17f))) * 43758.547f));
            let _e670 = (fract((sin(((((_e647.x * 17f) + (_e647.y * 31f)) + (_e645 * 13f)) + (_e599 * 0.23f))) * 43758.547f)) - 0.5f);
            let _e676 = (_e595 == 2f);
            if _e676 {
                phi_820_ = 2f;
            } else {
                phi_820_ = select(1f, 3f, (_e595 == 3f));
            }
            let _e680 = phi_820_;
            let _e681 = (_e599 % 3f);
            let _e686 = FragmentUniformBlock.DitherSettings[1i];
            let _e692 = floor(((_e127 + ((vec2<f32>(_e670, (_e669 - 0.5f)) * _e640) * 9f)) / vec2(max(((_e686 * _e680) * (1f + (_e681 * 0.08f))), 1f))));
            let _e693 = (_e595 == 1f);
            if _e693 {
                phi_858_ = vec2<f32>(-(_e692.x), _e692.y);
            } else {
                if _e676 {
                    phi_857_ = _e692.yx;
                } else {
                    if (_e595 == 3f) {
                        phi_856_ = vec2<f32>(_e692.x, -(_e692.y));
                    } else {
                        phi_855_ = _e692;
                        if (_e595 >= 4f) {
                            phi_855_ = -(_e692.yx);
                        }
                        let _e710 = phi_855_;
                        phi_856_ = _e710;
                    }
                    let _e712 = phi_856_;
                    phi_857_ = _e712;
                }
                let _e714 = phi_857_;
                phi_858_ = _e714;
            }
            let _e716 = phi_858_;
            let _e729 = (_e716 + (vec2<f32>(((_e595 * 2f) % 4f), ((_e595 * 3f) % 4f)) + (vec2<f32>((_e599 % 4f), (floor((_e599 * 0.25f)) % 4f)) * 0.35f)));
            if (_e640 > 0.65f) {
                phi_926_ = floor((_e669 * 11f));
            } else {
                switch bitcast<i32>(0u) {
                    default: {
                        if _e693 {
                            if (_e681 == 0f) {
                                phi_888_ = 8f;
                            } else {
                                phi_888_ = select(5f, 6f, (_e681 == 1f));
                            }
                            let _e738 = phi_888_;
                            phi_925_ = _e738;
                            break;
                        }
                        if _e676 {
                            if (_e681 == 0f) {
                                phi_897_ = 2f;
                            } else {
                                phi_897_ = select(10f, 9f, (_e681 == 1f));
                            }
                            let _e743 = phi_897_;
                            phi_925_ = _e743;
                            break;
                        }
                        if (_e595 == 3f) {
                            if (_e681 == 0f) {
                                phi_907_ = 3f;
                            } else {
                                phi_907_ = select(1f, 5f, (_e681 == 1f));
                            }
                            let _e749 = phi_907_;
                            phi_925_ = _e749;
                            break;
                        }
                        if (_e595 >= 4f) {
                            if (_e681 == 0f) {
                                phi_917_ = 4f;
                            } else {
                                phi_917_ = select(10f, 7f, (_e681 == 1f));
                            }
                            let _e755 = phi_917_;
                            phi_925_ = _e755;
                            break;
                        }
                        if (_e681 == 0f) {
                            phi_924_ = 0f;
                        } else {
                            phi_924_ = select(7f, 4f, (_e681 == 1f));
                        }
                        let _e760 = phi_924_;
                        phi_925_ = _e760;
                        break;
                    }
                }
                let _e762 = phi_925_;
                phi_926_ = _e762;
            }
            let _e764 = phi_926_;
            switch bitcast<i32>(0u) {
                default: {
                    if (_e764 == 1f) {
                        phi_1178_ = ((((_e729.x + (_e729.y * 2f)) % 6f) + 1f) * 0.14285715f);
                        break;
                    }
                    if (_e764 == 2f) {
                        let _e776 = ((_e729 % vec2<f32>(4f, 4f)) - vec2<f32>(1.5f, 1.5f));
                        phi_1178_ = clamp(((dot(_e776, _e776) + 0.5f) * 0.2f), 0f, 1f);
                        break;
                    }
                    if (_e764 == 3f) {
                        phi_1178_ = (((_e729.x % 4f) + 1f) * 0.2f);
                        break;
                    }
                    if (_e764 == 4f) {
                        phi_1178_ = fract((sin(dot(floor(_e729), vec2<f32>(127.1f, 311.7f))) * 43758.547f));
                        break;
                    }
                    if (_e764 == 5f) {
                        phi_1178_ = (((_e729.y % 4f) + 1f) * 0.2f);
                        break;
                    }
                    if (_e764 == 6f) {
                        phi_1178_ = ((min(((_e729.x + _e729.y) % 4f), (((_e729.x - _e729.y) + 8f) % 4f)) + 1f) * 0.2f);
                        break;
                    }
                    if (_e764 == 7f) {
                        let _e809 = (_e729 % vec2<f32>(4f, 4f));
                        let _e818 = i32((_e729.x % 4f));
                        let _e821 = i32((_e729.y % 4f));
                        if (_e818 == 0i) {
                            if (_e821 == 0i) {
                                phi_1014_ = 16f;
                            } else {
                                if (_e821 == 1i) {
                                    phi_1013_ = 5f;
                                } else {
                                    phi_1013_ = select(1f, 13f, (_e821 == 2i));
                                }
                                let _e828 = phi_1013_;
                                phi_1014_ = _e828;
                            }
                            let _e830 = phi_1014_;
                            phi_1061_ = _e830;
                        } else {
                            if (_e818 == 1i) {
                                if (_e821 == 0i) {
                                    phi_1030_ = 8f;
                                } else {
                                    if (_e821 == 1i) {
                                        phi_1029_ = 12f;
                                    } else {
                                        phi_1029_ = select(9f, 4f, (_e821 == 2i));
                                    }
                                    let _e837 = phi_1029_;
                                    phi_1030_ = _e837;
                                }
                                let _e839 = phi_1030_;
                                phi_1060_ = _e839;
                            } else {
                                if (_e818 == 2i) {
                                    if (_e821 == 0i) {
                                        phi_1046_ = 14f;
                                    } else {
                                        if (_e821 == 1i) {
                                            phi_1045_ = 2f;
                                        } else {
                                            phi_1045_ = select(3f, 15f, (_e821 == 2i));
                                        }
                                        let _e846 = phi_1045_;
                                        phi_1046_ = _e846;
                                    }
                                    let _e848 = phi_1046_;
                                    phi_1059_ = _e848;
                                } else {
                                    if (_e821 == 0i) {
                                        phi_1058_ = 6f;
                                    } else {
                                        if (_e821 == 1i) {
                                            phi_1057_ = 10f;
                                        } else {
                                            phi_1057_ = select(11f, 7f, (_e821 == 2i));
                                        }
                                        let _e854 = phi_1057_;
                                        phi_1058_ = _e854;
                                    }
                                    let _e856 = phi_1058_;
                                    phi_1059_ = _e856;
                                }
                                let _e858 = phi_1059_;
                                phi_1060_ = _e858;
                            }
                            let _e860 = phi_1060_;
                            phi_1061_ = _e860;
                        }
                        let _e862 = phi_1061_;
                        if (((floor(_e809.x) + floor(_e809.y)) % 2f) == 0f) {
                            phi_1069_ = (_e862 * 0.04411765f);
                        } else {
                            phi_1069_ = (0.35f + (_e862 * 0.03823529f));
                        }
                        let _e868 = phi_1069_;
                        phi_1178_ = _e868;
                        break;
                    }
                    if (_e764 == 8f) {
                        phi_1178_ = clamp(((min(((_e729.x + ((floor((_e729.y * 0.5f)) % 2f) * 2f)) % 4f), (_e729.y % 4f)) + 1f) * 0.2f), 0f, 1f);
                        break;
                    }
                    if (_e764 == 9f) {
                        phi_1178_ = (((((_e729.x * 2f) + _e729.y) % 8f) + 1f) * 0.11111111f);
                        break;
                    }
                    if (_e764 >= 10f) {
                        let _e894 = abs(((_e729 % vec2<f32>(8f, 8f)) - vec2<f32>(3.5f, 3.5f)));
                        phi_1178_ = clamp(((max(_e894.x, _e894.y) - 0.5f) * 0.2857143f), 0f, 1f);
                        break;
                    }
                    let _e903 = i32((_e729.x % 4f));
                    let _e906 = i32((_e729.y % 4f));
                    if (_e903 == 0i) {
                        if (_e906 == 0i) {
                            phi_1129_ = 16f;
                        } else {
                            if (_e906 == 1i) {
                                phi_1128_ = 5f;
                            } else {
                                phi_1128_ = select(1f, 13f, (_e906 == 2i));
                            }
                            let _e913 = phi_1128_;
                            phi_1129_ = _e913;
                        }
                        let _e915 = phi_1129_;
                        phi_1176_ = _e915;
                    } else {
                        if (_e903 == 1i) {
                            if (_e906 == 0i) {
                                phi_1145_ = 8f;
                            } else {
                                if (_e906 == 1i) {
                                    phi_1144_ = 12f;
                                } else {
                                    phi_1144_ = select(9f, 4f, (_e906 == 2i));
                                }
                                let _e922 = phi_1144_;
                                phi_1145_ = _e922;
                            }
                            let _e924 = phi_1145_;
                            phi_1175_ = _e924;
                        } else {
                            if (_e903 == 2i) {
                                if (_e906 == 0i) {
                                    phi_1161_ = 14f;
                                } else {
                                    if (_e906 == 1i) {
                                        phi_1160_ = 2f;
                                    } else {
                                        phi_1160_ = select(3f, 15f, (_e906 == 2i));
                                    }
                                    let _e931 = phi_1160_;
                                    phi_1161_ = _e931;
                                }
                                let _e933 = phi_1161_;
                                phi_1174_ = _e933;
                            } else {
                                if (_e906 == 0i) {
                                    phi_1173_ = 6f;
                                } else {
                                    if (_e906 == 1i) {
                                        phi_1172_ = 10f;
                                    } else {
                                        phi_1172_ = select(11f, 7f, (_e906 == 2i));
                                    }
                                    let _e939 = phi_1172_;
                                    phi_1173_ = _e939;
                                }
                                let _e941 = phi_1173_;
                                phi_1174_ = _e941;
                            }
                            let _e943 = phi_1174_;
                            phi_1175_ = _e943;
                        }
                        let _e945 = phi_1175_;
                        phi_1176_ = _e945;
                    }
                    let _e947 = phi_1176_;
                    phi_1178_ = (_e947 * 0.05882353f);
                    break;
                }
            }
            let _e950 = phi_1178_;
            let _e967 = FragmentUniformBlock.DitherSettings[2i];
            phi_1199_ = (_e582 * mix(1f, (select(1f, 0f, (clamp(_e590, 0f, 1f) < clamp((clamp((_e950 + ((fract((_e599 * 0.618034f)) - 0.5f) * 0.1f)), 0f, 1f) + ((_e670 * _e640) * 0.18f)), 0f, 1f))) / _e590), clamp((_e967 * _e112.x), 0f, 1f)));
            break;
        }
    }
    let _e973 = phi_1199_;
    let _e986 = _e973.xyz;
    let _e994 = clamp(mix(((_e579.xyz * _e986) + (_e986 * ((_e580.w * clamp((1f - (max(max(_e579.x, _e579.y), _e579.z) * 2.2f)), 0f, 1f)) * 0.18f))), _e581.xyz, vec3(_e581.w)), vec3<f32>(0f, 0f, 0f), vec3<f32>(1f, 1f, 1f));
    out_u002e_var_u002e_SV_Target0_ = clamp((vec4<f32>(_e994.x, _e994.y, _e994.z, _e579.w) + (vec4<f32>(_e572.x, _e572.y, _e572.z, _e109.w) * (1f - _e579.w))), vec4<f32>(0f, 0f, 0f, 0f), vec4<f32>(1f, 1f, 1f, 1f));
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
