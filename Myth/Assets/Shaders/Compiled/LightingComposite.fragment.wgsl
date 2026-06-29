struct type_u002e_FragmentUniformBlock {
    TextureSizeCamera: vec4<f32>,
    DitherSettings: vec4<f32>,
    DitherGlitch: vec4<f32>,
    DitherGlitchAxis: vec4<f32>,
    AoSettings: vec4<f32>,
    NormalDitherSettings: vec4<f32>,
    DitherAnchorOffsets: array<vec4<f32>, 32>,
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
@group(2) @binding(10)
var NormalTexture: texture_2d<f32>;
@group(2) @binding(110)
var NormalSampler: sampler;
var<private> in_u002e_var_u002e_TEXCOORD0_1: vec2<f32>;
var<private> in_u002e_var_u002e_TEXCOORD1_1: vec4<f32>;
var<private> in_u002e_var_u002e_TEXCOORD4_1: vec4<f32>;
var<private> global: vec4<f32>;
var<private> out_u002e_var_u002e_SV_Target0_: vec4<f32>;

fn fragment_main_1() {
    var phi_228_: vec4<f32>;
    var phi_240_: bool;
    var phi_246_: bool;
    var phi_342_: f32;
    var phi_377_: vec2<f32>;
    var phi_378_: vec2<f32>;
    var phi_379_: vec2<f32>;
    var phi_380_: vec2<f32>;
    var phi_410_: f32;
    var phi_419_: f32;
    var phi_429_: f32;
    var phi_439_: f32;
    var phi_446_: f32;
    var phi_447_: f32;
    var phi_448_: f32;
    var phi_535_: f32;
    var phi_536_: f32;
    var phi_551_: f32;
    var phi_552_: f32;
    var phi_567_: f32;
    var phi_568_: f32;
    var phi_579_: f32;
    var phi_580_: f32;
    var phi_581_: f32;
    var phi_582_: f32;
    var phi_583_: f32;
    var phi_591_: f32;
    var phi_650_: f32;
    var phi_651_: f32;
    var phi_666_: f32;
    var phi_667_: f32;
    var phi_682_: f32;
    var phi_683_: f32;
    var phi_694_: f32;
    var phi_695_: f32;
    var phi_696_: f32;
    var phi_697_: f32;
    var phi_698_: f32;
    var phi_700_: f32;
    var phi_722_: f32;
    var phi_736_: bool;
    var phi_754_: vec3<f32>;
    var phi_755_: vec3<f32>;
    var local: bool;
    var phi_803_: bool;
    var phi_809_: bool;
    var phi_905_: f32;
    var phi_940_: vec2<f32>;
    var phi_941_: vec2<f32>;
    var phi_942_: vec2<f32>;
    var phi_943_: vec2<f32>;
    var phi_973_: f32;
    var phi_982_: f32;
    var phi_992_: f32;
    var phi_1002_: f32;
    var phi_1009_: f32;
    var phi_1010_: f32;
    var phi_1011_: f32;
    var phi_1098_: f32;
    var phi_1099_: f32;
    var phi_1114_: f32;
    var phi_1115_: f32;
    var phi_1130_: f32;
    var phi_1131_: f32;
    var phi_1142_: f32;
    var phi_1143_: f32;
    var phi_1144_: f32;
    var phi_1145_: f32;
    var phi_1146_: f32;
    var phi_1154_: f32;
    var phi_1213_: f32;
    var phi_1214_: f32;
    var phi_1229_: f32;
    var phi_1230_: f32;
    var phi_1245_: f32;
    var phi_1246_: f32;
    var phi_1257_: f32;
    var phi_1258_: f32;
    var phi_1259_: f32;
    var phi_1260_: f32;
    var phi_1261_: f32;
    var phi_1263_: f32;
    var local_1: f32;
    var phi_1285_: f32;
    var phi_1299_: bool;
    var phi_1317_: vec3<f32>;
    var phi_1318_: vec3<f32>;

    let _e106 = in_u002e_var_u002e_TEXCOORD0_1;
    let _e107 = in_u002e_var_u002e_TEXCOORD1_1;
    let _e108 = in_u002e_var_u002e_TEXCOORD4_1;
    let _e109 = global;
    if (_e108.x < -1000f) {
        discard;
    }
    let _e116 = vec2<f32>(_e106.x, (1f - _e106.y));
    let _e117 = textureSample(Texture, Sampler, _e116);
    let _e118 = (_e117 * _e107);
    let _e119 = textureSample(LightTexture, LightSampler, _e116);
    let _e120 = textureSample(TileFieldTexture, TileFieldSampler, _e116);
    let _e121 = textureSample(DitherMaskTexture, DitherMaskSampler, _e116);
    let _e123 = textureSample(EmissiveTexture, EmissiveSampler, _e116);
    let _e124 = textureSample(NormalTexture, NormalSampler, _e116);
    let _e131 = (_e124.w * clamp(length(((_e124.xy * 2f) - vec2<f32>(1f, 1f))), 0f, 1f));
    let _e135 = FragmentUniformBlock.DitherGlitchAxis[2i];
    let _e139 = FragmentUniformBlock.TextureSizeCamera;
    let _e144 = (_e139.zw + (_e109.xy / vec2(select(1f, _e135, (_e135 > 0f)))));
    let _e148 = floor(((_e120.y * 255f) + 0.5f));
    let _e153 = FragmentUniformBlock.DitherAnchorOffsets[i32((_e148 % 32f))];
    let _e155 = (_e144 + _e153.xy);
    let _e159 = FragmentUniformBlock.AoSettings[0i];
    phi_228_ = _e119;
    if (_e159 > 0f) {
        let _e161 = FragmentUniformBlock.AoSettings;
        let _e166 = textureSample(AoTexture, AoSampler, (_e116 + (_e161.yz / _e139.xy)));
        let _e170 = textureSample(BackgroundCoverageTexture, BackgroundCoverageSampler, _e116);
        let _e174 = FragmentUniformBlock.AoSettings[3i];
        let _e178 = (_e119.xyz * mix(1f, _e174, (clamp((_e166.x * _e159), 0f, 1f) * _e170.x)));
        phi_228_ = vec4<f32>(_e178.x, _e178.y, _e178.z, _e119.w);
    }
    let _e185 = phi_228_;
    let _e186 = _e185.xyz;
    switch bitcast<i32>(0u) {
        default: {
            let _e190 = FragmentUniformBlock.DitherSettings[0i];
            let _e191 = (_e190 <= 0f);
            phi_240_ = false;
            local = _e191;
            local_1 = _e190;
            if _e191 {
                let _e194 = FragmentUniformBlock.NormalDitherSettings[0i];
                phi_240_ = (_e194 <= 0f);
            }
            let _e197 = phi_240_;
            phi_246_ = false;
            if _e197 {
                let _e200 = FragmentUniformBlock.NormalDitherSettings[1i];
                phi_246_ = (_e200 <= 1f);
            }
            let _e203 = phi_246_;
            if _e203 {
                phi_755_ = _e186;
                break;
            }
            let _e208 = max(max(_e185.x, _e185.y), _e185.z);
            if (_e208 <= 0.0001f) {
                phi_755_ = _e186;
                break;
            }
            let _e211 = floor((_e148 * 0.03125f));
            let _e215 = floor(((_e120.x * 255f) + 0.5f));
            let _e218 = FragmentUniformBlock.DitherSettings[3i];
            let _e222 = ((_e218 * _e218) * (3f - (2f * _e218)));
            let _e226 = FragmentUniformBlock.DitherGlitch[2i];
            let _e228 = mix(24f, (_e226 * 1.45f), _e222);
            let _e230 = FragmentUniformBlock.DitherGlitch;
            let _e232 = FragmentUniformBlock.DitherGlitchAxis;
            let _e234 = normalize(_e232.xy);
            let _e239 = (_e144 - _e230.xy);
            let _e242 = (dot(_e239, _e234) / max(_e228, 1f));
            let _e245 = (dot(_e239, vec2<f32>(-(_e234.y), _e234.x)) / max((_e228 * 0.38f), 1f));
            let _e256 = (((_e222 * smoothstep(1f, 0.25f, sqrt(((_e242 * _e242) + (_e245 * _e245))))) * 0.82f) + ((_e222 * smoothstep(0.6f, 1f, _e222)) * 0.18f));
            let _e259 = FragmentUniformBlock.DitherGlitch[3i];
            let _e261 = floor((_e259 * 9f));
            let _e263 = floor((_e155 * vec2<f32>(0.0625f, 0.0625f)));
            let _e285 = fract((sin(((((_e215 * 19.19f) + (_e263.x * 7.13f)) + (_e263.y * 3.71f)) + (_e261 * 5.17f))) * 43758.547f));
            let _e286 = (fract((sin(((((_e263.x * 17f) + (_e263.y * 31f)) + (_e261 * 13f)) + (_e215 * 0.23f))) * 43758.547f)) - 0.5f);
            let _e292 = (_e211 == 2f);
            if _e292 {
                phi_342_ = 2f;
            } else {
                phi_342_ = select(1f, 3f, (_e211 == 3f));
            }
            let _e296 = phi_342_;
            let _e297 = (_e215 % 3f);
            let _e302 = FragmentUniformBlock.DitherSettings[1i];
            let _e308 = floor(((_e155 + ((vec2<f32>(_e286, (_e285 - 0.5f)) * _e256) * 9f)) / vec2(max(((_e302 * _e296) * (1f + (_e297 * 0.08f))), 1f))));
            let _e309 = (_e211 == 1f);
            if _e309 {
                phi_380_ = vec2<f32>(-(_e308.x), _e308.y);
            } else {
                if _e292 {
                    phi_379_ = _e308.yx;
                } else {
                    if (_e211 == 3f) {
                        phi_378_ = vec2<f32>(_e308.x, -(_e308.y));
                    } else {
                        phi_377_ = _e308;
                        if (_e211 >= 4f) {
                            phi_377_ = -(_e308.yx);
                        }
                        let _e326 = phi_377_;
                        phi_378_ = _e326;
                    }
                    let _e328 = phi_378_;
                    phi_379_ = _e328;
                }
                let _e330 = phi_379_;
                phi_380_ = _e330;
            }
            let _e332 = phi_380_;
            let _e345 = (_e332 + (vec2<f32>(((_e211 * 2f) % 4f), ((_e211 * 3f) % 4f)) + (vec2<f32>((_e215 % 4f), (floor((_e215 * 0.25f)) % 4f)) * 0.35f)));
            if (_e256 > 0.65f) {
                phi_448_ = floor((_e285 * 11f));
            } else {
                switch bitcast<i32>(0u) {
                    default: {
                        if _e309 {
                            if (_e297 == 0f) {
                                phi_410_ = 8f;
                            } else {
                                phi_410_ = select(5f, 6f, (_e297 == 1f));
                            }
                            let _e354 = phi_410_;
                            phi_447_ = _e354;
                            break;
                        }
                        if _e292 {
                            if (_e297 == 0f) {
                                phi_419_ = 2f;
                            } else {
                                phi_419_ = select(10f, 9f, (_e297 == 1f));
                            }
                            let _e359 = phi_419_;
                            phi_447_ = _e359;
                            break;
                        }
                        if (_e211 == 3f) {
                            if (_e297 == 0f) {
                                phi_429_ = 3f;
                            } else {
                                phi_429_ = select(1f, 5f, (_e297 == 1f));
                            }
                            let _e365 = phi_429_;
                            phi_447_ = _e365;
                            break;
                        }
                        if (_e211 >= 4f) {
                            if (_e297 == 0f) {
                                phi_439_ = 4f;
                            } else {
                                phi_439_ = select(10f, 7f, (_e297 == 1f));
                            }
                            let _e371 = phi_439_;
                            phi_447_ = _e371;
                            break;
                        }
                        if (_e297 == 0f) {
                            phi_446_ = 0f;
                        } else {
                            phi_446_ = select(7f, 4f, (_e297 == 1f));
                        }
                        let _e376 = phi_446_;
                        phi_447_ = _e376;
                        break;
                    }
                }
                let _e378 = phi_447_;
                phi_448_ = _e378;
            }
            let _e380 = phi_448_;
            switch bitcast<i32>(0u) {
                default: {
                    if (_e380 == 1f) {
                        phi_700_ = ((((_e345.x + (_e345.y * 2f)) % 6f) + 1f) * 0.14285715f);
                        break;
                    }
                    if (_e380 == 2f) {
                        let _e392 = ((_e345 % vec2<f32>(4f, 4f)) - vec2<f32>(1.5f, 1.5f));
                        phi_700_ = clamp(((dot(_e392, _e392) + 0.5f) * 0.2f), 0f, 1f);
                        break;
                    }
                    if (_e380 == 3f) {
                        phi_700_ = (((_e345.x % 4f) + 1f) * 0.2f);
                        break;
                    }
                    if (_e380 == 4f) {
                        phi_700_ = fract((sin(dot(floor(_e345), vec2<f32>(127.1f, 311.7f))) * 43758.547f));
                        break;
                    }
                    if (_e380 == 5f) {
                        phi_700_ = (((_e345.y % 4f) + 1f) * 0.2f);
                        break;
                    }
                    if (_e380 == 6f) {
                        phi_700_ = ((min(((_e345.x + _e345.y) % 4f), (((_e345.x - _e345.y) + 8f) % 4f)) + 1f) * 0.2f);
                        break;
                    }
                    if (_e380 == 7f) {
                        let _e425 = (_e345 % vec2<f32>(4f, 4f));
                        let _e434 = i32((_e345.x % 4f));
                        let _e437 = i32((_e345.y % 4f));
                        if (_e434 == 0i) {
                            if (_e437 == 0i) {
                                phi_536_ = 16f;
                            } else {
                                if (_e437 == 1i) {
                                    phi_535_ = 5f;
                                } else {
                                    phi_535_ = select(1f, 13f, (_e437 == 2i));
                                }
                                let _e444 = phi_535_;
                                phi_536_ = _e444;
                            }
                            let _e446 = phi_536_;
                            phi_583_ = _e446;
                        } else {
                            if (_e434 == 1i) {
                                if (_e437 == 0i) {
                                    phi_552_ = 8f;
                                } else {
                                    if (_e437 == 1i) {
                                        phi_551_ = 12f;
                                    } else {
                                        phi_551_ = select(9f, 4f, (_e437 == 2i));
                                    }
                                    let _e453 = phi_551_;
                                    phi_552_ = _e453;
                                }
                                let _e455 = phi_552_;
                                phi_582_ = _e455;
                            } else {
                                if (_e434 == 2i) {
                                    if (_e437 == 0i) {
                                        phi_568_ = 14f;
                                    } else {
                                        if (_e437 == 1i) {
                                            phi_567_ = 2f;
                                        } else {
                                            phi_567_ = select(3f, 15f, (_e437 == 2i));
                                        }
                                        let _e462 = phi_567_;
                                        phi_568_ = _e462;
                                    }
                                    let _e464 = phi_568_;
                                    phi_581_ = _e464;
                                } else {
                                    if (_e437 == 0i) {
                                        phi_580_ = 6f;
                                    } else {
                                        if (_e437 == 1i) {
                                            phi_579_ = 10f;
                                        } else {
                                            phi_579_ = select(11f, 7f, (_e437 == 2i));
                                        }
                                        let _e470 = phi_579_;
                                        phi_580_ = _e470;
                                    }
                                    let _e472 = phi_580_;
                                    phi_581_ = _e472;
                                }
                                let _e474 = phi_581_;
                                phi_582_ = _e474;
                            }
                            let _e476 = phi_582_;
                            phi_583_ = _e476;
                        }
                        let _e478 = phi_583_;
                        if (((floor(_e425.x) + floor(_e425.y)) % 2f) == 0f) {
                            phi_591_ = (_e478 * 0.04411765f);
                        } else {
                            phi_591_ = (0.35f + (_e478 * 0.03823529f));
                        }
                        let _e484 = phi_591_;
                        phi_700_ = _e484;
                        break;
                    }
                    if (_e380 == 8f) {
                        phi_700_ = clamp(((min(((_e345.x + ((floor((_e345.y * 0.5f)) % 2f) * 2f)) % 4f), (_e345.y % 4f)) + 1f) * 0.2f), 0f, 1f);
                        break;
                    }
                    if (_e380 == 9f) {
                        phi_700_ = (((((_e345.x * 2f) + _e345.y) % 8f) + 1f) * 0.11111111f);
                        break;
                    }
                    if (_e380 >= 10f) {
                        let _e510 = abs(((_e345 % vec2<f32>(8f, 8f)) - vec2<f32>(3.5f, 3.5f)));
                        phi_700_ = clamp(((max(_e510.x, _e510.y) - 0.5f) * 0.2857143f), 0f, 1f);
                        break;
                    }
                    let _e519 = i32((_e345.x % 4f));
                    let _e522 = i32((_e345.y % 4f));
                    if (_e519 == 0i) {
                        if (_e522 == 0i) {
                            phi_651_ = 16f;
                        } else {
                            if (_e522 == 1i) {
                                phi_650_ = 5f;
                            } else {
                                phi_650_ = select(1f, 13f, (_e522 == 2i));
                            }
                            let _e529 = phi_650_;
                            phi_651_ = _e529;
                        }
                        let _e531 = phi_651_;
                        phi_698_ = _e531;
                    } else {
                        if (_e519 == 1i) {
                            if (_e522 == 0i) {
                                phi_667_ = 8f;
                            } else {
                                if (_e522 == 1i) {
                                    phi_666_ = 12f;
                                } else {
                                    phi_666_ = select(9f, 4f, (_e522 == 2i));
                                }
                                let _e538 = phi_666_;
                                phi_667_ = _e538;
                            }
                            let _e540 = phi_667_;
                            phi_697_ = _e540;
                        } else {
                            if (_e519 == 2i) {
                                if (_e522 == 0i) {
                                    phi_683_ = 14f;
                                } else {
                                    if (_e522 == 1i) {
                                        phi_682_ = 2f;
                                    } else {
                                        phi_682_ = select(3f, 15f, (_e522 == 2i));
                                    }
                                    let _e547 = phi_682_;
                                    phi_683_ = _e547;
                                }
                                let _e549 = phi_683_;
                                phi_696_ = _e549;
                            } else {
                                if (_e522 == 0i) {
                                    phi_695_ = 6f;
                                } else {
                                    if (_e522 == 1i) {
                                        phi_694_ = 10f;
                                    } else {
                                        phi_694_ = select(11f, 7f, (_e522 == 2i));
                                    }
                                    let _e555 = phi_694_;
                                    phi_695_ = _e555;
                                }
                                let _e557 = phi_695_;
                                phi_696_ = _e557;
                            }
                            let _e559 = phi_696_;
                            phi_697_ = _e559;
                        }
                        let _e561 = phi_697_;
                        phi_698_ = _e561;
                    }
                    let _e563 = phi_698_;
                    phi_700_ = (_e563 * 0.05882353f);
                    break;
                }
            }
            let _e566 = phi_700_;
            let _e576 = clamp((clamp((_e566 + ((fract((_e215 * 0.618034f)) - 0.5f) * 0.1f)), 0f, 1f) + ((_e286 * _e256) * 0.18f)), 0f, 1f);
            if (_e190 > 0f) {
                let _e584 = FragmentUniformBlock.DitherSettings[2i];
                phi_722_ = (_e584 * _e121.x);
            } else {
                phi_722_ = 0f;
            }
            let _e587 = phi_722_;
            let _e590 = FragmentUniformBlock.NormalDitherSettings[0i];
            let _e595 = (_e186 * mix(1f, (select(1f, 0f, (clamp(_e208, 0f, 1f) < _e576)) / _e208), clamp((_e587 + (_e131 * _e590)), 0f, 1f)));
            let _e598 = FragmentUniformBlock.NormalDitherSettings[1i];
            phi_736_ = false;
            if (_e598 > 1f) {
                phi_736_ = (_e131 > 0f);
            }
            let _e602 = phi_736_;
            phi_754_ = _e595;
            if _e602 {
                let _e607 = max(max(_e595.x, _e595.y), _e595.z);
                let _e608 = (_e598 - 1f);
                phi_754_ = (_e595 * mix(1f, ((floor(((clamp(_e607, 0f, 1f) * _e608) + _e576)) / _e608) / max(_e607, 0.0001f)), _e131));
            }
            let _e619 = phi_754_;
            phi_755_ = _e619;
            break;
        }
    }
    let _e621 = phi_755_;
    let _e634 = _e621.xyz;
    let _e642 = clamp(mix(((_e118.xyz * _e634) + (_e634 * ((_e185.w * clamp((1f - (max(max(_e118.x, _e118.y), _e118.z) * 2.2f)), 0f, 1f)) * 0.18f))), _e123.xyz, vec3(_e123.w)), vec3<f32>(0f, 0f, 0f), vec3<f32>(1f, 1f, 1f));
    let _e648 = textureSample(ForegroundTexture, ForegroundSampler, _e116);
    let _e649 = (_e648 * _e107);
    let _e650 = textureSample(ForegroundLightTexture, ForegroundLightSampler, _e116);
    let _e651 = textureSample(ForegroundEmissiveTexture, ForegroundEmissiveSampler, _e116);
    let _e652 = _e650.xyz;
    switch bitcast<i32>(0u) {
        default: {
            let _e655 = local;
            phi_803_ = false;
            if _e655 {
                let _e658 = FragmentUniformBlock.NormalDitherSettings[0i];
                phi_803_ = (_e658 <= 0f);
            }
            let _e661 = phi_803_;
            phi_809_ = false;
            if _e661 {
                let _e664 = FragmentUniformBlock.NormalDitherSettings[1i];
                phi_809_ = (_e664 <= 1f);
            }
            let _e667 = phi_809_;
            if _e667 {
                phi_1318_ = _e652;
                break;
            }
            let _e672 = max(max(_e650.x, _e650.y), _e650.z);
            if (_e672 <= 0.0001f) {
                phi_1318_ = _e652;
                break;
            }
            let _e675 = floor((_e148 * 0.03125f));
            let _e679 = floor(((_e120.x * 255f) + 0.5f));
            let _e682 = FragmentUniformBlock.DitherSettings[3i];
            let _e686 = ((_e682 * _e682) * (3f - (2f * _e682)));
            let _e690 = FragmentUniformBlock.DitherGlitch[2i];
            let _e692 = mix(24f, (_e690 * 1.45f), _e686);
            let _e694 = FragmentUniformBlock.DitherGlitch;
            let _e696 = FragmentUniformBlock.DitherGlitchAxis;
            let _e698 = normalize(_e696.xy);
            let _e703 = (_e144 - _e694.xy);
            let _e706 = (dot(_e703, _e698) / max(_e692, 1f));
            let _e709 = (dot(_e703, vec2<f32>(-(_e698.y), _e698.x)) / max((_e692 * 0.38f), 1f));
            let _e720 = (((_e686 * smoothstep(1f, 0.25f, sqrt(((_e706 * _e706) + (_e709 * _e709))))) * 0.82f) + ((_e686 * smoothstep(0.6f, 1f, _e686)) * 0.18f));
            let _e723 = FragmentUniformBlock.DitherGlitch[3i];
            let _e725 = floor((_e723 * 9f));
            let _e727 = floor((_e155 * vec2<f32>(0.0625f, 0.0625f)));
            let _e749 = fract((sin(((((_e679 * 19.19f) + (_e727.x * 7.13f)) + (_e727.y * 3.71f)) + (_e725 * 5.17f))) * 43758.547f));
            let _e750 = (fract((sin(((((_e727.x * 17f) + (_e727.y * 31f)) + (_e725 * 13f)) + (_e679 * 0.23f))) * 43758.547f)) - 0.5f);
            let _e756 = (_e675 == 2f);
            if _e756 {
                phi_905_ = 2f;
            } else {
                phi_905_ = select(1f, 3f, (_e675 == 3f));
            }
            let _e760 = phi_905_;
            let _e761 = (_e679 % 3f);
            let _e766 = FragmentUniformBlock.DitherSettings[1i];
            let _e772 = floor(((_e155 + ((vec2<f32>(_e750, (_e749 - 0.5f)) * _e720) * 9f)) / vec2(max(((_e766 * _e760) * (1f + (_e761 * 0.08f))), 1f))));
            let _e773 = (_e675 == 1f);
            if _e773 {
                phi_943_ = vec2<f32>(-(_e772.x), _e772.y);
            } else {
                if _e756 {
                    phi_942_ = _e772.yx;
                } else {
                    if (_e675 == 3f) {
                        phi_941_ = vec2<f32>(_e772.x, -(_e772.y));
                    } else {
                        phi_940_ = _e772;
                        if (_e675 >= 4f) {
                            phi_940_ = -(_e772.yx);
                        }
                        let _e790 = phi_940_;
                        phi_941_ = _e790;
                    }
                    let _e792 = phi_941_;
                    phi_942_ = _e792;
                }
                let _e794 = phi_942_;
                phi_943_ = _e794;
            }
            let _e796 = phi_943_;
            let _e809 = (_e796 + (vec2<f32>(((_e675 * 2f) % 4f), ((_e675 * 3f) % 4f)) + (vec2<f32>((_e679 % 4f), (floor((_e679 * 0.25f)) % 4f)) * 0.35f)));
            if (_e720 > 0.65f) {
                phi_1011_ = floor((_e749 * 11f));
            } else {
                switch bitcast<i32>(0u) {
                    default: {
                        if _e773 {
                            if (_e761 == 0f) {
                                phi_973_ = 8f;
                            } else {
                                phi_973_ = select(5f, 6f, (_e761 == 1f));
                            }
                            let _e818 = phi_973_;
                            phi_1010_ = _e818;
                            break;
                        }
                        if _e756 {
                            if (_e761 == 0f) {
                                phi_982_ = 2f;
                            } else {
                                phi_982_ = select(10f, 9f, (_e761 == 1f));
                            }
                            let _e823 = phi_982_;
                            phi_1010_ = _e823;
                            break;
                        }
                        if (_e675 == 3f) {
                            if (_e761 == 0f) {
                                phi_992_ = 3f;
                            } else {
                                phi_992_ = select(1f, 5f, (_e761 == 1f));
                            }
                            let _e829 = phi_992_;
                            phi_1010_ = _e829;
                            break;
                        }
                        if (_e675 >= 4f) {
                            if (_e761 == 0f) {
                                phi_1002_ = 4f;
                            } else {
                                phi_1002_ = select(10f, 7f, (_e761 == 1f));
                            }
                            let _e835 = phi_1002_;
                            phi_1010_ = _e835;
                            break;
                        }
                        if (_e761 == 0f) {
                            phi_1009_ = 0f;
                        } else {
                            phi_1009_ = select(7f, 4f, (_e761 == 1f));
                        }
                        let _e840 = phi_1009_;
                        phi_1010_ = _e840;
                        break;
                    }
                }
                let _e842 = phi_1010_;
                phi_1011_ = _e842;
            }
            let _e844 = phi_1011_;
            switch bitcast<i32>(0u) {
                default: {
                    if (_e844 == 1f) {
                        phi_1263_ = ((((_e809.x + (_e809.y * 2f)) % 6f) + 1f) * 0.14285715f);
                        break;
                    }
                    if (_e844 == 2f) {
                        let _e856 = ((_e809 % vec2<f32>(4f, 4f)) - vec2<f32>(1.5f, 1.5f));
                        phi_1263_ = clamp(((dot(_e856, _e856) + 0.5f) * 0.2f), 0f, 1f);
                        break;
                    }
                    if (_e844 == 3f) {
                        phi_1263_ = (((_e809.x % 4f) + 1f) * 0.2f);
                        break;
                    }
                    if (_e844 == 4f) {
                        phi_1263_ = fract((sin(dot(floor(_e809), vec2<f32>(127.1f, 311.7f))) * 43758.547f));
                        break;
                    }
                    if (_e844 == 5f) {
                        phi_1263_ = (((_e809.y % 4f) + 1f) * 0.2f);
                        break;
                    }
                    if (_e844 == 6f) {
                        phi_1263_ = ((min(((_e809.x + _e809.y) % 4f), (((_e809.x - _e809.y) + 8f) % 4f)) + 1f) * 0.2f);
                        break;
                    }
                    if (_e844 == 7f) {
                        let _e889 = (_e809 % vec2<f32>(4f, 4f));
                        let _e898 = i32((_e809.x % 4f));
                        let _e901 = i32((_e809.y % 4f));
                        if (_e898 == 0i) {
                            if (_e901 == 0i) {
                                phi_1099_ = 16f;
                            } else {
                                if (_e901 == 1i) {
                                    phi_1098_ = 5f;
                                } else {
                                    phi_1098_ = select(1f, 13f, (_e901 == 2i));
                                }
                                let _e908 = phi_1098_;
                                phi_1099_ = _e908;
                            }
                            let _e910 = phi_1099_;
                            phi_1146_ = _e910;
                        } else {
                            if (_e898 == 1i) {
                                if (_e901 == 0i) {
                                    phi_1115_ = 8f;
                                } else {
                                    if (_e901 == 1i) {
                                        phi_1114_ = 12f;
                                    } else {
                                        phi_1114_ = select(9f, 4f, (_e901 == 2i));
                                    }
                                    let _e917 = phi_1114_;
                                    phi_1115_ = _e917;
                                }
                                let _e919 = phi_1115_;
                                phi_1145_ = _e919;
                            } else {
                                if (_e898 == 2i) {
                                    if (_e901 == 0i) {
                                        phi_1131_ = 14f;
                                    } else {
                                        if (_e901 == 1i) {
                                            phi_1130_ = 2f;
                                        } else {
                                            phi_1130_ = select(3f, 15f, (_e901 == 2i));
                                        }
                                        let _e926 = phi_1130_;
                                        phi_1131_ = _e926;
                                    }
                                    let _e928 = phi_1131_;
                                    phi_1144_ = _e928;
                                } else {
                                    if (_e901 == 0i) {
                                        phi_1143_ = 6f;
                                    } else {
                                        if (_e901 == 1i) {
                                            phi_1142_ = 10f;
                                        } else {
                                            phi_1142_ = select(11f, 7f, (_e901 == 2i));
                                        }
                                        let _e934 = phi_1142_;
                                        phi_1143_ = _e934;
                                    }
                                    let _e936 = phi_1143_;
                                    phi_1144_ = _e936;
                                }
                                let _e938 = phi_1144_;
                                phi_1145_ = _e938;
                            }
                            let _e940 = phi_1145_;
                            phi_1146_ = _e940;
                        }
                        let _e942 = phi_1146_;
                        if (((floor(_e889.x) + floor(_e889.y)) % 2f) == 0f) {
                            phi_1154_ = (_e942 * 0.04411765f);
                        } else {
                            phi_1154_ = (0.35f + (_e942 * 0.03823529f));
                        }
                        let _e948 = phi_1154_;
                        phi_1263_ = _e948;
                        break;
                    }
                    if (_e844 == 8f) {
                        phi_1263_ = clamp(((min(((_e809.x + ((floor((_e809.y * 0.5f)) % 2f) * 2f)) % 4f), (_e809.y % 4f)) + 1f) * 0.2f), 0f, 1f);
                        break;
                    }
                    if (_e844 == 9f) {
                        phi_1263_ = (((((_e809.x * 2f) + _e809.y) % 8f) + 1f) * 0.11111111f);
                        break;
                    }
                    if (_e844 >= 10f) {
                        let _e974 = abs(((_e809 % vec2<f32>(8f, 8f)) - vec2<f32>(3.5f, 3.5f)));
                        phi_1263_ = clamp(((max(_e974.x, _e974.y) - 0.5f) * 0.2857143f), 0f, 1f);
                        break;
                    }
                    let _e983 = i32((_e809.x % 4f));
                    let _e986 = i32((_e809.y % 4f));
                    if (_e983 == 0i) {
                        if (_e986 == 0i) {
                            phi_1214_ = 16f;
                        } else {
                            if (_e986 == 1i) {
                                phi_1213_ = 5f;
                            } else {
                                phi_1213_ = select(1f, 13f, (_e986 == 2i));
                            }
                            let _e993 = phi_1213_;
                            phi_1214_ = _e993;
                        }
                        let _e995 = phi_1214_;
                        phi_1261_ = _e995;
                    } else {
                        if (_e983 == 1i) {
                            if (_e986 == 0i) {
                                phi_1230_ = 8f;
                            } else {
                                if (_e986 == 1i) {
                                    phi_1229_ = 12f;
                                } else {
                                    phi_1229_ = select(9f, 4f, (_e986 == 2i));
                                }
                                let _e1002 = phi_1229_;
                                phi_1230_ = _e1002;
                            }
                            let _e1004 = phi_1230_;
                            phi_1260_ = _e1004;
                        } else {
                            if (_e983 == 2i) {
                                if (_e986 == 0i) {
                                    phi_1246_ = 14f;
                                } else {
                                    if (_e986 == 1i) {
                                        phi_1245_ = 2f;
                                    } else {
                                        phi_1245_ = select(3f, 15f, (_e986 == 2i));
                                    }
                                    let _e1011 = phi_1245_;
                                    phi_1246_ = _e1011;
                                }
                                let _e1013 = phi_1246_;
                                phi_1259_ = _e1013;
                            } else {
                                if (_e986 == 0i) {
                                    phi_1258_ = 6f;
                                } else {
                                    if (_e986 == 1i) {
                                        phi_1257_ = 10f;
                                    } else {
                                        phi_1257_ = select(11f, 7f, (_e986 == 2i));
                                    }
                                    let _e1019 = phi_1257_;
                                    phi_1258_ = _e1019;
                                }
                                let _e1021 = phi_1258_;
                                phi_1259_ = _e1021;
                            }
                            let _e1023 = phi_1259_;
                            phi_1260_ = _e1023;
                        }
                        let _e1025 = phi_1260_;
                        phi_1261_ = _e1025;
                    }
                    let _e1027 = phi_1261_;
                    phi_1263_ = (_e1027 * 0.05882353f);
                    break;
                }
            }
            let _e1030 = phi_1263_;
            let _e1040 = clamp((clamp((_e1030 + ((fract((_e679 * 0.618034f)) - 0.5f) * 0.1f)), 0f, 1f) + ((_e750 * _e720) * 0.18f)), 0f, 1f);
            let _e1046 = local_1;
            if (_e1046 > 0f) {
                let _e1050 = FragmentUniformBlock.DitherSettings[2i];
                phi_1285_ = (_e1050 * _e121.x);
            } else {
                phi_1285_ = 0f;
            }
            let _e1053 = phi_1285_;
            let _e1056 = FragmentUniformBlock.NormalDitherSettings[0i];
            let _e1061 = (_e652 * mix(1f, (select(1f, 0f, (clamp(_e672, 0f, 1f) < _e1040)) / _e672), clamp((_e1053 + (_e131 * _e1056)), 0f, 1f)));
            let _e1064 = FragmentUniformBlock.NormalDitherSettings[1i];
            phi_1299_ = false;
            if (_e1064 > 1f) {
                phi_1299_ = (_e131 > 0f);
            }
            let _e1068 = phi_1299_;
            phi_1317_ = _e1061;
            if _e1068 {
                let _e1073 = max(max(_e1061.x, _e1061.y), _e1061.z);
                let _e1074 = (_e1064 - 1f);
                phi_1317_ = (_e1061 * mix(1f, ((floor(((clamp(_e1073, 0f, 1f) * _e1074) + _e1040)) / _e1074) / max(_e1073, 0.0001f)), _e131));
            }
            let _e1085 = phi_1317_;
            phi_1318_ = _e1085;
            break;
        }
    }
    let _e1087 = phi_1318_;
    let _e1100 = _e1087.xyz;
    let _e1108 = clamp(mix(((_e649.xyz * _e1100) + (_e1100 * ((_e650.w * clamp((1f - (max(max(_e649.x, _e649.y), _e649.z) * 2.2f)), 0f, 1f)) * 0.18f))), _e651.xyz, vec3(_e651.w)), vec3<f32>(0f, 0f, 0f), vec3<f32>(1f, 1f, 1f));
    out_u002e_var_u002e_SV_Target0_ = clamp((vec4<f32>(_e1108.x, _e1108.y, _e1108.z, _e649.w) + (vec4<f32>(_e642.x, _e642.y, _e642.z, _e118.w) * (1f - _e649.w))), vec4<f32>(0f, 0f, 0f, 0f), vec4<f32>(1f, 1f, 1f, 1f));
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
