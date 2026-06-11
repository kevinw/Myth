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
    OriginX: u32,
    OriginY: u32,
}

@group(0) @binding(0) 
var<storage> SourceState: type_u002e_StructuredBuffer_u002e_CellState;
@group(0) @binding(1) 
var<storage> Weights: type_u002e_StructuredBuffer_u002e_float;
@group(0) @binding(2) 
var<storage> Mask: type_u002e_StructuredBuffer_u002e_float;
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
    var phi_172_: bool;
    var phi_186_: f32;
    var phi_189_: i32;
    var phi_187_: f32;
    var phi_197_: i32;
    var phi_213_: bool;
    var phi_218_: bool;
    var phi_227_: bool;
    var phi_239_: f32;
    var local_3: f32;
    var phi_242_: bool;
    var phi_253_: bool;
    var phi_263_: bool;
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
    var phi_295_: f32;
    var phi_296_: f32;
    var phi_297_: f32;
    var phi_298_: f32;
    var phi_299_: f32;
    var phi_300_: f32;
    var phi_302_: f32;
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
    var phi_361_: f32;
    var phi_363_: f32;
    var phi_365_: f32;
    var phi_367_: i32;
    var phi_303_: f32;
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
    var phi_362_: f32;
    var phi_364_: f32;
    var phi_366_: f32;
    var phi_406_: i32;
    var phi_420_: bool;
    var phi_434_: bool;
    var phi_439_: bool;
    var phi_448_: bool;
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
    var phi_486_: f32;
    var phi_487_: f32;
    var phi_488_: f32;
    var phi_489_: f32;
    var phi_490_: f32;
    var phi_373_: f32;
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
    var phi_400_: f32;
    var phi_401_: f32;
    var phi_402_: f32;
    var phi_403_: f32;
    var phi_404_: f32;
    var phi_405_: f32;
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
    var phi_604_: i32;
    var phi_615_: f32;
    var phi_618_: i32;
    var local_36: f32;
    var phi_633_: i32;
    var phi_644_: f32;
    var phi_647_: i32;
    var local_37: f32;
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
    var phi_789_: f32;
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

    let _e109 = global;
    switch bitcast<i32>(0u) {
        default: {
            let _e112 = Params.OriginX;
            let _e115 = bitcast<i32>((_e112 + _e109.x));
            let _e117 = Params.OriginY;
            let _e120 = bitcast<i32>((_e117 + _e109.y));
            let _e121 = vec2<i32>(_e115, _e120);
            let _e123 = Params.Width;
            let _e124 = bitcast<i32>(_e123);
            let _e125 = (_e115 >= _e124);
            phi_172_ = true;
            if !(_e125) {
                let _e128 = Params.Height;
                phi_172_ = (_e120 >= bitcast<i32>(_e128));
            }
            let _e132 = phi_172_;
            if _e132 {
                break;
            }
            let _e133 = bitcast<u32>(_e120);
            let _e135 = bitcast<u32>(_e115);
            let _e136 = ((_e133 * _e123) + _e135);
            let _e139 = Mask.member[_e136];
            phi_242_ = true;
            if !((_e139 < 0.5f)) {
                phi_186_ = -1f;
                phi_189_ = -1i;
                loop {
                    let _e143 = phi_186_;
                    let _e145 = phi_189_;
                    local_3 = _e143;
                    if (_e145 <= 1i) {
                        phi_187_ = _e143;
                        phi_197_ = -1i;
                        loop {
                            let _e148 = phi_187_;
                            let _e150 = phi_197_;
                            local_38 = _e148;
                            if (_e150 <= 1i) {
                                let _e153 = (_e121 + vec2<i32>(_e150, _e145));
                                switch bitcast<i32>(0u) {
                                    default: {
                                        phi_213_ = true;
                                        if !((_e153.x < 0i)) {
                                            phi_213_ = (_e153.y < 0i);
                                        }
                                        let _e161 = phi_213_;
                                        phi_218_ = true;
                                        if !(_e161) {
                                            phi_218_ = (_e153.x >= _e124);
                                        }
                                        let _e165 = phi_218_;
                                        phi_227_ = true;
                                        if !(_e165) {
                                            let _e169 = Params.Height;
                                            phi_227_ = (_e153.y >= bitcast<i32>(_e169));
                                        }
                                        let _e173 = phi_227_;
                                        if _e173 {
                                            phi_239_ = 0f;
                                            break;
                                        }
                                        let _e181 = SourceState.member[((bitcast<u32>(_e153.y) * _e123) + bitcast<u32>(_e153.x))];
                                        phi_239_ = _e181.c[3];
                                        break;
                                    }
                                }
                                let _e185 = phi_239_;
                                local_39 = max(_e148, _e185);
                                continue;
                            } else {
                                break;
                            }
                            continuing {
                                let _e1091 = local_39;
                                phi_187_ = _e1091;
                                phi_197_ = (_e150 + 1i);
                            }
                        }
                        continue;
                    } else {
                        break;
                    }
                    continuing {
                        let _e1087 = local_38;
                        phi_186_ = _e1087;
                        phi_189_ = (_e145 + 1i);
                    }
                }
                let _e190 = local_3;
                phi_242_ = !((_e190 > 0.1f));
            }
            let _e194 = phi_242_;
            if _e194 {
                DestState.member[_e136] = CellState(array<f32, 16>(0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f));
                break;
            }
            sobel_x = array<array<f32, 3>, 3>(array<f32, 3>(-0.125f, 0f, 0.125f), array<f32, 3>(-0.25f, 0f, 0.25f), array<f32, 3>(-0.125f, 0f, 0.125f));
            sobel_y = array<array<f32, 3>, 3>(array<f32, 3>(-0.125f, -0.25f, -0.125f), array<f32, 3>(0f, 0f, 0f), array<f32, 3>(0.125f, 0.25f, 0.125f));
            switch bitcast<i32>(0u) {
                default: {
                    phi_253_ = true;
                    if !((_e115 < 0i)) {
                        phi_253_ = (_e120 < 0i);
                    }
                    let _e202 = phi_253_;
                    phi_263_ = true;
                    if !(select(true, _e125, !(_e202))) {
                        let _e207 = Params.Height;
                        phi_263_ = (_e120 >= bitcast<i32>(_e207));
                    }
                    let _e211 = phi_263_;
                    if _e211 {
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
                        phi_295_ = 0f;
                        phi_296_ = 0f;
                        phi_297_ = 0f;
                        phi_298_ = 0f;
                        phi_299_ = 0f;
                        phi_300_ = 0f;
                        break;
                    }
                    let _e214 = SourceState.member[_e136];
                    phi_285_ = _e214.c[0];
                    phi_286_ = _e214.c[1];
                    phi_287_ = _e214.c[2];
                    phi_288_ = _e214.c[3];
                    phi_289_ = _e214.c[4];
                    phi_290_ = _e214.c[5];
                    phi_291_ = _e214.c[6];
                    phi_292_ = _e214.c[7];
                    phi_293_ = _e214.c[8];
                    phi_294_ = _e214.c[9];
                    phi_295_ = _e214.c[10];
                    phi_296_ = _e214.c[11];
                    phi_297_ = _e214.c[12];
                    phi_298_ = _e214.c[13];
                    phi_299_ = _e214.c[14];
                    phi_300_ = _e214.c[15];
                    break;
                }
            }
            let _e233 = phi_285_;
            let _e235 = phi_286_;
            let _e237 = phi_287_;
            let _e239 = phi_288_;
            let _e241 = phi_289_;
            let _e243 = phi_290_;
            let _e245 = phi_291_;
            let _e247 = phi_292_;
            let _e249 = phi_293_;
            let _e251 = phi_294_;
            let _e253 = phi_295_;
            let _e255 = phi_296_;
            let _e257 = phi_297_;
            let _e259 = phi_298_;
            let _e261 = phi_299_;
            let _e263 = phi_300_;
            phi_302_ = 0f;
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
            phi_361_ = 0f;
            phi_363_ = 0f;
            phi_365_ = 0f;
            phi_367_ = -1i;
            loop {
                let _e265 = phi_302_;
                let _e267 = phi_305_;
                let _e269 = phi_307_;
                let _e271 = phi_309_;
                let _e273 = phi_311_;
                let _e275 = phi_313_;
                let _e277 = phi_315_;
                let _e279 = phi_317_;
                let _e281 = phi_319_;
                let _e283 = phi_321_;
                let _e285 = phi_323_;
                let _e287 = phi_325_;
                let _e289 = phi_327_;
                let _e291 = phi_329_;
                let _e293 = phi_331_;
                let _e295 = phi_333_;
                let _e297 = phi_335_;
                let _e299 = phi_337_;
                let _e301 = phi_339_;
                let _e303 = phi_341_;
                let _e305 = phi_343_;
                let _e307 = phi_345_;
                let _e309 = phi_347_;
                let _e311 = phi_349_;
                let _e313 = phi_351_;
                let _e315 = phi_353_;
                let _e317 = phi_355_;
                let _e319 = phi_357_;
                let _e321 = phi_359_;
                let _e323 = phi_361_;
                let _e325 = phi_363_;
                let _e327 = phi_365_;
                let _e329 = phi_367_;
                local_4 = _e327;
                local_5 = _e325;
                local_6 = _e323;
                local_7 = _e321;
                local_8 = _e319;
                local_9 = _e317;
                local_10 = _e315;
                local_11 = _e313;
                local_12 = _e311;
                local_13 = _e309;
                local_14 = _e307;
                local_15 = _e305;
                local_16 = _e303;
                local_17 = _e301;
                local_18 = _e299;
                local_19 = _e297;
                local_20 = _e295;
                local_21 = _e293;
                local_22 = _e291;
                local_23 = _e289;
                local_24 = _e287;
                local_25 = _e285;
                local_26 = _e283;
                local_27 = _e281;
                local_28 = _e279;
                local_29 = _e277;
                local_30 = _e275;
                local_31 = _e273;
                local_32 = _e271;
                local_33 = _e269;
                local_34 = _e267;
                local_35 = _e265;
                if (_e329 <= 1i) {
                    phi_303_ = _e265;
                    phi_306_ = _e267;
                    phi_308_ = _e269;
                    phi_310_ = _e271;
                    phi_312_ = _e273;
                    phi_314_ = _e275;
                    phi_316_ = _e277;
                    phi_318_ = _e279;
                    phi_320_ = _e281;
                    phi_322_ = _e283;
                    phi_324_ = _e285;
                    phi_326_ = _e287;
                    phi_328_ = _e289;
                    phi_330_ = _e291;
                    phi_332_ = _e293;
                    phi_334_ = _e295;
                    phi_336_ = _e297;
                    phi_338_ = _e299;
                    phi_340_ = _e301;
                    phi_342_ = _e303;
                    phi_344_ = _e305;
                    phi_346_ = _e307;
                    phi_348_ = _e309;
                    phi_350_ = _e311;
                    phi_352_ = _e313;
                    phi_354_ = _e315;
                    phi_356_ = _e317;
                    phi_358_ = _e319;
                    phi_360_ = _e321;
                    phi_362_ = _e323;
                    phi_364_ = _e325;
                    phi_366_ = _e327;
                    phi_406_ = -1i;
                    loop {
                        let _e332 = phi_303_;
                        let _e334 = phi_306_;
                        let _e336 = phi_308_;
                        let _e338 = phi_310_;
                        let _e340 = phi_312_;
                        let _e342 = phi_314_;
                        let _e344 = phi_316_;
                        let _e346 = phi_318_;
                        let _e348 = phi_320_;
                        let _e350 = phi_322_;
                        let _e352 = phi_324_;
                        let _e354 = phi_326_;
                        let _e356 = phi_328_;
                        let _e358 = phi_330_;
                        let _e360 = phi_332_;
                        let _e362 = phi_334_;
                        let _e364 = phi_336_;
                        let _e366 = phi_338_;
                        let _e368 = phi_340_;
                        let _e370 = phi_342_;
                        let _e372 = phi_344_;
                        let _e374 = phi_346_;
                        let _e376 = phi_348_;
                        let _e378 = phi_350_;
                        let _e380 = phi_352_;
                        let _e382 = phi_354_;
                        let _e384 = phi_356_;
                        let _e386 = phi_358_;
                        let _e388 = phi_360_;
                        let _e390 = phi_362_;
                        let _e392 = phi_364_;
                        let _e394 = phi_366_;
                        let _e396 = phi_406_;
                        local_40 = _e332;
                        local_41 = _e334;
                        local_42 = _e336;
                        local_43 = _e338;
                        local_44 = _e340;
                        local_45 = _e342;
                        local_46 = _e344;
                        local_47 = _e346;
                        local_48 = _e348;
                        local_49 = _e350;
                        local_50 = _e352;
                        local_51 = _e354;
                        local_52 = _e356;
                        local_53 = _e358;
                        local_54 = _e360;
                        local_55 = _e362;
                        local_56 = _e364;
                        local_57 = _e366;
                        local_58 = _e368;
                        local_59 = _e370;
                        local_60 = _e372;
                        local_61 = _e374;
                        local_62 = _e376;
                        local_63 = _e378;
                        local_64 = _e380;
                        local_65 = _e382;
                        local_66 = _e384;
                        local_67 = _e386;
                        local_68 = _e388;
                        local_69 = _e390;
                        local_70 = _e392;
                        local_71 = _e394;
                        if (_e396 <= 1i) {
                            let _e398 = (_e329 + 1i);
                            let _e399 = (_e396 + 1i);
                            let _e402 = sobel_x[_e398][_e399];
                            let _e405 = sobel_y[_e398][_e399];
                            local_72 = _e399;
                            phi_420_ = false;
                            if (_e402 == 0f) {
                                phi_420_ = (_e405 == 0f);
                            }
                            let _e409 = phi_420_;
                            if _e409 {
                                phi_373_ = _e332;
                                phi_375_ = _e334;
                                phi_376_ = _e336;
                                phi_377_ = _e338;
                                phi_378_ = _e340;
                                phi_379_ = _e342;
                                phi_380_ = _e344;
                                phi_381_ = _e346;
                                phi_382_ = _e348;
                                phi_383_ = _e350;
                                phi_384_ = _e352;
                                phi_385_ = _e354;
                                phi_386_ = _e356;
                                phi_387_ = _e358;
                                phi_388_ = _e360;
                                phi_389_ = _e362;
                                phi_390_ = _e364;
                                phi_391_ = _e366;
                                phi_392_ = _e368;
                                phi_393_ = _e370;
                                phi_394_ = _e372;
                                phi_395_ = _e374;
                                phi_396_ = _e376;
                                phi_397_ = _e378;
                                phi_398_ = _e380;
                                phi_399_ = _e382;
                                phi_400_ = _e384;
                                phi_401_ = _e386;
                                phi_402_ = _e388;
                                phi_403_ = _e390;
                                phi_404_ = _e392;
                                phi_405_ = _e394;
                                continue;
                            }
                            let _e411 = (_e121 + vec2<i32>(_e396, _e329));
                            switch bitcast<i32>(0u) {
                                default: {
                                    phi_434_ = true;
                                    if !((_e411.x < 0i)) {
                                        phi_434_ = (_e411.y < 0i);
                                    }
                                    let _e419 = phi_434_;
                                    phi_439_ = true;
                                    if !(_e419) {
                                        phi_439_ = (_e411.x >= _e124);
                                    }
                                    let _e423 = phi_439_;
                                    phi_448_ = true;
                                    if !(_e423) {
                                        let _e427 = Params.Height;
                                        phi_448_ = (_e411.y >= bitcast<i32>(_e427));
                                    }
                                    let _e431 = phi_448_;
                                    if _e431 {
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
                                        phi_486_ = 0f;
                                        phi_487_ = 0f;
                                        phi_488_ = 0f;
                                        phi_489_ = 0f;
                                        phi_490_ = 0f;
                                        break;
                                    }
                                    let _e439 = SourceState.member[((bitcast<u32>(_e411.y) * _e123) + bitcast<u32>(_e411.x))];
                                    phi_475_ = _e439.c[0];
                                    phi_476_ = _e439.c[1];
                                    phi_477_ = _e439.c[2];
                                    phi_478_ = _e439.c[3];
                                    phi_479_ = _e439.c[4];
                                    phi_480_ = _e439.c[5];
                                    phi_481_ = _e439.c[6];
                                    phi_482_ = _e439.c[7];
                                    phi_483_ = _e439.c[8];
                                    phi_484_ = _e439.c[9];
                                    phi_485_ = _e439.c[10];
                                    phi_486_ = _e439.c[11];
                                    phi_487_ = _e439.c[12];
                                    phi_488_ = _e439.c[13];
                                    phi_489_ = _e439.c[14];
                                    phi_490_ = _e439.c[15];
                                    break;
                                }
                            }
                            let _e458 = phi_475_;
                            let _e460 = phi_476_;
                            let _e462 = phi_477_;
                            let _e464 = phi_478_;
                            let _e466 = phi_479_;
                            let _e468 = phi_480_;
                            let _e470 = phi_481_;
                            let _e472 = phi_482_;
                            let _e474 = phi_483_;
                            let _e476 = phi_484_;
                            let _e478 = phi_485_;
                            let _e480 = phi_486_;
                            let _e482 = phi_487_;
                            let _e484 = phi_488_;
                            let _e486 = phi_489_;
                            let _e488 = phi_490_;
                            phi_373_ = (_e332 + (_e405 * _e488));
                            phi_375_ = (_e334 + (_e402 * _e488));
                            phi_376_ = (_e336 + (_e405 * _e486));
                            phi_377_ = (_e338 + (_e402 * _e486));
                            phi_378_ = (_e340 + (_e405 * _e484));
                            phi_379_ = (_e342 + (_e402 * _e484));
                            phi_380_ = (_e344 + (_e405 * _e482));
                            phi_381_ = (_e346 + (_e402 * _e482));
                            phi_382_ = (_e348 + (_e405 * _e480));
                            phi_383_ = (_e350 + (_e402 * _e480));
                            phi_384_ = (_e352 + (_e405 * _e478));
                            phi_385_ = (_e354 + (_e402 * _e478));
                            phi_386_ = (_e356 + (_e405 * _e476));
                            phi_387_ = (_e358 + (_e402 * _e476));
                            phi_388_ = (_e360 + (_e405 * _e474));
                            phi_389_ = (_e362 + (_e402 * _e474));
                            phi_390_ = (_e364 + (_e405 * _e472));
                            phi_391_ = (_e366 + (_e402 * _e472));
                            phi_392_ = (_e368 + (_e405 * _e470));
                            phi_393_ = (_e370 + (_e402 * _e470));
                            phi_394_ = (_e372 + (_e405 * _e468));
                            phi_395_ = (_e374 + (_e402 * _e468));
                            phi_396_ = (_e376 + (_e405 * _e466));
                            phi_397_ = (_e378 + (_e402 * _e466));
                            phi_398_ = (_e380 + (_e405 * _e464));
                            phi_399_ = (_e382 + (_e402 * _e464));
                            phi_400_ = (_e384 + (_e405 * _e462));
                            phi_401_ = (_e386 + (_e402 * _e462));
                            phi_402_ = (_e388 + (_e405 * _e460));
                            phi_403_ = (_e390 + (_e402 * _e460));
                            phi_404_ = (_e392 + (_e405 * _e458));
                            phi_405_ = (_e394 + (_e402 * _e458));
                            continue;
                        } else {
                            break;
                        }
                        continuing {
                            let _e554 = phi_373_;
                            let _e556 = phi_375_;
                            let _e558 = phi_376_;
                            let _e560 = phi_377_;
                            let _e562 = phi_378_;
                            let _e564 = phi_379_;
                            let _e566 = phi_380_;
                            let _e568 = phi_381_;
                            let _e570 = phi_382_;
                            let _e572 = phi_383_;
                            let _e574 = phi_384_;
                            let _e576 = phi_385_;
                            let _e578 = phi_386_;
                            let _e580 = phi_387_;
                            let _e582 = phi_388_;
                            let _e584 = phi_389_;
                            let _e586 = phi_390_;
                            let _e588 = phi_391_;
                            let _e590 = phi_392_;
                            let _e592 = phi_393_;
                            let _e594 = phi_394_;
                            let _e596 = phi_395_;
                            let _e598 = phi_396_;
                            let _e600 = phi_397_;
                            let _e602 = phi_398_;
                            let _e604 = phi_399_;
                            let _e606 = phi_400_;
                            let _e608 = phi_401_;
                            let _e610 = phi_402_;
                            let _e612 = phi_403_;
                            let _e614 = phi_404_;
                            let _e616 = phi_405_;
                            phi_303_ = _e554;
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
                            phi_362_ = _e612;
                            phi_364_ = _e614;
                            phi_366_ = _e616;
                            let _e1248 = local_72;
                            phi_406_ = _e1248;
                        }
                    }
                    continue;
                } else {
                    break;
                }
                continuing {
                    let _e1119 = local_40;
                    phi_302_ = _e1119;
                    let _e1122 = local_41;
                    phi_305_ = _e1122;
                    let _e1125 = local_42;
                    phi_307_ = _e1125;
                    let _e1128 = local_43;
                    phi_309_ = _e1128;
                    let _e1131 = local_44;
                    phi_311_ = _e1131;
                    let _e1134 = local_45;
                    phi_313_ = _e1134;
                    let _e1137 = local_46;
                    phi_315_ = _e1137;
                    let _e1140 = local_47;
                    phi_317_ = _e1140;
                    let _e1143 = local_48;
                    phi_319_ = _e1143;
                    let _e1146 = local_49;
                    phi_321_ = _e1146;
                    let _e1149 = local_50;
                    phi_323_ = _e1149;
                    let _e1152 = local_51;
                    phi_325_ = _e1152;
                    let _e1155 = local_52;
                    phi_327_ = _e1155;
                    let _e1158 = local_53;
                    phi_329_ = _e1158;
                    let _e1161 = local_54;
                    phi_331_ = _e1161;
                    let _e1164 = local_55;
                    phi_333_ = _e1164;
                    let _e1167 = local_56;
                    phi_335_ = _e1167;
                    let _e1170 = local_57;
                    phi_337_ = _e1170;
                    let _e1173 = local_58;
                    phi_339_ = _e1173;
                    let _e1176 = local_59;
                    phi_341_ = _e1176;
                    let _e1179 = local_60;
                    phi_343_ = _e1179;
                    let _e1182 = local_61;
                    phi_345_ = _e1182;
                    let _e1185 = local_62;
                    phi_347_ = _e1185;
                    let _e1188 = local_63;
                    phi_349_ = _e1188;
                    let _e1191 = local_64;
                    phi_351_ = _e1191;
                    let _e1194 = local_65;
                    phi_353_ = _e1194;
                    let _e1197 = local_66;
                    phi_355_ = _e1197;
                    let _e1200 = local_67;
                    phi_357_ = _e1200;
                    let _e1203 = local_68;
                    phi_359_ = _e1203;
                    let _e1206 = local_69;
                    phi_361_ = _e1206;
                    let _e1209 = local_70;
                    phi_363_ = _e1209;
                    let _e1212 = local_71;
                    phi_365_ = _e1212;
                    phi_367_ = (_e329 + 1i);
                }
            }
            local[0i] = _e233;
            let _e621 = local_4;
            local[1i] = _e621;
            let _e624 = local_5;
            local[2i] = _e624;
            local[3i] = _e235;
            let _e628 = local_6;
            local[4i] = _e628;
            let _e631 = local_7;
            local[5i] = _e631;
            local[6i] = _e237;
            let _e635 = local_8;
            local[7i] = _e635;
            let _e638 = local_9;
            local[8i] = _e638;
            local[9i] = _e239;
            let _e642 = local_10;
            local[10i] = _e642;
            let _e645 = local_11;
            local[11i] = _e645;
            local[12i] = _e241;
            let _e649 = local_12;
            local[13i] = _e649;
            let _e652 = local_13;
            local[14i] = _e652;
            local[15i] = _e243;
            let _e656 = local_14;
            local[16i] = _e656;
            let _e659 = local_15;
            local[17i] = _e659;
            local[18i] = _e245;
            let _e663 = local_16;
            local[19i] = _e663;
            let _e666 = local_17;
            local[20i] = _e666;
            local[21i] = _e247;
            let _e670 = local_18;
            local[22i] = _e670;
            let _e673 = local_19;
            local[23i] = _e673;
            local[24i] = _e249;
            let _e677 = local_20;
            local[25i] = _e677;
            let _e680 = local_21;
            local[26i] = _e680;
            local[27i] = _e251;
            let _e684 = local_22;
            local[28i] = _e684;
            let _e687 = local_23;
            local[29i] = _e687;
            local[30i] = _e253;
            let _e691 = local_24;
            local[31i] = _e691;
            let _e694 = local_25;
            local[32i] = _e694;
            local[33i] = _e255;
            let _e698 = local_26;
            local[34i] = _e698;
            let _e701 = local_27;
            local[35i] = _e701;
            local[36i] = _e257;
            let _e705 = local_28;
            local[37i] = _e705;
            let _e708 = local_29;
            local[38i] = _e708;
            local[39i] = _e259;
            let _e712 = local_30;
            local[40i] = _e712;
            let _e715 = local_31;
            local[41i] = _e715;
            local[42i] = _e261;
            let _e719 = local_32;
            local[43i] = _e719;
            let _e722 = local_33;
            local[44i] = _e722;
            local[45i] = _e263;
            let _e726 = local_34;
            local[46i] = _e726;
            let _e729 = local_35;
            local[47i] = _e729;
            phi_604_ = 0i;
            loop {
                let _e731 = phi_604_;
                if (_e731 < 128i) {
                    let _e737 = Weights.member[bitcast<u32>((6144i + _e731))];
                    phi_615_ = _e737;
                    phi_618_ = 0i;
                    loop {
                        let _e739 = phi_615_;
                        let _e741 = phi_618_;
                        local_36 = _e739;
                        if (_e741 < 48i) {
                            continue;
                        } else {
                            break;
                        }
                        continuing {
                            let _e744 = local[_e741];
                            let _e750 = Weights.member[bitcast<u32>(((_e741 * 128i) + _e731))];
                            phi_615_ = (_e739 + (_e744 * _e750));
                            phi_618_ = (_e741 + 1i);
                        }
                    }
                    let _e755 = local_36;
                    local_1[_e731] = max(_e755, 0f);
                    continue;
                } else {
                    break;
                }
                continuing {
                    phi_604_ = (_e731 + 1i);
                }
            }
            phi_633_ = 0i;
            loop {
                let _e760 = phi_633_;
                if (_e760 < 16i) {
                    let _e766 = Weights.member[bitcast<u32>((8320i + _e760))];
                    phi_644_ = _e766;
                    phi_647_ = 0i;
                    loop {
                        let _e768 = phi_644_;
                        let _e770 = phi_647_;
                        local_37 = _e768;
                        if (_e770 < 128i) {
                            continue;
                        } else {
                            break;
                        }
                        continuing {
                            let _e773 = local_1[_e770];
                            let _e780 = Weights.member[bitcast<u32>(((6272i + (_e770 * 16i)) + _e760))];
                            phi_644_ = (_e768 + (_e773 * _e780));
                            phi_647_ = (_e770 + 1i);
                        }
                    }
                    let _e786 = local_37;
                    local_2[_e760] = _e786;
                    continue;
                } else {
                    break;
                }
                continuing {
                    phi_633_ = (_e760 + 1i);
                }
            }
            let _e789 = Params.Seed;
            let _e792 = ((_e135 * 374761393u) + (_e133 * 668265263u));
            let _e794 = (_e792 + (_e789 * 2654435761u));
            let _e798 = ((_e794 ^ (_e794 >> bitcast<u32>(13u))) * 1274126177u);
            let _e806 = Params.FireRate;
            let _e807 = ((f32(((_e798 ^ (_e798 >> bitcast<u32>(16u))) & 16777215u)) * 0.000000059604645f) <= _e806);
            if _e807 {
                let _e809 = local_2[0i];
                phi_684_ = _e809;
            } else {
                phi_684_ = 0f;
            }
            let _e811 = phi_684_;
            if _e807 {
                let _e814 = local_2[1i];
                phi_691_ = _e814;
            } else {
                phi_691_ = 0f;
            }
            let _e816 = phi_691_;
            if _e807 {
                let _e819 = local_2[2i];
                phi_698_ = _e819;
            } else {
                phi_698_ = 0f;
            }
            let _e821 = phi_698_;
            if _e807 {
                let _e824 = local_2[3i];
                phi_705_ = _e824;
            } else {
                phi_705_ = 0f;
            }
            let _e826 = phi_705_;
            if _e807 {
                let _e829 = local_2[4i];
                phi_712_ = _e829;
            } else {
                phi_712_ = 0f;
            }
            let _e831 = phi_712_;
            if _e807 {
                let _e834 = local_2[5i];
                phi_719_ = _e834;
            } else {
                phi_719_ = 0f;
            }
            let _e836 = phi_719_;
            if _e807 {
                let _e839 = local_2[6i];
                phi_726_ = _e839;
            } else {
                phi_726_ = 0f;
            }
            let _e841 = phi_726_;
            if _e807 {
                let _e844 = local_2[7i];
                phi_733_ = _e844;
            } else {
                phi_733_ = 0f;
            }
            let _e846 = phi_733_;
            if _e807 {
                let _e849 = local_2[8i];
                phi_740_ = _e849;
            } else {
                phi_740_ = 0f;
            }
            let _e851 = phi_740_;
            if _e807 {
                let _e854 = local_2[9i];
                phi_747_ = _e854;
            } else {
                phi_747_ = 0f;
            }
            let _e856 = phi_747_;
            if _e807 {
                let _e859 = local_2[10i];
                phi_754_ = _e859;
            } else {
                phi_754_ = 0f;
            }
            let _e861 = phi_754_;
            if _e807 {
                let _e864 = local_2[11i];
                phi_761_ = _e864;
            } else {
                phi_761_ = 0f;
            }
            let _e866 = phi_761_;
            if _e807 {
                let _e869 = local_2[12i];
                phi_768_ = _e869;
            } else {
                phi_768_ = 0f;
            }
            let _e871 = phi_768_;
            if _e807 {
                let _e874 = local_2[13i];
                phi_775_ = _e874;
            } else {
                phi_775_ = 0f;
            }
            let _e876 = phi_775_;
            if _e807 {
                let _e879 = local_2[14i];
                phi_782_ = _e879;
            } else {
                phi_782_ = 0f;
            }
            let _e881 = phi_782_;
            if _e807 {
                let _e884 = local_2[15i];
                phi_789_ = _e884;
            } else {
                phi_789_ = 0f;
            }
            let _e886 = phi_789_;
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
            DestState.member[_e136] = CellState(array<f32, 16>((_e233 + _e811), (_e235 + _e816), (_e237 + _e821), (_e239 + _e826), ((_e241 + _e831) + (((f32(((_e894 ^ (_e894 >> bitcast<u32>(16u))) & 16777215u)) * 0.000000059604645f) - 0.5f) * 0.01f)), ((_e243 + _e836) + (((f32(((_e910 ^ (_e910 >> bitcast<u32>(16u))) & 16777215u)) * 0.000000059604645f) - 0.5f) * 0.01f)), ((_e245 + _e841) + (((f32(((_e926 ^ (_e926 >> bitcast<u32>(16u))) & 16777215u)) * 0.000000059604645f) - 0.5f) * 0.01f)), ((_e247 + _e846) + (((f32(((_e942 ^ (_e942 >> bitcast<u32>(16u))) & 16777215u)) * 0.000000059604645f) - 0.5f) * 0.01f)), ((_e249 + _e851) + (((f32(((_e958 ^ (_e958 >> bitcast<u32>(16u))) & 16777215u)) * 0.000000059604645f) - 0.5f) * 0.01f)), ((_e251 + _e856) + (((f32(((_e974 ^ (_e974 >> bitcast<u32>(16u))) & 16777215u)) * 0.000000059604645f) - 0.5f) * 0.01f)), ((_e253 + _e861) + (((f32(((_e990 ^ (_e990 >> bitcast<u32>(16u))) & 16777215u)) * 0.000000059604645f) - 0.5f) * 0.01f)), ((_e255 + _e866) + (((f32(((_e1006 ^ (_e1006 >> bitcast<u32>(16u))) & 16777215u)) * 0.000000059604645f) - 0.5f) * 0.01f)), ((_e257 + _e871) + (((f32(((_e1022 ^ (_e1022 >> bitcast<u32>(16u))) & 16777215u)) * 0.000000059604645f) - 0.5f) * 0.01f)), ((_e259 + _e876) + (((f32(((_e1038 ^ (_e1038 >> bitcast<u32>(16u))) & 16777215u)) * 0.000000059604645f) - 0.5f) * 0.01f)), ((_e261 + _e881) + (((f32(((_e1054 ^ (_e1054 >> bitcast<u32>(16u))) & 16777215u)) * 0.000000059604645f) - 0.5f) * 0.01f)), ((_e263 + _e886) + (((f32(((_e1070 ^ (_e1070 >> bitcast<u32>(16u))) & 16777215u)) * 0.000000059604645f) - 0.5f) * 0.01f))));
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
