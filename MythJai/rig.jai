// The humanoid rig Myth draws its player with (HumanoidRigVisual, BoneBody): Myth's ozz clips,
// played as the takes in Assets/Rigs/ual2.takes.json and cross-faded between, posed at the
// rig's own frame rate, turned to face the actor, projected flat, and drawn as depth-ordered
// tapered capsules over a silhouette outline.
// The costume (Assets/Sources/Costumes) wraps bands, studs and spikes onto bones by tag, and
// trousers over the legs (HumanoidRigVisual.Garment), depth-sorted in among the bones.
// The pose leans into speed and acceleration (UpdateVelocityLean) and squashes with the actor.
// Not ported yet: foot IK, turns and the other pose modifiers, secondary motion, ragdoll,
// kilts, the trousers' cloth swing (their hems hang at rest), costume surfaces, the sculpted
// head (a circle stands in), hands' fingers, skin modes, normals.

Rig_Pose :: struct {
    take: s32 = -1;
    time: float;
    previous: s32 = -1;         // the take being faded out of
    previous_time: float;
    blend := 1.0;               // 0..1 from previous toward take
    step: float;                // time since the last posed frame
    posed: bool;
    instance: *void;
    projected: [MAX_JOINTS] Vector2;    // rig-local pixels from the actor's position
    depth: [MAX_JOINTS] float;          // meters toward the camera
    lean, lean_rate: float;             // radians, a spring
    previous_velocity_x: float;
}

// The settings the player's rig uses: Myth's HumanoidRigSettings defaults, with
// Assets/Actors/humanoid.json and player.json applied.
RIG :: struct {
    PIXELS_PER_METER :: 14.0;
    VISUAL_FPS :: 24.0;
    BLEND_TIME :: 0.12;
    HEAD_SIZE :: 0.074;
    NECK_EXTENSION :: 0.054;
    TORSO :: 0.141; CHEST :: 0.116; NECK :: 0.25; BICEP :: 0.083; FOREARM :: 0.101; THIGH :: 0.125; SHIN :: 0.063; FOOT :: 0.07;
    OUTLINE :: Color.{0, 0, 0, 0xaa};
    FRONT_LIMB_OUTLINE :: 0.35;
    FRONT_LIMB_DEPTH :: 0.05;
    VELOCITY_LEAN :: 0.6;
    SQUISH_RESPONSE :: 0.6;
};

