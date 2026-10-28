// Bud's barks (Game.BudStory.cs, Game.Story.cs): short lines when something happens, drawn
// from a deck so none repeats too soon, the highest priority winning. He speaks them the way
// OutLoud.cs does: the line types out in a bubble pinned where he said it, with a blip of a
// baked German syllable per English syllable, its vowel walked from the line's German
// translation, the pitch settling at the end of a statement and rising on a question. While he
// talks, a leader line from him to the words pulses with his voice.

Trigger :: enum u8 { ROOM_ENTERED; PLAYER_HURT; ENEMY_KILLED; PLAYER_IDLE; }

bark :: (trigger: Trigger) {
    if story.speech.length return;  // one line at a time (Game.CanShowSpeech)
    candidates: [16] struct { set, line: int; };
    count, best := 0, -1;
    for set, s: BARKS {
        if set.trigger != trigger || set.chance < 1 && random_get_zero_to_one() >= set.chance continue;
        for set.lines {
            next := story.next_play[s][it_index];
            if set.redraw == ONCE && next < 0 || story.plays < next continue;
            if set.priority > best { best = set.priority; count = 0; }
            if set.priority == best && count < candidates.count { candidates[count] = .{s, it_index}; count += 1; }
        }
    }
    if !count return;
    pick := candidates[random_get() % cast(u64) count];
    set := BARKS[pick.set];
    story.plays += 1;
    story.next_play[pick.set][pick.line] = ifx set.redraw == ONCE then -1 else story.plays + set.redraw;
    for context.actors.buds { say(set.lines[pick.line], to_vector(it.position) + Vector2.{0, -32}); break; }
}

update_story :: () {
    // Ten seconds without moving or pressing anything.
    for player: context.actors.players {
        moved := length(to_vector(player.position - story.last_player)) > 0.5;
        story.last_player = player.position;
        if moved || held.count { story.idle_time = 0; story.idle_triggered = false; }
        else {
            story.idle_time += delta;
            if !story.idle_triggered && story.idle_time >= IDLE_SECONDS { story.idle_triggered = true; bark(.PLAYER_IDLE); }
        }
    }

    s := *story.speech;
    if !s.length return;
    s.age += delta;
    while s.visible < s.length && s.reveal_at[s.visible] <= s.age s.visible += 1;
    while s.fired < s.blip_count && s.blips[s.fired].char_index < s.visible {
        b := s.blips[s.fired];
        voice(b.clip, b.pitch, b.gain, s.at);
        s.voice_gain = clamp(b.gain, 0, 1);
        s.voice_hold = VOICE_HOLD;
        s.fired += 1;
    }
    s.voice_hold = max(0.0, s.voice_hold - delta);
    active := s.voice_hold > 0;
    s.voice_level = approach(s.voice_level, ifx active then s.voice_gain else 0, (ifx active then VOICE_ATTACK_RATE else VOICE_RELEASE_RATE) * delta);
    if s.age >= s.duration s.length = 0;
}

