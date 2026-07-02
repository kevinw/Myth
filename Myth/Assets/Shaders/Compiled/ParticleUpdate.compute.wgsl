struct Particle {
    pos: vec2<f32>,
    vel: vec2<f32>,
    age: f32,
    lifetime: f32,
    seed: f32,
    effect: f32,
    flags: f32,
    pad: f32,
}

struct type_u002e_StructuredBuffer_u002e_Particle {
    member: array<Particle>,
}

struct EffectParams {
    gravity: f32,
    drag: f32,
    noiseAmount: f32,
    noiseScale: f32,
    sizeStart: f32,
    sizeEnd: f32,
    collisionMode: f32,
    bounciness: f32,
    style: f32,
    pad0_: f32,
    pad1_: f32,
    pad2_: f32,
}

struct type_u002e_StructuredBuffer_u002e_EffectParams {
    member: array<EffectParams>,
}

struct EmitParams {
    start: u32,
    count: u32,
    seed: u32,
    effect: u32,
    originX: f32,
    originY: f32,
    shape: u32,
    radius: f32,
    dirAngle: f32,
    spread: f32,
    speedMin: f32,
    speedMax: f32,
    lifeMin: f32,
    lifeMax: f32,
    baseVelocityX: f32,
    baseVelocityY: f32,
}

struct type_u002e_StructuredBuffer_u002e_EmitParams {
    member: array<EmitParams>,
}

struct type_u002e_RWStructuredBuffer_u002e_Particle {
    member: array<Particle>,
}

struct type_u002e_Params {
    Count: u32,
    Dt: f32,
    Time: f32,
    EmitCount: u32,
    CameraX: f32,
    CameraY: f32,
    Zoom: f32,
    TargetW: f32,
    TargetH: f32,
    Pad0_: f32,
    Pad1_: f32,
    Pad2_: f32,
}

@group(0) @binding(0) 
var SolidMask: texture_2d<f32>;
@group(0) @binding(100) 
var SolidSampler: sampler;
@group(0) @binding(1) 
var<storage> Src: type_u002e_StructuredBuffer_u002e_Particle;
@group(0) @binding(2) 
var<storage> Effects: type_u002e_StructuredBuffer_u002e_EffectParams;
@group(0) @binding(3) 
var<storage> Emits: type_u002e_StructuredBuffer_u002e_EmitParams;
@group(1) @binding(0) 
var<storage, read_write> Dst: type_u002e_RWStructuredBuffer_u002e_Particle;
@group(2) @binding(0) 
var<uniform> Params: type_u002e_Params;
var<private> global: vec3<u32>;

