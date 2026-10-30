// The humanoid rig Myth draws its player with (HumanoidRigVisual, BoneBody): Myth's ozz clips,
// played as the takes in Assets/Rigs/ual2.takes.json and cross-faded between, posed at the rig's
// own frame rate, turned to face the actor, projected flat, and drawn as depth-ordered tapered
// capsules over a silhouette outline.
// The costume (Assets/Sources/Costumes) wraps bands, studs and spikes onto bones by tag, and
// trousers over the legs (HumanoidRigVisual.Garment), depth-sorted in among the bones.
// The pose leans into speed and acceleration (UpdateVelocityLean) and squashes with the actor.
// Turning round eases through the front (UpdateTurn); the head and hands trail on springs.
// Grounded, the feet find the floor (UpdateFoot); the body rides springs that a landing knocks
// down (UpdateRagdoll). A dive aims the arms along it (ApplyPoseRoll). Swinging in the air, the
// legs keep the air pose and up and down swipes roll the torso.
// Not ported yet: the rope climb's leg pike, hit impulses (enemies'), kilts, the trousers' cloth
// swing (their hems hang at rest), costume surfaces, the sculpted head (a circle stands in),
// hands' fingers, skin modes.
#include "game.h"
#include <algorithm>
#include <array>
#include <strings.h>

// Myth's ozz-animation runtime (Myth/tools/rig-baker/ozz_runtime.cc, built by Myth's C# project into
// bin/Debug): load a .ozzpack of skeleton + clips, then sample blended clips into model-space joint
// positions (meters, +Y up) and rotations.
extern "C" {
void* myth_ozz_rig_load(const void* data, int size, char* error, int error_capacity);
int myth_ozz_rig_joint_count(void* rig);
int myth_ozz_rig_clip_count(void* rig);
const char* myth_ozz_rig_joint_name(void* rig, int index);
int myth_ozz_rig_joint_parent(void* rig, int index);
const char* myth_ozz_rig_clip_name(void* rig, int index);
float myth_ozz_rig_clip_duration(void* rig, int index);
void* myth_ozz_instance_create(void* rig);
// positions: joint_count float3s; rotations: joint_count float4s (x, y, z, w). Returns 1 on success.
int myth_ozz_instance_sample(void* instance, int layer_count, const int* clips, const float* times, const float* weights,
    const u8* additive, const u8* masked, const float* joint_weights, Vector3* positions, Vector4* rotations);
}

static Vector3 operator+(Vector3 a, Vector3 b) { return {a.x + b.x, a.y + b.y, a.z + b.z}; }
static Vector3 operator-(Vector3 a, Vector3 b) { return {a.x - b.x, a.y - b.y, a.z - b.z}; }
static Vector3 operator*(Vector3 a, float k) { return {a.x * k, a.y * k, a.z * k}; }
static Vector3 operator/(Vector3 a, float k) { return {a.x / k, a.y / k, a.z / k}; }
static Vector3& operator+=(Vector3& a, Vector3 b) { return a = a + b; }
static Vector3& operator-=(Vector3& a, Vector3 b) { return a = a - b; }
static float length(Vector3 v) { return sqrtf(v.x * v.x + v.y * v.y + v.z * v.z); }
static Vector2 rotate(Vector2 v, float c, float s) { return {v.x * c - v.y * s, v.x * s + v.y * c}; }
static float wrap_angle(float a) { return a - TAU * floorf((a + PI) / TAU); }

// The settings the player's rig uses: Myth's HumanoidRigSettings defaults, with
// Assets/Actors/humanoid.json and player.json applied.
namespace RIG {
constexpr float PIXELS_PER_METER = 14;
constexpr float VISUAL_FPS = 24;
constexpr float BLEND_TIME = 0.12f;
constexpr float HEAD_SIZE = 0.074f;
constexpr float NECK_EXTENSION = 0.054f;
constexpr float TORSO = 0.141f, CHEST = 0.116f, NECK = 0.25f, BICEP = 0.083f, FOREARM = 0.101f, THIGH = 0.125f, SHIN = 0.063f, FOOT = 0.07f;
constexpr Color OUTLINE{0, 0, 0, 0xaa};
constexpr float FRONT_LIMB_OUTLINE = 0.35f;
constexpr float FRONT_LIMB_DEPTH = 0.05f;
constexpr float VELOCITY_LEAN = 0.6f;
constexpr float SQUISH_RESPONSE = 0.6f;
constexpr float SECONDARY_MOTION = 0.19f;
}

constexpr float NO_END = 3.4e38f;

// Myth's RigTags: what costume pieces match.
enum RigTag : u16 {
    TRUNK = 1, WAIST = 2, NECK = 4, SHOULDER = 8, UPPER_ARM = 16, FOREARM = 32, HAND = 64, HIP = 128,
    THIGH = 256, SHIN = 512, FOOT = 1024, TAIL = 2048, LEFT = 4096, RIGHT = 8192,
};
static constexpr const char* TAG_NAMES[] = {"Trunk", "Waist", "Neck", "Shoulder", "UpperArm", "Forearm", "Hand", "Hip", "Thigh", "Shin", "Foot", "Tail", "Left", "Right"};

struct BoneSpec { const char* from; const char* to; float size; bool limb; u16 tags; };
static constexpr BoneSpec BONES[BONE_COUNT] = {
    {"pelvis", "spine_01", RIG::TORSO, false, WAIST},
    {"spine_01", "spine_02", RIG::TORSO, false, TRUNK},
    {"spine_02", "spine_03", RIG::TORSO, false, TRUNK},
    {"spine_03", "neck_01", RIG::CHEST, false, TRUNK},
    {"neck_01", "Head", RIG::NECK, false, NECK},
    {"clavicle_l", "upperarm_l", RIG::BICEP, false, SHOULDER | LEFT},
    {"upperarm_l", "lowerarm_l", RIG::BICEP, true, UPPER_ARM | LEFT},
    {"lowerarm_l", "hand_l", RIG::FOREARM, true, FOREARM | LEFT},
    {"hand_l", "middle_01_l", RIG::FOREARM, true, HAND | LEFT},
    {"clavicle_r", "upperarm_r", RIG::BICEP, false, SHOULDER | RIGHT},
    {"upperarm_r", "lowerarm_r", RIG::BICEP, true, UPPER_ARM | RIGHT},
    {"lowerarm_r", "hand_r", RIG::FOREARM, true, FOREARM | RIGHT},
    {"hand_r", "middle_01_r", RIG::FOREARM, true, HAND | RIGHT},
    {"pelvis", "thigh_l", RIG::THIGH, false, HIP | LEFT},
    {"thigh_l", "calf_l", RIG::THIGH, true, THIGH | LEFT},
    {"calf_l", "foot_l", RIG::SHIN, true, SHIN | LEFT},
    {"foot_l", "ball_l", RIG::FOOT, true, FOOT | LEFT},
    {"ball_l", "ball_leaf_l", RIG::FOOT, true, FOOT | LEFT},
    {"pelvis", "thigh_r", RIG::THIGH, false, HIP | RIGHT},
    {"thigh_r", "calf_r", RIG::THIGH, true, THIGH | RIGHT},
    {"calf_r", "foot_r", RIG::SHIN, true, SHIN | RIGHT},
    {"foot_r", "ball_r", RIG::FOOT, true, FOOT | RIGHT},
    {"ball_r", "ball_leaf_r", RIG::FOOT, true, FOOT | RIGHT},
};

struct RigTake { string_view name; s32 clip; float start, end; bool loop; float speed, lean; };

