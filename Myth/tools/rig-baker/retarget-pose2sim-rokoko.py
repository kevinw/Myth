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


def arguments():
    parser = argparse.ArgumentParser()
    parser.add_argument("source_bvh")
    parser.add_argument("target_glb")
    parser.add_argument("output_glb")
    parser.add_argument("--name", default="Pose2Sim_Performance")
    parser.add_argument("--sway-window", type=float, default=2.0)
    return parser.parse_args(sys.argv[sys.argv.index("--") + 1 :])


def armatures():
    return [obj for obj in bpy.data.objects if obj.type == "ARMATURE"]


def action_fcurves(action):
    if hasattr(action, "fcurves"):
        return list(action.fcurves)
    return [
        fcurve
        for layer in action.layers
        for strip in layer.strips
        if hasattr(strip, "channelbags")
        for channelbag in strip.channelbags
        for fcurve in channelbag.fcurves
    ]


args = arguments()
bpy.ops.wm.read_factory_settings(use_empty=True)

try:
    bpy.ops.preferences.addon_enable(module="rokoko_studio_live_blender")
except RuntimeError as error:
    raise RuntimeError("Rokoko Studio Live is not installed and enabled") from error

bpy.ops.import_scene.gltf(filepath=os.path.abspath(args.target_glb))
target = next(obj for obj in armatures() if obj.name == "Armature")
target.animation_data_clear()
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
source = next(obj for obj in armatures() if obj != target)
source_action = source.animation_data.action

# Pose2Sim writes a canonical setup pose at frame one. Leave it out of the bake.
for fcurve in action_fcurves(source_action):
    points = [point for point in fcurve.keyframe_points if point.co.x < 2]
    for point in reversed(points):
        fcurve.keyframe_points.remove(point)
source_action.name = args.name + "_Source"

# Multi-camera calibration chooses an arbitrary world basis. Align its averaged
# anatomical left/up axes to UAL2's +X/+Z before Rokoko evaluates the motion.
first_frame = round(source_action.frame_range[0])
last_frame = round(source_action.frame_range[1])
body_up = Vector()
body_left = Vector()
for frame in range(first_frame, last_frame + 1, 10):
    bpy.context.scene.frame_set(frame)
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
capture_basis = Matrix((body_left, -body_forward, body_up)).transposed()
capture_alignment = capture_basis.inverted().to_4x4()
pelvis = source.pose.bones["pelvis"]
for frame in range(first_frame, last_frame + 1):
    bpy.context.scene.frame_set(frame)
    pelvis.matrix = capture_alignment @ pelvis.matrix
    pelvis.keyframe_insert("location", frame=frame, group=pelvis.name)
    if pelvis.rotation_mode == "QUATERNION":
        pelvis.keyframe_insert("rotation_quaternion", frame=frame, group=pelvis.name)
    else:
        pelvis.keyframe_insert("rotation_euler", frame=frame, group=pelvis.name)
print(
    "Capture basis:",
    "left", tuple(round(value, 4) for value in body_left),
    "forward", tuple(round(value, 4) for value in body_forward),
    "up", tuple(round(value, 4) for value in body_up),
)

missing = [
    f"{target_name} <- {source_name}"
    for target_name, source_name in BONE_MAP.items()
    if target_name not in target.pose.bones or source_name not in source.pose.bones
]
if missing:
    raise RuntimeError("Missing retarget bones: " + ", ".join(missing))

scene = bpy.context.scene
scene.rsl_retargeting_armature_source = source
scene.rsl_retargeting_armature_target = target
# Both rigs are already in metres. Rokoko's pose-height auto-scaler is unstable
# for this calibrated source because its canonical rest axes differ from motion.
scene.rsl_retargeting_auto_scaling = False
scene.rsl_retargeting_use_pose = "REST"
scene.rsl_retargeting_bone_list.clear()
for target_name, source_name in BONE_MAP.items():
    item = scene.rsl_retargeting_bone_list.add()
    item.is_custom = True
    item.bone_name_source = source_name
    item.bone_name_target = target_name

print("Rokoko mapping:")
for target_name, source_name in BONE_MAP.items():
    print(f"  {target_name} <- {source_name}")

result = bpy.ops.rsl.retarget_animation()
if result != {"FINISHED"}:
    raise RuntimeError(f"Rokoko retarget failed: {result}")

