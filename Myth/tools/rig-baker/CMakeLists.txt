cmake_minimum_required(VERSION 3.30)
project(myth_rig_tools LANGUAGES CXX)

set(CMAKE_POSITION_INDEPENDENT_CODE ON)
set(CMAKE_MSVC_RUNTIME_LIBRARY "MultiThreaded$<$<CONFIG:Debug>:Debug>")

set(ozz_build_tools ON CACHE BOOL "")
set(ozz_build_fbx OFF CACHE BOOL "")
set(ozz_build_gltf ON CACHE BOOL "")
set(ozz_build_data OFF CACHE BOOL "")
set(ozz_build_samples OFF CACHE BOOL "")
set(ozz_build_howtos OFF CACHE BOOL "")
set(ozz_build_tests OFF CACHE BOOL "")
set(ozz_build_postfix OFF CACHE BOOL "")
add_subdirectory(../ozz-animation ozz)
# Ozz passes -Wall with warnings as errors; clang-cl reads -Wall as MSVC's /Wall (every warning).
if(MSVC AND CMAKE_CXX_COMPILER_ID STREQUAL "Clang")
	target_compile_options(ozz_base PRIVATE /w)
	target_compile_options(ozz_animation PRIVATE /w)
endif()

add_executable(myth_rig_pack main.cc)
target_compile_features(myth_rig_pack PRIVATE cxx_std_17)
target_link_libraries(myth_rig_pack PRIVATE ozz_animation)

add_library(myth_ozz SHARED ozz_runtime.cc)
target_compile_features(myth_ozz PRIVATE cxx_std_17)
target_link_libraries(myth_ozz PRIVATE ozz_animation)
set_target_properties(myth_ozz PROPERTIES CXX_VISIBILITY_PRESET hidden VISIBILITY_INLINES_HIDDEN ON)
