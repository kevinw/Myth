// What the player hears and reads, and how they steer: controls, sound, Bud's barks, the HUD, the map.
#include "game.h"

// --- The player's controls, bound as Myth binds them (Controls.cs): arrows or WASD to move, Space to
// jump, E to attack, Shift or F to dash, X held to zoom in; on a gamepad the left stick or d-pad,
// South, West, the right trigger and a click of the left stick. A stick reaches full speed at three
// quarters of its travel (ShapeMoveX). ---

constexpr float MOVE_STICK_DEADZONE = 0.25f;
constexpr float MOVE_STICK_FULL_SPEED = 0.75f;

static SDL_Gamepad* gamepad;
static List<SDL_GamepadButton> buttons_pressed{.arena = &persist};
static bool trigger_down, trigger_pressed;

static bool button_down(SDL_GamepadButton button) { return SDL_GetGamepadButton(gamepad, button); }
static bool button_pressed(SDL_GamepadButton button) { return buttons_pressed.contains(button); }
static float axis(SDL_GamepadAxis a) { return SDL_GetGamepadAxis(gamepad, a) / 32767.0f; }
static float past_deadzone(float v, float deadzone) { return fabsf(v) <= deadzone ? 0 : sign(v) * (fabsf(v) - deadzone) / (1 - deadzone); }

float control_move_x() {
    float keys = (float)((key_down(SDLK_RIGHT) || key_down(SDLK_D)) - (key_down(SDLK_LEFT) || key_down(SDLK_A)));
    if (keys) return keys;
    if (!gamepad) return 0;
    if (s32 dpad = button_down(SDL_GAMEPAD_BUTTON_DPAD_RIGHT) - button_down(SDL_GAMEPAD_BUTTON_DPAD_LEFT)) return (float)dpad;
    float stick = past_deadzone(axis(SDL_GAMEPAD_AXIS_LEFTX), MOVE_STICK_DEADZONE);
    return clamp(stick / ((MOVE_STICK_FULL_SPEED - MOVE_STICK_DEADZONE) / (1 - MOVE_STICK_DEADZONE)), -1.0f, 1.0f);
}

s32 control_move_y() {
    s32 keys = (key_down(SDLK_DOWN) || key_down(SDLK_S)) - (key_down(SDLK_UP) || key_down(SDLK_W));
    if (keys || !gamepad) return keys;
    if (s32 dpad = button_down(SDL_GAMEPAD_BUTTON_DPAD_DOWN) - button_down(SDL_GAMEPAD_BUTTON_DPAD_UP)) return dpad;
    float y = axis(SDL_GAMEPAD_AXIS_LEFTY);
    return (y > 0.5f) - (y < -0.5f);
}

bool control_jump_pressed() { return key_pressed(SDLK_SPACE) || button_pressed(SDL_GAMEPAD_BUTTON_SOUTH); }
bool control_jump_down() { return key_down(SDLK_SPACE) || button_down(SDL_GAMEPAD_BUTTON_SOUTH); }
bool control_attack_pressed() { return key_pressed(SDLK_E) || button_pressed(SDL_GAMEPAD_BUTTON_WEST); }
bool control_dash_pressed() { return key_pressed(SDLK_LSHIFT) || key_pressed(SDLK_F) || trigger_pressed; }
bool control_dash_down() { return key_down(SDLK_LSHIFT) || key_down(SDLK_F) || trigger_down; }
// Escape, or Start or Back on a gamepad: the map.
bool control_map_pressed() { return key_pressed(SDLK_ESCAPE) || button_pressed(SDL_GAMEPAD_BUTTON_START) || button_pressed(SDL_GAMEPAD_BUTTON_BACK); }
// Any face button, which also puts the map away.
bool control_face_pressed() {
    return button_pressed(SDL_GAMEPAD_BUTTON_SOUTH) || button_pressed(SDL_GAMEPAD_BUTTON_EAST) || button_pressed(SDL_GAMEPAD_BUTTON_WEST) || button_pressed(SDL_GAMEPAD_BUTTON_NORTH);
}
// The right stick, to look ahead.
Vector2 control_look() {
    if (!gamepad) return {};
    return {past_deadzone(axis(SDL_GAMEPAD_AXIS_RIGHTX), 0.25f), past_deadzone(axis(SDL_GAMEPAD_AXIS_RIGHTY), 0.25f)};
}
bool control_zoom_down() { return key_down(SDLK_X) || (gamepad && button_down(SDL_GAMEPAD_BUTTON_LEFT_STICK)); }

