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
@group(2) @binding(11) 
var SporeFogTexture: texture_2d<f32>;
@group(2) @binding(111) 
var SporeFogSampler: sampler;
@group(2) @binding(12)
var ActorTileFieldTexture: texture_2d<f32>;
@group(2) @binding(112)
var ActorTileFieldSampler: sampler;
var<private> in_u002e_var_u002e_TEXCOORD0_1: vec2<f32>;
var<private> in_u002e_var_u002e_TEXCOORD1_1: vec4<f32>;
var<private> in_u002e_var_u002e_TEXCOORD4_1: vec4<f32>;
var<private> global: vec4<f32>;
var<private> out_u002e_var_u002e_SV_Target0_: vec4<f32>;

fn fragment_main_1() {
    var phi_252_: vec4<f32>;
    var phi_264_: bool;
    var phi_270_: bool;
    var phi_370_: f32;
    var phi_405_: vec2<f32>;
    var phi_406_: vec2<f32>;
    var phi_407_: vec2<f32>;
    var phi_408_: vec2<f32>;
    var phi_438_: f32;
    var phi_447_: f32;
    var phi_457_: f32;
    var phi_476_: f32;
    var phi_483_: f32;
    var phi_484_: f32;
    var phi_485_: f32;
    var phi_572_: f32;
    var phi_573_: f32;
    var phi_588_: f32;
    var phi_589_: f32;
    var phi_604_: f32;
    var phi_605_: f32;
    var phi_616_: f32;
    var phi_617_: f32;
    var phi_618_: f32;
    var phi_619_: f32;
    var phi_620_: f32;
    var phi_628_: f32;
    var phi_687_: f32;
    var phi_688_: f32;
    var phi_703_: f32;
    var phi_704_: f32;
    var phi_719_: f32;
    var phi_720_: f32;
    var phi_731_: f32;
    var phi_732_: f32;
    var phi_733_: f32;
    var phi_734_: f32;
    var phi_735_: f32;
    var phi_737_: f32;
    var phi_762_: f32;
    var phi_776_: bool;
    var phi_794_: vec3<f32>;
    var phi_795_: vec3<f32>;
    var local: bool;
    var phi_852_: bool;
    var phi_858_: bool;
    var phi_954_: f32;
    var phi_989_: vec2<f32>;
    var phi_990_: vec2<f32>;
    var phi_991_: vec2<f32>;
    var phi_992_: vec2<f32>;
    var phi_1022_: f32;
    var phi_1031_: f32;
    var phi_1041_: f32;
    var phi_1060_: f32;
    var phi_1067_: f32;
    var phi_1068_: f32;
    var phi_1069_: f32;
    var phi_1156_: f32;
    var phi_1157_: f32;
    var phi_1172_: f32;
    var phi_1173_: f32;
    var phi_1188_: f32;
    var phi_1189_: f32;
    var phi_1200_: f32;
    var phi_1201_: f32;
    var phi_1202_: f32;
    var phi_1203_: f32;
    var phi_1204_: f32;
    var phi_1212_: f32;
    var phi_1271_: f32;
    var phi_1272_: f32;
    var phi_1287_: f32;
    var phi_1288_: f32;
    var phi_1303_: f32;
    var phi_1304_: f32;
    var phi_1315_: f32;
    var phi_1316_: f32;
    var phi_1317_: f32;
    var phi_1318_: f32;
    var phi_1319_: f32;
    var phi_1321_: f32;
    var local_1: f32;
    var phi_1344_: f32;
    var phi_1358_: bool;
    var phi_1376_: vec3<f32>;
    var phi_1377_: vec3<f32>;

    let _e111 = in_u002e_var_u002e_TEXCOORD0_1;
    let _e112 = in_u002e_var_u002e_TEXCOORD1_1;
    let _e113 = in_u002e_var_u002e_TEXCOORD4_1;
    let _e114 = global;
    if (_e113.x < -1000f) {
        discard;
    }
    let _e117 = textureSample(Texture, Sampler, _e111);
    let _e118 = (_e117 * _e112);
    let _e119 = textureSample(LightTexture, LightSampler, _e111);
    let _e120 = textureSample(TileFieldTexture, TileFieldSampler, _e111);
    let _e121 = textureSample(ActorTileFieldTexture, ActorTileFieldSampler, _e111);
    let _e122 = textureSample(DitherMaskTexture, DitherMaskSampler, _e111);
    let _e124 = textureSample(EmissiveTexture, EmissiveSampler, _e111);
    let _e125 = textureSample(NormalTexture, NormalSampler, _e111);
    let _e132 = (_e125.w * clamp(length(((_e125.xy * 2f) - vec2<f32>(1f, 1f))), 0f, 1f));
    let _e136 = FragmentUniformBlock.DitherGlitchAxis[2i];
    let _e140 = FragmentUniformBlock.TextureSizeCamera;
    let _e145 = (_e140.zw + (_e114.xy / vec2(select(1f, _e136, (_e136 > 0f)))));
    let _e149 = floor(((_e120.y * 255f) + 0.5f));
    let _e154 = FragmentUniformBlock.DitherAnchorOffsets[i32((_e149 % 32f))];
    let _e156 = (_e145 + _e154.xy);
    let _e158 = step(0.5f, _e121.w);
    let _e160 = mix(_e120, _e121, vec4(_e158));
    let _e172 = mix(_e156, (_e145 / vec2(exp2((((floor(((_e121.y * 255f) + 0.5f)) % 32f) - 16f) * 0.125f)))), vec2(_e158));
    let _e178 = FragmentUniformBlock.AoSettings[0i];
    phi_252_ = _e119;
    if (_e178 > 0f) {
        let _e180 = FragmentUniformBlock.AoSettings;
        let _e185 = textureSample(AoTexture, AoSampler, (_e111 + (_e180.yz / _e140.xy)));
        let _e189 = textureSample(BackgroundCoverageTexture, BackgroundCoverageSampler, _e111);
        let _e193 = FragmentUniformBlock.AoSettings[3i];
        let _e197 = (_e119.xyz * mix(1f, _e193, (clamp((_e185.x * _e178), 0f, 1f) * _e189.x)));
        phi_252_ = vec4<f32>(_e197.x, _e197.y, _e197.z, _e119.w);
    }
    let _e204 = phi_252_;
    let _e205 = _e204.xyz;
    switch bitcast<i32>(0u) {
        default: {
            let _e209 = FragmentUniformBlock.DitherSettings[0i];
            let _e210 = (_e209 <= 0f);
            phi_264_ = false;
            local = _e210;
            local_1 = _e209;
            if _e210 {
                let _e213 = FragmentUniformBlock.NormalDitherSettings[0i];
                phi_264_ = (_e213 <= 0f);
            }
            let _e216 = phi_264_;
            phi_270_ = false;
            if _e216 {
                let _e219 = FragmentUniformBlock.NormalDitherSettings[1i];
                phi_270_ = (_e219 <= 1f);
            }
            let _e222 = phi_270_;
            if _e222 {
                phi_795_ = _e205;
                break;
            }
            let _e227 = max(max(_e204.x, _e204.y), _e204.z);
            if (_e227 <= 0.0001f) {
                phi_795_ = _e205;
                break;
            }
            let _e234 = floor((floor(((_e160.y * 255f) + 0.5f)) * 0.03125f));
            let _e238 = floor(((_e160.x * 255f) + 0.5f));
            let _e241 = FragmentUniformBlock.DitherSettings[3i];
            let _e245 = ((_e241 * _e241) * (3f - (2f * _e241)));
            let _e249 = FragmentUniformBlock.DitherGlitch[2i];
            let _e251 = mix(24f, (_e249 * 1.45f), _e245);
            let _e253 = FragmentUniformBlock.DitherGlitch;
            let _e255 = FragmentUniformBlock.DitherGlitchAxis;
            let _e257 = normalize(_e255.xy);
            let _e262 = (_e145 - _e253.xy);
            let _e265 = (dot(_e262, _e257) / max(_e251, 1f));
            let _e268 = (dot(_e262, vec2<f32>(-(_e257.y), _e257.x)) / max((_e251 * 0.38f), 1f));
            let _e279 = (((_e245 * smoothstep(1f, 0.25f, sqrt(((_e265 * _e265) + (_e268 * _e268))))) * 0.82f) + ((_e245 * smoothstep(0.6f, 1f, _e245)) * 0.18f));
            let _e282 = FragmentUniformBlock.DitherGlitch[3i];
            let _e284 = floor((_e282 * 9f));
            let _e286 = floor((_e172 * vec2<f32>(0.0625f, 0.0625f)));
            let _e308 = fract((sin(((((_e238 * 19.19f) + (_e286.x * 7.13f)) + (_e286.y * 3.71f)) + (_e284 * 5.17f))) * 43758.547f));
            let _e309 = (fract((sin(((((_e286.x * 17f) + (_e286.y * 31f)) + (_e284 * 13f)) + (_e238 * 0.23f))) * 43758.547f)) - 0.5f);
            let _e315 = (_e234 == 2f);
            if _e315 {
                phi_370_ = 2f;
            } else {
                phi_370_ = select(1f, 3f, (_e234 == 3f));
            }
            let _e319 = phi_370_;
            let _e320 = (_e238 % 3f);
            let _e325 = FragmentUniformBlock.DitherSettings[1i];
            let _e331 = floor(((_e172 + ((vec2<f32>(_e309, (_e308 - 0.5f)) * _e279) * 9f)) / vec2(max(((_e325 * _e319) * (1f + (_e320 * 0.08f))), 1f))));
            let _e332 = (_e234 == 1f);
            if _e332 {
                phi_408_ = vec2<f32>(-(_e331.x), _e331.y);
            } else {
                if _e315 {
                    phi_407_ = _e331.yx;
                } else {
                    if (_e234 == 3f) {
                        phi_406_ = vec2<f32>(_e331.x, -(_e331.y));
                    } else {
                        phi_405_ = _e331;
                        if (_e234 >= 4f) {
                            phi_405_ = -(_e331.yx);
                        }
                        let _e349 = phi_405_;
                        phi_406_ = _e349;
                    }
                    let _e351 = phi_406_;
                    phi_407_ = _e351;
                }
                let _e353 = phi_407_;
                phi_408_ = _e353;
            }
            let _e355 = phi_408_;
            let _e368 = (_e355 + (vec2<f32>(((_e234 * 2f) % 4f), ((_e234 * 3f) % 4f)) + (vec2<f32>((_e238 % 4f), (floor((_e238 * 0.25f)) % 4f)) * 0.35f)));
            if (_e279 > 0.65f) {
                phi_485_ = floor((_e308 * 11f));
            } else {
                switch bitcast<i32>(0u) {
                    default: {
                        if _e332 {
                            if (_e320 == 0f) {
                                phi_438_ = 8f;
                            } else {
                                phi_438_ = select(5f, 6f, (_e320 == 1f));
                            }
                            let _e377 = phi_438_;
                            phi_484_ = _e377;
                            break;
                        }
                        if _e315 {
                            if (_e320 == 0f) {
                                phi_447_ = 2f;
                            } else {
                                phi_447_ = select(10f, 9f, (_e320 == 1f));
                            }
                            let _e382 = phi_447_;
                            phi_484_ = _e382;
                            break;
                        }
                        if (_e234 == 3f) {
                            if (_e320 == 0f) {
                                phi_457_ = 3f;
                            } else {
                                phi_457_ = select(1f, 5f, (_e320 == 1f));
                            }
                            let _e388 = phi_457_;
                            phi_484_ = _e388;
                            break;
                        }
                        if (_e234 == 5f) {
                            phi_484_ = 0f;
                            break;
                        }
                        if (_e234 == 6f) {
                            phi_484_ = 6f;
                            break;
                        }
                        if (_e234 >= 7f) {
                            phi_484_ = 4f;
                            break;
                        }
                        if (_e234 >= 4f) {
                            if (_e320 == 0f) {
                                phi_476_ = 4f;
                            } else {
                                phi_476_ = select(10f, 7f, (_e320 == 1f));
                            }
                            let _e397 = phi_476_;
                            phi_484_ = _e397;
                            break;
                        }
                        if (_e320 == 0f) {
                            phi_483_ = 0f;
                        } else {
                            phi_483_ = select(7f, 4f, (_e320 == 1f));
                        }
                        let _e402 = phi_483_;
                        phi_484_ = _e402;
                        break;
                    }
                }
                let _e404 = phi_484_;
                phi_485_ = _e404;
            }
            let _e406 = phi_485_;
            switch bitcast<i32>(0u) {
                default: {
                    if (_e406 == 1f) {
                        phi_737_ = ((((_e368.x + (_e368.y * 2f)) % 6f) + 1f) * 0.14285715f);
                        break;
                    }
                    if (_e406 == 2f) {
                        let _e418 = ((_e368 % vec2<f32>(4f, 4f)) - vec2<f32>(1.5f, 1.5f));
                        phi_737_ = clamp(((dot(_e418, _e418) + 0.5f) * 0.2f), 0f, 1f);
                        break;
                    }
                    if (_e406 == 3f) {
                        phi_737_ = (((_e368.x % 4f) + 1f) * 0.2f);
                        break;
                    }
                    if (_e406 == 4f) {
                        phi_737_ = fract((sin(dot(floor(_e368), vec2<f32>(127.1f, 311.7f))) * 43758.547f));
                        break;
                    }
                    if (_e406 == 5f) {
                        phi_737_ = (((_e368.y % 4f) + 1f) * 0.2f);
                        break;
                    }
                    if (_e406 == 6f) {
                        phi_737_ = ((min(((_e368.x + _e368.y) % 4f), (((_e368.x - _e368.y) + 8f) % 4f)) + 1f) * 0.2f);
                        break;
                    }
                    if (_e406 == 7f) {
                        let _e451 = (_e368 % vec2<f32>(4f, 4f));
                        let _e460 = i32((_e368.x % 4f));
                        let _e463 = i32((_e368.y % 4f));
                        if (_e460 == 0i) {
                            if (_e463 == 0i) {
                                phi_573_ = 16f;
                            } else {
                                if (_e463 == 1i) {
                                    phi_572_ = 5f;
                                } else {
                                    phi_572_ = select(1f, 13f, (_e463 == 2i));
                                }
                                let _e470 = phi_572_;
                                phi_573_ = _e470;
                            }
                            let _e472 = phi_573_;
                            phi_620_ = _e472;
                        } else {
                            if (_e460 == 1i) {
                                if (_e463 == 0i) {
                                    phi_589_ = 8f;
                                } else {
                                    if (_e463 == 1i) {
                                        phi_588_ = 12f;
                                    } else {
                                        phi_588_ = select(9f, 4f, (_e463 == 2i));
                                    }
                                    let _e479 = phi_588_;
                                    phi_589_ = _e479;
                                }
                                let _e481 = phi_589_;
                                phi_619_ = _e481;
                            } else {
                                if (_e460 == 2i) {
                                    if (_e463 == 0i) {
                                        phi_605_ = 14f;
                                    } else {
                                        if (_e463 == 1i) {
                                            phi_604_ = 2f;
                                        } else {
                                            phi_604_ = select(3f, 15f, (_e463 == 2i));
                                        }
                                        let _e488 = phi_604_;
                                        phi_605_ = _e488;
                                    }
                                    let _e490 = phi_605_;
                                    phi_618_ = _e490;
                                } else {
                                    if (_e463 == 0i) {
                                        phi_617_ = 6f;
                                    } else {
                                        if (_e463 == 1i) {
                                            phi_616_ = 10f;
                                        } else {
                                            phi_616_ = select(11f, 7f, (_e463 == 2i));
                                        }
                                        let _e496 = phi_616_;
                                        phi_617_ = _e496;
                                    }
                                    let _e498 = phi_617_;
                                    phi_618_ = _e498;
                                }
                                let _e500 = phi_618_;
                                phi_619_ = _e500;
                            }
                            let _e502 = phi_619_;
                            phi_620_ = _e502;
                        }
                        let _e504 = phi_620_;
                        if (((floor(_e451.x) + floor(_e451.y)) % 2f) == 0f) {
                            phi_628_ = (_e504 * 0.04411765f);
                        } else {
                            phi_628_ = (0.35f + (_e504 * 0.03823529f));
                        }
                        let _e510 = phi_628_;
                        phi_737_ = _e510;
                        break;
                    }
                    if (_e406 == 8f) {
                        phi_737_ = clamp(((min(((_e368.x + ((floor((_e368.y * 0.5f)) % 2f) * 2f)) % 4f), (_e368.y % 4f)) + 1f) * 0.2f), 0f, 1f);
                        break;
                    }
                    if (_e406 == 9f) {
                        phi_737_ = (((((_e368.x * 2f) + _e368.y) % 8f) + 1f) * 0.11111111f);
                        break;
                    }
                    if (_e406 >= 10f) {
                        let _e536 = abs(((_e368 % vec2<f32>(8f, 8f)) - vec2<f32>(3.5f, 3.5f)));
                        phi_737_ = clamp(((max(_e536.x, _e536.y) - 0.5f) * 0.2857143f), 0f, 1f);
                        break;
                    }
                    let _e545 = i32((_e368.x % 4f));
                    let _e548 = i32((_e368.y % 4f));
                    if (_e545 == 0i) {
                        if (_e548 == 0i) {
                            phi_688_ = 16f;
                        } else {
                            if (_e548 == 1i) {
                                phi_687_ = 5f;
                            } else {
                                phi_687_ = select(1f, 13f, (_e548 == 2i));
                            }
                            let _e555 = phi_687_;
                            phi_688_ = _e555;
                        }
                        let _e557 = phi_688_;
                        phi_735_ = _e557;
                    } else {
                        if (_e545 == 1i) {
                            if (_e548 == 0i) {
                                phi_704_ = 8f;
                            } else {
                                if (_e548 == 1i) {
                                    phi_703_ = 12f;
                                } else {
                                    phi_703_ = select(9f, 4f, (_e548 == 2i));
                                }
                                let _e564 = phi_703_;
                                phi_704_ = _e564;
                            }
                            let _e566 = phi_704_;
                            phi_734_ = _e566;
                        } else {
                            if (_e545 == 2i) {
                                if (_e548 == 0i) {
                                    phi_720_ = 14f;
                                } else {
                                    if (_e548 == 1i) {
                                        phi_719_ = 2f;
                                    } else {
                                        phi_719_ = select(3f, 15f, (_e548 == 2i));
                                    }
                                    let _e573 = phi_719_;
                                    phi_720_ = _e573;
                                }
                                let _e575 = phi_720_;
                                phi_733_ = _e575;
                            } else {
                                if (_e548 == 0i) {
                                    phi_732_ = 6f;
                                } else {
                                    if (_e548 == 1i) {
                                        phi_731_ = 10f;
                                    } else {
                                        phi_731_ = select(11f, 7f, (_e548 == 2i));
                                    }
                                    let _e581 = phi_731_;
                                    phi_732_ = _e581;
                                }
                                let _e583 = phi_732_;
                                phi_733_ = _e583;
                            }
                            let _e585 = phi_733_;
                            phi_734_ = _e585;
                        }
                        let _e587 = phi_734_;
                        phi_735_ = _e587;
                    }
                    let _e589 = phi_735_;
                    phi_737_ = (_e589 * 0.05882353f);
                    break;
                }
            }
            let _e592 = phi_737_;
            let _e605 = clamp(((clamp((clamp((_e592 + ((fract((_e238 * 0.618034f)) - 0.5f) * 0.1f)), 0f, 1f) + ((_e309 * _e279) * 0.18f)), 0f, 1f) + 0.5f) - mix(0.5f, _e121.z, _e158)), 0f, 1f);
            if (_e209 > 0f) {
                let _e613 = FragmentUniformBlock.DitherSettings[2i];
                phi_762_ = (_e613 * _e122.x);
            } else {
                phi_762_ = 0f;
            }
            let _e616 = phi_762_;
            let _e619 = FragmentUniformBlock.NormalDitherSettings[0i];
            let _e624 = (_e205 * mix(1f, (select(1f, 0f, (clamp(_e227, 0f, 1f) < _e605)) / _e227), clamp((_e616 + (_e132 * _e619)), 0f, 1f)));
            let _e627 = FragmentUniformBlock.NormalDitherSettings[1i];
            phi_776_ = false;
            if (_e627 > 1f) {
                phi_776_ = (_e132 > 0f);
            }
            let _e631 = phi_776_;
            phi_794_ = _e624;
            if _e631 {
                let _e636 = max(max(_e624.x, _e624.y), _e624.z);
                let _e637 = (_e627 - 1f);
                phi_794_ = (_e624 * mix(1f, ((floor(((clamp(_e636, 0f, 1f) * _e637) + _e605)) / _e637) / max(_e636, 0.0001f)), _e132));
            }
            let _e648 = phi_794_;
            phi_795_ = _e648;
            break;
        }
    }
    let _e650 = phi_795_;
    let _e663 = _e650.xyz;
    let _e671 = clamp(mix(((_e118.xyz * _e663) + (_e663 * ((_e204.w * clamp((1f - (max(max(_e118.x, _e118.y), _e118.z) * 2.2f)), 0f, 1f)) * 0.18f))), _e124.xyz, vec3(_e124.w)), vec3<f32>(0f, 0f, 0f), vec3<f32>(1f, 1f, 1f));
    let _e676 = vec4<f32>(_e671.x, _e671.y, _e671.z, _e118.w);
    let _e678 = textureSample(SporeFogTexture, SporeFogSampler, _e111);
    let _e681 = clamp((_e676.xyz + _e678.xyz), vec3<f32>(0f, 0f, 0f), vec3<f32>(1f, 1f, 1f));
    let _e687 = textureSample(ForegroundTexture, ForegroundSampler, _e111);
    let _e688 = (_e687 * _e112);
    let _e689 = textureSample(ForegroundLightTexture, ForegroundLightSampler, _e111);
    let _e690 = textureSample(ForegroundEmissiveTexture, ForegroundEmissiveSampler, _e111);
    let _e691 = _e689.xyz;
    switch bitcast<i32>(0u) {
        default: {
            let _e694 = local;
            phi_852_ = false;
            if _e694 {
                let _e697 = FragmentUniformBlock.NormalDitherSettings[0i];
                phi_852_ = (_e697 <= 0f);
            }
            let _e700 = phi_852_;
            phi_858_ = false;
            if _e700 {
                let _e703 = FragmentUniformBlock.NormalDitherSettings[1i];
                phi_858_ = (_e703 <= 1f);
            }
            let _e706 = phi_858_;
            if _e706 {
                phi_1377_ = _e691;
                break;
            }
            let _e711 = max(max(_e689.x, _e689.y), _e689.z);
            if (_e711 <= 0.0001f) {
                phi_1377_ = _e691;
                break;
            }
            let _e714 = floor((_e149 * 0.03125f));
            let _e718 = floor(((_e120.x * 255f) + 0.5f));
            let _e721 = FragmentUniformBlock.DitherSettings[3i];
            let _e725 = ((_e721 * _e721) * (3f - (2f * _e721)));
            let _e729 = FragmentUniformBlock.DitherGlitch[2i];
            let _e731 = mix(24f, (_e729 * 1.45f), _e725);
            let _e733 = FragmentUniformBlock.DitherGlitch;
            let _e735 = FragmentUniformBlock.DitherGlitchAxis;
            let _e737 = normalize(_e735.xy);
            let _e742 = (_e145 - _e733.xy);
            let _e745 = (dot(_e742, _e737) / max(_e731, 1f));
            let _e748 = (dot(_e742, vec2<f32>(-(_e737.y), _e737.x)) / max((_e731 * 0.38f), 1f));
            let _e759 = (((_e725 * smoothstep(1f, 0.25f, sqrt(((_e745 * _e745) + (_e748 * _e748))))) * 0.82f) + ((_e725 * smoothstep(0.6f, 1f, _e725)) * 0.18f));
            let _e762 = FragmentUniformBlock.DitherGlitch[3i];
            let _e764 = floor((_e762 * 9f));
            let _e766 = floor((_e156 * vec2<f32>(0.0625f, 0.0625f)));
            let _e788 = fract((sin(((((_e718 * 19.19f) + (_e766.x * 7.13f)) + (_e766.y * 3.71f)) + (_e764 * 5.17f))) * 43758.547f));
            let _e789 = (fract((sin(((((_e766.x * 17f) + (_e766.y * 31f)) + (_e764 * 13f)) + (_e718 * 0.23f))) * 43758.547f)) - 0.5f);
            let _e795 = (_e714 == 2f);
            if _e795 {
                phi_954_ = 2f;
            } else {
                phi_954_ = select(1f, 3f, (_e714 == 3f));
            }
            let _e799 = phi_954_;
            let _e800 = (_e718 % 3f);
            let _e805 = FragmentUniformBlock.DitherSettings[1i];
            let _e811 = floor(((_e156 + ((vec2<f32>(_e789, (_e788 - 0.5f)) * _e759) * 9f)) / vec2(max(((_e805 * _e799) * (1f + (_e800 * 0.08f))), 1f))));
            let _e812 = (_e714 == 1f);
            if _e812 {
                phi_992_ = vec2<f32>(-(_e811.x), _e811.y);
            } else {
                if _e795 {
                    phi_991_ = _e811.yx;
                } else {
                    if (_e714 == 3f) {
                        phi_990_ = vec2<f32>(_e811.x, -(_e811.y));
                    } else {
                        phi_989_ = _e811;
                        if (_e714 >= 4f) {
                            phi_989_ = -(_e811.yx);
                        }
                        let _e829 = phi_989_;
                        phi_990_ = _e829;
                    }
                    let _e831 = phi_990_;
                    phi_991_ = _e831;
                }
                let _e833 = phi_991_;
                phi_992_ = _e833;
            }
            let _e835 = phi_992_;
            let _e848 = (_e835 + (vec2<f32>(((_e714 * 2f) % 4f), ((_e714 * 3f) % 4f)) + (vec2<f32>((_e718 % 4f), (floor((_e718 * 0.25f)) % 4f)) * 0.35f)));
            if (_e759 > 0.65f) {
                phi_1069_ = floor((_e788 * 11f));
            } else {
                switch bitcast<i32>(0u) {
                    default: {
                        if _e812 {
                            if (_e800 == 0f) {
                                phi_1022_ = 8f;
                            } else {
                                phi_1022_ = select(5f, 6f, (_e800 == 1f));
                            }
                            let _e857 = phi_1022_;
                            phi_1068_ = _e857;
                            break;
                        }
                        if _e795 {
                            if (_e800 == 0f) {
                                phi_1031_ = 2f;
                            } else {
                                phi_1031_ = select(10f, 9f, (_e800 == 1f));
                            }
                            let _e862 = phi_1031_;
                            phi_1068_ = _e862;
                            break;
                        }
                        if (_e714 == 3f) {
                            if (_e800 == 0f) {
                                phi_1041_ = 3f;
                            } else {
                                phi_1041_ = select(1f, 5f, (_e800 == 1f));
                            }
                            let _e868 = phi_1041_;
                            phi_1068_ = _e868;
                            break;
                        }
                        if (_e714 == 5f) {
                            phi_1068_ = 0f;
                            break;
                        }
                        if (_e714 == 6f) {
                            phi_1068_ = 6f;
                            break;
                        }
                        if (_e714 >= 7f) {
                            phi_1068_ = 4f;
                            break;
                        }
                        if (_e714 >= 4f) {
                            if (_e800 == 0f) {
                                phi_1060_ = 4f;
                            } else {
                                phi_1060_ = select(10f, 7f, (_e800 == 1f));
                            }
                            let _e877 = phi_1060_;
                            phi_1068_ = _e877;
                            break;
                        }
                        if (_e800 == 0f) {
                            phi_1067_ = 0f;
                        } else {
                            phi_1067_ = select(7f, 4f, (_e800 == 1f));
                        }
                        let _e882 = phi_1067_;
                        phi_1068_ = _e882;
                        break;
                    }
                }
                let _e884 = phi_1068_;
                phi_1069_ = _e884;
            }
            let _e886 = phi_1069_;
            switch bitcast<i32>(0u) {
                default: {
                    if (_e886 == 1f) {
                        phi_1321_ = ((((_e848.x + (_e848.y * 2f)) % 6f) + 1f) * 0.14285715f);
                        break;
                    }
                    if (_e886 == 2f) {
                        let _e898 = ((_e848 % vec2<f32>(4f, 4f)) - vec2<f32>(1.5f, 1.5f));
                        phi_1321_ = clamp(((dot(_e898, _e898) + 0.5f) * 0.2f), 0f, 1f);
                        break;
                    }
                    if (_e886 == 3f) {
                        phi_1321_ = (((_e848.x % 4f) + 1f) * 0.2f);
                        break;
                    }
                    if (_e886 == 4f) {
                        phi_1321_ = fract((sin(dot(floor(_e848), vec2<f32>(127.1f, 311.7f))) * 43758.547f));
                        break;
                    }
                    if (_e886 == 5f) {
                        phi_1321_ = (((_e848.y % 4f) + 1f) * 0.2f);
                        break;
                    }
                    if (_e886 == 6f) {
                        phi_1321_ = ((min(((_e848.x + _e848.y) % 4f), (((_e848.x - _e848.y) + 8f) % 4f)) + 1f) * 0.2f);
                        break;
                    }
                    if (_e886 == 7f) {
                        let _e931 = (_e848 % vec2<f32>(4f, 4f));
                        let _e940 = i32((_e848.x % 4f));
                        let _e943 = i32((_e848.y % 4f));
                        if (_e940 == 0i) {
                            if (_e943 == 0i) {
                                phi_1157_ = 16f;
                            } else {
                                if (_e943 == 1i) {
                                    phi_1156_ = 5f;
                                } else {
                                    phi_1156_ = select(1f, 13f, (_e943 == 2i));
                                }
                                let _e950 = phi_1156_;
                                phi_1157_ = _e950;
                            }
                            let _e952 = phi_1157_;
                            phi_1204_ = _e952;
                        } else {
                            if (_e940 == 1i) {
                                if (_e943 == 0i) {
                                    phi_1173_ = 8f;
                                } else {
                                    if (_e943 == 1i) {
                                        phi_1172_ = 12f;
                                    } else {
                                        phi_1172_ = select(9f, 4f, (_e943 == 2i));
                                    }
                                    let _e959 = phi_1172_;
                                    phi_1173_ = _e959;
                                }
                                let _e961 = phi_1173_;
                                phi_1203_ = _e961;
                            } else {
                                if (_e940 == 2i) {
                                    if (_e943 == 0i) {
                                        phi_1189_ = 14f;
                                    } else {
                                        if (_e943 == 1i) {
                                            phi_1188_ = 2f;
                                        } else {
                                            phi_1188_ = select(3f, 15f, (_e943 == 2i));
                                        }
                                        let _e968 = phi_1188_;
                                        phi_1189_ = _e968;
                                    }
                                    let _e970 = phi_1189_;
                                    phi_1202_ = _e970;
                                } else {
                                    if (_e943 == 0i) {
                                        phi_1201_ = 6f;
                                    } else {
                                        if (_e943 == 1i) {
                                            phi_1200_ = 10f;
                                        } else {
                                            phi_1200_ = select(11f, 7f, (_e943 == 2i));
                                        }
                                        let _e976 = phi_1200_;
                                        phi_1201_ = _e976;
                                    }
                                    let _e978 = phi_1201_;
                                    phi_1202_ = _e978;
                                }
                                let _e980 = phi_1202_;
                                phi_1203_ = _e980;
                            }
                            let _e982 = phi_1203_;
                            phi_1204_ = _e982;
                        }
                        let _e984 = phi_1204_;
                        if (((floor(_e931.x) + floor(_e931.y)) % 2f) == 0f) {
                            phi_1212_ = (_e984 * 0.04411765f);
                        } else {
                            phi_1212_ = (0.35f + (_e984 * 0.03823529f));
                        }
                        let _e990 = phi_1212_;
                        phi_1321_ = _e990;
                        break;
                    }
                    if (_e886 == 8f) {
                        phi_1321_ = clamp(((min(((_e848.x + ((floor((_e848.y * 0.5f)) % 2f) * 2f)) % 4f), (_e848.y % 4f)) + 1f) * 0.2f), 0f, 1f);
                        break;
                    }
                    if (_e886 == 9f) {
                        phi_1321_ = (((((_e848.x * 2f) + _e848.y) % 8f) + 1f) * 0.11111111f);
                        break;
                    }
                    if (_e886 >= 10f) {
                        let _e1016 = abs(((_e848 % vec2<f32>(8f, 8f)) - vec2<f32>(3.5f, 3.5f)));
                        phi_1321_ = clamp(((max(_e1016.x, _e1016.y) - 0.5f) * 0.2857143f), 0f, 1f);
                        break;
                    }
                    let _e1025 = i32((_e848.x % 4f));
                    let _e1028 = i32((_e848.y % 4f));
                    if (_e1025 == 0i) {
                        if (_e1028 == 0i) {
                            phi_1272_ = 16f;
                        } else {
                            if (_e1028 == 1i) {
                                phi_1271_ = 5f;
                            } else {
                                phi_1271_ = select(1f, 13f, (_e1028 == 2i));
                            }
                            let _e1035 = phi_1271_;
                            phi_1272_ = _e1035;
                        }
                        let _e1037 = phi_1272_;
                        phi_1319_ = _e1037;
                    } else {
                        if (_e1025 == 1i) {
                            if (_e1028 == 0i) {
                                phi_1288_ = 8f;
                            } else {
                                if (_e1028 == 1i) {
                                    phi_1287_ = 12f;
                                } else {
                                    phi_1287_ = select(9f, 4f, (_e1028 == 2i));
                                }
                                let _e1044 = phi_1287_;
                                phi_1288_ = _e1044;
                            }
                            let _e1046 = phi_1288_;
                            phi_1318_ = _e1046;
                        } else {
                            if (_e1025 == 2i) {
                                if (_e1028 == 0i) {
                                    phi_1304_ = 14f;
                                } else {
                                    if (_e1028 == 1i) {
                                        phi_1303_ = 2f;
                                    } else {
                                        phi_1303_ = select(3f, 15f, (_e1028 == 2i));
                                    }
                                    let _e1053 = phi_1303_;
                                    phi_1304_ = _e1053;
                                }
                                let _e1055 = phi_1304_;
                                phi_1317_ = _e1055;
                            } else {
                                if (_e1028 == 0i) {
                                    phi_1316_ = 6f;
                                } else {
                                    if (_e1028 == 1i) {
                                        phi_1315_ = 10f;
                                    } else {
                                        phi_1315_ = select(11f, 7f, (_e1028 == 2i));
                                    }
                                    let _e1061 = phi_1315_;
                                    phi_1316_ = _e1061;
                                }
                                let _e1063 = phi_1316_;
                                phi_1317_ = _e1063;
                            }
                            let _e1065 = phi_1317_;
                            phi_1318_ = _e1065;
                        }
                        let _e1067 = phi_1318_;
                        phi_1319_ = _e1067;
                    }
                    let _e1069 = phi_1319_;
                    phi_1321_ = (_e1069 * 0.05882353f);
                    break;
                }
            }
            let _e1072 = phi_1321_;
            let _e1083 = clamp(clamp((clamp((_e1072 + ((fract((_e718 * 0.618034f)) - 0.5f) * 0.1f)), 0f, 1f) + ((_e789 * _e759) * 0.18f)), 0f, 1f), 0f, 1f);
            let _e1089 = local_1;
            if (_e1089 > 0f) {
                let _e1093 = FragmentUniformBlock.DitherSettings[2i];
                phi_1344_ = (_e1093 * _e122.x);
            } else {
                phi_1344_ = 0f;
            }
            let _e1096 = phi_1344_;
            let _e1099 = FragmentUniformBlock.NormalDitherSettings[0i];
            let _e1104 = (_e691 * mix(1f, (select(1f, 0f, (clamp(_e711, 0f, 1f) < _e1083)) / _e711), clamp((_e1096 + (_e132 * _e1099)), 0f, 1f)));
            let _e1107 = FragmentUniformBlock.NormalDitherSettings[1i];
            phi_1358_ = false;
            if (_e1107 > 1f) {
                phi_1358_ = (_e132 > 0f);
            }
            let _e1111 = phi_1358_;
            phi_1376_ = _e1104;
            if _e1111 {
                let _e1116 = max(max(_e1104.x, _e1104.y), _e1104.z);
                let _e1117 = (_e1107 - 1f);
                phi_1376_ = (_e1104 * mix(1f, ((floor(((clamp(_e1116, 0f, 1f) * _e1117) + _e1083)) / _e1117) / max(_e1116, 0.0001f)), _e132));
            }
            let _e1128 = phi_1376_;
            phi_1377_ = _e1128;
            break;
        }
    }
    let _e1130 = phi_1377_;
    let _e1143 = _e1130.xyz;
    let _e1151 = clamp(mix(((_e688.xyz * _e1143) + (_e1143 * ((_e689.w * clamp((1f - (max(max(_e688.x, _e688.y), _e688.z) * 2.2f)), 0f, 1f)) * 0.18f))), _e690.xyz, vec3(_e690.w)), vec3<f32>(0f, 0f, 0f), vec3<f32>(1f, 1f, 1f));
    out_u002e_var_u002e_SV_Target0_ = clamp((vec4<f32>(_e1151.x, _e1151.y, _e1151.z, _e688.w) + (vec4<f32>(_e681.x, _e681.y, _e681.z, _e676.w) * (1f - _e688.w))), vec4<f32>(0f, 0f, 0f, 0f), vec4<f32>(1f, 1f, 1f, 1f));
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