static struct {
    void* handle;
    bool failed;
    std::span<string_view> joints;
    std::span<std::pair<string_view, float>> clips;  // name, duration
    List<RigTake> takes{.arena = &persist};
    s32 bone_joints[BONE_COUNT][2];
    s32 bone_parents[BONE_COUNT];
    s32 pelvis, neck, head;
    s32 legs[2][5];                 // left, right: hip, knee, ankle, toe, tip (Leg)
    float upper_body[MAX_JOINTS];   // how much each joint leans with the upper body
    s32 hands[2], palms[2];
    s32 right_upper_arm;
    bool right_arm[MAX_JOINTS];     // the right clavicle and everything out from it
    bool in_hand[2][MAX_JOINTS];    // a hand and its fingers
    bool on_ragdoll[MAX_JOINTS];    // the joints the bones join
    float lower_body[MAX_JOINTS];   // how much each joint goes with the legs
    s32 ragdoll_carrier[MAX_JOINTS];// the nearest of those a joint rides with, -1 if it's one
} rig{};
static Vector3 positions[MAX_JOINTS];
static Vector4 rotations[MAX_JOINTS];

static bool same(string_view a, string_view b) { return a.size() == b.size() && strncasecmp(a.data(), b.data(), a.size()) == 0; }
static s32 find_clip(string_view name) { for (size_t i = 0; i < rig.clips.size(); i++) if (same(rig.clips[i].first, name)) return (s32)i; return -1; }
static s32 find_take(string_view name) { for (s32 i = 0; i < rig.takes.count; i++) if (same(rig.takes[i].name, name)) return i; return -1; }
static string_view copy(const char* s) { size_t n = strlen(s); char* c = (char*)persist.alloc(n, 1); memcpy(c, s, n); return {c, n}; }

static bool rig_ready() {
    if (rig.handle || rig.failed) return !rig.failed;
    rig.failed = true;
    auto data = read_file(asset("Rigs/ual2.ozzpack"));
    if (data.empty()) { log("no rig"); return false; }
    char error[512] = {};
    void* handle = myth_ozz_rig_load(data.data(), (int)data.size(), error, sizeof(error));
    if (!handle) { log("can't load the rig: %s", error); return false; }
    s32 joint_count = myth_ozz_rig_joint_count(handle);
    if (joint_count > MAX_JOINTS) { log("the rig has %d joints; MAX_JOINTS is %d", joint_count, MAX_JOINTS); return false; }

    rig.joints = persist.array<string_view>(joint_count);
    for (s32 i = 0; i < joint_count; i++) rig.joints[i] = copy(myth_ozz_rig_joint_name(handle, i));
    rig.clips = persist.array<std::pair<string_view, float>>(myth_ozz_rig_clip_count(handle));
    for (size_t i = 0; i < rig.clips.size(); i++) rig.clips[i] = {copy(myth_ozz_rig_clip_name(handle, (int)i)), myth_ozz_rig_clip_duration(handle, (int)i)};

    auto joint = [&](string_view name) -> s32 {
        for (s32 i = 0; i < joint_count; i++) if (same(rig.joints[i], name)) return i;
        log("the rig has no joint %.*s", (int)name.size(), name.data());
        return 0;
    };
    auto parent = [&](s32 j) { return myth_ozz_rig_joint_parent(handle, j); };
    // Each joint's nearest ancestor (itself included) that's `stop` or passes `test`, -1 if none.
    auto climb = [&](s32 j, auto test) { while (j >= 0 && !test(j)) j = parent(j); return j; };
    for (s32 b = 0; b < BONE_COUNT; b++) {
        rig.bone_joints[b][0] = joint(BONES[b].from);
        rig.bone_joints[b][1] = joint(BONES[b].to);
        rig.bone_parents[b] = -1;
        for (s32 o = 0; o < BONE_COUNT; o++) if (string_view(BONES[o].to) == BONES[b].from) { rig.bone_parents[b] = o; break; }
    }
    rig.pelvis = joint("pelvis");
    rig.neck = joint("neck_01");
    rig.head = joint("Head");
    const char* SIDES[] = {"l", "r"};
    const char* PARTS[] = {"thigh", "calf", "foot", "ball", "ball_leaf"};
    for (s32 s = 0; s < 2; s++) for (s32 p = 0; p < 5; p++) rig.legs[s][p] = joint(tprint("%s_%s", PARTS[p], SIDES[s]));
    // The upper body: spine_01 and everything above it, eased in through the lower spine.
    s32 spine_01 = joint("spine_01"), spine_02 = joint("spine_02");
    for (s32 i = 0; i < joint_count; i++) rig.upper_body[i] = climb(i, [&](s32 j) { return j == spine_01; }) == spine_01;
    rig.upper_body[spine_01] = 0.25f;
    rig.upper_body[spine_02] = 0.6f;
    for (s32 i = 0; i < joint_count; i++) rig.lower_body[i] = climb(i, [&](s32 j) { return j == rig.pelvis || j == spine_01; }) == rig.pelvis;
    rig.lower_body[spine_01] = 0.75f;
    rig.lower_body[spine_02] = 0.4f;
    rig.hands[0] = joint("hand_l");
    rig.hands[1] = joint("hand_r");
    rig.palms[0] = joint("middle_01_l");
    rig.palms[1] = joint("middle_01_r");
    rig.right_upper_arm = joint("upperarm_r");
    s32 clavicle_r = joint("clavicle_r");
    for (s32 i = 0; i < joint_count; i++) rig.right_arm[i] = climb(i, [&](s32 j) { return j == clavicle_r; }) >= 0;
    for (auto& b : rig.bone_joints) rig.on_ragdoll[b[0]] = rig.on_ragdoll[b[1]] = true;
    for (s32 i = 0; i < joint_count; i++) {
        s32 carrier = climb(i, [&](s32 j) { return rig.on_ragdoll[j]; });
        rig.ragdoll_carrier[i] = carrier == i ? -1 : carrier;
    }
    for (s32 h = 0; h < 2; h++) for (s32 i = 0; i < joint_count; i++) rig.in_hand[h][i] = climb(i, [&](s32 j) { return j == rig.hands[h]; }) >= 0;

    // Saved takes, then every raw clip as a take of its own.
    auto text = read_file(asset("Rigs/ual2.takes.json"), persist);
    if (auto takes = parse_json({(const char*)text.data(), text.size()}, persist)) {
        for (size_t i = 0; i < takes->keys.size(); i++) {
            Json t = takes->items[i];
            s32 clip = find_clip(t["Clip"].as_string());
            if (clip < 0) continue;
            rig.takes.add({takes->keys[i], clip, t["Start"].as_float(), t["End"].as_float(NO_END), t["Loop"].as_bool(true), t["Speed"].as_float(1), t["UpperBodyLeanDegrees"].as_float()});
        }
    }
    for (size_t i = 0; i < rig.clips.size(); i++) rig.takes.add({rig.clips[i].first, (s32)i, 0, NO_END, true, 1, 0});

    rig.handle = handle;
    rig.failed = false;
    return true;
}

static float take_end(const RigTake& t) { return min(t.end, rig.clips[t.clip].second); }

static bool take_finished(const RigPose& pose) {
    const RigTake& t = rig.takes[pose.take];
    return !t.loop && pose.time >= take_end(t) - 0.0001f;
}

static float advance_take(s32 take, float time, float dt) {
    const RigTake& t = rig.takes[take];
    float end = take_end(t);
    time += dt * t.speed;
    if (time < end) return time;
    if (!t.loop || end <= t.start) return end;
    return t.start + fmod_cycling(time - t.start, end - t.start);
}