// Each frame, after the events: what the trigger did since the last.
void update_controls() {
    bool was = trigger_down;
    trigger_down = gamepad && axis(SDL_GAMEPAD_AXIS_RIGHT_TRIGGER) >= 0.25f;
    trigger_pressed = trigger_down && !was;
}

void control_event(const SDL_Event& event) {
    if (event.type == SDL_EVENT_GAMEPAD_ADDED && !gamepad) gamepad = SDL_OpenGamepad(event.gdevice.which);
    if (event.type == SDL_EVENT_GAMEPAD_REMOVED && gamepad && SDL_GetGamepadID(gamepad) == event.gdevice.which) { SDL_CloseGamepad(gamepad); gamepad = nullptr; }
    if (event.type == SDL_EVENT_GAMEPAD_BUTTON_DOWN) buttons_pressed.add((SDL_GamepadButton)event.gbutton.button);
}

void begin_control_frame() { buttons_pressed.count = 0; }

// --- Myth's sound: FMOD Studio events from the banks Myth builds (Assets/Sounds/FMOD/Build/Desktop),
// fired the way Myth's actors fire them (Game.PlayAudioEvent): one-shot instances placed in 3D, with
// the listener at the view's centre, ringing with the room's reverb. Music isn't hooked up: this
// room is outside every music region, so Myth plays silence there too. FMOD's headers aren't
// installed, so these few calls are declared by hand (fmod_studio.h / fmod.h 2.04). ---

struct FmodVector { float x, y, z; };
struct Fmod3DAttributes { FmodVector position, velocity, forward, up; };

extern "C" {
int FMOD_Studio_System_Create(void** system, unsigned header_version);
int FMOD_Studio_System_Initialize(void* system, int max_channels, unsigned studio_flags, unsigned flags, void* extra_driver_data);
int FMOD_Studio_System_GetCoreSystem(void* system, void** core);
int FMOD_System_SetOutput(void* core, int output);
int FMOD_System_SetDSPBufferSize(void* core, unsigned buffer_length, int num_buffers);
int FMOD_Studio_System_Update(void* system);
int FMOD_Studio_System_LoadBankFile(void* system, const char* filename, unsigned flags, void** bank);
int FMOD_Studio_System_GetEvent(void* system, const char* path, void** description);
int FMOD_Studio_System_SetParameterByName(void* system, const char* name, float value, int ignore_seek_speed);
int FMOD_Studio_System_SetListenerAttributes(void* system, int listener, const Fmod3DAttributes* attributes, const FmodVector* attenuation_position);
int FMOD_Studio_EventDescription_CreateInstance(void* description, void** instance);
int FMOD_Studio_EventInstance_Set3DAttributes(void* instance, const Fmod3DAttributes* attributes);
}

constexpr unsigned FMOD_VERSION = 0x00020400;  // must match the library
constexpr int FMOD_OK = 0;
constexpr int FMOD_OUTPUTTYPE_WAVWRITER_NRT = 5;  // mixes to a .wav as fast as it's updated (the file goes in extra_driver_data)
constexpr float AUDIO_PIXELS_PER_UNIT = 72;

static void* fmod_system;
static bool fmod_failed;
static List<string_view> missing_events{.arena = &persist};

// Myth's ToAudioPosition and Create3DAttributes.
static Fmod3DAttributes audio_attributes(Vector2 at) {
    return {.position = {-at.x / AUDIO_PIXELS_PER_UNIT, at.y / AUDIO_PIXELS_PER_UNIT, 0}, .forward = {0, 0, 1}, .up = {0, -1, 0}};
}

