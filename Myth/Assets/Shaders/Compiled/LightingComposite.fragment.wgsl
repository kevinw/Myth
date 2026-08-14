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
var EmissiveTexture: texture_2d<f32>;
@group(2) @binding(103) 
var EmissiveSampler: sampler;
@group(2) @binding(4) 
var AoTexture: texture_2d<f32>;
@group(2) @binding(104) 
var AoSampler: sampler;
@group(2) @binding(5) 
var BackgroundCoverageTexture: texture_2d<f32>;
@group(2) @binding(105) 
var BackgroundCoverageSampler: sampler;
@group(2) @binding(6) 
var ForegroundTexture: texture_2d<f32>;
@group(2) @binding(106) 
var ForegroundSampler: sampler;
@group(2) @binding(7) 
var ForegroundLightTexture: texture_2d<f32>;
@group(2) @binding(107) 
var ForegroundLightSampler: sampler;
@group(2) @binding(8) 
var ForegroundEmissiveTexture: texture_2d<f32>;
@group(2) @binding(108) 
var ForegroundEmissiveSampler: sampler;
@group(2) @binding(9) 
var NormalTexture: texture_2d<f32>;
@group(2) @binding(109) 
var NormalSampler: sampler;
@group(2) @binding(10) 
var SporeFogTexture: texture_2d<f32>;
@group(2) @binding(110) 
var SporeFogSampler: sampler;
@group(2) @binding(11) 
var ActorTileFieldTexture: texture_2d<f32>;
@group(2) @binding(111) 
var ActorTileFieldSampler: sampler;
var<private> in_u002e_var_u002e_TEXCOORD0_1: vec2<f32>;
var<private> in_u002e_var_u002e_TEXCOORD1_1: vec4<f32>;
var<private> in_u002e_var_u002e_TEXCOORD4_1: vec4<f32>;
var<private> global: vec4<f32>;
var<private> out_u002e_var_u002e_SV_Target0_: vec4<f32>;