fn compute_main_1() {
    var phi_118_: u32;
    var phi_236_: vec2<f32>;
    var phi_237_: vec2<f32>;
    var phi_273_: f32;
    var phi_330_: bool;
    var phi_648_: bool;
    var phi_654_: bool;
    var phi_660_: bool;
    var phi_668_: f32;
    var phi_670_: bool;
    var phi_708_: bool;
    var phi_714_: bool;
    var phi_720_: bool;
    var phi_728_: f32;
    var local: vec2<f32>;
    var local_1: f32;
    var local_2: vec2<f32>;
    var phi_741_: bool;
    var phi_747_: bool;
    var phi_753_: bool;
    var phi_761_: f32;
    var local_3: vec2<f32>;
    var local_4: f32;
    var local_5: vec2<f32>;
    var phi_774_: bool;
    var phi_780_: bool;
    var phi_786_: bool;
    var phi_794_: f32;
    var local_6: vec2<f32>;
    var local_7: f32;
    var local_8: vec2<f32>;
    var phi_807_: bool;
    var phi_813_: bool;
    var phi_819_: bool;
    var phi_827_: f32;
    var phi_837_: vec2<f32>;
    var phi_840_: vec2<f32>;
    var phi_842_: vec2<f32>;
    var phi_843_: f32;
    var phi_845_: vec2<f32>;
    var phi_846_: f32;
    var phi_847_: f32;

    let _e67 = global;
    switch bitcast<i32>(0u) {
        default: {
            let _e71 = Params.Count;
            if (_e67.x >= _e71) {
                break;
            }
            phi_118_ = 0u;
            loop {
                let _e74 = phi_118_;
                let _e76 = Params.EmitCount;
                phi_330_ = false;
                if (_e74 < _e76) {
                    let _e80 = Emits.member[_e74];
                    if ((((_e67.x + _e71) - _e80.start) % _e71) >= _e80.count) {
                        continue;
                    }
                    let _e102 = vec2<f32>(_e80.originX, _e80.originY);
                    if (_e80.shape == 1u) {
                        let _e106 = (((_e67.x * 23u) + _e80.seed) + 4u);
                        let _e111 = (((_e106 ^ 61u) ^ (_e106 >> bitcast<u32>(16u))) * 9u);
                        let _e115 = ((_e111 ^ (_e111 >> bitcast<u32>(4u))) * 668265261u);
                        let _e126 = (((_e67.x * 29u) + _e80.seed) + 5u);
                        let _e131 = (((_e126 ^ 61u) ^ (_e126 >> bitcast<u32>(16u))) * 9u);
                        let _e135 = ((_e131 ^ (_e131 >> bitcast<u32>(4u))) * 668265261u);
                        let _e141 = (f32(((_e135 ^ (_e135 >> bitcast<u32>(15u))) & 16777215u)) * 0.00000037450704f);
                        phi_237_ = (_e102 + (vec2<f32>(cos(_e141), sin(_e141)) * (sqrt((f32(((_e115 ^ (_e115 >> bitcast<u32>(15u))) & 16777215u)) * 0.000000059604645f)) * _e80.radius)));
                    } else {
                        phi_236_ = _e102;
                        if (_e80.shape == 3u) {
                            let _e148 = cos(_e80.dirAngle);
                            let _e149 = sin(_e80.dirAngle);
                            let _e155 = (((_e67.x * 23u) + _e80.seed) + 4u);
                            let _e160 = (((_e155 ^ 61u) ^ (_e155 >> bitcast<u32>(16u))) * 9u);
                            let _e164 = ((_e160 ^ (_e160 >> bitcast<u32>(4u))) * 668265261u);
                            let _e176 = (((_e67.x * 29u) + _e80.seed) + 5u);
                            let _e181 = (((_e176 ^ 61u) ^ (_e176 >> bitcast<u32>(16u))) * 9u);
                            let _e185 = ((_e181 ^ (_e181 >> bitcast<u32>(4u))) * 668265261u);
                            phi_236_ = ((_e102 + (vec2<f32>(_e148, _e149) * ((f32(((_e164 ^ (_e164 >> bitcast<u32>(15u))) & 16777215u)) * 0.000000059604645f) * _e80.radius))) + (vec2<f32>(-(_e149), _e148) * (((f32(((_e185 ^ (_e185 >> bitcast<u32>(15u))) & 16777215u)) * 0.000000059604645f) - 0.5f) * _e80.spread)));
                        }
                        let _e197 = phi_236_;
                        phi_237_ = _e197;
                    }
                    let _e199 = phi_237_;
                    if (_e80.shape == 2u) {
                        let _e202 = ((_e67.x * 7u) + _e80.seed);
                        let _e207 = (((_e202 ^ 61u) ^ (_e202 >> bitcast<u32>(16u))) * 9u);
                        let _e211 = ((_e207 ^ (_e207 >> bitcast<u32>(4u))) * 668265261u);
                        phi_273_ = (_e80.dirAngle + (((f32(((_e211 ^ (_e211 >> bitcast<u32>(15u))) & 16777215u)) * 0.000000059604645f) - 0.5f) * _e80.spread));
                    } else {
                        let _e222 = ((_e67.x * 7u) + _e80.seed);
                        let _e227 = (((_e222 ^ 61u) ^ (_e222 >> bitcast<u32>(16u))) * 9u);
                        let _e231 = ((_e227 ^ (_e227 >> bitcast<u32>(4u))) * 668265261u);
                        phi_273_ = (f32(((_e231 ^ (_e231 >> bitcast<u32>(15u))) & 16777215u)) * 0.00000037450704f);
                    }
                    let _e239 = phi_273_;
                    let _e242 = (((_e67.x * 13u) + _e80.seed) + 1u);
                    let _e247 = (((_e242 ^ 61u) ^ (_e242 >> bitcast<u32>(16u))) * 9u);
                    let _e251 = ((_e247 ^ (_e247 >> bitcast<u32>(4u))) * 668265261u);
                    let _e267 = (((_e67.x * 17u) + _e80.seed) + 2u);
                    let _e272 = (((_e267 ^ 61u) ^ (_e267 >> bitcast<u32>(16u))) * 9u);
                    let _e276 = ((_e272 ^ (_e272 >> bitcast<u32>(4u))) * 668265261u);
                    let _e286 = (((_e67.x * 19u) + _e80.seed) + 3u);
                    let _e291 = (((_e286 ^ 61u) ^ (_e286 >> bitcast<u32>(16u))) * 9u);
                    let _e295 = ((_e291 ^ (_e291 >> bitcast<u32>(4u))) * 668265261u);
                    Dst.member[_e67.x] = Particle(_e199, ((vec2<f32>(cos(_e239), sin(_e239)) * mix(_e80.speedMin, _e80.speedMax, (f32(((_e251 ^ (_e251 >> bitcast<u32>(15u))) & 16777215u)) * 0.000000059604645f))) + vec2<f32>(_e80.baseVelocityX, _e80.baseVelocityY)), 0f, mix(_e80.lifeMin, _e80.lifeMax, (f32(((_e276 ^ (_e276 >> bitcast<u32>(15u))) & 16777215u)) * 0.000000059604645f)), (f32(((_e295 ^ (_e295 >> bitcast<u32>(15u))) & 16777215u)) * 0.000059604645f), f32(_e80.effect), 0f, f32());
                    phi_330_ = true;
                    break;
                } else {
                    break;
                }
                continuing {
                    phi_118_ = (_e74 + 1u);
                }
            }
            let _e307 = phi_330_;
            if _e307 {
                break;
            }
            let _e310 = Src.member[_e67.x];
            if (_e310.lifetime <= 0f) {
                Dst.member[_e67.x] = _e310;
                break;
            }
            let _e323 = Params.Dt;
            let _e324 = (_e310.age + _e323);
            if (_e324 >= _e310.lifetime) {
                Dst.member[_e67.x] = Particle(_e310.pos, _e310.vel, _e324, 0f, _e310.seed, _e310.effect, _e310.flags, _e310.pad);
                break;
            }
            if (_e310.flags >= 0.5f) {
                Dst.member[_e67.x] = Particle(_e310.pos, _e310.vel, _e324, _e310.lifetime, _e310.seed, _e310.effect, _e310.flags, _e310.pad);
                break;
            }
            let _e338 = Effects.member[u32((_e310.effect + 0.5f))].gravity;
            let _e340 = Effects.member[u32((_e310.effect + 0.5f))].drag;
            let _e342 = Effects.member[u32((_e310.effect + 0.5f))].noiseAmount;
            let _e344 = Effects.member[u32((_e310.effect + 0.5f))].noiseScale;
            let _e346 = Effects.member[u32((_e310.effect + 0.5f))].collisionMode;
            let _e348 = Effects.member[u32((_e310.effect + 0.5f))].bounciness;
            let _e350 = u32((_e346 + 0.5f));
            let _e353 = Params.Time;
            let _e358 = (((_e310.pos * _e344) + vec2((_e353 * 0.5f))) + vec2(_e310.seed));
            let _e359 = (_e358 + vec2<f32>(0f, 0.1f));
            let _e360 = floor(_e359);
            let _e361 = fract(_e359);
            let _e365 = ((_e361 * _e361) * (vec2<f32>(3f, 3f) - (_e361 * 2f)));
            let _e367 = fract((_e360 * vec2<f32>(123.34f, 345.45f)));
            let _e371 = (_e367 + vec2(dot(_e367, (_e367 + vec2<f32>(34.345f, 34.345f)))));
            let _e378 = fract(((_e360 + vec2<f32>(1f, 0f)) * vec2<f32>(123.34f, 345.45f)));
            let _e382 = (_e378 + vec2(dot(_e378, (_e378 + vec2<f32>(34.345f, 34.345f)))));
            let _e389 = fract(((_e360 + vec2<f32>(0f, 1f)) * vec2<f32>(123.34f, 345.45f)));
            let _e393 = (_e389 + vec2(dot(_e389, (_e389 + vec2<f32>(34.345f, 34.345f)))));
            let _e400 = fract(((_e360 + vec2<f32>(1f, 1f)) * vec2<f32>(123.34f, 345.45f)));
            let _e404 = (_e400 + vec2(dot(_e400, (_e400 + vec2<f32>(34.345f, 34.345f)))));
            let _e414 = (_e358 - vec2<f32>(0f, 0.1f));
            let _e415 = floor(_e414);
            let _e416 = fract(_e414);
            let _e420 = ((_e416 * _e416) * (vec2<f32>(3f, 3f) - (_e416 * 2f)));
            let _e422 = fract((_e415 * vec2<f32>(123.34f, 345.45f)));
            let _e426 = (_e422 + vec2(dot(_e422, (_e422 + vec2<f32>(34.345f, 34.345f)))));
            let _e433 = fract(((_e415 + vec2<f32>(1f, 0f)) * vec2<f32>(123.34f, 345.45f)));
            let _e437 = (_e433 + vec2(dot(_e433, (_e433 + vec2<f32>(34.345f, 34.345f)))));
            let _e444 = fract(((_e415 + vec2<f32>(0f, 1f)) * vec2<f32>(123.34f, 345.45f)));
            let _e448 = (_e444 + vec2(dot(_e444, (_e444 + vec2<f32>(34.345f, 34.345f)))));
            let _e455 = fract(((_e415 + vec2<f32>(1f, 1f)) * vec2<f32>(123.34f, 345.45f)));
            let _e459 = (_e455 + vec2(dot(_e455, (_e455 + vec2<f32>(34.345f, 34.345f)))));
            let _e469 = (_e358 + vec2<f32>(0.1f, 0f));
            let _e470 = floor(_e469);
            let _e471 = fract(_e469);
            let _e475 = ((_e471 * _e471) * (vec2<f32>(3f, 3f) - (_e471 * 2f)));
            let _e477 = fract((_e470 * vec2<f32>(123.34f, 345.45f)));
            let _e481 = (_e477 + vec2(dot(_e477, (_e477 + vec2<f32>(34.345f, 34.345f)))));
            let _e488 = fract(((_e470 + vec2<f32>(1f, 0f)) * vec2<f32>(123.34f, 345.45f)));
            let _e492 = (_e488 + vec2(dot(_e488, (_e488 + vec2<f32>(34.345f, 34.345f)))));
            let _e499 = fract(((_e470 + vec2<f32>(0f, 1f)) * vec2<f32>(123.34f, 345.45f)));
            let _e503 = (_e499 + vec2(dot(_e499, (_e499 + vec2<f32>(34.345f, 34.345f)))));
            let _e510 = fract(((_e470 + vec2<f32>(1f, 1f)) * vec2<f32>(123.34f, 345.45f)));
            let _e514 = (_e510 + vec2(dot(_e510, (_e510 + vec2<f32>(34.345f, 34.345f)))));
            let _e524 = (_e358 - vec2<f32>(0.1f, 0f));
            let _e525 = floor(_e524);
            let _e526 = fract(_e524);
            let _e530 = ((_e526 * _e526) * (vec2<f32>(3f, 3f) - (_e526 * 2f)));
            let _e532 = fract((_e525 * vec2<f32>(123.34f, 345.45f)));
            let _e536 = (_e532 + vec2(dot(_e532, (_e532 + vec2<f32>(34.345f, 34.345f)))));
            let _e543 = fract(((_e525 + vec2<f32>(1f, 0f)) * vec2<f32>(123.34f, 345.45f)));
            let _e547 = (_e543 + vec2(dot(_e543, (_e543 + vec2<f32>(34.345f, 34.345f)))));
            let _e554 = fract(((_e525 + vec2<f32>(0f, 1f)) * vec2<f32>(123.34f, 345.45f)));
            let _e558 = (_e554 + vec2(dot(_e554, (_e554 + vec2<f32>(34.345f, 34.345f)))));
            let _e565 = fract(((_e525 + vec2<f32>(1f, 1f)) * vec2<f32>(123.34f, 345.45f)));
            let _e569 = (_e565 + vec2(dot(_e565, (_e565 + vec2<f32>(34.345f, 34.345f)))));
            let _e586 = (_e310.vel + ((vec2<f32>(((mix(mix(fract((_e371.x * _e371.y)), fract((_e382.x * _e382.y)), _e365.x), mix(fract((_e393.x * _e393.y)), fract((_e404.x * _e404.y)), _e365.x), _e365.y) - mix(mix(fract((_e426.x * _e426.y)), fract((_e437.x * _e437.y)), _e420.x), mix(fract((_e448.x * _e448.y)), fract((_e459.x * _e459.y)), _e420.x), _e420.y)) * 5f), ((mix(mix(fract((_e481.x * _e481.y)), fract((_e492.x * _e492.y)), _e475.x), mix(fract((_e503.x * _e503.y)), fract((_e514.x * _e514.y)), _e475.x), _e475.y) - mix(mix(fract((_e536.x * _e536.y)), fract((_e547.x * _e547.y)), _e530.x), mix(fract((_e558.x * _e558.y)), fract((_e569.x * _e569.y)), _e530.x), _e530.y)) * -5f)) * _e342) * _e323));
            let _e596 = (vec2<f32>(_e586.x, (_e586.y + (_e338 * _e323))) * clamp((1f - (_e340 * _e323)), 0f, 1f));
            let _e598 = (_e310.pos + (_e596 * _e323));
            phi_670_ = false;
            if (_e350 > 0u) {
                switch bitcast<i32>(0u) {
                    default: {
                        let _e602 = Params.CameraX;
                        let _e604 = Params.CameraY;
                        let _e608 = Params.Zoom;
                        let _e611 = Params.TargetW;
                        let _e613 = Params.TargetH;
                        let _e615 = (((_e598 - vec2<f32>(_e602, _e604)) * _e608) / vec2<f32>(_e611, _e613));
                        phi_648_ = true;
                        if !((_e615.x < 0f)) {
                            phi_648_ = (_e615.x > 1f);
                        }
                        let _e621 = phi_648_;
                        phi_654_ = true;
                        if !(_e621) {
                            phi_654_ = (_e615.y < 0f);
                        }
                        let _e626 = phi_654_;
                        phi_660_ = true;
                        if !(_e626) {
                            phi_660_ = (_e615.y > 1f);
                        }
                        let _e631 = phi_660_;
                        if _e631 {
                            phi_668_ = 0f;
                            break;
                        }
                        let _e632 = textureSampleLevel(SolidMask, SolidSampler, _e615, 0f);
                        phi_668_ = _e632.x;
                        break;
                    }
                }
                let _e635 = phi_668_;
                phi_670_ = (_e635 > 0.5f);
            }
            let _e638 = phi_670_;
            if _e638 {
                let _e639 = (_e350 == 1u);
                if _e639 {
                    phi_842_ = _e596;
                    phi_843_ = _e310.flags;
                } else {
                    let _e640 = (_e350 == 2u);
                    if _e640 {
                        phi_840_ = vec2<f32>(0f, 0f);
                    } else {
                        switch bitcast<i32>(0u) {
                            default: {
                                switch bitcast<i32>(0u) {
                                    default: {
                                        let _e645 = Params.CameraX;
                                        let _e647 = Params.CameraY;
                                        let _e648 = vec2<f32>(_e645, _e647);
                                        let _e651 = Params.Zoom;
                                        let _e654 = Params.TargetW;
                                        let _e656 = Params.TargetH;
                                        let _e657 = vec2<f32>(_e654, _e656);
                                        let _e658 = ((((_e598 - vec2<f32>(2f, 0f)) - _e648) * _e651) / _e657);
                                        phi_708_ = true;
                                        local = _e648;
                                        local_1 = _e651;
                                        local_2 = _e657;
                                        local_3 = _e648;
                                        local_4 = _e651;
                                        local_5 = _e657;
                                        local_6 = _e648;
                                        local_7 = _e651;
                                        local_8 = _e657;
                                        if !((_e658.x < 0f)) {
                                            phi_708_ = (_e658.x > 1f);
                                        }
                                        let _e664 = phi_708_;
                                        phi_714_ = true;
                                        if !(_e664) {
                                            phi_714_ = (_e658.y < 0f);
                                        }
                                        let _e669 = phi_714_;
                                        phi_720_ = true;
                                        if !(_e669) {
                                            phi_720_ = (_e658.y > 1f);
                                        }
                                        let _e674 = phi_720_;
                                        if _e674 {
                                            phi_728_ = 0f;
                                            break;
                                        }
                                        let _e675 = textureSampleLevel(SolidMask, SolidSampler, _e658, 0f);
                                        phi_728_ = _e675.x;
                                        break;
                                    }
                                }
                                let _e678 = phi_728_;
                                switch bitcast<i32>(0u) {
                                    default: {
                                        let _e682 = local;
                                        let _e685 = local_1;
                                        let _e688 = local_2;
                                        let _e689 = ((((_e598 + vec2<f32>(2f, 0f)) - _e682) * _e685) / _e688);
                                        phi_741_ = true;
                                        if !((_e689.x < 0f)) {
                                            phi_741_ = (_e689.x > 1f);
                                        }
                                        let _e695 = phi_741_;
                                        phi_747_ = true;
                                        if !(_e695) {
                                            phi_747_ = (_e689.y < 0f);
                                        }
                                        let _e700 = phi_747_;
                                        phi_753_ = true;
                                        if !(_e700) {
                                            phi_753_ = (_e689.y > 1f);
                                        }
                                        let _e705 = phi_753_;
                                        if _e705 {
                                            phi_761_ = 0f;
                                            break;
                                        }
                                        let _e706 = textureSampleLevel(SolidMask, SolidSampler, _e689, 0f);
                                        phi_761_ = _e706.x;
                                        break;
                                    }
                                }
                                let _e709 = phi_761_;
                                switch bitcast<i32>(0u) {
                                    default: {
                                        let _e713 = local_3;
                                        let _e716 = local_4;
                                        let _e719 = local_5;
                                        let _e720 = ((((_e598 - vec2<f32>(0f, 2f)) - _e713) * _e716) / _e719);
                                        phi_774_ = true;
                                        if !((_e720.x < 0f)) {
                                            phi_774_ = (_e720.x > 1f);
                                        }
                                        let _e726 = phi_774_;
                                        phi_780_ = true;
                                        if !(_e726) {
                                            phi_780_ = (_e720.y < 0f);
                                        }
                                        let _e731 = phi_780_;
                                        phi_786_ = true;
                                        if !(_e731) {
                                            phi_786_ = (_e720.y > 1f);
                                        }
                                        let _e736 = phi_786_;
                                        if _e736 {
                                            phi_794_ = 0f;
                                            break;
                                        }
                                        let _e737 = textureSampleLevel(SolidMask, SolidSampler, _e720, 0f);
                                        phi_794_ = _e737.x;
                                        break;
                                    }
                                }
                                let _e740 = phi_794_;
                                switch bitcast<i32>(0u) {
                                    default: {
                                        let _e744 = local_6;
                                        let _e747 = local_7;
                                        let _e750 = local_8;
                                        let _e751 = ((((_e598 + vec2<f32>(0f, 2f)) - _e744) * _e747) / _e750);
                                        phi_807_ = true;
                                        if !((_e751.x < 0f)) {
                                            phi_807_ = (_e751.x > 1f);
                                        }
                                        let _e757 = phi_807_;
                                        phi_813_ = true;
                                        if !(_e757) {
                                            phi_813_ = (_e751.y < 0f);
                                        }
                                        let _e762 = phi_813_;
                                        phi_819_ = true;
                                        if !(_e762) {
                                            phi_819_ = (_e751.y > 1f);
                                        }
                                        let _e767 = phi_819_;
                                        if _e767 {
                                            phi_827_ = 0f;
                                            break;
                                        }
                                        let _e768 = textureSampleLevel(SolidMask, SolidSampler, _e751, 0f);
                                        phi_827_ = _e768.x;
                                        break;
                                    }
                                }
                                let _e771 = phi_827_;
                                let _e774 = vec2<f32>((_e709 - _e678), (_e771 - _e740));
                                if (dot(_e774, _e774) < 0.000001f) {
                                    phi_837_ = vec2<f32>(0f, -1f);
                                    break;
                                }
                                phi_837_ = -(normalize(_e774));
                                break;
                            }
                        }
                        let _e780 = phi_837_;
                        phi_840_ = (reflect(_e596, _e780) * _e348);
                    }
                    let _e784 = phi_840_;
                    phi_842_ = _e784;
                    phi_843_ = select(_e310.flags, 1f, _e640);
                }
                let _e787 = phi_842_;
                let _e789 = phi_843_;
                phi_845_ = _e787;
                phi_846_ = select(_e310.lifetime, 0f, _e639);
                phi_847_ = _e789;
            } else {
                phi_845_ = _e596;
                phi_846_ = _e310.lifetime;
                phi_847_ = _e310.flags;
            }
            let _e792 = phi_845_;
            let _e794 = phi_846_;
            let _e796 = phi_847_;
            Dst.member[_e67.x] = Particle(select(_e598, _e310.pos, vec2(_e638)), _e792, _e324, _e794, _e310.seed, _e310.effect, _e796, _e310.pad);
            break;
        }
    }
    return;
}

@compute @workgroup_size(64, 1, 1) 
fn compute_main(@builtin(global_invocation_id) param: vec3<u32>) {
    global = param;
    compute_main_1();
}