static bool sound_ready() {
    if (fmod_system || fmod_failed) return !fmod_failed;
    void* system;
    fmod_failed = FMOD_Studio_System_Create(&system, FMOD_VERSION) != FMOD_OK;
    const char* wav = arg_after("wav");  // `myth wav <path>`: mix to a file instead of the speakers, a frame's worth per update, to check what plays
    if (!fmod_failed && wav) {
        void* core;
        // A sixtieth of a second per mix at 48kHz, so the file keeps time with a capture's frames.
        if (FMOD_Studio_System_GetCoreSystem(system, &core) != FMOD_OK || FMOD_System_SetOutput(core, FMOD_OUTPUTTYPE_WAVWRITER_NRT) != FMOD_OK ||
            FMOD_System_SetDSPBufferSize(core, 800, 4) != FMOD_OK) log("can't write audio to %s", wav);
    }
    fmod_failed = fmod_failed || FMOD_Studio_System_Initialize(system, 1024, 0, 0, (void*)wav) != FMOD_OK;
    if (fmod_failed) { log("FMOD didn't start; no sound"); return false; }
    for (const char* bank : {"Master.bank", "Master.strings.bank", "SFX.bank"}) {
        void* loaded;
        if (FMOD_Studio_System_LoadBankFile(system, asset("Sounds/FMOD/Build/Desktop/%s", bank), 0, &loaded) != FMOD_OK) log("can't load bank %s", bank);
    }
    fmod_system = system;
    apply_room_reverb();
    return true;
}

// Game.ApplyRoomReverb: how much the room rings.
void apply_room_reverb() {
    if (fmod_system && FMOD_Studio_System_SetParameterByName(fmod_system, "RoomReverb", room.reverb, 1) != FMOD_OK) log("no RoomReverb parameter");
}

void* start_sound(string_view path, Vector2 at) {
    if (!sound_ready()) return nullptr;
    void* description;
    if (FMOD_Studio_System_GetEvent(fmod_system, tprint("event:/%.*s", (int)path.size(), path.data()), &description) != FMOD_OK) {
        if (!missing_events.contains(path)) { missing_events.add(path); log("no sound event %.*s", (int)path.size(), path.data()); }
        return nullptr;
    }
    void* instance;
    if (FMOD_Studio_EventDescription_CreateInstance(description, &instance) != FMOD_OK) return nullptr;
    place_sound(instance, at);
    return instance;
}

void place_sound(void* instance, Vector2 at) {
    Fmod3DAttributes attributes = audio_attributes(at);
    FMOD_Studio_EventInstance_Set3DAttributes(instance, &attributes);
}

void finish_sound(void* instance) {
    FMOD_Studio_EventInstance_Start(instance);
    FMOD_Studio_EventInstance_Release(instance);  // freed once it finishes playing
}

void sound(string_view path, Point2 at, const char* parameter, const char* label) {
    void* instance = start_sound(path, at.vector());
    if (!instance) return;
    if (parameter) FMOD_Studio_EventInstance_SetParameterByNameWithLabel(instance, parameter, label, 1);
    finish_sound(instance);
}

void update_sound(Vector2 listener) {
    if (!sound_ready()) return;
    Fmod3DAttributes attributes = audio_attributes(listener);
    FMOD_Studio_System_SetListenerAttributes(fmod_system, 0, &attributes, nullptr);
    FMOD_Studio_System_Update(fmod_system);
}

// --- Bud's barks (Game.BudStory.cs, Game.Story.cs): short lines when something happens, drawn from
// a deck so none repeats too soon, the highest priority winning. He speaks them the way OutLoud.cs
// does: the line types out in a bubble pinned where he said it, with a blip of a baked German
// syllable per English syllable, its vowel walked from the line's German translation, the pitch
// settling at the end of a statement and rising on a question. While he talks, a leader line from
// him to the words pulses with his voice. ---

constexpr s32 ONCE = -1;
struct BarkSet {
    Trigger trigger;
    s32 priority;
    s32 redraw;         // plays before a line can come back, or ONCE
    float chance;       // rolled once for the whole set
    const char* lines[2];
};

// Game.BudStory.cs, minus the reveal door (no doors yet) and Bud being struck (off there too).
static const BarkSet BARKS[] = {
    {Trigger::ROOM_ENTERED, 10, ONCE, 1, {"Come on.", "OK..."}},
    {Trigger::PLAYER_HURT, 2, 4, 1, {"Ouch!", "Still with me?"}},
    {Trigger::ENEMY_KILLED, 1, 5, 0.05f, {"Ouch.", "Woah."}},
    {Trigger::PLAYER_IDLE, 1, 3, 1, {"I'm hungry. I told you we should have packed a lunch."}},
};
constexpr s32 BARK_SETS = 4;

