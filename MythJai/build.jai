// See ../JaiKit/build_kit.jai for the verbs: jai build.jai [- run | - hot]
#run,stallable kit_build("myth", copy = .["../Myth/Platforms/FMOD/Native/osx/libfmod.dylib", "../Myth/Platforms/FMOD/Native/osx/libfmodstudio.dylib", "../Myth/bin/Debug/libmyth_ozz.dylib"],
    shaders = .["../MythCpp/shaders/lighting.hlsl", "../Myth/Assets/Shaders/Crt.hlsl", "../Myth/Assets/Shaders/PixelSort.hlsl"]);
#load "../JaiKit/build_kit.jai";