// The rig's own random numbers, so fidgets don't shift the game's.
static u32 rig_rng = 0x2545f491;
static float rig_random() {
    u32 x = rig_rng;
    x ^= x << 13; x ^= x >> 17; x ^= x << 5;
    rig_rng = x;
    return float(x >> 8) / 16777216.0f;
}

// Pawn.RigFidget: standing still a while, one of a few fidgets plays through, never the same one
// twice running, then another after a rest.
static s32 fidget(RigPose& pose, s32 main, bool still, float dt) {
    static const char* FIDGETS[] = {"PlayerFidgetHolding", "PlayerFidgetOffensive", "PlayerFidgetWarrior", "PlayerFidgetDwarf", "PlayerFidgetHappy"};
    const s32 COUNT = 5;
    const float FIRST_DELAY = 5, MIN_GAP = 4, MAX_GAP = 9;
    if (rig.takes[main].name != "PlayerIdle" || !still) {
        pose.fidget_take = -1;
        pose.fidget_timer = FIRST_DELAY;
        return main;
    }
    if (pose.fidget_take >= 0 && pose.take == pose.fidget_take && take_finished(pose)) {
        pose.fidget_take = -1;
        pose.fidget_timer = MIN_GAP + (MAX_GAP - MIN_GAP) * rig_random();
    }
    if (pose.fidget_take < 0) {
        pose.fidget_timer -= dt;
        if (pose.fidget_timer <= 0) {
            pose.fidget_last = pose.fidget_last < 0 ? (s32)(rig_random() * COUNT) % COUNT : (pose.fidget_last + 1 + (s32)(rig_random() * (COUNT - 1))) % COUNT;
            pose.fidget_take = find_take(FIDGETS[pose.fidget_last]);
        }
    }
    return pose.fidget_take >= 0 ? pose.fidget_take : main;
}

// Pawn.RigLeadFor: the take that opens a change from one clip to another, if any.
static s32 lead_for(s32 from, s32 to, Vector2 velocity) {
    const float RUN_STOP_MIN_SPEED = 0.6f;  // × MAX_GROUND_SPEED; slower stops just settle into the idle
    if (from < 0) return -1;
    string_view f = rig.takes[from].name, t = rig.takes[to].name;
    const char* lead = nullptr;
    if (t == "PlayerAir" && f != "PlayerAir" && velocity.y < 0) lead = "PlayerJumpStart";
    else if ((f == "PlayerAir" || f == "PlayerDive") && t == "PlayerIdle") lead = "PlayerJumpLand";
    else if ((f == "PlayerRun" || f == "Jog_Fwd_Loop" || f == "Sprint_Loop") && t == "PlayerIdle" && fabsf(velocity.x) > MAX_GROUND_SPEED * RUN_STOP_MIN_SPEED) lead = "PlayerRunStop";
    else if (f == "PlayerCrawl" && t == "Crouch_Idle_Loop") lead = "PlayerCrawlToCrouch";
    else if (f == "Crouch_Idle_Loop" && t == "PlayerCrawl") lead = "PlayerCrouchToCrawl";
    else if (f == "Crouch_Idle_Loop" && t == "PlayerIdle") lead = "PlayerCrouchToStand";
    else if (t == "PlayerRoll" && f != "PlayerRoll") lead = "PlayerRollStart";
    return lead ? find_take(lead) : -1;
}

string_view take_for(string_view animation) {
    if (animation == "skid") return "PlayerSkid";
    if (animation == "idle" || animation == "look_up") return "PlayerIdle";
    if (animation == "wall_slide") return "PlayerWallSlide";
    if (animation == "walk") return "Walk_Loop";
    if (animation == "run" || animation == "run_start") return "PlayerRun";
    if (animation == "duck" || animation == "burrow") return "Crouch_Idle_Loop";
    if (animation == "back_dash") return "Roll";
    if (animation == "sword") return "Sword_Regular_A";
    if (animation == "crawl") return "PlayerCrawl";
    if (animation == "roll") return "PlayerRoll";
    if (animation == "jog") return "Jog_Fwd_Loop";
    if (animation == "sprint") return "Sprint_Loop";
    if (animation == "hurt") return "Hit_Knockback";
    if (animation == "dive") return "PlayerDive";
    if (animation.starts_with("jump")) return "PlayerAir";
    if (animation.find("attack") != animation.npos) return "PlayerAttack";
    return "PlayerIdle";
}

void land_rig(RigPose& pose, float speed) {
    const float LANDING_MIN_SPEED = 32;
    if (speed >= LANDING_MIN_SPEED) pose.landing_speed = max(pose.landing_speed, speed);
}

void play_rig(RigPose& pose, string_view animation, Vector2 velocity, float dt, bool restart, s32 facing, bool still) {
    const float RUN_LEAN_IN = 39;       // degrees
    const float RUN_LEAN_PEAK = 0.19f;  // seconds in
    const float GAIT_RATE = 2.2f, GAIT_MIN_RATE = 0.5f, SPRINT_RATE = 0.7f, ROLL_MIN_RATE = 0.5f;
    if (!rig_ready()) return;
    s32 main = find_take(take_for(animation));
    if (main < 0) return;
    main = fidget(pose, main, still, dt);
    if (main != pose.requested) {
        // Once the body has turned round, a skid plays out before the run resumes.
        string_view name = rig.takes[main].name;
        bool skid_holds = pose.take >= 0 && rig.takes[pose.take].name == "PlayerSkid" && facing != pose.skid_facing &&
            (name == "PlayerIdle" || name == "Walk_Loop" || name == "PlayerRun" || name == "Jog_Fwd_Loop" || name == "Sprint_Loop");
        pose.lead = skid_holds ? pose.take : lead_for(pose.requested, main, velocity);
        pose.requested = main;
        if (name == "PlayerSkid") {
            // The skid turns from the way you were going to the stick (facing already shows the brake).
            pose.skid_facing = facing;
            pose.skid_from_yaw = facing < 0 ? 0 : PI;
            pose.skid_to_yaw = facing < 0 ? PI : 0;
        }
    }
    if (pose.lead >= 0 && pose.take == pose.lead && take_finished(pose)) pose.lead = -1;
    s32 shown = pose.lead >= 0 ? pose.lead : main;
    if (shown != pose.take || restart) {
        if (pose.take >= 0 && shown != pose.take) { pose.previous = pose.take; pose.previous_time = pose.time; pose.blend = 0; }
        pose.take = shown;
        pose.time = rig.takes[shown].start;
        pose.arm_arc_set = false;
    }

    string_view name = rig.takes[shown].name;
    bool running = name == "PlayerRun" || name == "Jog_Fwd_Loop" || name == "Sprint_Loop";
    bool gait = running || name == "Walk_Loop" || name == "PlayerCrawl" || name == "Crouch_Fwd_Loop";
    float speed = fabsf(velocity.x) / MAX_GROUND_SPEED;
    pose.playback = gait ? max(GAIT_MIN_RATE, speed * GAIT_RATE * (name == "Sprint_Loop" ? SPRINT_RATE : 1.0f))
                    : name == "PlayerRoll" ? max(ROLL_MIN_RATE, speed) : 1;
    if (running && !pose.was_running) pose.run_lean_time = 0;
    pose.was_running = running;
    pose.gait = gait;
    pose.run_lean_time += dt;
    float t = pose.run_lean_time / RUN_LEAN_PEAK;
    pose.extra_lean = running ? RUN_LEAN_IN * t * expf(1 - t) : 0;
}

