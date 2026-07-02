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
    var phi_225_: vec4<f32>;
    var phi_237_: bool;
    var phi_243_: bool;
    var phi_339_: f32;
    var phi_374_: vec2<f32>;
    var phi_375_: vec2<f32>;
    var phi_376_: vec2<f32>;
    var phi_377_: vec2<f32>;
    var phi_407_: f32;
    var phi_416_: f32;
    var phi_426_: f32;
    var phi_436_: f32;
    var phi_443_: f32;
    var phi_444_: f32;
    var phi_445_: f32;
    var phi_532_: f32;
    var phi_533_: f32;
    var phi_548_: f32;
    var phi_549_: f32;
    var phi_564_: f32;
    var phi_565_: f32;
    var phi_576_: f32;
    var phi_577_: f32;
    var phi_578_: f32;
    var phi_579_: f32;
    var phi_580_: f32;
    var phi_588_: f32;
    var phi_647_: f32;
    var phi_648_: f32;
    var phi_663_: f32;
    var phi_664_: f32;
    var phi_679_: f32;
    var phi_680_: f32;
    var phi_691_: f32;
    var phi_692_: f32;
    var phi_693_: f32;
    var phi_694_: f32;
    var phi_695_: f32;
    var phi_697_: f32;
    var phi_719_: f32;
    var phi_733_: bool;
    var phi_751_: vec3<f32>;
    var phi_752_: vec3<f32>;
    var local: bool;
    var phi_800_: bool;
    var phi_806_: bool;
    var phi_902_: f32;
    var phi_937_: vec2<f32>;
    var phi_938_: vec2<f32>;
    var phi_939_: vec2<f32>;
    var phi_940_: vec2<f32>;
    var phi_970_: f32;
    var phi_979_: f32;
    var phi_989_: f32;
    var phi_999_: f32;
    var phi_1006_: f32;
    var phi_1007_: f32;
    var phi_1008_: f32;
    var phi_1095_: f32;
    var phi_1096_: f32;
    var phi_1111_: f32;
    var phi_1112_: f32;
    var phi_1127_: f32;
    var phi_1128_: f32;
    var phi_1139_: f32;
    var phi_1140_: f32;
    var phi_1141_: f32;
    var phi_1142_: f32;
    var phi_1143_: f32;
    var phi_1151_: f32;
    var phi_1210_: f32;
    var phi_1211_: f32;
    var phi_1226_: f32;
    var phi_1227_: f32;
    var phi_1242_: f32;
    var phi_1243_: f32;
    var phi_1254_: f32;
    var phi_1255_: f32;
    var phi_1256_: f32;
    var phi_1257_: f32;
    var phi_1258_: f32;
    var phi_1260_: f32;
    var local_1: f32;
    var phi_1282_: f32;
    var phi_1296_: bool;
    var phi_1314_: vec3<f32>;
    var phi_1315_: vec3<f32>;

    let _e106 = in_u002e_var_u002e_TEXCOORD0_1;
    let _e107 = in_u002e_var_u002e_TEXCOORD1_1;
    let _e108 = in_u002e_var_u002e_TEXCOORD4_1;
    let _e109 = global;
    if (_e108.x < -1000f) {
        discard;
    }
    let _e112 = textureSample(Texture, Sampler, _e106);
    let _e113 = (_e112 * _e107);
    let _e114 = textureSample(LightTexture, LightSampler, _e106);
    let _e115 = textureSample(TileFieldTexture, TileFieldSampler, _e106);
    let _e116 = textureSample(DitherMaskTexture, DitherMaskSampler, _e106);
    let _e118 = textureSample(EmissiveTexture, EmissiveSampler, _e106);
    let _e119 = textureSample(NormalTexture, NormalSampler, _e106);
    let _e126 = (_e119.w * clamp(length(((_e119.xy * 2f) - vec2<f32>(1f, 1f))), 0f, 1f));
    let _e130 = FragmentUniformBlock.DitherGlitchAxis[2i];
    let _e134 = FragmentUniformBlock.TextureSizeCamera;
    let _e139 = (_e134.zw + (_e109.xy / vec2(select(1f, _e130, (_e130 > 0f)))));
    let _e143 = floor(((_e115.y * 255f) + 0.5f));
    let _e148 = FragmentUniformBlock.DitherAnchorOffsets[i32((_e143 % 32f))];
    let _e150 = (_e139 + _e148.xy);
    let _e154 = FragmentUniformBlock.AoSettings[0i];
    phi_225_ = _e114;
    if (_e154 > 0f) {
        let _e156 = FragmentUniformBlock.AoSettings;
        let _e161 = textureSample(AoTexture, AoSampler, (_e106 + (_e156.yz / _e134.xy)));
        let _e165 = textureSample(BackgroundCoverageTexture, BackgroundCoverageSampler, _e106);
        let _e169 = FragmentUniformBlock.AoSettings[3i];
        let _e173 = (_e114.xyz * mix(1f, _e169, (clamp((_e161.x * _e154), 0f, 1f) * _e165.x)));
        phi_225_ = vec4<f32>(_e173.x, _e173.y, _e173.z, _e114.w);
    }
    let _e180 = phi_225_;
    let _e181 = _e180.xyz;
    switch bitcast<i32>(0u) {
        default: {
            let _e185 = FragmentUniformBlock.DitherSettings[0i];
            let _e186 = (_e185 <= 0f);
            phi_237_ = false;
            local = _e186;
            local_1 = _e185;
            if _e186 {
                let _e189 = FragmentUniformBlock.NormalDitherSettings[0i];
                phi_237_ = (_e189 <= 0f);
            }
            let _e192 = phi_237_;
            phi_243_ = false;
            if _e192 {
                let _e195 = FragmentUniformBlock.NormalDitherSettings[1i];
                phi_243_ = (_e195 <= 1f);
            }
            let _e198 = phi_243_;
            if _e198 {
                phi_752_ = _e181;
                break;
            }
            let _e203 = max(max(_e180.x, _e180.y), _e180.z);
            if (_e203 <= 0.0001f) {
                phi_752_ = _e181;
                break;
            }
            let _e206 = floor((_e143 * 0.03125f));
            let _e210 = floor(((_e115.x * 255f) + 0.5f));
            let _e213 = FragmentUniformBlock.DitherSettings[3i];
            let _e217 = ((_e213 * _e213) * (3f - (2f * _e213)));
            let _e221 = FragmentUniformBlock.DitherGlitch[2i];
            let _e223 = mix(24f, (_e221 * 1.45f), _e217);
            let _e225 = FragmentUniformBlock.DitherGlitch;
            let _e227 = FragmentUniformBlock.DitherGlitchAxis;
            let _e229 = normalize(_e227.xy);
            let _e234 = (_e139 - _e225.xy);
            let _e237 = (dot(_e234, _e229) / max(_e223, 1f));
            let _e240 = (dot(_e234, vec2<f32>(-(_e229.y), _e229.x)) / max((_e223 * 0.38f), 1f));
            let _e251 = (((_e217 * smoothstep(1f, 0.25f, sqrt(((_e237 * _e237) + (_e240 * _e240))))) * 0.82f) + ((_e217 * smoothstep(0.6f, 1f, _e217)) * 0.18f));
            let _e254 = FragmentUniformBlock.DitherGlitch[3i];
            let _e256 = floor((_e254 * 9f));
            let _e258 = floor((_e150 * vec2<f32>(0.0625f, 0.0625f)));
            let _e280 = fract((sin(((((_e210 * 19.19f) + (_e258.x * 7.13f)) + (_e258.y * 3.71f)) + (_e256 * 5.17f))) * 43758.547f));
            let _e281 = (fract((sin(((((_e258.x * 17f) + (_e258.y * 31f)) + (_e256 * 13f)) + (_e210 * 0.23f))) * 43758.547f)) - 0.5f);
            let _e287 = (_e206 == 2f);
            if _e287 {
                phi_339_ = 2f;
            } else {
                phi_339_ = select(1f, 3f, (_e206 == 3f));
            }
            let _e291 = phi_339_;
            let _e292 = (_e210 % 3f);
            let _e297 = FragmentUniformBlock.DitherSettings[1i];
            let _e303 = floor(((_e150 + ((vec2<f32>(_e281, (_e280 - 0.5f)) * _e251) * 9f)) / vec2(max(((_e297 * _e291) * (1f + (_e292 * 0.08f))), 1f))));
            let _e304 = (_e206 == 1f);
            if _e304 {
                phi_377_ = vec2<f32>(-(_e303.x), _e303.y);
            } else {
                if _e287 {
                    phi_376_ = _e303.yx;
                } else {
                    if (_e206 == 3f) {
                        phi_375_ = vec2<f32>(_e303.x, -(_e303.y));
                    } else {
                        phi_374_ = _e303;
                        if (_e206 >= 4f) {
                            phi_374_ = -(_e303.yx);
                        }
                        let _e321 = phi_374_;
                        phi_375_ = _e321;
                    }
                    let _e323 = phi_375_;
                    phi_376_ = _e323;
                }
                let _e325 = phi_376_;
                phi_377_ = _e325;
            }
            let _e327 = phi_377_;
            let _e340 = (_e327 + (vec2<f32>(((_e206 * 2f) % 4f), ((_e206 * 3f) % 4f)) + (vec2<f32>((_e210 % 4f), (floor((_e210 * 0.25f)) % 4f)) * 0.35f)));
            if (_e251 > 0.65f) {
                phi_445_ = floor((_e280 * 11f));
            } else {
                switch bitcast<i32>(0u) {
                    default: {
                        if _e304 {
                            if (_e292 == 0f) {
                                phi_407_ = 8f;
                            } else {
                                phi_407_ = select(5f, 6f, (_e292 == 1f));
                            }
                            let _e349 = phi_407_;
                            phi_444_ = _e349;
                            break;
                        }
                        if _e287 {
                            if (_e292 == 0f) {
                                phi_416_ = 2f;
                            } else {
                                phi_416_ = select(10f, 9f, (_e292 == 1f));
                            }
                            let _e354 = phi_416_;
                            phi_444_ = _e354;
                            break;
                        }
                        if (_e206 == 3f) {
                            if (_e292 == 0f) {
                                phi_426_ = 3f;
                            } else {
                                phi_426_ = select(1f, 5f, (_e292 == 1f));
                            }
                            let _e360 = phi_426_;
                            phi_444_ = _e360;
                            break;
                        }
                        if (_e206 >= 4f) {
                            if (_e292 == 0f) {
                                phi_436_ = 4f;
                            } else {
                                phi_436_ = select(10f, 7f, (_e292 == 1f));
                            }
                            let _e366 = phi_436_;
                            phi_444_ = _e366;
                            break;
                        }
                        if (_e292 == 0f) {
                            phi_443_ = 0f;
                        } else {
                            phi_443_ = select(7f, 4f, (_e292 == 1f));
                        }
                        let _e371 = phi_443_;
                        phi_444_ = _e371;
                        break;
                    }
                }
                let _e373 = phi_444_;
                phi_445_ = _e373;
            }
            let _e375 = phi_445_;
            switch bitcast<i32>(0u) {
                default: {
                    if (_e375 == 1f) {
                        phi_697_ = ((((_e340.x + (_e340.y * 2f)) % 6f) + 1f) * 0.14285715f);
                        break;
                    }
                    if (_e375 == 2f) {
                        let _e387 = ((_e340 % vec2<f32>(4f, 4f)) - vec2<f32>(1.5f, 1.5f));
                        phi_697_ = clamp(((dot(_e387, _e387) + 0.5f) * 0.2f), 0f, 1f);
                        break;
                    }
                    if (_e375 == 3f) {
                        phi_697_ = (((_e340.x % 4f) + 1f) * 0.2f);
                        break;
                    }
                    if (_e375 == 4f) {
                        phi_697_ = fract((sin(dot(floor(_e340), vec2<f32>(127.1f, 311.7f))) * 43758.547f));
                        break;
                    }
                    if (_e375 == 5f) {
                        phi_697_ = (((_e340.y % 4f) + 1f) * 0.2f);
                        break;
                    }
                    if (_e375 == 6f) {
                        phi_697_ = ((min(((_e340.x + _e340.y) % 4f), (((_e340.x - _e340.y) + 8f) % 4f)) + 1f) * 0.2f);
                        break;
                    }
                    if (_e375 == 7f) {
                        let _e420 = (_e340 % vec2<f32>(4f, 4f));
                        let _e429 = i32((_e340.x % 4f));
                        let _e432 = i32((_e340.y % 4f));
                        if (_e429 == 0i) {
                            if (_e432 == 0i) {
                                phi_533_ = 16f;
                            } else {
                                if (_e432 == 1i) {
                                    phi_532_ = 5f;
                                } else {
                                    phi_532_ = select(1f, 13f, (_e432 == 2i));
                                }
                                let _e439 = phi_532_;
                                phi_533_ = _e439;
                            }
                            let _e441 = phi_533_;
                            phi_580_ = _e441;
                        } else {
                            if (_e429 == 1i) {
                                if (_e432 == 0i) {
                                    phi_549_ = 8f;
                                } else {
                                    if (_e432 == 1i) {
                                        phi_548_ = 12f;
                                    } else {
                                        phi_548_ = select(9f, 4f, (_e432 == 2i));
                                    }
                                    let _e448 = phi_548_;
                                    phi_549_ = _e448;
                                }
                                let _e450 = phi_549_;
                                phi_579_ = _e450;
                            } else {
                                if (_e429 == 2i) {
                                    if (_e432 == 0i) {
                                        phi_565_ = 14f;
                                    } else {
                                        if (_e432 == 1i) {
                                            phi_564_ = 2f;
                                        } else {
                                            phi_564_ = select(3f, 15f, (_e432 == 2i));
                                        }
                                        let _e457 = phi_564_;
                                        phi_565_ = _e457;
                                    }
                                    let _e459 = phi_565_;
                                    phi_578_ = _e459;
                                } else {
                                    if (_e432 == 0i) {
                                        phi_577_ = 6f;
                                    } else {
                                        if (_e432 == 1i) {
                                            phi_576_ = 10f;
                                        } else {
                                            phi_576_ = select(11f, 7f, (_e432 == 2i));
                                        }
                                        let _e465 = phi_576_;
                                        phi_577_ = _e465;
                                    }
                                    let _e467 = phi_577_;
                                    phi_578_ = _e467;
                                }
                                let _e469 = phi_578_;
                                phi_579_ = _e469;
                            }
                            let _e471 = phi_579_;
                            phi_580_ = _e471;
                        }
                        let _e473 = phi_580_;
                        if (((floor(_e420.x) + floor(_e420.y)) % 2f) == 0f) {
                            phi_588_ = (_e473 * 0.04411765f);
                        } else {
                            phi_588_ = (0.35f + (_e473 * 0.03823529f));
                        }
                        let _e479 = phi_588_;
                        phi_697_ = _e479;
                        break;
                    }
                    if (_e375 == 8f) {
                        phi_697_ = clamp(((min(((_e340.x + ((floor((_e340.y * 0.5f)) % 2f) * 2f)) % 4f), (_e340.y % 4f)) + 1f) * 0.2f), 0f, 1f);
                        break;
                    }
                    if (_e375 == 9f) {
                        phi_697_ = (((((_e340.x * 2f) + _e340.y) % 8f) + 1f) * 0.11111111f);
                        break;
                    }
                    if (_e375 >= 10f) {
                        let _e505 = abs(((_e340 % vec2<f32>(8f, 8f)) - vec2<f32>(3.5f, 3.5f)));
                        phi_697_ = clamp(((max(_e505.x, _e505.y) - 0.5f) * 0.2857143f), 0f, 1f);
                        break;
                    }
                    let _e514 = i32((_e340.x % 4f));
                    let _e517 = i32((_e340.y % 4f));
                    if (_e514 == 0i) {
                        if (_e517 == 0i) {
                            phi_648_ = 16f;
                        } else {
                            if (_e517 == 1i) {
                                phi_647_ = 5f;
                            } else {
                                phi_647_ = select(1f, 13f, (_e517 == 2i));
                            }
                            let _e524 = phi_647_;
                            phi_648_ = _e524;
                        }
                        let _e526 = phi_648_;
                        phi_695_ = _e526;
                    } else {
                        if (_e514 == 1i) {
                            if (_e517 == 0i) {
                                phi_664_ = 8f;
                            } else {
                                if (_e517 == 1i) {
                                    phi_663_ = 12f;
                                } else {
                                    phi_663_ = select(9f, 4f, (_e517 == 2i));
                                }
                                let _e533 = phi_663_;
                                phi_664_ = _e533;
                            }
                            let _e535 = phi_664_;
                            phi_694_ = _e535;
                        } else {
                            if (_e514 == 2i) {
                                if (_e517 == 0i) {
                                    phi_680_ = 14f;
                                } else {
                                    if (_e517 == 1i) {
                                        phi_679_ = 2f;
                                    } else {
                                        phi_679_ = select(3f, 15f, (_e517 == 2i));
                                    }
                                    let _e542 = phi_679_;
                                    phi_680_ = _e542;
                                }
                                let _e544 = phi_680_;
                                phi_693_ = _e544;
                            } else {
                                if (_e517 == 0i) {
                                    phi_692_ = 6f;
                                } else {
                                    if (_e517 == 1i) {
                                        phi_691_ = 10f;
                                    } else {
                                        phi_691_ = select(11f, 7f, (_e517 == 2i));
                                    }
                                    let _e550 = phi_691_;
                                    phi_692_ = _e550;
                                }
                                let _e552 = phi_692_;
                                phi_693_ = _e552;
                            }
                            let _e554 = phi_693_;
                            phi_694_ = _e554;
                        }
                        let _e556 = phi_694_;
                        phi_695_ = _e556;
                    }
                    let _e558 = phi_695_;
                    phi_697_ = (_e558 * 0.05882353f);
                    break;
                }
            }
            let _e561 = phi_697_;
            let _e571 = clamp((clamp((_e561 + ((fract((_e210 * 0.618034f)) - 0.5f) * 0.1f)), 0f, 1f) + ((_e281 * _e251) * 0.18f)), 0f, 1f);
            if (_e185 > 0f) {
                let _e579 = FragmentUniformBlock.DitherSettings[2i];
                phi_719_ = (_e579 * _e116.x);
            } else {
                phi_719_ = 0f;
            }
            let _e582 = phi_719_;
            let _e585 = FragmentUniformBlock.NormalDitherSettings[0i];
            let _e590 = (_e181 * mix(1f, (select(1f, 0f, (clamp(_e203, 0f, 1f) < _e571)) / _e203), clamp((_e582 + (_e126 * _e585)), 0f, 1f)));
            let _e593 = FragmentUniformBlock.NormalDitherSettings[1i];
            phi_733_ = false;
            if (_e593 > 1f) {
                phi_733_ = (_e126 > 0f);
            }
            let _e597 = phi_733_;
            phi_751_ = _e590;
            if _e597 {
                let _e602 = max(max(_e590.x, _e590.y), _e590.z);
                let _e603 = (_e593 - 1f);
                phi_751_ = (_e590 * mix(1f, ((floor(((clamp(_e602, 0f, 1f) * _e603) + _e571)) / _e603) / max(_e602, 0.0001f)), _e126));
            }
            let _e614 = phi_751_;
            phi_752_ = _e614;
            break;
        }
    }
    let _e616 = phi_752_;
    let _e629 = _e616.xyz;
    let _e637 = clamp(mix(((_e113.xyz * _e629) + (_e629 * ((_e180.w * clamp((1f - (max(max(_e113.x, _e113.y), _e113.z) * 2.2f)), 0f, 1f)) * 0.18f))), _e118.xyz, vec3(_e118.w)), vec3<f32>(0f, 0f, 0f), vec3<f32>(1f, 1f, 1f));
    let _e643 = textureSample(ForegroundTexture, ForegroundSampler, _e106);
    let _e644 = (_e643 * _e107);
    let _e645 = textureSample(ForegroundLightTexture, ForegroundLightSampler, _e106);
    let _e646 = textureSample(ForegroundEmissiveTexture, ForegroundEmissiveSampler, _e106);
    let _e647 = _e645.xyz;
    switch bitcast<i32>(0u) {
        default: {
            let _e650 = local;
            phi_800_ = false;
            if _e650 {
                let _e653 = FragmentUniformBlock.NormalDitherSettings[0i];
                phi_800_ = (_e653 <= 0f);
            }
            let _e656 = phi_800_;
            phi_806_ = false;
            if _e656 {
                let _e659 = FragmentUniformBlock.NormalDitherSettings[1i];
                phi_806_ = (_e659 <= 1f);
            }
            let _e662 = phi_806_;
            if _e662 {
                phi_1315_ = _e647;
                break;
            }
            let _e667 = max(max(_e645.x, _e645.y), _e645.z);
            if (_e667 <= 0.0001f) {
                phi_1315_ = _e647;
                break;
            }
            let _e670 = floor((_e143 * 0.03125f));
            let _e674 = floor(((_e115.x * 255f) + 0.5f));
            let _e677 = FragmentUniformBlock.DitherSettings[3i];
            let _e681 = ((_e677 * _e677) * (3f - (2f * _e677)));
            let _e685 = FragmentUniformBlock.DitherGlitch[2i];
            let _e687 = mix(24f, (_e685 * 1.45f), _e681);
            let _e689 = FragmentUniformBlock.DitherGlitch;
            let _e691 = FragmentUniformBlock.DitherGlitchAxis;
            let _e693 = normalize(_e691.xy);
            let _e698 = (_e139 - _e689.xy);
            let _e701 = (dot(_e698, _e693) / max(_e687, 1f));
            let _e704 = (dot(_e698, vec2<f32>(-(_e693.y), _e693.x)) / max((_e687 * 0.38f), 1f));
            let _e715 = (((_e681 * smoothstep(1f, 0.25f, sqrt(((_e701 * _e701) + (_e704 * _e704))))) * 0.82f) + ((_e681 * smoothstep(0.6f, 1f, _e681)) * 0.18f));
            let _e718 = FragmentUniformBlock.DitherGlitch[3i];
            let _e720 = floor((_e718 * 9f));
            let _e722 = floor((_e150 * vec2<f32>(0.0625f, 0.0625f)));
            let _e744 = fract((sin(((((_e674 * 19.19f) + (_e722.x * 7.13f)) + (_e722.y * 3.71f)) + (_e720 * 5.17f))) * 43758.547f));
            let _e745 = (fract((sin(((((_e722.x * 17f) + (_e722.y * 31f)) + (_e720 * 13f)) + (_e674 * 0.23f))) * 43758.547f)) - 0.5f);
            let _e751 = (_e670 == 2f);
            if _e751 {
                phi_902_ = 2f;
            } else {
                phi_902_ = select(1f, 3f, (_e670 == 3f));
            }
            let _e755 = phi_902_;
            let _e756 = (_e674 % 3f);
            let _e761 = FragmentUniformBlock.DitherSettings[1i];
            let _e767 = floor(((_e150 + ((vec2<f32>(_e745, (_e744 - 0.5f)) * _e715) * 9f)) / vec2(max(((_e761 * _e755) * (1f + (_e756 * 0.08f))), 1f))));
            let _e768 = (_e670 == 1f);
            if _e768 {
                phi_940_ = vec2<f32>(-(_e767.x), _e767.y);
            } else {
                if _e751 {
                    phi_939_ = _e767.yx;
                } else {
                    if (_e670 == 3f) {
                        phi_938_ = vec2<f32>(_e767.x, -(_e767.y));
                    } else {
                        phi_937_ = _e767;
                        if (_e670 >= 4f) {
                            phi_937_ = -(_e767.yx);
                        }
                        let _e785 = phi_937_;
                        phi_938_ = _e785;
                    }
                    let _e787 = phi_938_;
                    phi_939_ = _e787;
                }
                let _e789 = phi_939_;
                phi_940_ = _e789;
            }
            let _e791 = phi_940_;
            let _e804 = (_e791 + (vec2<f32>(((_e670 * 2f) % 4f), ((_e670 * 3f) % 4f)) + (vec2<f32>((_e674 % 4f), (floor((_e674 * 0.25f)) % 4f)) * 0.35f)));
            if (_e715 > 0.65f) {
                phi_1008_ = floor((_e744 * 11f));
            } else {
                switch bitcast<i32>(0u) {
                    default: {
                        if _e768 {
                            if (_e756 == 0f) {
                                phi_970_ = 8f;
                            } else {
                                phi_970_ = select(5f, 6f, (_e756 == 1f));
                            }
                            let _e813 = phi_970_;
                            phi_1007_ = _e813;
                            break;
                        }
                        if _e751 {
                            if (_e756 == 0f) {
                                phi_979_ = 2f;
                            } else {
                                phi_979_ = select(10f, 9f, (_e756 == 1f));
                            }
                            let _e818 = phi_979_;
                            phi_1007_ = _e818;
                            break;
                        }
                        if (_e670 == 3f) {
                            if (_e756 == 0f) {
                                phi_989_ = 3f;
                            } else {
                                phi_989_ = select(1f, 5f, (_e756 == 1f));
                            }
                            let _e824 = phi_989_;
                            phi_1007_ = _e824;
                            break;
                        }
                        if (_e670 >= 4f) {
                            if (_e756 == 0f) {
                                phi_999_ = 4f;
                            } else {
                                phi_999_ = select(10f, 7f, (_e756 == 1f));
                            }
                            let _e830 = phi_999_;
                            phi_1007_ = _e830;
                            break;
                        }
                        if (_e756 == 0f) {
                            phi_1006_ = 0f;
                        } else {
                            phi_1006_ = select(7f, 4f, (_e756 == 1f));
                        }
                        let _e835 = phi_1006_;
                        phi_1007_ = _e835;
                        break;
                    }
                }
                let _e837 = phi_1007_;
                phi_1008_ = _e837;
            }
            let _e839 = phi_1008_;
            switch bitcast<i32>(0u) {
                default: {
                    if (_e839 == 1f) {
                        phi_1260_ = ((((_e804.x + (_e804.y * 2f)) % 6f) + 1f) * 0.14285715f);
                        break;
                    }
                    if (_e839 == 2f) {
                        let _e851 = ((_e804 % vec2<f32>(4f, 4f)) - vec2<f32>(1.5f, 1.5f));
                        phi_1260_ = clamp(((dot(_e851, _e851) + 0.5f) * 0.2f), 0f, 1f);
                        break;
                    }
                    if (_e839 == 3f) {
                        phi_1260_ = (((_e804.x % 4f) + 1f) * 0.2f);
                        break;
                    }
                    if (_e839 == 4f) {
                        phi_1260_ = fract((sin(dot(floor(_e804), vec2<f32>(127.1f, 311.7f))) * 43758.547f));
                        break;
                    }
                    if (_e839 == 5f) {
                        phi_1260_ = (((_e804.y % 4f) + 1f) * 0.2f);
                        break;
                    }
                    if (_e839 == 6f) {
                        phi_1260_ = ((min(((_e804.x + _e804.y) % 4f), (((_e804.x - _e804.y) + 8f) % 4f)) + 1f) * 0.2f);
                        break;
                    }
                    if (_e839 == 7f) {
                        let _e884 = (_e804 % vec2<f32>(4f, 4f));
                        let _e893 = i32((_e804.x % 4f));
                        let _e896 = i32((_e804.y % 4f));
                        if (_e893 == 0i) {
                            if (_e896 == 0i) {
                                phi_1096_ = 16f;
                            } else {
                                if (_e896 == 1i) {
                                    phi_1095_ = 5f;
                                } else {
                                    phi_1095_ = select(1f, 13f, (_e896 == 2i));
                                }
                                let _e903 = phi_1095_;
                                phi_1096_ = _e903;
                            }
                            let _e905 = phi_1096_;
                            phi_1143_ = _e905;
                        } else {
                            if (_e893 == 1i) {
                                if (_e896 == 0i) {
                                    phi_1112_ = 8f;
                                } else {
                                    if (_e896 == 1i) {
                                        phi_1111_ = 12f;
                                    } else {
                                        phi_1111_ = select(9f, 4f, (_e896 == 2i));
                                    }
                                    let _e912 = phi_1111_;
                                    phi_1112_ = _e912;
                                }
                                let _e914 = phi_1112_;
                                phi_1142_ = _e914;
                            } else {
                                if (_e893 == 2i) {
                                    if (_e896 == 0i) {
                                        phi_1128_ = 14f;
                                    } else {
                                        if (_e896 == 1i) {
                                            phi_1127_ = 2f;
                                        } else {
                                            phi_1127_ = select(3f, 15f, (_e896 == 2i));
                                        }
                                        let _e921 = phi_1127_;
                                        phi_1128_ = _e921;
                                    }
                                    let _e923 = phi_1128_;
                                    phi_1141_ = _e923;
                                } else {
                                    if (_e896 == 0i) {
                                        phi_1140_ = 6f;
                                    } else {
                                        if (_e896 == 1i) {
                                            phi_1139_ = 10f;
                                        } else {
                                            phi_1139_ = select(11f, 7f, (_e896 == 2i));
                                        }
                                        let _e929 = phi_1139_;
                                        phi_1140_ = _e929;
                                    }
                                    let _e931 = phi_1140_;
                                    phi_1141_ = _e931;
                                }
                                let _e933 = phi_1141_;
                                phi_1142_ = _e933;
                            }
                            let _e935 = phi_1142_;
                            phi_1143_ = _e935;
                        }
                        let _e937 = phi_1143_;
                        if (((floor(_e884.x) + floor(_e884.y)) % 2f) == 0f) {
                            phi_1151_ = (_e937 * 0.04411765f);
                        } else {
                            phi_1151_ = (0.35f + (_e937 * 0.03823529f));
                        }
                        let _e943 = phi_1151_;
                        phi_1260_ = _e943;
                        break;
                    }
                    if (_e839 == 8f) {
                        phi_1260_ = clamp(((min(((_e804.x + ((floor((_e804.y * 0.5f)) % 2f) * 2f)) % 4f), (_e804.y % 4f)) + 1f) * 0.2f), 0f, 1f);
                        break;
                    }
                    if (_e839 == 9f) {
                        phi_1260_ = (((((_e804.x * 2f) + _e804.y) % 8f) + 1f) * 0.11111111f);
                        break;
                    }
                    if (_e839 >= 10f) {
                        let _e969 = abs(((_e804 % vec2<f32>(8f, 8f)) - vec2<f32>(3.5f, 3.5f)));
                        phi_1260_ = clamp(((max(_e969.x, _e969.y) - 0.5f) * 0.2857143f), 0f, 1f);
                        break;
                    }
                    let _e978 = i32((_e804.x % 4f));
                    let _e981 = i32((_e804.y % 4f));
                    if (_e978 == 0i) {
                        if (_e981 == 0i) {
                            phi_1211_ = 16f;
                        } else {
                            if (_e981 == 1i) {
                                phi_1210_ = 5f;
                            } else {
                                phi_1210_ = select(1f, 13f, (_e981 == 2i));
                            }
                            let _e988 = phi_1210_;
                            phi_1211_ = _e988;
                        }
                        let _e990 = phi_1211_;
                        phi_1258_ = _e990;
                    } else {
                        if (_e978 == 1i) {
                            if (_e981 == 0i) {
                                phi_1227_ = 8f;
                            } else {
                                if (_e981 == 1i) {
                                    phi_1226_ = 12f;
                                } else {
                                    phi_1226_ = select(9f, 4f, (_e981 == 2i));
                                }
                                let _e997 = phi_1226_;
                                phi_1227_ = _e997;
                            }
                            let _e999 = phi_1227_;
                            phi_1257_ = _e999;
                        } else {
                            if (_e978 == 2i) {
                                if (_e981 == 0i) {
                                    phi_1243_ = 14f;
                                } else {
                                    if (_e981 == 1i) {
                                        phi_1242_ = 2f;
                                    } else {
                                        phi_1242_ = select(3f, 15f, (_e981 == 2i));
                                    }
                                    let _e1006 = phi_1242_;
                                    phi_1243_ = _e1006;
                                }
                                let _e1008 = phi_1243_;
                                phi_1256_ = _e1008;
                            } else {
                                if (_e981 == 0i) {
                                    phi_1255_ = 6f;
                                } else {
                                    if (_e981 == 1i) {
                                        phi_1254_ = 10f;
                                    } else {
                                        phi_1254_ = select(11f, 7f, (_e981 == 2i));
                                    }
                                    let _e1014 = phi_1254_;
                                    phi_1255_ = _e1014;
                                }
                                let _e1016 = phi_1255_;
                                phi_1256_ = _e1016;
                            }
                            let _e1018 = phi_1256_;
                            phi_1257_ = _e1018;
                        }
                        let _e1020 = phi_1257_;
                        phi_1258_ = _e1020;
                    }
                    let _e1022 = phi_1258_;
                    phi_1260_ = (_e1022 * 0.05882353f);
                    break;
                }
            }
            let _e1025 = phi_1260_;
            let _e1035 = clamp((clamp((_e1025 + ((fract((_e674 * 0.618034f)) - 0.5f) * 0.1f)), 0f, 1f) + ((_e745 * _e715) * 0.18f)), 0f, 1f);
            let _e1041 = local_1;
            if (_e1041 > 0f) {
                let _e1045 = FragmentUniformBlock.DitherSettings[2i];
                phi_1282_ = (_e1045 * _e116.x);
            } else {
                phi_1282_ = 0f;
            }
            let _e1048 = phi_1282_;
            let _e1051 = FragmentUniformBlock.NormalDitherSettings[0i];
            let _e1056 = (_e647 * mix(1f, (select(1f, 0f, (clamp(_e667, 0f, 1f) < _e1035)) / _e667), clamp((_e1048 + (_e126 * _e1051)), 0f, 1f)));
            let _e1059 = FragmentUniformBlock.NormalDitherSettings[1i];
            phi_1296_ = false;
            if (_e1059 > 1f) {
                phi_1296_ = (_e126 > 0f);
            }
            let _e1063 = phi_1296_;
            phi_1314_ = _e1056;
            if _e1063 {
                let _e1068 = max(max(_e1056.x, _e1056.y), _e1056.z);
                let _e1069 = (_e1059 - 1f);
                phi_1314_ = (_e1056 * mix(1f, ((floor(((clamp(_e1068, 0f, 1f) * _e1069) + _e1035)) / _e1069) / max(_e1068, 0.0001f)), _e126));
            }
            let _e1080 = phi_1314_;
            phi_1315_ = _e1080;
            break;
        }
    }
    let _e1082 = phi_1315_;
    let _e1095 = _e1082.xyz;
    let _e1103 = clamp(mix(((_e644.xyz * _e1095) + (_e1095 * ((_e645.w * clamp((1f - (max(max(_e644.x, _e644.y), _e644.z) * 2.2f)), 0f, 1f)) * 0.18f))), _e646.xyz, vec3(_e646.w)), vec3<f32>(0f, 0f, 0f), vec3<f32>(1f, 1f, 1f));
    out_u002e_var_u002e_SV_Target0_ = clamp((vec4<f32>(_e1103.x, _e1103.y, _e1103.z, _e644.w) + (vec4<f32>(_e637.x, _e637.y, _e637.z, _e113.w) * (1f - _e644.w))), vec4<f32>(0f, 0f, 0f, 0f), vec4<f32>(1f, 1f, 1f, 1f));
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