fn fragment_main_1() {
    var phi_234_: f32;
    var phi_235_: vec4<f32>;
    var phi_236_: vec2<f32>;
    var phi_267_: vec4<f32>;
    var phi_279_: bool;
    var phi_285_: bool;
    var phi_385_: f32;
    var phi_420_: vec2<f32>;
    var phi_421_: vec2<f32>;
    var phi_422_: vec2<f32>;
    var phi_423_: vec2<f32>;
    var phi_453_: f32;
    var phi_462_: f32;
    var phi_472_: f32;
    var phi_491_: f32;
    var phi_498_: f32;
    var phi_499_: f32;
    var phi_500_: f32;
    var phi_587_: f32;
    var phi_588_: f32;
    var phi_603_: f32;
    var phi_604_: f32;
    var phi_619_: f32;
    var phi_620_: f32;
    var phi_631_: f32;
    var phi_632_: f32;
    var phi_633_: f32;
    var phi_634_: f32;
    var phi_635_: f32;
    var phi_643_: f32;
    var phi_702_: f32;
    var phi_703_: f32;
    var phi_718_: f32;
    var phi_719_: f32;
    var phi_734_: f32;
    var phi_735_: f32;
    var phi_746_: f32;
    var phi_747_: f32;
    var phi_748_: f32;
    var phi_749_: f32;
    var phi_750_: f32;
    var phi_752_: f32;
    var phi_777_: f32;
    var phi_791_: bool;
    var phi_809_: vec3<f32>;
    var phi_810_: vec3<f32>;
    var local: bool;
    var phi_867_: bool;
    var phi_873_: bool;
    var phi_969_: f32;
    var phi_1004_: vec2<f32>;
    var phi_1005_: vec2<f32>;
    var phi_1006_: vec2<f32>;
    var phi_1007_: vec2<f32>;
    var phi_1037_: f32;
    var phi_1046_: f32;
    var phi_1056_: f32;
    var phi_1075_: f32;
    var phi_1082_: f32;
    var phi_1083_: f32;
    var phi_1084_: f32;
    var phi_1171_: f32;
    var phi_1172_: f32;
    var phi_1187_: f32;
    var phi_1188_: f32;
    var phi_1203_: f32;
    var phi_1204_: f32;
    var phi_1215_: f32;
    var phi_1216_: f32;
    var phi_1217_: f32;
    var phi_1218_: f32;
    var phi_1219_: f32;
    var phi_1227_: f32;
    var phi_1286_: f32;
    var phi_1287_: f32;
    var phi_1302_: f32;
    var phi_1303_: f32;
    var phi_1318_: f32;
    var phi_1319_: f32;
    var phi_1330_: f32;
    var phi_1331_: f32;
    var phi_1332_: f32;
    var phi_1333_: f32;
    var phi_1334_: f32;
    var phi_1336_: f32;
    var local_1: f32;
    var phi_1359_: f32;
    var phi_1373_: bool;
    var phi_1391_: vec3<f32>;
    var phi_1392_: vec3<f32>;

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
    let _e121 = textureSample(EmissiveTexture, EmissiveSampler, _e111);
    let _e122 = textureSample(NormalTexture, NormalSampler, _e111);
    let _e130 = (_e122.w * clamp(length(((_e122.xy * 2f) - vec2<f32>(1f, 1f))), 0f, 1f));
    let _e134 = FragmentUniformBlock.DitherGlitchAxis[2i];
    let _e138 = FragmentUniformBlock.TextureSizeCamera;
    let _e143 = (_e138.zw + (_e114.xy / vec2(select(1f, _e134, (_e134 > 0f)))));
    let _e147 = floor(((_e120.y * 255f) + 0.5f));
    let _e152 = FragmentUniformBlock.DitherAnchorOffsets[i32((_e147 % 32f))];
    let _e154 = (_e143 + _e152.xy);
    let _e157 = FragmentUniformBlock.NormalDitherSettings[2i];
    phi_234_ = 0.5f;
    phi_235_ = _e120;
    phi_236_ = _e154;
    if (_e157 > 0.5f) {
        let _e159 = textureSample(ActorTileFieldTexture, ActorTileFieldSampler, _e111);
        let _e163 = floor(((_e159.x * 255f) + 0.5f));
        let _e165 = floor((_e163 * 0.03125f));
        let _e167 = step(0.5f, _e165);
        let _e176 = floor(((_e159.y * 255f) + 0.5f));
        phi_234_ = mix(0.5f, ((_e176 % 8f) * 0.14285715f), _e167);
        phi_235_ = mix(_e120, vec4<f32>(((_e163 % 32f) * 0.003921569f), (_e165 * 0.1254902f), 0f, 1f), vec4(_e167));
        phi_236_ = mix(_e154, (_e143 / vec2(exp2(((floor((_e176 * 0.125f)) - 16f) * 0.125f)))), vec2(_e167));
    }
    let _e190 = phi_234_;
    let _e192 = phi_235_;
    let _e194 = phi_236_;
    let _e198 = FragmentUniformBlock.AoSettings[0i];
    phi_267_ = _e119;
    if (_e198 > 0f) {
        let _e200 = FragmentUniformBlock.AoSettings;
        let _e205 = textureSample(AoTexture, AoSampler, (_e111 + (_e200.yz / _e138.xy)));
        let _e209 = textureSample(BackgroundCoverageTexture, BackgroundCoverageSampler, _e111);
        let _e213 = FragmentUniformBlock.AoSettings[3i];
        let _e217 = (_e119.xyz * mix(1f, _e213, (clamp((_e205.x * _e198), 0f, 1f) * _e209.x)));
        phi_267_ = vec4<f32>(_e217.x, _e217.y, _e217.z, _e119.w);
    }
    let _e224 = phi_267_;
    let _e225 = _e224.xyz;
    switch bitcast<i32>(0u) {
        default: {
            let _e229 = FragmentUniformBlock.DitherSettings[0i];
            let _e230 = (_e229 <= 0f);
            phi_279_ = false;
            local = _e230;
            local_1 = _e229;
            if _e230 {
                let _e233 = FragmentUniformBlock.NormalDitherSettings[0i];
                phi_279_ = (_e233 <= 0f);
            }
            let _e236 = phi_279_;
            phi_285_ = false;
            if _e236 {
                let _e239 = FragmentUniformBlock.NormalDitherSettings[1i];
                phi_285_ = (_e239 <= 1f);
            }
            let _e242 = phi_285_;
            if _e242 {
                phi_810_ = _e225;
                break;
            }
            let _e247 = max(max(_e224.x, _e224.y), _e224.z);
            if (_e247 <= 0.0001f) {
                phi_810_ = _e225;
                break;
            }
            let _e254 = floor((floor(((_e192.y * 255f) + 0.5f)) * 0.03125f));
            let _e258 = floor(((_e192.x * 255f) + 0.5f));
            let _e261 = FragmentUniformBlock.DitherSettings[3i];
            let _e265 = ((_e261 * _e261) * (3f - (2f * _e261)));
            let _e269 = FragmentUniformBlock.DitherGlitch[2i];
            let _e271 = mix(24f, (_e269 * 1.45f), _e265);
            let _e273 = FragmentUniformBlock.DitherGlitch;
            let _e275 = FragmentUniformBlock.DitherGlitchAxis;
            let _e277 = normalize(_e275.xy);
            let _e282 = (_e143 - _e273.xy);
            let _e285 = (dot(_e282, _e277) / max(_e271, 1f));
            let _e288 = (dot(_e282, vec2<f32>(-(_e277.y), _e277.x)) / max((_e271 * 0.38f), 1f));
            let _e299 = (((_e265 * smoothstep(1f, 0.25f, sqrt(((_e285 * _e285) + (_e288 * _e288))))) * 0.82f) + ((_e265 * smoothstep(0.6f, 1f, _e265)) * 0.18f));
            let _e302 = FragmentUniformBlock.DitherGlitch[3i];
            let _e304 = floor((_e302 * 9f));
            let _e306 = floor((_e194 * vec2<f32>(0.0625f, 0.0625f)));
            let _e328 = fract((sin(((((_e258 * 19.19f) + (_e306.x * 7.13f)) + (_e306.y * 3.71f)) + (_e304 * 5.17f))) * 43758.547f));
            let _e329 = (fract((sin(((((_e306.x * 17f) + (_e306.y * 31f)) + (_e304 * 13f)) + (_e258 * 0.23f))) * 43758.547f)) - 0.5f);
            let _e335 = (_e254 == 2f);
            if _e335 {
                phi_385_ = 2f;
            } else {
                phi_385_ = select(1f, 3f, (_e254 == 3f));
            }
            let _e339 = phi_385_;
            let _e340 = (_e258 % 3f);
            let _e345 = FragmentUniformBlock.DitherSettings[1i];
            let _e351 = floor(((_e194 + ((vec2<f32>(_e329, (_e328 - 0.5f)) * _e299) * 9f)) / vec2(max(((_e345 * _e339) * (1f + (_e340 * 0.08f))), 1f))));
            let _e352 = (_e254 == 1f);
            if _e352 {
                phi_423_ = vec2<f32>(-(_e351.x), _e351.y);
            } else {
                if _e335 {
                    phi_422_ = _e351.yx;
                } else {
                    if (_e254 == 3f) {
                        phi_421_ = vec2<f32>(_e351.x, -(_e351.y));
                    } else {
                        phi_420_ = _e351;
                        if (_e254 >= 4f) {
                            phi_420_ = -(_e351.yx);
                        }
                        let _e369 = phi_420_;
                        phi_421_ = _e369;
                    }
                    let _e371 = phi_421_;
                    phi_422_ = _e371;
                }
                let _e373 = phi_422_;
                phi_423_ = _e373;
            }
            let _e375 = phi_423_;
            let _e388 = (_e375 + (vec2<f32>(((_e254 * 2f) % 4f), ((_e254 * 3f) % 4f)) + (vec2<f32>((_e258 % 4f), (floor((_e258 * 0.25f)) % 4f)) * 0.35f)));
            if (_e299 > 0.65f) {
                phi_500_ = floor((_e328 * 11f));
            } else {
                switch bitcast<i32>(0u) {
                    default: {
                        if _e352 {
                            if (_e340 == 0f) {
                                phi_453_ = 8f;
                            } else {
                                phi_453_ = select(5f, 6f, (_e340 == 1f));
                            }
                            let _e397 = phi_453_;
                            phi_499_ = _e397;
                            break;
                        }
                        if _e335 {
                            if (_e340 == 0f) {
                                phi_462_ = 2f;
                            } else {
                                phi_462_ = select(10f, 9f, (_e340 == 1f));
                            }
                            let _e402 = phi_462_;
                            phi_499_ = _e402;
                            break;
                        }
                        if (_e254 == 3f) {
                            if (_e340 == 0f) {
                                phi_472_ = 3f;
                            } else {
                                phi_472_ = select(1f, 5f, (_e340 == 1f));
                            }
                            let _e408 = phi_472_;
                            phi_499_ = _e408;
                            break;
                        }
                        if (_e254 == 5f) {
                            phi_499_ = 0f;
                            break;
                        }
                        if (_e254 == 6f) {
                            phi_499_ = 6f;
                            break;
                        }
                        if (_e254 >= 7f) {
                            phi_499_ = 4f;
                            break;
                        }
                        if (_e254 >= 4f) {
                            if (_e340 == 0f) {
                                phi_491_ = 4f;
                            } else {
                                phi_491_ = select(10f, 7f, (_e340 == 1f));
                            }
                            let _e417 = phi_491_;
                            phi_499_ = _e417;
                            break;
                        }
                        if (_e340 == 0f) {
                            phi_498_ = 0f;
                        } else {
                            phi_498_ = select(7f, 4f, (_e340 == 1f));
                        }
                        let _e422 = phi_498_;
                        phi_499_ = _e422;
                        break;
                    }
                }
                let _e424 = phi_499_;
                phi_500_ = _e424;
            }
            let _e426 = phi_500_;
            switch bitcast<i32>(0u) {
                default: {
                    if (_e426 == 1f) {
                        phi_752_ = ((((_e388.x + (_e388.y * 2f)) % 6f) + 1f) * 0.14285715f);
                        break;
                    }
                    if (_e426 == 2f) {
                        let _e438 = ((_e388 % vec2<f32>(4f, 4f)) - vec2<f32>(1.5f, 1.5f));
                        phi_752_ = clamp(((dot(_e438, _e438) + 0.5f) * 0.2f), 0f, 1f);
                        break;
                    }
                    if (_e426 == 3f) {
                        phi_752_ = (((_e388.x % 4f) + 1f) * 0.2f);
                        break;
                    }
                    if (_e426 == 4f) {
                        phi_752_ = fract((sin(dot(floor(_e388), vec2<f32>(127.1f, 311.7f))) * 43758.547f));
                        break;
                    }
                    if (_e426 == 5f) {
                        phi_752_ = (((_e388.y % 4f) + 1f) * 0.2f);
                        break;
                    }
                    if (_e426 == 6f) {
                        phi_752_ = ((min(((_e388.x + _e388.y) % 4f), (((_e388.x - _e388.y) + 8f) % 4f)) + 1f) * 0.2f);
                        break;
                    }
                    if (_e426 == 7f) {
                        let _e471 = (_e388 % vec2<f32>(4f, 4f));
                        let _e480 = i32((_e388.x % 4f));
                        let _e483 = i32((_e388.y % 4f));
                        if (_e480 == 0i) {
                            if (_e483 == 0i) {
                                phi_588_ = 16f;
                            } else {
                                if (_e483 == 1i) {
                                    phi_587_ = 5f;
                                } else {
                                    phi_587_ = select(1f, 13f, (_e483 == 2i));
                                }
                                let _e490 = phi_587_;
                                phi_588_ = _e490;
                            }
                            let _e492 = phi_588_;
                            phi_635_ = _e492;
                        } else {
                            if (_e480 == 1i) {
                                if (_e483 == 0i) {
                                    phi_604_ = 8f;
                                } else {
                                    if (_e483 == 1i) {
                                        phi_603_ = 12f;
                                    } else {
                                        phi_603_ = select(9f, 4f, (_e483 == 2i));
                                    }
                                    let _e499 = phi_603_;
                                    phi_604_ = _e499;
                                }
                                let _e501 = phi_604_;
                                phi_634_ = _e501;
                            } else {
                                if (_e480 == 2i) {
                                    if (_e483 == 0i) {
                                        phi_620_ = 14f;
                                    } else {
                                        if (_e483 == 1i) {
                                            phi_619_ = 2f;
                                        } else {
                                            phi_619_ = select(3f, 15f, (_e483 == 2i));
                                        }
                                        let _e508 = phi_619_;
                                        phi_620_ = _e508;
                                    }
                                    let _e510 = phi_620_;
                                    phi_633_ = _e510;
                                } else {
                                    if (_e483 == 0i) {
                                        phi_632_ = 6f;
                                    } else {
                                        if (_e483 == 1i) {
                                            phi_631_ = 10f;
                                        } else {
                                            phi_631_ = select(11f, 7f, (_e483 == 2i));
                                        }
                                        let _e516 = phi_631_;
                                        phi_632_ = _e516;
                                    }
                                    let _e518 = phi_632_;
                                    phi_633_ = _e518;
                                }
                                let _e520 = phi_633_;
                                phi_634_ = _e520;
                            }
                            let _e522 = phi_634_;
                            phi_635_ = _e522;
                        }
                        let _e524 = phi_635_;
                        if (((floor(_e471.x) + floor(_e471.y)) % 2f) == 0f) {
                            phi_643_ = (_e524 * 0.04411765f);
                        } else {
                            phi_643_ = (0.35f + (_e524 * 0.03823529f));
                        }
                        let _e530 = phi_643_;
                        phi_752_ = _e530;
                        break;
                    }
                    if (_e426 == 8f) {
                        phi_752_ = clamp(((min(((_e388.x + ((floor((_e388.y * 0.5f)) % 2f) * 2f)) % 4f), (_e388.y % 4f)) + 1f) * 0.2f), 0f, 1f);
                        break;
                    }
                    if (_e426 == 9f) {
                        phi_752_ = (((((_e388.x * 2f) + _e388.y) % 8f) + 1f) * 0.11111111f);
                        break;
                    }
                    if (_e426 >= 10f) {
                        let _e556 = abs(((_e388 % vec2<f32>(8f, 8f)) - vec2<f32>(3.5f, 3.5f)));
                        phi_752_ = clamp(((max(_e556.x, _e556.y) - 0.5f) * 0.2857143f), 0f, 1f);
                        break;
                    }
                    let _e565 = i32((_e388.x % 4f));
                    let _e568 = i32((_e388.y % 4f));
                    if (_e565 == 0i) {
                        if (_e568 == 0i) {
                            phi_703_ = 16f;
                        } else {
                            if (_e568 == 1i) {
                                phi_702_ = 5f;
                            } else {
                                phi_702_ = select(1f, 13f, (_e568 == 2i));
                            }
                            let _e575 = phi_702_;
                            phi_703_ = _e575;
                        }
                        let _e577 = phi_703_;
                        phi_750_ = _e577;
                    } else {
                        if (_e565 == 1i) {
                            if (_e568 == 0i) {
                                phi_719_ = 8f;
                            } else {
                                if (_e568 == 1i) {
                                    phi_718_ = 12f;
                                } else {
                                    phi_718_ = select(9f, 4f, (_e568 == 2i));
                                }
                                let _e584 = phi_718_;
                                phi_719_ = _e584;
                            }
                            let _e586 = phi_719_;
                            phi_749_ = _e586;
                        } else {
                            if (_e565 == 2i) {
                                if (_e568 == 0i) {
                                    phi_735_ = 14f;
                                } else {
                                    if (_e568 == 1i) {
                                        phi_734_ = 2f;
                                    } else {
                                        phi_734_ = select(3f, 15f, (_e568 == 2i));
                                    }
                                    let _e593 = phi_734_;
                                    phi_735_ = _e593;
                                }
                                let _e595 = phi_735_;
                                phi_748_ = _e595;
                            } else {
                                if (_e568 == 0i) {
                                    phi_747_ = 6f;
                                } else {
                                    if (_e568 == 1i) {
                                        phi_746_ = 10f;
                                    } else {
                                        phi_746_ = select(11f, 7f, (_e568 == 2i));
                                    }
                                    let _e601 = phi_746_;
                                    phi_747_ = _e601;
                                }
                                let _e603 = phi_747_;
                                phi_748_ = _e603;
                            }
                            let _e605 = phi_748_;
                            phi_749_ = _e605;
                        }
                        let _e607 = phi_749_;
                        phi_750_ = _e607;
                    }
                    let _e609 = phi_750_;
                    phi_752_ = (_e609 * 0.05882353f);
                    break;
                }
            }
            let _e612 = phi_752_;
            let _e625 = clamp(((clamp((clamp((_e612 + ((fract((_e258 * 0.618034f)) - 0.5f) * 0.1f)), 0f, 1f) + ((_e329 * _e299) * 0.18f)), 0f, 1f) + 0.5f) - _e190), 0f, 1f);
            if (_e229 > 0f) {
                let _e633 = FragmentUniformBlock.DitherSettings[2i];
                phi_777_ = (_e633 * _e122.z);
            } else {
                phi_777_ = 0f;
            }
            let _e636 = phi_777_;
            let _e639 = FragmentUniformBlock.NormalDitherSettings[0i];
            let _e644 = (_e225 * mix(1f, (select(1f, 0f, (clamp(_e247, 0f, 1f) < _e625)) / _e247), clamp((_e636 + (_e130 * _e639)), 0f, 1f)));
            let _e647 = FragmentUniformBlock.NormalDitherSettings[1i];
            phi_791_ = false;
            if (_e647 > 1f) {
                phi_791_ = (_e130 > 0f);
            }
            let _e651 = phi_791_;
            phi_809_ = _e644;
            if _e651 {
                let _e656 = max(max(_e644.x, _e644.y), _e644.z);
                let _e657 = (_e647 - 1f);
                phi_809_ = (_e644 * mix(1f, ((floor(((clamp(_e656, 0f, 1f) * _e657) + _e625)) / _e657) / max(_e656, 0.0001f)), _e130));
            }
            let _e668 = phi_809_;
            phi_810_ = _e668;
            break;
        }
    }
    let _e670 = phi_810_;
    let _e683 = _e670.xyz;
    let _e691 = clamp(mix(((_e118.xyz * _e683) + (_e683 * ((_e224.w * clamp((1f - (max(max(_e118.x, _e118.y), _e118.z) * 2.2f)), 0f, 1f)) * 0.18f))), _e121.xyz, vec3(_e121.w)), vec3<f32>(0f, 0f, 0f), vec3<f32>(1f, 1f, 1f));
    let _e696 = vec4<f32>(_e691.x, _e691.y, _e691.z, _e118.w);
    let _e698 = textureSample(SporeFogTexture, SporeFogSampler, _e111);
    let _e701 = clamp((_e696.xyz + _e698.xyz), vec3<f32>(0f, 0f, 0f), vec3<f32>(1f, 1f, 1f));
    let _e707 = textureSample(ForegroundTexture, ForegroundSampler, _e111);
    let _e708 = (_e707 * _e112);
    let _e709 = textureSample(ForegroundLightTexture, ForegroundLightSampler, _e111);
    let _e710 = textureSample(ForegroundEmissiveTexture, ForegroundEmissiveSampler, _e111);
    let _e711 = _e709.xyz;
    switch bitcast<i32>(0u) {
        default: {
            let _e714 = local;
            phi_867_ = false;
            if _e714 {
                let _e717 = FragmentUniformBlock.NormalDitherSettings[0i];
                phi_867_ = (_e717 <= 0f);
            }
            let _e720 = phi_867_;
            phi_873_ = false;
            if _e720 {
                let _e723 = FragmentUniformBlock.NormalDitherSettings[1i];
                phi_873_ = (_e723 <= 1f);
            }
            let _e726 = phi_873_;
            if _e726 {
                phi_1392_ = _e711;
                break;
            }
            let _e731 = max(max(_e709.x, _e709.y), _e709.z);
            if (_e731 <= 0.0001f) {
                phi_1392_ = _e711;
                break;
            }
            let _e734 = floor((_e147 * 0.03125f));
            let _e738 = floor(((_e120.x * 255f) + 0.5f));
            let _e741 = FragmentUniformBlock.DitherSettings[3i];
            let _e745 = ((_e741 * _e741) * (3f - (2f * _e741)));
            let _e749 = FragmentUniformBlock.DitherGlitch[2i];
            let _e751 = mix(24f, (_e749 * 1.45f), _e745);
            let _e753 = FragmentUniformBlock.DitherGlitch;
            let _e755 = FragmentUniformBlock.DitherGlitchAxis;
            let _e757 = normalize(_e755.xy);
            let _e762 = (_e143 - _e753.xy);
            let _e765 = (dot(_e762, _e757) / max(_e751, 1f));
            let _e768 = (dot(_e762, vec2<f32>(-(_e757.y), _e757.x)) / max((_e751 * 0.38f), 1f));
            let _e779 = (((_e745 * smoothstep(1f, 0.25f, sqrt(((_e765 * _e765) + (_e768 * _e768))))) * 0.82f) + ((_e745 * smoothstep(0.6f, 1f, _e745)) * 0.18f));
            let _e782 = FragmentUniformBlock.DitherGlitch[3i];
            let _e784 = floor((_e782 * 9f));
            let _e786 = floor((_e154 * vec2<f32>(0.0625f, 0.0625f)));
            let _e808 = fract((sin(((((_e738 * 19.19f) + (_e786.x * 7.13f)) + (_e786.y * 3.71f)) + (_e784 * 5.17f))) * 43758.547f));
            let _e809 = (fract((sin(((((_e786.x * 17f) + (_e786.y * 31f)) + (_e784 * 13f)) + (_e738 * 0.23f))) * 43758.547f)) - 0.5f);
            let _e815 = (_e734 == 2f);
            if _e815 {
                phi_969_ = 2f;
            } else {
                phi_969_ = select(1f, 3f, (_e734 == 3f));
            }
            let _e819 = phi_969_;
            let _e820 = (_e738 % 3f);
            let _e825 = FragmentUniformBlock.DitherSettings[1i];
            let _e831 = floor(((_e154 + ((vec2<f32>(_e809, (_e808 - 0.5f)) * _e779) * 9f)) / vec2(max(((_e825 * _e819) * (1f + (_e820 * 0.08f))), 1f))));
            let _e832 = (_e734 == 1f);
            if _e832 {
                phi_1007_ = vec2<f32>(-(_e831.x), _e831.y);
            } else {
                if _e815 {
                    phi_1006_ = _e831.yx;
                } else {
                    if (_e734 == 3f) {
                        phi_1005_ = vec2<f32>(_e831.x, -(_e831.y));
                    } else {
                        phi_1004_ = _e831;
                        if (_e734 >= 4f) {
                            phi_1004_ = -(_e831.yx);
                        }
                        let _e849 = phi_1004_;
                        phi_1005_ = _e849;
                    }
                    let _e851 = phi_1005_;
                    phi_1006_ = _e851;
                }
                let _e853 = phi_1006_;
                phi_1007_ = _e853;
            }
            let _e855 = phi_1007_;
            let _e868 = (_e855 + (vec2<f32>(((_e734 * 2f) % 4f), ((_e734 * 3f) % 4f)) + (vec2<f32>((_e738 % 4f), (floor((_e738 * 0.25f)) % 4f)) * 0.35f)));
            if (_e779 > 0.65f) {
                phi_1084_ = floor((_e808 * 11f));
            } else {
                switch bitcast<i32>(0u) {
                    default: {
                        if _e832 {
                            if (_e820 == 0f) {
                                phi_1037_ = 8f;
                            } else {
                                phi_1037_ = select(5f, 6f, (_e820 == 1f));
                            }
                            let _e877 = phi_1037_;
                            phi_1083_ = _e877;
                            break;
                        }
                        if _e815 {
                            if (_e820 == 0f) {
                                phi_1046_ = 2f;
                            } else {
                                phi_1046_ = select(10f, 9f, (_e820 == 1f));
                            }
                            let _e882 = phi_1046_;
                            phi_1083_ = _e882;
                            break;
                        }
                        if (_e734 == 3f) {
                            if (_e820 == 0f) {
                                phi_1056_ = 3f;
                            } else {
                                phi_1056_ = select(1f, 5f, (_e820 == 1f));
                            }
                            let _e888 = phi_1056_;
                            phi_1083_ = _e888;
                            break;
                        }
                        if (_e734 == 5f) {
                            phi_1083_ = 0f;
                            break;
                        }
                        if (_e734 == 6f) {
                            phi_1083_ = 6f;
                            break;
                        }
                        if (_e734 >= 7f) {
                            phi_1083_ = 4f;
                            break;
                        }
                        if (_e734 >= 4f) {
                            if (_e820 == 0f) {
                                phi_1075_ = 4f;
                            } else {
                                phi_1075_ = select(10f, 7f, (_e820 == 1f));
                            }
                            let _e897 = phi_1075_;
                            phi_1083_ = _e897;
                            break;
                        }
                        if (_e820 == 0f) {
                            phi_1082_ = 0f;
                        } else {
                            phi_1082_ = select(7f, 4f, (_e820 == 1f));
                        }
                        let _e902 = phi_1082_;
                        phi_1083_ = _e902;
                        break;
                    }
                }
                let _e904 = phi_1083_;
                phi_1084_ = _e904;
            }
            let _e906 = phi_1084_;
            switch bitcast<i32>(0u) {
                default: {
                    if (_e906 == 1f) {
                        phi_1336_ = ((((_e868.x + (_e868.y * 2f)) % 6f) + 1f) * 0.14285715f);
                        break;
                    }
                    if (_e906 == 2f) {
                        let _e918 = ((_e868 % vec2<f32>(4f, 4f)) - vec2<f32>(1.5f, 1.5f));
                        phi_1336_ = clamp(((dot(_e918, _e918) + 0.5f) * 0.2f), 0f, 1f);
                        break;
                    }
                    if (_e906 == 3f) {
                        phi_1336_ = (((_e868.x % 4f) + 1f) * 0.2f);
                        break;
                    }
                    if (_e906 == 4f) {
                        phi_1336_ = fract((sin(dot(floor(_e868), vec2<f32>(127.1f, 311.7f))) * 43758.547f));
                        break;
                    }
                    if (_e906 == 5f) {
                        phi_1336_ = (((_e868.y % 4f) + 1f) * 0.2f);
                        break;
                    }
                    if (_e906 == 6f) {
                        phi_1336_ = ((min(((_e868.x + _e868.y) % 4f), (((_e868.x - _e868.y) + 8f) % 4f)) + 1f) * 0.2f);
                        break;
                    }
                    if (_e906 == 7f) {
                        let _e951 = (_e868 % vec2<f32>(4f, 4f));
                        let _e960 = i32((_e868.x % 4f));
                        let _e963 = i32((_e868.y % 4f));
                        if (_e960 == 0i) {
                            if (_e963 == 0i) {
                                phi_1172_ = 16f;
                            } else {
                                if (_e963 == 1i) {
                                    phi_1171_ = 5f;
                                } else {
                                    phi_1171_ = select(1f, 13f, (_e963 == 2i));
                                }
                                let _e970 = phi_1171_;
                                phi_1172_ = _e970;
                            }
                            let _e972 = phi_1172_;
                            phi_1219_ = _e972;
                        } else {
                            if (_e960 == 1i) {
                                if (_e963 == 0i) {
                                    phi_1188_ = 8f;
                                } else {
                                    if (_e963 == 1i) {
                                        phi_1187_ = 12f;
                                    } else {
                                        phi_1187_ = select(9f, 4f, (_e963 == 2i));
                                    }
                                    let _e979 = phi_1187_;
                                    phi_1188_ = _e979;
                                }
                                let _e981 = phi_1188_;
                                phi_1218_ = _e981;
                            } else {
                                if (_e960 == 2i) {
                                    if (_e963 == 0i) {
                                        phi_1204_ = 14f;
                                    } else {
                                        if (_e963 == 1i) {
                                            phi_1203_ = 2f;
                                        } else {
                                            phi_1203_ = select(3f, 15f, (_e963 == 2i));
                                        }
                                        let _e988 = phi_1203_;
                                        phi_1204_ = _e988;
                                    }
                                    let _e990 = phi_1204_;
                                    phi_1217_ = _e990;
                                } else {
                                    if (_e963 == 0i) {
                                        phi_1216_ = 6f;
                                    } else {
                                        if (_e963 == 1i) {
                                            phi_1215_ = 10f;
                                        } else {
                                            phi_1215_ = select(11f, 7f, (_e963 == 2i));
                                        }
                                        let _e996 = phi_1215_;
                                        phi_1216_ = _e996;
                                    }
                                    let _e998 = phi_1216_;
                                    phi_1217_ = _e998;
                                }
                                let _e1000 = phi_1217_;
                                phi_1218_ = _e1000;
                            }
                            let _e1002 = phi_1218_;
                            phi_1219_ = _e1002;
                        }
                        let _e1004 = phi_1219_;
                        if (((floor(_e951.x) + floor(_e951.y)) % 2f) == 0f) {
                            phi_1227_ = (_e1004 * 0.04411765f);
                        } else {
                            phi_1227_ = (0.35f + (_e1004 * 0.03823529f));
                        }
                        let _e1010 = phi_1227_;
                        phi_1336_ = _e1010;
                        break;
                    }
                    if (_e906 == 8f) {
                        phi_1336_ = clamp(((min(((_e868.x + ((floor((_e868.y * 0.5f)) % 2f) * 2f)) % 4f), (_e868.y % 4f)) + 1f) * 0.2f), 0f, 1f);
                        break;
                    }
                    if (_e906 == 9f) {
                        phi_1336_ = (((((_e868.x * 2f) + _e868.y) % 8f) + 1f) * 0.11111111f);
                        break;
                    }
                    if (_e906 >= 10f) {
                        let _e1036 = abs(((_e868 % vec2<f32>(8f, 8f)) - vec2<f32>(3.5f, 3.5f)));
                        phi_1336_ = clamp(((max(_e1036.x, _e1036.y) - 0.5f) * 0.2857143f), 0f, 1f);
                        break;
                    }
                    let _e1045 = i32((_e868.x % 4f));
                    let _e1048 = i32((_e868.y % 4f));
                    if (_e1045 == 0i) {
                        if (_e1048 == 0i) {
                            phi_1287_ = 16f;
                        } else {
                            if (_e1048 == 1i) {
                                phi_1286_ = 5f;
                            } else {
                                phi_1286_ = select(1f, 13f, (_e1048 == 2i));
                            }
                            let _e1055 = phi_1286_;
                            phi_1287_ = _e1055;
                        }
                        let _e1057 = phi_1287_;
                        phi_1334_ = _e1057;
                    } else {
                        if (_e1045 == 1i) {
                            if (_e1048 == 0i) {
                                phi_1303_ = 8f;
                            } else {
                                if (_e1048 == 1i) {
                                    phi_1302_ = 12f;
                                } else {
                                    phi_1302_ = select(9f, 4f, (_e1048 == 2i));
                                }
                                let _e1064 = phi_1302_;
                                phi_1303_ = _e1064;
                            }
                            let _e1066 = phi_1303_;
                            phi_1333_ = _e1066;
                        } else {
                            if (_e1045 == 2i) {
                                if (_e1048 == 0i) {
                                    phi_1319_ = 14f;
                                } else {
                                    if (_e1048 == 1i) {
                                        phi_1318_ = 2f;
                                    } else {
                                        phi_1318_ = select(3f, 15f, (_e1048 == 2i));
                                    }
                                    let _e1073 = phi_1318_;
                                    phi_1319_ = _e1073;
                                }
                                let _e1075 = phi_1319_;
                                phi_1332_ = _e1075;
                            } else {
                                if (_e1048 == 0i) {
                                    phi_1331_ = 6f;
                                } else {
                                    if (_e1048 == 1i) {
                                        phi_1330_ = 10f;
                                    } else {
                                        phi_1330_ = select(11f, 7f, (_e1048 == 2i));
                                    }
                                    let _e1081 = phi_1330_;
                                    phi_1331_ = _e1081;
                                }
                                let _e1083 = phi_1331_;
                                phi_1332_ = _e1083;
                            }
                            let _e1085 = phi_1332_;
                            phi_1333_ = _e1085;
                        }
                        let _e1087 = phi_1333_;
                        phi_1334_ = _e1087;
                    }
                    let _e1089 = phi_1334_;
                    phi_1336_ = (_e1089 * 0.05882353f);
                    break;
                }
            }
            let _e1092 = phi_1336_;
            let _e1103 = clamp(clamp((clamp((_e1092 + ((fract((_e738 * 0.618034f)) - 0.5f) * 0.1f)), 0f, 1f) + ((_e809 * _e779) * 0.18f)), 0f, 1f), 0f, 1f);
            let _e1109 = local_1;
            if (_e1109 > 0f) {
                let _e1113 = FragmentUniformBlock.DitherSettings[2i];
                phi_1359_ = (_e1113 * _e122.z);
            } else {
                phi_1359_ = 0f;
            }
            let _e1116 = phi_1359_;
            let _e1119 = FragmentUniformBlock.NormalDitherSettings[0i];
            let _e1124 = (_e711 * mix(1f, (select(1f, 0f, (clamp(_e731, 0f, 1f) < _e1103)) / _e731), clamp((_e1116 + (_e130 * _e1119)), 0f, 1f)));
            let _e1127 = FragmentUniformBlock.NormalDitherSettings[1i];
            phi_1373_ = false;
            if (_e1127 > 1f) {
                phi_1373_ = (_e130 > 0f);
            }
            let _e1131 = phi_1373_;
            phi_1391_ = _e1124;
            if _e1131 {
                let _e1136 = max(max(_e1124.x, _e1124.y), _e1124.z);
                let _e1137 = (_e1127 - 1f);
                phi_1391_ = (_e1124 * mix(1f, ((floor(((clamp(_e1136, 0f, 1f) * _e1137) + _e1103)) / _e1137) / max(_e1136, 0.0001f)), _e130));
            }
            let _e1148 = phi_1391_;
            phi_1392_ = _e1148;
            break;
        }
    }
    let _e1150 = phi_1392_;
    let _e1163 = _e1150.xyz;
    let _e1171 = clamp(mix(((_e708.xyz * _e1163) + (_e1163 * ((_e709.w * clamp((1f - (max(max(_e708.x, _e708.y), _e708.z) * 2.2f)), 0f, 1f)) * 0.18f))), _e710.xyz, vec3(_e710.w)), vec3<f32>(0f, 0f, 0f), vec3<f32>(1f, 1f, 1f));
    out_u002e_var_u002e_SV_Target0_ = clamp((vec4<f32>(_e1171.x, _e1171.y, _e1171.z, _e708.w) + (vec4<f32>(_e701.x, _e701.y, _e701.z, _e696.w) * (1f - _e708.w))), vec4<f32>(0f, 0f, 0f, 0f), vec4<f32>(1f, 1f, 1f, 1f));
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
