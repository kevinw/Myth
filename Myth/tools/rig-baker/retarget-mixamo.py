import argparse
import json
import math
import os
import re
import sys

import bpy
from mathutils import Matrix


MIXAMO_BONE_MAP = {
    "pelvis": "Hips",
    "spine_01": "Spine",
    "spine_02": "Spine1",
    "spine_03": "Spine2",
    "neck_01": "Neck",
    "Head": "Head",
    "clavicle_l": "LeftShoulder",
    "upperarm_l": "LeftArm",
    "lowerarm_l": "LeftForeArm",
    "hand_l": "LeftHand",
    "middle_01_l": "LeftHandMiddle1",
    "clavicle_r": "RightShoulder",
    "upperarm_r": "RightArm",
    "lowerarm_r": "RightForeArm",
    "hand_r": "RightHand",
    "middle_01_r": "RightHandMiddle1",
    "thigh_l": "LeftUpLeg",
    "calf_l": "LeftLeg",
    "foot_l": "LeftFoot",
    "ball_l": "LeftToeBase",
    "ball_leaf_l": "LeftToe_End",
    "thigh_r": "RightUpLeg",
    "calf_r": "RightLeg",
    "foot_r": "RightFoot",
    "ball_r": "RightToeBase",
    "ball_leaf_r": "RightToe_End",
}

# Quaternius UAL1 (ozz-animation/media/fbx/quaternius/ual.fbx) ships a Rigify DEF- skeleton.
RIGIFY_BONE_MAP = {
    "pelvis": "DEF-hips",
    "spine_01": "DEF-spine.001",
    "spine_02": "DEF-spine.002",
    "spine_03": "DEF-spine.003",
    "neck_01": "DEF-neck",
    "Head": "DEF-head",
    "clavicle_l": "DEF-shoulder.L",
    "upperarm_l": "DEF-upper_arm.L",
    "lowerarm_l": "DEF-forearm.L",
    "hand_l": "DEF-hand.L",
    "middle_01_l": "DEF-f_middle.01.L",
    "clavicle_r": "DEF-shoulder.R",
    "upperarm_r": "DEF-upper_arm.R",
    "lowerarm_r": "DEF-forearm.R",
    "hand_r": "DEF-hand.R",
    "middle_01_r": "DEF-f_middle.01.R",
    "thigh_l": "DEF-thigh.L",
    "calf_l": "DEF-shin.L",
    "foot_l": "DEF-foot.L",
    "ball_l": "DEF-toe.L",
    "thigh_r": "DEF-thigh.R",
    "calf_r": "DEF-shin.R",
    "foot_r": "DEF-foot.R",
    "ball_r": "DEF-toe.R",
}


def arguments():
    parser = argparse.ArgumentParser(
        description="Retarget standard Mixamo (or Rigify UAL1) FBX animations onto Myth's UAL2 skeleton"
    )
    parser.add_argument("source_fbx", nargs="+")
    # Export these actions from each multi-clip FBX instead of its active one; the clip is named after the action.
    parser.add_argument("--action", action="append", default=[])
    parser.add_argument("--target", required=True)
    parser.add_argument("--output-dir", required=True)
    parser.add_argument("--profiles", required=True)
    return parser.parse_args(sys.argv[sys.argv.index("--") + 1 :])


def rotation(matrix):
    return matrix.to_3x3().normalized().to_quaternion()


def clip_name(path):
    return re.sub(r"[^A-Za-z0-9]+", "_", os.path.splitext(os.path.basename(path))[0]).strip("_")


# Yaw that turns the source character to face the way the target does, measured left thigh to right thigh.
# UAL1's armature is rotated 180 degrees, so a world-space delta applied as-is mirrors every swing.
def facing_alignment(source, source_bones, bone_map, target):
    def side(armature, left, right):
        vector = armature.matrix_world @ armature.data.bones[right].head_local - \
            armature.matrix_world @ armature.data.bones[left].head_local
        vector.z = 0
        return vector.normalized()
    return side(source, source_bones[bone_map["thigh_l"]], source_bones[bone_map["thigh_r"]]) \
        .rotation_difference(side(target, "thigh_l", "thigh_r"))


def ground_height(armature):
    return min(armature.pose.bones[name].head.z for name in ("ball_leaf_l", "ball_leaf_r"))


