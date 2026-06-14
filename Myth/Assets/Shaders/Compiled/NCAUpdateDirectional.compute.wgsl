struct CellState {
    c: array<f32, 16>,
}

struct type_u002e_StructuredBuffer_u002e_CellState {
    member: array<CellState>,
}

struct type_u002e_StructuredBuffer_u002e_float {
    member: array<f32>,
}

struct type_u002e_RWStructuredBuffer_u002e_CellState {
    member: array<CellState>,
}

struct type_u002e_Params {
    Width: u32,
    Height: u32,
    Seed: u32,
    FireRate: f32,
    PlayerDirX: f32,
    PlayerDirY: f32,
}

@group(0) @binding(0) 
var<storage> SourceState: type_u002e_StructuredBuffer_u002e_CellState;
@group(0) @binding(1) 
var<storage> Weights: type_u002e_StructuredBuffer_u002e_float;
@group(1) @binding(0) 
var<storage, read_write> DestState: type_u002e_RWStructuredBuffer_u002e_CellState;
@group(2) @binding(0) 
var<uniform> Params: type_u002e_Params;
var<private> global: vec3<u32>;

fn compute_main_1() {
    var sobel_y: array<array<f32, 3>, 3>;
    var sobel_x: array<array<f32, 3>, 3>;
    var local: array<f32, 50>;
    var local_1: array<f32, 128>;
    var local_2: array<f32, 16>;
    var phi_162_: bool;
    var phi_172_: f32;
    var phi_175_: i32;
    var phi_173_: f32;
    var phi_183_: i32;
    var phi_199_: bool;
    var phi_205_: bool;
    var phi_214_: bool;
    var phi_226_: f32;
    var local_3: f32;
    var phi_239_: bool;
    var phi_245_: bool;
    var phi_253_: bool;
    var phi_279_: f32;
    var phi_280_: f32;
    var phi_281_: f32;
    var phi_282_: f32;
    var phi_283_: f32;
    var phi_284_: f32;
    var phi_285_: f32;
    var phi_286_: f32;
    var phi_287_: f32;
    var phi_288_: f32;
    var phi_289_: f32;
    var phi_290_: f32;
    var phi_291_: f32;
    var phi_292_: f32;
    var phi_293_: f32;
    var phi_294_: f32;
    var phi_296_: f32;
    var phi_299_: f32;
    var phi_301_: f32;
    var phi_303_: f32;
    var phi_305_: f32;
    var phi_307_: f32;
    var phi_309_: f32;
    var phi_311_: f32;
    var phi_313_: f32;
    var phi_315_: f32;
    var phi_317_: f32;
    var phi_319_: f32;
    var phi_321_: f32;
    var phi_323_: f32;
    var phi_325_: f32;
    var phi_327_: f32;
    var phi_329_: f32;
    var phi_331_: f32;
    var phi_333_: f32;
    var phi_335_: f32;
    var phi_337_: f32;
    var phi_339_: f32;
    var phi_341_: f32;
    var phi_343_: f32;
    var phi_345_: f32;
    var phi_347_: f32;
    var phi_349_: f32;
    var phi_351_: f32;
    var phi_353_: f32;
    var phi_355_: f32;
    var phi_357_: f32;
    var phi_359_: f32;
    var phi_361_: i32;
    var phi_297_: f32;
    var phi_300_: f32;
    var phi_302_: f32;
    var phi_304_: f32;
    var phi_306_: f32;
    var phi_308_: f32;
    var phi_310_: f32;
    var phi_312_: f32;
    var phi_314_: f32;
    var phi_316_: f32;
    var phi_318_: f32;
    var phi_320_: f32;
    var phi_322_: f32;
    var phi_324_: f32;
    var phi_326_: f32;
    var phi_328_: f32;
    var phi_330_: f32;
    var phi_332_: f32;
    var phi_334_: f32;
    var phi_336_: f32;
    var phi_338_: f32;
    var phi_340_: f32;
    var phi_342_: f32;
    var phi_344_: f32;
    var phi_346_: f32;
    var phi_348_: f32;
    var phi_350_: f32;
    var phi_352_: f32;
    var phi_354_: f32;
    var phi_356_: f32;
    var phi_358_: f32;
    var phi_360_: f32;
    var phi_400_: i32;
    var phi_414_: bool;
    var phi_428_: bool;
    var phi_434_: bool;
    var phi_443_: bool;
    var phi_470_: f32;
    var phi_471_: f32;
    var phi_472_: f32;
    var phi_473_: f32;
    var phi_474_: f32;
    var phi_475_: f32;
    var phi_476_: f32;
    var phi_477_: f32;
    var phi_478_: f32;
    var phi_479_: f32;
    var phi_480_: f32;
    var phi_481_: f32;
    var phi_482_: f32;
    var phi_483_: f32;
    var phi_484_: f32;
    var phi_485_: f32;
    var phi_367_: f32;
    var phi_369_: f32;
    var phi_370_: f32;
    var phi_371_: f32;
    var phi_372_: f32;
    var phi_373_: f32;
    var phi_374_: f32;
    var phi_375_: f32;
    var phi_376_: f32;
    var phi_377_: f32;
    var phi_378_: f32;
    var phi_379_: f32;
    var phi_380_: f32;
    var phi_381_: f32;
    var phi_382_: f32;
    var phi_383_: f32;
    var phi_384_: f32;
    var phi_385_: f32;
    var phi_386_: f32;
    var phi_387_: f32;
    var phi_388_: f32;
    var phi_389_: f32;
    var phi_390_: f32;
    var phi_391_: f32;
    var phi_392_: f32;
    var phi_393_: f32;
    var phi_394_: f32;
    var phi_395_: f32;
    var phi_396_: f32;
    var phi_397_: f32;
    var phi_398_: f32;
    var phi_399_: f32;
    var local_4: f32;
    var local_5: f32;
    var local_6: f32;
    var local_7: f32;
    var local_8: f32;
    var local_9: f32;
    var local_10: f32;
    var local_11: f32;
    var local_12: f32;
    var local_13: f32;
    var local_14: f32;
    var local_15: f32;
    var local_16: f32;
    var local_17: f32;
    var local_18: f32;
    var local_19: f32;
    var local_20: f32;
    var local_21: f32;
    var local_22: f32;
    var local_23: f32;
    var local_24: f32;
    var local_25: f32;
    var local_26: f32;
    var local_27: f32;
    var local_28: f32;
    var local_29: f32;
    var local_30: f32;
    var local_31: f32;
    var local_32: f32;
    var local_33: f32;
    var local_34: f32;
    var local_35: f32;
    var phi_605_: i32;
    var phi_616_: f32;
    var phi_619_: i32;
    var local_36: f32;
    var phi_634_: i32;
    var phi_645_: f32;
    var phi_648_: i32;
    var local_37: f32;
    var phi_685_: f32;
    var phi_692_: f32;
    var phi_699_: f32;
    var phi_706_: f32;
    var phi_713_: f32;
    var phi_720_: f32;
    var phi_727_: f32;
    var phi_734_: f32;
    var phi_741_: f32;
    var phi_748_: f32;
    var phi_755_: f32;
    var phi_762_: f32;
    var phi_769_: f32;
    var phi_776_: f32;
    var phi_783_: f32;
    var phi_790_: f32;
    var local_38: f32;
    var local_39: f32;
    var local_40: f32;
    var local_41: f32;
    var local_42: f32;
    var local_43: f32;
    var local_44: f32;
    var local_45: f32;
    var local_46: f32;
    var local_47: f32;
    var local_48: f32;
    var local_49: f32;
    var local_50: f32;
    var local_51: f32;
    var local_52: f32;
    var local_53: f32;
    var local_54: f32;
    var local_55: f32;
    var local_56: f32;
    var local_57: f32;
    var local_58: f32;
    var local_59: f32;
    var local_60: f32;
    var local_61: f32;
    var local_62: f32;
    var local_63: f32;
    var local_64: f32;
    var local_65: f32;
    var local_66: f32;
    var local_67: f32;
    var local_68: f32;
    var local_69: f32;
    var local_70: f32;
    var local_71: f32;
    var local_72: i32;

    let _e110 = global;
    switch bitcast<i32>(0u) {
        default: {
            let _e114 = Params.Width;
            phi_162_ = true;
            if !((_e110.x >= _e114)) {
                let _e119 = Params.Height;
                phi_162_ = (_e110.y >= _e119);
            }
            let _e122 = phi_162_;
            if _e122 {
                break;
            }
            let _e123 = bitcast<i32>(_e110.x);
            let _e125 = bitcast<i32>(_e110.y);
            let _e126 = vec2<i32>(_e123, _e125);
            let _e128 = ((_e110.y * _e114) + _e110.x);
            phi_172_ = -1f;
            phi_175_ = -1i;
            loop {
                let _e130 = phi_172_;
                let _e132 = phi_175_;
                local_3 = _e130;
                if (_e132 <= 1i) {
                    phi_173_ = _e130;
                    phi_183_ = -1i;
                    loop {
                        let _e135 = phi_173_;
                        let _e137 = phi_183_;
                        local_38 = _e135;
                        if (_e137 <= 1i) {
                            let _e140 = (_e126 + vec2<i32>(_e137, _e132));
                            switch bitcast<i32>(0u) {
                                default: {
                                    phi_199_ = true;
                                    if !((_e140.x < 0i)) {
                                        phi_199_ = (_e140.y < 0i);
                                    }
                                    let _e148 = phi_199_;
                                    phi_205_ = true;
                                    if !(_e148) {
                                        phi_205_ = (_e140.x >= bitcast<i32>(_e114));
                                    }
                                    let _e153 = phi_205_;
                                    phi_214_ = true;
                                    if !(_e153) {
                                        let _e157 = Params.Height;
                                        phi_214_ = (_e140.y >= bitcast<i32>(_e157));
                                    }
                                    let _e161 = phi_214_;
                                    if _e161 {
                                        phi_226_ = 0f;
                                        break;
                                    }
                                    let _e169 = SourceState.member[((bitcast<u32>(_e140.y) * _e114) + bitcast<u32>(_e140.x))];
                                    phi_226_ = _e169.c[3];
                                    break;
                                }
                            }
                            let _e173 = phi_226_;
                            local_39 = max(_e135, _e173);
                            continue;
                        } else {
                            break;
                        }
                        continuing {
                            let _e1091 = local_39;
                            phi_173_ = _e1091;
                            phi_183_ = (_e137 + 1i);
                        }
                    }
                    continue;
                } else {
                    break;
                }
                continuing {
                    let _e1087 = local_38;
                    phi_172_ = _e1087;
                    phi_175_ = (_e132 + 1i);
                }
            }
            let _e178 = local_3;
            if !((_e178 > 0.1f)) {
                DestState.member[_e128] = CellState(array<f32, 16>(0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f));
                break;
            }
            sobel_x = array<array<f32, 3>, 3>(array<f32, 3>(-0.125f, 0f, 0.125f), array<f32, 3>(-0.25f, 0f, 0.25f), array<f32, 3>(-0.125f, 0f, 0.125f));
            sobel_y = array<array<f32, 3>, 3>(array<f32, 3>(-0.125f, -0.25f, -0.125f), array<f32, 3>(0f, 0f, 0f), array<f32, 3>(0.125f, 0.25f, 0.125f));
            switch bitcast<i32>(0u) {
                default: {
                    phi_239_ = true;
                    if !((_e123 < 0i)) {
                        phi_239_ = (_e125 < 0i);
                    }
                    let _e188 = phi_239_;
                    phi_245_ = true;
                    if !(_e188) {
                        phi_245_ = (_e123 >= bitcast<i32>(_e114));
                    }
                    let _e193 = phi_245_;
                    phi_253_ = true;
                    if !(_e193) {
                        let _e196 = Params.Height;
                        phi_253_ = (_e125 >= bitcast<i32>(_e196));
                    }
                    let _e200 = phi_253_;
                    if _e200 {
                        phi_279_ = 0f;
                        phi_280_ = 0f;
                        phi_281_ = 0f;
                        phi_282_ = 0f;
                        phi_283_ = 0f;
                        phi_284_ = 0f;
                        phi_285_ = 0f;
                        phi_286_ = 0f;
                        phi_287_ = 0f;
                        phi_288_ = 0f;
                        phi_289_ = 0f;
                        phi_290_ = 0f;
                        phi_291_ = 0f;
                        phi_292_ = 0f;
                        phi_293_ = 0f;
                        phi_294_ = 0f;
                        break;
                    }
                    let _e207 = SourceState.member[((bitcast<u32>(_e125) * _e114) + bitcast<u32>(_e123))];
                    phi_279_ = _e207.c[0];
                    phi_280_ = _e207.c[1];
                    phi_281_ = _e207.c[2];
                    phi_282_ = _e207.c[3];
                    phi_283_ = _e207.c[4];
                    phi_284_ = _e207.c[5];
                    phi_285_ = _e207.c[6];
                    phi_286_ = _e207.c[7];
                    phi_287_ = _e207.c[8];
                    phi_288_ = _e207.c[9];
                    phi_289_ = _e207.c[10];
                    phi_290_ = _e207.c[11];
                    phi_291_ = _e207.c[12];
                    phi_292_ = _e207.c[13];
                    phi_293_ = _e207.c[14];
                    phi_294_ = _e207.c[15];
                    break;
                }
            }
            let _e226 = phi_279_;
            let _e228 = phi_280_;
            let _e230 = phi_281_;
            let _e232 = phi_282_;
            let _e234 = phi_283_;
            let _e236 = phi_284_;
            let _e238 = phi_285_;
            let _e240 = phi_286_;
            let _e242 = phi_287_;
            let _e244 = phi_288_;
            let _e246 = phi_289_;
            let _e248 = phi_290_;
            let _e250 = phi_291_;
            let _e252 = phi_292_;
            let _e254 = phi_293_;
            let _e256 = phi_294_;
            phi_296_ = 0f;
            phi_299_ = 0f;
            phi_301_ = 0f;
            phi_303_ = 0f;
            phi_305_ = 0f;
            phi_307_ = 0f;
            phi_309_ = 0f;
            phi_311_ = 0f;
            phi_313_ = 0f;
            phi_315_ = 0f;
            phi_317_ = 0f;
            phi_319_ = 0f;
            phi_321_ = 0f;
            phi_323_ = 0f;
            phi_325_ = 0f;
            phi_327_ = 0f;
            phi_329_ = 0f;
            phi_331_ = 0f;
            phi_333_ = 0f;
            phi_335_ = 0f;
            phi_337_ = 0f;
            phi_339_ = 0f;
            phi_341_ = 0f;
            phi_343_ = 0f;
            phi_345_ = 0f;
            phi_347_ = 0f;
            phi_349_ = 0f;
            phi_351_ = 0f;
            phi_353_ = 0f;
            phi_355_ = 0f;
            phi_357_ = 0f;
            phi_359_ = 0f;
            phi_361_ = -1i;
            loop {
                let _e258 = phi_296_;
                let _e260 = phi_299_;
                let _e262 = phi_301_;
                let _e264 = phi_303_;
                let _e266 = phi_305_;
                let _e268 = phi_307_;
                let _e270 = phi_309_;
                let _e272 = phi_311_;
                let _e274 = phi_313_;
                let _e276 = phi_315_;
                let _e278 = phi_317_;
                let _e280 = phi_319_;
                let _e282 = phi_321_;
                let _e284 = phi_323_;
                let _e286 = phi_325_;
                let _e288 = phi_327_;
                let _e290 = phi_329_;
                let _e292 = phi_331_;
                let _e294 = phi_333_;
                let _e296 = phi_335_;
                let _e298 = phi_337_;
                let _e300 = phi_339_;
                let _e302 = phi_341_;
                let _e304 = phi_343_;
                let _e306 = phi_345_;
                let _e308 = phi_347_;
                let _e310 = phi_349_;
                let _e312 = phi_351_;
                let _e314 = phi_353_;
                let _e316 = phi_355_;
                let _e318 = phi_357_;
                let _e320 = phi_359_;
                let _e322 = phi_361_;
                local_4 = _e320;
                local_5 = _e318;
                local_6 = _e316;
                local_7 = _e314;
                local_8 = _e312;
                local_9 = _e310;
                local_10 = _e308;
                local_11 = _e306;
                local_12 = _e304;
                local_13 = _e302;
                local_14 = _e300;
                local_15 = _e298;
                local_16 = _e296;
                local_17 = _e294;
                local_18 = _e292;
                local_19 = _e290;
                local_20 = _e288;
                local_21 = _e286;
                local_22 = _e284;
                local_23 = _e282;
                local_24 = _e280;
                local_25 = _e278;
                local_26 = _e276;
                local_27 = _e274;
                local_28 = _e272;
                local_29 = _e270;
                local_30 = _e268;
                local_31 = _e266;
                local_32 = _e264;
                local_33 = _e262;
                local_34 = _e260;
                local_35 = _e258;
                if (_e322 <= 1i) {
                    phi_297_ = _e258;
                    phi_300_ = _e260;
                    phi_302_ = _e262;
                    phi_304_ = _e264;
                    phi_306_ = _e266;
                    phi_308_ = _e268;
                    phi_310_ = _e270;
                    phi_312_ = _e272;
                    phi_314_ = _e274;
                    phi_316_ = _e276;
                    phi_318_ = _e278;
                    phi_320_ = _e280;
                    phi_322_ = _e282;
                    phi_324_ = _e284;
                    phi_326_ = _e286;
                    phi_328_ = _e288;
                    phi_330_ = _e290;
                    phi_332_ = _e292;
                    phi_334_ = _e294;
                    phi_336_ = _e296;
                    phi_338_ = _e298;
                    phi_340_ = _e300;
                    phi_342_ = _e302;
                    phi_344_ = _e304;
                    phi_346_ = _e306;
                    phi_348_ = _e308;
                    phi_350_ = _e310;
                    phi_352_ = _e312;
                    phi_354_ = _e314;
                    phi_356_ = _e316;
                    phi_358_ = _e318;
                    phi_360_ = _e320;
                    phi_400_ = -1i;
                    loop {
                        let _e325 = phi_297_;
                        let _e327 = phi_300_;
                        let _e329 = phi_302_;
                        let _e331 = phi_304_;
                        let _e333 = phi_306_;
                        let _e335 = phi_308_;
                        let _e337 = phi_310_;
                        let _e339 = phi_312_;
                        let _e341 = phi_314_;
                        let _e343 = phi_316_;
                        let _e345 = phi_318_;
                        let _e347 = phi_320_;
                        let _e349 = phi_322_;
                        let _e351 = phi_324_;
                        let _e353 = phi_326_;
                        let _e355 = phi_328_;
                        let _e357 = phi_330_;
                        let _e359 = phi_332_;
                        let _e361 = phi_334_;
                        let _e363 = phi_336_;
                        let _e365 = phi_338_;
                        let _e367 = phi_340_;
                        let _e369 = phi_342_;
                        let _e371 = phi_344_;
                        let _e373 = phi_346_;
                        let _e375 = phi_348_;
                        let _e377 = phi_350_;
                        let _e379 = phi_352_;
                        let _e381 = phi_354_;
                        let _e383 = phi_356_;
                        let _e385 = phi_358_;
                        let _e387 = phi_360_;
                        let _e389 = phi_400_;
                        local_40 = _e325;
                        local_41 = _e327;
                        local_42 = _e329;
                        local_43 = _e331;
                        local_44 = _e333;
                        local_45 = _e335;
                        local_46 = _e337;
                        local_47 = _e339;
                        local_48 = _e341;
                        local_49 = _e343;
                        local_50 = _e345;
                        local_51 = _e347;
                        local_52 = _e349;
                        local_53 = _e351;
                        local_54 = _e353;
                        local_55 = _e355;
                        local_56 = _e357;
                        local_57 = _e359;
                        local_58 = _e361;
                        local_59 = _e363;
                        local_60 = _e365;
                        local_61 = _e367;
                        local_62 = _e369;
                        local_63 = _e371;
                        local_64 = _e373;
                        local_65 = _e375;
                        local_66 = _e377;
                        local_67 = _e379;
                        local_68 = _e381;
                        local_69 = _e383;
                        local_70 = _e385;
                        local_71 = _e387;
                        if (_e389 <= 1i) {
                            let _e391 = (_e322 + 1i);
                            let _e392 = (_e389 + 1i);
                            let _e395 = sobel_x[_e391][_e392];
                            let _e398 = sobel_y[_e391][_e392];
                            local_72 = _e392;
                            phi_414_ = false;
                            if (_e395 == 0f) {
                                phi_414_ = (_e398 == 0f);
                            }
                            let _e402 = phi_414_;
                            if _e402 {
                                phi_367_ = _e325;
                                phi_369_ = _e327;
                                phi_370_ = _e329;
                                phi_371_ = _e331;
                                phi_372_ = _e333;
                                phi_373_ = _e335;
                                phi_374_ = _e337;
                                phi_375_ = _e339;
                                phi_376_ = _e341;
                                phi_377_ = _e343;
                                phi_378_ = _e345;
                                phi_379_ = _e347;
                                phi_380_ = _e349;
                                phi_381_ = _e351;
                                phi_382_ = _e353;
                                phi_383_ = _e355;
                                phi_384_ = _e357;
                                phi_385_ = _e359;
                                phi_386_ = _e361;
                                phi_387_ = _e363;
                                phi_388_ = _e365;
                                phi_389_ = _e367;
                                phi_390_ = _e369;
                                phi_391_ = _e371;
                                phi_392_ = _e373;
                                phi_393_ = _e375;
                                phi_394_ = _e377;
                                phi_395_ = _e379;
                                phi_396_ = _e381;
                                phi_397_ = _e383;
                                phi_398_ = _e385;
                                phi_399_ = _e387;
                                continue;
                            }
                            let _e404 = (_e126 + vec2<i32>(_e389, _e322));
                            switch bitcast<i32>(0u) {
                                default: {
                                    phi_428_ = true;
                                    if !((_e404.x < 0i)) {
                                        phi_428_ = (_e404.y < 0i);
                                    }
                                    let _e412 = phi_428_;
                                    phi_434_ = true;
                                    if !(_e412) {
                                        phi_434_ = (_e404.x >= bitcast<i32>(_e114));
                                    }
                                    let _e417 = phi_434_;
                                    phi_443_ = true;
                                    if !(_e417) {
                                        let _e421 = Params.Height;
                                        phi_443_ = (_e404.y >= bitcast<i32>(_e421));
                                    }
                                    let _e425 = phi_443_;
                                    if _e425 {
                                        phi_470_ = 0f;
                                        phi_471_ = 0f;
                                        phi_472_ = 0f;
                                        phi_473_ = 0f;
                                        phi_474_ = 0f;
                                        phi_475_ = 0f;
                                        phi_476_ = 0f;
                                        phi_477_ = 0f;
                                        phi_478_ = 0f;
                                        phi_479_ = 0f;
                                        phi_480_ = 0f;
                                        phi_481_ = 0f;
                                        phi_482_ = 0f;
                                        phi_483_ = 0f;
                                        phi_484_ = 0f;
                                        phi_485_ = 0f;
                                        break;
                                    }
                                    let _e433 = SourceState.member[((bitcast<u32>(_e404.y) * _e114) + bitcast<u32>(_e404.x))];
                                    phi_470_ = _e433.c[0];
                                    phi_471_ = _e433.c[1];
                                    phi_472_ = _e433.c[2];
                                    phi_473_ = _e433.c[3];
                                    phi_474_ = _e433.c[4];
                                    phi_475_ = _e433.c[5];
                                    phi_476_ = _e433.c[6];
                                    phi_477_ = _e433.c[7];
                                    phi_478_ = _e433.c[8];
                                    phi_479_ = _e433.c[9];
                                    phi_480_ = _e433.c[10];
                                    phi_481_ = _e433.c[11];
                                    phi_482_ = _e433.c[12];
                                    phi_483_ = _e433.c[13];
                                    phi_484_ = _e433.c[14];
                                    phi_485_ = _e433.c[15];
                                    break;
                                }
                            }
                            let _e452 = phi_470_;
                            let _e454 = phi_471_;
                            let _e456 = phi_472_;
                            let _e458 = phi_473_;
                            let _e460 = phi_474_;
                            let _e462 = phi_475_;
                            let _e464 = phi_476_;
                            let _e466 = phi_477_;
                            let _e468 = phi_478_;
                            let _e470 = phi_479_;
                            let _e472 = phi_480_;
                            let _e474 = phi_481_;
                            let _e476 = phi_482_;
                            let _e478 = phi_483_;
                            let _e480 = phi_484_;
                            let _e482 = phi_485_;
                            phi_367_ = (_e325 + (_e398 * _e482));
                            phi_369_ = (_e327 + (_e395 * _e482));
                            phi_370_ = (_e329 + (_e398 * _e480));
                            phi_371_ = (_e331 + (_e395 * _e480));
                            phi_372_ = (_e333 + (_e398 * _e478));
                            phi_373_ = (_e335 + (_e395 * _e478));
                            phi_374_ = (_e337 + (_e398 * _e476));
                            phi_375_ = (_e339 + (_e395 * _e476));
                            phi_376_ = (_e341 + (_e398 * _e474));
                            phi_377_ = (_e343 + (_e395 * _e474));
                            phi_378_ = (_e345 + (_e398 * _e472));
                            phi_379_ = (_e347 + (_e395 * _e472));
                            phi_380_ = (_e349 + (_e398 * _e470));
                            phi_381_ = (_e351 + (_e395 * _e470));
                            phi_382_ = (_e353 + (_e398 * _e468));
                            phi_383_ = (_e355 + (_e395 * _e468));
                            phi_384_ = (_e357 + (_e398 * _e466));
                            phi_385_ = (_e359 + (_e395 * _e466));
                            phi_386_ = (_e361 + (_e398 * _e464));
                            phi_387_ = (_e363 + (_e395 * _e464));
                            phi_388_ = (_e365 + (_e398 * _e462));
                            phi_389_ = (_e367 + (_e395 * _e462));
                            phi_390_ = (_e369 + (_e398 * _e460));
                            phi_391_ = (_e371 + (_e395 * _e460));
                            phi_392_ = (_e373 + (_e398 * _e458));
                            phi_393_ = (_e375 + (_e395 * _e458));
                            phi_394_ = (_e377 + (_e398 * _e456));
                            phi_395_ = (_e379 + (_e395 * _e456));
                            phi_396_ = (_e381 + (_e398 * _e454));
                            phi_397_ = (_e383 + (_e395 * _e454));
                            phi_398_ = (_e385 + (_e398 * _e452));
                            phi_399_ = (_e387 + (_e395 * _e452));
                            continue;
                        } else {
                            break;
                        }
                        continuing {
                            let _e548 = phi_367_;
                            let _e550 = phi_369_;
                            let _e552 = phi_370_;
                            let _e554 = phi_371_;
                            let _e556 = phi_372_;
                            let _e558 = phi_373_;
                            let _e560 = phi_374_;
                            let _e562 = phi_375_;
                            let _e564 = phi_376_;
                            let _e566 = phi_377_;
                            let _e568 = phi_378_;
                            let _e570 = phi_379_;
                            let _e572 = phi_380_;
                            let _e574 = phi_381_;
                            let _e576 = phi_382_;
                            let _e578 = phi_383_;
                            let _e580 = phi_384_;
                            let _e582 = phi_385_;
                            let _e584 = phi_386_;
                            let _e586 = phi_387_;
                            let _e588 = phi_388_;
                            let _e590 = phi_389_;
                            let _e592 = phi_390_;
                            let _e594 = phi_391_;
                            let _e596 = phi_392_;
                            let _e598 = phi_393_;
                            let _e600 = phi_394_;
                            let _e602 = phi_395_;
                            let _e604 = phi_396_;
                            let _e606 = phi_397_;
                            let _e608 = phi_398_;
                            let _e610 = phi_399_;
                            phi_297_ = _e548;
                            phi_300_ = _e550;
                            phi_302_ = _e552;
                            phi_304_ = _e554;
                            phi_306_ = _e556;
                            phi_308_ = _e558;
                            phi_310_ = _e560;
                            phi_312_ = _e562;
                            phi_314_ = _e564;
                            phi_316_ = _e566;
                            phi_318_ = _e568;
                            phi_320_ = _e570;
                            phi_322_ = _e572;
                            phi_324_ = _e574;
                            phi_326_ = _e576;
                            phi_328_ = _e578;
                            phi_330_ = _e580;
                            phi_332_ = _e582;
                            phi_334_ = _e584;
                            phi_336_ = _e586;
                            phi_338_ = _e588;
                            phi_340_ = _e590;
                            phi_342_ = _e592;
                            phi_344_ = _e594;
                            phi_346_ = _e596;
                            phi_348_ = _e598;
                            phi_350_ = _e600;
                            phi_352_ = _e602;
                            phi_354_ = _e604;
                            phi_356_ = _e606;
                            phi_358_ = _e608;
                            phi_360_ = _e610;
                            let _e1248 = local_72;
                            phi_400_ = _e1248;
                        }
                    }
                    continue;
                } else {
                    break;
                }
                continuing {
                    let _e1119 = local_40;
                    phi_296_ = _e1119;
                    let _e1122 = local_41;
                    phi_299_ = _e1122;
                    let _e1125 = local_42;
                    phi_301_ = _e1125;
                    let _e1128 = local_43;
                    phi_303_ = _e1128;
                    let _e1131 = local_44;
                    phi_305_ = _e1131;
                    let _e1134 = local_45;
                    phi_307_ = _e1134;
                    let _e1137 = local_46;
                    phi_309_ = _e1137;
                    let _e1140 = local_47;
                    phi_311_ = _e1140;
                    let _e1143 = local_48;
                    phi_313_ = _e1143;
                    let _e1146 = local_49;
                    phi_315_ = _e1146;
                    let _e1149 = local_50;
                    phi_317_ = _e1149;
                    let _e1152 = local_51;
                    phi_319_ = _e1152;
                    let _e1155 = local_52;
                    phi_321_ = _e1155;
                    let _e1158 = local_53;
                    phi_323_ = _e1158;
                    let _e1161 = local_54;
                    phi_325_ = _e1161;
                    let _e1164 = local_55;
                    phi_327_ = _e1164;
                    let _e1167 = local_56;
                    phi_329_ = _e1167;
                    let _e1170 = local_57;
                    phi_331_ = _e1170;
                    let _e1173 = local_58;
                    phi_333_ = _e1173;
                    let _e1176 = local_59;
                    phi_335_ = _e1176;
                    let _e1179 = local_60;
                    phi_337_ = _e1179;
                    let _e1182 = local_61;
                    phi_339_ = _e1182;
                    let _e1185 = local_62;
                    phi_341_ = _e1185;
                    let _e1188 = local_63;
                    phi_343_ = _e1188;
                    let _e1191 = local_64;
                    phi_345_ = _e1191;
                    let _e1194 = local_65;
                    phi_347_ = _e1194;
                    let _e1197 = local_66;
                    phi_349_ = _e1197;
                    let _e1200 = local_67;
                    phi_351_ = _e1200;
                    let _e1203 = local_68;
                    phi_353_ = _e1203;
                    let _e1206 = local_69;
                    phi_355_ = _e1206;
                    let _e1209 = local_70;
                    phi_357_ = _e1209;
                    let _e1212 = local_71;
                    phi_359_ = _e1212;
                    phi_361_ = (_e322 + 1i);
                }
            }
            local[0i] = _e226;
            let _e615 = local_4;
            local[1i] = _e615;
            let _e618 = local_5;
            local[2i] = _e618;
            local[3i] = _e228;
            let _e622 = local_6;
            local[4i] = _e622;
            let _e625 = local_7;
            local[5i] = _e625;
            local[6i] = _e230;
            let _e629 = local_8;
            local[7i] = _e629;
            let _e632 = local_9;
            local[8i] = _e632;
            local[9i] = _e232;
            let _e636 = local_10;
            local[10i] = _e636;
            let _e639 = local_11;
            local[11i] = _e639;
            local[12i] = _e234;
            let _e643 = local_12;
            local[13i] = _e643;
            let _e646 = local_13;
            local[14i] = _e646;
            local[15i] = _e236;
            let _e650 = local_14;
            local[16i] = _e650;
            let _e653 = local_15;
            local[17i] = _e653;
            local[18i] = _e238;
            let _e657 = local_16;
            local[19i] = _e657;
            let _e660 = local_17;
            local[20i] = _e660;
            local[21i] = _e240;
            let _e664 = local_18;
            local[22i] = _e664;
            let _e667 = local_19;
            local[23i] = _e667;
            local[24i] = _e242;
            let _e671 = local_20;
            local[25i] = _e671;
            let _e674 = local_21;
            local[26i] = _e674;
            local[27i] = _e244;
            let _e678 = local_22;
            local[28i] = _e678;
            let _e681 = local_23;
            local[29i] = _e681;
            local[30i] = _e246;
            let _e685 = local_24;
            local[31i] = _e685;
            let _e688 = local_25;
            local[32i] = _e688;
            local[33i] = _e248;
            let _e692 = local_26;
            local[34i] = _e692;
            let _e695 = local_27;
            local[35i] = _e695;
            local[36i] = _e250;
            let _e699 = local_28;
            local[37i] = _e699;
            let _e702 = local_29;
            local[38i] = _e702;
            local[39i] = _e252;
            let _e706 = local_30;
            local[40i] = _e706;
            let _e709 = local_31;
            local[41i] = _e709;
            local[42i] = _e254;
            let _e713 = local_32;
            local[43i] = _e713;
            let _e716 = local_33;
            local[44i] = _e716;
            local[45i] = _e256;
            let _e720 = local_34;
            local[46i] = _e720;
            let _e723 = local_35;
            local[47i] = _e723;
            let _e725 = Params.PlayerDirX;
            local[48i] = _e725;
            let _e728 = Params.PlayerDirY;
            local[49i] = _e728;
            phi_605_ = 0i;
            loop {
                let _e731 = phi_605_;
                if (_e731 < 128i) {
                    let _e737 = Weights.member[bitcast<u32>((6400i + _e731))];
                    phi_616_ = _e737;
                    phi_619_ = 0i;
                    loop {
                        let _e739 = phi_616_;
                        let _e741 = phi_619_;
                        local_36 = _e739;
                        if (_e741 < 50i) {
                            continue;
                        } else {
                            break;
                        }
                        continuing {
                            let _e744 = local[_e741];
                            let _e750 = Weights.member[bitcast<u32>(((_e741 * 128i) + _e731))];
                            phi_616_ = (_e739 + (_e744 * _e750));
                            phi_619_ = (_e741 + 1i);
                        }
                    }
                    let _e755 = local_36;
                    local_1[_e731] = max(_e755, 0f);
                    continue;
                } else {
                    break;
                }
                continuing {
                    phi_605_ = (_e731 + 1i);
                }
            }
            phi_634_ = 0i;
            loop {
                let _e760 = phi_634_;
                if (_e760 < 16i) {
                    let _e766 = Weights.member[bitcast<u32>((8576i + _e760))];
                    phi_645_ = _e766;
                    phi_648_ = 0i;
                    loop {
                        let _e768 = phi_645_;
                        let _e770 = phi_648_;
                        local_37 = _e768;
                        if (_e770 < 128i) {
                            continue;
                        } else {
                            break;
                        }
                        continuing {
                            let _e773 = local_1[_e770];
                            let _e780 = Weights.member[bitcast<u32>(((6528i + (_e770 * 16i)) + _e760))];
                            phi_645_ = (_e768 + (_e773 * _e780));
                            phi_648_ = (_e770 + 1i);
                        }
                    }
                    let _e786 = local_37;
                    local_2[_e760] = _e786;
                    continue;
                } else {
                    break;
                }
                continuing {
                    phi_634_ = (_e760 + 1i);
                }
            }
            let _e789 = Params.Seed;
            let _e792 = ((_e110.x * 374761393u) + (_e110.y * 668265263u));
            let _e794 = (_e792 + (_e789 * 2654435761u));
            let _e798 = ((_e794 ^ (_e794 >> bitcast<u32>(13u))) * 1274126177u);
            let _e806 = Params.FireRate;
            let _e807 = ((f32(((_e798 ^ (_e798 >> bitcast<u32>(16u))) & 16777215u)) * 0.000000059604645f) <= _e806);
            if _e807 {
                let _e809 = local_2[0i];
                phi_685_ = _e809;
            } else {
                phi_685_ = 0f;
            }
            let _e811 = phi_685_;
            if _e807 {
                let _e814 = local_2[1i];
                phi_692_ = _e814;
            } else {
                phi_692_ = 0f;
            }
            let _e816 = phi_692_;
            if _e807 {
                let _e819 = local_2[2i];
                phi_699_ = _e819;
            } else {
                phi_699_ = 0f;
            }
            let _e821 = phi_699_;
            if _e807 {
                let _e824 = local_2[3i];
                phi_706_ = _e824;
            } else {
                phi_706_ = 0f;
            }
            let _e826 = phi_706_;
            if _e807 {
                let _e829 = local_2[4i];
                phi_713_ = _e829;
            } else {
                phi_713_ = 0f;
            }
            let _e831 = phi_713_;
            if _e807 {
                let _e834 = local_2[5i];
                phi_720_ = _e834;
            } else {
                phi_720_ = 0f;
            }
            let _e836 = phi_720_;
            if _e807 {
                let _e839 = local_2[6i];
                phi_727_ = _e839;
            } else {
                phi_727_ = 0f;
            }
            let _e841 = phi_727_;
            if _e807 {
                let _e844 = local_2[7i];
                phi_734_ = _e844;
            } else {
                phi_734_ = 0f;
            }
            let _e846 = phi_734_;
            if _e807 {
                let _e849 = local_2[8i];
                phi_741_ = _e849;
            } else {
                phi_741_ = 0f;
            }
            let _e851 = phi_741_;
            if _e807 {
                let _e854 = local_2[9i];
                phi_748_ = _e854;
            } else {
                phi_748_ = 0f;
            }
            let _e856 = phi_748_;
            if _e807 {
                let _e859 = local_2[10i];
                phi_755_ = _e859;
            } else {
                phi_755_ = 0f;
            }
            let _e861 = phi_755_;
            if _e807 {
                let _e864 = local_2[11i];
                phi_762_ = _e864;
            } else {
                phi_762_ = 0f;
            }
            let _e866 = phi_762_;
            if _e807 {
                let _e869 = local_2[12i];
                phi_769_ = _e869;
            } else {
                phi_769_ = 0f;
            }
            let _e871 = phi_769_;
            if _e807 {
                let _e874 = local_2[13i];
                phi_776_ = _e874;
            } else {
                phi_776_ = 0f;
            }
            let _e876 = phi_776_;
            if _e807 {
                let _e879 = local_2[14i];
                phi_783_ = _e879;
            } else {
                phi_783_ = 0f;
            }
            let _e881 = phi_783_;
            if _e807 {
                let _e884 = local_2[15i];
                phi_790_ = _e884;
            } else {
                phi_790_ = 0f;
            }
            let _e886 = phi_790_;
            let _e890 = (_e792 + ((_e789 + 1388u) * 2654435761u));
            let _e894 = ((_e890 ^ (_e890 >> bitcast<u32>(13u))) * 1274126177u);
            let _e906 = (_e792 + ((_e789 + 1485u) * 2654435761u));
            let _e910 = ((_e906 ^ (_e906 >> bitcast<u32>(13u))) * 1274126177u);
            let _e922 = (_e792 + ((_e789 + 1582u) * 2654435761u));
            let _e926 = ((_e922 ^ (_e922 >> bitcast<u32>(13u))) * 1274126177u);
            let _e938 = (_e792 + ((_e789 + 1679u) * 2654435761u));
            let _e942 = ((_e938 ^ (_e938 >> bitcast<u32>(13u))) * 1274126177u);
            let _e954 = (_e792 + ((_e789 + 1776u) * 2654435761u));
            let _e958 = ((_e954 ^ (_e954 >> bitcast<u32>(13u))) * 1274126177u);
            let _e970 = (_e792 + ((_e789 + 1873u) * 2654435761u));
            let _e974 = ((_e970 ^ (_e970 >> bitcast<u32>(13u))) * 1274126177u);
            let _e986 = (_e792 + ((_e789 + 1970u) * 2654435761u));
            let _e990 = ((_e986 ^ (_e986 >> bitcast<u32>(13u))) * 1274126177u);
            let _e1002 = (_e792 + ((_e789 + 2067u) * 2654435761u));
            let _e1006 = ((_e1002 ^ (_e1002 >> bitcast<u32>(13u))) * 1274126177u);
            let _e1018 = (_e792 + ((_e789 + 2164u) * 2654435761u));
            let _e1022 = ((_e1018 ^ (_e1018 >> bitcast<u32>(13u))) * 1274126177u);
            let _e1034 = (_e792 + ((_e789 + 2261u) * 2654435761u));
            let _e1038 = ((_e1034 ^ (_e1034 >> bitcast<u32>(13u))) * 1274126177u);
            let _e1050 = (_e792 + ((_e789 + 2358u) * 2654435761u));
            let _e1054 = ((_e1050 ^ (_e1050 >> bitcast<u32>(13u))) * 1274126177u);
            let _e1066 = (_e792 + ((_e789 + 2455u) * 2654435761u));
            let _e1070 = ((_e1066 ^ (_e1066 >> bitcast<u32>(13u))) * 1274126177u);
            DestState.member[_e128] = CellState(array<f32, 16>((_e226 + _e811), (_e228 + _e816), (_e230 + _e821), (_e232 + _e826), ((_e234 + _e831) + (((f32(((_e894 ^ (_e894 >> bitcast<u32>(16u))) & 16777215u)) * 0.000000059604645f) - 0.5f) * 0.01f)), ((_e236 + _e836) + (((f32(((_e910 ^ (_e910 >> bitcast<u32>(16u))) & 16777215u)) * 0.000000059604645f) - 0.5f) * 0.01f)), ((_e238 + _e841) + (((f32(((_e926 ^ (_e926 >> bitcast<u32>(16u))) & 16777215u)) * 0.000000059604645f) - 0.5f) * 0.01f)), ((_e240 + _e846) + (((f32(((_e942 ^ (_e942 >> bitcast<u32>(16u))) & 16777215u)) * 0.000000059604645f) - 0.5f) * 0.01f)), ((_e242 + _e851) + (((f32(((_e958 ^ (_e958 >> bitcast<u32>(16u))) & 16777215u)) * 0.000000059604645f) - 0.5f) * 0.01f)), ((_e244 + _e856) + (((f32(((_e974 ^ (_e974 >> bitcast<u32>(16u))) & 16777215u)) * 0.000000059604645f) - 0.5f) * 0.01f)), ((_e246 + _e861) + (((f32(((_e990 ^ (_e990 >> bitcast<u32>(16u))) & 16777215u)) * 0.000000059604645f) - 0.5f) * 0.01f)), ((_e248 + _e866) + (((f32(((_e1006 ^ (_e1006 >> bitcast<u32>(16u))) & 16777215u)) * 0.000000059604645f) - 0.5f) * 0.01f)), ((_e250 + _e871) + (((f32(((_e1022 ^ (_e1022 >> bitcast<u32>(16u))) & 16777215u)) * 0.000000059604645f) - 0.5f) * 0.01f)), ((_e252 + _e876) + (((f32(((_e1038 ^ (_e1038 >> bitcast<u32>(16u))) & 16777215u)) * 0.000000059604645f) - 0.5f) * 0.01f)), ((_e254 + _e881) + (((f32(((_e1054 ^ (_e1054 >> bitcast<u32>(16u))) & 16777215u)) * 0.000000059604645f) - 0.5f) * 0.01f)), ((_e256 + _e886) + (((f32(((_e1070 ^ (_e1070 >> bitcast<u32>(16u))) & 16777215u)) * 0.000000059604645f) - 0.5f) * 0.01f))));
            break;
        }
    }
    return;
}

@compute @workgroup_size(8, 8, 1) 
fn compute_main(@builtin(global_invocation_id) param: vec3<u32>) {
    global = param;
    compute_main_1();
}