// In view pixels over the lit world (GameView.DrawStorySpeechBubble).
draw_speech :: (view: Vector2) {
    s := *story.speech;
    if !s.length return;
    said := string.{s.length, s.text.data};
    size := Vector2.{s.width + SPEECH_PADDING.x * 2, s.line_count * s.line_height + SPEECH_PADDING.y * 2};
    // Up and to the left of where it was said, slid back onto the screen.
    anchor := s.at - view;
    box := Rect.{.{clamp_to_view(anchor.x - size.x + SPEECH_PADDING.x, size.x, VIEW.x), clamp_to_view(anchor.y - size.y + SPEECH_PADDING.y, size.y, VIEW.y)}, size};

    for context.actors.buds if s.voice_level > 0 {
        bounds := hitbox_rect(it);
        speaker_center := Vector2.{bounds.x + bounds.w * 0.5, bounds.y + bounds.h * 0.5} - view;
        words := clamp_point(speaker_center, box.pos + SPEECH_PADDING, box.pos + box.size - SPEECH_PADDING);
        speaker := clamp_point(words + view, Vector2.{xx bounds.x, xx bounds.y}, Vector2.{xx (bounds.x + bounds.w), xx (bounds.y + bounds.h)}) - view;
        gap := words - speaker;
        if length(gap) <= LEADER_SPEAKER_GAP + LEADER_TEXT_GAP break;
        direction := unit_vector(gap);
        from, to := speaker + direction * LEADER_SPEAKER_GAP, words - direction * LEADER_TEXT_GAP;
        opacity := sqrt(s.voice_level);
        line(from + Vector2.{1, 1}, to + Vector2.{1, 1}, fade(SPEECH_SHADOW, opacity));
        line(from, to, fade(BUD_SPEECH_COLOR, opacity));
        break;
    }

    remaining := s.visible;
    for 0..s.line_count-1 {
        run := s.lines[it];
        full := slice(said, run.start, run.end - run.start);
        shown := slice(full, 0, clamp(remaining, 0, full.count));
        at := box.pos + SPEECH_PADDING + Vector2.{(s.width - run.width) * 0.5, xx (it * s.line_height)};
        at = .{floor(at.x + 0.5), floor(at.y + 0.5)};
        text(speech_font, shown, at + Vector2.{1, 1}, SPEECH_SHADOW);
        text(speech_font, shown, at, BUD_SPEECH_COLOR);
        remaining -= full.count + 1;  // and the space that joined it to the next
        if remaining <= 0 break;
    }
}

load_story :: () {
    speech_font = load_font(tprint("%/Fonts/m3x6.ttf", ASSETS), 16);
    manifest_text := read_entire_file(tprint("%/Voice/manifest.json", ASSETS), log_errors = false);
    manifest := parse_json(manifest_text);
    names := get(manifest, "names");
    vowels := get(manifest, "vowels");
    clip_names = NewArray(names.items.count, string);
    clip_vowels = NewArray(vowels.items.count, s8);
    for names.items clip_names[it_index] = as_string(it);
    for vowels.items clip_vowels[it_index] = vowel_of(as_string(it), 0);
    german = parse_json(read_entire_file(tprint("%/Voice/voice-de.json", ASSETS), log_errors = false));
    bark(.ROOM_ENTERED);
}

#scope_file

Bark_Set :: struct {
    trigger: Trigger;
    priority: s32;
    redraw: s32;      // plays before a line can come back, or ONCE
    chance := 1.0;    // rolled once for the whole set
    lines: [] string;
}
ONCE :: -1;

// Game.BudStory.cs, minus the reveal door (no doors yet) and Bud being struck (off there too).
BARKS :: Bark_Set.[
    .{trigger = .ROOM_ENTERED, priority = 10, redraw = ONCE, lines = .["Come on.", "OK..."]},
    .{trigger = .PLAYER_HURT, priority = 2, redraw = 4, lines = .["Ouch!", "Still with me?"]},
    .{trigger = .ENEMY_KILLED, priority = 1, redraw = 5, chance = 0.05, lines = .["Ouch.", "Woah."]},
    .{trigger = .PLAYER_IDLE, priority = 1, redraw = 3, lines = .["I'm hungry. I told you we should have packed a lunch."]},
];

IDLE_SECONDS :: 10.0;
MAX_SPEECH :: 160;
MAX_LINES :: 8;
SPEECH_MAX_WIDTH :: 120.0;
SPEECH_PADDING :: Vector2.{6, 5};
SPEECH_SHADOW :: Color.{0, 0, 0, 170};
BUD_SPEECH_COLOR :: Color.{185, 214, 255, 255};  // his blue (7fb6ff) lifted 45% toward white
LEADER_SPEAKER_GAP :: 1.5;
LEADER_TEXT_GAP :: 1.0;
VOICE_EVENT :: "voice/Kev";
VOICE_ATTACK_RATE :: 24.0;
VOICE_HOLD :: 0.08;
VOICE_RELEASE_RATE :: 3.5;
// Bud's VoiceProfile: a small companion's higher, quicker voice. Pitch in semitones.
BUD_PITCH :: 4.0;
BUD_PITCH_VARIANCE :: 2.0;
BUD_CHARS_PER_SECOND :: 26.0;
QUESTION_RISE :: 4.0;
STATEMENT_FALL :: 1.5;

