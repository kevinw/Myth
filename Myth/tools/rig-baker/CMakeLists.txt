cmake_minimum_required(VERSION 3.30)
project(myth_rig_baker LANGUAGES CXX)

set(ozz_build_tools ON CACHE BOOL "" FORCE)
set(ozz_build_fbx OFF CACHE BOOL "" FORCE)
set(ozz_build_gltf ON CACHE BOOL "" FORCE)
set(ozz_build_data OFF CACHE BOOL "" FORCE)
set(ozz_build_samples OFF CACHE BOOL "" FORCE)
set(ozz_build_howtos OFF CACHE BOOL "" FORCE)
set(ozz_build_tests OFF CACHE BOOL "" FORCE)
set(ozz_build_postfix OFF CACHE BOOL "" FORCE)
add_subdirectory(../ozz-animation ozz)

add_executable(myth_rig_baker main.cc)
target_compile_features(myth_rig_baker PRIVATE cxx_std_17)
target_link_libraries(myth_rig_baker PRIVATE ozz_animation)