constexpr float IDLE_SECONDS = 10;
constexpr s32 MAX_SPEECH = 160;
constexpr s32 MAX_LINES = 8;
constexpr float SPEECH_MAX_WIDTH = 120;
constexpr Vector2 SPEECH_PADDING{6, 5};
constexpr Color SPEECH_SHADOW{0, 0, 0, 170};
constexpr Color BUD_SPEECH_COLOR{185, 214, 255, 255};  // his blue (7fb6ff) lifted 45% toward white
constexpr float LEADER_SPEAKER_GAP = 1.5f;
constexpr float LEADER_TEXT_GAP = 1;
constexpr float VOICE_ATTACK_RATE = 24;
constexpr float VOICE_HOLD = 0.08f;
constexpr float VOICE_RELEASE_RATE = 3.5f;
// Bud's VoiceProfile: a small companion's higher, quicker voice. Pitch in semitones.
constexpr float BUD_PITCH = 4;
constexpr float BUD_PITCH_VARIANCE = 2;
constexpr float BUD_CHARS_PER_SECOND = 26;
constexpr float QUESTION_RISE = 4;
constexpr float STATEMENT_FALL = 1.5f;

struct Blip { s32 char_index, clip; float pitch, gain; };

struct Speech {
    char text[MAX_SPEECH];
    s32 length;             // 0 when nobody's talking
    Vector2 at;             // where it was said, in room pixels
    float age, duration;
    s32 visible;
    float reveal_at[MAX_SPEECH];
    Blip blips[MAX_SPEECH];
    s32 blip_count, fired;
    struct { s32 start, end; float width; } lines[MAX_LINES];
    s32 line_count;
    float width;
    s32 line_height;
    float voice_level, voice_gain, voice_hold;
};

static struct {
    s32 plays;
    s32 next_play[BARK_SETS][2];
    float idle_time;
    bool idle_triggered;
    Point2 last_player;
    Speech speech;
} story{};
static Font speech_font;
static std::span<string_view> clip_names;
static std::span<s8> clip_vowels;   // per clip: 0..4 for a e i o u
static Json german;                 // English line -> its German translation

// 0..4 for a e i o u (y counts as i; German umlauts fold to their vowel), -1 otherwise.
static s8 vowel_of(string_view s, size_t i) {
    u8 c = (u8)s[i];
    if (c == 0xC3 && i + 1 < s.size()) {
        switch ((u8)s[i + 1]) {
            case 0xA4: case 0x84: return 1;  // ä Ä
            case 0xB6: case 0x96: return 3;  // ö Ö
            case 0xBC: case 0x9C: return 4;  // ü Ü
        }
        return -1;
    }
    if (c >= 'A' && c <= 'Z') c += 32;
    switch (c) {
        case 'a': return 0;
        case 'e': return 1;
        case 'i': case 'y': return 2;
        case 'o': return 3;
        case 'u': return 4;
    }
    return -1;
}

// OutLoud's xorshift32, seeded with Hash32.Of (FNV-1a), so a line always babbles the same.
static u32 fnv(string_view s) {
    u32 hash = 2166136261u;
    for (char c : s) hash = (hash ^ (u8)c) * 16777619u;
    return hash ? hash : 1;
}
static u32 next_u32(u32& state) {
    u32 x = state;
    x ^= x << 13; x ^= x >> 17; x ^= x << 5;
    return state = x;
}
static s32 next(u32& state, s32 max) { return max <= 0 ? 0 : s32(next_u32(state) % (u32)max); }
static float next_float(u32& state) { return float(next_u32(state) >> 8) / 16777216.0f; }

static void add_line(Speech& s, string_view text, s32 start, s32 end) {
    float width = ceilf(text_size(speech_font, text.substr(start, end - start)).x);
    s.lines[s.line_count++] = {start, end, width};
    s.width = max(s.width, width);
}