static void update_ragdoll(RigPose& pose, float visual_delta) {
    const float SPRING = 160, DAMPING = 11, MAX_SPEED = 10;
    const s32 CONSTRAINT_ITERATIONS = 4;
    const float LANDING_INERTIA = 1, LANDING_COMPRESSION_TIME = 0.0225f;
    const float LANDING_HOLD = 0.06f;            // humanoid.json
    const float LANDING_HOLD_FULL_SPEED = 8;     // meters a second of landing that earns all of it
    s32 count = (s32)rig.joints.size();
    if (!pose.ragdoll_ready) {
        for (s32 i = 0; i < count; i++) { pose.ragdoll[i] = pose.carried[i] = positions[i]; pose.ragdoll_velocity[i] = {}; }
        for (s32 b = 0; b < BONE_COUNT; b++) pose.bone_lengths[b] = length(positions[rig.bone_joints[b][1]] - positions[rig.bone_joints[b][0]]);
        pose.ragdoll_ready = true;
        pose.landing_speed = 0;
        return;
    }
    if (pose.landing_speed > 0) {
        float impact = pose.landing_speed / RIG::PIXELS_PER_METER;
        for (s32 i = 0; i < count; i++) if (rig.on_ragdoll[i]) {
            float height = clamp(positions[i].y / 1.55f, 0.0f, 1.0f);
            pose.ragdoll[i].y -= impact * LANDING_COMPRESSION_TIME * height;
            pose.ragdoll_velocity[i].y -= impact * LANDING_INERTIA * height;
        }
        pose.freeze = max(pose.freeze, LANDING_HOLD * clamp(impact / LANDING_HOLD_FULL_SPEED, 0.0f, 1.0f));
        pose.landing_speed = 0;
    }
    // Carried along by the animation, so only the knocks show.
    for (s32 i = 0; i < count; i++) if (rig.on_ragdoll[i]) pose.ragdoll[i] += positions[i] - pose.carried[i];
    for (s32 i = 0; i < count; i++) pose.carried[i] = positions[i];

    for (float remaining = visual_delta; remaining > 0;) {
        float dt = min(remaining, 1.0f / 30);
        remaining -= dt;
        Vector3 previous[MAX_JOINTS];
        for (s32 i = 0; i < count; i++) if (rig.on_ragdoll[i]) {
            previous[i] = pose.ragdoll[i];
            Vector3& v = pose.ragdoll_velocity[i];
            v += (positions[i] - pose.ragdoll[i]) * SPRING * dt;
            v = v * expf(-DAMPING * dt);
            if (length(v) > MAX_SPEED) v = v / length(v) * MAX_SPEED;
            pose.ragdoll[i] += v * dt;
        }
        for (s32 k = 0; k < CONSTRAINT_ITERATIONS; k++) for (s32 b = 0; b < BONE_COUNT; b++) {
            s32 from = rig.bone_joints[b][0], to = rig.bone_joints[b][1];
            Vector3 d = pose.ragdoll[to] - pose.ragdoll[from];
            float l = length(d);
            if (l <= 0.0001f) continue;
            Vector3 correction = d * ((l - pose.bone_lengths[b]) / l * 0.5f);
            pose.ragdoll[from] += correction;
            pose.ragdoll[to] -= correction;
        }
        for (s32 i = 0; i < count; i++) if (rig.on_ragdoll[i]) pose.ragdoll_velocity[i] = (pose.ragdoll[i] - previous[i]) / dt;
    }
}

// UpdateSecondaryPoint: a point springing after `target` (world pixels), no further off it than
// `max_offset`; returns how far off it is. A jump too far just snaps it there.
static Vector2 secondary(decltype(RigPose::motion[0])& point, Vector2 target, float spring, float damping, float max_offset, float visual_delta) {
    const float TELEPORT = 24;
    if (!point.ready || (point.position - target).length() > TELEPORT) { point.position = target; point.velocity = {}; point.ready = true; return {}; }
    float dt = min(visual_delta, 1.0f / 30);
    point.velocity += (target - point.position) * spring * dt;
    point.velocity = point.velocity * expf(-damping * dt);
    point.position += point.velocity * dt;
    Vector2 offset = point.position - target;
    if (offset.length() <= max_offset) return offset;
    Vector2 direction = unit(offset);
    point.position = target + direction * max_offset;
    point.velocity -= direction * max(0.0f, dot(point.velocity, direction));
    return direction * max_offset;
}

// UpdateFoot: a grounded foot near the floor eases onto it (walking, the floor under the body is what
// counts), its knee re-solved to reach; a planted foot lies flat, toes forward.
static void foot_ik(RigPose& pose, s32 leg, Vector2 at, s32 facing, bool grounded, float visual_delta) {
    const float FOOT_SCALE = min(1.0f, RIG::PIXELS_PER_METER / 25);
    const float PROBE_ABOVE = 4 * FOOT_SCALE, PROBE_DISTANCE = 12 * FOOT_SCALE, PLANT_DISTANCE = 5 * FOOT_SCALE;
    const float PLANTED_SLACK = 0.5f, IK_SPEED = 40 * FOOT_SCALE;
    s32 hip = rig.legs[leg][0], knee = rig.legs[leg][1], ankle = rig.legs[leg][2], toe = rig.legs[leg][3], tip = rig.legs[leg][4];
    Vector2* p = pose.projected;
    Vector2 animated_toe = at + p[toe];
    float target = 0;
    bool planted = false;
    if (grounded) {
        Vector2 probe = animated_toe - Vector2{0, PROBE_ABOVE};
        s32 x = (s32)floorf(probe.x + 0.5f), y = (s32)floorf(probe.y + 0.5f);
        for (s32 d = 0; d <= (s32)ceilf(PROBE_DISTANCE); d++) if (overlaps_solid(RectInt{x - 1, y + d, 2, 1})) {
            float floor_y = (float)(y + d);
            if (pose.gait) { target = floor_y - at.y; planted = animated_toe.y + target >= floor_y - PLANTED_SLACK; }
            else if (fabsf(animated_toe.y - floor_y) <= PLANT_DISTANCE) { target = floor_y - animated_toe.y; planted = true; }
            break;
        }
    }
    pose.foot_offset[leg] = approach(pose.foot_offset[leg], target, IK_SPEED * visual_delta);
    float offset = pose.foot_offset[leg];
    if (fabsf(offset) > 0.001f) {
        Vector2 old_toe = p[toe];
        Vector2 foot = old_toe - p[ankle];
        // SolveLeg: the knee bends (forward of the facing) so the ankle reaches its target.
        float upper = (p[knee] - p[hip]).length(), lower = (p[ankle] - p[knee]).length();
        Vector2 reach = old_toe + Vector2{0, offset} - foot - p[hip];
        float distance = reach.length();
        if (distance > 0.001f && upper > 0.001f && lower > 0.001f) {
            Vector2 direction = reach / distance;
            distance = clamp(distance, fabsf(upper - lower) + 0.001f, upper + lower - min(0.4f, (upper + lower) * 0.04f));
            float along = (upper * upper - lower * lower + distance * distance) / (2 * distance);
            float height = sqrtf(max(0.0f, upper * upper - along * along));
            p[knee] = p[hip] + direction * along - Vector2{-direction.y, direction.x} * (height * facing);
            p[ankle] = p[hip] + direction * distance;
        }
        p[toe] = p[ankle] + foot;
        p[tip] += p[toe] - old_toe;
    }
    if (planted) p[tip] = p[toe] + Vector2{(float)facing, 0} * (p[tip] - p[toe]).length();
}

