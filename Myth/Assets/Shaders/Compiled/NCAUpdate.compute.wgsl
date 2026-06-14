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
    var local: array<f32, 48>;
    var local_1: array<f32, 128>;
    var local_2: array<f32, 16>;
    var phi_160_: bool;
    var phi_170_: f32;
    var phi_173_: i32;
    var phi_171_: f32;
    var phi_181_: i32;
    var phi_197_: bool;
    var phi_203_: bool;
    var phi_212_: bool;
    var phi_224_: f32;
    var local_3: f32;
    var phi_237_: bool;
    var phi_243_: bool;
    var phi_251_: bool;
    var phi_277_: f32;
    var phi_278_: f32;
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
    var phi_294_: f32;
    var phi_297_: f32;
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
    var phi_359_: i32;
    var phi_295_: f32;
    var phi_298_: f32;
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
    var phi_398_: i32;
    var phi_412_: bool;
    var phi_426_: bool;
    var phi_432_: bool;
    var phi_441_: bool;
    var phi_468_: f32;
    var phi_469_: f32;
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
    var phi_365_: f32;
    var phi_367_: f32;
    var phi_368_: f32;
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
    var phi_597_: i32;
    var phi_608_: f32;
    var phi_611_: i32;
    var local_36: f32;
    var phi_626_: i32;
    var phi_637_: f32;
    var phi_640_: i32;
    var local_37: f32;
    var phi_677_: f32;
    var phi_684_: f32;
    var phi_691_: f32;
    var phi_698_: f32;
    var phi_705_: f32;
    var phi_712_: f32;
    var phi_719_: f32;
    var phi_726_: f32;
    var phi_733_: f32;
    var phi_740_: f32;
    var phi_747_: f32;
    var phi_754_: f32;
    var phi_761_: f32;
    var phi_768_: f32;
    var phi_775_: f32;
    var phi_782_: f32;
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

    let _e108 = global;
    switch bitcast<i32>(0u) {
        default: {
            let _e112 = Params.Width;
            phi_160_ = true;
            if !((_e108.x >= _e112)) {
                let _e117 = Params.Height;
                phi_160_ = (_e108.y >= _e117);
            }
            let _e120 = phi_160_;
            if _e120 {
                break;
            }
            let _e121 = bitcast<i32>(_e108.x);
            let _e123 = bitcast<i32>(_e108.y);
            let _e124 = vec2<i32>(_e121, _e123);
            let _e126 = ((_e108.y * _e112) + _e108.x);
            phi_170_ = -1f;
            phi_173_ = -1i;
            loop {
                let _e128 = phi_170_;
                let _e130 = phi_173_;
                local_3 = _e128;
                if (_e130 <= 1i) {
                    phi_171_ = _e128;
                    phi_181_ = -1i;
                    loop {
                        let _e133 = phi_171_;
                        let _e135 = phi_181_;
                        local_38 = _e133;
                        if (_e135 <= 1i) {
                            let _e138 = (_e124 + vec2<i32>(_e135, _e130));
                            switch bitcast<i32>(0u) {
                                default: {
                                    phi_197_ = true;
                                    if !((_e138.x < 0i)) {
                                        phi_197_ = (_e138.y < 0i);
                                    }
                                    let _e146 = phi_197_;
                                    phi_203_ = true;
                                    if !(_e146) {
                                        phi_203_ = (_e138.x >= bitcast<i32>(_e112));
                                    }
                                    let _e151 = phi_203_;
                                    phi_212_ = true;
                                    if !(_e151) {
                                        let _e155 = Params.Height;
                                        phi_212_ = (_e138.y >= bitcast<i32>(_e155));
                                    }
                                    let _e159 = phi_212_;
                                    if _e159 {
                                        phi_224_ = 0f;
                                        break;
                                    }
                                    let _e167 = SourceState.member[((bitcast<u32>(_e138.y) * _e112) + bitcast<u32>(_e138.x))];
                                    phi_224_ = _e167.c[3];
                                    break;
                                }
                            }
                            let _e171 = phi_224_;
                            local_39 = max(_e133, _e171);
                            continue;
                        } else {
                            break;
                        }
                        continuing {
                            let _e1083 = local_39;
                            phi_171_ = _e1083;
                            phi_181_ = (_e135 + 1i);
                        }
                    }
                    continue;
                } else {
                    break;
                }
                continuing {
                    let _e1079 = local_38;
                    phi_170_ = _e1079;
                    phi_173_ = (_e130 + 1i);
                }
            }
            let _e176 = local_3;
            if !((_e176 > 0.1f)) {
                DestState.member[_e126] = CellState(array<f32, 16>(0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f));
                break;
            }
            sobel_x = array<array<f32, 3>, 3>(array<f32, 3>(-0.125f, 0f, 0.125f), array<f32, 3>(-0.25f, 0f, 0.25f), array<f32, 3>(-0.125f, 0f, 0.125f));
            sobel_y = array<array<f32, 3>, 3>(array<f32, 3>(-0.125f, -0.25f, -0.125f), array<f32, 3>(0f, 0f, 0f), array<f32, 3>(0.125f, 0.25f, 0.125f));
            switch bitcast<i32>(0u) {
                default: {
                    phi_237_ = true;
                    if !((_e121 < 0i)) {
                        phi_237_ = (_e123 < 0i);
                    }
                    let _e186 = phi_237_;
                    phi_243_ = true;
                    if !(_e186) {
                        phi_243_ = (_e121 >= bitcast<i32>(_e112));
                    }
                    let _e191 = phi_243_;
                    phi_251_ = true;
                    if !(_e191) {
                        let _e194 = Params.Height;
                        phi_251_ = (_e123 >= bitcast<i32>(_e194));
                    }
                    let _e198 = phi_251_;
                    if _e198 {
                        phi_277_ = 0f;
                        phi_278_ = 0f;
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
                        break;
                    }
                    let _e205 = SourceState.member[((bitcast<u32>(_e123) * _e112) + bitcast<u32>(_e121))];
                    phi_277_ = _e205.c[0];
                    phi_278_ = _e205.c[1];
                    phi_279_ = _e205.c[2];
                    phi_280_ = _e205.c[3];
                    phi_281_ = _e205.c[4];
                    phi_282_ = _e205.c[5];
                    phi_283_ = _e205.c[6];
                    phi_284_ = _e205.c[7];
                    phi_285_ = _e205.c[8];
                    phi_286_ = _e205.c[9];
                    phi_287_ = _e205.c[10];
                    phi_288_ = _e205.c[11];
                    phi_289_ = _e205.c[12];
                    phi_290_ = _e205.c[13];
                    phi_291_ = _e205.c[14];
                    phi_292_ = _e205.c[15];
                    break;
                }
            }
            let _e224 = phi_277_;
            let _e226 = phi_278_;
            let _e228 = phi_279_;
            let _e230 = phi_280_;
            let _e232 = phi_281_;
            let _e234 = phi_282_;
            let _e236 = phi_283_;
            let _e238 = phi_284_;
            let _e240 = phi_285_;
            let _e242 = phi_286_;
            let _e244 = phi_287_;
            let _e246 = phi_288_;
            let _e248 = phi_289_;
            let _e250 = phi_290_;
            let _e252 = phi_291_;
            let _e254 = phi_292_;
            phi_294_ = 0f;
            phi_297_ = 0f;
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
            phi_359_ = -1i;
            loop {
                let _e256 = phi_294_;
                let _e258 = phi_297_;
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
                local_4 = _e318;
                local_5 = _e316;
                local_6 = _e314;
                local_7 = _e312;
                local_8 = _e310;
                local_9 = _e308;
                local_10 = _e306;
                local_11 = _e304;
                local_12 = _e302;
                local_13 = _e300;
                local_14 = _e298;
                local_15 = _e296;
                local_16 = _e294;
                local_17 = _e292;
                local_18 = _e290;
                local_19 = _e288;
                local_20 = _e286;
                local_21 = _e284;
                local_22 = _e282;
                local_23 = _e280;
                local_24 = _e278;
                local_25 = _e276;
                local_26 = _e274;
                local_27 = _e272;
                local_28 = _e270;
                local_29 = _e268;
                local_30 = _e266;
                local_31 = _e264;
                local_32 = _e262;
                local_33 = _e260;
                local_34 = _e258;
                local_35 = _e256;
                if (_e320 <= 1i) {
                    phi_295_ = _e256;
                    phi_298_ = _e258;
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
                    phi_398_ = -1i;
                    loop {
                        let _e323 = phi_295_;
                        let _e325 = phi_298_;
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
                        let _e387 = phi_398_;
                        local_40 = _e323;
                        local_41 = _e325;
                        local_42 = _e327;
                        local_43 = _e329;
                        local_44 = _e331;
                        local_45 = _e333;
                        local_46 = _e335;
                        local_47 = _e337;
                        local_48 = _e339;
                        local_49 = _e341;
                        local_50 = _e343;
                        local_51 = _e345;
                        local_52 = _e347;
                        local_53 = _e349;
                        local_54 = _e351;
                        local_55 = _e353;
                        local_56 = _e355;
                        local_57 = _e357;
                        local_58 = _e359;
                        local_59 = _e361;
                        local_60 = _e363;
                        local_61 = _e365;
                        local_62 = _e367;
                        local_63 = _e369;
                        local_64 = _e371;
                        local_65 = _e373;
                        local_66 = _e375;
                        local_67 = _e377;
                        local_68 = _e379;
                        local_69 = _e381;
                        local_70 = _e383;
                        local_71 = _e385;
                        if (_e387 <= 1i) {
                            let _e389 = (_e320 + 1i);
                            let _e390 = (_e387 + 1i);
                            let _e393 = sobel_x[_e389][_e390];
                            let _e396 = sobel_y[_e389][_e390];
                            local_72 = _e390;
                            phi_412_ = false;
                            if (_e393 == 0f) {
                                phi_412_ = (_e396 == 0f);
                            }
                            let _e400 = phi_412_;
                            if _e400 {
                                phi_365_ = _e323;
                                phi_367_ = _e325;
                                phi_368_ = _e327;
                                phi_369_ = _e329;
                                phi_370_ = _e331;
                                phi_371_ = _e333;
                                phi_372_ = _e335;
                                phi_373_ = _e337;
                                phi_374_ = _e339;
                                phi_375_ = _e341;
                                phi_376_ = _e343;
                                phi_377_ = _e345;
                                phi_378_ = _e347;
                                phi_379_ = _e349;
                                phi_380_ = _e351;
                                phi_381_ = _e353;
                                phi_382_ = _e355;
                                phi_383_ = _e357;
                                phi_384_ = _e359;
                                phi_385_ = _e361;
                                phi_386_ = _e363;
                                phi_387_ = _e365;
                                phi_388_ = _e367;
                                phi_389_ = _e369;
                                phi_390_ = _e371;
                                phi_391_ = _e373;
                                phi_392_ = _e375;
                                phi_393_ = _e377;
                                phi_394_ = _e379;
                                phi_395_ = _e381;
                                phi_396_ = _e383;
                                phi_397_ = _e385;
                                continue;
                            }
                            let _e402 = (_e124 + vec2<i32>(_e387, _e320));
                            switch bitcast<i32>(0u) {
                                default: {
                                    phi_426_ = true;
                                    if !((_e402.x < 0i)) {
                                        phi_426_ = (_e402.y < 0i);
                                    }
                                    let _e410 = phi_426_;
                                    phi_432_ = true;
                                    if !(_e410) {
                                        phi_432_ = (_e402.x >= bitcast<i32>(_e112));
                                    }
                                    let _e415 = phi_432_;
                                    phi_441_ = true;
                                    if !(_e415) {
                                        let _e419 = Params.Height;
                                        phi_441_ = (_e402.y >= bitcast<i32>(_e419));
                                    }
                                    let _e423 = phi_441_;
                                    if _e423 {
                                        phi_468_ = 0f;
                                        phi_469_ = 0f;
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
                                        break;
                                    }
                                    let _e431 = SourceState.member[((bitcast<u32>(_e402.y) * _e112) + bitcast<u32>(_e402.x))];
                                    phi_468_ = _e431.c[0];
                                    phi_469_ = _e431.c[1];
                                    phi_470_ = _e431.c[2];
                                    phi_471_ = _e431.c[3];
                                    phi_472_ = _e431.c[4];
                                    phi_473_ = _e431.c[5];
                                    phi_474_ = _e431.c[6];
                                    phi_475_ = _e431.c[7];
                                    phi_476_ = _e431.c[8];
                                    phi_477_ = _e431.c[9];
                                    phi_478_ = _e431.c[10];
                                    phi_479_ = _e431.c[11];
                                    phi_480_ = _e431.c[12];
                                    phi_481_ = _e431.c[13];
                                    phi_482_ = _e431.c[14];
                                    phi_483_ = _e431.c[15];
                                    break;
                                }
                            }
                            let _e450 = phi_468_;
                            let _e452 = phi_469_;
                            let _e454 = phi_470_;
                            let _e456 = phi_471_;
                            let _e458 = phi_472_;
                            let _e460 = phi_473_;
                            let _e462 = phi_474_;
                            let _e464 = phi_475_;
                            let _e466 = phi_476_;
                            let _e468 = phi_477_;
                            let _e470 = phi_478_;
                            let _e472 = phi_479_;
                            let _e474 = phi_480_;
                            let _e476 = phi_481_;
                            let _e478 = phi_482_;
                            let _e480 = phi_483_;
                            phi_365_ = (_e323 + (_e396 * _e480));
                            phi_367_ = (_e325 + (_e393 * _e480));
                            phi_368_ = (_e327 + (_e396 * _e478));
                            phi_369_ = (_e329 + (_e393 * _e478));
                            phi_370_ = (_e331 + (_e396 * _e476));
                            phi_371_ = (_e333 + (_e393 * _e476));
                            phi_372_ = (_e335 + (_e396 * _e474));
                            phi_373_ = (_e337 + (_e393 * _e474));
                            phi_374_ = (_e339 + (_e396 * _e472));
                            phi_375_ = (_e341 + (_e393 * _e472));
                            phi_376_ = (_e343 + (_e396 * _e470));
                            phi_377_ = (_e345 + (_e393 * _e470));
                            phi_378_ = (_e347 + (_e396 * _e468));
                            phi_379_ = (_e349 + (_e393 * _e468));
                            phi_380_ = (_e351 + (_e396 * _e466));
                            phi_381_ = (_e353 + (_e393 * _e466));
                            phi_382_ = (_e355 + (_e396 * _e464));
                            phi_383_ = (_e357 + (_e393 * _e464));
                            phi_384_ = (_e359 + (_e396 * _e462));
                            phi_385_ = (_e361 + (_e393 * _e462));
                            phi_386_ = (_e363 + (_e396 * _e460));
                            phi_387_ = (_e365 + (_e393 * _e460));
                            phi_388_ = (_e367 + (_e396 * _e458));
                            phi_389_ = (_e369 + (_e393 * _e458));
                            phi_390_ = (_e371 + (_e396 * _e456));
                            phi_391_ = (_e373 + (_e393 * _e456));
                            phi_392_ = (_e375 + (_e396 * _e454));
                            phi_393_ = (_e377 + (_e393 * _e454));
                            phi_394_ = (_e379 + (_e396 * _e452));
                            phi_395_ = (_e381 + (_e393 * _e452));
                            phi_396_ = (_e383 + (_e396 * _e450));
                            phi_397_ = (_e385 + (_e393 * _e450));
                            continue;
                        } else {
                            break;
                        }
                        continuing {
                            let _e546 = phi_365_;
                            let _e548 = phi_367_;
                            let _e550 = phi_368_;
                            let _e552 = phi_369_;
                            let _e554 = phi_370_;
                            let _e556 = phi_371_;
                            let _e558 = phi_372_;
                            let _e560 = phi_373_;
                            let _e562 = phi_374_;
                            let _e564 = phi_375_;
                            let _e566 = phi_376_;
                            let _e568 = phi_377_;
                            let _e570 = phi_378_;
                            let _e572 = phi_379_;
                            let _e574 = phi_380_;
                            let _e576 = phi_381_;
                            let _e578 = phi_382_;
                            let _e580 = phi_383_;
                            let _e582 = phi_384_;
                            let _e584 = phi_385_;
                            let _e586 = phi_386_;
                            let _e588 = phi_387_;
                            let _e590 = phi_388_;
                            let _e592 = phi_389_;
                            let _e594 = phi_390_;
                            let _e596 = phi_391_;
                            let _e598 = phi_392_;
                            let _e600 = phi_393_;
                            let _e602 = phi_394_;
                            let _e604 = phi_395_;
                            let _e606 = phi_396_;
                            let _e608 = phi_397_;
                            phi_295_ = _e546;
                            phi_298_ = _e548;
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
                            let _e1240 = local_72;
                            phi_398_ = _e1240;
                        }
                    }
                    continue;
                } else {
                    break;
                }
                continuing {
                    let _e1111 = local_40;
                    phi_294_ = _e1111;
                    let _e1114 = local_41;
                    phi_297_ = _e1114;
                    let _e1117 = local_42;
                    phi_299_ = _e1117;
                    let _e1120 = local_43;
                    phi_301_ = _e1120;
                    let _e1123 = local_44;
                    phi_303_ = _e1123;
                    let _e1126 = local_45;
                    phi_305_ = _e1126;
                    let _e1129 = local_46;
                    phi_307_ = _e1129;
                    let _e1132 = local_47;
                    phi_309_ = _e1132;
                    let _e1135 = local_48;
                    phi_311_ = _e1135;
                    let _e1138 = local_49;
                    phi_313_ = _e1138;
                    let _e1141 = local_50;
                    phi_315_ = _e1141;
                    let _e1144 = local_51;
                    phi_317_ = _e1144;
                    let _e1147 = local_52;
                    phi_319_ = _e1147;
                    let _e1150 = local_53;
                    phi_321_ = _e1150;
                    let _e1153 = local_54;
                    phi_323_ = _e1153;
                    let _e1156 = local_55;
                    phi_325_ = _e1156;
                    let _e1159 = local_56;
                    phi_327_ = _e1159;
                    let _e1162 = local_57;
                    phi_329_ = _e1162;
                    let _e1165 = local_58;
                    phi_331_ = _e1165;
                    let _e1168 = local_59;
                    phi_333_ = _e1168;
                    let _e1171 = local_60;
                    phi_335_ = _e1171;
                    let _e1174 = local_61;
                    phi_337_ = _e1174;
                    let _e1177 = local_62;
                    phi_339_ = _e1177;
                    let _e1180 = local_63;
                    phi_341_ = _e1180;
                    let _e1183 = local_64;
                    phi_343_ = _e1183;
                    let _e1186 = local_65;
                    phi_345_ = _e1186;
                    let _e1189 = local_66;
                    phi_347_ = _e1189;
                    let _e1192 = local_67;
                    phi_349_ = _e1192;
                    let _e1195 = local_68;
                    phi_351_ = _e1195;
                    let _e1198 = local_69;
                    phi_353_ = _e1198;
                    let _e1201 = local_70;
                    phi_355_ = _e1201;
                    let _e1204 = local_71;
                    phi_357_ = _e1204;
                    phi_359_ = (_e320 + 1i);
                }
            }
            local[0i] = _e224;
            let _e613 = local_4;
            local[1i] = _e613;
            let _e616 = local_5;
            local[2i] = _e616;
            local[3i] = _e226;
            let _e620 = local_6;
            local[4i] = _e620;
            let _e623 = local_7;
            local[5i] = _e623;
            local[6i] = _e228;
            let _e627 = local_8;
            local[7i] = _e627;
            let _e630 = local_9;
            local[8i] = _e630;
            local[9i] = _e230;
            let _e634 = local_10;
            local[10i] = _e634;
            let _e637 = local_11;
            local[11i] = _e637;
            local[12i] = _e232;
            let _e641 = local_12;
            local[13i] = _e641;
            let _e644 = local_13;
            local[14i] = _e644;
            local[15i] = _e234;
            let _e648 = local_14;
            local[16i] = _e648;
            let _e651 = local_15;
            local[17i] = _e651;
            local[18i] = _e236;
            let _e655 = local_16;
            local[19i] = _e655;
            let _e658 = local_17;
            local[20i] = _e658;
            local[21i] = _e238;
            let _e662 = local_18;
            local[22i] = _e662;
            let _e665 = local_19;
            local[23i] = _e665;
            local[24i] = _e240;
            let _e669 = local_20;
            local[25i] = _e669;
            let _e672 = local_21;
            local[26i] = _e672;
            local[27i] = _e242;
            let _e676 = local_22;
            local[28i] = _e676;
            let _e679 = local_23;
            local[29i] = _e679;
            local[30i] = _e244;
            let _e683 = local_24;
            local[31i] = _e683;
            let _e686 = local_25;
            local[32i] = _e686;
            local[33i] = _e246;
            let _e690 = local_26;
            local[34i] = _e690;
            let _e693 = local_27;
            local[35i] = _e693;
            local[36i] = _e248;
            let _e697 = local_28;
            local[37i] = _e697;
            let _e700 = local_29;
            local[38i] = _e700;
            local[39i] = _e250;
            let _e704 = local_30;
            local[40i] = _e704;
            let _e707 = local_31;
            local[41i] = _e707;
            local[42i] = _e252;
            let _e711 = local_32;
            local[43i] = _e711;
            let _e714 = local_33;
            local[44i] = _e714;
            local[45i] = _e254;
            let _e718 = local_34;
            local[46i] = _e718;
            let _e721 = local_35;
            local[47i] = _e721;
            phi_597_ = 0i;
            loop {
                let _e723 = phi_597_;
                if (_e723 < 128i) {
                    let _e729 = Weights.member[bitcast<u32>((6144i + _e723))];
                    phi_608_ = _e729;
                    phi_611_ = 0i;
                    loop {
                        let _e731 = phi_608_;
                        let _e733 = phi_611_;
                        local_36 = _e731;
                        if (_e733 < 48i) {
                            continue;
                        } else {
                            break;
                        }
                        continuing {
                            let _e736 = local[_e733];
                            let _e742 = Weights.member[bitcast<u32>(((_e733 * 128i) + _e723))];
                            phi_608_ = (_e731 + (_e736 * _e742));
                            phi_611_ = (_e733 + 1i);
                        }
                    }
                    let _e747 = local_36;
                    local_1[_e723] = max(_e747, 0f);
                    continue;
                } else {
                    break;
                }
                continuing {
                    phi_597_ = (_e723 + 1i);
                }
            }
            phi_626_ = 0i;
            loop {
                let _e752 = phi_626_;
                if (_e752 < 16i) {
                    let _e758 = Weights.member[bitcast<u32>((8320i + _e752))];
                    phi_637_ = _e758;
                    phi_640_ = 0i;
                    loop {
                        let _e760 = phi_637_;
                        let _e762 = phi_640_;
                        local_37 = _e760;
                        if (_e762 < 128i) {
                            continue;
                        } else {
                            break;
                        }
                        continuing {
                            let _e765 = local_1[_e762];
                            let _e772 = Weights.member[bitcast<u32>(((6272i + (_e762 * 16i)) + _e752))];
                            phi_637_ = (_e760 + (_e765 * _e772));
                            phi_640_ = (_e762 + 1i);
                        }
                    }
                    let _e778 = local_37;
                    local_2[_e752] = _e778;
                    continue;
                } else {
                    break;
                }
                continuing {
                    phi_626_ = (_e752 + 1i);
                }
            }
            let _e781 = Params.Seed;
            let _e784 = ((_e108.x * 374761393u) + (_e108.y * 668265263u));
            let _e786 = (_e784 + (_e781 * 2654435761u));
            let _e790 = ((_e786 ^ (_e786 >> bitcast<u32>(13u))) * 1274126177u);
            let _e798 = Params.FireRate;
            let _e799 = ((f32(((_e790 ^ (_e790 >> bitcast<u32>(16u))) & 16777215u)) * 0.000000059604645f) <= _e798);
            if _e799 {
                let _e801 = local_2[0i];
                phi_677_ = _e801;
            } else {
                phi_677_ = 0f;
            }
            let _e803 = phi_677_;
            if _e799 {
                let _e806 = local_2[1i];
                phi_684_ = _e806;
            } else {
                phi_684_ = 0f;
            }
            let _e808 = phi_684_;
            if _e799 {
                let _e811 = local_2[2i];
                phi_691_ = _e811;
            } else {
                phi_691_ = 0f;
            }
            let _e813 = phi_691_;
            if _e799 {
                let _e816 = local_2[3i];
                phi_698_ = _e816;
            } else {
                phi_698_ = 0f;
            }
            let _e818 = phi_698_;
            if _e799 {
                let _e821 = local_2[4i];
                phi_705_ = _e821;
            } else {
                phi_705_ = 0f;
            }
            let _e823 = phi_705_;
            if _e799 {
                let _e826 = local_2[5i];
                phi_712_ = _e826;
            } else {
                phi_712_ = 0f;
            }
            let _e828 = phi_712_;
            if _e799 {
                let _e831 = local_2[6i];
                phi_719_ = _e831;
            } else {
                phi_719_ = 0f;
            }
            let _e833 = phi_719_;
            if _e799 {
                let _e836 = local_2[7i];
                phi_726_ = _e836;
            } else {
                phi_726_ = 0f;
            }
            let _e838 = phi_726_;
            if _e799 {
                let _e841 = local_2[8i];
                phi_733_ = _e841;
            } else {
                phi_733_ = 0f;
            }
            let _e843 = phi_733_;
            if _e799 {
                let _e846 = local_2[9i];
                phi_740_ = _e846;
            } else {
                phi_740_ = 0f;
            }
            let _e848 = phi_740_;
            if _e799 {
                let _e851 = local_2[10i];
                phi_747_ = _e851;
            } else {
                phi_747_ = 0f;
            }
            let _e853 = phi_747_;
            if _e799 {
                let _e856 = local_2[11i];
                phi_754_ = _e856;
            } else {
                phi_754_ = 0f;
            }
            let _e858 = phi_754_;
            if _e799 {
                let _e861 = local_2[12i];
                phi_761_ = _e861;
            } else {
                phi_761_ = 0f;
            }
            let _e863 = phi_761_;
            if _e799 {
                let _e866 = local_2[13i];
                phi_768_ = _e866;
            } else {
                phi_768_ = 0f;
            }
            let _e868 = phi_768_;
            if _e799 {
                let _e871 = local_2[14i];
                phi_775_ = _e871;
            } else {
                phi_775_ = 0f;
            }
            let _e873 = phi_775_;
            if _e799 {
                let _e876 = local_2[15i];
                phi_782_ = _e876;
            } else {
                phi_782_ = 0f;
            }
            let _e878 = phi_782_;
            let _e882 = (_e784 + ((_e781 + 1388u) * 2654435761u));
            let _e886 = ((_e882 ^ (_e882 >> bitcast<u32>(13u))) * 1274126177u);
            let _e898 = (_e784 + ((_e781 + 1485u) * 2654435761u));
            let _e902 = ((_e898 ^ (_e898 >> bitcast<u32>(13u))) * 1274126177u);
            let _e914 = (_e784 + ((_e781 + 1582u) * 2654435761u));
            let _e918 = ((_e914 ^ (_e914 >> bitcast<u32>(13u))) * 1274126177u);
            let _e930 = (_e784 + ((_e781 + 1679u) * 2654435761u));
            let _e934 = ((_e930 ^ (_e930 >> bitcast<u32>(13u))) * 1274126177u);
            let _e946 = (_e784 + ((_e781 + 1776u) * 2654435761u));
            let _e950 = ((_e946 ^ (_e946 >> bitcast<u32>(13u))) * 1274126177u);
            let _e962 = (_e784 + ((_e781 + 1873u) * 2654435761u));
            let _e966 = ((_e962 ^ (_e962 >> bitcast<u32>(13u))) * 1274126177u);
            let _e978 = (_e784 + ((_e781 + 1970u) * 2654435761u));
            let _e982 = ((_e978 ^ (_e978 >> bitcast<u32>(13u))) * 1274126177u);
            let _e994 = (_e784 + ((_e781 + 2067u) * 2654435761u));
            let _e998 = ((_e994 ^ (_e994 >> bitcast<u32>(13u))) * 1274126177u);
            let _e1010 = (_e784 + ((_e781 + 2164u) * 2654435761u));
            let _e1014 = ((_e1010 ^ (_e1010 >> bitcast<u32>(13u))) * 1274126177u);
            let _e1026 = (_e784 + ((_e781 + 2261u) * 2654435761u));
            let _e1030 = ((_e1026 ^ (_e1026 >> bitcast<u32>(13u))) * 1274126177u);
            let _e1042 = (_e784 + ((_e781 + 2358u) * 2654435761u));
            let _e1046 = ((_e1042 ^ (_e1042 >> bitcast<u32>(13u))) * 1274126177u);
            let _e1058 = (_e784 + ((_e781 + 2455u) * 2654435761u));
            let _e1062 = ((_e1058 ^ (_e1058 >> bitcast<u32>(13u))) * 1274126177u);
            DestState.member[_e126] = CellState(array<f32, 16>((_e224 + _e803), (_e226 + _e808), (_e228 + _e813), (_e230 + _e818), ((_e232 + _e823) + (((f32(((_e886 ^ (_e886 >> bitcast<u32>(16u))) & 16777215u)) * 0.000000059604645f) - 0.5f) * 0.01f)), ((_e234 + _e828) + (((f32(((_e902 ^ (_e902 >> bitcast<u32>(16u))) & 16777215u)) * 0.000000059604645f) - 0.5f) * 0.01f)), ((_e236 + _e833) + (((f32(((_e918 ^ (_e918 >> bitcast<u32>(16u))) & 16777215u)) * 0.000000059604645f) - 0.5f) * 0.01f)), ((_e238 + _e838) + (((f32(((_e934 ^ (_e934 >> bitcast<u32>(16u))) & 16777215u)) * 0.000000059604645f) - 0.5f) * 0.01f)), ((_e240 + _e843) + (((f32(((_e950 ^ (_e950 >> bitcast<u32>(16u))) & 16777215u)) * 0.000000059604645f) - 0.5f) * 0.01f)), ((_e242 + _e848) + (((f32(((_e966 ^ (_e966 >> bitcast<u32>(16u))) & 16777215u)) * 0.000000059604645f) - 0.5f) * 0.01f)), ((_e244 + _e853) + (((f32(((_e982 ^ (_e982 >> bitcast<u32>(16u))) & 16777215u)) * 0.000000059604645f) - 0.5f) * 0.01f)), ((_e246 + _e858) + (((f32(((_e998 ^ (_e998 >> bitcast<u32>(16u))) & 16777215u)) * 0.000000059604645f) - 0.5f) * 0.01f)), ((_e248 + _e863) + (((f32(((_e1014 ^ (_e1014 >> bitcast<u32>(16u))) & 16777215u)) * 0.000000059604645f) - 0.5f) * 0.01f)), ((_e250 + _e868) + (((f32(((_e1030 ^ (_e1030 >> bitcast<u32>(16u))) & 16777215u)) * 0.000000059604645f) - 0.5f) * 0.01f)), ((_e252 + _e873) + (((f32(((_e1046 ^ (_e1046 >> bitcast<u32>(16u))) & 16777215u)) * 0.000000059604645f) - 0.5f) * 0.01f)), ((_e254 + _e878) + (((f32(((_e1062 ^ (_e1062 >> bitcast<u32>(16u))) & 16777215u)) * 0.000000059604645f) - 0.5f) * 0.01f))));
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
