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
    var phi_617_: bool;
    var phi_622_: bool;
    var phi_627_: bool;
    var phi_635_: f32;
    var phi_637_: bool;
    var phi_678_: bool;
    var phi_683_: bool;
    var phi_688_: bool;
    var phi_696_: f32;
    var local: vec2<f32>;
    var local_1: f32;
    var local_2: vec2<f32>;
    var phi_712_: bool;
    var phi_717_: bool;
    var phi_722_: bool;
    var phi_730_: f32;
    var local_3: vec2<f32>;
    var local_4: f32;
    var local_5: vec2<f32>;
    var phi_746_: bool;
    var phi_751_: bool;
    var phi_756_: bool;
    var phi_764_: f32;
    var local_6: vec2<f32>;
    var local_7: f32;
    var local_8: vec2<f32>;
    var phi_780_: bool;
    var phi_785_: bool;
    var phi_790_: bool;
    var phi_798_: f32;
    var phi_808_: vec2<f32>;
    var phi_811_: vec2<f32>;
    var phi_813_: vec2<f32>;
    var phi_814_: f32;
    var phi_816_: vec2<f32>;
    var phi_817_: f32;
    var phi_818_: f32;

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
            phi_637_ = false;
            if (_e312 > 0u) {
                switch bitcast<i32>(0u) {
                    default: {
                        let _e564 = Params.CameraX;
                        let _e566 = Params.CameraY;
                        let _e570 = Params.Zoom;
                        let _e573 = Params.TargetW;
                        let _e575 = Params.TargetH;
                        let _e577 = (((_e560 - vec2<f32>(_e564, _e566)) * _e570) / vec2<f32>(_e573, _e575));
                        let _e579 = (1f - _e577.y);
                        phi_617_ = true;
                        if !((_e577.x < 0f)) {
                            phi_617_ = (_e577.x > 1f);
                        }
                        let _e588 = phi_617_;
                        phi_622_ = true;
                        if !(_e588) {
                            phi_622_ = (_e579 < 0f);
                        }
                        let _e592 = phi_622_;
                        phi_627_ = true;
                        if !(_e592) {
                            phi_627_ = (_e579 > 1f);
                        }
                        let _e596 = phi_627_;
                        if _e596 {
                            phi_635_ = 0f;
                            break;
                        }
                        let _e597 = textureSampleLevel(SolidMask, SolidSampler, vec2<f32>(_e577.x, _e579), 0f);
                        phi_635_ = _e597.w;
                        break;
                    }
                }
                let _e600 = phi_635_;
                phi_637_ = (_e600 > 0.5f);
            }
            let _e603 = phi_637_;
            if _e603 {
                let _e604 = (_e312 == 1u);
                if _e604 {
                    phi_813_ = _e558;
                    phi_814_ = _e272.flags;
                } else {
                    let _e605 = (_e312 == 2u);
                    if _e605 {
                        phi_811_ = vec2<f32>(0f, 0f);
                    } else {
                        switch bitcast<i32>(0u) {
                            default: {
                                switch bitcast<i32>(0u) {
                                    default: {
                                        let _e610 = Params.CameraX;
                                        let _e612 = Params.CameraY;
                                        let _e613 = vec2<f32>(_e610, _e612);
                                        let _e616 = Params.Zoom;
                                        let _e619 = Params.TargetW;
                                        let _e621 = Params.TargetH;
                                        let _e622 = vec2<f32>(_e619, _e621);
                                        let _e623 = ((((_e560 - vec2<f32>(2f, 0f)) - _e613) * _e616) / _e622);
                                        let _e625 = (1f - _e623.y);
                                        phi_678_ = true;
                                        local = _e613;
                                        local_1 = _e616;
                                        local_2 = _e622;
                                        local_3 = _e613;
                                        local_4 = _e616;
                                        local_5 = _e622;
                                        local_6 = _e613;
                                        local_7 = _e616;
                                        local_8 = _e622;
                                        if !((_e623.x < 0f)) {
                                            phi_678_ = (_e623.x > 1f);
                                        }
                                        let _e634 = phi_678_;
                                        phi_683_ = true;
                                        if !(_e634) {
                                            phi_683_ = (_e625 < 0f);
                                        }
                                        let _e638 = phi_683_;
                                        phi_688_ = true;
                                        if !(_e638) {
                                            phi_688_ = (_e625 > 1f);
                                        }
                                        let _e642 = phi_688_;
                                        if _e642 {
                                            phi_696_ = 0f;
                                            break;
                                        }
                                        let _e643 = textureSampleLevel(SolidMask, SolidSampler, vec2<f32>(_e623.x, _e625), 0f);
                                        phi_696_ = _e643.w;
                                        break;
                                    }
                                }
                                let _e646 = phi_696_;
                                switch bitcast<i32>(0u) {
                                    default: {
                                        let _e650 = local;
                                        let _e653 = local_1;
                                        let _e656 = local_2;
                                        let _e657 = ((((_e560 + vec2<f32>(2f, 0f)) - _e650) * _e653) / _e656);
                                        let _e659 = (1f - _e657.y);
                                        phi_712_ = true;
                                        if !((_e657.x < 0f)) {
                                            phi_712_ = (_e657.x > 1f);
                                        }
                                        let _e668 = phi_712_;
                                        phi_717_ = true;
                                        if !(_e668) {
                                            phi_717_ = (_e659 < 0f);
                                        }
                                        let _e672 = phi_717_;
                                        phi_722_ = true;
                                        if !(_e672) {
                                            phi_722_ = (_e659 > 1f);
                                        }
                                        let _e676 = phi_722_;
                                        if _e676 {
                                            phi_730_ = 0f;
                                            break;
                                        }
                                        let _e677 = textureSampleLevel(SolidMask, SolidSampler, vec2<f32>(_e657.x, _e659), 0f);
                                        phi_730_ = _e677.w;
                                        break;
                                    }
                                }
                                let _e680 = phi_730_;
                                switch bitcast<i32>(0u) {
                                    default: {
                                        let _e684 = local_3;
                                        let _e687 = local_4;
                                        let _e690 = local_5;
                                        let _e691 = ((((_e560 - vec2<f32>(0f, 2f)) - _e684) * _e687) / _e690);
                                        let _e693 = (1f - _e691.y);
                                        phi_746_ = true;
                                        if !((_e691.x < 0f)) {
                                            phi_746_ = (_e691.x > 1f);
                                        }
                                        let _e702 = phi_746_;
                                        phi_751_ = true;
                                        if !(_e702) {
                                            phi_751_ = (_e693 < 0f);
                                        }
                                        let _e706 = phi_751_;
                                        phi_756_ = true;
                                        if !(_e706) {
                                            phi_756_ = (_e693 > 1f);
                                        }
                                        let _e710 = phi_756_;
                                        if _e710 {
                                            phi_764_ = 0f;
                                            break;
                                        }
                                        let _e711 = textureSampleLevel(SolidMask, SolidSampler, vec2<f32>(_e691.x, _e693), 0f);
                                        phi_764_ = _e711.w;
                                        break;
                                    }
                                }
                                let _e714 = phi_764_;
                                switch bitcast<i32>(0u) {
                                    default: {
                                        let _e718 = local_6;
                                        let _e721 = local_7;
                                        let _e724 = local_8;
                                        let _e725 = ((((_e560 + vec2<f32>(0f, 2f)) - _e718) * _e721) / _e724);
                                        let _e727 = (1f - _e725.y);
                                        phi_780_ = true;
                                        if !((_e725.x < 0f)) {
                                            phi_780_ = (_e725.x > 1f);
                                        }
                                        let _e736 = phi_780_;
                                        phi_785_ = true;
                                        if !(_e736) {
                                            phi_785_ = (_e727 < 0f);
                                        }
                                        let _e740 = phi_785_;
                                        phi_790_ = true;
                                        if !(_e740) {
                                            phi_790_ = (_e727 > 1f);
                                        }
                                        let _e744 = phi_790_;
                                        if _e744 {
                                            phi_798_ = 0f;
                                            break;
                                        }
                                        let _e745 = textureSampleLevel(SolidMask, SolidSampler, vec2<f32>(_e725.x, _e727), 0f);
                                        phi_798_ = _e745.w;
                                        break;
                                    }
                                }
                                let _e748 = phi_798_;
                                let _e751 = vec2<f32>((_e680 - _e646), (_e748 - _e714));
                                if (dot(_e751, _e751) < 0.000001f) {
                                    phi_808_ = vec2<f32>(0f, -1f);
                                    break;
                                }
                                phi_808_ = -(normalize(_e751));
                                break;
                            }
                        }
                        let _e757 = phi_808_;
                        phi_811_ = (reflect(_e558, _e757) * _e310);
                    }
                    let _e761 = phi_811_;
                    phi_813_ = _e761;
                    phi_814_ = select(_e272.flags, 1f, _e605);
                }
                let _e764 = phi_813_;
                let _e766 = phi_814_;
                phi_816_ = _e764;
                phi_817_ = select(_e272.lifetime, 0f, _e604);
                phi_818_ = _e766;
            } else {
                phi_816_ = _e558;
                phi_817_ = _e272.lifetime;
                phi_818_ = _e272.flags;
            }
            let _e769 = phi_816_;
            let _e771 = phi_817_;
            let _e773 = phi_818_;
            Dst.member[_e71.x] = Particle(select(_e560, _e272.pos, vec2(_e603)), _e769, _e286, _e771, _e272.seed, _e272.effect, _e773, _e272.pad);
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