action = target.animation_data.action
action.name = args.name
scene.frame_start = round(action.frame_range[0])
scene.frame_end = round(action.frame_range[1])

# OpenSim's bind pose has arms down while UAL2 is a T-pose, so a pure
# rest-delta retarget leaves too much of UAL2's horizontal bind orientation.
# Rebuild only the arm-chain swing from measured joint positions. A body-forward
# roll reference removes the unreliable OpenSim radius/hand axial twist.
for frame in range(scene.frame_start, scene.frame_end + 1):
    scene.frame_set(frame)
    source_pose = source.pose.bones
    source_feet = (source_pose["calcn_l"].head + source_pose["calcn_r"].head) * 0.5
    source_up = (source_pose["head"].head - source_feet).normalized()
    source_left = (
        source_pose["femur_l"].head - source_pose["femur_r"].head
    ).normalized()
    source_left = (source_left - source_up * source_left.dot(source_up)).normalized()
    source_forward = source_left.cross(source_up).normalized()

    for side in ("l", "r"):
        shoulder = source_pose[f"humerus_{side}"].head
        elbow = source_pose[f"radius_{side}"].head
        wrist = source_pose[f"hand_{side}"].head
        upper_direction = (elbow - shoulder).normalized()
        lower_direction = (wrist - elbow).normalized()

        for target_name, direction in (
            (f"upperarm_{side}", upper_direction),
            (f"lowerarm_{side}", lower_direction),
            (f"hand_{side}", lower_direction),
        ):
            bone = target.pose.bones[target_name]
            z_axis = source_forward - direction * source_forward.dot(direction)
            if z_axis.length_squared < 0.000001:
                z_axis = source_up - direction * source_up.dot(direction)
            z_axis.normalize()
            x_axis = direction.cross(z_axis).normalized()
            rotation = Matrix((x_axis, direction, z_axis)).transposed()
            corrected = rotation.to_4x4()
            corrected.translation = bone.head.copy()
            bone.matrix = corrected
            bpy.context.view_layer.update()
            if bone.rotation_mode == "QUATERNION":
                bone.keyframe_insert("rotation_quaternion", frame=frame, group=bone.name)
            else:
                bone.keyframe_insert("rotation_euler", frame=frame, group=bone.name)

# Convert the spatial capture into an in-place ambient clip: retain short body
# sway, but remove slow travel and place the measured floor at target Z zero.
pelvis = target.pose.bones["pelvis"]
pelvis_matrices = []
pelvis_heads = []
floor_samples = []
for frame in range(scene.frame_start, scene.frame_end + 1):
    scene.frame_set(frame)
    pelvis_matrices.append(pelvis.matrix.copy())
    pelvis_heads.append(pelvis.head.copy())
    floor_samples.append(
        min(target.pose.bones["foot_l"].tail.z, target.pose.bones["foot_r"].tail.z)
    )
floor_height = sorted(floor_samples)[round(len(floor_samples) * 0.05)]
window = max(1, round(args.sway_window * scene.render.fps / scene.render.fps_base))
prefix = [Vector()]
for head in pelvis_heads:
    prefix.append(prefix[-1] + head)
for index, frame in enumerate(range(scene.frame_start, scene.frame_end + 1)):
    begin = max(0, index - window // 2)
    end = min(len(pelvis_heads), index + window // 2 + 1)
    average = (prefix[end] - prefix[begin]) / (end - begin)
    matrix = pelvis_matrices[index].copy()
    translation = pelvis_heads[index].copy()
    translation.x -= average.x
    translation.y -= average.y
    translation.z -= floor_height
    matrix.translation = translation
    scene.frame_set(frame)
    pelvis.matrix = matrix
    pelvis.keyframe_insert("location", frame=frame, group=pelvis.name)

# Keep only the baked target and mesh for a predictable glTF animation export.
bpy.data.objects.remove(source, do_unlink=True)
for candidate in list(bpy.data.actions):
    if candidate != action:
        bpy.data.actions.remove(candidate)
for obj in bpy.data.objects:
    obj.select_set(False)
target.select_set(True)
mesh = bpy.data.objects.get("Mannequin")
if mesh:
    mesh.select_set(True)
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
fps = scene.render.fps / scene.render.fps_base
print(
    f"Exported {action.name}: {scene.frame_end - scene.frame_start + 1} frames "
    f"at {fps:.3f} fps to {output}"
)