def retarget(source_path, action_name, target_path, output_dir, profile, ground_reference_clip):
    name = clip_name(action_name or source_path)
    if not name:
        raise RuntimeError(f"Cannot derive a clip name from {source_path}")

    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.gltf(filepath=target_path)
    target = next(obj for obj in bpy.data.objects if obj.type == "ARMATURE")
    grounded = profile.get("Ground", False)
    target_ground_height = None
    if grounded:
        reference_action = bpy.data.actions.get(ground_reference_clip)
        if reference_action is None:
            raise RuntimeError(f"Ground reference clip '{ground_reference_clip}' does not exist")
        target.animation_data.action = reference_action
        bpy.context.scene.frame_set(round(reference_action.frame_range[0]))
        bpy.context.view_layer.update()
        target_ground_height = ground_height(target)
    target.animation_data_clear()
    for action in list(bpy.data.actions):
        bpy.data.actions.remove(action)

    bpy.ops.import_scene.fbx(filepath=source_path)
    source = next(obj for obj in bpy.data.objects if obj.type == "ARMATURE" and obj != target)
    if action_name:
        source_action = next((a for a in bpy.data.actions if a.name.rsplit("|", 1)[-1] == action_name), None)
        if source_action is None:
            raise RuntimeError(f"No action '{action_name}' in {source_path}")
        source.animation_data_create()
        source.animation_data.action = source_action
    else:
        source_action = source.animation_data.action if source.animation_data else None
    if source_action is None:
        raise RuntimeError(f"No animation found in {source_path}")

    source_bones = {bone.name.rsplit(":", 1)[-1]: bone.name for bone in source.data.bones}
    BONE_MAP = RIGIFY_BONE_MAP if "DEF-hips" in source_bones else MIXAMO_BONE_MAP
    missing = [
        f"{target_name} <- {source_name}"
        for target_name, source_name in BONE_MAP.items()
        if target_name not in target.pose.bones or source_name not in source_bones
    ]
    if missing:
        raise RuntimeError("Missing retarget bones: " + ", ".join(missing))

    first_frame = round(source_action.frame_range[0])
    last_frame = round(source_action.frame_range[1])
    scene = bpy.context.scene
    fps = scene.render.fps / scene.render.fps_base
    scene.frame_start = first_frame
    scene.frame_end = last_frame

    source_rest = {
        name: rotation(source.matrix_world @ source.data.bones[source_bones[name]].matrix_local)
        for name in BONE_MAP.values()
    }
    target_rest = {
        name: rotation(target.matrix_world @ target.data.bones[name].matrix_local)
        for name in BONE_MAP
    }
    # Rest pose, not the first frame: a clip that starts crouched would otherwise scale all hip motion.
    source_pelvis_origin = source.matrix_world @ source.data.bones[source_bones[BONE_MAP["pelvis"]]].head_local
    target_pelvis_origin = target.data.bones["pelvis"].head_local.copy()
    translation_scale = target_pelvis_origin.z / source_pelvis_origin.z
    target_world_rotation_inverse = rotation(target.matrix_world).inverted()
    alignment = facing_alignment(source, source_bones, BONE_MAP, target)

    target.animation_data_create()
    action = bpy.data.actions.new(name)
    action.slots.new("OBJECT", target.name)
    target.animation_data.action = action
    for bone in target.pose.bones:
        bone.rotation_mode = "QUATERNION"

    endpoints = []
    ground_offset = None
    for frame in range(first_frame, last_frame + 1):
        scene.frame_set(frame)
        for bone in target.pose.bones:
            bone.matrix_basis.identity()
        bpy.context.view_layer.update()

        for target_name, source_base_name in BONE_MAP.items():
            source_name = source_bones[source_base_name]
            source_pose = rotation(source.matrix_world @ source.pose.bones[source_name].matrix)
            delta = alignment @ source_pose @ source_rest[source_base_name].inverted() @ alignment.inverted()
            desired = target_world_rotation_inverse @ delta @ target_rest[target_name]
            pose_bone = target.pose.bones[target_name]
            head = pose_bone.head.copy()
            if target_name == "pelvis":
                source_head = source.matrix_world @ source.pose.bones[source_name].head
                head = target_pelvis_origin + alignment @ (source_head - source_pelvis_origin) * translation_scale
            pose_bone.matrix = Matrix.Translation(head) @ desired.to_matrix().to_4x4()
            bpy.context.view_layer.update()

        if grounded:
            if ground_offset is None:
                ground_offset = target_ground_height - ground_height(target)
            pelvis = target.pose.bones["pelvis"]
            matrix = pelvis.matrix.copy()
            matrix.translation.z += ground_offset
            pelvis.matrix = matrix
            bpy.context.view_layer.update()

        for target_name in BONE_MAP:
            pose_bone = target.pose.bones[target_name]
            pose_bone.keyframe_insert("rotation_quaternion", frame=frame)
            if target_name == "pelvis":
                pose_bone.keyframe_insert("location", frame=frame)

        if frame == first_frame or frame == last_frame:
            bpy.context.view_layer.update()
            endpoints.append((
                target.pose.bones["pelvis"].head.copy(),
                [rotation(target.pose.bones[bone].matrix) for bone in BONE_MAP],
            ))

    pelvis_delta = (endpoints[1][0] - endpoints[0][0]).length
    rotation_deltas = [
        (bone, 2 * math.acos(min(1, abs(first.dot(last)))))
        for bone, first, last in zip(BONE_MAP, endpoints[0][1], endpoints[1][1])
    ]
    max_rotation_bone, max_rotation_delta = max(rotation_deltas, key=lambda item: item[1])

    bpy.data.objects.remove(source, do_unlink=True)
    for candidate in list(bpy.data.actions):
        if candidate != action:
            bpy.data.actions.remove(candidate)
    target.animation_data.action = action
    for obj in bpy.data.objects:
        obj.select_set(False)
    target.select_set(True)
    bpy.data.objects["Mannequin"].select_set(True)
    bpy.context.view_layer.objects.active = target

    output = os.path.join(output_dir, name + ".glb")
    os.makedirs(output_dir, exist_ok=True)
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
    print(
        f"Exported {name}: {last_frame - first_frame + 1} frames at {fps:.3f} fps to {output}\n"
        f"  Loop delta: {pelvis_delta * 100:.2f} cm pelvis, "
        f"{max_rotation_delta * 57.2957795:.1f} degrees at {max_rotation_bone}" +
        (f"\n  Ground offset: {ground_offset * 100:.2f} cm against {ground_reference_clip}"
         if grounded else "")
    )


args = arguments()
target_path = os.path.abspath(args.target)
output_dir = os.path.abspath(args.output_dir)
with open(args.profiles, "r", encoding="utf-8") as file:
    profiles = json.load(file)
ground_reference_clip = profiles["GroundReferenceClip"]
clip_profiles = profiles.get("Clips", {})
for source_fbx in args.source_fbx:
    for action_name in args.action or [None]:
        retarget(os.path.abspath(source_fbx), action_name, target_path, output_dir,
                 clip_profiles.get(clip_name(action_name or source_fbx), {}), ground_reference_clip)