void update_rig(RigPose& pose, Actor& a, float dt, bool grounded) {
    if (!rig_ready() || pose.take < 0) return;
    if (pose.freeze > 0) { pose.freeze = max(0.0f, pose.freeze - dt); pose.step = 0; return; }
    pose.time = advance_take(pose.take, pose.time, dt * pose.playback);
    if (pose.previous >= 0) pose.previous_time = advance_take(pose.previous, pose.previous_time, dt);
    pose.blend = min(1.0f, pose.blend + dt / RIG::BLEND_TIME);
    pose.step += dt;
    if (pose.posed && pose.step < 1 / RIG::VISUAL_FPS) return;
    float visual_delta = pose.posed ? pose.step : dt;
    pose.step = 0;
    pose.posed = true;

    if (!pose.instance) pose.instance = myth_ozz_instance_create(rig.handle);
    s32 joint_count = (s32)rig.joints.size();
    int clips[3] = {};
    float times[3] = {}, weights[3] = {};
    u8 additive[3] = {}, masked[3] = {};
    s32 layers = 0;
    auto add_layer = [&](s32 take, float time, float weight) {
        if (take < 0 || weight <= 0) return;
        clips[layers] = rig.takes[take].clip;
        times[layers] = time;
        weights[layers] = weight;
        layers++;
    };
    add_layer(pose.previous, pose.previous_time, 1 - pose.blend);
    add_layer(pose.take, pose.time, pose.blend);
    if (pose.blend >= 1) pose.previous = -1;
    // Swinging in the air, the legs keep the air pose: an override on the lower body (UpdateAnimationLayers).
    float joint_weights[MAX_JOINTS * 3] = {};
    const float AIR_LEGS_RESPONSE = 25, ATTACK_PITCH_RESPONSE = 20;
    pose.leg_weight += ((pose.air_swing ? 1.0f : 0.0f) - pose.leg_weight) * (1 - expf(-AIR_LEGS_RESPONSE * visual_delta));
    if (pose.leg_weight < 0.01f) pose.leg_weight = 0;
    pose.attack_reach += ((pose.air_swing ? pose.attack_pitch : 0.0f) - pose.attack_reach) * (1 - expf(-ATTACK_PITCH_RESPONSE * visual_delta));
    s32 legs = find_take("PlayerAir");
    if (pose.leg_weight > 0 && legs >= 0) {
        const RigTake& t = rig.takes[legs];
        float end = take_end(t);
        pose.leg_time = t.start + fmod_cycling(max(0.0f, pose.leg_time - t.start) + visual_delta * t.speed, max(0.0001f, end - t.start));
        for (s32 layer = 0; layer < layers; layer++) {
            masked[layer] = 1;
            for (s32 i = 0; i < joint_count; i++) joint_weights[layer * joint_count + i] = 1 - pose.leg_weight * rig.lower_body[i];
        }
        clips[layers] = t.clip;
        times[layers] = pose.leg_time;
        weights[layers] = pose.leg_weight;
        masked[layers] = 1;
        for (s32 i = 0; i < joint_count; i++) joint_weights[layers * joint_count + i] = rig.lower_body[i];
        layers++;
    }
    if (!myth_ozz_instance_sample(pose.instance, layers, clips, times, weights, additive, masked, joint_weights, positions, rotations)) return;

    // The head sits out along the neck (HeadPosition3D).
    Vector3 along = positions[rig.head] - positions[rig.neck];
    if (length(along) > 0.001f) positions[rig.head] += along / length(along) * RIG::NECK_EXTENSION;
    update_ragdoll(pose, visual_delta);

    // Facing left looks down -Z's projection; facing right turns half around (FacingYaw), and a
    // change of facing eases round through the front over the turn's duration (UpdateTurn).
    const float TURN_DURATION = 0.05f;
    float facing_yaw = a.facing > 0 ? PI : 0;
    if (!pose.turning_to) { pose.turning_to = a.facing; pose.yaw = facing_yaw; pose.turn_time = TURN_DURATION; }
    if (a.facing != pose.turning_to) { pose.turning_to = a.facing; pose.turn_from = pose.yaw; pose.turn_to = facing_yaw; pose.turn_time = 0; }
    if (pose.turn_time < TURN_DURATION) {
        pose.turn_time = min(pose.turn_time + visual_delta, TURN_DURATION);
        float t = pose.turn_time / TURN_DURATION;
        pose.yaw = pose.turn_from + (pose.turn_to - pose.turn_from) * t * t * (3 - 2 * t);
    }
    const float SKID_YAW_RELEASE = 0.15f;
    float yaw = pose.yaw;
    if (rig.takes[pose.take].name == "PlayerSkid") {
        const RigTake& t = rig.takes[pose.take];
        float progress = clamp((pose.time - t.start) / max(0.0001f, take_end(t) - t.start), 0.0f, 1.0f);
        pose.skid_yaw = pose.skid_from_yaw + (pose.skid_to_yaw - pose.skid_from_yaw) * progress * progress * (3 - 2 * progress);
        pose.skid_yaw_weight = 1;
    } else pose.skid_yaw_weight = approach(pose.skid_yaw_weight, 0, visual_delta / SKID_YAW_RELEASE);
    if (pose.skid_yaw_weight > 0) yaw += wrap_angle(pose.skid_yaw - yaw) * pose.skid_yaw_weight;
    float yaw_cos = cosf(yaw), yaw_sin = sinf(yaw);
    for (s32 i = 0; i < joint_count; i++) {
        // Off the ragdoll (fingers) a joint rides with its nearest ancestor on it.
        s32 carrier = rig.ragdoll_carrier[i];
        Vector3 p = carrier < 0 ? positions[i] : positions[i] + pose.ragdoll[carrier] - positions[carrier];
        Vector3 turned{p.x * yaw_cos + p.z * yaw_sin, p.y, -p.x * yaw_sin + p.z * yaw_cos};
        pose.projected[i] = Vector2{-turned.z, -p.y} * RIG::PIXELS_PER_METER;
        pose.depth[i] = turned.x;
    }

    // A spring toward leaning into the speed and the change in it, the upper body turning about the pelvis.
    const float VELOCITY_LEAN_MAX_ANGLE = 0.35f, VELOCITY_LEAN_FULL_SPEED = 280, VELOCITY_LEAN_FULL_ACCELERATION = 900;
    const float VELOCITY_LEAN_SPRING = 140, VELOCITY_LEAN_DAMPING = 9;
    float acceleration = (a.velocity.x - pose.previous_velocity_x) / visual_delta;
    pose.previous_velocity_x = a.velocity.x;
    float target = clamp(a.velocity.x / VELOCITY_LEAN_FULL_SPEED + acceleration / VELOCITY_LEAN_FULL_ACCELERATION, -1.0f, 1.0f) * VELOCITY_LEAN_MAX_ANGLE * RIG::VELOCITY_LEAN;
    pose.lean_rate += (target - pose.lean) * VELOCITY_LEAN_SPRING * visual_delta;
    pose.lean_rate *= expf(-VELOCITY_LEAN_DAMPING * visual_delta);
    pose.lean += pose.lean_rate * visual_delta;
    Vector2 pivot = pose.projected[rig.pelvis];
    // Turns the upper body about `center` by `angle`, each joint as much as it's upper body.
    auto turn_upper_body = [&](Vector2 center, float angle) {
        float c = cosf(angle), s = sinf(angle);
        for (s32 i = 0; i < joint_count; i++) if (rig.upper_body[i] > 0)
            pose.projected[i] += (center + rotate(pose.projected[i] - center, c, s) - pose.projected[i]) * rig.upper_body[i];
    };
    turn_upper_body(pivot, (rig.takes[pose.take].lean + pose.extra_lean) * PI / 180 * a.facing + pose.lean);  // ApplyUpperBodyLean

    // Squash and stretch about the feet (ApplySquish).
    Vector2 squish = Vector2{1, 1} + (a.squish - Vector2{1, 1}) * RIG::SQUISH_RESPONSE;
    Vector2 ground{pivot.x, 0};
    for (s32 i = 0; i < joint_count; i++) pose.projected[i] = ground + (pose.projected[i] - ground) * squish;

    // An up or down swipe in the air rolls the upper body into it (TorsoRoll).
    const float ATTACK_TORSO_DEGREES = 20;
    float torso = pose.attack_reach * a.facing * ATTACK_TORSO_DEGREES * PI / 180;
    if (fabsf(torso) > 0.0001f) turn_upper_body(pose.projected[rig.pelvis], torso);

    // The swing arm's arc grows with the swipe, up and ahead or flipped for a down one (ApplyArmReach).
    {
        const float ARM_ARC_SCALE = 1.6f, ARM_REACH_RELEASE = 15, ATTACK_UP_ARM_DEGREES = 60, ATTACK_DOWN_ARM_DEGREES = 90;
        Vector2 shoulder = pose.projected[rig.right_upper_arm];
        Vector2 arm = pose.projected[rig.palms[1]] - shoulder;
        float angle = atan2f(arm.y, arm.x * a.facing);
        if (!pose.arm_arc_set) { pose.arm_arc_from = angle; pose.arm_arc_set = true; }
        float weight = pose.air_swing ? fabsf(pose.attack_reach) : 0;
        float roll = -(pose.attack_reach > 0 ? ATTACK_DOWN_ARM_DEGREES : ATTACK_UP_ARM_DEGREES) * PI / 180;
        float swing = pose.arm_arc_from + roll + ARM_ARC_SCALE * wrap_angle(angle - pose.arm_arc_from);
        pose.arm_reach_offset = weight > 0 ? a.facing * weight * ((pose.attack_reach > 0 ? -swing : swing) - angle)
                                : pose.arm_reach_offset * expf(-ARM_REACH_RELEASE * visual_delta);
        if (fabsf(pose.arm_reach_offset) > 0.0001f) {
            float rc = cosf(pose.arm_reach_offset), rs = sinf(pose.arm_reach_offset);
            for (s32 i = 0; i < joint_count; i++) if (rig.right_arm[i]) pose.projected[i] = shoulder + rotate(pose.projected[i] - shoulder, rc, rs);
        }
    }

    // Rolled about the torso so the arms point along the aim (ApplyPoseRoll).
    if (pose.arm_aim_weight > 0) {
        Vector2 palms = (pose.projected[rig.palms[0]] + pose.projected[rig.palms[1]]) * 0.5f - pose.projected[rig.neck];
        float roll = wrap_angle(atan2f(pose.arm_aim.y, pose.arm_aim.x) - atan2f(palms.y, palms.x)) * pose.arm_aim_weight;
        Vector2 center = (pose.projected[rig.pelvis] + pose.projected[rig.neck]) * 0.5f;
        float rc = cosf(roll), rs = sinf(roll);
        for (s32 i = 0; i < joint_count; i++) pose.projected[i] = center + rotate(pose.projected[i] - center, rc, rs);
    }

    // The head and hands trail on springs a little (UpdateSecondaryMotion).
    Vector2 at = a.position.vector();
    Vector2 head = secondary(pose.motion[0], at + pose.projected[rig.head], 105, 16, 2.5f, visual_delta) * RIG::SECONDARY_MOTION;
    pose.projected[rig.neck] += head * 0.25f;
    pose.projected[rig.head] += head;
    for (s32 hand = 0; hand < 2; hand++) {
        Vector2 offset = secondary(pose.motion[hand + 1], at + pose.projected[rig.hands[hand]], 65, 10, 4.5f, visual_delta) * RIG::SECONDARY_MOTION;
        for (s32 i = 0; i < joint_count; i++) if (rig.in_hand[hand][i]) pose.projected[i] += offset;
    }

    for (s32 leg = 0; leg < 2; leg++) foot_ik(pose, leg, at, a.facing, grounded, visual_delta);

    for (s32 i = 0; i < joint_count; i++) pose.projected[i] = (pose.projected[i] + Vector2{0.5f, 0.5f}).floor();  // pixel snap
}

