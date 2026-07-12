struct Particle {
    pos: vec2<f32>,
    vel: vec2<f32>,
    age: f32,
    lifetime: f32,
    seed: f32,
    effect: f32,
    flags: f32,
    trailHead: f32,
    trailTimer: f32,
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
    trailSeconds: f32,
    trailWidthStart: f32,
    trailWidthEnd: f32,
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
    pointStart: u32,
    pad0_: u32,
    pad1_: u32,
    pad2_: u32,
}

struct type_u002e_StructuredBuffer_u002e_EmitParams {
    member: array<EmitParams>,
}

struct type_u002e_StructuredBuffer_u002e_v2float {
    member: array<vec2<f32>>,
}

struct type_u002e_StructuredBuffer_u002e_v4float {
    member: array<vec4<f32>>,
}

struct EdgeFieldParams {
    originX: f32,
    originY: f32,
    rectLocalX: f32,
    rectLocalY: f32,
    rectW: f32,
    rectH: f32,
    srcX: f32,
    srcY: f32,
    flip: f32,
    attraction: f32,
    flow: f32,
    enable_: f32,
}

struct type_u002e_StructuredBuffer_u002e_EdgeFieldParams {
    member: array<EdgeFieldParams>,
}

struct type_u002e_RWStructuredBuffer_u002e_Particle {
    member: array<Particle>,
}

struct type_u002e_RWStructuredBuffer_u002e_v2float {
    member: array<vec2<f32>>,
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
    SdfAtlasW: f32,
    SdfAtlasH: f32,
    Pad2_: f32,
}

@group(0) @binding(0) 
var SolidMask: texture_2d<f32>;
@group(0) @binding(100) 
var SolidSampler: sampler;
@group(0) @binding(1) 
var SdfAtlas: texture_2d<f32>;
@group(0) @binding(101) 
var SdfSampler: sampler;
@group(0) @binding(2) 
var<storage> Src: type_u002e_StructuredBuffer_u002e_Particle;
@group(0) @binding(3) 
var<storage> Effects: type_u002e_StructuredBuffer_u002e_EffectParams;
@group(0) @binding(4) 
var<storage> Emits: type_u002e_StructuredBuffer_u002e_EmitParams;
@group(0) @binding(5) 
var<storage> TrailSrc: type_u002e_StructuredBuffer_u002e_v2float;
@group(0) @binding(6) 
var<storage> EmitPoints: type_u002e_StructuredBuffer_u002e_v4float;
@group(0) @binding(7) 
var<storage> Fields: type_u002e_StructuredBuffer_u002e_EdgeFieldParams;
@group(1) @binding(0) 
var<storage, read_write> Dst: type_u002e_RWStructuredBuffer_u002e_Particle;
@group(1) @binding(1) 
var<storage, read_write> TrailDst: type_u002e_RWStructuredBuffer_u002e_v2float;
@group(2) @binding(0) 
var<uniform> Params: type_u002e_Params;
var<private> global: vec3<u32>;

