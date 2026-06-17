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
    pad0_: f32,
    pad1_: f32,
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
    var phi_122_: u32;
    var phi_205_: vec2<f32>;
    var phi_241_: f32;
    var phi_296_: bool;
    var phi_614_: bool;
    var phi_620_: bool;
    var phi_626_: bool;
    var phi_634_: f32;
    var phi_636_: bool;
    var phi_674_: bool;
    var phi_680_: bool;
    var phi_686_: bool;
    var phi_694_: f32;
    var local: vec2<f32>;
    var local_1: f32;
    var local_2: vec2<f32>;
    var phi_707_: bool;
    var phi_713_: bool;
    var phi_719_: bool;
    var phi_727_: f32;
    var local_3: vec2<f32>;
    var local_4: f32;
    var local_5: vec2<f32>;
    var phi_740_: bool;
    var phi_746_: bool;
    var phi_752_: bool;
    var phi_760_: f32;
    var local_6: vec2<f32>;
    var local_7: f32;
    var local_8: vec2<f32>;
    var phi_773_: bool;
    var phi_779_: bool;
    var phi_785_: bool;
    var phi_793_: f32;
    var phi_803_: vec2<f32>;
    var phi_806_: vec2<f32>;
    var phi_808_: vec2<f32>;
    var phi_809_: f32;
    var phi_811_: vec2<f32>;
    var phi_812_: f32;
    var phi_813_: f32;

    let _e71 = global;
    switch bitcast<i32>(0u) {
        default: {
            let _e75 = Params.Count;
            if (_e71.x >= _e75) {
                break;
            }
            phi_122_ = 0u;
            loop {
                let _e78 = phi_122_;
                let _e80 = Params.EmitCount;
                phi_296_ = false;
                if (_e78 < _e80) {
                    let _e85 = Emits.member[_e78].start;
                    let _e87 = Emits.member[_e78].count;
                    let _e89 = Emits.member[_e78].seed;
                    let _e91 = Emits.member[_e78].effect;
                    let _e93 = Emits.member[_e78].originX;
                    let _e95 = Emits.member[_e78].originY;
                    let _e97 = Emits.member[_e78].shape;
                    let _e99 = Emits.member[_e78].radius;
                    let _e101 = Emits.member[_e78].dirAngle;
                    let _e103 = Emits.member[_e78].spread;
                    let _e105 = Emits.member[_e78].speedMin;
                    let _e107 = Emits.member[_e78].speedMax;
                    let _e109 = Emits.member[_e78].lifeMin;
                    let _e111 = Emits.member[_e78].lifeMax;
                    if ((((_e71.x + _e75) - _e85) % _e75) >= _e87) {
                        continue;
                    }
                    let _e117 = vec2<f32>(_e93, _e95);
                    phi_205_ = _e117;
                    if (_e97 == 1u) {
                        let _e121 = (((_e71.x * 23u) + _e89) + 4u);
                        let _e126 = (((_e121 ^ 61u) ^ (_e121 >> bitcast<u32>(16u))) * 9u);
                        let _e130 = ((_e126 ^ (_e126 >> bitcast<u32>(4u))) * 668265261u);
                        let _e141 = (((_e71.x * 29u) + _e89) + 5u);
                        let _e146 = (((_e141 ^ 61u) ^ (_e141 >> bitcast<u32>(16u))) * 9u);
                        let _e150 = ((_e146 ^ (_e146 >> bitcast<u32>(4u))) * 668265261u);
                        let _e156 = (f32(((_e150 ^ (_e150 >> bitcast<u32>(15u))) & 16777215u)) * 0.00000037450704f);
                        phi_205_ = (_e117 + (vec2<f32>(cos(_e156), sin(_e156)) * (sqrt((f32(((_e130 ^ (_e130 >> bitcast<u32>(15u))) & 16777215u)) * 0.000000059604645f)) * _e99)));
                    }
                    let _e163 = phi_205_;
                    if (_e97 == 2u) {
                        let _e166 = ((_e71.x * 7u) + _e89);
                        let _e171 = (((_e166 ^ 61u) ^ (_e166 >> bitcast<u32>(16u))) * 9u);
                        let _e175 = ((_e171 ^ (_e171 >> bitcast<u32>(4u))) * 668265261u);
                        phi_241_ = (_e101 + (((f32(((_e175 ^ (_e175 >> bitcast<u32>(15u))) & 16777215u)) * 0.000000059604645f) - 0.5f) * _e103));
                    } else {
                        let _e186 = ((_e71.x * 7u) + _e89);
                        let _e191 = (((_e186 ^ 61u) ^ (_e186 >> bitcast<u32>(16u))) * 9u);
                        let _e195 = ((_e191 ^ (_e191 >> bitcast<u32>(4u))) * 668265261u);
                        phi_241_ = (f32(((_e195 ^ (_e195 >> bitcast<u32>(15u))) & 16777215u)) * 0.00000037450704f);
                    }
                    let _e203 = phi_241_;
                    let _e206 = (((_e71.x * 13u) + _e89) + 1u);
                    let _e211 = (((_e206 ^ 61u) ^ (_e206 >> bitcast<u32>(16u))) * 9u);
                    let _e215 = ((_e211 ^ (_e211 >> bitcast<u32>(4u))) * 668265261u);
                    let _e229 = (((_e71.x * 17u) + _e89) + 2u);
                    let _e234 = (((_e229 ^ 61u) ^ (_e229 >> bitcast<u32>(16u))) * 9u);
                    let _e238 = ((_e234 ^ (_e234 >> bitcast<u32>(4u))) * 668265261u);
                    let _e248 = (((_e71.x * 19u) + _e89) + 3u);
                    let _e253 = (((_e248 ^ 61u) ^ (_e248 >> bitcast<u32>(16u))) * 9u);
                    let _e257 = ((_e253 ^ (_e253 >> bitcast<u32>(4u))) * 668265261u);
                    Dst.member[_e71.x] = Particle(_e163, (vec2<f32>(cos(_e203), sin(_e203)) * mix(_e105, _e107, (f32(((_e215 ^ (_e215 >> bitcast<u32>(15u))) & 16777215u)) * 0.000000059604645f))), 0f, mix(_e109, _e111, (f32(((_e238 ^ (_e238 >> bitcast<u32>(15u))) & 16777215u)) * 0.000000059604645f)), (f32(((_e257 ^ (_e257 >> bitcast<u32>(15u))) & 16777215u)) * 0.000059604645f), f32(_e91), 0f, f32());
                    phi_296_ = true;
                    break;
                } else {
                    break;
                }
                continuing {
                    phi_122_ = (_e78 + 1u);
                }
            }
            let _e269 = phi_296_;
            if _e269 {
                break;
            }
            let _e272 = Src.member[_e71.x];
            if (_e272.lifetime <= 0f) {
                Dst.member[_e71.x] = _e272;
                break;
            }
            let _e285 = Params.Dt;
            let _e286 = (_e272.age + _e285);
            if (_e286 >= _e272.lifetime) {
                Dst.member[_e71.x] = Particle(_e272.pos, _e272.vel, _e286, 0f, _e272.seed, _e272.effect, _e272.flags, _e272.pad);
                break;
            }
            if (_e272.flags >= 0.5f) {
                Dst.member[_e71.x] = Particle(_e272.pos, _e272.vel, _e286, _e272.lifetime, _e272.seed, _e272.effect, _e272.flags, _e272.pad);
                break;
            }
            let _e300 = Effects.member[u32((_e272.effect + 0.5f))].gravity;
            let _e302 = Effects.member[u32((_e272.effect + 0.5f))].drag;
            let _e304 = Effects.member[u32((_e272.effect + 0.5f))].noiseAmount;
            let _e306 = Effects.member[u32((_e272.effect + 0.5f))].noiseScale;
            let _e308 = Effects.member[u32((_e272.effect + 0.5f))].collisionMode;
            let _e310 = Effects.member[u32((_e272.effect + 0.5f))].bounciness;
            let _e312 = u32((_e308 + 0.5f));
            let _e315 = Params.Time;
            let _e320 = (((_e272.pos * _e306) + vec2((_e315 * 0.5f))) + vec2(_e272.seed));
            let _e321 = (_e320 + vec2<f32>(0f, 0.1f));
            let _e322 = floor(_e321);
            let _e323 = fract(_e321);
            let _e327 = ((_e323 * _e323) * (vec2<f32>(3f, 3f) - (_e323 * 2f)));
            let _e329 = fract((_e322 * vec2<f32>(123.34f, 345.45f)));
            let _e333 = (_e329 + vec2(dot(_e329, (_e329 + vec2<f32>(34.345f, 34.345f)))));
            let _e340 = fract(((_e322 + vec2<f32>(1f, 0f)) * vec2<f32>(123.34f, 345.45f)));
            let _e344 = (_e340 + vec2(dot(_e340, (_e340 + vec2<f32>(34.345f, 34.345f)))));
            let _e351 = fract(((_e322 + vec2<f32>(0f, 1f)) * vec2<f32>(123.34f, 345.45f)));
            let _e355 = (_e351 + vec2(dot(_e351, (_e351 + vec2<f32>(34.345f, 34.345f)))));
            let _e362 = fract(((_e322 + vec2<f32>(1f, 1f)) * vec2<f32>(123.34f, 345.45f)));
            let _e366 = (_e362 + vec2(dot(_e362, (_e362 + vec2<f32>(34.345f, 34.345f)))));
            let _e376 = (_e320 - vec2<f32>(0f, 0.1f));
            let _e377 = floor(_e376);
            let _e378 = fract(_e376);
            let _e382 = ((_e378 * _e378) * (vec2<f32>(3f, 3f) - (_e378 * 2f)));
            let _e384 = fract((_e377 * vec2<f32>(123.34f, 345.45f)));
            let _e388 = (_e384 + vec2(dot(_e384, (_e384 + vec2<f32>(34.345f, 34.345f)))));
            let _e395 = fract(((_e377 + vec2<f32>(1f, 0f)) * vec2<f32>(123.34f, 345.45f)));
            let _e399 = (_e395 + vec2(dot(_e395, (_e395 + vec2<f32>(34.345f, 34.345f)))));
            let _e406 = fract(((_e377 + vec2<f32>(0f, 1f)) * vec2<f32>(123.34f, 345.45f)));
            let _e410 = (_e406 + vec2(dot(_e406, (_e406 + vec2<f32>(34.345f, 34.345f)))));
            let _e417 = fract(((_e377 + vec2<f32>(1f, 1f)) * vec2<f32>(123.34f, 345.45f)));
            let _e421 = (_e417 + vec2(dot(_e417, (_e417 + vec2<f32>(34.345f, 34.345f)))));
            let _e431 = (_e320 + vec2<f32>(0.1f, 0f));
            let _e432 = floor(_e431);
            let _e433 = fract(_e431);
            let _e437 = ((_e433 * _e433) * (vec2<f32>(3f, 3f) - (_e433 * 2f)));
            let _e439 = fract((_e432 * vec2<f32>(123.34f, 345.45f)));
            let _e443 = (_e439 + vec2(dot(_e439, (_e439 + vec2<f32>(34.345f, 34.345f)))));
            let _e450 = fract(((_e432 + vec2<f32>(1f, 0f)) * vec2<f32>(123.34f, 345.45f)));
            let _e454 = (_e450 + vec2(dot(_e450, (_e450 + vec2<f32>(34.345f, 34.345f)))));
            let _e461 = fract(((_e432 + vec2<f32>(0f, 1f)) * vec2<f32>(123.34f, 345.45f)));
            let _e465 = (_e461 + vec2(dot(_e461, (_e461 + vec2<f32>(34.345f, 34.345f)))));
            let _e472 = fract(((_e432 + vec2<f32>(1f, 1f)) * vec2<f32>(123.34f, 345.45f)));
            let _e476 = (_e472 + vec2(dot(_e472, (_e472 + vec2<f32>(34.345f, 34.345f)))));
            let _e486 = (_e320 - vec2<f32>(0.1f, 0f));
            let _e487 = floor(_e486);
            let _e488 = fract(_e486);
            let _e492 = ((_e488 * _e488) * (vec2<f32>(3f, 3f) - (_e488 * 2f)));
            let _e494 = fract((_e487 * vec2<f32>(123.34f, 345.45f)));
            let _e498 = (_e494 + vec2(dot(_e494, (_e494 + vec2<f32>(34.345f, 34.345f)))));
            let _e505 = fract(((_e487 + vec2<f32>(1f, 0f)) * vec2<f32>(123.34f, 345.45f)));
            let _e509 = (_e505 + vec2(dot(_e505, (_e505 + vec2<f32>(34.345f, 34.345f)))));
            let _e516 = fract(((_e487 + vec2<f32>(0f, 1f)) * vec2<f32>(123.34f, 345.45f)));
            let _e520 = (_e516 + vec2(dot(_e516, (_e516 + vec2<f32>(34.345f, 34.345f)))));
            let _e527 = fract(((_e487 + vec2<f32>(1f, 1f)) * vec2<f32>(123.34f, 345.45f)));
            let _e531 = (_e527 + vec2(dot(_e527, (_e527 + vec2<f32>(34.345f, 34.345f)))));
            let _e548 = (_e272.vel + ((vec2<f32>(((mix(mix(fract((_e333.x * _e333.y)), fract((_e344.x * _e344.y)), _e327.x), mix(fract((_e355.x * _e355.y)), fract((_e366.x * _e366.y)), _e327.x), _e327.y) - mix(mix(fract((_e388.x * _e388.y)), fract((_e399.x * _e399.y)), _e382.x), mix(fract((_e410.x * _e410.y)), fract((_e421.x * _e421.y)), _e382.x), _e382.y)) * 5f), ((mix(mix(fract((_e443.x * _e443.y)), fract((_e454.x * _e454.y)), _e437.x), mix(fract((_e465.x * _e465.y)), fract((_e476.x * _e476.y)), _e437.x), _e437.y) - mix(mix(fract((_e498.x * _e498.y)), fract((_e509.x * _e509.y)), _e492.x), mix(fract((_e520.x * _e520.y)), fract((_e531.x * _e531.y)), _e492.x), _e492.y)) * -5f)) * _e304) * _e285));
            let _e558 = (vec2<f32>(_e548.x, (_e548.y + (_e300 * _e285))) * clamp((1f - (_e302 * _e285)), 0f, 1f));
            let _e560 = (_e272.pos + (_e558 * _e285));
            phi_636_ = false;
            if (_e312 > 0u) {
                switch bitcast<i32>(0u) {
                    default: {
                        let _e564 = Params.CameraX;
                        let _e566 = Params.CameraY;
                        let _e570 = Params.Zoom;
                        let _e573 = Params.TargetW;
                        let _e575 = Params.TargetH;
                        let _e577 = (((_e560 - vec2<f32>(_e564, _e566)) * _e570) / vec2<f32>(_e573, _e575));
                        phi_614_ = true;
                        if !((_e577.x < 0f)) {
                            phi_614_ = (_e577.x > 1f);
                        }
                        let _e583 = phi_614_;
                        phi_620_ = true;
                        if !(_e583) {
                            phi_620_ = (_e577.y < 0f);
                        }
                        let _e588 = phi_620_;
                        phi_626_ = true;
                        if !(_e588) {
                            phi_626_ = (_e577.y > 1f);
                        }
                        let _e593 = phi_626_;
                        if _e593 {
                            phi_634_ = 0f;
                            break;
                        }
                        let _e594 = textureSampleLevel(SolidMask, SolidSampler, _e577, 0f);
                        phi_634_ = _e594.w;
                        break;
                    }
                }
                let _e597 = phi_634_;
                phi_636_ = (_e597 > 0.5f);
            }
            let _e600 = phi_636_;
            if _e600 {
                let _e601 = (_e312 == 1u);
                if _e601 {
                    phi_808_ = _e558;
                    phi_809_ = _e272.flags;
                } else {
                    let _e602 = (_e312 == 2u);
                    if _e602 {
                        phi_806_ = vec2<f32>(0f, 0f);
                    } else {
                        switch bitcast<i32>(0u) {
                            default: {
                                switch bitcast<i32>(0u) {
                                    default: {
                                        let _e607 = Params.CameraX;
                                        let _e609 = Params.CameraY;
                                        let _e610 = vec2<f32>(_e607, _e609);
                                        let _e613 = Params.Zoom;
                                        let _e616 = Params.TargetW;
                                        let _e618 = Params.TargetH;
                                        let _e619 = vec2<f32>(_e616, _e618);
                                        let _e620 = ((((_e560 - vec2<f32>(2f, 0f)) - _e610) * _e613) / _e619);
                                        phi_674_ = true;
                                        local = _e610;
                                        local_1 = _e613;
                                        local_2 = _e619;
                                        local_3 = _e610;
                                        local_4 = _e613;
                                        local_5 = _e619;
                                        local_6 = _e610;
                                        local_7 = _e613;
                                        local_8 = _e619;
                                        if !((_e620.x < 0f)) {
                                            phi_674_ = (_e620.x > 1f);
                                        }
                                        let _e626 = phi_674_;
                                        phi_680_ = true;
                                        if !(_e626) {
                                            phi_680_ = (_e620.y < 0f);
                                        }
                                        let _e631 = phi_680_;
                                        phi_686_ = true;
                                        if !(_e631) {
                                            phi_686_ = (_e620.y > 1f);
                                        }
                                        let _e636 = phi_686_;
                                        if _e636 {
                                            phi_694_ = 0f;
                                            break;
                                        }
                                        let _e637 = textureSampleLevel(SolidMask, SolidSampler, _e620, 0f);
                                        phi_694_ = _e637.w;
                                        break;
                                    }
                                }
                                let _e640 = phi_694_;
                                switch bitcast<i32>(0u) {
                                    default: {
                                        let _e644 = local;
                                        let _e647 = local_1;
                                        let _e650 = local_2;
                                        let _e651 = ((((_e560 + vec2<f32>(2f, 0f)) - _e644) * _e647) / _e650);
                                        phi_707_ = true;
                                        if !((_e651.x < 0f)) {
                                            phi_707_ = (_e651.x > 1f);
                                        }
                                        let _e657 = phi_707_;
                                        phi_713_ = true;
                                        if !(_e657) {
                                            phi_713_ = (_e651.y < 0f);
                                        }
                                        let _e662 = phi_713_;
                                        phi_719_ = true;
                                        if !(_e662) {
                                            phi_719_ = (_e651.y > 1f);
                                        }
                                        let _e667 = phi_719_;
                                        if _e667 {
                                            phi_727_ = 0f;
                                            break;
                                        }
                                        let _e668 = textureSampleLevel(SolidMask, SolidSampler, _e651, 0f);
                                        phi_727_ = _e668.w;
                                        break;
                                    }
                                }
                                let _e671 = phi_727_;
                                switch bitcast<i32>(0u) {
                                    default: {
                                        let _e675 = local_3;
                                        let _e678 = local_4;
                                        let _e681 = local_5;
                                        let _e682 = ((((_e560 - vec2<f32>(0f, 2f)) - _e675) * _e678) / _e681);
                                        phi_740_ = true;
                                        if !((_e682.x < 0f)) {
                                            phi_740_ = (_e682.x > 1f);
                                        }
                                        let _e688 = phi_740_;
                                        phi_746_ = true;
                                        if !(_e688) {
                                            phi_746_ = (_e682.y < 0f);
                                        }
                                        let _e693 = phi_746_;
                                        phi_752_ = true;
                                        if !(_e693) {
                                            phi_752_ = (_e682.y > 1f);
                                        }
                                        let _e698 = phi_752_;
                                        if _e698 {
                                            phi_760_ = 0f;
                                            break;
                                        }
                                        let _e699 = textureSampleLevel(SolidMask, SolidSampler, _e682, 0f);
                                        phi_760_ = _e699.w;
                                        break;
                                    }
                                }
                                let _e702 = phi_760_;
                                switch bitcast<i32>(0u) {
                                    default: {
                                        let _e706 = local_6;
                                        let _e709 = local_7;
                                        let _e712 = local_8;
                                        let _e713 = ((((_e560 + vec2<f32>(0f, 2f)) - _e706) * _e709) / _e712);
                                        phi_773_ = true;
                                        if !((_e713.x < 0f)) {
                                            phi_773_ = (_e713.x > 1f);
                                        }
                                        let _e719 = phi_773_;
                                        phi_779_ = true;
                                        if !(_e719) {
                                            phi_779_ = (_e713.y < 0f);
                                        }
                                        let _e724 = phi_779_;
                                        phi_785_ = true;
                                        if !(_e724) {
                                            phi_785_ = (_e713.y > 1f);
                                        }
                                        let _e729 = phi_785_;
                                        if _e729 {
                                            phi_793_ = 0f;
                                            break;
                                        }
                                        let _e730 = textureSampleLevel(SolidMask, SolidSampler, _e713, 0f);
                                        phi_793_ = _e730.w;
                                        break;
                                    }
                                }
                                let _e733 = phi_793_;
                                let _e736 = vec2<f32>((_e671 - _e640), (_e733 - _e702));
                                if (dot(_e736, _e736) < 0.000001f) {
                                    phi_803_ = vec2<f32>(0f, -1f);
                                    break;
                                }
                                phi_803_ = -(normalize(_e736));
                                break;
                            }
                        }
                        let _e742 = phi_803_;
                        phi_806_ = (reflect(_e558, _e742) * _e310);
                    }
                    let _e746 = phi_806_;
                    phi_808_ = _e746;
                    phi_809_ = select(_e272.flags, 1f, _e602);
                }
                let _e749 = phi_808_;
                let _e751 = phi_809_;
                phi_811_ = _e749;
                phi_812_ = select(_e272.lifetime, 0f, _e601);
                phi_813_ = _e751;
            } else {
                phi_811_ = _e558;
                phi_812_ = _e272.lifetime;
                phi_813_ = _e272.flags;
            }
            let _e754 = phi_811_;
            let _e756 = phi_812_;
            let _e758 = phi_813_;
            Dst.member[_e71.x] = Particle(select(_e560, _e272.pos, vec2(_e600)), _e754, _e286, _e756, _e272.seed, _e272.effect, _e758, _e272.pad);
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