Vector2 rig_head(const RigPose& pose, float* radius) {
    if (!rig_ready()) { *radius = 0; return {}; }
    *radius = RIG::HEAD_SIZE * RIG::PIXELS_PER_METER;
    return pose.projected[rig.head];
}

// --- Drawing ---

struct CostumePiece {
    enum Kind : u8 { SLEEVE, STUD, SPIKES, TROUSERS } kind = SLEEVE;
    u16 bones = 0;
    float from = 0, to = 1;
    s32 count = 1;
    float width = 1;
    float length = 1;   // spikes: how far out, in the bone's widths; negative for the other side
    float flare = 0.3f; // trousers: how much wider the hem is than the shin
    Color color = WHITE;
};
static struct { string_view name; std::span<CostumePiece> pieces; } costumes[8];

static std::span<CostumePiece> load_costume(string_view name) {
    auto text = read_file(asset("Sources/Costumes/%.*s.json", (int)name.size(), name.data()));
    if (text.empty()) { log("no costume %.*s", (int)name.size(), name.data()); return {}; }
    auto json = parse_json({(const char*)text.data(), text.size()}, temp);
    if (!json) { log("costume %.*s isn't valid JSON", (int)name.size(), name.data()); return {}; }
    List<CostumePiece> pieces;
    for (Json& it : (*json)["Pieces"]) {
        CostumePiece piece;
        string_view kind = it["Kind"].as_string("Sleeve");
        if (kind == "Sleeve") piece.kind = CostumePiece::SLEEVE;
        else if (kind == "Stud") piece.kind = CostumePiece::STUD;
        else if (kind == "Spikes") piece.kind = CostumePiece::SPIKES;
        else if (kind == "Trousers") piece.kind = CostumePiece::TROUSERS;
        else continue;  // kilts aren't ported
        string_view bones = it["Bones"].as_string();
        while (!bones.empty()) {
            size_t comma = bones.find(',');
            string_view tag = bones.substr(0, comma);
            while (!tag.empty() && tag.front() == ' ') tag.remove_prefix(1);
            while (!tag.empty() && tag.back() == ' ') tag.remove_suffix(1);
            for (s32 t = 0; t < 14; t++) if (same(TAG_NAMES[t], tag)) piece.bones |= u16(1 << t);
            bones.remove_prefix(comma == bones.npos ? bones.size() : comma + 1);
        }
        piece.from = it["From"].as_float(0);
        piece.to = it["To"].as_float(1);
        piece.count = it["Count"].as_int(1);
        piece.width = it["Width"].as_float(1);
        piece.length = it["Length"].as_float(1);
        piece.flare = it["Flare"].as_float(0.3f);
        piece.color = rgb(it["Color"].as_string("ffffff"), WHITE);
        pieces.add(piece);
    }
    auto kept = persist.array<CostumePiece>(pieces.count);
    for (s32 i = 0; i < pieces.count; i++) kept[i] = pieces[i];
    return kept;
}

