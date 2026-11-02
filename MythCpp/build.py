import os, sys
HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.join(HERE, "..", "CppKit"))
os.chdir(HERE)
import build as kit

MYTH = os.path.join("..", "Myth")
FMOD = os.path.join(MYTH, "Platforms", "FMOD", "Native", "osx")
NATIVE = [os.path.join(FMOD, "libfmod.dylib"), os.path.join(FMOD, "libfmodstudio.dylib"), os.path.join(MYTH, "bin", "Debug", "libmyth_ozz.dylib")]
kit.build("myth", ["main.cpp", "world.cpp", "actors.cpp", "pawn.cpp", "rig.cpp", "effects.cpp", "ui.cpp"],
    shader_sources=["../MythJai/shaders/lighting.hlsl", "shaders/shadow_depths.compute.hlsl", os.path.join(MYTH, "Assets", "Shaders", "Crt.hlsl"), os.path.join(MYTH, "Assets", "Shaders", "PixelSort.hlsl")],
    copy=NATIVE, libs=NATIVE, args=sys.argv[1:])
