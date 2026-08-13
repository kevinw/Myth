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
    var phi_259_: vec4<f32>;
    var phi_271_: bool;
    var phi_277_: bool;
    var phi_377_: f32;
    var phi_412_: vec2<f32>;
    var phi_413_: vec2<f32>;
    var phi_414_: vec2<f32>;
    var phi_415_: vec2<f32>;
    var phi_445_: f32;
    var phi_454_: f32;
    var phi_464_: f32;
    var phi_483_: f32;
    var phi_490_: f32;
    var phi_491_: f32;
    var phi_492_: f32;
    var phi_579_: f32;
    var phi_580_: f32;
    var phi_595_: f32;
    var phi_596_: f32;
    var phi_611_: f32;
    var phi_612_: f32;
    var phi_623_: f32;
    var phi_624_: f32;
    var phi_625_: f32;
    var phi_626_: f32;
    var phi_627_: f32;
    var phi_635_: f32;
    var phi_694_: f32;
    var phi_695_: f32;
    var phi_710_: f32;
    var phi_711_: f32;
    var phi_726_: f32;
    var phi_727_: f32;
    var phi_738_: f32;
    var phi_739_: f32;
    var phi_740_: f32;
    var phi_741_: f32;
    var phi_742_: f32;
    var phi_744_: f32;
    var phi_769_: f32;
    var phi_783_: bool;
    var phi_801_: vec3<f32>;
    var phi_802_: vec3<f32>;
    var local: bool;
    var phi_859_: bool;
    var phi_865_: bool;
    var phi_961_: f32;
    var phi_996_: vec2<f32>;
    var phi_997_: vec2<f32>;
    var phi_998_: vec2<f32>;
    var phi_999_: vec2<f32>;
    var phi_1029_: f32;
    var phi_1038_: f32;
    var phi_1048_: f32;
    var phi_1067_: f32;
    var phi_1074_: f32;
    var phi_1075_: f32;
    var phi_1076_: f32;
    var phi_1163_: f32;
    var phi_1164_: f32;
    var phi_1179_: f32;
    var phi_1180_: f32;
    var phi_1195_: f32;
    var phi_1196_: f32;
    var phi_1207_: f32;
    var phi_1208_: f32;
    var phi_1209_: f32;
    var phi_1210_: f32;
    var phi_1211_: f32;
    var phi_1219_: f32;
    var phi_1278_: f32;
    var phi_1279_: f32;
    var phi_1294_: f32;
    var phi_1295_: f32;
    var phi_1310_: f32;
    var phi_1311_: f32;
    var phi_1322_: f32;
    var phi_1323_: f32;
    var phi_1324_: f32;
    var phi_1325_: f32;
    var phi_1326_: f32;
    var phi_1328_: f32;
    var local_1: f32;
    var phi_1351_: f32;
    var phi_1365_: bool;
    var phi_1383_: vec3<f32>;
    var phi_1384_: vec3<f32>;

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
    let _e122 = textureSample(EmissiveTexture, EmissiveSampler, _e111);
    let _e123 = textureSample(NormalTexture, NormalSampler, _e111);
    let _e131 = (_e123.w * clamp(length(((_e123.xy * 2f) - vec2<f32>(1f, 1f))), 0f, 1f));
    let _e135 = FragmentUniformBlock.DitherGlitchAxis[2i];
    let _e139 = FragmentUniformBlock.TextureSizeCamera;
    let _e144 = (_e139.zw + (_e114.xy / vec2(select(1f, _e135, (_e135 > 0f)))));
    let _e148 = floor(((_e120.y * 255f) + 0.5f));
    let _e153 = FragmentUniformBlock.DitherAnchorOffsets[i32((_e148 % 32f))];
    let _e155 = (_e144 + _e153.xy);
    let _e159 = floor(((_e121.x * 255f) + 0.5f));
    let _e161 = floor((_e159 * 0.03125f));
    let _e163 = step(0.5f, _e161);
    let _e168 = mix(_e120, vec4<f32>(((_e159 % 32f) * 0.003921569f), (_e161 * 0.1254902f), 0f, 1f), vec4(_e163));
    let _e172 = floor(((_e121.y * 255f) + 0.5f));
    let _e183 = mix(_e155, (_e144 / vec2(exp2(((floor((_e172 * 0.125f)) - 16f) * 0.125f)))), vec2(_e163));
    let _e188 = FragmentUniformBlock.AoSettings[0i];
    phi_259_ = _e119;
    if (_e188 > 0f) {
        let _e190 = FragmentUniformBlock.AoSettings;
        let _e195 = textureSample(AoTexture, AoSampler, (_e111 + (_e190.yz / _e139.xy)));
        let _e199 = textureSample(BackgroundCoverageTexture, BackgroundCoverageSampler, _e111);
        let _e203 = FragmentUniformBlock.AoSettings[3i];
        let _e207 = (_e119.xyz * mix(1f, _e203, (clamp((_e195.x * _e188), 0f, 1f) * _e199.x)));
        phi_259_ = vec4<f32>(_e207.x, _e207.y, _e207.z, _e119.w);
    }
    let _e214 = phi_259_;
    let _e215 = _e214.xyz;
    switch bitcast<i32>(0u) {
        default: {
            let _e219 = FragmentUniformBlock.DitherSettings[0i];
            let _e220 = (_e219 <= 0f);
            phi_271_ = false;
            local = _e220;
            local_1 = _e219;
            if _e220 {
                let _e223 = FragmentUniformBlock.NormalDitherSettings[0i];
                phi_271_ = (_e223 <= 0f);
            }
            let _e226 = phi_271_;
            phi_277_ = false;
            if _e226 {
                let _e229 = FragmentUniformBlock.NormalDitherSettings[1i];
                phi_277_ = (_e229 <= 1f);
            }
            let _e232 = phi_277_;
            if _e232 {
                phi_802_ = _e215;
                break;
            }
            let _e237 = max(max(_e214.x, _e214.y), _e214.z);
            if (_e237 <= 0.0001f) {
                phi_802_ = _e215;
                break;
            }
            let _e244 = floor((floor(((_e168.y * 255f) + 0.5f)) * 0.03125f));
            let _e248 = floor(((_e168.x * 255f) + 0.5f));
            let _e251 = FragmentUniformBlock.DitherSettings[3i];
            let _e255 = ((_e251 * _e251) * (3f - (2f * _e251)));
            let _e259 = FragmentUniformBlock.DitherGlitch[2i];
            let _e261 = mix(24f, (_e259 * 1.45f), _e255);
            let _e263 = FragmentUniformBlock.DitherGlitch;
            let _e265 = FragmentUniformBlock.DitherGlitchAxis;
            let _e267 = normalize(_e265.xy);
            let _e272 = (_e144 - _e263.xy);
            let _e275 = (dot(_e272, _e267) / max(_e261, 1f));
            let _e278 = (dot(_e272, vec2<f32>(-(_e267.y), _e267.x)) / max((_e261 * 0.38f), 1f));
            let _e289 = (((_e255 * smoothstep(1f, 0.25f, sqrt(((_e275 * _e275) + (_e278 * _e278))))) * 0.82f) + ((_e255 * smoothstep(0.6f, 1f, _e255)) * 0.18f));
            let _e292 = FragmentUniformBlock.DitherGlitch[3i];
            let _e294 = floor((_e292 * 9f));
            let _e296 = floor((_e183 * vec2<f32>(0.0625f, 0.0625f)));
            let _e318 = fract((sin(((((_e248 * 19.19f) + (_e296.x * 7.13f)) + (_e296.y * 3.71f)) + (_e294 * 5.17f))) * 43758.547f));
            let _e319 = (fract((sin(((((_e296.x * 17f) + (_e296.y * 31f)) + (_e294 * 13f)) + (_e248 * 0.23f))) * 43758.547f)) - 0.5f);
            let _e325 = (_e244 == 2f);
            if _e325 {
                phi_377_ = 2f;
            } else {
                phi_377_ = select(1f, 3f, (_e244 == 3f));
            }
            let _e329 = phi_377_;
            let _e330 = (_e248 % 3f);
            let _e335 = FragmentUniformBlock.DitherSettings[1i];
            let _e341 = floor(((_e183 + ((vec2<f32>(_e319, (_e318 - 0.5f)) * _e289) * 9f)) / vec2(max(((_e335 * _e329) * (1f + (_e330 * 0.08f))), 1f))));
            let _e342 = (_e244 == 1f);
            if _e342 {
                phi_415_ = vec2<f32>(-(_e341.x), _e341.y);
            } else {
                if _e325 {
                    phi_414_ = _e341.yx;
                } else {
                    if (_e244 == 3f) {
                        phi_413_ = vec2<f32>(_e341.x, -(_e341.y));
                    } else {
                        phi_412_ = _e341;
                        if (_e244 >= 4f) {
                            phi_412_ = -(_e341.yx);
                        }
                        let _e359 = phi_412_;
                        phi_413_ = _e359;
                    }
                    let _e361 = phi_413_;
                    phi_414_ = _e361;
                }
                let _e363 = phi_414_;
                phi_415_ = _e363;
            }
            let _e365 = phi_415_;
            let _e378 = (_e365 + (vec2<f32>(((_e244 * 2f) % 4f), ((_e244 * 3f) % 4f)) + (vec2<f32>((_e248 % 4f), (floor((_e248 * 0.25f)) % 4f)) * 0.35f)));
            if (_e289 > 0.65f) {
                phi_492_ = floor((_e318 * 11f));
            } else {
                switch bitcast<i32>(0u) {
                    default: {
                        if _e342 {
                            if (_e330 == 0f) {
                                phi_445_ = 8f;
                            } else {
                                phi_445_ = select(5f, 6f, (_e330 == 1f));
                            }
                            let _e387 = phi_445_;
                            phi_491_ = _e387;
                            break;
                        }
                        if _e325 {
                            if (_e330 == 0f) {
                                phi_454_ = 2f;
                            } else {
                                phi_454_ = select(10f, 9f, (_e330 == 1f));
                            }
                            let _e392 = phi_454_;
                            phi_491_ = _e392;
                            break;
                        }
                        if (_e244 == 3f) {
                            if (_e330 == 0f) {
                                phi_464_ = 3f;
                            } else {
                                phi_464_ = select(1f, 5f, (_e330 == 1f));
                            }
                            let _e398 = phi_464_;
                            phi_491_ = _e398;
                            break;
                        }
                        if (_e244 == 5f) {
                            phi_491_ = 0f;
                            break;
                        }
                        if (_e244 == 6f) {
                            phi_491_ = 6f;
                            break;
                        }
                        if (_e244 >= 7f) {
                            phi_491_ = 4f;
                            break;
                        }
                        if (_e244 >= 4f) {
                            if (_e330 == 0f) {
                                phi_483_ = 4f;
                            } else {
                                phi_483_ = select(10f, 7f, (_e330 == 1f));
                            }
                            let _e407 = phi_483_;
                            phi_491_ = _e407;
                            break;
                        }
                        if (_e330 == 0f) {
                            phi_490_ = 0f;
                        } else {
                            phi_490_ = select(7f, 4f, (_e330 == 1f));
                        }
                        let _e412 = phi_490_;
                        phi_491_ = _e412;
                        break;
                    }
                }
                let _e414 = phi_491_;
                phi_492_ = _e414;
            }
            let _e416 = phi_492_;
            switch bitcast<i32>(0u) {
                default: {
                    if (_e416 == 1f) {
                        phi_744_ = ((((_e378.x + (_e378.y * 2f)) % 6f) + 1f) * 0.14285715f);
                        break;
                    }
                    if (_e416 == 2f) {
                        let _e428 = ((_e378 % vec2<f32>(4f, 4f)) - vec2<f32>(1.5f, 1.5f));
                        phi_744_ = clamp(((dot(_e428, _e428) + 0.5f) * 0.2f), 0f, 1f);
                        break;
                    }
                    if (_e416 == 3f) {
                        phi_744_ = (((_e378.x % 4f) + 1f) * 0.2f);
                        break;
                    }
                    if (_e416 == 4f) {
                        phi_744_ = fract((sin(dot(floor(_e378), vec2<f32>(127.1f, 311.7f))) * 43758.547f));
                        break;
                    }
                    if (_e416 == 5f) {
                        phi_744_ = (((_e378.y % 4f) + 1f) * 0.2f);
                        break;
                    }
                    if (_e416 == 6f) {
                        phi_744_ = ((min(((_e378.x + _e378.y) % 4f), (((_e378.x - _e378.y) + 8f) % 4f)) + 1f) * 0.2f);
                        break;
                    }
                    if (_e416 == 7f) {
                        let _e461 = (_e378 % vec2<f32>(4f, 4f));
                        let _e470 = i32((_e378.x % 4f));
                        let _e473 = i32((_e378.y % 4f));
                        if (_e470 == 0i) {
                            if (_e473 == 0i) {
                                phi_580_ = 16f;
                            } else {
                                if (_e473 == 1i) {
                                    phi_579_ = 5f;
                                } else {
                                    phi_579_ = select(1f, 13f, (_e473 == 2i));
                                }
                                let _e480 = phi_579_;
                                phi_580_ = _e480;
                            }
                            let _e482 = phi_580_;
                            phi_627_ = _e482;
                        } else {
                            if (_e470 == 1i) {
                                if (_e473 == 0i) {
                                    phi_596_ = 8f;
                                } else {
                                    if (_e473 == 1i) {
                                        phi_595_ = 12f;
                                    } else {
                                        phi_595_ = select(9f, 4f, (_e473 == 2i));
                                    }
                                    let _e489 = phi_595_;
                                    phi_596_ = _e489;
                                }
                                let _e491 = phi_596_;
                                phi_626_ = _e491;
                            } else {
                                if (_e470 == 2i) {
                                    if (_e473 == 0i) {
                                        phi_612_ = 14f;
                                    } else {
                                        if (_e473 == 1i) {
                                            phi_611_ = 2f;
                                        } else {
                                            phi_611_ = select(3f, 15f, (_e473 == 2i));
                                        }
                                        let _e498 = phi_611_;
                                        phi_612_ = _e498;
                                    }
                                    let _e500 = phi_612_;
                                    phi_625_ = _e500;
                                } else {
                                    if (_e473 == 0i) {
                                        phi_624_ = 6f;
                                    } else {
                                        if (_e473 == 1i) {
                                            phi_623_ = 10f;
                                        } else {
                                            phi_623_ = select(11f, 7f, (_e473 == 2i));
                                        }
                                        let _e506 = phi_623_;
                                        phi_624_ = _e506;
                                    }
                                    let _e508 = phi_624_;
                                    phi_625_ = _e508;
                                }
                                let _e510 = phi_625_;
                                phi_626_ = _e510;
                            }
                            let _e512 = phi_626_;
                            phi_627_ = _e512;
                        }
                        let _e514 = phi_627_;
                        if (((floor(_e461.x) + floor(_e461.y)) % 2f) == 0f) {
                            phi_635_ = (_e514 * 0.04411765f);
                        } else {
                            phi_635_ = (0.35f + (_e514 * 0.03823529f));
                        }
                        let _e520 = phi_635_;
                        phi_744_ = _e520;
                        break;
                    }
                    if (_e416 == 8f) {
                        phi_744_ = clamp(((min(((_e378.x + ((floor((_e378.y * 0.5f)) % 2f) * 2f)) % 4f), (_e378.y % 4f)) + 1f) * 0.2f), 0f, 1f);
                        break;
                    }
                    if (_e416 == 9f) {
                        phi_744_ = (((((_e378.x * 2f) + _e378.y) % 8f) + 1f) * 0.11111111f);
                        break;
                    }
                    if (_e416 >= 10f) {
                        let _e546 = abs(((_e378 % vec2<f32>(8f, 8f)) - vec2<f32>(3.5f, 3.5f)));
                        phi_744_ = clamp(((max(_e546.x, _e546.y) - 0.5f) * 0.2857143f), 0f, 1f);
                        break;
                    }
                    let _e555 = i32((_e378.x % 4f));
                    let _e558 = i32((_e378.y % 4f));
                    if (_e555 == 0i) {
                        if (_e558 == 0i) {
                            phi_695_ = 16f;
                        } else {
                            if (_e558 == 1i) {
                                phi_694_ = 5f;
                            } else {
                                phi_694_ = select(1f, 13f, (_e558 == 2i));
                            }
                            let _e565 = phi_694_;
                            phi_695_ = _e565;
                        }
                        let _e567 = phi_695_;
                        phi_742_ = _e567;
                    } else {
                        if (_e555 == 1i) {
                            if (_e558 == 0i) {
                                phi_711_ = 8f;
                            } else {
                                if (_e558 == 1i) {
                                    phi_710_ = 12f;
                                } else {
                                    phi_710_ = select(9f, 4f, (_e558 == 2i));
                                }
                                let _e574 = phi_710_;
                                phi_711_ = _e574;
                            }
                            let _e576 = phi_711_;
                            phi_741_ = _e576;
                        } else {
                            if (_e555 == 2i) {
                                if (_e558 == 0i) {
                                    phi_727_ = 14f;
                                } else {
                                    if (_e558 == 1i) {
                                        phi_726_ = 2f;
                                    } else {
                                        phi_726_ = select(3f, 15f, (_e558 == 2i));
                                    }
                                    let _e583 = phi_726_;
                                    phi_727_ = _e583;
                                }
                                let _e585 = phi_727_;
                                phi_740_ = _e585;
                            } else {
                                if (_e558 == 0i) {
                                    phi_739_ = 6f;
                                } else {
                                    if (_e558 == 1i) {
                                        phi_738_ = 10f;
                                    } else {
                                        phi_738_ = select(11f, 7f, (_e558 == 2i));
                                    }
                                    let _e591 = phi_738_;
                                    phi_739_ = _e591;
                                }
                                let _e593 = phi_739_;
                                phi_740_ = _e593;
                            }
                            let _e595 = phi_740_;
                            phi_741_ = _e595;
                        }
                        let _e597 = phi_741_;
                        phi_742_ = _e597;
                    }
                    let _e599 = phi_742_;
                    phi_744_ = (_e599 * 0.05882353f);
                    break;
                }
            }
            let _e602 = phi_744_;
            let _e615 = clamp(((clamp((clamp((_e602 + ((fract((_e248 * 0.618034f)) - 0.5f) * 0.1f)), 0f, 1f) + ((_e319 * _e289) * 0.18f)), 0f, 1f) + 0.5f) - mix(0.5f, ((_e172 % 8f) * 0.14285715f), _e163)), 0f, 1f);
            if (_e219 > 0f) {
                let _e623 = FragmentUniformBlock.DitherSettings[2i];
                phi_769_ = (_e623 * _e123.z);
            } else {
                phi_769_ = 0f;
            }
            let _e626 = phi_769_;
            let _e629 = FragmentUniformBlock.NormalDitherSettings[0i];
            let _e634 = (_e215 * mix(1f, (select(1f, 0f, (clamp(_e237, 0f, 1f) < _e615)) / _e237), clamp((_e626 + (_e131 * _e629)), 0f, 1f)));
            let _e637 = FragmentUniformBlock.NormalDitherSettings[1i];
            phi_783_ = false;
            if (_e637 > 1f) {
                phi_783_ = (_e131 > 0f);
            }
            let _e641 = phi_783_;
            phi_801_ = _e634;
            if _e641 {
                let _e646 = max(max(_e634.x, _e634.y), _e634.z);
                let _e647 = (_e637 - 1f);
                phi_801_ = (_e634 * mix(1f, ((floor(((clamp(_e646, 0f, 1f) * _e647) + _e615)) / _e647) / max(_e646, 0.0001f)), _e131));
            }
            let _e658 = phi_801_;
            phi_802_ = _e658;
            break;
        }
    }
    let _e660 = phi_802_;
    let _e673 = _e660.xyz;
    let _e681 = clamp(mix(((_e118.xyz * _e673) + (_e673 * ((_e214.w * clamp((1f - (max(max(_e118.x, _e118.y), _e118.z) * 2.2f)), 0f, 1f)) * 0.18f))), _e122.xyz, vec3(_e122.w)), vec3<f32>(0f, 0f, 0f), vec3<f32>(1f, 1f, 1f));
    let _e686 = vec4<f32>(_e681.x, _e681.y, _e681.z, _e118.w);
    let _e688 = textureSample(SporeFogTexture, SporeFogSampler, _e111);
    let _e691 = clamp((_e686.xyz + _e688.xyz), vec3<f32>(0f, 0f, 0f), vec3<f32>(1f, 1f, 1f));
    let _e697 = textureSample(ForegroundTexture, ForegroundSampler, _e111);
    let _e698 = (_e697 * _e112);
    let _e699 = textureSample(ForegroundLightTexture, ForegroundLightSampler, _e111);
    let _e700 = textureSample(ForegroundEmissiveTexture, ForegroundEmissiveSampler, _e111);
    let _e701 = _e699.xyz;
    switch bitcast<i32>(0u) {
        default: {
            let _e704 = local;
            phi_859_ = false;
            if _e704 {
                let _e707 = FragmentUniformBlock.NormalDitherSettings[0i];
                phi_859_ = (_e707 <= 0f);
            }
            let _e710 = phi_859_;
            phi_865_ = false;
            if _e710 {
                let _e713 = FragmentUniformBlock.NormalDitherSettings[1i];
                phi_865_ = (_e713 <= 1f);
            }
            let _e716 = phi_865_;
            if _e716 {
                phi_1384_ = _e701;
                break;
            }
            let _e721 = max(max(_e699.x, _e699.y), _e699.z);
            if (_e721 <= 0.0001f) {
                phi_1384_ = _e701;
                break;
            }
            let _e724 = floor((_e148 * 0.03125f));
            let _e728 = floor(((_e120.x * 255f) + 0.5f));
            let _e731 = FragmentUniformBlock.DitherSettings[3i];
            let _e735 = ((_e731 * _e731) * (3f - (2f * _e731)));
            let _e739 = FragmentUniformBlock.DitherGlitch[2i];
            let _e741 = mix(24f, (_e739 * 1.45f), _e735);
            let _e743 = FragmentUniformBlock.DitherGlitch;
            let _e745 = FragmentUniformBlock.DitherGlitchAxis;
            let _e747 = normalize(_e745.xy);
            let _e752 = (_e144 - _e743.xy);
            let _e755 = (dot(_e752, _e747) / max(_e741, 1f));
            let _e758 = (dot(_e752, vec2<f32>(-(_e747.y), _e747.x)) / max((_e741 * 0.38f), 1f));
            let _e769 = (((_e735 * smoothstep(1f, 0.25f, sqrt(((_e755 * _e755) + (_e758 * _e758))))) * 0.82f) + ((_e735 * smoothstep(0.6f, 1f, _e735)) * 0.18f));
            let _e772 = FragmentUniformBlock.DitherGlitch[3i];
            let _e774 = floor((_e772 * 9f));
            let _e776 = floor((_e155 * vec2<f32>(0.0625f, 0.0625f)));
            let _e798 = fract((sin(((((_e728 * 19.19f) + (_e776.x * 7.13f)) + (_e776.y * 3.71f)) + (_e774 * 5.17f))) * 43758.547f));
            let _e799 = (fract((sin(((((_e776.x * 17f) + (_e776.y * 31f)) + (_e774 * 13f)) + (_e728 * 0.23f))) * 43758.547f)) - 0.5f);
            let _e805 = (_e724 == 2f);
            if _e805 {
                phi_961_ = 2f;
            } else {
                phi_961_ = select(1f, 3f, (_e724 == 3f));
            }
            let _e809 = phi_961_;
            let _e810 = (_e728 % 3f);
            let _e815 = FragmentUniformBlock.DitherSettings[1i];
            let _e821 = floor(((_e155 + ((vec2<f32>(_e799, (_e798 - 0.5f)) * _e769) * 9f)) / vec2(max(((_e815 * _e809) * (1f + (_e810 * 0.08f))), 1f))));
            let _e822 = (_e724 == 1f);
            if _e822 {
                phi_999_ = vec2<f32>(-(_e821.x), _e821.y);
            } else {
                if _e805 {
                    phi_998_ = _e821.yx;
                } else {
                    if (_e724 == 3f) {
                        phi_997_ = vec2<f32>(_e821.x, -(_e821.y));
                    } else {
                        phi_996_ = _e821;
                        if (_e724 >= 4f) {
                            phi_996_ = -(_e821.yx);
                        }
                        let _e839 = phi_996_;
                        phi_997_ = _e839;
                    }
                    let _e841 = phi_997_;
                    phi_998_ = _e841;
                }
                let _e843 = phi_998_;
                phi_999_ = _e843;
            }
            let _e845 = phi_999_;
            let _e858 = (_e845 + (vec2<f32>(((_e724 * 2f) % 4f), ((_e724 * 3f) % 4f)) + (vec2<f32>((_e728 % 4f), (floor((_e728 * 0.25f)) % 4f)) * 0.35f)));
            if (_e769 > 0.65f) {
                phi_1076_ = floor((_e798 * 11f));
            } else {
                switch bitcast<i32>(0u) {
                    default: {
                        if _e822 {
                            if (_e810 == 0f) {
                                phi_1029_ = 8f;
                            } else {
                                phi_1029_ = select(5f, 6f, (_e810 == 1f));
                            }
                            let _e867 = phi_1029_;
                            phi_1075_ = _e867;
                            break;
                        }
                        if _e805 {
                            if (_e810 == 0f) {
                                phi_1038_ = 2f;
                            } else {
                                phi_1038_ = select(10f, 9f, (_e810 == 1f));
                            }
                            let _e872 = phi_1038_;
                            phi_1075_ = _e872;
                            break;
                        }
                        if (_e724 == 3f) {
                            if (_e810 == 0f) {
                                phi_1048_ = 3f;
                            } else {
                                phi_1048_ = select(1f, 5f, (_e810 == 1f));
                            }
                            let _e878 = phi_1048_;
                            phi_1075_ = _e878;
                            break;
                        }
                        if (_e724 == 5f) {
                            phi_1075_ = 0f;
                            break;
                        }
                        if (_e724 == 6f) {
                            phi_1075_ = 6f;
                            break;
                        }
                        if (_e724 >= 7f) {
                            phi_1075_ = 4f;
                            break;
                        }
                        if (_e724 >= 4f) {
                            if (_e810 == 0f) {
                                phi_1067_ = 4f;
                            } else {
                                phi_1067_ = select(10f, 7f, (_e810 == 1f));
                            }
                            let _e887 = phi_1067_;
                            phi_1075_ = _e887;
                            break;
                        }
                        if (_e810 == 0f) {
                            phi_1074_ = 0f;
                        } else {
                            phi_1074_ = select(7f, 4f, (_e810 == 1f));
                        }
                        let _e892 = phi_1074_;
                        phi_1075_ = _e892;
                        break;
                    }
                }
                let _e894 = phi_1075_;
                phi_1076_ = _e894;
            }
            let _e896 = phi_1076_;
            switch bitcast<i32>(0u) {
                default: {
                    if (_e896 == 1f) {
                        phi_1328_ = ((((_e858.x + (_e858.y * 2f)) % 6f) + 1f) * 0.14285715f);
                        break;
                    }
                    if (_e896 == 2f) {
                        let _e908 = ((_e858 % vec2<f32>(4f, 4f)) - vec2<f32>(1.5f, 1.5f));
                        phi_1328_ = clamp(((dot(_e908, _e908) + 0.5f) * 0.2f), 0f, 1f);
                        break;
                    }
                    if (_e896 == 3f) {
                        phi_1328_ = (((_e858.x % 4f) + 1f) * 0.2f);
                        break;
                    }
                    if (_e896 == 4f) {
                        phi_1328_ = fract((sin(dot(floor(_e858), vec2<f32>(127.1f, 311.7f))) * 43758.547f));
                        break;
                    }
                    if (_e896 == 5f) {
                        phi_1328_ = (((_e858.y % 4f) + 1f) * 0.2f);
                        break;
                    }
                    if (_e896 == 6f) {
                        phi_1328_ = ((min(((_e858.x + _e858.y) % 4f), (((_e858.x - _e858.y) + 8f) % 4f)) + 1f) * 0.2f);
                        break;
                    }
                    if (_e896 == 7f) {
                        let _e941 = (_e858 % vec2<f32>(4f, 4f));
                        let _e950 = i32((_e858.x % 4f));
                        let _e953 = i32((_e858.y % 4f));
                        if (_e950 == 0i) {
                            if (_e953 == 0i) {
                                phi_1164_ = 16f;
                            } else {
                                if (_e953 == 1i) {
                                    phi_1163_ = 5f;
                                } else {
                                    phi_1163_ = select(1f, 13f, (_e953 == 2i));
                                }
                                let _e960 = phi_1163_;
                                phi_1164_ = _e960;
                            }
                            let _e962 = phi_1164_;
                            phi_1211_ = _e962;
                        } else {
                            if (_e950 == 1i) {
                                if (_e953 == 0i) {
                                    phi_1180_ = 8f;
                                } else {
                                    if (_e953 == 1i) {
                                        phi_1179_ = 12f;
                                    } else {
                                        phi_1179_ = select(9f, 4f, (_e953 == 2i));
                                    }
                                    let _e969 = phi_1179_;
                                    phi_1180_ = _e969;
                                }
                                let _e971 = phi_1180_;
                                phi_1210_ = _e971;
                            } else {
                                if (_e950 == 2i) {
                                    if (_e953 == 0i) {
                                        phi_1196_ = 14f;
                                    } else {
                                        if (_e953 == 1i) {
                                            phi_1195_ = 2f;
                                        } else {
                                            phi_1195_ = select(3f, 15f, (_e953 == 2i));
                                        }
                                        let _e978 = phi_1195_;
                                        phi_1196_ = _e978;
                                    }
                                    let _e980 = phi_1196_;
                                    phi_1209_ = _e980;
                                } else {
                                    if (_e953 == 0i) {
                                        phi_1208_ = 6f;
                                    } else {
                                        if (_e953 == 1i) {
                                            phi_1207_ = 10f;
                                        } else {
                                            phi_1207_ = select(11f, 7f, (_e953 == 2i));
                                        }
                                        let _e986 = phi_1207_;
                                        phi_1208_ = _e986;
                                    }
                                    let _e988 = phi_1208_;
                                    phi_1209_ = _e988;
                                }
                                let _e990 = phi_1209_;
                                phi_1210_ = _e990;
                            }
                            let _e992 = phi_1210_;
                            phi_1211_ = _e992;
                        }
                        let _e994 = phi_1211_;
                        if (((floor(_e941.x) + floor(_e941.y)) % 2f) == 0f) {
                            phi_1219_ = (_e994 * 0.04411765f);
                        } else {
                            phi_1219_ = (0.35f + (_e994 * 0.03823529f));
                        }
                        let _e1000 = phi_1219_;
                        phi_1328_ = _e1000;
                        break;
                    }
                    if (_e896 == 8f) {
                        phi_1328_ = clamp(((min(((_e858.x + ((floor((_e858.y * 0.5f)) % 2f) * 2f)) % 4f), (_e858.y % 4f)) + 1f) * 0.2f), 0f, 1f);
                        break;
                    }
                    if (_e896 == 9f) {
                        phi_1328_ = (((((_e858.x * 2f) + _e858.y) % 8f) + 1f) * 0.11111111f);
                        break;
                    }
                    if (_e896 >= 10f) {
                        let _e1026 = abs(((_e858 % vec2<f32>(8f, 8f)) - vec2<f32>(3.5f, 3.5f)));
                        phi_1328_ = clamp(((max(_e1026.x, _e1026.y) - 0.5f) * 0.2857143f), 0f, 1f);
                        break;
                    }
                    let _e1035 = i32((_e858.x % 4f));
                    let _e1038 = i32((_e858.y % 4f));
                    if (_e1035 == 0i) {
                        if (_e1038 == 0i) {
                            phi_1279_ = 16f;
                        } else {
                            if (_e1038 == 1i) {
                                phi_1278_ = 5f;
                            } else {
                                phi_1278_ = select(1f, 13f, (_e1038 == 2i));
                            }
                            let _e1045 = phi_1278_;
                            phi_1279_ = _e1045;
                        }
                        let _e1047 = phi_1279_;
                        phi_1326_ = _e1047;
                    } else {
                        if (_e1035 == 1i) {
                            if (_e1038 == 0i) {
                                phi_1295_ = 8f;
                            } else {
                                if (_e1038 == 1i) {
                                    phi_1294_ = 12f;
                                } else {
                                    phi_1294_ = select(9f, 4f, (_e1038 == 2i));
                                }
                                let _e1054 = phi_1294_;
                                phi_1295_ = _e1054;
                            }
                            let _e1056 = phi_1295_;
                            phi_1325_ = _e1056;
                        } else {
                            if (_e1035 == 2i) {
                                if (_e1038 == 0i) {
                                    phi_1311_ = 14f;
                                } else {
                                    if (_e1038 == 1i) {
                                        phi_1310_ = 2f;
                                    } else {
                                        phi_1310_ = select(3f, 15f, (_e1038 == 2i));
                                    }
                                    let _e1063 = phi_1310_;
                                    phi_1311_ = _e1063;
                                }
                                let _e1065 = phi_1311_;
                                phi_1324_ = _e1065;
                            } else {
                                if (_e1038 == 0i) {
                                    phi_1323_ = 6f;
                                } else {
                                    if (_e1038 == 1i) {
                                        phi_1322_ = 10f;
                                    } else {
                                        phi_1322_ = select(11f, 7f, (_e1038 == 2i));
                                    }
                                    let _e1071 = phi_1322_;
                                    phi_1323_ = _e1071;
                                }
                                let _e1073 = phi_1323_;
                                phi_1324_ = _e1073;
                            }
                            let _e1075 = phi_1324_;
                            phi_1325_ = _e1075;
                        }
                        let _e1077 = phi_1325_;
                        phi_1326_ = _e1077;
                    }
                    let _e1079 = phi_1326_;
                    phi_1328_ = (_e1079 * 0.05882353f);
                    break;
                }
            }
            let _e1082 = phi_1328_;
            let _e1093 = clamp(clamp((clamp((_e1082 + ((fract((_e728 * 0.618034f)) - 0.5f) * 0.1f)), 0f, 1f) + ((_e799 * _e769) * 0.18f)), 0f, 1f), 0f, 1f);
            let _e1099 = local_1;
            if (_e1099 > 0f) {
                let _e1103 = FragmentUniformBlock.DitherSettings[2i];
                phi_1351_ = (_e1103 * _e123.z);
            } else {
                phi_1351_ = 0f;
            }
            let _e1106 = phi_1351_;
            let _e1109 = FragmentUniformBlock.NormalDitherSettings[0i];
            let _e1114 = (_e701 * mix(1f, (select(1f, 0f, (clamp(_e721, 0f, 1f) < _e1093)) / _e721), clamp((_e1106 + (_e131 * _e1109)), 0f, 1f)));
            let _e1117 = FragmentUniformBlock.NormalDitherSettings[1i];
            phi_1365_ = false;
            if (_e1117 > 1f) {
                phi_1365_ = (_e131 > 0f);
            }
            let _e1121 = phi_1365_;
            phi_1383_ = _e1114;
            if _e1121 {
                let _e1126 = max(max(_e1114.x, _e1114.y), _e1114.z);
                let _e1127 = (_e1117 - 1f);
                phi_1383_ = (_e1114 * mix(1f, ((floor(((clamp(_e1126, 0f, 1f) * _e1127) + _e1093)) / _e1127) / max(_e1126, 0.0001f)), _e131));
            }
            let _e1138 = phi_1383_;
            phi_1384_ = _e1138;
            break;
        }
    }
    let _e1140 = phi_1384_;
    let _e1153 = _e1140.xyz;
    let _e1161 = clamp(mix(((_e698.xyz * _e1153) + (_e1153 * ((_e699.w * clamp((1f - (max(max(_e698.x, _e698.y), _e698.z) * 2.2f)), 0f, 1f)) * 0.18f))), _e700.xyz, vec3(_e700.w)), vec3<f32>(0f, 0f, 0f), vec3<f32>(1f, 1f, 1f));
    out_u002e_var_u002e_SV_Target0_ = clamp((vec4<f32>(_e1161.x, _e1161.y, _e1161.z, _e698.w) + (vec4<f32>(_e691.x, _e691.y, _e691.z, _e686.w) * (1f - _e698.w))), vec4<f32>(0f, 0f, 0f, 0f), vec4<f32>(1f, 1f, 1f, 1f));
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
