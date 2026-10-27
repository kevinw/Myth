// Myth's ozz-animation runtime (Myth/tools/rig-baker/ozz_runtime.cc, built by Myth's C# project
// into bin/Debug): load a .ozzpack of skeleton + clips, then sample blended clips into
// model-space joint positions (meters, +Y up) and rotations.

myth_ozz_rig_load          :: (data: *u8, size: s32, error: *u8, error_capacity: s32) -> *void #foreign myth_ozz;
myth_ozz_rig_joint_count   :: (rig: *void) -> s32 #foreign myth_ozz;
myth_ozz_rig_clip_count    :: (rig: *void) -> s32 #foreign myth_ozz;
myth_ozz_rig_joint_name    :: (rig: *void, index: s32) -> *u8 #foreign myth_ozz;
myth_ozz_rig_joint_parent  :: (rig: *void, index: s32) -> s32 #foreign myth_ozz;
myth_ozz_rig_clip_name     :: (rig: *void, index: s32) -> *u8 #foreign myth_ozz;
myth_ozz_rig_clip_duration :: (rig: *void, index: s32) -> float #foreign myth_ozz;
myth_ozz_instance_create   :: (rig: *void) -> *void #foreign myth_ozz;
// positions: joint_count float3s; rotations: joint_count float4s (x, y, z, w). Returns 1 on success.
myth_ozz_instance_sample   :: (instance: *void, layer_count: s32, clips: *s32, times: *float, weights: *float,
    additive: *u8, masked: *u8, joint_weights: *float, positions: *Vector3, rotations: *Vector4) -> s32 #foreign myth_ozz;

#scope_file

myth_ozz :: #library "../Myth/bin/Debug/libmyth_ozz";
