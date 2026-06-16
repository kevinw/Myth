struct Particle {
    pos: vec2<f32>,
    vel: vec2<f32>,
    age: f32,
    lifetime: f32,
    seed: f32,
    pad: f32,
}

struct type_u002e_StructuredBuffer_u002e_Particle {
    member: array<Particle>,
}

struct type_u002e_RWStructuredBuffer_u002e_Particle {
    member: array<Particle>,
}

struct type_u002e_Params {
    Count: u32,
    Dt: f32,
    Time: f32,
    Gravity: f32,
    NoiseAmount: f32,
    NoiseScale: f32,
    Drag: f32,
    _pad0_: f32,
    EmitStart: u32,
    EmitCount: u32,
    EmitSeed: u32,
    OriginX: f32,
    OriginY: f32,
    SpeedMin: f32,
    SpeedMax: f32,
    _pad1_: f32,
    LifeMin: f32,
    LifeMax: f32,
    _pad2_: f32,
    _pad3_: f32,
}

@group(0) @binding(0) 
var<storage> Src: type_u002e_StructuredBuffer_u002e_Particle;
@group(1) @binding(0) 
var<storage, read_write> Dst: type_u002e_RWStructuredBuffer_u002e_Particle;
@group(2) @binding(0) 
var<uniform> Params: type_u002e_Params;
var<private> global: vec3<u32>;

