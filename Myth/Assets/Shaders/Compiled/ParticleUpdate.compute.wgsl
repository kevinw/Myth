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
    var phi_153_: u32;
    var phi_288_: vec2<f32>;
    var phi_289_: vec2<f32>;
    var phi_298_: vec2<f32>;
    var phi_299_: f32;
    var phi_355_: f32;
    var phi_356_: f32;
    var phi_414_: u32;
    var phi_421_: bool;
    var phi_440_: u32;
    var phi_476_: u32;
    var phi_489_: bool;
    var phi_768_: bool;
    var phi_773_: bool;
    var phi_779_: bool;
    var phi_850_: vec2<f32>;
    var phi_851_: vec2<f32>;
    var phi_852_: vec2<f32>;
    var phi_853_: vec2<f32>;
    var phi_854_: vec2<f32>;
    var phi_855_: vec2<f32>;
    var phi_856_: vec2<f32>;
    var phi_885_: bool;
    var phi_891_: bool;
    var phi_897_: bool;
    var phi_905_: f32;
    var phi_907_: bool;
    var phi_946_: bool;
    var phi_952_: bool;
    var phi_958_: bool;
    var phi_966_: f32;
    var local: vec2<f32>;
    var local_1: f32;
    var local_2: vec2<f32>;
    var phi_979_: bool;
    var phi_985_: bool;
    var phi_991_: bool;
    var phi_999_: f32;
    var local_3: vec2<f32>;
    var local_4: f32;
    var local_5: vec2<f32>;
    var phi_1012_: bool;
    var phi_1018_: bool;
    var phi_1024_: bool;
    var phi_1032_: f32;
    var local_6: vec2<f32>;
    var local_7: f32;
    var local_8: vec2<f32>;
    var phi_1045_: bool;
    var phi_1051_: bool;
    var phi_1057_: bool;
    var phi_1065_: f32;
    var phi_1075_: vec2<f32>;
    var phi_1078_: vec2<f32>;
    var phi_1080_: vec2<f32>;
    var phi_1081_: f32;
    var phi_1082_: f32;
    var phi_1083_: vec2<f32>;
    var phi_1084_: f32;
    var phi_1085_: f32;
    var phi_1088_: vec2<f32>;
    var phi_1089_: f32;
    var phi_1090_: f32;
    var phi_1091_: vec2<f32>;
    var phi_1093_: u32;
    var phi_1116_: f32;
    var phi_1117_: f32;
    var phi_1118_: u32;
    var phi_1121_: f32;
    var phi_1122_: f32;

    let _e86 = global;
    switch bitcast<i32>(0u) {
        default: {
            let _e90 = Params.Count;
            if (_e86.x >= _e90) {
                break;
            }
            let _e92 = (_e86.x * 16u);
            phi_153_ = 0u;
            loop {
                let _e94 = phi_153_;
                let _e96 = Params.EmitCount;
                phi_421_ = false;
                if (_e94 < _e96) {
                    let _e101 = Emits.member[_e94].start;
                    let _e103 = Emits.member[_e94].count;
                    let _e105 = Emits.member[_e94].seed;
                    let _e107 = Emits.member[_e94].effect;
                    let _e109 = Emits.member[_e94].originX;
                    let _e111 = Emits.member[_e94].originY;
                    let _e113 = Emits.member[_e94].shape;
                    let _e115 = Emits.member[_e94].radius;
                    let _e117 = Emits.member[_e94].dirAngle;
                    let _e119 = Emits.member[_e94].spread;
                    let _e121 = Emits.member[_e94].speedMin;
                    let _e123 = Emits.member[_e94].speedMax;
                    let _e125 = Emits.member[_e94].lifeMin;
                    let _e127 = Emits.member[_e94].lifeMax;
                    let _e129 = Emits.member[_e94].baseVelocityX;
                    let _e131 = Emits.member[_e94].baseVelocityY;
                    let _e133 = Emits.member[_e94].pointStart;
                    let _e136 = (((_e86.x + _e90) - _e101) % _e90);
                    if (_e136 >= _e103) {
                        continue;
                    }
                    let _e139 = vec2<f32>(_e109, _e111);
                    if (_e113 == 1u) {
                        let _e143 = (((_e86.x * 23u) + _e105) + 4u);
                        let _e148 = (((_e143 ^ 61u) ^ (_e143 >> bitcast<u32>(16u))) * 9u);
                        let _e152 = ((_e148 ^ (_e148 >> bitcast<u32>(4u))) * 668265261u);
                        let _e163 = (((_e86.x * 29u) + _e105) + 5u);
                        let _e168 = (((_e163 ^ 61u) ^ (_e163 >> bitcast<u32>(16u))) * 9u);
                        let _e172 = ((_e168 ^ (_e168 >> bitcast<u32>(4u))) * 668265261u);
                        let _e178 = (f32(((_e172 ^ (_e172 >> bitcast<u32>(15u))) & 16777215u)) * 0.00000037450704f);
                        phi_289_ = (_e139 + (vec2<f32>(cos(_e178), sin(_e178)) * (sqrt((f32(((_e152 ^ (_e152 >> bitcast<u32>(15u))) & 16777215u)) * 0.000000059604645f)) * _e115)));
                    } else {
                        phi_288_ = _e139;
                        if (_e113 == 3u) {
                            let _e185 = cos(_e117);
                            let _e186 = sin(_e117);
                            let _e192 = (((_e86.x * 23u) + _e105) + 4u);
                            let _e197 = (((_e192 ^ 61u) ^ (_e192 >> bitcast<u32>(16u))) * 9u);
                            let _e201 = ((_e197 ^ (_e197 >> bitcast<u32>(4u))) * 668265261u);
                            let _e213 = (((_e86.x * 29u) + _e105) + 5u);
                            let _e218 = (((_e213 ^ 61u) ^ (_e213 >> bitcast<u32>(16u))) * 9u);
                            let _e222 = ((_e218 ^ (_e218 >> bitcast<u32>(4u))) * 668265261u);
                            phi_288_ = ((_e139 + (vec2<f32>(_e185, _e186) * ((f32(((_e201 ^ (_e201 >> bitcast<u32>(15u))) & 16777215u)) * 0.000000059604645f) * _e115))) + (vec2<f32>(-(_e186), _e185) * (((f32(((_e222 ^ (_e222 >> bitcast<u32>(15u))) & 16777215u)) * 0.000000059604645f) - 0.5f) * _e119)));
                        }
                        let _e234 = phi_288_;
                        phi_289_ = _e234;
                    }
                    let _e236 = phi_289_;
                    let _e237 = (_e113 == 4u);
                    phi_298_ = _e236;
                    phi_299_ = 0f;
                    if _e237 {
                        let _e241 = EmitPoints.member[(_e133 + _e136)];
                        phi_298_ = _e241.xy;
                        phi_299_ = _e241.z;
                    }
                    let _e245 = phi_298_;
                    let _e247 = phi_299_;
                    if (_e113 == 2u) {
                        let _e250 = ((_e86.x * 7u) + _e105);
                        let _e255 = (((_e250 ^ 61u) ^ (_e250 >> bitcast<u32>(16u))) * 9u);
                        let _e259 = ((_e255 ^ (_e255 >> bitcast<u32>(4u))) * 668265261u);
                        phi_356_ = (_e117 + (((f32(((_e259 ^ (_e259 >> bitcast<u32>(15u))) & 16777215u)) * 0.000000059604645f) - 0.5f) * _e119));
                    } else {
                        if _e237 {
                            let _e270 = ((_e86.x * 7u) + _e105);
                            let _e275 = (((_e270 ^ 61u) ^ (_e270 >> bitcast<u32>(16u))) * 9u);
                            let _e279 = ((_e275 ^ (_e275 >> bitcast<u32>(4u))) * 668265261u);
                            phi_355_ = (_e247 + (((f32(((_e279 ^ (_e279 >> bitcast<u32>(15u))) & 16777215u)) * 0.000000059604645f) - 0.5f) * _e119));
                        } else {
                            let _e290 = ((_e86.x * 7u) + _e105);
                            let _e295 = (((_e290 ^ 61u) ^ (_e290 >> bitcast<u32>(16u))) * 9u);
                            let _e299 = ((_e295 ^ (_e295 >> bitcast<u32>(4u))) * 668265261u);
                            phi_355_ = (f32(((_e299 ^ (_e299 >> bitcast<u32>(15u))) & 16777215u)) * 0.00000037450704f);
                        }
                        let _e307 = phi_355_;
                        phi_356_ = _e307;
                    }
                    let _e309 = phi_356_;
                    let _e312 = (((_e86.x * 13u) + _e105) + 1u);
                    let _e317 = (((_e312 ^ 61u) ^ (_e312 >> bitcast<u32>(16u))) * 9u);
                    let _e321 = ((_e317 ^ (_e317 >> bitcast<u32>(4u))) * 668265261u);
                    let _e337 = (((_e86.x * 17u) + _e105) + 2u);
                    let _e342 = (((_e337 ^ 61u) ^ (_e337 >> bitcast<u32>(16u))) * 9u);
                    let _e346 = ((_e342 ^ (_e342 >> bitcast<u32>(4u))) * 668265261u);
                    let _e356 = (((_e86.x * 19u) + _e105) + 3u);
                    let _e361 = (((_e356 ^ 61u) ^ (_e356 >> bitcast<u32>(16u))) * 9u);
                    let _e365 = ((_e361 ^ (_e361 >> bitcast<u32>(4u))) * 668265261u);
                    Dst.member[_e86.x] = Particle(_e245, ((vec2<f32>(cos(_e309), sin(_e309)) * mix(_e121, _e123, (f32(((_e321 ^ (_e321 >> bitcast<u32>(15u))) & 16777215u)) * 0.000000059604645f))) + vec2<f32>(_e129, _e131)), 0f, mix(_e125, _e127, (f32(((_e346 ^ (_e346 >> bitcast<u32>(15u))) & 16777215u)) * 0.000000059604645f)), (f32(((_e365 ^ (_e365 >> bitcast<u32>(15u))) & 16777215u)) * 0.000059604645f), f32(_e107), 0f, 0f, 0f, f32());
                    phi_414_ = 0u;
                    loop {
                        let _e377 = phi_414_;
                        if (_e377 < 16u) {
                            continue;
                        } else {
                            break;
                        }
                        continuing {
                            TrailDst.member[(_e92 + _e377)] = _e245;
                            phi_414_ = (_e377 + 1u);
                        }
                    }
                    phi_421_ = true;
                    break;
                } else {
                    break;
                }
                continuing {
                    phi_153_ = (_e94 + 1u);
                }
            }
            let _e384 = phi_421_;
            if _e384 {
                break;
            }
            let _e387 = Src.member[_e86.x];
            if (_e387.lifetime <= 0f) {
                Dst.member[_e86.x] = _e387;
                phi_440_ = 0u;
                loop {
                    let _e402 = phi_440_;
                    if (_e402 < 16u) {
                        continue;
                    } else {
                        break;
                    }
                    continuing {
                        let _e404 = (_e92 + _e402);
                        let _e407 = TrailSrc.member[_e404];
                        TrailDst.member[_e404] = _e407;
                        phi_440_ = (_e402 + 1u);
                    }
                }
                break;
            }
            let _e412 = u32((_e387.effect + 0.5f));
            let _e416 = Effects.member[_e412].gravity;
            let _e418 = Effects.member[_e412].drag;
            let _e420 = Effects.member[_e412].noiseAmount;
            let _e422 = Effects.member[_e412].noiseScale;
            let _e424 = Effects.member[_e412].collisionMode;
            let _e426 = Effects.member[_e412].bounciness;
            let _e428 = Effects.member[_e412].trailSeconds;
            let _e430 = Params.Dt;
            let _e431 = (_e387.age + _e430);
            if (_e431 >= (_e387.lifetime + _e428)) {
                Dst.member[_e86.x] = Particle(_e387.pos, _e387.vel, _e431, 0f, _e387.seed, _e387.effect, _e387.flags, _e387.trailHead, _e387.trailTimer, _e387.pad);
                phi_476_ = 0u;
                loop {
                    let _e438 = phi_476_;
                    if (_e438 < 16u) {
                        continue;
                    } else {
                        break;
                    }
                    continuing {
                        let _e440 = (_e92 + _e438);
                        let _e443 = TrailSrc.member[_e440];
                        TrailDst.member[_e440] = _e443;
                        phi_476_ = (_e438 + 1u);
                    }
                }
                break;
            }
            phi_489_ = false;
            if (_e387.flags < 0.5f) {
                phi_489_ = (_e431 < _e387.lifetime);
            }
            let _e450 = phi_489_;
            phi_1088_ = _e387.vel;
            phi_1089_ = _e387.lifetime;
            phi_1090_ = _e387.flags;
            phi_1091_ = _e387.pos;
            if _e450 {
                let _e452 = u32((_e424 + 0.5f));
                let _e455 = Params.Time;
                let _e460 = (((_e387.pos * _e422) + vec2((_e455 * 0.5f))) + vec2(_e387.seed));
                let _e461 = (_e460 + vec2<f32>(0f, 0.1f));
                let _e462 = floor(_e461);
                let _e463 = fract(_e461);
                let _e467 = ((_e463 * _e463) * (vec2<f32>(3f, 3f) - (_e463 * 2f)));
                let _e469 = fract((_e462 * vec2<f32>(123.34f, 345.45f)));
                let _e473 = (_e469 + vec2(dot(_e469, (_e469 + vec2<f32>(34.345f, 34.345f)))));
                let _e480 = fract(((_e462 + vec2<f32>(1f, 0f)) * vec2<f32>(123.34f, 345.45f)));
                let _e484 = (_e480 + vec2(dot(_e480, (_e480 + vec2<f32>(34.345f, 34.345f)))));
                let _e491 = fract(((_e462 + vec2<f32>(0f, 1f)) * vec2<f32>(123.34f, 345.45f)));
                let _e495 = (_e491 + vec2(dot(_e491, (_e491 + vec2<f32>(34.345f, 34.345f)))));
                let _e502 = fract(((_e462 + vec2<f32>(1f, 1f)) * vec2<f32>(123.34f, 345.45f)));
                let _e506 = (_e502 + vec2(dot(_e502, (_e502 + vec2<f32>(34.345f, 34.345f)))));
                let _e516 = (_e460 - vec2<f32>(0f, 0.1f));
                let _e517 = floor(_e516);
                let _e518 = fract(_e516);
                let _e522 = ((_e518 * _e518) * (vec2<f32>(3f, 3f) - (_e518 * 2f)));
                let _e524 = fract((_e517 * vec2<f32>(123.34f, 345.45f)));
                let _e528 = (_e524 + vec2(dot(_e524, (_e524 + vec2<f32>(34.345f, 34.345f)))));
                let _e535 = fract(((_e517 + vec2<f32>(1f, 0f)) * vec2<f32>(123.34f, 345.45f)));
                let _e539 = (_e535 + vec2(dot(_e535, (_e535 + vec2<f32>(34.345f, 34.345f)))));
                let _e546 = fract(((_e517 + vec2<f32>(0f, 1f)) * vec2<f32>(123.34f, 345.45f)));
                let _e550 = (_e546 + vec2(dot(_e546, (_e546 + vec2<f32>(34.345f, 34.345f)))));
                let _e557 = fract(((_e517 + vec2<f32>(1f, 1f)) * vec2<f32>(123.34f, 345.45f)));
                let _e561 = (_e557 + vec2(dot(_e557, (_e557 + vec2<f32>(34.345f, 34.345f)))));
                let _e571 = (_e460 + vec2<f32>(0.1f, 0f));
                let _e572 = floor(_e571);
                let _e573 = fract(_e571);
                let _e577 = ((_e573 * _e573) * (vec2<f32>(3f, 3f) - (_e573 * 2f)));
                let _e579 = fract((_e572 * vec2<f32>(123.34f, 345.45f)));
                let _e583 = (_e579 + vec2(dot(_e579, (_e579 + vec2<f32>(34.345f, 34.345f)))));
                let _e590 = fract(((_e572 + vec2<f32>(1f, 0f)) * vec2<f32>(123.34f, 345.45f)));
                let _e594 = (_e590 + vec2(dot(_e590, (_e590 + vec2<f32>(34.345f, 34.345f)))));
                let _e601 = fract(((_e572 + vec2<f32>(0f, 1f)) * vec2<f32>(123.34f, 345.45f)));
                let _e605 = (_e601 + vec2(dot(_e601, (_e601 + vec2<f32>(34.345f, 34.345f)))));
                let _e612 = fract(((_e572 + vec2<f32>(1f, 1f)) * vec2<f32>(123.34f, 345.45f)));
                let _e616 = (_e612 + vec2(dot(_e612, (_e612 + vec2<f32>(34.345f, 34.345f)))));
                let _e626 = (_e460 - vec2<f32>(0.1f, 0f));
                let _e627 = floor(_e626);
                let _e628 = fract(_e626);
                let _e632 = ((_e628 * _e628) * (vec2<f32>(3f, 3f) - (_e628 * 2f)));
                let _e634 = fract((_e627 * vec2<f32>(123.34f, 345.45f)));
                let _e638 = (_e634 + vec2(dot(_e634, (_e634 + vec2<f32>(34.345f, 34.345f)))));
                let _e645 = fract(((_e627 + vec2<f32>(1f, 0f)) * vec2<f32>(123.34f, 345.45f)));
                let _e649 = (_e645 + vec2(dot(_e645, (_e645 + vec2<f32>(34.345f, 34.345f)))));
                let _e656 = fract(((_e627 + vec2<f32>(0f, 1f)) * vec2<f32>(123.34f, 345.45f)));
                let _e660 = (_e656 + vec2(dot(_e656, (_e656 + vec2<f32>(34.345f, 34.345f)))));
                let _e667 = fract(((_e627 + vec2<f32>(1f, 1f)) * vec2<f32>(123.34f, 345.45f)));
                let _e671 = (_e667 + vec2(dot(_e667, (_e667 + vec2<f32>(34.345f, 34.345f)))));
                let _e688 = (_e387.vel + ((vec2<f32>(((mix(mix(fract((_e473.x * _e473.y)), fract((_e484.x * _e484.y)), _e467.x), mix(fract((_e495.x * _e495.y)), fract((_e506.x * _e506.y)), _e467.x), _e467.y) - mix(mix(fract((_e528.x * _e528.y)), fract((_e539.x * _e539.y)), _e522.x), mix(fract((_e550.x * _e550.y)), fract((_e561.x * _e561.y)), _e522.x), _e522.y)) * 5f), ((mix(mix(fract((_e583.x * _e583.y)), fract((_e594.x * _e594.y)), _e577.x), mix(fract((_e605.x * _e605.y)), fract((_e616.x * _e616.y)), _e577.x), _e577.y) - mix(mix(fract((_e638.x * _e638.y)), fract((_e649.x * _e649.y)), _e632.x), mix(fract((_e660.x * _e660.y)), fract((_e671.x * _e671.y)), _e632.x), _e632.y)) * -5f)) * _e420) * _e430));
                let _e698 = (vec2<f32>(_e688.x, (_e688.y + (_e416 * _e430))) * clamp((1f - (_e418 * _e430)), 0f, 1f));
                let _e701 = Fields.member[_e412];
                phi_855_ = _e698;
                phi_856_ = _e387.pos;
                if (_e701.enable_ > 0.5f) {
                    let _e716 = (_e387.pos - vec2<f32>(_e701.originX, _e701.originY));
                    let _e723 = (vec2<f32>((_e716.x * _e701.flip), _e716.y) - vec2<f32>(_e701.rectLocalX, _e701.rectLocalY));
                    phi_768_ = false;
                    if (_e723.x >= 1f) {
                        phi_768_ = (_e723.y >= 1f);
                    }
                    let _e729 = phi_768_;
                    phi_773_ = false;
                    if _e729 {
                        phi_773_ = (_e723.x <= (_e701.rectW - 2f));
                    }
                    let _e733 = phi_773_;
                    phi_779_ = false;
                    if _e733 {
                        phi_779_ = (_e723.y <= (_e701.rectH - 2f));
                    }
                    let _e738 = phi_779_;
                    phi_853_ = _e698;
                    phi_854_ = _e387.pos;
                    if _e738 {
                        let _e741 = Params.SdfAtlasW;
                        let _e743 = Params.SdfAtlasH;
                        let _e744 = vec2<f32>(_e741, _e743);
                        let _e745 = (vec2<f32>(_e701.srcX, _e701.srcY) + _e723);
                        let _e747 = textureSampleLevel(SdfAtlas, SdfSampler, (_e745 / _e744), 0f);
                        let _e749 = (_e747.x * 255f);
                        let _e752 = textureSampleLevel(SdfAtlas, SdfSampler, ((_e745 + vec2<f32>(1f, 0f)) / _e744), 0f);
                        let _e757 = textureSampleLevel(SdfAtlas, SdfSampler, ((_e745 + vec2<f32>(0f, 1f)) / _e744), 0f);
                        let _e762 = vec2<f32>(((_e752.x * 255f) - _e749), ((_e757.x * 255f) - _e749));
                        phi_851_ = _e698;
                        phi_852_ = _e387.pos;
                        if (dot(_e762, _e762) > 0.000001f) {
                            let _e765 = normalize(_e762);
                            let _e767 = (_e765.x * _e701.flip);
                            let _e770 = vec2<f32>(_e767, _e765.y);
                            let _e776 = (_e698 - (_e770 * (dot(_e698, _e770) * clamp((8f * _e430), 0f, 1f))));
                            phi_850_ = _e776;
                            if (_e701.flow != 0f) {
                                let _e788 = (vec2<f32>(-(_e765.y), _e767) * _e701.flip);
                                phi_850_ = (_e776 + (_e788 * ((_e701.flow - dot(_e776, _e788)) * clamp((10f * _e430), 0f, 1f))));
                            }
                            let _e797 = phi_850_;
                            phi_851_ = _e797;
                            phi_852_ = (_e387.pos - (_e770 * (clamp((_e749 - 1.5f), -3f, 3f) * clamp((_e701.attraction * _e430), 0f, 1f))));
                        }
                        let _e799 = phi_851_;
                        let _e801 = phi_852_;
                        phi_853_ = _e799;
                        phi_854_ = _e801;
                    }
                    let _e803 = phi_853_;
                    let _e805 = phi_854_;
                    phi_855_ = _e803;
                    phi_856_ = _e805;
                }
                let _e807 = phi_855_;
                let _e809 = phi_856_;
                let _e811 = (_e809 + (_e807 * _e430));
                phi_907_ = false;
                if (_e452 > 0u) {
                    switch bitcast<i32>(0u) {
                        default: {
                            let _e815 = Params.CameraX;
                            let _e817 = Params.CameraY;
                            let _e821 = Params.Zoom;
                            let _e824 = Params.TargetW;
                            let _e826 = Params.TargetH;
                            let _e828 = (((_e811 - vec2<f32>(_e815, _e817)) * _e821) / vec2<f32>(_e824, _e826));
                            phi_885_ = true;
                            if !((_e828.x < 0f)) {
                                phi_885_ = (_e828.x > 1f);
                            }
                            let _e834 = phi_885_;
                            phi_891_ = true;
                            if !(_e834) {
                                phi_891_ = (_e828.y < 0f);
                            }
                            let _e839 = phi_891_;
                            phi_897_ = true;
                            if !(_e839) {
                                phi_897_ = (_e828.y > 1f);
                            }
                            let _e844 = phi_897_;
                            if _e844 {
                                phi_905_ = 0f;
                                break;
                            }
                            let _e845 = textureSampleLevel(SolidMask, SolidSampler, _e828, 0f);
                            phi_905_ = _e845.x;
                            break;
                        }
                    }
                    let _e848 = phi_905_;
                    phi_907_ = (_e848 > 0.5f);
                }
                let _e851 = phi_907_;
                if _e851 {
                    if (_e452 == 1u) {
                        phi_1080_ = _e807;
                        phi_1081_ = min(_e387.lifetime, _e431);
                        phi_1082_ = _e387.flags;
                    } else {
                        let _e854 = (_e452 == 2u);
                        if _e854 {
                            phi_1078_ = vec2<f32>(0f, 0f);
                        } else {
                            switch bitcast<i32>(0u) {
                                default: {
                                    switch bitcast<i32>(0u) {
                                        default: {
                                            let _e859 = Params.CameraX;
                                            let _e861 = Params.CameraY;
                                            let _e862 = vec2<f32>(_e859, _e861);
                                            let _e865 = Params.Zoom;
                                            let _e868 = Params.TargetW;
                                            let _e870 = Params.TargetH;
                                            let _e871 = vec2<f32>(_e868, _e870);
                                            let _e872 = ((((_e811 - vec2<f32>(2f, 0f)) - _e862) * _e865) / _e871);
                                            phi_946_ = true;
                                            local = _e862;
                                            local_1 = _e865;
                                            local_2 = _e871;
                                            local_3 = _e862;
                                            local_4 = _e865;
                                            local_5 = _e871;
                                            local_6 = _e862;
                                            local_7 = _e865;
                                            local_8 = _e871;
                                            if !((_e872.x < 0f)) {
                                                phi_946_ = (_e872.x > 1f);
                                            }
                                            let _e878 = phi_946_;
                                            phi_952_ = true;
                                            if !(_e878) {
                                                phi_952_ = (_e872.y < 0f);
                                            }
                                            let _e883 = phi_952_;
                                            phi_958_ = true;
                                            if !(_e883) {
                                                phi_958_ = (_e872.y > 1f);
                                            }
                                            let _e888 = phi_958_;
                                            if _e888 {
                                                phi_966_ = 0f;
                                                break;
                                            }
                                            let _e889 = textureSampleLevel(SolidMask, SolidSampler, _e872, 0f);
                                            phi_966_ = _e889.x;
                                            break;
                                        }
                                    }
                                    let _e892 = phi_966_;
                                    switch bitcast<i32>(0u) {
                                        default: {
                                            let _e896 = local;
                                            let _e899 = local_1;
                                            let _e902 = local_2;
                                            let _e903 = ((((_e811 + vec2<f32>(2f, 0f)) - _e896) * _e899) / _e902);
                                            phi_979_ = true;
                                            if !((_e903.x < 0f)) {
                                                phi_979_ = (_e903.x > 1f);
                                            }
                                            let _e909 = phi_979_;
                                            phi_985_ = true;
                                            if !(_e909) {
                                                phi_985_ = (_e903.y < 0f);
                                            }
                                            let _e914 = phi_985_;
                                            phi_991_ = true;
                                            if !(_e914) {
                                                phi_991_ = (_e903.y > 1f);
                                            }
                                            let _e919 = phi_991_;
                                            if _e919 {
                                                phi_999_ = 0f;
                                                break;
                                            }
                                            let _e920 = textureSampleLevel(SolidMask, SolidSampler, _e903, 0f);
                                            phi_999_ = _e920.x;
                                            break;
                                        }
                                    }
                                    let _e923 = phi_999_;
                                    switch bitcast<i32>(0u) {
                                        default: {
                                            let _e927 = local_3;
                                            let _e930 = local_4;
                                            let _e933 = local_5;
                                            let _e934 = ((((_e811 - vec2<f32>(0f, 2f)) - _e927) * _e930) / _e933);
                                            phi_1012_ = true;
                                            if !((_e934.x < 0f)) {
                                                phi_1012_ = (_e934.x > 1f);
                                            }
                                            let _e940 = phi_1012_;
                                            phi_1018_ = true;
                                            if !(_e940) {
                                                phi_1018_ = (_e934.y < 0f);
                                            }
                                            let _e945 = phi_1018_;
                                            phi_1024_ = true;
                                            if !(_e945) {
                                                phi_1024_ = (_e934.y > 1f);
                                            }
                                            let _e950 = phi_1024_;
                                            if _e950 {
                                                phi_1032_ = 0f;
                                                break;
                                            }
                                            let _e951 = textureSampleLevel(SolidMask, SolidSampler, _e934, 0f);
                                            phi_1032_ = _e951.x;
                                            break;
                                        }
                                    }
                                    let _e954 = phi_1032_;
                                    switch bitcast<i32>(0u) {
                                        default: {
                                            let _e958 = local_6;
                                            let _e961 = local_7;
                                            let _e964 = local_8;
                                            let _e965 = ((((_e811 + vec2<f32>(0f, 2f)) - _e958) * _e961) / _e964);
                                            phi_1045_ = true;
                                            if !((_e965.x < 0f)) {
                                                phi_1045_ = (_e965.x > 1f);
                                            }
                                            let _e971 = phi_1045_;
                                            phi_1051_ = true;
                                            if !(_e971) {
                                                phi_1051_ = (_e965.y < 0f);
                                            }
                                            let _e976 = phi_1051_;
                                            phi_1057_ = true;
                                            if !(_e976) {
                                                phi_1057_ = (_e965.y > 1f);
                                            }
                                            let _e981 = phi_1057_;
                                            if _e981 {
                                                phi_1065_ = 0f;
                                                break;
                                            }
                                            let _e982 = textureSampleLevel(SolidMask, SolidSampler, _e965, 0f);
                                            phi_1065_ = _e982.x;
                                            break;
                                        }
                                    }
                                    let _e985 = phi_1065_;
                                    let _e988 = vec2<f32>((_e923 - _e892), (_e985 - _e954));
                                    if (dot(_e988, _e988) < 0.000001f) {
                                        phi_1075_ = vec2<f32>(0f, -1f);
                                        break;
                                    }
                                    phi_1075_ = -(normalize(_e988));
                                    break;
                                }
                            }
                            let _e994 = phi_1075_;
                            phi_1078_ = (reflect(_e807, _e994) * _e426);
                        }
                        let _e998 = phi_1078_;
                        phi_1080_ = _e998;
                        phi_1081_ = _e387.lifetime;
                        phi_1082_ = select(_e387.flags, 1f, _e854);
                    }
                    let _e1001 = phi_1080_;
                    let _e1003 = phi_1081_;
                    let _e1005 = phi_1082_;
                    phi_1083_ = _e1001;
                    phi_1084_ = _e1003;
                    phi_1085_ = _e1005;
                } else {
                    phi_1083_ = _e807;
                    phi_1084_ = _e387.lifetime;
                    phi_1085_ = _e387.flags;
                }
                let _e1007 = phi_1083_;
                let _e1009 = phi_1084_;
                let _e1011 = phi_1085_;
                phi_1088_ = _e1007;
                phi_1089_ = _e1009;
                phi_1090_ = _e1011;
                phi_1091_ = select(_e811, _e809, vec2(_e851));
            }
            let _e1015 = phi_1088_;
            let _e1017 = phi_1089_;
            let _e1019 = phi_1090_;
            let _e1021 = phi_1091_;
            phi_1093_ = 0u;
            loop {
                let _e1023 = phi_1093_;
                if (_e1023 < 16u) {
                    continue;
                } else {
                    break;
                }
                continuing {
                    let _e1025 = (_e92 + _e1023);
                    let _e1028 = TrailSrc.member[_e1025];
                    TrailDst.member[_e1025] = _e1028;
                    phi_1093_ = (_e1023 + 1u);
                }
            }
            phi_1121_ = _e387.trailHead;
            phi_1122_ = _e387.trailTimer;
            if (_e428 > 0f) {
                let _e1033 = (_e428 * 0.06666667f);
                let _e1035 = u32((_e387.trailHead + 0.5f));
                let _e1036 = (_e387.trailTimer + _e430);
                phi_1116_ = _e387.trailHead;
                phi_1117_ = _e1036;
                phi_1118_ = _e1035;
                if (_e1036 >= _e1033) {
                    let _e1040 = ((_e1035 + 1u) % 16u);
                    phi_1116_ = f32(_e1040);
                    phi_1117_ = (_e1036 % _e1033);
                    phi_1118_ = _e1040;
                }
                let _e1043 = phi_1116_;
                let _e1045 = phi_1117_;
                let _e1047 = phi_1118_;
                TrailDst.member[(_e92 + _e1047)] = _e1021;
                phi_1121_ = _e1043;
                phi_1122_ = _e1045;
            }
            let _e1052 = phi_1121_;
            let _e1054 = phi_1122_;
            Dst.member[_e86.x] = Particle(_e1021, _e1015, _e431, _e1017, _e387.seed, _e387.effect, _e1019, _e1052, _e1054, _e387.pad);
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