static std::span<CostumePiece> costume_for(string_view name) {
    if (name.empty()) return {};
    for (auto& c : costumes) if (c.name == name) return c.pieces;
    for (auto& c : costumes) if (c.name.empty()) { c.name = name; c.pieces = load_costume(name); return c.pieces; }
    return {};
}

struct Piece { s32 bone; float depth; Vector2 from, to; float from_width, to_width; };

static float joint_radius(float width) { return max(1.2f, width * 0.5f); }
static s32 joint_steps(float radius) { return clamp((s32)(radius * TAU / 2), 4, 14); }
static Color darken(Color c, float k) { return {u8(c.r * k), u8(c.g * k), u8(c.b * k), 255}; }
static Color multiply(Color a, Color b) { return {u8(u32(a.r) * b.r / 255), u8(u32(a.g) * b.g / 255), u8(u32(a.b) * b.b / 255), u8(u32(a.a) * b.a / 255)}; }

// The bones as drawn pieces, far to near.
static std::array<Piece, BONE_COUNT> rig_pieces(const RigPose& pose, Vector2 at) {
    std::array<Piece, BONE_COUNT> pieces;
    for (s32 b = 0; b < BONE_COUNT; b++) {
        s32 from = rig.bone_joints[b][0], to = rig.bone_joints[b][1];
        float width = RIG::PIXELS_PER_METER * BONES[b].size;
        s32 parent = rig.bone_parents[b];
        float from_width = parent >= 0 ? RIG::PIXELS_PER_METER * BONES[parent].size : width;  // joint continuity
        pieces[b] = {b, (pose.depth[from] + pose.depth[to]) * 0.5f, at + pose.projected[from], at + pose.projected[to], from_width, width};
    }
    std::sort(pieces.begin(), pieces.end(), [](const Piece& a, const Piece& b) { return a.depth < b.depth; });
    return pieces;
}

static void bone_corners(Vector2 from, Vector2 to, float from_width, float to_width, Vector2 out[4]) {
    Vector2 along = to - from;
    float span = along.length();
    along = span > 0.0001f ? along / span : Vector2{0, 1};
    Vector2 across{along.y, -along.x};
    out[0] = from + across * from_width * 0.5f;
    out[1] = from - across * from_width * 0.5f;
    out[2] = to - across * to_width * 0.5f;
    out[3] = to + across * to_width * 0.5f;
}

static void bone_quad(Vector2 from, Vector2 to, float from_width, float to_width, Color color) {
    Vector2 c[4];
    bone_corners(from, to, from_width, to_width, c);
    polygon(c[0], c[1], c[2], c[3], color);
}

// The costume's pieces on a drawn bone, in their order (RigDraw.DrawCostume, sleeves and studs).
static void draw_costume(const Piece& piece, std::span<CostumePiece> costume, Color shade, bool silhouette = false) {
    const u16 SIDES = LEFT | RIGHT;
    u16 tags = BONES[piece.bone].tags;
    for (const CostumePiece& c : costume) {
        if (!(tags & c.bones & ~SIDES) || ((c.bones & SIDES) && !(tags & c.bones & SIDES))) continue;
        Color color = silhouette ? shade : multiply(c.color, shade);
        for (s32 i = 0; i < c.count; i++) {
            float start = c.from + (c.to - c.from) * i / c.count;
            float end = c.from + (c.to - c.from) * (i + (c.count == 1 ? 1.0f : 0.6f)) / c.count;
            Vector2 from = piece.from + (piece.to - piece.from) * start;
            Vector2 to = piece.from + (piece.to - piece.from) * end;
            float from_width = (piece.from_width + (piece.to_width - piece.from_width) * start) * c.width;
            float to_width = (piece.from_width + (piece.to_width - piece.from_width) * end) * c.width;
            float grow = silhouette ? 2 : 0;
            switch (c.kind) {
            case CostumePiece::SLEEVE: bone_quad(from, to, from_width + grow, to_width + grow, color); break;
            case CostumePiece::STUD: circle((from + to) * 0.5f, (from_width + to_width) * 0.25f + grow * 0.5f, 10, color); break;
            case CostumePiece::SPIKES: {
                Vector2 along = unit(piece.to - piece.from);
                Vector2 side = Vector2{along.y, -along.x} * (c.length < 0 ? -1.0f : 1.0f);
                Vector2 a = from + side * from_width * 0.5f, b = to + side * to_width * 0.5f;
                Vector2 tip = (a + b) * 0.5f + side * fabsf(c.length) * (from_width + to_width) * 0.5f;
                if (silhouette) triangle(a - along, b + along, tip + side, color);
                else triangle(a, b, tip, color);
                break;
            }
            case CostumePiece::TROUSERS: break;
            }
        }
    }
}

struct GarmentFace { bool disc; Vector2 a, b, c, d; float radius, depth; Color color; };

// AddTrouserFaces and AddTrouserSeat: a tube per leg, its knee bulging as it bends and its hem
// flaring, hung a little above the ankle so the foot shows; the far leg's a shade darker; a seat
// joins them. Sorted far to near.
static s32 trouser_faces(const RigPose& pose, Vector2 at, const CostumePiece& trousers, GarmentFace faces[9]) {
    const float TROUSER_HEM_RAISE = 0.12f, TROUSER_KNEE_BULGE = 0.4f, SEAT_DROP = 0.35f;
    s32 count = 0;
    auto leg_depth = [&](const s32 leg[5]) { float d = 0; for (s32 i = 0; i < 5; i++) d += pose.depth[leg[i]]; return d / 5; };
    s32 back = leg_depth(rig.legs[0]) < leg_depth(rig.legs[1]) ? 0 : 1;
    float thigh = RIG::THIGH * RIG::PIXELS_PER_METER * trousers.width;
    float shin = RIG::SHIN * RIG::PIXELS_PER_METER * trousers.width;
    for (s32 side = 0; side < 2; side++) {
        const s32* leg = rig.legs[side];
        Vector2 hip = at + pose.projected[leg[0]], knee = at + pose.projected[leg[1]], ankle = at + pose.projected[leg[2]];
        Vector2 hem = (ankle + (knee - ankle) * TROUSER_HEM_RAISE + Vector2{0.5f, 0.5f}).floor();
        Vector2 upper = knee - hip, lower = hem - knee;
        float bend = dot(upper, upper) > 0.01f && dot(lower, lower) > 0.01f ? fabsf(sinf(atan2f(upper.x * lower.y - upper.y * lower.x, dot(upper, lower)))) : 0;
        float knee_width = (thigh + shin) * 0.5f + thigh * TROUSER_KNEE_BULGE * bend;
        float depth = (pose.depth[leg[0]] + pose.depth[leg[1]] + pose.depth[leg[2]]) / 3 + 0.001f;
        Color color = side == back ? darken(trousers.color, 0.8f) : trousers.color;
        Vector2 c[4];
        bone_corners(hip, knee, thigh, knee_width, c);
        faces[count++] = {false, c[0], c[1], c[2], c[3], 0, depth, color};
        bone_corners(knee, hem, knee_width, shin * (1 + trousers.flare), c);
        faces[count++] = {false, c[0], c[1], c[2], c[3], 0, depth, color};
        faces[count++] = {true, knee, {}, {}, {}, knee_width * 0.5f, depth, color};
        faces[count++] = {true, hip, {}, {}, {}, thigh * 0.5f, depth, color};
    }
    Vector2 left = at + pose.projected[rig.legs[0][0]], right = at + pose.projected[rig.legs[1][0]];
    Vector2 raise{0, -RIG::THIGH * RIG::PIXELS_PER_METER * 0.5f};
    Vector2 left_low = left + (at + pose.projected[rig.legs[0][1]] - left) * SEAT_DROP;
    Vector2 right_low = right + (at + pose.projected[rig.legs[1][1]] - right) * SEAT_DROP;
    faces[count++] = {false, left + raise, right + raise, right_low, left_low, 0, min(pose.depth[rig.legs[0][0]], pose.depth[rig.legs[1][0]]) - 0.001f, darken(trousers.color, 0.9f)};
    std::sort(faces, faces + count, [](const GarmentFace& x, const GarmentFace& y) { return x.depth < y.depth; });
    return count;
}

