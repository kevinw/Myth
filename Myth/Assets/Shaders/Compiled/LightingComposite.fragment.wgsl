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
var<private> in_u002e_var_u002e_TEXCOORD0_1: vec2<f32>;
var<private> in_u002e_var_u002e_TEXCOORD1_1: vec4<f32>;
var<private> in_u002e_var_u002e_TEXCOORD4_1: vec4<f32>;
var<private> global: vec4<f32>;
var<private> out_u002e_var_u002e_SV_Target0_: vec4<f32>;

fn fragment_main_1() {
    var phi_227_: vec4<f32>;
    var phi_239_: bool;
    var phi_245_: bool;
    var phi_341_: f32;
    var phi_376_: vec2<f32>;
    var phi_377_: vec2<f32>;
    var phi_378_: vec2<f32>;
    var phi_379_: vec2<f32>;
    var phi_409_: f32;
    var phi_418_: f32;
    var phi_428_: f32;
    var phi_438_: f32;
    var phi_445_: f32;
    var phi_446_: f32;
    var phi_447_: f32;
    var phi_534_: f32;
    var phi_535_: f32;
    var phi_550_: f32;
    var phi_551_: f32;
    var phi_566_: f32;
    var phi_567_: f32;
    var phi_578_: f32;
    var phi_579_: f32;
    var phi_580_: f32;
    var phi_581_: f32;
    var phi_582_: f32;
    var phi_590_: f32;
    var phi_649_: f32;
    var phi_650_: f32;
    var phi_665_: f32;
    var phi_666_: f32;
    var phi_681_: f32;
    var phi_682_: f32;
    var phi_693_: f32;
    var phi_694_: f32;
    var phi_695_: f32;
    var phi_696_: f32;
    var phi_697_: f32;
    var phi_699_: f32;
    var phi_721_: f32;
    var phi_735_: bool;
    var phi_753_: vec3<f32>;
    var phi_754_: vec3<f32>;
    var local: bool;
    var phi_811_: bool;
    var phi_817_: bool;
    var phi_913_: f32;
    var phi_948_: vec2<f32>;
    var phi_949_: vec2<f32>;
    var phi_950_: vec2<f32>;
    var phi_951_: vec2<f32>;
    var phi_981_: f32;
    var phi_990_: f32;
    var phi_1000_: f32;
    var phi_1010_: f32;
    var phi_1017_: f32;
    var phi_1018_: f32;
    var phi_1019_: f32;
    var phi_1106_: f32;
    var phi_1107_: f32;
    var phi_1122_: f32;
    var phi_1123_: f32;
    var phi_1138_: f32;
    var phi_1139_: f32;
    var phi_1150_: f32;
    var phi_1151_: f32;
    var phi_1152_: f32;
    var phi_1153_: f32;
    var phi_1154_: f32;
    var phi_1162_: f32;
    var phi_1221_: f32;
    var phi_1222_: f32;
    var phi_1237_: f32;
    var phi_1238_: f32;
    var phi_1253_: f32;
    var phi_1254_: f32;
    var phi_1265_: f32;
    var phi_1266_: f32;
    var phi_1267_: f32;
    var phi_1268_: f32;
    var phi_1269_: f32;
    var phi_1271_: f32;
    var local_1: f32;
    var phi_1293_: f32;
    var phi_1307_: bool;
    var phi_1325_: vec3<f32>;
    var phi_1326_: vec3<f32>;

    let _e108 = in_u002e_var_u002e_TEXCOORD0_1;
    let _e109 = in_u002e_var_u002e_TEXCOORD1_1;
    let _e110 = in_u002e_var_u002e_TEXCOORD4_1;
    let _e111 = global;
    if (_e110.x < -1000f) {
        discard;
    }
    let _e114 = textureSample(Texture, Sampler, _e108);
    let _e115 = (_e114 * _e109);
    let _e116 = textureSample(LightTexture, LightSampler, _e108);
    let _e117 = textureSample(TileFieldTexture, TileFieldSampler, _e108);
    let _e118 = textureSample(DitherMaskTexture, DitherMaskSampler, _e108);
    let _e120 = textureSample(EmissiveTexture, EmissiveSampler, _e108);
    let _e121 = textureSample(NormalTexture, NormalSampler, _e108);
    let _e128 = (_e121.w * clamp(length(((_e121.xy * 2f) - vec2<f32>(1f, 1f))), 0f, 1f));
    let _e132 = FragmentUniformBlock.DitherGlitchAxis[2i];
    let _e136 = FragmentUniformBlock.TextureSizeCamera;
    let _e141 = (_e136.zw + (_e111.xy / vec2(select(1f, _e132, (_e132 > 0f)))));
    let _e145 = floor(((_e117.y * 255f) + 0.5f));
    let _e150 = FragmentUniformBlock.DitherAnchorOffsets[i32((_e145 % 32f))];
    let _e152 = (_e141 + _e150.xy);
    let _e156 = FragmentUniformBlock.AoSettings[0i];
    phi_227_ = _e116;
    if (_e156 > 0f) {
        let _e158 = FragmentUniformBlock.AoSettings;
        let _e163 = textureSample(AoTexture, AoSampler, (_e108 + (_e158.yz / _e136.xy)));
        let _e167 = textureSample(BackgroundCoverageTexture, BackgroundCoverageSampler, _e108);
        let _e171 = FragmentUniformBlock.AoSettings[3i];
        let _e175 = (_e116.xyz * mix(1f, _e171, (clamp((_e163.x * _e156), 0f, 1f) * _e167.x)));
        phi_227_ = vec4<f32>(_e175.x, _e175.y, _e175.z, _e116.w);
    }
    let _e182 = phi_227_;
    let _e183 = _e182.xyz;
    switch bitcast<i32>(0u) {
        default: {
            let _e187 = FragmentUniformBlock.DitherSettings[0i];
            let _e188 = (_e187 <= 0f);
            phi_239_ = false;
            local = _e188;
            local_1 = _e187;
            if _e188 {
                let _e191 = FragmentUniformBlock.NormalDitherSettings[0i];
                phi_239_ = (_e191 <= 0f);
            }
            let _e194 = phi_239_;
            phi_245_ = false;
            if _e194 {
                let _e197 = FragmentUniformBlock.NormalDitherSettings[1i];
                phi_245_ = (_e197 <= 1f);
            }
            let _e200 = phi_245_;
            if _e200 {
                phi_754_ = _e183;
                break;
            }
            let _e205 = max(max(_e182.x, _e182.y), _e182.z);
            if (_e205 <= 0.0001f) {
                phi_754_ = _e183;
                break;
            }
            let _e208 = floor((_e145 * 0.03125f));
            let _e212 = floor(((_e117.x * 255f) + 0.5f));
            let _e215 = FragmentUniformBlock.DitherSettings[3i];
            let _e219 = ((_e215 * _e215) * (3f - (2f * _e215)));
            let _e223 = FragmentUniformBlock.DitherGlitch[2i];
            let _e225 = mix(24f, (_e223 * 1.45f), _e219);
            let _e227 = FragmentUniformBlock.DitherGlitch;
            let _e229 = FragmentUniformBlock.DitherGlitchAxis;
            let _e231 = normalize(_e229.xy);
            let _e236 = (_e141 - _e227.xy);
            let _e239 = (dot(_e236, _e231) / max(_e225, 1f));
            let _e242 = (dot(_e236, vec2<f32>(-(_e231.y), _e231.x)) / max((_e225 * 0.38f), 1f));
            let _e253 = (((_e219 * smoothstep(1f, 0.25f, sqrt(((_e239 * _e239) + (_e242 * _e242))))) * 0.82f) + ((_e219 * smoothstep(0.6f, 1f, _e219)) * 0.18f));
            let _e256 = FragmentUniformBlock.DitherGlitch[3i];
            let _e258 = floor((_e256 * 9f));
            let _e260 = floor((_e152 * vec2<f32>(0.0625f, 0.0625f)));
            let _e282 = fract((sin(((((_e212 * 19.19f) + (_e260.x * 7.13f)) + (_e260.y * 3.71f)) + (_e258 * 5.17f))) * 43758.547f));
            let _e283 = (fract((sin(((((_e260.x * 17f) + (_e260.y * 31f)) + (_e258 * 13f)) + (_e212 * 0.23f))) * 43758.547f)) - 0.5f);
            let _e289 = (_e208 == 2f);
            if _e289 {
                phi_341_ = 2f;
            } else {
                phi_341_ = select(1f, 3f, (_e208 == 3f));
            }
            let _e293 = phi_341_;
            let _e294 = (_e212 % 3f);
            let _e299 = FragmentUniformBlock.DitherSettings[1i];
            let _e305 = floor(((_e152 + ((vec2<f32>(_e283, (_e282 - 0.5f)) * _e253) * 9f)) / vec2(max(((_e299 * _e293) * (1f + (_e294 * 0.08f))), 1f))));
            let _e306 = (_e208 == 1f);
            if _e306 {
                phi_379_ = vec2<f32>(-(_e305.x), _e305.y);
            } else {
                if _e289 {
                    phi_378_ = _e305.yx;
                } else {
                    if (_e208 == 3f) {
                        phi_377_ = vec2<f32>(_e305.x, -(_e305.y));
                    } else {
                        phi_376_ = _e305;
                        if (_e208 >= 4f) {
                            phi_376_ = -(_e305.yx);
                        }
                        let _e323 = phi_376_;
                        phi_377_ = _e323;
                    }
                    let _e325 = phi_377_;
                    phi_378_ = _e325;
                }
                let _e327 = phi_378_;
                phi_379_ = _e327;
            }
            let _e329 = phi_379_;
            let _e342 = (_e329 + (vec2<f32>(((_e208 * 2f) % 4f), ((_e208 * 3f) % 4f)) + (vec2<f32>((_e212 % 4f), (floor((_e212 * 0.25f)) % 4f)) * 0.35f)));
            if (_e253 > 0.65f) {
                phi_447_ = floor((_e282 * 11f));
            } else {
                switch bitcast<i32>(0u) {
                    default: {
                        if _e306 {
                            if (_e294 == 0f) {
                                phi_409_ = 8f;
                            } else {
                                phi_409_ = select(5f, 6f, (_e294 == 1f));
                            }
                            let _e351 = phi_409_;
                            phi_446_ = _e351;
                            break;
                        }
                        if _e289 {
                            if (_e294 == 0f) {
                                phi_418_ = 2f;
                            } else {
                                phi_418_ = select(10f, 9f, (_e294 == 1f));
                            }
                            let _e356 = phi_418_;
                            phi_446_ = _e356;
                            break;
                        }
                        if (_e208 == 3f) {
                            if (_e294 == 0f) {
                                phi_428_ = 3f;
                            } else {
                                phi_428_ = select(1f, 5f, (_e294 == 1f));
                            }
                            let _e362 = phi_428_;
                            phi_446_ = _e362;
                            break;
                        }
                        if (_e208 >= 4f) {
                            if (_e294 == 0f) {
                                phi_438_ = 4f;
                            } else {
                                phi_438_ = select(10f, 7f, (_e294 == 1f));
                            }
                            let _e368 = phi_438_;
                            phi_446_ = _e368;
                            break;
                        }
                        if (_e294 == 0f) {
                            phi_445_ = 0f;
                        } else {
                            phi_445_ = select(7f, 4f, (_e294 == 1f));
                        }
                        let _e373 = phi_445_;
                        phi_446_ = _e373;
                        break;
                    }
                }
                let _e375 = phi_446_;
                phi_447_ = _e375;
            }
            let _e377 = phi_447_;
            switch bitcast<i32>(0u) {
                default: {
                    if (_e377 == 1f) {
                        phi_699_ = ((((_e342.x + (_e342.y * 2f)) % 6f) + 1f) * 0.14285715f);
                        break;
                    }
                    if (_e377 == 2f) {
                        let _e389 = ((_e342 % vec2<f32>(4f, 4f)) - vec2<f32>(1.5f, 1.5f));
                        phi_699_ = clamp(((dot(_e389, _e389) + 0.5f) * 0.2f), 0f, 1f);
                        break;
                    }
                    if (_e377 == 3f) {
                        phi_699_ = (((_e342.x % 4f) + 1f) * 0.2f);
                        break;
                    }
                    if (_e377 == 4f) {
                        phi_699_ = fract((sin(dot(floor(_e342), vec2<f32>(127.1f, 311.7f))) * 43758.547f));
                        break;
                    }
                    if (_e377 == 5f) {
                        phi_699_ = (((_e342.y % 4f) + 1f) * 0.2f);
                        break;
                    }
                    if (_e377 == 6f) {
                        phi_699_ = ((min(((_e342.x + _e342.y) % 4f), (((_e342.x - _e342.y) + 8f) % 4f)) + 1f) * 0.2f);
                        break;
                    }
                    if (_e377 == 7f) {
                        let _e422 = (_e342 % vec2<f32>(4f, 4f));
                        let _e431 = i32((_e342.x % 4f));
                        let _e434 = i32((_e342.y % 4f));
                        if (_e431 == 0i) {
                            if (_e434 == 0i) {
                                phi_535_ = 16f;
                            } else {
                                if (_e434 == 1i) {
                                    phi_534_ = 5f;
                                } else {
                                    phi_534_ = select(1f, 13f, (_e434 == 2i));
                                }
                                let _e441 = phi_534_;
                                phi_535_ = _e441;
                            }
                            let _e443 = phi_535_;
                            phi_582_ = _e443;
                        } else {
                            if (_e431 == 1i) {
                                if (_e434 == 0i) {
                                    phi_551_ = 8f;
                                } else {
                                    if (_e434 == 1i) {
                                        phi_550_ = 12f;
                                    } else {
                                        phi_550_ = select(9f, 4f, (_e434 == 2i));
                                    }
                                    let _e450 = phi_550_;
                                    phi_551_ = _e450;
                                }
                                let _e452 = phi_551_;
                                phi_581_ = _e452;
                            } else {
                                if (_e431 == 2i) {
                                    if (_e434 == 0i) {
                                        phi_567_ = 14f;
                                    } else {
                                        if (_e434 == 1i) {
                                            phi_566_ = 2f;
                                        } else {
                                            phi_566_ = select(3f, 15f, (_e434 == 2i));
                                        }
                                        let _e459 = phi_566_;
                                        phi_567_ = _e459;
                                    }
                                    let _e461 = phi_567_;
                                    phi_580_ = _e461;
                                } else {
                                    if (_e434 == 0i) {
                                        phi_579_ = 6f;
                                    } else {
                                        if (_e434 == 1i) {
                                            phi_578_ = 10f;
                                        } else {
                                            phi_578_ = select(11f, 7f, (_e434 == 2i));
                                        }
                                        let _e467 = phi_578_;
                                        phi_579_ = _e467;
                                    }
                                    let _e469 = phi_579_;
                                    phi_580_ = _e469;
                                }
                                let _e471 = phi_580_;
                                phi_581_ = _e471;
                            }
                            let _e473 = phi_581_;
                            phi_582_ = _e473;
                        }
                        let _e475 = phi_582_;
                        if (((floor(_e422.x) + floor(_e422.y)) % 2f) == 0f) {
                            phi_590_ = (_e475 * 0.04411765f);
                        } else {
                            phi_590_ = (0.35f + (_e475 * 0.03823529f));
                        }
                        let _e481 = phi_590_;
                        phi_699_ = _e481;
                        break;
                    }
                    if (_e377 == 8f) {
                        phi_699_ = clamp(((min(((_e342.x + ((floor((_e342.y * 0.5f)) % 2f) * 2f)) % 4f), (_e342.y % 4f)) + 1f) * 0.2f), 0f, 1f);
                        break;
                    }
                    if (_e377 == 9f) {
                        phi_699_ = (((((_e342.x * 2f) + _e342.y) % 8f) + 1f) * 0.11111111f);
                        break;
                    }
                    if (_e377 >= 10f) {
                        let _e507 = abs(((_e342 % vec2<f32>(8f, 8f)) - vec2<f32>(3.5f, 3.5f)));
                        phi_699_ = clamp(((max(_e507.x, _e507.y) - 0.5f) * 0.2857143f), 0f, 1f);
                        break;
                    }
                    let _e516 = i32((_e342.x % 4f));
                    let _e519 = i32((_e342.y % 4f));
                    if (_e516 == 0i) {
                        if (_e519 == 0i) {
                            phi_650_ = 16f;
                        } else {
                            if (_e519 == 1i) {
                                phi_649_ = 5f;
                            } else {
                                phi_649_ = select(1f, 13f, (_e519 == 2i));
                            }
                            let _e526 = phi_649_;
                            phi_650_ = _e526;
                        }
                        let _e528 = phi_650_;
                        phi_697_ = _e528;
                    } else {
                        if (_e516 == 1i) {
                            if (_e519 == 0i) {
                                phi_666_ = 8f;
                            } else {
                                if (_e519 == 1i) {
                                    phi_665_ = 12f;
                                } else {
                                    phi_665_ = select(9f, 4f, (_e519 == 2i));
                                }
                                let _e535 = phi_665_;
                                phi_666_ = _e535;
                            }
                            let _e537 = phi_666_;
                            phi_696_ = _e537;
                        } else {
                            if (_e516 == 2i) {
                                if (_e519 == 0i) {
                                    phi_682_ = 14f;
                                } else {
                                    if (_e519 == 1i) {
                                        phi_681_ = 2f;
                                    } else {
                                        phi_681_ = select(3f, 15f, (_e519 == 2i));
                                    }
                                    let _e544 = phi_681_;
                                    phi_682_ = _e544;
                                }
                                let _e546 = phi_682_;
                                phi_695_ = _e546;
                            } else {
                                if (_e519 == 0i) {
                                    phi_694_ = 6f;
                                } else {
                                    if (_e519 == 1i) {
                                        phi_693_ = 10f;
                                    } else {
                                        phi_693_ = select(11f, 7f, (_e519 == 2i));
                                    }
                                    let _e552 = phi_693_;
                                    phi_694_ = _e552;
                                }
                                let _e554 = phi_694_;
                                phi_695_ = _e554;
                            }
                            let _e556 = phi_695_;
                            phi_696_ = _e556;
                        }
                        let _e558 = phi_696_;
                        phi_697_ = _e558;
                    }
                    let _e560 = phi_697_;
                    phi_699_ = (_e560 * 0.05882353f);
                    break;
                }
            }
            let _e563 = phi_699_;
            let _e573 = clamp((clamp((_e563 + ((fract((_e212 * 0.618034f)) - 0.5f) * 0.1f)), 0f, 1f) + ((_e283 * _e253) * 0.18f)), 0f, 1f);
            if (_e187 > 0f) {
                let _e581 = FragmentUniformBlock.DitherSettings[2i];
                phi_721_ = (_e581 * _e118.x);
            } else {
                phi_721_ = 0f;
            }
            let _e584 = phi_721_;
            let _e587 = FragmentUniformBlock.NormalDitherSettings[0i];
            let _e592 = (_e183 * mix(1f, (select(1f, 0f, (clamp(_e205, 0f, 1f) < _e573)) / _e205), clamp((_e584 + (_e128 * _e587)), 0f, 1f)));
            let _e595 = FragmentUniformBlock.NormalDitherSettings[1i];
            phi_735_ = false;
            if (_e595 > 1f) {
                phi_735_ = (_e128 > 0f);
            }
            let _e599 = phi_735_;
            phi_753_ = _e592;
            if _e599 {
                let _e604 = max(max(_e592.x, _e592.y), _e592.z);
                let _e605 = (_e595 - 1f);
                phi_753_ = (_e592 * mix(1f, ((floor(((clamp(_e604, 0f, 1f) * _e605) + _e573)) / _e605) / max(_e604, 0.0001f)), _e128));
            }
            let _e616 = phi_753_;
            phi_754_ = _e616;
            break;
        }
    }
    let _e618 = phi_754_;
    let _e631 = _e618.xyz;
    let _e639 = clamp(mix(((_e115.xyz * _e631) + (_e631 * ((_e182.w * clamp((1f - (max(max(_e115.x, _e115.y), _e115.z) * 2.2f)), 0f, 1f)) * 0.18f))), _e120.xyz, vec3(_e120.w)), vec3<f32>(0f, 0f, 0f), vec3<f32>(1f, 1f, 1f));
    let _e644 = vec4<f32>(_e639.x, _e639.y, _e639.z, _e115.w);
    let _e646 = textureSample(SporeFogTexture, SporeFogSampler, _e108);
    let _e649 = clamp((_e644.xyz + _e646.xyz), vec3<f32>(0f, 0f, 0f), vec3<f32>(1f, 1f, 1f));
    let _e655 = textureSample(ForegroundTexture, ForegroundSampler, _e108);
    let _e656 = (_e655 * _e109);
    let _e657 = textureSample(ForegroundLightTexture, ForegroundLightSampler, _e108);
    let _e658 = textureSample(ForegroundEmissiveTexture, ForegroundEmissiveSampler, _e108);
    let _e659 = _e657.xyz;
    switch bitcast<i32>(0u) {
        default: {
            let _e662 = local;
            phi_811_ = false;
            if _e662 {
                let _e665 = FragmentUniformBlock.NormalDitherSettings[0i];
                phi_811_ = (_e665 <= 0f);
            }
            let _e668 = phi_811_;
            phi_817_ = false;
            if _e668 {
                let _e671 = FragmentUniformBlock.NormalDitherSettings[1i];
                phi_817_ = (_e671 <= 1f);
            }
            let _e674 = phi_817_;
            if _e674 {
                phi_1326_ = _e659;
                break;
            }
            let _e679 = max(max(_e657.x, _e657.y), _e657.z);
            if (_e679 <= 0.0001f) {
                phi_1326_ = _e659;
                break;
            }
            let _e682 = floor((_e145 * 0.03125f));
            let _e686 = floor(((_e117.x * 255f) + 0.5f));
            let _e689 = FragmentUniformBlock.DitherSettings[3i];
            let _e693 = ((_e689 * _e689) * (3f - (2f * _e689)));
            let _e697 = FragmentUniformBlock.DitherGlitch[2i];
            let _e699 = mix(24f, (_e697 * 1.45f), _e693);
            let _e701 = FragmentUniformBlock.DitherGlitch;
            let _e703 = FragmentUniformBlock.DitherGlitchAxis;
            let _e705 = normalize(_e703.xy);
            let _e710 = (_e141 - _e701.xy);
            let _e713 = (dot(_e710, _e705) / max(_e699, 1f));
            let _e716 = (dot(_e710, vec2<f32>(-(_e705.y), _e705.x)) / max((_e699 * 0.38f), 1f));
            let _e727 = (((_e693 * smoothstep(1f, 0.25f, sqrt(((_e713 * _e713) + (_e716 * _e716))))) * 0.82f) + ((_e693 * smoothstep(0.6f, 1f, _e693)) * 0.18f));
            let _e730 = FragmentUniformBlock.DitherGlitch[3i];
            let _e732 = floor((_e730 * 9f));
            let _e734 = floor((_e152 * vec2<f32>(0.0625f, 0.0625f)));
            let _e756 = fract((sin(((((_e686 * 19.19f) + (_e734.x * 7.13f)) + (_e734.y * 3.71f)) + (_e732 * 5.17f))) * 43758.547f));
            let _e757 = (fract((sin(((((_e734.x * 17f) + (_e734.y * 31f)) + (_e732 * 13f)) + (_e686 * 0.23f))) * 43758.547f)) - 0.5f);
            let _e763 = (_e682 == 2f);
            if _e763 {
                phi_913_ = 2f;
            } else {
                phi_913_ = select(1f, 3f, (_e682 == 3f));
            }
            let _e767 = phi_913_;
            let _e768 = (_e686 % 3f);
            let _e773 = FragmentUniformBlock.DitherSettings[1i];
            let _e779 = floor(((_e152 + ((vec2<f32>(_e757, (_e756 - 0.5f)) * _e727) * 9f)) / vec2(max(((_e773 * _e767) * (1f + (_e768 * 0.08f))), 1f))));
            let _e780 = (_e682 == 1f);
            if _e780 {
                phi_951_ = vec2<f32>(-(_e779.x), _e779.y);
            } else {
                if _e763 {
                    phi_950_ = _e779.yx;
                } else {
                    if (_e682 == 3f) {
                        phi_949_ = vec2<f32>(_e779.x, -(_e779.y));
                    } else {
                        phi_948_ = _e779;
                        if (_e682 >= 4f) {
                            phi_948_ = -(_e779.yx);
                        }
                        let _e797 = phi_948_;
                        phi_949_ = _e797;
                    }
                    let _e799 = phi_949_;
                    phi_950_ = _e799;
                }
                let _e801 = phi_950_;
                phi_951_ = _e801;
            }
            let _e803 = phi_951_;
            let _e816 = (_e803 + (vec2<f32>(((_e682 * 2f) % 4f), ((_e682 * 3f) % 4f)) + (vec2<f32>((_e686 % 4f), (floor((_e686 * 0.25f)) % 4f)) * 0.35f)));
            if (_e727 > 0.65f) {
                phi_1019_ = floor((_e756 * 11f));
            } else {
                switch bitcast<i32>(0u) {
                    default: {
                        if _e780 {
                            if (_e768 == 0f) {
                                phi_981_ = 8f;
                            } else {
                                phi_981_ = select(5f, 6f, (_e768 == 1f));
                            }
                            let _e825 = phi_981_;
                            phi_1018_ = _e825;
                            break;
                        }
                        if _e763 {
                            if (_e768 == 0f) {
                                phi_990_ = 2f;
                            } else {
                                phi_990_ = select(10f, 9f, (_e768 == 1f));
                            }
                            let _e830 = phi_990_;
                            phi_1018_ = _e830;
                            break;
                        }
                        if (_e682 == 3f) {
                            if (_e768 == 0f) {
                                phi_1000_ = 3f;
                            } else {
                                phi_1000_ = select(1f, 5f, (_e768 == 1f));
                            }
                            let _e836 = phi_1000_;
                            phi_1018_ = _e836;
                            break;
                        }
                        if (_e682 >= 4f) {
                            if (_e768 == 0f) {
                                phi_1010_ = 4f;
                            } else {
                                phi_1010_ = select(10f, 7f, (_e768 == 1f));
                            }
                            let _e842 = phi_1010_;
                            phi_1018_ = _e842;
                            break;
                        }
                        if (_e768 == 0f) {
                            phi_1017_ = 0f;
                        } else {
                            phi_1017_ = select(7f, 4f, (_e768 == 1f));
                        }
                        let _e847 = phi_1017_;
                        phi_1018_ = _e847;
                        break;
                    }
                }
                let _e849 = phi_1018_;
                phi_1019_ = _e849;
            }
            let _e851 = phi_1019_;
            switch bitcast<i32>(0u) {
                default: {
                    if (_e851 == 1f) {
                        phi_1271_ = ((((_e816.x + (_e816.y * 2f)) % 6f) + 1f) * 0.14285715f);
                        break;
                    }
                    if (_e851 == 2f) {
                        let _e863 = ((_e816 % vec2<f32>(4f, 4f)) - vec2<f32>(1.5f, 1.5f));
                        phi_1271_ = clamp(((dot(_e863, _e863) + 0.5f) * 0.2f), 0f, 1f);
                        break;
                    }
                    if (_e851 == 3f) {
                        phi_1271_ = (((_e816.x % 4f) + 1f) * 0.2f);
                        break;
                    }
                    if (_e851 == 4f) {
                        phi_1271_ = fract((sin(dot(floor(_e816), vec2<f32>(127.1f, 311.7f))) * 43758.547f));
                        break;
                    }
                    if (_e851 == 5f) {
                        phi_1271_ = (((_e816.y % 4f) + 1f) * 0.2f);
                        break;
                    }
                    if (_e851 == 6f) {
                        phi_1271_ = ((min(((_e816.x + _e816.y) % 4f), (((_e816.x - _e816.y) + 8f) % 4f)) + 1f) * 0.2f);
                        break;
                    }
                    if (_e851 == 7f) {
                        let _e896 = (_e816 % vec2<f32>(4f, 4f));
                        let _e905 = i32((_e816.x % 4f));
                        let _e908 = i32((_e816.y % 4f));
                        if (_e905 == 0i) {
                            if (_e908 == 0i) {
                                phi_1107_ = 16f;
                            } else {
                                if (_e908 == 1i) {
                                    phi_1106_ = 5f;
                                } else {
                                    phi_1106_ = select(1f, 13f, (_e908 == 2i));
                                }
                                let _e915 = phi_1106_;
                                phi_1107_ = _e915;
                            }
                            let _e917 = phi_1107_;
                            phi_1154_ = _e917;
                        } else {
                            if (_e905 == 1i) {
                                if (_e908 == 0i) {
                                    phi_1123_ = 8f;
                                } else {
                                    if (_e908 == 1i) {
                                        phi_1122_ = 12f;
                                    } else {
                                        phi_1122_ = select(9f, 4f, (_e908 == 2i));
                                    }
                                    let _e924 = phi_1122_;
                                    phi_1123_ = _e924;
                                }
                                let _e926 = phi_1123_;
                                phi_1153_ = _e926;
                            } else {
                                if (_e905 == 2i) {
                                    if (_e908 == 0i) {
                                        phi_1139_ = 14f;
                                    } else {
                                        if (_e908 == 1i) {
                                            phi_1138_ = 2f;
                                        } else {
                                            phi_1138_ = select(3f, 15f, (_e908 == 2i));
                                        }
                                        let _e933 = phi_1138_;
                                        phi_1139_ = _e933;
                                    }
                                    let _e935 = phi_1139_;
                                    phi_1152_ = _e935;
                                } else {
                                    if (_e908 == 0i) {
                                        phi_1151_ = 6f;
                                    } else {
                                        if (_e908 == 1i) {
                                            phi_1150_ = 10f;
                                        } else {
                                            phi_1150_ = select(11f, 7f, (_e908 == 2i));
                                        }
                                        let _e941 = phi_1150_;
                                        phi_1151_ = _e941;
                                    }
                                    let _e943 = phi_1151_;
                                    phi_1152_ = _e943;
                                }
                                let _e945 = phi_1152_;
                                phi_1153_ = _e945;
                            }
                            let _e947 = phi_1153_;
                            phi_1154_ = _e947;
                        }
                        let _e949 = phi_1154_;
                        if (((floor(_e896.x) + floor(_e896.y)) % 2f) == 0f) {
                            phi_1162_ = (_e949 * 0.04411765f);
                        } else {
                            phi_1162_ = (0.35f + (_e949 * 0.03823529f));
                        }
                        let _e955 = phi_1162_;
                        phi_1271_ = _e955;
                        break;
                    }
                    if (_e851 == 8f) {
                        phi_1271_ = clamp(((min(((_e816.x + ((floor((_e816.y * 0.5f)) % 2f) * 2f)) % 4f), (_e816.y % 4f)) + 1f) * 0.2f), 0f, 1f);
                        break;
                    }
                    if (_e851 == 9f) {
                        phi_1271_ = (((((_e816.x * 2f) + _e816.y) % 8f) + 1f) * 0.11111111f);
                        break;
                    }
                    if (_e851 >= 10f) {
                        let _e981 = abs(((_e816 % vec2<f32>(8f, 8f)) - vec2<f32>(3.5f, 3.5f)));
                        phi_1271_ = clamp(((max(_e981.x, _e981.y) - 0.5f) * 0.2857143f), 0f, 1f);
                        break;
                    }
                    let _e990 = i32((_e816.x % 4f));
                    let _e993 = i32((_e816.y % 4f));
                    if (_e990 == 0i) {
                        if (_e993 == 0i) {
                            phi_1222_ = 16f;
                        } else {
                            if (_e993 == 1i) {
                                phi_1221_ = 5f;
                            } else {
                                phi_1221_ = select(1f, 13f, (_e993 == 2i));
                            }
                            let _e1000 = phi_1221_;
                            phi_1222_ = _e1000;
                        }
                        let _e1002 = phi_1222_;
                        phi_1269_ = _e1002;
                    } else {
                        if (_e990 == 1i) {
                            if (_e993 == 0i) {
                                phi_1238_ = 8f;
                            } else {
                                if (_e993 == 1i) {
                                    phi_1237_ = 12f;
                                } else {
                                    phi_1237_ = select(9f, 4f, (_e993 == 2i));
                                }
                                let _e1009 = phi_1237_;
                                phi_1238_ = _e1009;
                            }
                            let _e1011 = phi_1238_;
                            phi_1268_ = _e1011;
                        } else {
                            if (_e990 == 2i) {
                                if (_e993 == 0i) {
                                    phi_1254_ = 14f;
                                } else {
                                    if (_e993 == 1i) {
                                        phi_1253_ = 2f;
                                    } else {
                                        phi_1253_ = select(3f, 15f, (_e993 == 2i));
                                    }
                                    let _e1018 = phi_1253_;
                                    phi_1254_ = _e1018;
                                }
                                let _e1020 = phi_1254_;
                                phi_1267_ = _e1020;
                            } else {
                                if (_e993 == 0i) {
                                    phi_1266_ = 6f;
                                } else {
                                    if (_e993 == 1i) {
                                        phi_1265_ = 10f;
                                    } else {
                                        phi_1265_ = select(11f, 7f, (_e993 == 2i));
                                    }
                                    let _e1026 = phi_1265_;
                                    phi_1266_ = _e1026;
                                }
                                let _e1028 = phi_1266_;
                                phi_1267_ = _e1028;
                            }
                            let _e1030 = phi_1267_;
                            phi_1268_ = _e1030;
                        }
                        let _e1032 = phi_1268_;
                        phi_1269_ = _e1032;
                    }
                    let _e1034 = phi_1269_;
                    phi_1271_ = (_e1034 * 0.05882353f);
                    break;
                }
            }
            let _e1037 = phi_1271_;
            let _e1047 = clamp((clamp((_e1037 + ((fract((_e686 * 0.618034f)) - 0.5f) * 0.1f)), 0f, 1f) + ((_e757 * _e727) * 0.18f)), 0f, 1f);
            let _e1053 = local_1;
            if (_e1053 > 0f) {
                let _e1057 = FragmentUniformBlock.DitherSettings[2i];
                phi_1293_ = (_e1057 * _e118.x);
            } else {
                phi_1293_ = 0f;
            }
            let _e1060 = phi_1293_;
            let _e1063 = FragmentUniformBlock.NormalDitherSettings[0i];
            let _e1068 = (_e659 * mix(1f, (select(1f, 0f, (clamp(_e679, 0f, 1f) < _e1047)) / _e679), clamp((_e1060 + (_e128 * _e1063)), 0f, 1f)));
            let _e1071 = FragmentUniformBlock.NormalDitherSettings[1i];
            phi_1307_ = false;
            if (_e1071 > 1f) {
                phi_1307_ = (_e128 > 0f);
            }
            let _e1075 = phi_1307_;
            phi_1325_ = _e1068;
            if _e1075 {
                let _e1080 = max(max(_e1068.x, _e1068.y), _e1068.z);
                let _e1081 = (_e1071 - 1f);
                phi_1325_ = (_e1068 * mix(1f, ((floor(((clamp(_e1080, 0f, 1f) * _e1081) + _e1047)) / _e1081) / max(_e1080, 0.0001f)), _e128));
            }
            let _e1092 = phi_1325_;
            phi_1326_ = _e1092;
            break;
        }
    }
    let _e1094 = phi_1326_;
    let _e1107 = _e1094.xyz;
    let _e1115 = clamp(mix(((_e656.xyz * _e1107) + (_e1107 * ((_e657.w * clamp((1f - (max(max(_e656.x, _e656.y), _e656.z) * 2.2f)), 0f, 1f)) * 0.18f))), _e658.xyz, vec3(_e658.w)), vec3<f32>(0f, 0f, 0f), vec3<f32>(1f, 1f, 1f));
    out_u002e_var_u002e_SV_Target0_ = clamp((vec4<f32>(_e1115.x, _e1115.y, _e1115.z, _e656.w) + (vec4<f32>(_e649.x, _e649.y, _e649.z, _e644.w) * (1f - _e656.w))), vec4<f32>(0f, 0f, 0f, 0f), vec4<f32>(1f, 1f, 1f, 1f));
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