// What tells one rigged actor from another: its palette and costume (Assets/Actors/<name>.json).
Rig_Look :: struct {
    far := #run rgb(0x435263);
    near := #run rgb(0xb7cdd0);
    core := #run rgb(0x708b94);
    costume: string;
}
PLAYER_LOOK :: Rig_Look.{costume = "brass-wanderer"};
BUD_LOOK :: Rig_Look.{far = #run rgb(0x45648c), near = #run rgb(0xabcfff), core = #run rgb(0x7fb6ff), costume = "bud-crest"};

// Which take a pawn animation plays (Myth's Pawn.RigClipFor).
take_for :: (animation: string) -> string {
    if animation == {
      case "skid";                return "PlayerSkid";
      case "idle"; #through;
      case "look_up";             return "PlayerIdle";
      case "wall_slide";          return "PlayerWallSlide";
      case "walk";                return "Walk_Loop";
      case "run"; #through;
      case "run_start";           return "PlayerRun";
      case "duck";                return "Crouch_Idle_Loop";
      case "crawl";               return "PlayerCrawl";
      case "roll";                return "PlayerRoll";
      case "hurt";                return "Hit_Knockback";
    }
    if begins_with(animation, "jump") || animation == "dive" return "PlayerAir";
    if contains(animation, "attack") return "PlayerAttack";
    return "PlayerIdle";
}

// Plays `animation`'s take, fading from what was playing. `restart` replays it if it's already playing.
play_rig :: (pose: *Rig_Pose, animation: string, restart := false) {
    if !rig_ready() return;
    take := find_take(take_for(animation));
    if take < 0 || take == pose.take && !restart return;
    if pose.take >= 0 && take != pose.take { pose.previous, pose.previous_time, pose.blend = pose.take, pose.time, 0; }
    pose.take = take;
    pose.time = rig.takes[take].start;
}

// Advances the takes; poses and projects a new frame at the rig's frame rate, turned to face
// the actor's way, leaning with its motion and squashed with it.
update_rig :: (pose: *Rig_Pose, a: *Actor, dt: float) {
    if !rig_ready() || pose.take < 0 return;
    pose.time = advance_take(pose.take, pose.time, dt);
    if pose.previous >= 0 pose.previous_time = advance_take(pose.previous, pose.previous_time, dt);
    pose.blend = min(1.0, pose.blend + dt / RIG.BLEND_TIME);
    pose.step += dt;
    if pose.posed && pose.step < 1 / RIG.VISUAL_FPS return;
    visual_delta := ifx pose.posed then pose.step else dt;
    pose.step = 0;
    pose.posed = true;

    if !pose.instance pose.instance = myth_ozz_instance_create(rig.handle);
    clips: [2] s32;
    times, weights: [2] float;
    flags: [2] u8;
    layers: s32;
    add_layer :: (take: s32, time: float, weight: float) #expand {
        if take < 0 || weight <= 0 return;
        clips[layers], times[layers], weights[layers] = rig.takes[take].clip, time, weight;
        layers += 1;
    }
    add_layer(pose.previous, pose.previous_time, 1 - pose.blend);
    add_layer(pose.take, pose.time, pose.blend);
    if pose.blend >= 1 pose.previous = -1;
    joint_weights: [MAX_JOINTS * 2] float;
    if !myth_ozz_instance_sample(pose.instance, layers, clips.data, times.data, weights.data, flags.data, flags.data, joint_weights.data, positions.data, rotations.data) return;

    // The head sits out along the neck (HeadPosition3D).
    along := positions[rig.head] - positions[rig.neck];
    if length(along) > 0.001 positions[rig.head] += unit_vector(along) * RIG.NECK_EXTENSION;
    // Facing left looks down -Z's projection; facing right turns half around (FacingYaw).
    turn := ifx a.facing > 0 then -1.0 else 1.0;
    for 0..rig.joints.count-1 {
        p := positions[it];
        turned := Vector3.{p.x * turn, p.y, p.z * turn};
        pose.projected[it] = Vector2.{-turned.z, -p.y} * RIG.PIXELS_PER_METER;
        pose.depth[it] = turned.x;
    }

    // A spring toward leaning into the speed and the change in it, the upper body turning about the pelvis.
    VELOCITY_LEAN_MAX_ANGLE :: 0.35;
    VELOCITY_LEAN_FULL_SPEED :: 280.0;
    VELOCITY_LEAN_FULL_ACCELERATION :: 900.0;
    VELOCITY_LEAN_SPRING :: 140.0;
    VELOCITY_LEAN_DAMPING :: 9.0;
    acceleration := (a.velocity.x - pose.previous_velocity_x) / visual_delta;
    pose.previous_velocity_x = a.velocity.x;
    target := clamp(a.velocity.x / VELOCITY_LEAN_FULL_SPEED + acceleration / VELOCITY_LEAN_FULL_ACCELERATION, -1, 1) * VELOCITY_LEAN_MAX_ANGLE * RIG.VELOCITY_LEAN;
    pose.lean_rate += (target - pose.lean) * VELOCITY_LEAN_SPRING * visual_delta;
    pose.lean_rate *= exp(-VELOCITY_LEAN_DAMPING * visual_delta);
    pose.lean += pose.lean_rate * visual_delta;
    pivot := pose.projected[rig.pelvis];
    c, s := cos(pose.lean), sin(pose.lean);
    for 0..rig.joints.count-1 if rig.upper_body[it] > 0 {
        v := pose.projected[it] - pivot;
        leaned := pivot + Vector2.{v.x * c - v.y * s, v.x * s + v.y * c};
        pose.projected[it] += (leaned - pose.projected[it]) * rig.upper_body[it];
    }

    // Squash and stretch about the feet (ApplySquish).
    squish := Vector2.{1, 1} + (a.squish - Vector2.{1, 1}) * RIG.SQUISH_RESPONSE;
    ground := Vector2.{pivot.x, 0};
    for 0..rig.joints.count-1 {
        p := ground + (pose.projected[it] - ground) * squish;
        pose.projected[it] = .{floor(p.x + 0.5), floor(p.y + 0.5)};  // pixel snap
    }
}

