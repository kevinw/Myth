// The few FMOD Studio C API calls a game needs to play events from banks built in FMOD
// Studio, written by hand (FMOD's headers aren't installed). Signatures from fmod_studio.h /
// fmod.h 2.04. macos/ links to Myth's copies of the libraries; their install names are
// @loader_path, so the game copies both next to its executable (see kit_build's `copy`).

FMOD_VERSION :: 0x00020400;  // must match the library
FMOD_OK :: 0;

FMOD_Vector :: struct { x, y, z: float; }
FMOD_3D_Attributes :: struct { position, velocity, forward, up: FMOD_Vector; }

FMOD_Studio_System_Create :: (system: **void, header_version: u32) -> s32 #foreign fmodstudio;
FMOD_Studio_System_Initialize :: (system: *void, max_channels: s32, studio_flags: u32, flags: u32, extra_driver_data: *void) -> s32 #foreign fmodstudio;
FMOD_Studio_System_Update :: (system: *void) -> s32 #foreign fmodstudio;
FMOD_Studio_System_LoadBankFile :: (system: *void, filename: *u8, flags: u32, bank: **void) -> s32 #foreign fmodstudio;
FMOD_Studio_System_GetEvent :: (system: *void, path: *u8, description: **void) -> s32 #foreign fmodstudio;
FMOD_Studio_System_SetParameterByName :: (system: *void, name: *u8, value: float, ignore_seek_speed: s32) -> s32 #foreign fmodstudio;
FMOD_Studio_System_SetListenerAttributes :: (system: *void, listener: s32, attributes: *FMOD_3D_Attributes, attenuation_position: *FMOD_Vector) -> s32 #foreign fmodstudio;
FMOD_Studio_EventDescription_CreateInstance :: (description: *void, instance: **void) -> s32 #foreign fmodstudio;
FMOD_Studio_EventInstance_Start :: (instance: *void) -> s32 #foreign fmodstudio;
FMOD_Studio_EventInstance_Release :: (instance: *void) -> s32 #foreign fmodstudio;
FMOD_Studio_EventInstance_Set3DAttributes :: (instance: *void, attributes: *FMOD_3D_Attributes) -> s32 #foreign fmodstudio;
FMOD_Studio_EventInstance_SetVolume :: (instance: *void, volume: float) -> s32 #foreign fmodstudio;
FMOD_Studio_EventInstance_SetParameterByName :: (instance: *void, name: *u8, value: float, ignore_seek_speed: s32) -> s32 #foreign fmodstudio;
FMOD_Studio_EventInstance_SetParameterByNameWithLabel :: (instance: *void, name: *u8, label: *u8, ignore_seek_speed: s32) -> s32 #foreign fmodstudio;

#scope_file

fmodstudio :: #library "macos/libfmodstudio";