// DrawGarment; `grow` nudges it a pixel each way for the outline.
static void draw_face(const GarmentFace& face, Color color, float grow = 0) {
    if (face.disc) { circle(face.a, face.radius + grow, 10, color); return; }
    if (grow <= 0) { polygon(face.a, face.b, face.c, face.d, color); return; }
    for (Vector2 n : {Vector2{1, 0}, Vector2{-1, 0}, Vector2{0, 1}, Vector2{0, -1}})
        polygon(face.a + n * grow, face.b + n * grow, face.c + n * grow, face.d + n * grow, color);
}

static s32 garment_faces(const RigPose& pose, Vector2 at, std::span<CostumePiece> costume, GarmentFace faces[9]) {
    for (const CostumePiece& c : costume) if (c.kind == CostumePiece::TROUSERS) return trouser_faces(pose, at, c, faces);
    return 0;
}

void draw_rig(RigPose& pose, Vector2 at, const RigLook& look) {
    auto costume = costume_for(look.costume);
    if (!rig_ready() || !pose.posed) return;
    auto pieces = rig_pieces(pose, at);
    Vector2 head = at + pose.projected[rig.head];
    float head_radius = RIG::HEAD_SIZE * RIG::PIXELS_PER_METER;
    float pelvis_depth = pose.depth[rig.pelvis];
    GarmentFace faces[9];
    s32 face_count = garment_faces(pose, at, costume, faces);

    // The silhouette: every piece grown a pixel, in the outline color, under everything.
    for (const Piece& p : pieces) {
        capsule(p.from, p.to, joint_radius(p.from_width) + 1, joint_radius(p.to_width) + 1, joint_steps(joint_radius(p.to_width) + 1), RIG::OUTLINE, false);
        draw_costume(p, costume, RIG::OUTLINE, true);
    }
    for (s32 i = 0; i < face_count; i++) draw_face(faces[i], RIG::OUTLINE, 1);
    circle(head, head_radius + 1.4f, 14, RIG::OUTLINE);

    bool head_drawn = false;
    s32 next_face = 0;
    for (const Piece& p : pieces) {
        const BoneSpec& bone = BONES[p.bone];
        while (next_face < face_count && faces[next_face].depth <= p.depth) { draw_face(faces[next_face], faces[next_face].color); next_face++; }
        if (!head_drawn && pose.depth[rig.head] <= p.depth) { circle(head, head_radius, 14, look.near); head_drawn = true; }
        bool core = string_view(bone.from) == "pelvis";
        float t = clamp(p.depth + 0.5f, 0.0f, 1.0f);
        Color color = core ? look.core : mix(look.far, look.near, t);
        // Limbs in front of the body get a faint outline of their own.
        if (bone.limb && p.depth - pelvis_depth > RIG::FRONT_LIMB_DEPTH)
            capsule(p.from, p.to, joint_radius(p.from_width) + 1, joint_radius(p.to_width) + 1, joint_steps(joint_radius(p.to_width) + 1), Color{0, 0, 0, 255}.fade(RIG::FRONT_LIMB_OUTLINE));
        capsule(p.from, p.to, joint_radius(p.from_width), joint_radius(p.to_width), joint_steps(joint_radius(p.to_width)), color);
        // Clothing darkens with depth as much as the limb under it does.
        auto ratio = [](u8 far, u8 near) { return u8(near ? min(255.0f, 255.0f * far / near) : 255); };
        Color shade = core ? WHITE : mix(Color{ratio(look.far.r, look.near.r), ratio(look.far.g, look.near.g), ratio(look.far.b, look.near.b), 255}, WHITE, t);
        draw_costume(p, costume, shade);
    }
    while (next_face < face_count) { draw_face(faces[next_face], faces[next_face].color); next_face++; }
    if (!head_drawn) circle(head, head_radius, 14, look.near);
}

// RigDraw.NormalColor: a sideways lean, the rest of the unit normal facing the viewer, as a color.
static Color normal_color(Vector2 lateral) {
    Vector2 l = lateral;
    if (l.length() > 0.999f) l = unit(l) * 0.999f;
    Vector3 n{l.x, l.y, sqrtf(max(0.0f, 1 - dot(l, l)))};
    return {u8((n.x * 0.5f + 0.5f) * 255), u8((n.y * 0.5f + 0.5f) * 255), u8((n.z * 0.5f + 0.5f) * 255), 255};
}

static void sphere_normals(Vector2 center, float radius, float roundness) {
    s32 steps = joint_steps(radius);
    Vector2 previous{1, 0};
    for (s32 i = 1; i <= steps; i++) {
        float angle = i * TAU / steps;
        Vector2 next{cosf(angle), sinf(angle)};
        triangle(center, center + previous * radius, center + next * radius, normal_color({}), normal_color(previous * roundness), normal_color(next * roundness));
        previous = next;
    }
}

// The rig's normals, for the lighting (BoneBody.DrawItemNormals): each bone a rounded tube, its sides
// turning away, a ball at each joint; the head, costume and trousers face the viewer.
void draw_rig_normals(RigPose& pose, Vector2 at, const RigLook& look) {
    const float ROUNDNESS = 0.9f;
    const Color FLAT{128, 128, 255, 255};
    auto costume = costume_for(look.costume);
    if (!rig_ready() || !pose.posed) return;
    auto pieces = rig_pieces(pose, at);
    GarmentFace faces[9];
    s32 face_count = garment_faces(pose, at, costume, faces);
    s32 next_face = 0;
    bool head_drawn = false;
    Vector2 head = at + pose.projected[rig.head];
    float head_radius = RIG::HEAD_SIZE * RIG::PIXELS_PER_METER + 1.4f;
    for (const Piece& p : pieces) {
        while (next_face < face_count && faces[next_face].depth <= p.depth) { draw_face(faces[next_face], FLAT, 1); next_face++; }
        if (!head_drawn && pose.depth[rig.head] <= p.depth) { circle(head, head_radius, 14, FLAT); head_drawn = true; }
        Vector2 c[4];  // c[0], c[3]: the left side
        bone_corners(p.from, p.to, p.from_width + 2, p.to_width + 2, c);
        Vector2 across = unit(c[0] - c[1]);
        Color side = normal_color(across * ROUNDNESS), center = normal_color({}), other = normal_color(-across * ROUNDNESS);
        triangle(c[0], p.from, p.to, side, center, center);
        triangle(c[0], p.to, c[3], side, center, side);
        triangle(p.from, c[1], c[2], center, other, other);
        triangle(p.from, c[2], p.to, center, other, center);
        if (rig.bone_parents[p.bone] >= 0) sphere_normals(p.from, joint_radius(p.from_width) + 1, ROUNDNESS);
        sphere_normals(p.to, joint_radius(p.to_width) + 1, ROUNDNESS);
        draw_costume(p, costume, FLAT, true);
    }
    while (next_face < face_count) { draw_face(faces[next_face], FLAT, 1); next_face++; }
    if (!head_drawn) circle(head, head_radius, 14, FLAT);
}
