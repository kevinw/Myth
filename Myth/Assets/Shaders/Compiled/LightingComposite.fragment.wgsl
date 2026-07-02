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
    var phi_232_: vec4<f32>;
    var phi_244_: bool;
    var phi_250_: bool;
    var phi_346_: f32;
    var phi_381_: vec2<f32>;
    var phi_382_: vec2<f32>;
    var phi_383_: vec2<f32>;
    var phi_384_: vec2<f32>;
    var phi_414_: f32;
    var phi_423_: f32;
    var phi_433_: f32;
    var phi_443_: f32;
    var phi_450_: f32;
    var phi_451_: f32;
    var phi_452_: f32;
    var phi_539_: f32;
    var phi_540_: f32;
    var phi_555_: f32;
    var phi_556_: f32;
    var phi_571_: f32;
    var phi_572_: f32;
    var phi_583_: f32;
    var phi_584_: f32;
    var phi_585_: f32;
    var phi_586_: f32;
    var phi_587_: f32;
    var phi_595_: f32;
    var phi_654_: f32;
    var phi_655_: f32;
    var phi_670_: f32;
    var phi_671_: f32;
    var phi_686_: f32;
    var phi_687_: f32;
    var phi_698_: f32;
    var phi_699_: f32;
    var phi_700_: f32;
    var phi_701_: f32;
    var phi_702_: f32;
    var phi_704_: f32;
    var phi_726_: f32;
    var phi_740_: bool;
    var phi_758_: vec3<f32>;
    var phi_759_: vec3<f32>;
    var local: bool;
    var phi_807_: bool;
    var phi_813_: bool;
    var phi_909_: f32;
    var phi_944_: vec2<f32>;
    var phi_945_: vec2<f32>;
    var phi_946_: vec2<f32>;
    var phi_947_: vec2<f32>;
    var phi_977_: f32;
    var phi_986_: f32;
    var phi_996_: f32;
    var phi_1006_: f32;
    var phi_1013_: f32;
    var phi_1014_: f32;
    var phi_1015_: f32;
    var phi_1102_: f32;
    var phi_1103_: f32;
    var phi_1118_: f32;
    var phi_1119_: f32;
    var phi_1134_: f32;
    var phi_1135_: f32;
    var phi_1146_: f32;
    var phi_1147_: f32;
    var phi_1148_: f32;
    var phi_1149_: f32;
    var phi_1150_: f32;
    var phi_1158_: f32;
    var phi_1217_: f32;
    var phi_1218_: f32;
    var phi_1233_: f32;
    var phi_1234_: f32;
    var phi_1249_: f32;
    var phi_1250_: f32;
    var phi_1261_: f32;
    var phi_1262_: f32;
    var phi_1263_: f32;
    var phi_1264_: f32;
    var phi_1265_: f32;
    var phi_1267_: f32;
    var local_1: f32;
    var phi_1289_: f32;
    var phi_1303_: bool;
    var phi_1321_: vec3<f32>;
    var phi_1322_: vec3<f32>;

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
    let _e149 = (_e139.zw + (vec2<f32>(_e109.x, (_e139.y - _e109.y)) / vec2(select(1f, _e135, (_e135 > 0f)))));
    let _e153 = floor(((_e120.y * 255f) + 0.5f));
    let _e158 = FragmentUniformBlock.DitherAnchorOffsets[i32((_e153 % 32f))];
    let _e160 = (_e149 + _e158.xy);
    let _e164 = FragmentUniformBlock.AoSettings[0i];
    phi_232_ = _e119;
    if (_e164 > 0f) {
        let _e166 = FragmentUniformBlock.AoSettings;
        let _e170 = textureSample(AoTexture, AoSampler, (_e116 + (_e166.yz / _e139.xy)));
        let _e174 = textureSample(BackgroundCoverageTexture, BackgroundCoverageSampler, _e116);
        let _e178 = FragmentUniformBlock.AoSettings[3i];
        let _e182 = (_e119.xyz * mix(1f, _e178, (clamp((_e170.x * _e164), 0f, 1f) * _e174.x)));
        phi_232_ = vec4<f32>(_e182.x, _e182.y, _e182.z, _e119.w);
    }
    let _e189 = phi_232_;
    let _e190 = _e189.xyz;
    switch bitcast<i32>(0u) {
        default: {
            let _e194 = FragmentUniformBlock.DitherSettings[0i];
            let _e195 = (_e194 <= 0f);
            phi_244_ = false;
            local = _e195;
            local_1 = _e194;
            if _e195 {
                let _e198 = FragmentUniformBlock.NormalDitherSettings[0i];
                phi_244_ = (_e198 <= 0f);
            }
            let _e201 = phi_244_;
            phi_250_ = false;
            if _e201 {
                let _e204 = FragmentUniformBlock.NormalDitherSettings[1i];
                phi_250_ = (_e204 <= 1f);
            }
            let _e207 = phi_250_;
            if _e207 {
                phi_759_ = _e190;
                break;
            }
            let _e212 = max(max(_e189.x, _e189.y), _e189.z);
            if (_e212 <= 0.0001f) {
                phi_759_ = _e190;
                break;
            }
            let _e215 = floor((_e153 * 0.03125f));
            let _e219 = floor(((_e120.x * 255f) + 0.5f));
            let _e222 = FragmentUniformBlock.DitherSettings[3i];
            let _e226 = ((_e222 * _e222) * (3f - (2f * _e222)));
            let _e230 = FragmentUniformBlock.DitherGlitch[2i];
            let _e232 = mix(24f, (_e230 * 1.45f), _e226);
            let _e234 = FragmentUniformBlock.DitherGlitch;
            let _e236 = FragmentUniformBlock.DitherGlitchAxis;
            let _e238 = normalize(_e236.xy);
            let _e243 = (_e149 - _e234.xy);
            let _e246 = (dot(_e243, _e238) / max(_e232, 1f));
            let _e249 = (dot(_e243, vec2<f32>(-(_e238.y), _e238.x)) / max((_e232 * 0.38f), 1f));
            let _e260 = (((_e226 * smoothstep(1f, 0.25f, sqrt(((_e246 * _e246) + (_e249 * _e249))))) * 0.82f) + ((_e226 * smoothstep(0.6f, 1f, _e226)) * 0.18f));
            let _e263 = FragmentUniformBlock.DitherGlitch[3i];
            let _e265 = floor((_e263 * 9f));
            let _e267 = floor((_e160 * vec2<f32>(0.0625f, 0.0625f)));
            let _e289 = fract((sin(((((_e219 * 19.19f) + (_e267.x * 7.13f)) + (_e267.y * 3.71f)) + (_e265 * 5.17f))) * 43758.547f));
            let _e290 = (fract((sin(((((_e267.x * 17f) + (_e267.y * 31f)) + (_e265 * 13f)) + (_e219 * 0.23f))) * 43758.547f)) - 0.5f);
            let _e296 = (_e215 == 2f);
            if _e296 {
                phi_346_ = 2f;
            } else {
                phi_346_ = select(1f, 3f, (_e215 == 3f));
            }
            let _e300 = phi_346_;
            let _e301 = (_e219 % 3f);
            let _e306 = FragmentUniformBlock.DitherSettings[1i];
            let _e312 = floor(((_e160 + ((vec2<f32>(_e290, (_e289 - 0.5f)) * _e260) * 9f)) / vec2(max(((_e306 * _e300) * (1f + (_e301 * 0.08f))), 1f))));
            let _e313 = (_e215 == 1f);
            if _e313 {
                phi_384_ = vec2<f32>(-(_e312.x), _e312.y);
            } else {
                if _e296 {
                    phi_383_ = _e312.yx;
                } else {
                    if (_e215 == 3f) {
                        phi_382_ = vec2<f32>(_e312.x, -(_e312.y));
                    } else {
                        phi_381_ = _e312;
                        if (_e215 >= 4f) {
                            phi_381_ = -(_e312.yx);
                        }
                        let _e330 = phi_381_;
                        phi_382_ = _e330;
                    }
                    let _e332 = phi_382_;
                    phi_383_ = _e332;
                }
                let _e334 = phi_383_;
                phi_384_ = _e334;
            }
            let _e336 = phi_384_;
            let _e349 = (_e336 + (vec2<f32>(((_e215 * 2f) % 4f), ((_e215 * 3f) % 4f)) + (vec2<f32>((_e219 % 4f), (floor((_e219 * 0.25f)) % 4f)) * 0.35f)));
            if (_e260 > 0.65f) {
                phi_452_ = floor((_e289 * 11f));
            } else {
                switch bitcast<i32>(0u) {
                    default: {
                        if _e313 {
                            if (_e301 == 0f) {
                                phi_414_ = 8f;
                            } else {
                                phi_414_ = select(5f, 6f, (_e301 == 1f));
                            }
                            let _e358 = phi_414_;
                            phi_451_ = _e358;
                            break;
                        }
                        if _e296 {
                            if (_e301 == 0f) {
                                phi_423_ = 2f;
                            } else {
                                phi_423_ = select(10f, 9f, (_e301 == 1f));
                            }
                            let _e363 = phi_423_;
                            phi_451_ = _e363;
                            break;
                        }
                        if (_e215 == 3f) {
                            if (_e301 == 0f) {
                                phi_433_ = 3f;
                            } else {
                                phi_433_ = select(1f, 5f, (_e301 == 1f));
                            }
                            let _e369 = phi_433_;
                            phi_451_ = _e369;
                            break;
                        }
                        if (_e215 >= 4f) {
                            if (_e301 == 0f) {
                                phi_443_ = 4f;
                            } else {
                                phi_443_ = select(10f, 7f, (_e301 == 1f));
                            }
                            let _e375 = phi_443_;
                            phi_451_ = _e375;
                            break;
                        }
                        if (_e301 == 0f) {
                            phi_450_ = 0f;
                        } else {
                            phi_450_ = select(7f, 4f, (_e301 == 1f));
                        }
                        let _e380 = phi_450_;
                        phi_451_ = _e380;
                        break;
                    }
                }
                let _e382 = phi_451_;
                phi_452_ = _e382;
            }
            let _e384 = phi_452_;
            switch bitcast<i32>(0u) {
                default: {
                    if (_e384 == 1f) {
                        phi_704_ = ((((_e349.x + (_e349.y * 2f)) % 6f) + 1f) * 0.14285715f);
                        break;
                    }
                    if (_e384 == 2f) {
                        let _e396 = ((_e349 % vec2<f32>(4f, 4f)) - vec2<f32>(1.5f, 1.5f));
                        phi_704_ = clamp(((dot(_e396, _e396) + 0.5f) * 0.2f), 0f, 1f);
                        break;
                    }
                    if (_e384 == 3f) {
                        phi_704_ = (((_e349.x % 4f) + 1f) * 0.2f);
                        break;
                    }
                    if (_e384 == 4f) {
                        phi_704_ = fract((sin(dot(floor(_e349), vec2<f32>(127.1f, 311.7f))) * 43758.547f));
                        break;
                    }
                    if (_e384 == 5f) {
                        phi_704_ = (((_e349.y % 4f) + 1f) * 0.2f);
                        break;
                    }
                    if (_e384 == 6f) {
                        phi_704_ = ((min(((_e349.x + _e349.y) % 4f), (((_e349.x - _e349.y) + 8f) % 4f)) + 1f) * 0.2f);
                        break;
                    }
                    if (_e384 == 7f) {
                        let _e429 = (_e349 % vec2<f32>(4f, 4f));
                        let _e438 = i32((_e349.x % 4f));
                        let _e441 = i32((_e349.y % 4f));
                        if (_e438 == 0i) {
                            if (_e441 == 0i) {
                                phi_540_ = 16f;
                            } else {
                                if (_e441 == 1i) {
                                    phi_539_ = 5f;
                                } else {
                                    phi_539_ = select(1f, 13f, (_e441 == 2i));
                                }
                                let _e448 = phi_539_;
                                phi_540_ = _e448;
                            }
                            let _e450 = phi_540_;
                            phi_587_ = _e450;
                        } else {
                            if (_e438 == 1i) {
                                if (_e441 == 0i) {
                                    phi_556_ = 8f;
                                } else {
                                    if (_e441 == 1i) {
                                        phi_555_ = 12f;
                                    } else {
                                        phi_555_ = select(9f, 4f, (_e441 == 2i));
                                    }
                                    let _e457 = phi_555_;
                                    phi_556_ = _e457;
                                }
                                let _e459 = phi_556_;
                                phi_586_ = _e459;
                            } else {
                                if (_e438 == 2i) {
                                    if (_e441 == 0i) {
                                        phi_572_ = 14f;
                                    } else {
                                        if (_e441 == 1i) {
                                            phi_571_ = 2f;
                                        } else {
                                            phi_571_ = select(3f, 15f, (_e441 == 2i));
                                        }
                                        let _e466 = phi_571_;
                                        phi_572_ = _e466;
                                    }
                                    let _e468 = phi_572_;
                                    phi_585_ = _e468;
                                } else {
                                    if (_e441 == 0i) {
                                        phi_584_ = 6f;
                                    } else {
                                        if (_e441 == 1i) {
                                            phi_583_ = 10f;
                                        } else {
                                            phi_583_ = select(11f, 7f, (_e441 == 2i));
                                        }
                                        let _e474 = phi_583_;
                                        phi_584_ = _e474;
                                    }
                                    let _e476 = phi_584_;
                                    phi_585_ = _e476;
                                }
                                let _e478 = phi_585_;
                                phi_586_ = _e478;
                            }
                            let _e480 = phi_586_;
                            phi_587_ = _e480;
                        }
                        let _e482 = phi_587_;
                        if (((floor(_e429.x) + floor(_e429.y)) % 2f) == 0f) {
                            phi_595_ = (_e482 * 0.04411765f);
                        } else {
                            phi_595_ = (0.35f + (_e482 * 0.03823529f));
                        }
                        let _e488 = phi_595_;
                        phi_704_ = _e488;
                        break;
                    }
                    if (_e384 == 8f) {
                        phi_704_ = clamp(((min(((_e349.x + ((floor((_e349.y * 0.5f)) % 2f) * 2f)) % 4f), (_e349.y % 4f)) + 1f) * 0.2f), 0f, 1f);
                        break;
                    }
                    if (_e384 == 9f) {
                        phi_704_ = (((((_e349.x * 2f) + _e349.y) % 8f) + 1f) * 0.11111111f);
                        break;
                    }
                    if (_e384 >= 10f) {
                        let _e514 = abs(((_e349 % vec2<f32>(8f, 8f)) - vec2<f32>(3.5f, 3.5f)));
                        phi_704_ = clamp(((max(_e514.x, _e514.y) - 0.5f) * 0.2857143f), 0f, 1f);
                        break;
                    }
                    let _e523 = i32((_e349.x % 4f));
                    let _e526 = i32((_e349.y % 4f));
                    if (_e523 == 0i) {
                        if (_e526 == 0i) {
                            phi_655_ = 16f;
                        } else {
                            if (_e526 == 1i) {
                                phi_654_ = 5f;
                            } else {
                                phi_654_ = select(1f, 13f, (_e526 == 2i));
                            }
                            let _e533 = phi_654_;
                            phi_655_ = _e533;
                        }
                        let _e535 = phi_655_;
                        phi_702_ = _e535;
                    } else {
                        if (_e523 == 1i) {
                            if (_e526 == 0i) {
                                phi_671_ = 8f;
                            } else {
                                if (_e526 == 1i) {
                                    phi_670_ = 12f;
                                } else {
                                    phi_670_ = select(9f, 4f, (_e526 == 2i));
                                }
                                let _e542 = phi_670_;
                                phi_671_ = _e542;
                            }
                            let _e544 = phi_671_;
                            phi_701_ = _e544;
                        } else {
                            if (_e523 == 2i) {
                                if (_e526 == 0i) {
                                    phi_687_ = 14f;
                                } else {
                                    if (_e526 == 1i) {
                                        phi_686_ = 2f;
                                    } else {
                                        phi_686_ = select(3f, 15f, (_e526 == 2i));
                                    }
                                    let _e551 = phi_686_;
                                    phi_687_ = _e551;
                                }
                                let _e553 = phi_687_;
                                phi_700_ = _e553;
                            } else {
                                if (_e526 == 0i) {
                                    phi_699_ = 6f;
                                } else {
                                    if (_e526 == 1i) {
                                        phi_698_ = 10f;
                                    } else {
                                        phi_698_ = select(11f, 7f, (_e526 == 2i));
                                    }
                                    let _e559 = phi_698_;
                                    phi_699_ = _e559;
                                }
                                let _e561 = phi_699_;
                                phi_700_ = _e561;
                            }
                            let _e563 = phi_700_;
                            phi_701_ = _e563;
                        }
                        let _e565 = phi_701_;
                        phi_702_ = _e565;
                    }
                    let _e567 = phi_702_;
                    phi_704_ = (_e567 * 0.05882353f);
                    break;
                }
            }
            let _e570 = phi_704_;
            let _e580 = clamp((clamp((_e570 + ((fract((_e219 * 0.618034f)) - 0.5f) * 0.1f)), 0f, 1f) + ((_e290 * _e260) * 0.18f)), 0f, 1f);
            if (_e194 > 0f) {
                let _e588 = FragmentUniformBlock.DitherSettings[2i];
                phi_726_ = (_e588 * _e121.x);
            } else {
                phi_726_ = 0f;
            }
            let _e591 = phi_726_;
            let _e594 = FragmentUniformBlock.NormalDitherSettings[0i];
            let _e599 = (_e190 * mix(1f, (select(1f, 0f, (clamp(_e212, 0f, 1f) < _e580)) / _e212), clamp((_e591 + (_e131 * _e594)), 0f, 1f)));
            let _e602 = FragmentUniformBlock.NormalDitherSettings[1i];
            phi_740_ = false;
            if (_e602 > 1f) {
                phi_740_ = (_e131 > 0f);
            }
            let _e606 = phi_740_;
            phi_758_ = _e599;
            if _e606 {
                let _e611 = max(max(_e599.x, _e599.y), _e599.z);
                let _e612 = (_e602 - 1f);
                phi_758_ = (_e599 * mix(1f, ((floor(((clamp(_e611, 0f, 1f) * _e612) + _e580)) / _e612) / max(_e611, 0.0001f)), _e131));
            }
            let _e623 = phi_758_;
            phi_759_ = _e623;
            break;
        }
    }
    let _e625 = phi_759_;
    let _e638 = _e625.xyz;
    let _e646 = clamp(mix(((_e118.xyz * _e638) + (_e638 * ((_e189.w * clamp((1f - (max(max(_e118.x, _e118.y), _e118.z) * 2.2f)), 0f, 1f)) * 0.18f))), _e123.xyz, vec3(_e123.w)), vec3<f32>(0f, 0f, 0f), vec3<f32>(1f, 1f, 1f));
    let _e652 = textureSample(ForegroundTexture, ForegroundSampler, _e116);
    let _e653 = (_e652 * _e107);
    let _e654 = textureSample(ForegroundLightTexture, ForegroundLightSampler, _e116);
    let _e655 = textureSample(ForegroundEmissiveTexture, ForegroundEmissiveSampler, _e116);
    let _e656 = _e654.xyz;
    switch bitcast<i32>(0u) {
        default: {
            let _e659 = local;
            phi_807_ = false;
            if _e659 {
                let _e662 = FragmentUniformBlock.NormalDitherSettings[0i];
                phi_807_ = (_e662 <= 0f);
            }
            let _e665 = phi_807_;
            phi_813_ = false;
            if _e665 {
                let _e668 = FragmentUniformBlock.NormalDitherSettings[1i];
                phi_813_ = (_e668 <= 1f);
            }
            let _e671 = phi_813_;
            if _e671 {
                phi_1322_ = _e656;
                break;
            }
            let _e676 = max(max(_e654.x, _e654.y), _e654.z);
            if (_e676 <= 0.0001f) {
                phi_1322_ = _e656;
                break;
            }
            let _e679 = floor((_e153 * 0.03125f));
            let _e683 = floor(((_e120.x * 255f) + 0.5f));
            let _e686 = FragmentUniformBlock.DitherSettings[3i];
            let _e690 = ((_e686 * _e686) * (3f - (2f * _e686)));
            let _e694 = FragmentUniformBlock.DitherGlitch[2i];
            let _e696 = mix(24f, (_e694 * 1.45f), _e690);
            let _e698 = FragmentUniformBlock.DitherGlitch;
            let _e700 = FragmentUniformBlock.DitherGlitchAxis;
            let _e702 = normalize(_e700.xy);
            let _e707 = (_e149 - _e698.xy);
            let _e710 = (dot(_e707, _e702) / max(_e696, 1f));
            let _e713 = (dot(_e707, vec2<f32>(-(_e702.y), _e702.x)) / max((_e696 * 0.38f), 1f));
            let _e724 = (((_e690 * smoothstep(1f, 0.25f, sqrt(((_e710 * _e710) + (_e713 * _e713))))) * 0.82f) + ((_e690 * smoothstep(0.6f, 1f, _e690)) * 0.18f));
            let _e727 = FragmentUniformBlock.DitherGlitch[3i];
            let _e729 = floor((_e727 * 9f));
            let _e731 = floor((_e160 * vec2<f32>(0.0625f, 0.0625f)));
            let _e753 = fract((sin(((((_e683 * 19.19f) + (_e731.x * 7.13f)) + (_e731.y * 3.71f)) + (_e729 * 5.17f))) * 43758.547f));
            let _e754 = (fract((sin(((((_e731.x * 17f) + (_e731.y * 31f)) + (_e729 * 13f)) + (_e683 * 0.23f))) * 43758.547f)) - 0.5f);
            let _e760 = (_e679 == 2f);
            if _e760 {
                phi_909_ = 2f;
            } else {
                phi_909_ = select(1f, 3f, (_e679 == 3f));
            }
            let _e764 = phi_909_;
            let _e765 = (_e683 % 3f);
            let _e770 = FragmentUniformBlock.DitherSettings[1i];
            let _e776 = floor(((_e160 + ((vec2<f32>(_e754, (_e753 - 0.5f)) * _e724) * 9f)) / vec2(max(((_e770 * _e764) * (1f + (_e765 * 0.08f))), 1f))));
            let _e777 = (_e679 == 1f);
            if _e777 {
                phi_947_ = vec2<f32>(-(_e776.x), _e776.y);
            } else {
                if _e760 {
                    phi_946_ = _e776.yx;
                } else {
                    if (_e679 == 3f) {
                        phi_945_ = vec2<f32>(_e776.x, -(_e776.y));
                    } else {
                        phi_944_ = _e776;
                        if (_e679 >= 4f) {
                            phi_944_ = -(_e776.yx);
                        }
                        let _e794 = phi_944_;
                        phi_945_ = _e794;
                    }
                    let _e796 = phi_945_;
                    phi_946_ = _e796;
                }
                let _e798 = phi_946_;
                phi_947_ = _e798;
            }
            let _e800 = phi_947_;
            let _e813 = (_e800 + (vec2<f32>(((_e679 * 2f) % 4f), ((_e679 * 3f) % 4f)) + (vec2<f32>((_e683 % 4f), (floor((_e683 * 0.25f)) % 4f)) * 0.35f)));
            if (_e724 > 0.65f) {
                phi_1015_ = floor((_e753 * 11f));
            } else {
                switch bitcast<i32>(0u) {
                    default: {
                        if _e777 {
                            if (_e765 == 0f) {
                                phi_977_ = 8f;
                            } else {
                                phi_977_ = select(5f, 6f, (_e765 == 1f));
                            }
                            let _e822 = phi_977_;
                            phi_1014_ = _e822;
                            break;
                        }
                        if _e760 {
                            if (_e765 == 0f) {
                                phi_986_ = 2f;
                            } else {
                                phi_986_ = select(10f, 9f, (_e765 == 1f));
                            }
                            let _e827 = phi_986_;
                            phi_1014_ = _e827;
                            break;
                        }
                        if (_e679 == 3f) {
                            if (_e765 == 0f) {
                                phi_996_ = 3f;
                            } else {
                                phi_996_ = select(1f, 5f, (_e765 == 1f));
                            }
                            let _e833 = phi_996_;
                            phi_1014_ = _e833;
                            break;
                        }
                        if (_e679 >= 4f) {
                            if (_e765 == 0f) {
                                phi_1006_ = 4f;
                            } else {
                                phi_1006_ = select(10f, 7f, (_e765 == 1f));
                            }
                            let _e839 = phi_1006_;
                            phi_1014_ = _e839;
                            break;
                        }
                        if (_e765 == 0f) {
                            phi_1013_ = 0f;
                        } else {
                            phi_1013_ = select(7f, 4f, (_e765 == 1f));
                        }
                        let _e844 = phi_1013_;
                        phi_1014_ = _e844;
                        break;
                    }
                }
                let _e846 = phi_1014_;
                phi_1015_ = _e846;
            }
            let _e848 = phi_1015_;
            switch bitcast<i32>(0u) {
                default: {
                    if (_e848 == 1f) {
                        phi_1267_ = ((((_e813.x + (_e813.y * 2f)) % 6f) + 1f) * 0.14285715f);
                        break;
                    }
                    if (_e848 == 2f) {
                        let _e860 = ((_e813 % vec2<f32>(4f, 4f)) - vec2<f32>(1.5f, 1.5f));
                        phi_1267_ = clamp(((dot(_e860, _e860) + 0.5f) * 0.2f), 0f, 1f);
                        break;
                    }
                    if (_e848 == 3f) {
                        phi_1267_ = (((_e813.x % 4f) + 1f) * 0.2f);
                        break;
                    }
                    if (_e848 == 4f) {
                        phi_1267_ = fract((sin(dot(floor(_e813), vec2<f32>(127.1f, 311.7f))) * 43758.547f));
                        break;
                    }
                    if (_e848 == 5f) {
                        phi_1267_ = (((_e813.y % 4f) + 1f) * 0.2f);
                        break;
                    }
                    if (_e848 == 6f) {
                        phi_1267_ = ((min(((_e813.x + _e813.y) % 4f), (((_e813.x - _e813.y) + 8f) % 4f)) + 1f) * 0.2f);
                        break;
                    }
                    if (_e848 == 7f) {
                        let _e893 = (_e813 % vec2<f32>(4f, 4f));
                        let _e902 = i32((_e813.x % 4f));
                        let _e905 = i32((_e813.y % 4f));
                        if (_e902 == 0i) {
                            if (_e905 == 0i) {
                                phi_1103_ = 16f;
                            } else {
                                if (_e905 == 1i) {
                                    phi_1102_ = 5f;
                                } else {
                                    phi_1102_ = select(1f, 13f, (_e905 == 2i));
                                }
                                let _e912 = phi_1102_;
                                phi_1103_ = _e912;
                            }
                            let _e914 = phi_1103_;
                            phi_1150_ = _e914;
                        } else {
                            if (_e902 == 1i) {
                                if (_e905 == 0i) {
                                    phi_1119_ = 8f;
                                } else {
                                    if (_e905 == 1i) {
                                        phi_1118_ = 12f;
                                    } else {
                                        phi_1118_ = select(9f, 4f, (_e905 == 2i));
                                    }
                                    let _e921 = phi_1118_;
                                    phi_1119_ = _e921;
                                }
                                let _e923 = phi_1119_;
                                phi_1149_ = _e923;
                            } else {
                                if (_e902 == 2i) {
                                    if (_e905 == 0i) {
                                        phi_1135_ = 14f;
                                    } else {
                                        if (_e905 == 1i) {
                                            phi_1134_ = 2f;
                                        } else {
                                            phi_1134_ = select(3f, 15f, (_e905 == 2i));
                                        }
                                        let _e930 = phi_1134_;
                                        phi_1135_ = _e930;
                                    }
                                    let _e932 = phi_1135_;
                                    phi_1148_ = _e932;
                                } else {
                                    if (_e905 == 0i) {
                                        phi_1147_ = 6f;
                                    } else {
                                        if (_e905 == 1i) {
                                            phi_1146_ = 10f;
                                        } else {
                                            phi_1146_ = select(11f, 7f, (_e905 == 2i));
                                        }
                                        let _e938 = phi_1146_;
                                        phi_1147_ = _e938;
                                    }
                                    let _e940 = phi_1147_;
                                    phi_1148_ = _e940;
                                }
                                let _e942 = phi_1148_;
                                phi_1149_ = _e942;
                            }
                            let _e944 = phi_1149_;
                            phi_1150_ = _e944;
                        }
                        let _e946 = phi_1150_;
                        if (((floor(_e893.x) + floor(_e893.y)) % 2f) == 0f) {
                            phi_1158_ = (_e946 * 0.04411765f);
                        } else {
                            phi_1158_ = (0.35f + (_e946 * 0.03823529f));
                        }
                        let _e952 = phi_1158_;
                        phi_1267_ = _e952;
                        break;
                    }
                    if (_e848 == 8f) {
                        phi_1267_ = clamp(((min(((_e813.x + ((floor((_e813.y * 0.5f)) % 2f) * 2f)) % 4f), (_e813.y % 4f)) + 1f) * 0.2f), 0f, 1f);
                        break;
                    }
                    if (_e848 == 9f) {
                        phi_1267_ = (((((_e813.x * 2f) + _e813.y) % 8f) + 1f) * 0.11111111f);
                        break;
                    }
                    if (_e848 >= 10f) {
                        let _e978 = abs(((_e813 % vec2<f32>(8f, 8f)) - vec2<f32>(3.5f, 3.5f)));
                        phi_1267_ = clamp(((max(_e978.x, _e978.y) - 0.5f) * 0.2857143f), 0f, 1f);
                        break;
                    }
                    let _e987 = i32((_e813.x % 4f));
                    let _e990 = i32((_e813.y % 4f));
                    if (_e987 == 0i) {
                        if (_e990 == 0i) {
                            phi_1218_ = 16f;
                        } else {
                            if (_e990 == 1i) {
                                phi_1217_ = 5f;
                            } else {
                                phi_1217_ = select(1f, 13f, (_e990 == 2i));
                            }
                            let _e997 = phi_1217_;
                            phi_1218_ = _e997;
                        }
                        let _e999 = phi_1218_;
                        phi_1265_ = _e999;
                    } else {
                        if (_e987 == 1i) {
                            if (_e990 == 0i) {
                                phi_1234_ = 8f;
                            } else {
                                if (_e990 == 1i) {
                                    phi_1233_ = 12f;
                                } else {
                                    phi_1233_ = select(9f, 4f, (_e990 == 2i));
                                }
                                let _e1006 = phi_1233_;
                                phi_1234_ = _e1006;
                            }
                            let _e1008 = phi_1234_;
                            phi_1264_ = _e1008;
                        } else {
                            if (_e987 == 2i) {
                                if (_e990 == 0i) {
                                    phi_1250_ = 14f;
                                } else {
                                    if (_e990 == 1i) {
                                        phi_1249_ = 2f;
                                    } else {
                                        phi_1249_ = select(3f, 15f, (_e990 == 2i));
                                    }
                                    let _e1015 = phi_1249_;
                                    phi_1250_ = _e1015;
                                }
                                let _e1017 = phi_1250_;
                                phi_1263_ = _e1017;
                            } else {
                                if (_e990 == 0i) {
                                    phi_1262_ = 6f;
                                } else {
                                    if (_e990 == 1i) {
                                        phi_1261_ = 10f;
                                    } else {
                                        phi_1261_ = select(11f, 7f, (_e990 == 2i));
                                    }
                                    let _e1023 = phi_1261_;
                                    phi_1262_ = _e1023;
                                }
                                let _e1025 = phi_1262_;
                                phi_1263_ = _e1025;
                            }
                            let _e1027 = phi_1263_;
                            phi_1264_ = _e1027;
                        }
                        let _e1029 = phi_1264_;
                        phi_1265_ = _e1029;
                    }
                    let _e1031 = phi_1265_;
                    phi_1267_ = (_e1031 * 0.05882353f);
                    break;
                }
            }
            let _e1034 = phi_1267_;
            let _e1044 = clamp((clamp((_e1034 + ((fract((_e683 * 0.618034f)) - 0.5f) * 0.1f)), 0f, 1f) + ((_e754 * _e724) * 0.18f)), 0f, 1f);
            let _e1050 = local_1;
            if (_e1050 > 0f) {
                let _e1054 = FragmentUniformBlock.DitherSettings[2i];
                phi_1289_ = (_e1054 * _e121.x);
            } else {
                phi_1289_ = 0f;
            }
            let _e1057 = phi_1289_;
            let _e1060 = FragmentUniformBlock.NormalDitherSettings[0i];
            let _e1065 = (_e656 * mix(1f, (select(1f, 0f, (clamp(_e676, 0f, 1f) < _e1044)) / _e676), clamp((_e1057 + (_e131 * _e1060)), 0f, 1f)));
            let _e1068 = FragmentUniformBlock.NormalDitherSettings[1i];
            phi_1303_ = false;
            if (_e1068 > 1f) {
                phi_1303_ = (_e131 > 0f);
            }
            let _e1072 = phi_1303_;
            phi_1321_ = _e1065;
            if _e1072 {
                let _e1077 = max(max(_e1065.x, _e1065.y), _e1065.z);
                let _e1078 = (_e1068 - 1f);
                phi_1321_ = (_e1065 * mix(1f, ((floor(((clamp(_e1077, 0f, 1f) * _e1078) + _e1044)) / _e1078) / max(_e1077, 0.0001f)), _e131));
            }
            let _e1089 = phi_1321_;
            phi_1322_ = _e1089;
            break;
        }
    }
    let _e1091 = phi_1322_;
    let _e1104 = _e1091.xyz;
    let _e1112 = clamp(mix(((_e653.xyz * _e1104) + (_e1104 * ((_e654.w * clamp((1f - (max(max(_e653.x, _e653.y), _e653.z) * 2.2f)), 0f, 1f)) * 0.18f))), _e655.xyz, vec3(_e655.w)), vec3<f32>(0f, 0f, 0f), vec3<f32>(1f, 1f, 1f));
    out_u002e_var_u002e_SV_Target0_ = clamp((vec4<f32>(_e1112.x, _e1112.y, _e1112.z, _e653.w) + (vec4<f32>(_e646.x, _e646.y, _e646.z, _e118.w) * (1f - _e653.w))), vec4<f32>(0f, 0f, 0f, 0f), vec4<f32>(1f, 1f, 1f, 1f));
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