// Game.ShowSpeech + Utterance.Build.
static void say(string_view line, Vector2 at) {
    Speech& s = story.speech;
    s = {};
    s.length = min((s32)line.size(), MAX_SPEECH);
    memcpy(s.text, line.data(), s.length);
    string_view text{s.text, (size_t)s.length};
    s.at = at;

    float time = 0;
    for (s32 i = 0; i < s.length; i++) {
        s.reveal_at[i] = time;
        char c = text[i];
        time += 1 / BUD_CHARS_PER_SECOND + (c == ',' || c == ';' || c == ':' ? 0.18f : c == '.' || c == '!' || c == '?' ? 0.32f : 0);
    }
    s.duration = s.reveal_at[s.length - 1] + clamp(s.length * 0.04f, 1.2f, 3.0f);

    string_view translation = german.get(line).as_string();
    s8 german_vowels[MAX_SPEECH];
    s32 german_count = 0;
    bool previous = false;
    for (size_t i = 0; i < translation.size(); i += (u8)translation[i] == 0xC3 ? 2 : 1) {
        s8 v = vowel_of(translation, i);
        if (v >= 0 && !previous && german_count < MAX_SPEECH) german_vowels[german_count++] = v;
        previous = v >= 0;
    }

    u32 rng = fnv(text);
    previous = false;
    for (s32 i = 0; i < s.length; i++) {
        bool vowel = vowel_of(text, i) >= 0;
        if (vowel && !previous && !clip_names.empty()) {
            s32 clip = -1;
            if (german_count) {
                s8 target = german_vowels[s.blip_count % german_count];
                s32 matches = 0;
                for (s8 v : clip_vowels) matches += v == target;
                if (matches) {
                    s32 n = next(rng, matches);
                    for (size_t k = 0; k < clip_vowels.size(); k++) if (clip_vowels[k] == target) { if (n == 0) { clip = (s32)k; break; } n--; }
                }
            }
            if (clip < 0) clip = next(rng, (s32)clip_names.size());
            s.blips[s.blip_count++] = {i, clip, BUD_PITCH + (next_float(rng) * 2 - 1) * BUD_PITCH_VARIANCE, 1};
        }
        previous = vowel;
    }
    // Settle (statement) or lift (question) the last few blips.
    bool question = false;
    for (s32 i = s.length - 1; i >= 0; i--) if (!strchr(" \"')", text[i])) { question = text[i] == '?'; break; }
    s32 k = min(3, s.blip_count);
    for (s32 j = 0; j < k; j++) s.blips[s.blip_count - k + j].pitch += (question ? QUESTION_RISE : -STATEMENT_FALL) * (j + 1.0f) / k;

    // Wrap into lines no wider than SPEECH_MAX_WIDTH, once.
    s.line_height = (s32)ceilf(speech_font.line_height * 0.7f) + 1;
    s32 start = 0, end = 0;
    while (end <= s.length) {
        s32 word_end = end;
        while (word_end < s.length && text[word_end] != ' ') word_end++;
        if (end > start && text_size(speech_font, text.substr(start, word_end - start)).x > SPEECH_MAX_WIDTH && s.line_count < MAX_LINES - 1) {
            add_line(s, text, start, end - 1);
            start = end;
        }
        if (word_end == s.length) { add_line(s, text, start, word_end); break; }
        end = word_end + 1;
    }
}

void bark(Trigger trigger) {
    if (story.speech.length) return;  // one line at a time (Game.CanShowSpeech)
    struct { s32 set, line; } candidates[16];
    s32 count = 0, best = -1;
    for (s32 s = 0; s < BARK_SETS; s++) {
        const BarkSet& set = BARKS[s];
        if (set.trigger != trigger || (set.chance < 1 && random01() >= set.chance)) continue;
        for (s32 l = 0; l < 2 && set.lines[l]; l++) {
            s32 next = story.next_play[s][l];
            if ((set.redraw == ONCE && next < 0) || story.plays < next) continue;
            if (set.priority > best) { best = set.priority; count = 0; }
            if (set.priority == best && count < 16) candidates[count++] = {s, l};
        }
    }
    if (!count) return;
    auto pick = candidates[random_u64() % (u64)count];
    const BarkSet& set = BARKS[pick.set];
    story.plays++;
    story.next_play[pick.set][pick.line] = set.redraw == ONCE ? -1 : story.plays + set.redraw;
    for (Bud& b : actors.buds) { say(set.lines[pick.line], b.position.vector() + Vector2{0, -32}); break; }
}