// Where the head is, from the actor's position.
rig_head :: (pose: *Rig_Pose) -> Vector2, radius: float {
    if !rig_ready() return .{}, 0;
    return pose.projected[rig.head], RIG.HEAD_SIZE * RIG.PIXELS_PER_METER;
}

draw_rig :: (pose: *Rig_Pose, at: Vector2, look: Rig_Look) {
    pieces_of_costume := costume_for(look.costume);
    if !rig_ready() || !pose.posed return;
    Piece :: struct { bone: s32; depth: float; from, to: Vector2; from_width, to_width: float; }
    pieces: [BONES.count] Piece;
    for BONES {
        from, to := rig.bone_joints[it_index][0], rig.bone_joints[it_index][1];
        width := RIG.PIXELS_PER_METER * it.size;
        parent := rig.bone_parents[it_index];
        from_width := ifx parent >= 0 then RIG.PIXELS_PER_METER * BONES[parent].size else width;  // joint continuity
        pieces[it_index] = .{xx it_index, (pose.depth[from] + pose.depth[to]) * 0.5, at + pose.projected[from], at + pose.projected[to], from_width, width};
    }
    quick_sort(pieces, (a: Piece, b: Piece) -> s64 { return ifx a.depth < b.depth then -1 else ifx a.depth > b.depth then 1 else 0; });
    head := at + pose.projected[rig.head];
    head_radius := RIG.HEAD_SIZE * RIG.PIXELS_PER_METER;
    pelvis_depth := pose.depth[rig.pelvis];
    faces: [9] Garment_Face;
    face_count := 0;
    for pieces_of_costume if it.kind == .TROUSERS { face_count = trouser_faces(pose, at, it, *faces); break; }

    // The silhouette: every piece grown a pixel, in the outline color, under everything.
    for pieces {
        capsule(it.from, it.to, joint_radius(it.from_width) + 1, joint_radius(it.to_width) + 1, joint_steps(joint_radius(it.to_width) + 1), RIG.OUTLINE, from_cap = false);
        draw_costume(it, pieces_of_costume, RIG.OUTLINE, silhouette = true);
    }
    for 0..face_count-1 draw_face(faces[it], RIG.OUTLINE, grow = 1);
    circle(head, head_radius + 1.4, 14, RIG.OUTLINE);

    head_drawn := false;
    next_face := 0;
    for pieces {
        bone := BONES[it.bone];
        while next_face < face_count && faces[next_face].depth <= it.depth { draw_face(faces[next_face], faces[next_face].color); next_face += 1; }
        if !head_drawn && pose.depth[rig.head] <= it.depth { circle(head, head_radius, 14, look.near); head_drawn = true; }
        core := bone.from == "pelvis";
        t := clamp(it.depth + 0.5, 0, 1);
        color := ifx core then look.core else mix(look.far, look.near, t);
        // Limbs in front of the body get a faint outline of their own.
        if bone.limb && it.depth - pelvis_depth > RIG.FRONT_LIMB_DEPTH
            capsule(it.from, it.to, joint_radius(it.from_width) + 1, joint_radius(it.to_width) + 1, joint_steps(joint_radius(it.to_width) + 1), fade(rgb(0), RIG.FRONT_LIMB_OUTLINE));
        capsule(it.from, it.to, joint_radius(it.from_width), joint_radius(it.to_width), joint_steps(joint_radius(it.to_width)), color);
        // Clothing darkens with depth as much as the limb under it does.
        ratio :: (far: u8, near: u8) -> u8 #expand { return xx (ifx near then min(255.0, 255.0 * far / near) else 255); }
        shade := ifx core then WHITE else mix(Color.{ratio(look.far.r, look.near.r), ratio(look.far.g, look.near.g), ratio(look.far.b, look.near.b), 255}, WHITE, t);
        draw_costume(it, pieces_of_costume, shade);
    }
    while next_face < face_count { draw_face(faces[next_face], faces[next_face].color); next_face += 1; }
    if !head_drawn circle(head, head_radius, 14, look.near);
}

#scope_file

MAX_JOINTS :: 128;
NO_END :: 3.4e38;

