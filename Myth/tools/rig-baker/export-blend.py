import argparse
import os
import sys

import bpy


def arguments():
    parser = argparse.ArgumentParser(description="Export selected actions from a Blender rig to GLB")
    parser.add_argument("--output", required=True)
    parser.add_argument("--action", action="append", required=True)
    return parser.parse_args(sys.argv[sys.argv.index("--") + 1 :])


args = arguments()
armatures = [obj for obj in bpy.data.objects if obj.type == "ARMATURE"]
if len(armatures) != 1:
    raise RuntimeError(f"Expected one armature, found {len(armatures)}")
missing = [name for name in args.action if bpy.data.actions.get(name) is None]
if missing:
    raise RuntimeError("Missing actions: " + ", ".join(missing))

armature = armatures[0]
selected_actions = [bpy.data.actions[name] for name in args.action]
for action in list(bpy.data.actions):
    if action not in selected_actions:
        bpy.data.actions.remove(action)
armature.animation_data_create()
armature.animation_data.action = selected_actions[0]
for obj in bpy.data.objects:
    obj.select_set(False)
armature.select_set(True)
if mannequin := bpy.data.objects.get("Mannequin"):
    mannequin.select_set(True)
bpy.context.view_layer.objects.active = armature

output = os.path.abspath(args.output)
os.makedirs(os.path.dirname(output), exist_ok=True)
bpy.ops.export_scene.gltf(
    filepath=output,
    export_format="GLB",
    use_selection=True,
    export_animations=True,
    export_animation_mode="ACTIONS",
    export_frame_range=False,
    export_force_sampling=True,
    export_def_bones=True,
)
print(f"Exported {', '.join(args.action)} from {bpy.data.filepath} to {output}")