// Game.PlayVoiceSyllable: one baked syllable, named so FMOD shows which.
static void voice(s32 clip, float pitch, float gain, Vector2 at) {
    void* instance = start_sound("voice/Kev", at);
    if (!instance) return;
    FMOD_Studio_EventInstance_SetVolume(instance, clamp(gain, 0.0f, 1.0f));
    string_view name = clip_names[clip];
    FMOD_Studio_EventInstance_SetParameterByNameWithLabel(instance, "syllable", tprint("%.*s", (int)name.size(), name.data()), 1);
    FMOD_Studio_EventInstance_SetParameterByName(instance, "pitch", pitch, 0);
    finish_sound(instance);
}

void load_story() {
    speech_font = load_font(asset("Fonts/m3x6.ttf"), 16).value_or(Font{});
    auto manifest_text = read_file(asset("Voice/manifest.json"), persist);
    Json manifest = parse_json({(const char*)manifest_text.data(), manifest_text.size()}, persist).value_or(Json{});
    Json names = manifest["names"], vowels = manifest["vowels"];
    clip_names = persist.array<string_view>(names.items.size());
    clip_vowels = persist.array<s8>(vowels.items.size());
    for (size_t i = 0; i < names.items.size(); i++) clip_names[i] = names.items[i].as_string();
    for (size_t i = 0; i < vowels.items.size(); i++) clip_vowels[i] = vowel_of(vowels.items[i].as_string(), 0);
    auto german_text = read_file(asset("Voice/voice-de.json"), persist);
    german = parse_json({(const char*)german_text.data(), german_text.size()}, persist).value_or(Json{});
    bark(Trigger::ROOM_ENTERED);
}

void update_story() {
    // Ten seconds without moving or pressing anything.
    for (Player& player : actors.players) {
        bool moved = (player.position - story.last_player).vector().length() > 0.5f;
        story.last_player = player.position;
        if (moved || held.count) { story.idle_time = 0; story.idle_triggered = false; }
        else {
            story.idle_time += delta;
            if (!story.idle_triggered && story.idle_time >= IDLE_SECONDS) { story.idle_triggered = true; bark(Trigger::PLAYER_IDLE); }
        }
    }

    Speech& s = story.speech;
    if (!s.length) return;
    s.age += delta;
    while (s.visible < s.length && s.reveal_at[s.visible] <= s.age) s.visible++;
    while (s.fired < s.blip_count && s.blips[s.fired].char_index < s.visible) {
        const Blip& b = s.blips[s.fired];
        voice(b.clip, b.pitch, b.gain, s.at);
        s.voice_gain = clamp(b.gain, 0.0f, 1.0f);
        s.voice_hold = VOICE_HOLD;
        s.fired++;
    }
    s.voice_hold = max(0.0f, s.voice_hold - delta);
    bool active = s.voice_hold > 0;
    s.voice_level = approach(s.voice_level, active ? s.voice_gain : 0, (active ? VOICE_ATTACK_RATE : VOICE_RELEASE_RATE) * delta);
    if (s.age >= s.duration) s.length = 0;
}

static float clamp_to_view(float position, float size, float view) {
    const float MARGIN = 3;
    return floorf(max(MARGIN, min(position, view - MARGIN - size)) + 0.5f);
}

static Vector2 clamp_point(Vector2 v, Vector2 lo, Vector2 hi) { return {clamp(v.x, lo.x, hi.x), clamp(v.y, lo.y, hi.y)}; }

static void line(Vector2 from, Vector2 to, Color color) {
    Vector2 side = unit({to.y - from.y, from.x - to.x});  // weight 2: one pixel either side
    polygon(from + side, to + side, to - side, from - side, color);
}

void shift_speech(Vector2 offset) { story.speech.at += offset; }