// Myth's RigTags: what costume pieces match.
Rig_Tags :: enum_flags u16 { TRUNK; WAIST; NECK; SHOULDER; UPPER_ARM; FOREARM; HAND; HIP; THIGH; SHIN; FOOT; TAIL; LEFT; RIGHT; }

Bone_Spec :: struct { from, to: string; size: float; limb: bool; tags: Rig_Tags; }
BONES :: Bone_Spec.[
    .{"pelvis", "spine_01", RIG.TORSO, false, .WAIST},
    .{"spine_01", "spine_02", RIG.TORSO, false, .TRUNK},
    .{"spine_02", "spine_03", RIG.TORSO, false, .TRUNK},
    .{"spine_03", "neck_01", RIG.CHEST, false, .TRUNK},
    .{"neck_01", "Head", RIG.NECK, false, .NECK},
    .{"clavicle_l", "upperarm_l", RIG.BICEP, false, .SHOULDER | .LEFT},
    .{"upperarm_l", "lowerarm_l", RIG.BICEP, true, .UPPER_ARM | .LEFT},
    .{"lowerarm_l", "hand_l", RIG.FOREARM, true, .FOREARM | .LEFT},
    .{"hand_l", "middle_01_l", RIG.FOREARM, true, .HAND | .LEFT},
    .{"clavicle_r", "upperarm_r", RIG.BICEP, false, .SHOULDER | .RIGHT},
    .{"upperarm_r", "lowerarm_r", RIG.BICEP, true, .UPPER_ARM | .RIGHT},
    .{"lowerarm_r", "hand_r", RIG.FOREARM, true, .FOREARM | .RIGHT},
    .{"hand_r", "middle_01_r", RIG.FOREARM, true, .HAND | .RIGHT},
    .{"pelvis", "thigh_l", RIG.THIGH, false, .HIP | .LEFT},
    .{"thigh_l", "calf_l", RIG.THIGH, true, .THIGH | .LEFT},
    .{"calf_l", "foot_l", RIG.SHIN, true, .SHIN | .LEFT},
    .{"foot_l", "ball_l", RIG.FOOT, true, .FOOT | .LEFT},
    .{"ball_l", "ball_leaf_l", RIG.FOOT, true, .FOOT | .LEFT},
    .{"pelvis", "thigh_r", RIG.THIGH, false, .HIP | .RIGHT},
    .{"thigh_r", "calf_r", RIG.THIGH, true, .THIGH | .RIGHT},
    .{"calf_r", "foot_r", RIG.SHIN, true, .SHIN | .RIGHT},
    .{"foot_r", "ball_r", RIG.FOOT, true, .FOOT | .RIGHT},
    .{"ball_r", "ball_leaf_r", RIG.FOOT, true, .FOOT | .RIGHT},
];

Costume_Piece :: struct {
    kind: enum u8 { SLEEVE; STUD; SPIKES; TROUSERS; };
    bones: Rig_Tags;
    from: float;
    to := 1.0;
    count := 1;
    width := 1.0;
    length := 1.0;   // spikes: how far out, in the bone's widths; negative for the other side
    flare := 0.3;    // trousers: how much wider the hem is than the shin
    color := WHITE;
}
costumes: [8] struct { name: string; pieces: [..] Costume_Piece; };

costume_for :: (name: string) -> [] Costume_Piece {
    if !name return .[];
    for * costumes if it.name == name return it.pieces;
    for * costumes if !it.name { it.name = copy_string(name); load_costume(name, *it.pieces); return it.pieces; }
    return .[];
}

