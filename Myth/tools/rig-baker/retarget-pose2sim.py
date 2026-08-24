import argparse
import os
import sys

import bpy
from mathutils import Matrix, Vector


BONE_MAP = {
    "pelvis": "pelvis",
    "spine_01": "lumbar4",
    "spine_02": "lumbar2",
    "spine_03": "torso",
    "neck_01": "torso",
    "Head": "head",
    "upperarm_l": "humerus_l",
    "lowerarm_l": "radius_l",
    "hand_l": "hand_l",
    "upperarm_r": "humerus_r",
    "lowerarm_r": "radius_r",
    "hand_r": "hand_r",
    "thigh_l": "femur_l",
    "calf_l": "tibia_l",
    "foot_l": "calcn_l",
    "ball_l": "toes_l",
    "thigh_r": "femur_r",
    "calf_r": "tibia_r",
    "foot_r": "calcn_r",
    "ball_r": "toes_r",
}

# OpenSim body frames contain anatomical axial rotations that do not map cleanly
# onto UAL2's simpler limbs. These target bones only need to aim between the
# corresponding measured joints; retaining body-frame twist distorts endpoints.
AIM_MAP = {
    "neck_01": ("torso", "head"),
    "upperarm_l": ("humerus_l", "radius_l"),
    "lowerarm_l": ("radius_l", "hand_l"),
    "upperarm_r": ("humerus_r", "radius_r"),
    "lowerarm_r": ("radius_r", "hand_r"),
}


def arguments():
    parser = argparse.ArgumentParser()
    parser.add_argument("source_bvh")
    parser.add_argument("target_glb")
    parser.add_argument("output_glb")
    parser.add_argument("--name", default="Pose2Sim_Performance")
    parser.add_argument("--sway-window", type=float, default=2.0)
    return parser.parse_args(sys.argv[sys.argv.index("--") + 1 :])


def armature_named(name):
    return next(obj for obj in bpy.data.objects if obj.type == "ARMATURE" and obj.name == name)


def rotation(matrix):
    return matrix.to_3x3().normalized().to_quaternion()


args = arguments()
bpy.ops.wm.read_factory_settings(use_empty=True)

bpy.ops.import_scene.gltf(filepath=os.path.abspath(args.target_glb))
target = armature_named("Armature")
for action in list(bpy.data.actions):
    bpy.data.actions.remove(action)

bpy.ops.import_anim.bvh(
    filepath=os.path.abspath(args.source_bvh),
    axis_forward="-Z",
    axis_up="Y",
    global_scale=1,
    frame_start=1,
    use_fps_scale=False,
    update_scene_fps=True,
    update_scene_duration=True,
)
source = next(obj for obj in bpy.data.objects if obj.type == "ARMATURE" and obj != target)
source_action = source.animation_data.action
# Pose2Sim's BVH exporter inserts a canonical rest frame before the captured motion.
first_frame = round(source_action.frame_range[0]) + 1
last_frame = round(source_action.frame_range[1])
scene = bpy.context.scene
fps = scene.render.fps / scene.render.fps_base

missing = [f"{target_name} <- {source_name}" for target_name, source_name in BONE_MAP.items()
           if target_name not in target.pose.bones or source_name not in source.pose.bones]
if missing:
    raise RuntimeError("Missing retarget bones: " + ", ".join(missing))

# The calibration world axes are arbitrary. Derive a stable frame where average
# body-left is +X, average body-up is +Z, and body-forward is -Y like UAL2.
body_up = Vector()
body_left = Vector()
for frame in range(first_frame + 1, last_frame + 1, 10):
    scene.frame_set(frame)
    pose = source.pose.bones
    feet = (pose["calcn_l"].head + pose["calcn_r"].head) * 0.5
    frame_up = (pose["head"].head - feet).normalized()
    frame_left = (pose["femur_l"].head - pose["femur_r"].head).normalized()
    frame_left = (frame_left - frame_up * frame_left.dot(frame_up)).normalized()
    body_up += frame_up
    body_left += frame_left