void draw_speech(Vector2 view) {
    Speech& s = story.speech;
    if (!s.length) return;
    string_view said{s.text, (size_t)s.length};
    Vector2 size{s.width + SPEECH_PADDING.x * 2, s.line_count * s.line_height + SPEECH_PADDING.y * 2};
    // Up and to the left of where it was said, slid back onto the screen.
    Vector2 anchor = (s.at - view) * zoom;
    Rect box{{clamp_to_view(anchor.x - size.x + SPEECH_PADDING.x, size.x, VIEW.x), clamp_to_view(anchor.y - size.y + SPEECH_PADDING.y, size.y, VIEW.y)}, size};

    for (Bud& b : actors.buds) {
        if (s.voice_level <= 0) break;
        RectInt bounds = hitbox_rect(b);
        Vector2 speaker_center = (Vector2{bounds.x + bounds.w * 0.5f, bounds.y + bounds.h * 0.5f} - view) * zoom;
        Vector2 words = clamp_point(speaker_center, box.pos + SPEECH_PADDING, box.pos + box.size - SPEECH_PADDING);
        Vector2 speaker = (clamp_point(words / zoom + view, Point2{bounds.x, bounds.y}.vector(), Point2{bounds.x + bounds.w, bounds.y + bounds.h}.vector()) - view) * zoom;
        Vector2 gap = words - speaker;
        if (gap.length() <= LEADER_SPEAKER_GAP + LEADER_TEXT_GAP) break;
        Vector2 direction = unit(gap);
        Vector2 from = speaker + direction * LEADER_SPEAKER_GAP, to = words - direction * LEADER_TEXT_GAP;
        float opacity = sqrtf(s.voice_level);
        line(from + Vector2{1, 1}, to + Vector2{1, 1}, SPEECH_SHADOW.fade(opacity));
        line(from, to, BUD_SPEECH_COLOR.fade(opacity));
        break;
    }

    s32 remaining = s.visible;
    for (s32 i = 0; i < s.line_count; i++) {
        string_view full = said.substr(s.lines[i].start, s.lines[i].end - s.lines[i].start);
        string_view shown = full.substr(0, clamp(remaining, 0, (s32)full.size()));
        Vector2 at = (box.pos + SPEECH_PADDING + Vector2{(s.width - s.lines[i].width) * 0.5f, (float)(i * s.line_height)} + Vector2{0.5f, 0.5f}).floor();
        text(speech_font, shown, at + Vector2{1, 1}, SPEECH_SHADOW);
        text(speech_font, shown, at, BUD_SPEECH_COLOR);
        remaining -= (s32)full.size() + 1;  // and the space that joined it to the next
        if (remaining <= 0) break;
    }
}

// --- The player's HUD, as Myth draws it (Game.Rendering.Hud.cs): health in the "numbers" sprite,
// counting down to the real value so a hit visibly ticks away and pulsing red on the last hit's
// worth, over the burrow stamina bar. In view pixels, drawn unlit. ---

static float displayed_health = -1;
static float bar_opacity = 1;

void draw_hud() {
    const float HEALTH_COUNT_RATE = 60;  // points per second the readout falls toward a hit
    const float HEALTH_LOW_PULSE_SECONDS = 0.8f;
    const float BURROW_BAR_OPACITY_RATE = 8;
    for (Player& player : actors.players) {
        float y = VIEW.y - 14;
        // Burrow stamina: its width is the capacity; it drains while burrowing, pulsing toward white,
        // and stays dim after a burrow until it re-arms.
        const float PIXELS_PER_BURROW_SECOND = 35;
        float width = player.burrow.duration * PIXELS_PER_BURROW_SECOND;
        if (width <= 0) width = BURROW_DURATION * PIXELS_PER_BURROW_SECOND;
        bar_opacity = approach(bar_opacity, player.burrow.hud_spent ? 0.5f : 1.0f, BURROW_BAR_OPACITY_RATE * delta);
        Color bar{99, 155, 255, 200};
        if (player.state == Player::BURROW) {
            float wave = 0.5f - 0.5f * cosf((float)now() * TAU / 0.13f);
            bar = mix(bar, mix(bar, WHITE, 0.3f), wave);
        }
        rect({{0, y + 11}, {width, 2}}, Color{30, 30, 30, 30}.fade(bar_opacity));
        rect({{0, y + 11}, {width * player.burrow.hud_fill, 2}}, bar.fade(bar_opacity));

        float health = (float)max(player.health, 0);
        if (displayed_health < 0 || health > displayed_health) displayed_health = health;
        else displayed_health = approach(displayed_health, health, HEALTH_COUNT_RATE * delta);
        float low = health <= HIT_DAMAGE ? 0.5f + 0.5f * sinf((float)now() * TAU / HEALTH_LOW_PULSE_SECONDS) : 0;
        Color color = mix(WHITE, Color{255, 89, 89, 255}, low).fade(0.75f);

        Sprite* digits = get_sprite("numbers");
        if (!digits) return;
        Vector2 at{2, y};
        for (const char* c = tprint("%d", (s32)ceilf(displayed_health)); *c; c++) {
            draw_frame(*digits, *c - '0', at, color);
            at.x += digits->frame_size.x;
        }
    }
}