fn compute_main_1() {
    var phi_151_: u32;
    var phi_286_: vec2<f32>;
    var phi_287_: vec2<f32>;
    var phi_296_: vec2<f32>;
    var phi_297_: f32;
    var phi_353_: f32;
    var phi_354_: f32;
    var phi_412_: u32;
    var phi_419_: bool;
    var phi_438_: u32;
    var phi_474_: u32;
    var phi_487_: bool;
    var phi_766_: bool;
    var phi_771_: bool;
    var phi_777_: bool;
    var phi_846_: vec2<f32>;
    var phi_847_: vec2<f32>;
    var phi_848_: vec2<f32>;
    var phi_849_: vec2<f32>;
    var phi_878_: bool;
    var phi_884_: bool;
    var phi_890_: bool;
    var phi_898_: f32;
    var phi_900_: bool;
    var phi_939_: bool;
    var phi_945_: bool;
    var phi_951_: bool;
    var phi_959_: f32;
    var local: vec2<f32>;
    var local_1: f32;
    var local_2: vec2<f32>;
    var phi_972_: bool;
    var phi_978_: bool;
    var phi_984_: bool;
    var phi_992_: f32;
    var local_3: vec2<f32>;
    var local_4: f32;
    var local_5: vec2<f32>;
    var phi_1005_: bool;
    var phi_1011_: bool;
    var phi_1017_: bool;
    var phi_1025_: f32;
    var local_6: vec2<f32>;
    var local_7: f32;
    var local_8: vec2<f32>;
    var phi_1038_: bool;
    var phi_1044_: bool;
    var phi_1050_: bool;
    var phi_1058_: f32;
    var phi_1068_: vec2<f32>;
    var phi_1071_: vec2<f32>;
    var phi_1073_: vec2<f32>;
    var phi_1074_: f32;
    var phi_1075_: f32;
    var phi_1076_: vec2<f32>;
    var phi_1077_: f32;
    var phi_1078_: f32;
    var phi_1081_: vec2<f32>;
    var phi_1082_: f32;
    var phi_1083_: f32;
    var phi_1084_: vec2<f32>;
    var phi_1086_: u32;
    var phi_1109_: f32;
    var phi_1110_: f32;
    var phi_1111_: u32;
    var phi_1114_: f32;
    var phi_1115_: f32;

    let _e84 = global;
    switch bitcast<i32>(0u) {
        default: {
            let _e88 = Params.Count;
            if (_e84.x >= _e88) {
                break;
            }
            let _e90 = (_e84.x * 16u);
            phi_151_ = 0u;
            loop {
                let _e92 = phi_151_;
                let _e94 = Params.EmitCount;
                phi_419_ = false;
                if (_e92 < _e94) {
                    let _e99 = Emits.member[_e92].start;
                    let _e101 = Emits.member[_e92].count;
                    let _e103 = Emits.member[_e92].seed;
                    let _e105 = Emits.member[_e92].effect;
                    let _e107 = Emits.member[_e92].originX;
                    let _e109 = Emits.member[_e92].originY;
                    let _e111 = Emits.member[_e92].shape;
                    let _e113 = Emits.member[_e92].radius;
                    let _e115 = Emits.member[_e92].dirAngle;
                    let _e117 = Emits.member[_e92].spread;
                    let _e119 = Emits.member[_e92].speedMin;
                    let _e121 = Emits.member[_e92].speedMax;
                    let _e123 = Emits.member[_e92].lifeMin;
                    let _e125 = Emits.member[_e92].lifeMax;
                    let _e127 = Emits.member[_e92].baseVelocityX;
                    let _e129 = Emits.member[_e92].baseVelocityY;
                    let _e131 = Emits.member[_e92].pointStart;
                    let _e134 = (((_e84.x + _e88) - _e99) % _e88);
                    if (_e134 >= _e101) {
                        continue;
                    }
                    let _e137 = vec2<f32>(_e107, _e109);
                    if (_e111 == 1u) {
                        let _e141 = (((_e84.x * 23u) + _e103) + 4u);
                        let _e146 = (((_e141 ^ 61u) ^ (_e141 >> bitcast<u32>(16u))) * 9u);
                        let _e150 = ((_e146 ^ (_e146 >> bitcast<u32>(4u))) * 668265261u);
                        let _e161 = (((_e84.x * 29u) + _e103) + 5u);
                        let _e166 = (((_e161 ^ 61u) ^ (_e161 >> bitcast<u32>(16u))) * 9u);
                        let _e170 = ((_e166 ^ (_e166 >> bitcast<u32>(4u))) * 668265261u);
                        let _e176 = (f32(((_e170 ^ (_e170 >> bitcast<u32>(15u))) & 16777215u)) * 0.00000037450704f);
                        phi_287_ = (_e137 + (vec2<f32>(cos(_e176), sin(_e176)) * (sqrt((f32(((_e150 ^ (_e150 >> bitcast<u32>(15u))) & 16777215u)) * 0.000000059604645f)) * _e113)));
                    } else {
                        phi_286_ = _e137;
                        if (_e111 == 3u) {
                            let _e183 = cos(_e115);
                            let _e184 = sin(_e115);
                            let _e190 = (((_e84.x * 23u) + _e103) + 4u);
                            let _e195 = (((_e190 ^ 61u) ^ (_e190 >> bitcast<u32>(16u))) * 9u);
                            let _e199 = ((_e195 ^ (_e195 >> bitcast<u32>(4u))) * 668265261u);
                            let _e211 = (((_e84.x * 29u) + _e103) + 5u);
                            let _e216 = (((_e211 ^ 61u) ^ (_e211 >> bitcast<u32>(16u))) * 9u);
                            let _e220 = ((_e216 ^ (_e216 >> bitcast<u32>(4u))) * 668265261u);
                            phi_286_ = ((_e137 + (vec2<f32>(_e183, _e184) * ((f32(((_e199 ^ (_e199 >> bitcast<u32>(15u))) & 16777215u)) * 0.000000059604645f) * _e113))) + (vec2<f32>(-(_e184), _e183) * (((f32(((_e220 ^ (_e220 >> bitcast<u32>(15u))) & 16777215u)) * 0.000000059604645f) - 0.5f) * _e117)));
                        }
                        let _e232 = phi_286_;
                        phi_287_ = _e232;
                    }
                    let _e234 = phi_287_;
                    let _e235 = (_e111 == 4u);
                    phi_296_ = _e234;
                    phi_297_ = 0f;
                    if _e235 {
                        let _e239 = EmitPoints.member[(_e131 + _e134)];
                        phi_296_ = _e239.xy;
                        phi_297_ = _e239.z;
                    }
                    let _e243 = phi_296_;
                    let _e245 = phi_297_;
                    if (_e111 == 2u) {
                        let _e248 = ((_e84.x * 7u) + _e103);
                        let _e253 = (((_e248 ^ 61u) ^ (_e248 >> bitcast<u32>(16u))) * 9u);
                        let _e257 = ((_e253 ^ (_e253 >> bitcast<u32>(4u))) * 668265261u);
                        phi_354_ = (_e115 + (((f32(((_e257 ^ (_e257 >> bitcast<u32>(15u))) & 16777215u)) * 0.000000059604645f) - 0.5f) * _e117));
                    } else {
                        if _e235 {
                            let _e268 = ((_e84.x * 7u) + _e103);
                            let _e273 = (((_e268 ^ 61u) ^ (_e268 >> bitcast<u32>(16u))) * 9u);
                            let _e277 = ((_e273 ^ (_e273 >> bitcast<u32>(4u))) * 668265261u);
                            phi_353_ = (_e245 + (((f32(((_e277 ^ (_e277 >> bitcast<u32>(15u))) & 16777215u)) * 0.000000059604645f) - 0.5f) * _e117));
                        } else {
                            let _e288 = ((_e84.x * 7u) + _e103);
                            let _e293 = (((_e288 ^ 61u) ^ (_e288 >> bitcast<u32>(16u))) * 9u);
                            let _e297 = ((_e293 ^ (_e293 >> bitcast<u32>(4u))) * 668265261u);
                            phi_353_ = (f32(((_e297 ^ (_e297 >> bitcast<u32>(15u))) & 16777215u)) * 0.00000037450704f);
                        }
                        let _e305 = phi_353_;
                        phi_354_ = _e305;
                    }
                    let _e307 = phi_354_;
                    let _e310 = (((_e84.x * 13u) + _e103) + 1u);
                    let _e315 = (((_e310 ^ 61u) ^ (_e310 >> bitcast<u32>(16u))) * 9u);
                    let _e319 = ((_e315 ^ (_e315 >> bitcast<u32>(4u))) * 668265261u);
                    let _e335 = (((_e84.x * 17u) + _e103) + 2u);
                    let _e340 = (((_e335 ^ 61u) ^ (_e335 >> bitcast<u32>(16u))) * 9u);
                    let _e344 = ((_e340 ^ (_e340 >> bitcast<u32>(4u))) * 668265261u);
                    let _e354 = (((_e84.x * 19u) + _e103) + 3u);
                    let _e359 = (((_e354 ^ 61u) ^ (_e354 >> bitcast<u32>(16u))) * 9u);
                    let _e363 = ((_e359 ^ (_e359 >> bitcast<u32>(4u))) * 668265261u);
                    Dst.member[_e84.x] = Particle(_e243, ((vec2<f32>(cos(_e307), sin(_e307)) * mix(_e119, _e121, (f32(((_e319 ^ (_e319 >> bitcast<u32>(15u))) & 16777215u)) * 0.000000059604645f))) + vec2<f32>(_e127, _e129)), 0f, mix(_e123, _e125, (f32(((_e344 ^ (_e344 >> bitcast<u32>(15u))) & 16777215u)) * 0.000000059604645f)), (f32(((_e363 ^ (_e363 >> bitcast<u32>(15u))) & 16777215u)) * 0.000059604645f), f32(_e105), 0f, 0f, 0f, f32());
                    phi_412_ = 0u;
                    loop {
                        let _e375 = phi_412_;
                        if (_e375 < 16u) {
                            continue;
                        } else {
                            break;
                        }
                        continuing {
                            TrailDst.member[(_e90 + _e375)] = _e243;
                            phi_412_ = (_e375 + 1u);
                        }
                    }
                    phi_419_ = true;
                    break;
                } else {
                    break;
                }
                continuing {
                    phi_151_ = (_e92 + 1u);
                }
            }
            let _e382 = phi_419_;
            if _e382 {
                break;
            }
            let _e385 = Src.member[_e84.x];
            if (_e385.lifetime <= 0f) {
                Dst.member[_e84.x] = _e385;
                phi_438_ = 0u;
                loop {
                    let _e400 = phi_438_;
                    if (_e400 < 16u) {
                        continue;
                    } else {
                        break;
                    }
                    continuing {
                        let _e402 = (_e90 + _e400);
                        let _e405 = TrailSrc.member[_e402];
                        TrailDst.member[_e402] = _e405;
                        phi_438_ = (_e400 + 1u);
                    }
                }
                break;
            }
            let _e410 = u32((_e385.effect + 0.5f));
            let _e414 = Effects.member[_e410].gravity;
            let _e416 = Effects.member[_e410].drag;
            let _e418 = Effects.member[_e410].noiseAmount;
            let _e420 = Effects.member[_e410].noiseScale;
            let _e422 = Effects.member[_e410].collisionMode;
            let _e424 = Effects.member[_e410].bounciness;
            let _e426 = Effects.member[_e410].trailSeconds;
            let _e428 = Params.Dt;
            let _e429 = (_e385.age + _e428);
            if (_e429 >= (_e385.lifetime + _e426)) {
                Dst.member[_e84.x] = Particle(_e385.pos, _e385.vel, _e429, 0f, _e385.seed, _e385.effect, _e385.flags, _e385.trailHead, _e385.trailTimer, _e385.pad);
                phi_474_ = 0u;
                loop {
                    let _e436 = phi_474_;
                    if (_e436 < 16u) {
                        continue;
                    } else {
                        break;
                    }
                    continuing {
                        let _e438 = (_e90 + _e436);
                        let _e441 = TrailSrc.member[_e438];
                        TrailDst.member[_e438] = _e441;
                        phi_474_ = (_e436 + 1u);
                    }
                }
                break;
            }
            phi_487_ = false;
            if (_e385.flags < 0.5f) {
                phi_487_ = (_e429 < _e385.lifetime);
            }
            let _e448 = phi_487_;
            phi_1081_ = _e385.vel;
            phi_1082_ = _e385.lifetime;
            phi_1083_ = _e385.flags;
            phi_1084_ = _e385.pos;
            if _e448 {
                let _e450 = u32((_e422 + 0.5f));
                let _e453 = Params.Time;
                let _e458 = (((_e385.pos * _e420) + vec2((_e453 * 0.5f))) + vec2(_e385.seed));
                let _e459 = (_e458 + vec2<f32>(0f, 0.1f));
                let _e460 = floor(_e459);
                let _e461 = fract(_e459);
                let _e465 = ((_e461 * _e461) * (vec2<f32>(3f, 3f) - (_e461 * 2f)));
                let _e467 = fract((_e460 * vec2<f32>(123.34f, 345.45f)));
                let _e471 = (_e467 + vec2(dot(_e467, (_e467 + vec2<f32>(34.345f, 34.345f)))));
                let _e478 = fract(((_e460 + vec2<f32>(1f, 0f)) * vec2<f32>(123.34f, 345.45f)));
                let _e482 = (_e478 + vec2(dot(_e478, (_e478 + vec2<f32>(34.345f, 34.345f)))));
                let _e489 = fract(((_e460 + vec2<f32>(0f, 1f)) * vec2<f32>(123.34f, 345.45f)));
                let _e493 = (_e489 + vec2(dot(_e489, (_e489 + vec2<f32>(34.345f, 34.345f)))));
                let _e500 = fract(((_e460 + vec2<f32>(1f, 1f)) * vec2<f32>(123.34f, 345.45f)));
                let _e504 = (_e500 + vec2(dot(_e500, (_e500 + vec2<f32>(34.345f, 34.345f)))));
                let _e514 = (_e458 - vec2<f32>(0f, 0.1f));
                let _e515 = floor(_e514);
                let _e516 = fract(_e514);
                let _e520 = ((_e516 * _e516) * (vec2<f32>(3f, 3f) - (_e516 * 2f)));
                let _e522 = fract((_e515 * vec2<f32>(123.34f, 345.45f)));
                let _e526 = (_e522 + vec2(dot(_e522, (_e522 + vec2<f32>(34.345f, 34.345f)))));
                let _e533 = fract(((_e515 + vec2<f32>(1f, 0f)) * vec2<f32>(123.34f, 345.45f)));
                let _e537 = (_e533 + vec2(dot(_e533, (_e533 + vec2<f32>(34.345f, 34.345f)))));
                let _e544 = fract(((_e515 + vec2<f32>(0f, 1f)) * vec2<f32>(123.34f, 345.45f)));
                let _e548 = (_e544 + vec2(dot(_e544, (_e544 + vec2<f32>(34.345f, 34.345f)))));
                let _e555 = fract(((_e515 + vec2<f32>(1f, 1f)) * vec2<f32>(123.34f, 345.45f)));
                let _e559 = (_e555 + vec2(dot(_e555, (_e555 + vec2<f32>(34.345f, 34.345f)))));
                let _e569 = (_e458 + vec2<f32>(0.1f, 0f));
                let _e570 = floor(_e569);
                let _e571 = fract(_e569);
                let _e575 = ((_e571 * _e571) * (vec2<f32>(3f, 3f) - (_e571 * 2f)));
                let _e577 = fract((_e570 * vec2<f32>(123.34f, 345.45f)));
                let _e581 = (_e577 + vec2(dot(_e577, (_e577 + vec2<f32>(34.345f, 34.345f)))));
                let _e588 = fract(((_e570 + vec2<f32>(1f, 0f)) * vec2<f32>(123.34f, 345.45f)));
                let _e592 = (_e588 + vec2(dot(_e588, (_e588 + vec2<f32>(34.345f, 34.345f)))));
                let _e599 = fract(((_e570 + vec2<f32>(0f, 1f)) * vec2<f32>(123.34f, 345.45f)));
                let _e603 = (_e599 + vec2(dot(_e599, (_e599 + vec2<f32>(34.345f, 34.345f)))));
                let _e610 = fract(((_e570 + vec2<f32>(1f, 1f)) * vec2<f32>(123.34f, 345.45f)));
                let _e614 = (_e610 + vec2(dot(_e610, (_e610 + vec2<f32>(34.345f, 34.345f)))));
                let _e624 = (_e458 - vec2<f32>(0.1f, 0f));
                let _e625 = floor(_e624);
                let _e626 = fract(_e624);
                let _e630 = ((_e626 * _e626) * (vec2<f32>(3f, 3f) - (_e626 * 2f)));
                let _e632 = fract((_e625 * vec2<f32>(123.34f, 345.45f)));
                let _e636 = (_e632 + vec2(dot(_e632, (_e632 + vec2<f32>(34.345f, 34.345f)))));
                let _e643 = fract(((_e625 + vec2<f32>(1f, 0f)) * vec2<f32>(123.34f, 345.45f)));
                let _e647 = (_e643 + vec2(dot(_e643, (_e643 + vec2<f32>(34.345f, 34.345f)))));
                let _e654 = fract(((_e625 + vec2<f32>(0f, 1f)) * vec2<f32>(123.34f, 345.45f)));
                let _e658 = (_e654 + vec2(dot(_e654, (_e654 + vec2<f32>(34.345f, 34.345f)))));
                let _e665 = fract(((_e625 + vec2<f32>(1f, 1f)) * vec2<f32>(123.34f, 345.45f)));
                let _e669 = (_e665 + vec2(dot(_e665, (_e665 + vec2<f32>(34.345f, 34.345f)))));
                let _e686 = (_e385.vel + ((vec2<f32>(((mix(mix(fract((_e471.x * _e471.y)), fract((_e482.x * _e482.y)), _e465.x), mix(fract((_e493.x * _e493.y)), fract((_e504.x * _e504.y)), _e465.x), _e465.y) - mix(mix(fract((_e526.x * _e526.y)), fract((_e537.x * _e537.y)), _e520.x), mix(fract((_e548.x * _e548.y)), fract((_e559.x * _e559.y)), _e520.x), _e520.y)) * 5f), ((mix(mix(fract((_e581.x * _e581.y)), fract((_e592.x * _e592.y)), _e575.x), mix(fract((_e603.x * _e603.y)), fract((_e614.x * _e614.y)), _e575.x), _e575.y) - mix(mix(fract((_e636.x * _e636.y)), fract((_e647.x * _e647.y)), _e630.x), mix(fract((_e658.x * _e658.y)), fract((_e669.x * _e669.y)), _e630.x), _e630.y)) * -5f)) * _e418) * _e428));
                let _e696 = (vec2<f32>(_e686.x, (_e686.y + (_e414 * _e428))) * clamp((1f - (_e416 * _e428)), 0f, 1f));
                let _e699 = Fields.member[_e410];
                phi_849_ = _e696;
                if (_e699.enable_ > 0.5f) {
                    let _e714 = (_e385.pos - vec2<f32>(_e699.originX, _e699.originY));
                    let _e721 = (vec2<f32>((_e714.x * _e699.flip), _e714.y) - vec2<f32>(_e699.rectLocalX, _e699.rectLocalY));
                    phi_766_ = false;
                    if (_e721.x >= 1f) {
                        phi_766_ = (_e721.y >= 1f);
                    }
                    let _e727 = phi_766_;
                    phi_771_ = false;
                    if _e727 {
                        phi_771_ = (_e721.x <= (_e699.rectW - 2f));
                    }
                    let _e731 = phi_771_;
                    phi_777_ = false;
                    if _e731 {
                        phi_777_ = (_e721.y <= (_e699.rectH - 2f));
                    }
                    let _e736 = phi_777_;
                    phi_848_ = _e696;
                    if _e736 {
                        let _e739 = Params.SdfAtlasW;
                        let _e741 = Params.SdfAtlasH;
                        let _e742 = vec2<f32>(_e739, _e741);
                        let _e743 = (vec2<f32>(_e699.srcX, _e699.srcY) + _e721);
                        let _e745 = textureSampleLevel(SdfAtlas, SdfSampler, (_e743 / _e742), 0f);
                        let _e747 = (_e745.x * 255f);
                        let _e750 = textureSampleLevel(SdfAtlas, SdfSampler, ((_e743 + vec2<f32>(1f, 0f)) / _e742), 0f);
                        let _e755 = textureSampleLevel(SdfAtlas, SdfSampler, ((_e743 + vec2<f32>(0f, 1f)) / _e742), 0f);
                        let _e760 = vec2<f32>(((_e750.x * 255f) - _e747), ((_e755.x * 255f) - _e747));
                        phi_847_ = _e696;
                        if (dot(_e760, _e760) > 0.000001f) {
                            let _e763 = normalize(_e760);
                            let _e765 = (_e763.x * _e699.flip);
                            let _e768 = vec2<f32>(_e765, _e763.y);
                            let _e779 = ((_e696 - (_e768 * (dot(_e696, _e768) * clamp((8f * _e428), 0f, 1f)))) - (_e768 * (((_e747 - 1f) * _e699.attraction) * _e428)));
                            phi_846_ = _e779;
                            if (_e699.flow != 0f) {
                                let _e784 = (vec2<f32>(-(_e763.y), _e765) * _e699.flip);
                                phi_846_ = (_e779 + (_e784 * ((_e699.flow - dot(_e779, _e784)) * clamp((10f * _e428), 0f, 1f))));
                            }
                            let _e793 = phi_846_;
                            phi_847_ = _e793;
                        }
                        let _e795 = phi_847_;
                        phi_848_ = _e795;
                    }
                    let _e797 = phi_848_;
                    phi_849_ = _e797;
                }
                let _e799 = phi_849_;
                let _e801 = (_e385.pos + (_e799 * _e428));
                phi_900_ = false;
                if (_e450 > 0u) {
                    switch bitcast<i32>(0u) {
                        default: {
                            let _e805 = Params.CameraX;
                            let _e807 = Params.CameraY;
                            let _e811 = Params.Zoom;
                            let _e814 = Params.TargetW;
                            let _e816 = Params.TargetH;
                            let _e818 = (((_e801 - vec2<f32>(_e805, _e807)) * _e811) / vec2<f32>(_e814, _e816));
                            phi_878_ = true;
                            if !((_e818.x < 0f)) {
                                phi_878_ = (_e818.x > 1f);
                            }
                            let _e824 = phi_878_;
                            phi_884_ = true;
                            if !(_e824) {
                                phi_884_ = (_e818.y < 0f);
                            }
                            let _e829 = phi_884_;
                            phi_890_ = true;
                            if !(_e829) {
                                phi_890_ = (_e818.y > 1f);
                            }
                            let _e834 = phi_890_;
                            if _e834 {
                                phi_898_ = 0f;
                                break;
                            }
                            let _e835 = textureSampleLevel(SolidMask, SolidSampler, _e818, 0f);
                            phi_898_ = _e835.x;
                            break;
                        }
                    }
                    let _e838 = phi_898_;
                    phi_900_ = (_e838 > 0.5f);
                }
                let _e841 = phi_900_;
                if _e841 {
                    if (_e450 == 1u) {
                        phi_1073_ = _e799;
                        phi_1074_ = min(_e385.lifetime, _e429);
                        phi_1075_ = _e385.flags;
                    } else {
                        let _e844 = (_e450 == 2u);
                        if _e844 {
                            phi_1071_ = vec2<f32>(0f, 0f);
                        } else {
                            switch bitcast<i32>(0u) {
                                default: {
                                    switch bitcast<i32>(0u) {
                                        default: {
                                            let _e849 = Params.CameraX;
                                            let _e851 = Params.CameraY;
                                            let _e852 = vec2<f32>(_e849, _e851);
                                            let _e855 = Params.Zoom;
                                            let _e858 = Params.TargetW;
                                            let _e860 = Params.TargetH;
                                            let _e861 = vec2<f32>(_e858, _e860);
                                            let _e862 = ((((_e801 - vec2<f32>(2f, 0f)) - _e852) * _e855) / _e861);
                                            phi_939_ = true;
                                            local = _e852;
                                            local_1 = _e855;
                                            local_2 = _e861;
                                            local_3 = _e852;
                                            local_4 = _e855;
                                            local_5 = _e861;
                                            local_6 = _e852;
                                            local_7 = _e855;
                                            local_8 = _e861;
                                            if !((_e862.x < 0f)) {
                                                phi_939_ = (_e862.x > 1f);
                                            }
                                            let _e868 = phi_939_;
                                            phi_945_ = true;
                                            if !(_e868) {
                                                phi_945_ = (_e862.y < 0f);
                                            }
                                            let _e873 = phi_945_;
                                            phi_951_ = true;
                                            if !(_e873) {
                                                phi_951_ = (_e862.y > 1f);
                                            }
                                            let _e878 = phi_951_;
                                            if _e878 {
                                                phi_959_ = 0f;
                                                break;
                                            }
                                            let _e879 = textureSampleLevel(SolidMask, SolidSampler, _e862, 0f);
                                            phi_959_ = _e879.x;
                                            break;
                                        }
                                    }
                                    let _e882 = phi_959_;
                                    switch bitcast<i32>(0u) {
                                        default: {
                                            let _e886 = local;
                                            let _e889 = local_1;
                                            let _e892 = local_2;
                                            let _e893 = ((((_e801 + vec2<f32>(2f, 0f)) - _e886) * _e889) / _e892);
                                            phi_972_ = true;
                                            if !((_e893.x < 0f)) {
                                                phi_972_ = (_e893.x > 1f);
                                            }
                                            let _e899 = phi_972_;
                                            phi_978_ = true;
                                            if !(_e899) {
                                                phi_978_ = (_e893.y < 0f);
                                            }
                                            let _e904 = phi_978_;
                                            phi_984_ = true;
                                            if !(_e904) {
                                                phi_984_ = (_e893.y > 1f);
                                            }
                                            let _e909 = phi_984_;
                                            if _e909 {
                                                phi_992_ = 0f;
                                                break;
                                            }
                                            let _e910 = textureSampleLevel(SolidMask, SolidSampler, _e893, 0f);
                                            phi_992_ = _e910.x;
                                            break;
                                        }
                                    }
                                    let _e913 = phi_992_;
                                    switch bitcast<i32>(0u) {
                                        default: {
                                            let _e917 = local_3;
                                            let _e920 = local_4;
                                            let _e923 = local_5;
                                            let _e924 = ((((_e801 - vec2<f32>(0f, 2f)) - _e917) * _e920) / _e923);
                                            phi_1005_ = true;
                                            if !((_e924.x < 0f)) {
                                                phi_1005_ = (_e924.x > 1f);
                                            }
                                            let _e930 = phi_1005_;
                                            phi_1011_ = true;
                                            if !(_e930) {
                                                phi_1011_ = (_e924.y < 0f);
                                            }
                                            let _e935 = phi_1011_;
                                            phi_1017_ = true;
                                            if !(_e935) {
                                                phi_1017_ = (_e924.y > 1f);
                                            }
                                            let _e940 = phi_1017_;
                                            if _e940 {
                                                phi_1025_ = 0f;
                                                break;
                                            }
                                            let _e941 = textureSampleLevel(SolidMask, SolidSampler, _e924, 0f);
                                            phi_1025_ = _e941.x;
                                            break;
                                        }
                                    }
                                    let _e944 = phi_1025_;
                                    switch bitcast<i32>(0u) {
                                        default: {
                                            let _e948 = local_6;
                                            let _e951 = local_7;
                                            let _e954 = local_8;
                                            let _e955 = ((((_e801 + vec2<f32>(0f, 2f)) - _e948) * _e951) / _e954);
                                            phi_1038_ = true;
                                            if !((_e955.x < 0f)) {
                                                phi_1038_ = (_e955.x > 1f);
                                            }
                                            let _e961 = phi_1038_;
                                            phi_1044_ = true;
                                            if !(_e961) {
                                                phi_1044_ = (_e955.y < 0f);
                                            }
                                            let _e966 = phi_1044_;
                                            phi_1050_ = true;
                                            if !(_e966) {
                                                phi_1050_ = (_e955.y > 1f);
                                            }
                                            let _e971 = phi_1050_;
                                            if _e971 {
                                                phi_1058_ = 0f;
                                                break;
                                            }
                                            let _e972 = textureSampleLevel(SolidMask, SolidSampler, _e955, 0f);
                                            phi_1058_ = _e972.x;
                                            break;
                                        }
                                    }
                                    let _e975 = phi_1058_;
                                    let _e978 = vec2<f32>((_e913 - _e882), (_e975 - _e944));
                                    if (dot(_e978, _e978) < 0.000001f) {
                                        phi_1068_ = vec2<f32>(0f, -1f);
                                        break;
                                    }
                                    phi_1068_ = -(normalize(_e978));
                                    break;
                                }
                            }
                            let _e984 = phi_1068_;
                            phi_1071_ = (reflect(_e799, _e984) * _e424);
                        }
                        let _e988 = phi_1071_;
                        phi_1073_ = _e988;
                        phi_1074_ = _e385.lifetime;
                        phi_1075_ = select(_e385.flags, 1f, _e844);
                    }
                    let _e991 = phi_1073_;
                    let _e993 = phi_1074_;
                    let _e995 = phi_1075_;
                    phi_1076_ = _e991;
                    phi_1077_ = _e993;
                    phi_1078_ = _e995;
                } else {
                    phi_1076_ = _e799;
                    phi_1077_ = _e385.lifetime;
                    phi_1078_ = _e385.flags;
                }
                let _e997 = phi_1076_;
                let _e999 = phi_1077_;
                let _e1001 = phi_1078_;
                phi_1081_ = _e997;
                phi_1082_ = _e999;
                phi_1083_ = _e1001;
                phi_1084_ = select(_e801, _e385.pos, vec2(_e841));
            }
            let _e1005 = phi_1081_;
            let _e1007 = phi_1082_;
            let _e1009 = phi_1083_;
            let _e1011 = phi_1084_;
            phi_1086_ = 0u;
            loop {
                let _e1013 = phi_1086_;
                if (_e1013 < 16u) {
                    continue;
                } else {
                    break;
                }
                continuing {
                    let _e1015 = (_e90 + _e1013);
                    let _e1018 = TrailSrc.member[_e1015];
                    TrailDst.member[_e1015] = _e1018;
                    phi_1086_ = (_e1013 + 1u);
                }
            }
            phi_1114_ = _e385.trailHead;
            phi_1115_ = _e385.trailTimer;
            if (_e426 > 0f) {
                let _e1023 = (_e426 * 0.06666667f);
                let _e1025 = u32((_e385.trailHead + 0.5f));
                let _e1026 = (_e385.trailTimer + _e428);
                phi_1109_ = _e385.trailHead;
                phi_1110_ = _e1026;
                phi_1111_ = _e1025;
                if (_e1026 >= _e1023) {
                    let _e1030 = ((_e1025 + 1u) % 16u);
                    phi_1109_ = f32(_e1030);
                    phi_1110_ = (_e1026 % _e1023);
                    phi_1111_ = _e1030;
                }
                let _e1033 = phi_1109_;
                let _e1035 = phi_1110_;
                let _e1037 = phi_1111_;
                TrailDst.member[(_e90 + _e1037)] = _e1011;
                phi_1114_ = _e1033;
                phi_1115_ = _e1035;
            }
            let _e1042 = phi_1114_;
            let _e1044 = phi_1115_;
            Dst.member[_e84.x] = Particle(_e1011, _e1005, _e429, _e1007, _e385.seed, _e385.effect, _e1009, _e1042, _e1044, _e385.pad);
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