body_up.normalize()
body_left = (body_left - body_up * body_left.dot(body_up)).normalized()
body_forward = body_left.cross(body_up).normalized()
source_basis = Matrix((body_left, -body_forward, body_up)).transposed()
axis = source_basis.inverted()
axis_q = axis.to_quaternion()
print("Capture basis:", "left", tuple(body_left), "forward", tuple(body_forward), "up", tuple(body_up))

source_rest_rotations = {
    name: rotation(source.data.bones[name].matrix_local) for name in BONE_MAP.values()
}
target_rest_rotations = {
    name: rotation(target.data.bones[name].matrix_local) for name in BONE_MAP
}
target_pelvis_head = target.data.bones["pelvis"].head_local.copy()

# Remove slow floor travel while preserving short-term pelvis sway.
source_positions = []
floor_samples = []
for frame in range(first_frame, last_frame + 1):
    scene.frame_set(frame)
    pose = source.pose.bones
    source_positions.append(axis @ pose["pelvis"].head)
    floor_samples.append(min((axis @ pose["calcn_l"].head).z, (axis @ pose["calcn_r"].head).z))
floor_height = sorted(floor_samples)[round(len(floor_samples) * 0.05)]
window = max(1, round(args.sway_window * fps))
prefix = [Vector((0, 0, 0))]
for position in source_positions:
    prefix.append(prefix[-1] + position)

def horizontal_sway(index):
    begin = max(0, index - window // 2)
    end = min(len(source_positions), index + window // 2 + 1)
    average = (prefix[end] - prefix[begin]) / (end - begin)
    sway = source_positions[index] - average
    sway.z = source_positions[index].z - floor_height - target_pelvis_head.z
    return sway

target.animation_data_create()
action = bpy.data.actions.new(args.name)
action.slots.new("OBJECT", target.name)
target.animation_data.action = action

for pose_bone in target.pose.bones:
    pose_bone.rotation_mode = "QUATERNION"

for frame_index, frame in enumerate(range(first_frame, last_frame + 1)):
    scene.frame_set(frame)
    for pose_bone in target.pose.bones:
        pose_bone.matrix_basis.identity()

    for target_name, source_name in BONE_MAP.items():
        source_rotation = rotation(source.pose.bones[source_name].matrix)
        aligned_delta = axis_q @ source_rotation @ source_rest_rotations[source_name].inverted()
        desired_rotation = aligned_delta @ target_rest_rotations[target_name]
        if target_name in AIM_MAP:
            source_from, source_to = AIM_MAP[target_name]
            desired_direction = (axis @ (source.pose.bones[source_to].head -
                                           source.pose.bones[source_from].head)).normalized()
            current_direction = desired_rotation @ Vector((0, 1, 0))
            desired_rotation = current_direction.rotation_difference(desired_direction) @ desired_rotation

        pose_bone = target.pose.bones[target_name]
        head = pose_bone.head.copy()
        if target_name == "pelvis":
            head = target_pelvis_head + horizontal_sway(frame_index)
        pose_bone.matrix = Matrix.Translation(head) @ desired_rotation.to_matrix().to_4x4()
        pose_bone.keyframe_insert("rotation_quaternion", frame=frame)
        if target_name == "pelvis":
            pose_bone.keyframe_insert("location", frame=frame)

    if frame_index % 250 == 0:
        print(f"Retargeted {frame_index + 1}/{last_frame - first_frame + 1} frames")

action.name = args.name
scene.frame_start = first_frame
scene.frame_end = last_frame

# Export only the canonical target skeleton and this action.
bpy.data.objects.remove(source, do_unlink=True)
if source_action.users == 0:
    bpy.data.actions.remove(source_action)
for obj in bpy.data.objects:
    obj.select_set(False)
target.select_set(True)
bpy.data.objects["Mannequin"].select_set(True)
bpy.context.view_layer.objects.active = target

output = os.path.abspath(args.output_glb)
os.makedirs(os.path.dirname(output), exist_ok=True)
bpy.ops.export_scene.gltf(
    filepath=output,
    export_format="GLB",
    use_selection=True,
    export_animations=True,
    export_animation_mode="ACTIONS",
    export_frame_range=True,
    export_force_sampling=True,
    export_def_bones=True,
)
print(f"Exported {args.name}: {last_frame - first_frame + 1} frames at {fps:.3f} fps to {output}")
