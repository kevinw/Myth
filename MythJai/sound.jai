// Myth's sound: FMOD Studio events from the banks Myth builds (Assets/Sounds/FMOD/Build/Desktop),
// fired the way Myth's actors fire them (Game.PlayAudioEvent): one-shot instances placed in 3D,
// with the listener at the view's centre, ringing with the room's reverb. Music isn't hooked
// up: this room is outside every music region, so Myth plays silence there too.

sound :: (path: string, at: Point2, parameter := "", label := "") {
    instance := start_sound(path, to_vector(at));
    if !instance return;
    if parameter FMOD_Studio_EventInstance_SetParameterByNameWithLabel(instance, temp_c_string(parameter), temp_c_string(label), 1);
    finish_sound(instance);
}

// An instance of the event placed at `at`, to set parameters on before finish_sound plays it.
start_sound :: (path: string, at: Vector2) -> *void {
    if !sound_ready() return null;
    description: *void;
    if FMOD_Studio_System_GetEvent(fmod_system, temp_c_string(tprint("event:/%", path)), *description) != FMOD_OK {
        if !array_find(missing_events, path) { array_add(*missing_events, copy_string(path)); log_error("no sound event %", path); }
        return null;
    }
    instance: *void;
    if FMOD_Studio_EventDescription_CreateInstance(description, *instance) != FMOD_OK return null;
    attributes := audio_attributes(at);
    FMOD_Studio_EventInstance_Set3DAttributes(instance, *attributes);
    return instance;
}

// Moves a playing instance (a loop that follows its source).
place_sound :: (instance: *void, at: Vector2) {
    attributes := audio_attributes(at);
    FMOD_Studio_EventInstance_Set3DAttributes(instance, *attributes);
}

finish_sound :: (instance: *void) {
    FMOD_Studio_EventInstance_Start(instance);
    FMOD_Studio_EventInstance_Release(instance);  // freed once it finishes playing
}

update_sound :: (listener: Vector2) {
    if !sound_ready() return;
    attributes := audio_attributes(listener);
    FMOD_Studio_System_SetListenerAttributes(fmod_system, 0, *attributes, null);
    FMOD_Studio_System_Update(fmod_system);
}

#scope_file

BANKS :: string.["Master.bank", "Master.strings.bank", "SFX.bank"];
AUDIO_PIXELS_PER_UNIT :: 72.0;

fmod_system: *void;
fmod_failed: bool;
missing_events: [..] string;

sound_ready :: () -> bool {
    if fmod_system || fmod_failed return !fmod_failed;
    system: *void;
    fmod_failed = FMOD_Studio_System_Create(*system, FMOD_VERSION) != FMOD_OK;
    // `myth wav <path>`: mix to a file instead of the speakers, a frame's worth per update, to check what plays.
    found, at := array_find(get_command_line_arguments(), "wav");
    wav: *u8;
    if !fmod_failed && found && at + 1 < get_command_line_arguments().count {
        core: *void;
        wav = temp_c_string(get_command_line_arguments()[at + 1]);
        // A sixtieth of a second per mix at 48kHz, so the file keeps time with a capture's frames.
        if FMOD_Studio_System_GetCoreSystem(system, *core) != FMOD_OK || FMOD_System_SetOutput(core, FMOD_OUTPUTTYPE_WAVWRITER_NRT) != FMOD_OK ||
           FMOD_System_SetDSPBufferSize(core, 800, 4) != FMOD_OK log_error("can't write audio to %", to_string(wav));
    }
    fmod_failed = fmod_failed || FMOD_Studio_System_Initialize(system, 1024, 0, 0, wav) != FMOD_OK;
    if fmod_failed { log_error("FMOD didn't start; no sound"); return false; }
    for BANKS {
        bank: *void;
        if FMOD_Studio_System_LoadBankFile(system, temp_c_string(tprint("%/Sounds/FMOD/Build/Desktop/%", ASSETS, it)), 0, *bank) != FMOD_OK log_error("can't load bank %", it);
    }
    fmod_system = system;
    if FMOD_Studio_System_SetParameterByName(system, "RoomReverb", room.reverb, 1) != FMOD_OK log_error("no RoomReverb parameter");  // Game.ApplyRoomReverb
    return true;
}

// Myth's ToAudioPosition and Create3DAttributes.
audio_attributes :: (at: Vector2) -> FMOD_3D_Attributes {
    return .{position = .{-at.x / AUDIO_PIXELS_PER_UNIT, at.y / AUDIO_PIXELS_PER_UNIT, 0}, forward = .{0, 0, 1}, up = .{0, -1, 0}};
}

#import "Basic";
#import "FMOD";
#import "Kit";