Blip :: struct { char_index: int; clip: int; pitch: float; gain: float; }

Speech :: struct {
    text: [MAX_SPEECH] u8;  // copied: a hot reload unloads the build its literal lived in
    length: int;            // 0 when nobody's talking
    at: Vector2;            // where it was said, in room pixels
    age, duration: float;
    visible: int;
    reveal_at: [MAX_SPEECH] float;
    blips: [MAX_SPEECH] Blip;
    blip_count, fired: int;
    lines: [MAX_LINES] struct { start, end: int; width: float; };
    line_count: int;
    width: float;
    line_height: s32;
    voice_level, voice_gain, voice_hold: float;
}

story: struct {
    plays: s32;
    next_play: [BARKS.count][4] s32;
    idle_time: float;
    idle_triggered: bool;
    last_player: Point2;
    speech: Speech;
};
speech_font: Font;
clip_names: [] string;
clip_vowels: [] s8;  // per clip: 0..4 for a e i o u
german: Json;        // English line -> its German translation

// Game.ShowSpeech + Utterance.Build.
say :: (line: string, at: Vector2) {
    s := *story.speech;
    s.* = .{};
    s.length = min(line.count, MAX_SPEECH);
    memcpy(s.text.data, line.data, s.length);
    text := string.{s.length, s.text.data};
    s.at = at;

    time := 0.0;
    for 0..s.length-1 {
        s.reveal_at[it] = time;
        c := text[it];
        time += 1 / BUD_CHARS_PER_SECOND + ifx c == #char "," || c == #char ";" || c == #char ":" then 0.18 else ifx c == #char "." || c == #char "!" || c == #char "?" then 0.32 else 0.0;
    }
    s.duration = s.reveal_at[s.length - 1] + clamp(s.length * 0.04, 1.2, 3);

    translation := as_string(get(german, line));
    german_vowels: [MAX_SPEECH] s8;
    german_count, previous := 0, false;
    i := 0;
    while i < translation.count {
        v := vowel_of(translation, i);
        if v >= 0 && !previous && german_count < german_vowels.count { german_vowels[german_count] = v; german_count += 1; }
        previous = v >= 0;
        i += ifx translation[i] == 0xC3 then 2 else 1;
    }

    rng := fnv(text);
    previous = false;
    for 0..s.length-1 {
        vowel := vowel_of(text, it) >= 0;
        if vowel && !previous && clip_names.count {
            clip := -1;
            if german_count {
                target := german_vowels[s.blip_count % german_count];
                matches := 0;
                for clip_vowels if it == target matches += 1;
                if matches {
                    n := next(*rng, matches);
                    for clip_vowels if it == target { if n == 0 { clip = it_index; break; } n -= 1; }
                }
            }
            if clip < 0 clip = next(*rng, clip_names.count);
            s.blips[s.blip_count] = .{it, clip, BUD_PITCH + (next_float(*rng) * 2 - 1) * BUD_PITCH_VARIANCE, 1};
            s.blip_count += 1;
        }
        previous = vowel;
    }
    // Settle (statement) or lift (question) the last few blips.
    question := false;
    for < 0..s.length-1 if !is_any(text[it], " \"')") { question = text[it] == #char "?"; break; }
    k := min(3, s.blip_count);
    for j: 0..k-1 s.blips[s.blip_count - k + j].pitch += (ifx question then QUESTION_RISE else -STATEMENT_FALL) * (j + 1.0) / k;

    // Wrap into lines no wider than SPEECH_MAX_WIDTH, once.
    s.line_height = cast(s32) ceil(speech_font.line_height * 0.7) + 1;
    start, end := 0, 0;
    while end <= s.length {
        word_end := end;
        while word_end < s.length && text[word_end] != #char " " word_end += 1;
        if end > start && text_size(speech_font, slice(text, start, word_end - start)).x > SPEECH_MAX_WIDTH && s.line_count < MAX_LINES - 1 {
            add_line(s, text, start, end - 1);
            start = end;
        }
        if word_end == s.length { add_line(s, text, start, word_end); break; }
        end = word_end + 1;
    }
}

add_line :: (s: *Speech, text: string, start: int, end: int) {
    width := ceil(text_size(speech_font, slice(text, start, end - start)).x);
    s.lines[s.line_count] = .{start, end, width};
    s.line_count += 1;
    s.width = max(s.width, width);
}

// Game.PlayVoiceSyllable: one baked syllable, named so FMOD shows which.
voice :: (clip: int, pitch: float, gain: float, at: Vector2) {
    instance := start_sound(VOICE_EVENT, at);
    if !instance return;
    FMOD_Studio_EventInstance_SetVolume(instance, clamp(gain, 0, 1));
    FMOD_Studio_EventInstance_SetParameterByNameWithLabel(instance, "syllable", temp_c_string(clip_names[clip]), 1);
    FMOD_Studio_EventInstance_SetParameterByName(instance, "pitch", pitch, 0);
    finish_sound(instance);
}

// 0..4 for a e i o u (y counts as i; German umlauts fold to their vowel), -1 otherwise.
vowel_of :: (s: string, i: int) -> s8 {
    c := s[i];
    if c == 0xC3 && i + 1 < s.count {
        if s[i + 1] == {
            case 0xA4; #through; case 0x84; return 1;  // ä Ä
            case 0xB6; #through; case 0x96; return 3;  // ö Ö
            case 0xBC; #through; case 0x9C; return 4;  // ü Ü
        }
        return -1;
    }
    if c >= #char "A" && c <= #char "Z" c += 32;
    if c == {
        case #char "a"; return 0;
        case #char "e"; return 1;
        case #char "i"; #through; case #char "y"; return 2;
        case #char "o"; return 3;
        case #char "u"; return 4;
    }
    return -1;
}

// OutLoud's xorshift32, seeded with Hash32.Of (FNV-1a), so a line always babbles the same.
fnv :: (s: string) -> u32 {
    hash: u32 = 2166136261;
    for 0..s.count-1 hash = (hash ^ s[it]) * 16777619;
    return ifx hash then hash else 1;
}
next_u32 :: (state: *u32) -> u32 {
    x := state.*;
    x ^= x << 13; x ^= x >> 17; x ^= x << 5;
    state.* = x;
    return x;
}
next :: (state: *u32, max: int) -> int { return ifx max <= 0 then 0 else cast(int) (next_u32(state) % cast(u32) max); }
next_float :: (state: *u32) -> float { return cast(float) (next_u32(state) >> 8) / 16777216.0; }

is_any :: (c: u8, set: string) -> bool { for 0..set.count-1 if set[it] == c return true; return false; }

clamp_to_view :: (position: float, size: float, view: float) -> float {
    MARGIN :: 3.0;
    return floor(max(MARGIN, min(position, view - MARGIN - size)) + 0.5);
}

clamp_point :: (v: Vector2, lo: Vector2, hi: Vector2) -> Vector2 { return .{clamp(v.x, lo.x, hi.x), clamp(v.y, lo.y, hi.y)}; }

line :: (from: Vector2, to: Vector2, color: Color) {
    side := unit_vector(Vector2.{to.y - from.y, from.x - to.x});  // weight 2: one pixel either side
    polygon(from + side, to + side, to - side, from - side, color);
}

#import "Basic";
#import "File";
#import "Math";
#import "String";
#import "Random";
#import "FMOD";
#import "Kit";