fn compute_main_1() {
    var phi_102_: bool;

    let _e58 = global;
    switch bitcast<i32>(0u) {
        default: {
            let _e62 = Params.Count;
            if (_e58.x >= _e62) {
                break;
            }
            let _e66 = Params.EmitStart;
            let _e70 = Params.EmitCount;
            phi_102_ = false;
            if (_e70 > 0u) {
                phi_102_ = ((((_e58.x + _e62) - _e66) % _e62) < _e70);
            }
            let _e74 = phi_102_;
            if _e74 {
                let _e77 = Params.EmitSeed;
                let _e78 = ((_e58.x * 7u) + _e77);
                let _e83 = (((_e78 ^ 61u) ^ (_e78 >> bitcast<u32>(16u))) * 9u);
                let _e87 = ((_e83 ^ (_e83 >> bitcast<u32>(4u))) * 668265261u);
                let _e93 = (f32(((_e87 ^ (_e87 >> bitcast<u32>(15u))) & 16777215u)) * 0.00000037450704f);
                let _e95 = Params.SpeedMin;
                let _e97 = Params.SpeedMax;
                let _e100 = (((_e58.x * 13u) + _e77) + 1u);
                let _e105 = (((_e100 ^ 61u) ^ (_e100 >> bitcast<u32>(16u))) * 9u);
                let _e109 = ((_e105 ^ (_e105 >> bitcast<u32>(4u))) * 668265261u);
                let _e118 = Params.OriginX;
                let _e120 = Params.OriginY;
                let _e127 = Params.LifeMin;
                let _e129 = Params.LifeMax;
                let _e132 = (((_e58.x * 17u) + _e77) + 2u);
                let _e137 = (((_e132 ^ 61u) ^ (_e132 >> bitcast<u32>(16u))) * 9u);
                let _e141 = ((_e137 ^ (_e137 >> bitcast<u32>(4u))) * 668265261u);
                let _e151 = (((_e58.x * 19u) + _e77) + 3u);
                let _e156 = (((_e151 ^ 61u) ^ (_e151 >> bitcast<u32>(16u))) * 9u);
                let _e160 = ((_e156 ^ (_e156 >> bitcast<u32>(4u))) * 668265261u);
                Dst.member[_e58.x] = Particle(vec2<f32>(_e118, _e120), (vec2<f32>(cos(_e93), sin(_e93)) * mix(_e95, _e97, (f32(((_e109 ^ (_e109 >> bitcast<u32>(15u))) & 16777215u)) * 0.000000059604645f))), 0f, mix(_e127, _e129, (f32(((_e141 ^ (_e141 >> bitcast<u32>(15u))) & 16777215u)) * 0.000000059604645f)), (f32(((_e160 ^ (_e160 >> bitcast<u32>(15u))) & 16777215u)) * 0.000059604645f), 0f);
                break;
            }
            let _e172 = Src.member[_e58.x];
            if (_e172.lifetime <= 0f) {
                Dst.member[_e58.x] = _e172;
                break;
            }
            let _e183 = Params.Dt;
            let _e184 = (_e172.age + _e183);
            if (_e184 >= _e172.lifetime) {
                Dst.member[_e58.x] = Particle(_e172.pos, _e172.vel, _e184, 0f, _e172.seed, _e172.pad);
                break;
            }
            let _e190 = Params.NoiseScale;
            let _e193 = Params.Time;
            let _e198 = (((_e172.pos * _e190) + vec2((_e193 * 0.5f))) + vec2(_e172.seed));
            let _e199 = (_e198 + vec2<f32>(0f, 0.1f));
            let _e200 = floor(_e199);
            let _e201 = fract(_e199);
            let _e205 = ((_e201 * _e201) * (vec2<f32>(3f, 3f) - (_e201 * 2f)));
            let _e207 = fract((_e200 * vec2<f32>(123.34f, 345.45f)));
            let _e211 = (_e207 + vec2(dot(_e207, (_e207 + vec2<f32>(34.345f, 34.345f)))));
            let _e218 = fract(((_e200 + vec2<f32>(1f, 0f)) * vec2<f32>(123.34f, 345.45f)));
            let _e222 = (_e218 + vec2(dot(_e218, (_e218 + vec2<f32>(34.345f, 34.345f)))));
            let _e229 = fract(((_e200 + vec2<f32>(0f, 1f)) * vec2<f32>(123.34f, 345.45f)));
            let _e233 = (_e229 + vec2(dot(_e229, (_e229 + vec2<f32>(34.345f, 34.345f)))));
            let _e240 = fract(((_e200 + vec2<f32>(1f, 1f)) * vec2<f32>(123.34f, 345.45f)));
            let _e244 = (_e240 + vec2(dot(_e240, (_e240 + vec2<f32>(34.345f, 34.345f)))));
            let _e254 = (_e198 - vec2<f32>(0f, 0.1f));
            let _e255 = floor(_e254);
            let _e256 = fract(_e254);
            let _e260 = ((_e256 * _e256) * (vec2<f32>(3f, 3f) - (_e256 * 2f)));
            let _e262 = fract((_e255 * vec2<f32>(123.34f, 345.45f)));
            let _e266 = (_e262 + vec2(dot(_e262, (_e262 + vec2<f32>(34.345f, 34.345f)))));
            let _e273 = fract(((_e255 + vec2<f32>(1f, 0f)) * vec2<f32>(123.34f, 345.45f)));
            let _e277 = (_e273 + vec2(dot(_e273, (_e273 + vec2<f32>(34.345f, 34.345f)))));
            let _e284 = fract(((_e255 + vec2<f32>(0f, 1f)) * vec2<f32>(123.34f, 345.45f)));
            let _e288 = (_e284 + vec2(dot(_e284, (_e284 + vec2<f32>(34.345f, 34.345f)))));
            let _e295 = fract(((_e255 + vec2<f32>(1f, 1f)) * vec2<f32>(123.34f, 345.45f)));
            let _e299 = (_e295 + vec2(dot(_e295, (_e295 + vec2<f32>(34.345f, 34.345f)))));
            let _e309 = (_e198 + vec2<f32>(0.1f, 0f));
            let _e310 = floor(_e309);
            let _e311 = fract(_e309);
            let _e315 = ((_e311 * _e311) * (vec2<f32>(3f, 3f) - (_e311 * 2f)));
            let _e317 = fract((_e310 * vec2<f32>(123.34f, 345.45f)));
            let _e321 = (_e317 + vec2(dot(_e317, (_e317 + vec2<f32>(34.345f, 34.345f)))));
            let _e328 = fract(((_e310 + vec2<f32>(1f, 0f)) * vec2<f32>(123.34f, 345.45f)));
            let _e332 = (_e328 + vec2(dot(_e328, (_e328 + vec2<f32>(34.345f, 34.345f)))));
            let _e339 = fract(((_e310 + vec2<f32>(0f, 1f)) * vec2<f32>(123.34f, 345.45f)));
            let _e343 = (_e339 + vec2(dot(_e339, (_e339 + vec2<f32>(34.345f, 34.345f)))));
            let _e350 = fract(((_e310 + vec2<f32>(1f, 1f)) * vec2<f32>(123.34f, 345.45f)));
            let _e354 = (_e350 + vec2(dot(_e350, (_e350 + vec2<f32>(34.345f, 34.345f)))));
            let _e364 = (_e198 - vec2<f32>(0.1f, 0f));
            let _e365 = floor(_e364);
            let _e366 = fract(_e364);
            let _e370 = ((_e366 * _e366) * (vec2<f32>(3f, 3f) - (_e366 * 2f)));
            let _e372 = fract((_e365 * vec2<f32>(123.34f, 345.45f)));
            let _e376 = (_e372 + vec2(dot(_e372, (_e372 + vec2<f32>(34.345f, 34.345f)))));
            let _e383 = fract(((_e365 + vec2<f32>(1f, 0f)) * vec2<f32>(123.34f, 345.45f)));
            let _e387 = (_e383 + vec2(dot(_e383, (_e383 + vec2<f32>(34.345f, 34.345f)))));
            let _e394 = fract(((_e365 + vec2<f32>(0f, 1f)) * vec2<f32>(123.34f, 345.45f)));
            let _e398 = (_e394 + vec2(dot(_e394, (_e394 + vec2<f32>(34.345f, 34.345f)))));
            let _e405 = fract(((_e365 + vec2<f32>(1f, 1f)) * vec2<f32>(123.34f, 345.45f)));
            let _e409 = (_e405 + vec2(dot(_e405, (_e405 + vec2<f32>(34.345f, 34.345f)))));
            let _e425 = Params.NoiseAmount;
            let _e428 = (_e172.vel + ((vec2<f32>(((mix(mix(fract((_e211.x * _e211.y)), fract((_e222.x * _e222.y)), _e205.x), mix(fract((_e233.x * _e233.y)), fract((_e244.x * _e244.y)), _e205.x), _e205.y) - mix(mix(fract((_e266.x * _e266.y)), fract((_e277.x * _e277.y)), _e260.x), mix(fract((_e288.x * _e288.y)), fract((_e299.x * _e299.y)), _e260.x), _e260.y)) * 5f), ((mix(mix(fract((_e321.x * _e321.y)), fract((_e332.x * _e332.y)), _e315.x), mix(fract((_e343.x * _e343.y)), fract((_e354.x * _e354.y)), _e315.x), _e315.y) - mix(mix(fract((_e376.x * _e376.y)), fract((_e387.x * _e387.y)), _e370.x), mix(fract((_e398.x * _e398.y)), fract((_e409.x * _e409.y)), _e370.x), _e370.y)) * -5f)) * _e425) * _e183));
            let _e430 = Params.Gravity;
            let _e438 = Params.Drag;
            let _e442 = (vec2<f32>(_e428.x, (_e428.y + (_e430 * _e183))) * clamp((1f - (_e438 * _e183)), 0f, 1f));
            Dst.member[_e58.x] = Particle((_e172.pos + (_e442 * _e183)), _e442, _e184, _e172.lifetime, _e172.seed, _e172.pad);
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