// --- Myth's map overlay (Minimap, GameView.DrawMinimap): Escape, or a gamepad's Start or Back,
// toggles it, and the game pauses underneath. It dims the view and draws the room a pixel to a tile,
// its solid ground darker, the player a blinking dot; and since it doubles as the status screen, the
// counts of what's been collected slide in at the top while it's up (DrawCurrencyHud).
// Not ported: other rooms and their exits (there's just the one), the baked world map's colors, map
// pins, save point warps. ---

bool map_open;
static float map_opacity, currency_reveal;
static Font counter_font;

void toggle_map() {
    map_open = !map_open;
    for (Player& p : actors.players) sound(map_open ? "map/open" : "map/close", p.position);
}

// In view pixels, over the lit view (so the CRT takes it too).
void draw_map() {
    const float FADE_SPEED = 10, DIM = 0.8f, REVEAL_SPEED = 3.5f;
    const Color ROOM_FILL = Color::rgb(0x241a3e).fade(50.0f / 255);  // premultiplied, as Foster's Color(rgb, alpha) is
    const Color WALL{0, 0, 0, 80};
    map_opacity = approach(map_opacity, map_open ? 1.0f : 0.0f, FADE_SPEED * delta);
    currency_reveal = approach(currency_reveal, map_open ? 1.0f : 0.0f, REVEAL_SPEED * delta);

    if (map_opacity > 0.001f) {
        rect({{}, VIEW}, Color{0, 0, 0, 255}.fade(DIM * map_opacity));
        // Centered on the room, a pixel per tile.
        Vector2 origin = (VIEW * 0.5f - room.size.vector() * 0.5f).floor();
        rect({origin, room.size.vector()}, ROOM_FILL.fade(map_opacity));
        for (s32 y = 0; y < room.size.y; y++) for (s32 x = 0; x < room.size.x; x++)
            if (room.tiles[size_t(y) * room.size.x + x] != '0') rect({origin + Point2{x, y}.vector(), {1, 1}}, WALL.fade(map_opacity));
        // The player blinks: off a fifth of a second, on three fifths.
        if (fmod_cycling((float)now(), 0.8f) >= 0.2f) for (Player& p : actors.players) {
            Vector2 at = origin + p.position.vector() / TILE_SIZE;
            rect({at.floor() - Vector2{1, 1}, {2, 2}}, WHITE.fade(map_opacity));
        }
    }

    if (currency_reveal > 0.001f) {
        if (!counter_font.texture.gpu) counter_font = load_font(asset("Fonts/dogica.ttf"), 8).value_or(Font{});
        float ease = currency_reveal * currency_reveal * (3 - 2 * currency_reveal);
        Color tint = WHITE.fade(ease);
        float x = 4, y = -16 + 20 * ease;
        if (Sprite* mycelon = get_sprite("mycelon")) { draw_frame(*mycelon, 0, {x, y}, tint); x += mycelon->frame_size.x + 3; }
        text(counter_font, "0", {x, y}, tint);  // nothing to collect in this room yet
        x += text_size(counter_font, "0").x + 8;
        if (Sprite* duck = get_sprite("Props/duck")) {
            push_matrix({x, y}, 0.5f);
            draw_frame(*duck, 0, {}, tint);
            pop_matrix();
            x += duck->frame_size.x * 0.5f + 3;
        }
        text(counter_font, "0", {x, y}, tint);
    }
}
