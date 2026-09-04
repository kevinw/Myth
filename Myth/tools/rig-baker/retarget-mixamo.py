import argparse
import json
import math
import os
import re
import sys

import bpy
from mathutils import Matrix


BONE_MAP = {
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


def arguments():
    parser = argparse.ArgumentParser(
        description="Retarget standard Mixamo FBX animations onto Myth's UAL2 skeleton"
    )
    parser.add_argument("source_fbx", nargs="+")
    parser.add_argument("--target", required=True)
    parser.add_argument("--output-dir", required=True)
    parser.add_argument("--profiles", required=True)
    return parser.parse_args(sys.argv[sys.argv.index("--") + 1 :])


def rotation(matrix):
    return matrix.to_3x3().normalized().to_quaternion()


def clip_name(path):
    return re.sub(r"[^A-Za-z0-9]+", "_", os.path.splitext(os.path.basename(path))[0]).strip("_")


def ground_height(armature):
    return min(armature.pose.bones[name].head.z for name in ("ball_leaf_l", "ball_leaf_r"))


def retarget(source_path, target_path, output_dir, profile, ground_reference_clip):
    name = clip_name(source_path)
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
    source_action = source.animation_data.action if source.animation_data else None
    if source_action is None:
        raise RuntimeError(f"No animation found in {source_path}")

    source_bones = {bone.name.rsplit(":", 1)[-1]: bone.name for bone in source.data.bones}
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
    scene.frame_set(first_frame)
    source_pelvis_origin = source.matrix_world @ source.pose.bones[source_bones["Hips"]].head
    target_pelvis_origin = target.data.bones["pelvis"].head_local.copy()
    translation_scale = target_pelvis_origin.z / source_pelvis_origin.z
    target_world_rotation_inverse = rotation(target.matrix_world).inverted()

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
            delta = source_pose @ source_rest[source_base_name].inverted()
            desired = target_world_rotation_inverse @ delta @ target_rest[target_name]
            pose_bone = target.pose.bones[target_name]
            head = pose_bone.head.copy()
            if target_name == "pelvis":
                source_head = source.matrix_world @ source.pose.bones[source_name].head
                head = target_pelvis_origin + (source_head - source_pelvis_origin) * translation_scale
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
    name = clip_name(source_fbx)
    retarget(os.path.abspath(source_fbx), target_path, output_dir,
             clip_profiles.get(name, {}), ground_reference_clip)