// The costume's pieces on a drawn bone, in their order (RigDraw.DrawCostume, sleeves and studs).
draw_costume :: (piece: $T, costume: [] Costume_Piece, shade: Color, silhouette := false) {
    tags := BONES[piece.bone].tags;
    SIDES :: Rig_Tags.LEFT | .RIGHT;
    for c: costume {
        if !(tags & c.bones & ~SIDES) || (c.bones & SIDES) && !(tags & c.bones & SIDES) continue;
        color := ifx silhouette then shade else multiply(c.color, shade);
        for i: 0..c.count-1 {
            start := c.from + (c.to - c.from) * i / c.count;
            end := c.from + (c.to - c.from) * (i + ifx c.count == 1 then 1.0 else 0.6) / c.count;
            from := piece.from + (piece.to - piece.from) * start;
            to := piece.from + (piece.to - piece.from) * end;
            from_width := (piece.from_width + (piece.to_width - piece.from_width) * start) * c.width;
            to_width := (piece.from_width + (piece.to_width - piece.from_width) * end) * c.width;
            grow := ifx silhouette then 2.0 else 0;
            if c.kind == {
              case .SLEEVE; bone_quad(from, to, from_width + grow, to_width + grow, color);
              case .STUD;   circle((from + to) * 0.5, (from_width + to_width) * 0.25 + grow * 0.5, 10, color);
              case .SPIKES;
                along := unit_vector(piece.to - piece.from);
                side := Vector2.{along.y, -along.x} * ifx c.length < 0 then -1.0 else 1.0;
                a, b := from + side * from_width * 0.5, to + side * to_width * 0.5;
                tip := (a + b) * 0.5 + side * abs(c.length) * (from_width + to_width) * 0.5;
                if silhouette triangle(a - along, b + along, tip + side, color);
                else triangle(a, b, tip, color);
            }
        }
    }
}

load_costume :: (name: string, costume: *[..] Costume_Piece) {
    text, ok := read_entire_file(tprint("%/Sources/Costumes/%.json", ASSETS, name), log_errors = false);
    if !ok { log_error("no costume %", name); return; }
    json:, ok = parse_json(text);
    if !ok { log_error("costume % isn't valid JSON", name); return; }
    for get(json, "Pieces").items {
        piece: Costume_Piece;
        kind := as_string(get(it, "Kind"), "Sleeve");
        if kind == {
          case "Sleeve";   piece.kind = .SLEEVE;
          case "Stud";     piece.kind = .STUD;
          case "Spikes";   piece.kind = .SPIKES;
          case "Trousers"; piece.kind = .TROUSERS;
          case; continue;  // kilts aren't ported
        }
        for split(as_string(get(it, "Bones")), ",",, temp) {
            name := trim(it);
            for name_of: type_info(Rig_Tags).names if equal_nocase(replace(name_of, "_", "",, temp), name) piece.bones |= xx type_info(Rig_Tags).values[it_index];
        }
        piece.from = as_float(get(it, "From"), 0);
        piece.to = as_float(get(it, "To"), 1);
        piece.count = as_int(get(it, "Count"), 1);
        piece.width = as_float(get(it, "Width"), 1);
        piece.length = as_float(get(it, "Length"), 1);
        piece.flare = as_float(get(it, "Flare"), 0.3);
        piece.color = rgb(as_string(get(it, "Color"), "ffffff"), default = WHITE);
        array_add(costume, piece);
    }
}

bone_quad :: (from: Vector2, to: Vector2, from_width: float, to_width: float, color: Color) {
    a, b, c, d := bone_corners(from, to, from_width, to_width);
    polygon(a, b, c, d, color);
}

bone_corners :: (from: Vector2, to: Vector2, from_width: float, to_width: float) -> Vector2, Vector2, Vector2, Vector2 {
    along := to - from;
    span := length(along);
    along = ifx span > 0.0001 then along / span else Vector2.{0, 1};
    across := Vector2.{along.y, -along.x};
    return from + across * from_width * 0.5, from - across * from_width * 0.5, to - across * to_width * 0.5, to + across * to_width * 0.5;
}

Garment_Face :: struct { disc: bool; a, b, c, d: Vector2; radius: float; depth: float; color: Color; }

