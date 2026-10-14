import argparse
import os
import subprocess
import sys

import bpy
from bpy.app.handlers import persistent


def arguments():
    parser = argparse.ArgumentParser(description="Open the UAL2 armature for Myth pose authoring")
    parser.add_argument("--action", required=True)
    parser.add_argument("--from", dest="source", help="a new action starts from this action's first frame")
    parser.add_argument("--wall", type=float,
                        help="show a wall guide this many meters in front of the body (not saved or exported)")
    parser.add_argument("--loop", type=int,
                        help="a new action is keyed at 1, the middle and this frame; saving copies frame 1 onto it")
    return parser.parse_args(sys.argv[sys.argv.index("--") + 1 :])


args = arguments()


def key_pose(frame):
    for bone in armature.pose.bones:
        bone.keyframe_insert("location", frame=frame, group=bone.name)
        bone.keyframe_insert(
            "rotation_quaternion" if bone.rotation_mode == "QUATERNION" else "rotation_euler",
            frame=frame, group=bone.name)
        bone.keyframe_insert("scale", frame=frame, group=bone.name)

armatures = [obj for obj in bpy.data.objects if obj.type == "ARMATURE"]
if len(armatures) != 1:
    raise RuntimeError(f"Expected one armature, found {len(armatures)}")

armature = armatures[0]
armature.animation_data_create()
action = bpy.data.actions.get(args.action)
if action is None:
    if args.source:
        source = bpy.data.actions.get(args.source)
        if source is None:
            raise RuntimeError(f"No action named {args.source}")
        armature.animation_data.action = source
        bpy.context.scene.frame_set(int(source.frame_range[0]))
        bpy.context.view_layer.update()
    action = bpy.data.actions.new(args.action)
    action.use_fake_user = True
    armature.animation_data.action = action
    bpy.context.scene.frame_set(1)
    for frame in [1] + ([(1 + args.loop) // 2, args.loop] if args.loop else []):
        key_pose(frame)
else:
    armature.animation_data.action = action

for obj in bpy.context.selected_objects:
    obj.select_set(False)
armature.hide_set(False)
armature.hide_viewport = False
armature.show_in_front = True
armature.select_set(True)
bpy.context.view_layer.objects.active = armature
bpy.context.scene.frame_set(int(action.frame_range[0]))
armature.pose.use_auto_ik = True
bpy.context.scene.tool_settings.use_keyframe_insert_auto = True

if bpy.context.object.mode != "POSE":
    bpy.ops.object.mode_set(mode="POSE")

for window in bpy.context.window_manager.windows:
    for area in window.screen.areas:
        if area.type != "VIEW_3D":
            continue
        region = next((item for item in area.regions if item.type == "WINDOW"), None)
        if region is None:
            continue
        with bpy.context.temp_override(window=window, area=area, region=region):
            bpy.ops.view3d.view_axis(type="FRONT", align_active=True)
            bpy.ops.view3d.view_selected(use_all_regions=False)

# The rig faces -Y, so the wall stands across it at -Y. Display-only: it's removed before each save.
WALL_GUIDE = "Wall guide (not saved)"


def add_wall_guide():
    if args.wall is None or bpy.data.objects.get(WALL_GUIDE):
        return
    y = -args.wall
    mesh = bpy.data.meshes.new(WALL_GUIDE)
    mesh.from_pydata([(-1, y, 0), (1, y, 0), (1, y, 2.2), (-1, y, 2.2)], [], [(0, 1, 2, 3)])
    guide = bpy.data.objects.new(WALL_GUIDE, mesh)
    guide.display_type = "WIRE"
    guide.hide_select = True
    bpy.context.scene.collection.objects.link(guide)


def remove_wall_guide():
    if guide := bpy.data.objects.get(WALL_GUIDE):
        mesh = guide.data
        bpy.data.objects.remove(guide)
        bpy.data.meshes.remove(mesh)


add_wall_guide()

script_dir = os.path.dirname(os.path.abspath(__file__))
bake_script = os.path.join(script_dir, "bake-ual2.sh")
_bake_process = None
_bake_again = False


def start_bake():
    global _bake_process
    print("Myth: baking UAL2 rig pack...")
    _bake_process = subprocess.Popen([bake_script])
    if not bpy.app.timers.is_registered(poll_bake):
        bpy.app.timers.register(poll_bake, first_interval=0.5)


def poll_bake():
    global _bake_process, _bake_again
    if _bake_process is not None and _bake_process.poll() is None:
        return 0.5
    if _bake_process is not None:
        print(f"Myth: rig bake exited with code {_bake_process.returncode}")
        _bake_process = None
    if _bake_again:
        _bake_again = False
        start_bake()
        return None
    return None


@persistent
def key_pose_before_save(_):
    remove_wall_guide()
    if armature.animation_data.action != action:
        return
    bpy.context.scene.frame_set(1)
    key_pose(1)
    # A loop ends where it starts: frame 1's pose closes it.
    if args.loop:
        key_pose(args.loop)


@persistent
def bake_after_save(_):
    global _bake_again
    add_wall_guide()
    if _bake_process is not None and _bake_process.poll() is None:
        _bake_again = True
    else:
        start_bake()


if key_pose_before_save not in bpy.app.handlers.save_pre:
    bpy.app.handlers.save_pre.append(key_pose_before_save)
if bake_after_save not in bpy.app.handlers.save_post:
    bpy.app.handlers.save_post.append(bake_after_save)

print(f"Myth: editing {args.action}. Saving this blend rebuilds Assets/Rigs/ual2.ozzpack.")
