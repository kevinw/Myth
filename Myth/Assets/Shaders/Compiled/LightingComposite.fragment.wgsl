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
    var phi_246_: vec4<f32>;
    var phi_258_: bool;
    var phi_264_: bool;
    var phi_364_: f32;
    var phi_399_: vec2<f32>;
    var phi_400_: vec2<f32>;
    var phi_401_: vec2<f32>;
    var phi_402_: vec2<f32>;
    var phi_432_: f32;
    var phi_441_: f32;
    var phi_451_: f32;
    var phi_470_: f32;
    var phi_477_: f32;
    var phi_478_: f32;
    var phi_479_: f32;
    var phi_566_: f32;
    var phi_567_: f32;
    var phi_582_: f32;
    var phi_583_: f32;
    var phi_598_: f32;
    var phi_599_: f32;
    var phi_610_: f32;
    var phi_611_: f32;
    var phi_612_: f32;
    var phi_613_: f32;
    var phi_614_: f32;
    var phi_622_: f32;
    var phi_681_: f32;
    var phi_682_: f32;
    var phi_697_: f32;
    var phi_698_: f32;
    var phi_713_: f32;
    var phi_714_: f32;
    var phi_725_: f32;
    var phi_726_: f32;
    var phi_727_: f32;
    var phi_728_: f32;
    var phi_729_: f32;
    var phi_731_: f32;
    var phi_756_: f32;
    var phi_770_: bool;
    var phi_788_: vec3<f32>;
    var phi_789_: vec3<f32>;
    var local: bool;
    var phi_846_: bool;
    var phi_852_: bool;
    var phi_948_: f32;
    var phi_983_: vec2<f32>;
    var phi_984_: vec2<f32>;
    var phi_985_: vec2<f32>;
    var phi_986_: vec2<f32>;
    var phi_1016_: f32;
    var phi_1025_: f32;
    var phi_1035_: f32;
    var phi_1054_: f32;
    var phi_1061_: f32;
    var phi_1062_: f32;
    var phi_1063_: f32;
    var phi_1150_: f32;
    var phi_1151_: f32;
    var phi_1166_: f32;
    var phi_1167_: f32;
    var phi_1182_: f32;
    var phi_1183_: f32;
    var phi_1194_: f32;
    var phi_1195_: f32;
    var phi_1196_: f32;
    var phi_1197_: f32;
    var phi_1198_: f32;
    var phi_1206_: f32;
    var phi_1265_: f32;
    var phi_1266_: f32;
    var phi_1281_: f32;
    var phi_1282_: f32;
    var phi_1297_: f32;
    var phi_1298_: f32;
    var phi_1309_: f32;
    var phi_1310_: f32;
    var phi_1311_: f32;
    var phi_1312_: f32;
    var phi_1313_: f32;
    var phi_1315_: f32;
    var local_1: f32;
    var phi_1338_: f32;
    var phi_1352_: bool;
    var phi_1370_: vec3<f32>;
    var phi_1371_: vec3<f32>;

    let _e109 = in_u002e_var_u002e_TEXCOORD0_1;
    let _e110 = in_u002e_var_u002e_TEXCOORD1_1;
    let _e111 = in_u002e_var_u002e_TEXCOORD4_1;
    let _e112 = global;
    if (_e111.x < -1000f) {
        discard;
    }
    let _e115 = textureSample(Texture, Sampler, _e109);
    let _e116 = (_e115 * _e110);
    let _e117 = textureSample(LightTexture, LightSampler, _e109);
    let _e118 = textureSample(TileFieldTexture, TileFieldSampler, _e109);
    let _e119 = textureSample(ActorTileFieldTexture, ActorTileFieldSampler, _e109);
    let _e120 = textureSample(EmissiveTexture, EmissiveSampler, _e109);
    let _e121 = textureSample(NormalTexture, NormalSampler, _e109);
    let _e129 = (_e121.w * clamp(length(((_e121.xy * 2f) - vec2<f32>(1f, 1f))), 0f, 1f));
    let _e133 = FragmentUniformBlock.DitherGlitchAxis[2i];
    let _e137 = FragmentUniformBlock.TextureSizeCamera;
    let _e142 = (_e137.zw + (_e112.xy / vec2(select(1f, _e133, (_e133 > 0f)))));
    let _e146 = floor(((_e118.y * 255f) + 0.5f));
    let _e151 = FragmentUniformBlock.DitherAnchorOffsets[i32((_e146 % 32f))];
    let _e153 = (_e142 + _e151.xy);
    let _e155 = step(0.5f, _e119.w);
    let _e157 = mix(_e118, _e119, vec4(_e155));
    let _e169 = mix(_e153, (_e142 / vec2(exp2((((floor(((_e119.y * 255f) + 0.5f)) % 32f) - 16f) * 0.125f)))), vec2(_e155));
    let _e175 = FragmentUniformBlock.AoSettings[0i];
    phi_246_ = _e117;
    if (_e175 > 0f) {
        let _e177 = FragmentUniformBlock.AoSettings;
        let _e182 = textureSample(AoTexture, AoSampler, (_e109 + (_e177.yz / _e137.xy)));
        let _e186 = textureSample(BackgroundCoverageTexture, BackgroundCoverageSampler, _e109);
        let _e190 = FragmentUniformBlock.AoSettings[3i];
        let _e194 = (_e117.xyz * mix(1f, _e190, (clamp((_e182.x * _e175), 0f, 1f) * _e186.x)));
        phi_246_ = vec4<f32>(_e194.x, _e194.y, _e194.z, _e117.w);
    }
    let _e201 = phi_246_;
    let _e202 = _e201.xyz;
    switch bitcast<i32>(0u) {
        default: {
            let _e206 = FragmentUniformBlock.DitherSettings[0i];
            let _e207 = (_e206 <= 0f);
            phi_258_ = false;
            local = _e207;
            local_1 = _e206;
            if _e207 {
                let _e210 = FragmentUniformBlock.NormalDitherSettings[0i];
                phi_258_ = (_e210 <= 0f);
            }
            let _e213 = phi_258_;
            phi_264_ = false;
            if _e213 {
                let _e216 = FragmentUniformBlock.NormalDitherSettings[1i];
                phi_264_ = (_e216 <= 1f);
            }
            let _e219 = phi_264_;
            if _e219 {
                phi_789_ = _e202;
                break;
            }
            let _e224 = max(max(_e201.x, _e201.y), _e201.z);
            if (_e224 <= 0.0001f) {
                phi_789_ = _e202;
                break;
            }
            let _e231 = floor((floor(((_e157.y * 255f) + 0.5f)) * 0.03125f));
            let _e235 = floor(((_e157.x * 255f) + 0.5f));
            let _e238 = FragmentUniformBlock.DitherSettings[3i];
            let _e242 = ((_e238 * _e238) * (3f - (2f * _e238)));
            let _e246 = FragmentUniformBlock.DitherGlitch[2i];
            let _e248 = mix(24f, (_e246 * 1.45f), _e242);
            let _e250 = FragmentUniformBlock.DitherGlitch;
            let _e252 = FragmentUniformBlock.DitherGlitchAxis;
            let _e254 = normalize(_e252.xy);
            let _e259 = (_e142 - _e250.xy);
            let _e262 = (dot(_e259, _e254) / max(_e248, 1f));
            let _e265 = (dot(_e259, vec2<f32>(-(_e254.y), _e254.x)) / max((_e248 * 0.38f), 1f));
            let _e276 = (((_e242 * smoothstep(1f, 0.25f, sqrt(((_e262 * _e262) + (_e265 * _e265))))) * 0.82f) + ((_e242 * smoothstep(0.6f, 1f, _e242)) * 0.18f));
            let _e279 = FragmentUniformBlock.DitherGlitch[3i];
            let _e281 = floor((_e279 * 9f));
            let _e283 = floor((_e169 * vec2<f32>(0.0625f, 0.0625f)));
            let _e305 = fract((sin(((((_e235 * 19.19f) + (_e283.x * 7.13f)) + (_e283.y * 3.71f)) + (_e281 * 5.17f))) * 43758.547f));
            let _e306 = (fract((sin(((((_e283.x * 17f) + (_e283.y * 31f)) + (_e281 * 13f)) + (_e235 * 0.23f))) * 43758.547f)) - 0.5f);
            let _e312 = (_e231 == 2f);
            if _e312 {
                phi_364_ = 2f;
            } else {
                phi_364_ = select(1f, 3f, (_e231 == 3f));
            }
            let _e316 = phi_364_;
            let _e317 = (_e235 % 3f);
            let _e322 = FragmentUniformBlock.DitherSettings[1i];
            let _e328 = floor(((_e169 + ((vec2<f32>(_e306, (_e305 - 0.5f)) * _e276) * 9f)) / vec2(max(((_e322 * _e316) * (1f + (_e317 * 0.08f))), 1f))));
            let _e329 = (_e231 == 1f);
            if _e329 {
                phi_402_ = vec2<f32>(-(_e328.x), _e328.y);
            } else {
                if _e312 {
                    phi_401_ = _e328.yx;
                } else {
                    if (_e231 == 3f) {
                        phi_400_ = vec2<f32>(_e328.x, -(_e328.y));
                    } else {
                        phi_399_ = _e328;
                        if (_e231 >= 4f) {
                            phi_399_ = -(_e328.yx);
                        }
                        let _e346 = phi_399_;
                        phi_400_ = _e346;
                    }
                    let _e348 = phi_400_;
                    phi_401_ = _e348;
                }
                let _e350 = phi_401_;
                phi_402_ = _e350;
            }
            let _e352 = phi_402_;
            let _e365 = (_e352 + (vec2<f32>(((_e231 * 2f) % 4f), ((_e231 * 3f) % 4f)) + (vec2<f32>((_e235 % 4f), (floor((_e235 * 0.25f)) % 4f)) * 0.35f)));
            if (_e276 > 0.65f) {
                phi_479_ = floor((_e305 * 11f));
            } else {
                switch bitcast<i32>(0u) {
                    default: {
                        if _e329 {
                            if (_e317 == 0f) {
                                phi_432_ = 8f;
                            } else {
                                phi_432_ = select(5f, 6f, (_e317 == 1f));
                            }
                            let _e374 = phi_432_;
                            phi_478_ = _e374;
                            break;
                        }
                        if _e312 {
                            if (_e317 == 0f) {
                                phi_441_ = 2f;
                            } else {
                                phi_441_ = select(10f, 9f, (_e317 == 1f));
                            }
                            let _e379 = phi_441_;
                            phi_478_ = _e379;
                            break;
                        }
                        if (_e231 == 3f) {
                            if (_e317 == 0f) {
                                phi_451_ = 3f;
                            } else {
                                phi_451_ = select(1f, 5f, (_e317 == 1f));
                            }
                            let _e385 = phi_451_;
                            phi_478_ = _e385;
                            break;
                        }
                        if (_e231 == 5f) {
                            phi_478_ = 0f;
                            break;
                        }
                        if (_e231 == 6f) {
                            phi_478_ = 6f;
                            break;
                        }
                        if (_e231 >= 7f) {
                            phi_478_ = 4f;
                            break;
                        }
                        if (_e231 >= 4f) {
                            if (_e317 == 0f) {
                                phi_470_ = 4f;
                            } else {
                                phi_470_ = select(10f, 7f, (_e317 == 1f));
                            }
                            let _e394 = phi_470_;
                            phi_478_ = _e394;
                            break;
                        }
                        if (_e317 == 0f) {
                            phi_477_ = 0f;
                        } else {
                            phi_477_ = select(7f, 4f, (_e317 == 1f));
                        }
                        let _e399 = phi_477_;
                        phi_478_ = _e399;
                        break;
                    }
                }
                let _e401 = phi_478_;
                phi_479_ = _e401;
            }
            let _e403 = phi_479_;
            switch bitcast<i32>(0u) {
                default: {
                    if (_e403 == 1f) {
                        phi_731_ = ((((_e365.x + (_e365.y * 2f)) % 6f) + 1f) * 0.14285715f);
                        break;
                    }
                    if (_e403 == 2f) {
                        let _e415 = ((_e365 % vec2<f32>(4f, 4f)) - vec2<f32>(1.5f, 1.5f));
                        phi_731_ = clamp(((dot(_e415, _e415) + 0.5f) * 0.2f), 0f, 1f);
                        break;
                    }
                    if (_e403 == 3f) {
                        phi_731_ = (((_e365.x % 4f) + 1f) * 0.2f);
                        break;
                    }
                    if (_e403 == 4f) {
                        phi_731_ = fract((sin(dot(floor(_e365), vec2<f32>(127.1f, 311.7f))) * 43758.547f));
                        break;
                    }
                    if (_e403 == 5f) {
                        phi_731_ = (((_e365.y % 4f) + 1f) * 0.2f);
                        break;
                    }
                    if (_e403 == 6f) {
                        phi_731_ = ((min(((_e365.x + _e365.y) % 4f), (((_e365.x - _e365.y) + 8f) % 4f)) + 1f) * 0.2f);
                        break;
                    }
                    if (_e403 == 7f) {
                        let _e448 = (_e365 % vec2<f32>(4f, 4f));
                        let _e457 = i32((_e365.x % 4f));
                        let _e460 = i32((_e365.y % 4f));
                        if (_e457 == 0i) {
                            if (_e460 == 0i) {
                                phi_567_ = 16f;
                            } else {
                                if (_e460 == 1i) {
                                    phi_566_ = 5f;
                                } else {
                                    phi_566_ = select(1f, 13f, (_e460 == 2i));
                                }
                                let _e467 = phi_566_;
                                phi_567_ = _e467;
                            }
                            let _e469 = phi_567_;
                            phi_614_ = _e469;
                        } else {
                            if (_e457 == 1i) {
                                if (_e460 == 0i) {
                                    phi_583_ = 8f;
                                } else {
                                    if (_e460 == 1i) {
                                        phi_582_ = 12f;
                                    } else {
                                        phi_582_ = select(9f, 4f, (_e460 == 2i));
                                    }
                                    let _e476 = phi_582_;
                                    phi_583_ = _e476;
                                }
                                let _e478 = phi_583_;
                                phi_613_ = _e478;
                            } else {
                                if (_e457 == 2i) {
                                    if (_e460 == 0i) {
                                        phi_599_ = 14f;
                                    } else {
                                        if (_e460 == 1i) {
                                            phi_598_ = 2f;
                                        } else {
                                            phi_598_ = select(3f, 15f, (_e460 == 2i));
                                        }
                                        let _e485 = phi_598_;
                                        phi_599_ = _e485;
                                    }
                                    let _e487 = phi_599_;
                                    phi_612_ = _e487;
                                } else {
                                    if (_e460 == 0i) {
                                        phi_611_ = 6f;
                                    } else {
                                        if (_e460 == 1i) {
                                            phi_610_ = 10f;
                                        } else {
                                            phi_610_ = select(11f, 7f, (_e460 == 2i));
                                        }
                                        let _e493 = phi_610_;
                                        phi_611_ = _e493;
                                    }
                                    let _e495 = phi_611_;
                                    phi_612_ = _e495;
                                }
                                let _e497 = phi_612_;
                                phi_613_ = _e497;
                            }
                            let _e499 = phi_613_;
                            phi_614_ = _e499;
                        }
                        let _e501 = phi_614_;
                        if (((floor(_e448.x) + floor(_e448.y)) % 2f) == 0f) {
                            phi_622_ = (_e501 * 0.04411765f);
                        } else {
                            phi_622_ = (0.35f + (_e501 * 0.03823529f));
                        }
                        let _e507 = phi_622_;
                        phi_731_ = _e507;
                        break;
                    }
                    if (_e403 == 8f) {
                        phi_731_ = clamp(((min(((_e365.x + ((floor((_e365.y * 0.5f)) % 2f) * 2f)) % 4f), (_e365.y % 4f)) + 1f) * 0.2f), 0f, 1f);
                        break;
                    }
                    if (_e403 == 9f) {
                        phi_731_ = (((((_e365.x * 2f) + _e365.y) % 8f) + 1f) * 0.11111111f);
                        break;
                    }
                    if (_e403 >= 10f) {
                        let _e533 = abs(((_e365 % vec2<f32>(8f, 8f)) - vec2<f32>(3.5f, 3.5f)));
                        phi_731_ = clamp(((max(_e533.x, _e533.y) - 0.5f) * 0.2857143f), 0f, 1f);
                        break;
                    }
                    let _e542 = i32((_e365.x % 4f));
                    let _e545 = i32((_e365.y % 4f));
                    if (_e542 == 0i) {
                        if (_e545 == 0i) {
                            phi_682_ = 16f;
                        } else {
                            if (_e545 == 1i) {
                                phi_681_ = 5f;
                            } else {
                                phi_681_ = select(1f, 13f, (_e545 == 2i));
                            }
                            let _e552 = phi_681_;
                            phi_682_ = _e552;
                        }
                        let _e554 = phi_682_;
                        phi_729_ = _e554;
                    } else {
                        if (_e542 == 1i) {
                            if (_e545 == 0i) {
                                phi_698_ = 8f;
                            } else {
                                if (_e545 == 1i) {
                                    phi_697_ = 12f;
                                } else {
                                    phi_697_ = select(9f, 4f, (_e545 == 2i));
                                }
                                let _e561 = phi_697_;
                                phi_698_ = _e561;
                            }
                            let _e563 = phi_698_;
                            phi_728_ = _e563;
                        } else {
                            if (_e542 == 2i) {
                                if (_e545 == 0i) {
                                    phi_714_ = 14f;
                                } else {
                                    if (_e545 == 1i) {
                                        phi_713_ = 2f;
                                    } else {
                                        phi_713_ = select(3f, 15f, (_e545 == 2i));
                                    }
                                    let _e570 = phi_713_;
                                    phi_714_ = _e570;
                                }
                                let _e572 = phi_714_;
                                phi_727_ = _e572;
                            } else {
                                if (_e545 == 0i) {
                                    phi_726_ = 6f;
                                } else {
                                    if (_e545 == 1i) {
                                        phi_725_ = 10f;
                                    } else {
                                        phi_725_ = select(11f, 7f, (_e545 == 2i));
                                    }
                                    let _e578 = phi_725_;
                                    phi_726_ = _e578;
                                }
                                let _e580 = phi_726_;
                                phi_727_ = _e580;
                            }
                            let _e582 = phi_727_;
                            phi_728_ = _e582;
                        }
                        let _e584 = phi_728_;
                        phi_729_ = _e584;
                    }
                    let _e586 = phi_729_;
                    phi_731_ = (_e586 * 0.05882353f);
                    break;
                }
            }
            let _e589 = phi_731_;
            let _e602 = clamp(((clamp((clamp((_e589 + ((fract((_e235 * 0.618034f)) - 0.5f) * 0.1f)), 0f, 1f) + ((_e306 * _e276) * 0.18f)), 0f, 1f) + 0.5f) - mix(0.5f, _e119.z, _e155)), 0f, 1f);
            if (_e206 > 0f) {
                let _e610 = FragmentUniformBlock.DitherSettings[2i];
                phi_756_ = (_e610 * _e121.z);
            } else {
                phi_756_ = 0f;
            }
            let _e613 = phi_756_;
            let _e616 = FragmentUniformBlock.NormalDitherSettings[0i];
            let _e621 = (_e202 * mix(1f, (select(1f, 0f, (clamp(_e224, 0f, 1f) < _e602)) / _e224), clamp((_e613 + (_e129 * _e616)), 0f, 1f)));
            let _e624 = FragmentUniformBlock.NormalDitherSettings[1i];
            phi_770_ = false;
            if (_e624 > 1f) {
                phi_770_ = (_e129 > 0f);
            }
            let _e628 = phi_770_;
            phi_788_ = _e621;
            if _e628 {
                let _e633 = max(max(_e621.x, _e621.y), _e621.z);
                let _e634 = (_e624 - 1f);
                phi_788_ = (_e621 * mix(1f, ((floor(((clamp(_e633, 0f, 1f) * _e634) + _e602)) / _e634) / max(_e633, 0.0001f)), _e129));
            }
            let _e645 = phi_788_;
            phi_789_ = _e645;
            break;
        }
    }
    let _e647 = phi_789_;
    let _e660 = _e647.xyz;
    let _e668 = clamp(mix(((_e116.xyz * _e660) + (_e660 * ((_e201.w * clamp((1f - (max(max(_e116.x, _e116.y), _e116.z) * 2.2f)), 0f, 1f)) * 0.18f))), _e120.xyz, vec3(_e120.w)), vec3<f32>(0f, 0f, 0f), vec3<f32>(1f, 1f, 1f));
    let _e673 = vec4<f32>(_e668.x, _e668.y, _e668.z, _e116.w);
    let _e675 = textureSample(SporeFogTexture, SporeFogSampler, _e109);
    let _e678 = clamp((_e673.xyz + _e675.xyz), vec3<f32>(0f, 0f, 0f), vec3<f32>(1f, 1f, 1f));
    let _e684 = textureSample(ForegroundTexture, ForegroundSampler, _e109);
    let _e685 = (_e684 * _e110);
    let _e686 = textureSample(ForegroundLightTexture, ForegroundLightSampler, _e109);
    let _e687 = textureSample(ForegroundEmissiveTexture, ForegroundEmissiveSampler, _e109);
    let _e688 = _e686.xyz;
    switch bitcast<i32>(0u) {
        default: {
            let _e691 = local;
            phi_846_ = false;
            if _e691 {
                let _e694 = FragmentUniformBlock.NormalDitherSettings[0i];
                phi_846_ = (_e694 <= 0f);
            }
            let _e697 = phi_846_;
            phi_852_ = false;
            if _e697 {
                let _e700 = FragmentUniformBlock.NormalDitherSettings[1i];
                phi_852_ = (_e700 <= 1f);
            }
            let _e703 = phi_852_;
            if _e703 {
                phi_1371_ = _e688;
                break;
            }
            let _e708 = max(max(_e686.x, _e686.y), _e686.z);
            if (_e708 <= 0.0001f) {
                phi_1371_ = _e688;
                break;
            }
            let _e711 = floor((_e146 * 0.03125f));
            let _e715 = floor(((_e118.x * 255f) + 0.5f));
            let _e718 = FragmentUniformBlock.DitherSettings[3i];
            let _e722 = ((_e718 * _e718) * (3f - (2f * _e718)));
            let _e726 = FragmentUniformBlock.DitherGlitch[2i];
            let _e728 = mix(24f, (_e726 * 1.45f), _e722);
            let _e730 = FragmentUniformBlock.DitherGlitch;
            let _e732 = FragmentUniformBlock.DitherGlitchAxis;
            let _e734 = normalize(_e732.xy);
            let _e739 = (_e142 - _e730.xy);
            let _e742 = (dot(_e739, _e734) / max(_e728, 1f));
            let _e745 = (dot(_e739, vec2<f32>(-(_e734.y), _e734.x)) / max((_e728 * 0.38f), 1f));
            let _e756 = (((_e722 * smoothstep(1f, 0.25f, sqrt(((_e742 * _e742) + (_e745 * _e745))))) * 0.82f) + ((_e722 * smoothstep(0.6f, 1f, _e722)) * 0.18f));
            let _e759 = FragmentUniformBlock.DitherGlitch[3i];
            let _e761 = floor((_e759 * 9f));
            let _e763 = floor((_e153 * vec2<f32>(0.0625f, 0.0625f)));
            let _e785 = fract((sin(((((_e715 * 19.19f) + (_e763.x * 7.13f)) + (_e763.y * 3.71f)) + (_e761 * 5.17f))) * 43758.547f));
            let _e786 = (fract((sin(((((_e763.x * 17f) + (_e763.y * 31f)) + (_e761 * 13f)) + (_e715 * 0.23f))) * 43758.547f)) - 0.5f);
            let _e792 = (_e711 == 2f);
            if _e792 {
                phi_948_ = 2f;
            } else {
                phi_948_ = select(1f, 3f, (_e711 == 3f));
            }
            let _e796 = phi_948_;
            let _e797 = (_e715 % 3f);
            let _e802 = FragmentUniformBlock.DitherSettings[1i];
            let _e808 = floor(((_e153 + ((vec2<f32>(_e786, (_e785 - 0.5f)) * _e756) * 9f)) / vec2(max(((_e802 * _e796) * (1f + (_e797 * 0.08f))), 1f))));
            let _e809 = (_e711 == 1f);
            if _e809 {
                phi_986_ = vec2<f32>(-(_e808.x), _e808.y);
            } else {
                if _e792 {
                    phi_985_ = _e808.yx;
                } else {
                    if (_e711 == 3f) {
                        phi_984_ = vec2<f32>(_e808.x, -(_e808.y));
                    } else {
                        phi_983_ = _e808;
                        if (_e711 >= 4f) {
                            phi_983_ = -(_e808.yx);
                        }
                        let _e826 = phi_983_;
                        phi_984_ = _e826;
                    }
                    let _e828 = phi_984_;
                    phi_985_ = _e828;
                }
                let _e830 = phi_985_;
                phi_986_ = _e830;
            }
            let _e832 = phi_986_;
            let _e845 = (_e832 + (vec2<f32>(((_e711 * 2f) % 4f), ((_e711 * 3f) % 4f)) + (vec2<f32>((_e715 % 4f), (floor((_e715 * 0.25f)) % 4f)) * 0.35f)));
            if (_e756 > 0.65f) {
                phi_1063_ = floor((_e785 * 11f));
            } else {
                switch bitcast<i32>(0u) {
                    default: {
                        if _e809 {
                            if (_e797 == 0f) {
                                phi_1016_ = 8f;
                            } else {
                                phi_1016_ = select(5f, 6f, (_e797 == 1f));
                            }
                            let _e854 = phi_1016_;
                            phi_1062_ = _e854;
                            break;
                        }
                        if _e792 {
                            if (_e797 == 0f) {
                                phi_1025_ = 2f;
                            } else {
                                phi_1025_ = select(10f, 9f, (_e797 == 1f));
                            }
                            let _e859 = phi_1025_;
                            phi_1062_ = _e859;
                            break;
                        }
                        if (_e711 == 3f) {
                            if (_e797 == 0f) {
                                phi_1035_ = 3f;
                            } else {
                                phi_1035_ = select(1f, 5f, (_e797 == 1f));
                            }
                            let _e865 = phi_1035_;
                            phi_1062_ = _e865;
                            break;
                        }
                        if (_e711 == 5f) {
                            phi_1062_ = 0f;
                            break;
                        }
                        if (_e711 == 6f) {
                            phi_1062_ = 6f;
                            break;
                        }
                        if (_e711 >= 7f) {
                            phi_1062_ = 4f;
                            break;
                        }
                        if (_e711 >= 4f) {
                            if (_e797 == 0f) {
                                phi_1054_ = 4f;
                            } else {
                                phi_1054_ = select(10f, 7f, (_e797 == 1f));
                            }
                            let _e874 = phi_1054_;
                            phi_1062_ = _e874;
                            break;
                        }
                        if (_e797 == 0f) {
                            phi_1061_ = 0f;
                        } else {
                            phi_1061_ = select(7f, 4f, (_e797 == 1f));
                        }
                        let _e879 = phi_1061_;
                        phi_1062_ = _e879;
                        break;
                    }
                }
                let _e881 = phi_1062_;
                phi_1063_ = _e881;
            }
            let _e883 = phi_1063_;
            switch bitcast<i32>(0u) {
                default: {
                    if (_e883 == 1f) {
                        phi_1315_ = ((((_e845.x + (_e845.y * 2f)) % 6f) + 1f) * 0.14285715f);
                        break;
                    }
                    if (_e883 == 2f) {
                        let _e895 = ((_e845 % vec2<f32>(4f, 4f)) - vec2<f32>(1.5f, 1.5f));
                        phi_1315_ = clamp(((dot(_e895, _e895) + 0.5f) * 0.2f), 0f, 1f);
                        break;
                    }
                    if (_e883 == 3f) {
                        phi_1315_ = (((_e845.x % 4f) + 1f) * 0.2f);
                        break;
                    }
                    if (_e883 == 4f) {
                        phi_1315_ = fract((sin(dot(floor(_e845), vec2<f32>(127.1f, 311.7f))) * 43758.547f));
                        break;
                    }
                    if (_e883 == 5f) {
                        phi_1315_ = (((_e845.y % 4f) + 1f) * 0.2f);
                        break;
                    }
                    if (_e883 == 6f) {
                        phi_1315_ = ((min(((_e845.x + _e845.y) % 4f), (((_e845.x - _e845.y) + 8f) % 4f)) + 1f) * 0.2f);
                        break;
                    }
                    if (_e883 == 7f) {
                        let _e928 = (_e845 % vec2<f32>(4f, 4f));
                        let _e937 = i32((_e845.x % 4f));
                        let _e940 = i32((_e845.y % 4f));
                        if (_e937 == 0i) {
                            if (_e940 == 0i) {
                                phi_1151_ = 16f;
                            } else {
                                if (_e940 == 1i) {
                                    phi_1150_ = 5f;
                                } else {
                                    phi_1150_ = select(1f, 13f, (_e940 == 2i));
                                }
                                let _e947 = phi_1150_;
                                phi_1151_ = _e947;
                            }
                            let _e949 = phi_1151_;
                            phi_1198_ = _e949;
                        } else {
                            if (_e937 == 1i) {
                                if (_e940 == 0i) {
                                    phi_1167_ = 8f;
                                } else {
                                    if (_e940 == 1i) {
                                        phi_1166_ = 12f;
                                    } else {
                                        phi_1166_ = select(9f, 4f, (_e940 == 2i));
                                    }
                                    let _e956 = phi_1166_;
                                    phi_1167_ = _e956;
                                }
                                let _e958 = phi_1167_;
                                phi_1197_ = _e958;
                            } else {
                                if (_e937 == 2i) {
                                    if (_e940 == 0i) {
                                        phi_1183_ = 14f;
                                    } else {
                                        if (_e940 == 1i) {
                                            phi_1182_ = 2f;
                                        } else {
                                            phi_1182_ = select(3f, 15f, (_e940 == 2i));
                                        }
                                        let _e965 = phi_1182_;
                                        phi_1183_ = _e965;
                                    }
                                    let _e967 = phi_1183_;
                                    phi_1196_ = _e967;
                                } else {
                                    if (_e940 == 0i) {
                                        phi_1195_ = 6f;
                                    } else {
                                        if (_e940 == 1i) {
                                            phi_1194_ = 10f;
                                        } else {
                                            phi_1194_ = select(11f, 7f, (_e940 == 2i));
                                        }
                                        let _e973 = phi_1194_;
                                        phi_1195_ = _e973;
                                    }
                                    let _e975 = phi_1195_;
                                    phi_1196_ = _e975;
                                }
                                let _e977 = phi_1196_;
                                phi_1197_ = _e977;
                            }
                            let _e979 = phi_1197_;
                            phi_1198_ = _e979;
                        }
                        let _e981 = phi_1198_;
                        if (((floor(_e928.x) + floor(_e928.y)) % 2f) == 0f) {
                            phi_1206_ = (_e981 * 0.04411765f);
                        } else {
                            phi_1206_ = (0.35f + (_e981 * 0.03823529f));
                        }
                        let _e987 = phi_1206_;
                        phi_1315_ = _e987;
                        break;
                    }
                    if (_e883 == 8f) {
                        phi_1315_ = clamp(((min(((_e845.x + ((floor((_e845.y * 0.5f)) % 2f) * 2f)) % 4f), (_e845.y % 4f)) + 1f) * 0.2f), 0f, 1f);
                        break;
                    }
                    if (_e883 == 9f) {
                        phi_1315_ = (((((_e845.x * 2f) + _e845.y) % 8f) + 1f) * 0.11111111f);
                        break;
                    }
                    if (_e883 >= 10f) {
                        let _e1013 = abs(((_e845 % vec2<f32>(8f, 8f)) - vec2<f32>(3.5f, 3.5f)));
                        phi_1315_ = clamp(((max(_e1013.x, _e1013.y) - 0.5f) * 0.2857143f), 0f, 1f);
                        break;
                    }
                    let _e1022 = i32((_e845.x % 4f));
                    let _e1025 = i32((_e845.y % 4f));
                    if (_e1022 == 0i) {
                        if (_e1025 == 0i) {
                            phi_1266_ = 16f;
                        } else {
                            if (_e1025 == 1i) {
                                phi_1265_ = 5f;
                            } else {
                                phi_1265_ = select(1f, 13f, (_e1025 == 2i));
                            }
                            let _e1032 = phi_1265_;
                            phi_1266_ = _e1032;
                        }
                        let _e1034 = phi_1266_;
                        phi_1313_ = _e1034;
                    } else {
                        if (_e1022 == 1i) {
                            if (_e1025 == 0i) {
                                phi_1282_ = 8f;
                            } else {
                                if (_e1025 == 1i) {
                                    phi_1281_ = 12f;
                                } else {
                                    phi_1281_ = select(9f, 4f, (_e1025 == 2i));
                                }
                                let _e1041 = phi_1281_;
                                phi_1282_ = _e1041;
                            }
                            let _e1043 = phi_1282_;
                            phi_1312_ = _e1043;
                        } else {
                            if (_e1022 == 2i) {
                                if (_e1025 == 0i) {
                                    phi_1298_ = 14f;
                                } else {
                                    if (_e1025 == 1i) {
                                        phi_1297_ = 2f;
                                    } else {
                                        phi_1297_ = select(3f, 15f, (_e1025 == 2i));
                                    }
                                    let _e1050 = phi_1297_;
                                    phi_1298_ = _e1050;
                                }
                                let _e1052 = phi_1298_;
                                phi_1311_ = _e1052;
                            } else {
                                if (_e1025 == 0i) {
                                    phi_1310_ = 6f;
                                } else {
                                    if (_e1025 == 1i) {
                                        phi_1309_ = 10f;
                                    } else {
                                        phi_1309_ = select(11f, 7f, (_e1025 == 2i));
                                    }
                                    let _e1058 = phi_1309_;
                                    phi_1310_ = _e1058;
                                }
                                let _e1060 = phi_1310_;
                                phi_1311_ = _e1060;
                            }
                            let _e1062 = phi_1311_;
                            phi_1312_ = _e1062;
                        }
                        let _e1064 = phi_1312_;
                        phi_1313_ = _e1064;
                    }
                    let _e1066 = phi_1313_;
                    phi_1315_ = (_e1066 * 0.05882353f);
                    break;
                }
            }
            let _e1069 = phi_1315_;
            let _e1080 = clamp(clamp((clamp((_e1069 + ((fract((_e715 * 0.618034f)) - 0.5f) * 0.1f)), 0f, 1f) + ((_e786 * _e756) * 0.18f)), 0f, 1f), 0f, 1f);
            let _e1086 = local_1;
            if (_e1086 > 0f) {
                let _e1090 = FragmentUniformBlock.DitherSettings[2i];
                phi_1338_ = (_e1090 * _e121.z);
            } else {
                phi_1338_ = 0f;
            }
            let _e1093 = phi_1338_;
            let _e1096 = FragmentUniformBlock.NormalDitherSettings[0i];
            let _e1101 = (_e688 * mix(1f, (select(1f, 0f, (clamp(_e708, 0f, 1f) < _e1080)) / _e708), clamp((_e1093 + (_e129 * _e1096)), 0f, 1f)));
            let _e1104 = FragmentUniformBlock.NormalDitherSettings[1i];
            phi_1352_ = false;
            if (_e1104 > 1f) {
                phi_1352_ = (_e129 > 0f);
            }
            let _e1108 = phi_1352_;
            phi_1370_ = _e1101;
            if _e1108 {
                let _e1113 = max(max(_e1101.x, _e1101.y), _e1101.z);
                let _e1114 = (_e1104 - 1f);
                phi_1370_ = (_e1101 * mix(1f, ((floor(((clamp(_e1113, 0f, 1f) * _e1114) + _e1080)) / _e1114) / max(_e1113, 0.0001f)), _e129));
            }
            let _e1125 = phi_1370_;
            phi_1371_ = _e1125;
            break;
        }
    }
    let _e1127 = phi_1371_;
    let _e1140 = _e1127.xyz;
    let _e1148 = clamp(mix(((_e685.xyz * _e1140) + (_e1140 * ((_e686.w * clamp((1f - (max(max(_e685.x, _e685.y), _e685.z) * 2.2f)), 0f, 1f)) * 0.18f))), _e687.xyz, vec3(_e687.w)), vec3<f32>(0f, 0f, 0f), vec3<f32>(1f, 1f, 1f));
    out_u002e_var_u002e_SV_Target0_ = clamp((vec4<f32>(_e1148.x, _e1148.y, _e1148.z, _e685.w) + (vec4<f32>(_e678.x, _e678.y, _e678.z, _e673.w) * (1f - _e685.w))), vec4<f32>(0f, 0f, 0f, 0f), vec4<f32>(1f, 1f, 1f, 1f));
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