// AddTrouserFaces and AddTrouserSeat: a tube per leg, its knee bulging as it bends and its hem
// flaring, hung a little above the ankle so the foot shows; the far leg's a shade darker; a seat
// joins them. Sorted far to near.
trouser_faces :: (pose: *Rig_Pose, at: Vector2, trousers: Costume_Piece, faces: *[9] Garment_Face) -> int {
    TROUSER_HEM_RAISE :: 0.12;
    TROUSER_KNEE_BULGE :: 0.4;
    SEAT_DROP :: 0.35;
    count := 0;
    add :: (face: Garment_Face) #expand { faces.*[count] = face; count += 1; }
    leg_depth :: (leg: [5] s32) -> float #expand { d := 0.0; for leg d += pose.depth[it]; return d / 5; }
    back := ifx leg_depth(rig.legs[0]) < leg_depth(rig.legs[1]) then 0 else 1;
    thigh := RIG.THIGH * RIG.PIXELS_PER_METER * trousers.width;
    shin := RIG.SHIN * RIG.PIXELS_PER_METER * trousers.width;
    for leg, side: rig.legs {
        hip, knee, ankle := at + pose.projected[leg[0]], at + pose.projected[leg[1]], at + pose.projected[leg[2]];
        hem := ankle + (knee - ankle) * TROUSER_HEM_RAISE;
        hem = .{floor(hem.x + 0.5), floor(hem.y + 0.5)};
        upper, lower := knee - hip, hem - knee;
        bend := ifx dot(upper, upper) > 0.01 && dot(lower, lower) > 0.01 then abs(sin(atan2(upper.x * lower.y - upper.y * lower.x, dot(upper, lower)))) else 0.0;
        knee_width := (thigh + shin) * 0.5 + thigh * TROUSER_KNEE_BULGE * bend;
        depth := (pose.depth[leg[0]] + pose.depth[leg[1]] + pose.depth[leg[2]]) / 3 + 0.001;
        color := ifx side == back then darken(trousers.color, 0.8) else trousers.color;
        a, b, c, d := bone_corners(hip, knee, thigh, knee_width);
        add(.{false, a, b, c, d, 0, depth, color});
        a, b, c, d = bone_corners(knee, hem, knee_width, shin * (1 + trousers.flare));
        add(.{false, a, b, c, d, 0, depth, color});
        add(.{true, knee, .{}, .{}, .{}, knee_width * 0.5, depth, color});
        add(.{true, hip, .{}, .{}, .{}, thigh * 0.5, depth, color});
    }
    left, right := at + pose.projected[rig.legs[0][0]], at + pose.projected[rig.legs[1][0]];
    raise := Vector2.{0, -RIG.THIGH * RIG.PIXELS_PER_METER * 0.5};
    left_low := left + (at + pose.projected[rig.legs[0][1]] - left) * SEAT_DROP;
    right_low := right + (at + pose.projected[rig.legs[1][1]] - right) * SEAT_DROP;
    add(.{false, left + raise, right + raise, right_low, left_low, 0, min(pose.depth[rig.legs[0][0]], pose.depth[rig.legs[1][0]]) - 0.001, darken(trousers.color, 0.9)});
    quick_sort(array_view(faces.*, 0, count), (x: Garment_Face, y: Garment_Face) -> s64 { return ifx x.depth < y.depth then -1 else ifx x.depth > y.depth then 1 else 0; });
    return count;
}

// DrawGarment; `grow` nudges it a pixel each way for the outline.
draw_face :: (face: Garment_Face, color: Color, grow := 0.0) {
    if face.disc { circle(face.a, face.radius + grow, 10, color); return; }
    if grow <= 0 { polygon(face.a, face.b, face.c, face.d, color); return; }
    for Vector2.[.{1, 0}, .{-1, 0}, .{0, 1}, .{0, -1}] polygon(face.a + it * grow, face.b + it * grow, face.c + it * grow, face.d + it * grow, color);
}

darken :: (c: Color, k: float) -> Color { return .{xx (c.r * k), xx (c.g * k), xx (c.b * k), 255}; }

multiply :: (a: Color, b: Color) -> Color {
    return .{xx (cast(u32) a.r * b.r / 255), xx (cast(u32) a.g * b.g / 255), xx (cast(u32) a.b * b.b / 255), xx (cast(u32) a.a * b.a / 255)};
}

Rig_Take :: struct { name: string; clip: s32; start, end: float; loop: bool; speed: float; }

rig: struct {
    handle: *void;
    failed: bool;
    joints: [] string;
    clips: [] struct { name: string; duration: float; };
    takes: [..] Rig_Take;
    bone_joints: [BONES.count] [2] s32;
    bone_parents: [BONES.count] s32;
    pelvis, neck, head: s32;
    legs: [2][5] s32;  // left, right: hip, knee, ankle, toe, tip (Leg)
    upper_body: [MAX_JOINTS] float;  // how much each joint leans with the upper body
};
positions: [MAX_JOINTS] Vector3;
rotations: [MAX_JOINTS] Vector4;

