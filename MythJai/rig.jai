// The humanoid rig Myth draws its player with (HumanoidRigVisual, BoneBody): Myth's ozz clips,
// played as the takes in Assets/Rigs/ual2.takes.json and cross-faded between, posed at the
// rig's own frame rate, turned to face the actor, projected flat, and drawn as depth-ordered
// tapered capsules over a silhouette outline.
// Not ported yet: foot IK, turns, leans and other pose modifiers, secondary motion, ragdoll,
// costumes, the sculpted head (a circle stands in), hands' fingers, skin modes, normals.

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
    FAR :: #run rgb(0x435263); NEAR :: #run rgb(0xb7cdd0); CORE :: #run rgb(0x708b94);
    OUTLINE :: Color.{0, 0, 0, 0xaa};
    FRONT_LIMB_OUTLINE :: 0.35;
    FRONT_LIMB_DEPTH :: 0.05;
};

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

// Advances the takes; poses and projects a new frame at the rig's frame rate. `facing` is -1 or 1.
update_rig :: (pose: *Rig_Pose, facing: s32, dt: float) {
    if !rig_ready() || pose.take < 0 return;
    pose.time = advance_take(pose.take, pose.time, dt);
    if pose.previous >= 0 pose.previous_time = advance_take(pose.previous, pose.previous_time, dt);
    pose.blend = min(1.0, pose.blend + dt / RIG.BLEND_TIME);
    pose.step += dt;
    if pose.posed && pose.step < 1 / RIG.VISUAL_FPS return;
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
    turn := ifx facing > 0 then -1.0 else 1.0;
    for 0..rig.joints.count-1 {
        p := positions[it];
        turned := Vector3.{p.x * turn, p.y, p.z * turn};
        projected := Vector2.{-turned.z, -p.y} * RIG.PIXELS_PER_METER;
        pose.projected[it] = .{floor(projected.x + 0.5), floor(projected.y + 0.5)};  // pixel snap
        pose.depth[it] = turned.x;
    }
}

draw_rig :: (pose: *Rig_Pose, at: Vector2) {
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

    // The silhouette: every piece grown a pixel, in the outline color, under everything.
    for pieces capsule(it.from, it.to, joint_radius(it.from_width) + 1, joint_radius(it.to_width) + 1, joint_steps(joint_radius(it.to_width) + 1), RIG.OUTLINE, from_cap = false);
    circle(head, head_radius + 1.4, 14, RIG.OUTLINE);

    head_drawn := false;
    for pieces {
        bone := BONES[it.bone];
        if !head_drawn && pose.depth[rig.head] <= it.depth { circle(head, head_radius, 14, RIG.NEAR); head_drawn = true; }
        core := bone.from == "pelvis";
        t := clamp(it.depth + 0.5, 0, 1);
        color := ifx core then RIG.CORE else mix(RIG.FAR, RIG.NEAR, t);
        // Limbs in front of the body get a faint outline of their own.
        if bone.limb && it.depth - pelvis_depth > RIG.FRONT_LIMB_DEPTH
            capsule(it.from, it.to, joint_radius(it.from_width) + 1, joint_radius(it.to_width) + 1, joint_steps(joint_radius(it.to_width) + 1), fade(rgb(0), RIG.FRONT_LIMB_OUTLINE));
        capsule(it.from, it.to, joint_radius(it.from_width), joint_radius(it.to_width), joint_steps(joint_radius(it.to_width)), color);
    }
    if !head_drawn circle(head, head_radius, 14, RIG.NEAR);
}

#scope_file

MAX_JOINTS :: 128;
NO_END :: 3.4e38;

Bone_Spec :: struct { from, to: string; size: float; limb: bool; }
BONES :: Bone_Spec.[
    .{"pelvis", "spine_01", RIG.TORSO, false},
    .{"spine_01", "spine_02", RIG.TORSO, false},
    .{"spine_02", "spine_03", RIG.TORSO, false},
    .{"spine_03", "neck_01", RIG.CHEST, false},
    .{"neck_01", "Head", RIG.NECK, false},
    .{"clavicle_l", "upperarm_l", RIG.BICEP, false},
    .{"upperarm_l", "lowerarm_l", RIG.BICEP, true},
    .{"lowerarm_l", "hand_l", RIG.FOREARM, true},
    .{"hand_l", "middle_01_l", RIG.FOREARM, true},
    .{"clavicle_r", "upperarm_r", RIG.BICEP, false},
    .{"upperarm_r", "lowerarm_r", RIG.BICEP, true},
    .{"lowerarm_r", "hand_r", RIG.FOREARM, true},
    .{"hand_r", "middle_01_r", RIG.FOREARM, true},
    .{"pelvis", "thigh_l", RIG.THIGH, false},
    .{"thigh_l", "calf_l", RIG.THIGH, true},
    .{"calf_l", "foot_l", RIG.SHIN, true},
    .{"foot_l", "ball_l", RIG.FOOT, true},
    .{"ball_l", "ball_leaf_l", RIG.FOOT, true},
    .{"pelvis", "thigh_r", RIG.THIGH, false},
    .{"thigh_r", "calf_r", RIG.THIGH, true},
    .{"calf_r", "foot_r", RIG.SHIN, true},
    .{"foot_r", "ball_r", RIG.FOOT, true},
    .{"ball_r", "ball_leaf_r", RIG.FOOT, true},
];

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