rig_ready :: () -> bool {
    if rig.handle || rig.failed return !rig.failed;
    rig.failed = true;
    data, ok := read_entire_file(tprint("%/Rigs/ual2.ozzpack", ASSETS), log_errors = false);
    if !ok { log_error("no rig"); return false; }
    defer free(data);
    error: [512] u8;
    handle := myth_ozz_rig_load(data.data, xx data.count, error.data, error.count);
    if !handle { log_error("can't load the rig: %", to_string(error.data)); return false; }
    joint_count := myth_ozz_rig_joint_count(handle);
    if joint_count > MAX_JOINTS { log_error("the rig has % joints; MAX_JOINTS is %", joint_count, MAX_JOINTS); return false; }

    rig.joints = NewArray(joint_count, string);
    for * rig.joints it.* = copy_string(to_string(myth_ozz_rig_joint_name(handle, xx it_index)));
    rig.clips = NewArray(myth_ozz_rig_clip_count(handle), type_of(rig.clips[0]));
    for * rig.clips it.* = .{copy_string(to_string(myth_ozz_rig_clip_name(handle, xx it_index))), myth_ozz_rig_clip_duration(handle, xx it_index)};

    joint :: (name: string) -> s32 { for rig.joints if equal_nocase(it, name) return xx it_index; log_error("the rig has no joint %", name); return 0; }
    for BONES rig.bone_joints[it_index] = .[joint(it.from), joint(it.to)];
    for bone, index: BONES {
        rig.bone_parents[index] = -1;
        for BONES if it.to == bone.from { rig.bone_parents[index] = xx it_index; break; }
    }
    rig.pelvis, rig.neck, rig.head = joint("pelvis"), joint("neck_01"), joint("Head");
    for side, s: string.["l", "r"] for part, p: string.["thigh", "calf", "foot", "ball", "ball_leaf"]
        rig.legs[s][p] = joint(tprint("%_%", part, side));
    // The upper body: spine_01 and everything above it, eased in through the lower spine.
    spine_01, spine_02 := joint("spine_01"), joint("spine_02");
    for 0..joint_count-1 {
        j: s32 = xx it;
        while j >= 0 && j != spine_01 j = myth_ozz_rig_joint_parent(handle, j);
        rig.upper_body[it] = ifx j == spine_01 then 1.0 else 0.0;
    }
    rig.upper_body[spine_01], rig.upper_body[spine_02] = 0.25, 0.6;

    // Saved takes, then every raw clip as a take of its own.
    text, read := read_entire_file(tprint("%/Rigs/ual2.takes.json", ASSETS), log_errors = false);
    takes := ifx read then parse_json(text) else Json.{};
    for takes.keys {
        t := takes.items[it_index];
        clip := find_clip(as_string(get(t, "Clip")));
        if clip < 0 continue;
        array_add(*rig.takes, .{copy_string(it), clip, as_float(get(t, "Start")), as_float(get(t, "End"), NO_END), as_bool(get(t, "Loop"), true), as_float(get(t, "Speed"), 1)});
    }
    for rig.clips array_add(*rig.takes, .{it.name, xx it_index, 0, NO_END, true, 1});

    rig.handle = handle;
    rig.failed = false;
    return true;
}

find_clip :: (name: string) -> s32 { for rig.clips if equal_nocase(it.name, name) return xx it_index; return -1; }
find_take :: (name: string) -> s32 { for rig.takes if equal_nocase(it.name, name) return xx it_index; return -1; }

advance_take :: (take: s32, time: float, dt: float) -> float {
    t := rig.takes[take];
    end := min(t.end, rig.clips[t.clip].duration);
    time += dt * t.speed;
    if time < end return time;
    if !t.loop || end <= t.start return end;
    return t.start + fmod_cycling(time - t.start, end - t.start);
}

joint_radius :: (width: float) -> float { return max(1.2, width * 0.5); }
joint_steps :: (radius: float) -> int { return clamp(cast(int) (radius * TAU / 2), 4, 14); }

mix :: (a: Color, b: Color, t: float) -> Color {
    m :: (x: u8, y: u8) -> u8 #expand { return xx (x + (cast(float) y - x) * t); }
    return .{m(a.r, b.r), m(a.g, b.g), m(a.b, b.b), m(a.a, b.a)};
}

#import "Basic";
#import "File";
#import "Math";
#import "Sort";
#import "String";
#import "Kit";
