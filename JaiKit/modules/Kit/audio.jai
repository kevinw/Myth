// A tiny sfxr-style synth (ported from SokobanMinc). Voices are rendered on the main
// thread and queued on an SDL audio stream each frame, so none of our code runs on the
// audio thread, which matters when hotload swaps the code out from under it.

Sound :: struct {
    freq: float;        // Hz at the start
    sweep: float;       // octaves per second
    attack: float;      // seconds to swell in
    decay: float;       // seconds to fade out
    square: float;      // 0 = sine, 1 = square
    noise: float;       // 0 = tone, 1 = noise (resampled at freq)
    volume: float = 1;
    delay: float;       // seconds before it starts
}

play :: (s: Sound) {
    // A free voice, else steal the one furthest along.
    slot := 0;
    for voices {
        if !it.active { slot = it_index; break; }
        if it.t > voices[slot].t slot = it_index;
    }
    voices[slot] = .{sound = s, active = true, t = -s.delay, freq = s.freq, sweep = pow(2, s.sweep / AUDIO_RATE), rng = 0x9E3779B9 + cast(u32) slot};
}

// Call once a frame: tops the stream up to AUDIO_LATENCY seconds of sound.
pump_audio :: () {
    if !stream {
        if audio_failed return;
        spec := SDL_AudioSpec.{.F32, 1, AUDIO_RATE};
        if SDL_InitSubSystem(.AUDIO) stream = SDL_OpenAudioDeviceStream(SDL_AUDIO_DEVICE_DEFAULT_PLAYBACK, *spec, null, null);
        if !stream { log_error("no audio: %", to_string(SDL_GetError())); audio_failed = true; return; }
        SDL_ResumeAudioStreamDevice(stream);
    }

    n := min(cast(s32) (AUDIO_RATE * AUDIO_LATENCY) - SDL_GetAudioStreamQueued(stream) / size_of(float), buffer.count);
    if n <= 0 return;
    for i: 0..n-1 {
        mix: float;
        for *voices if it.active mix += sample(it);
        buffer[i] = mix / (1 + abs(mix));  // soft clip
    }
    SDL_PutAudioStreamData(stream, buffer.data, n * size_of(float));
}

#scope_file

AUDIO_RATE    :: 44100;
AUDIO_LATENCY :: 0.05;

Voice :: struct {
    sound: Sound;
    active: bool;
    t: float;       // seconds since start; negative while delayed
    freq: float;
    sweep: float;   // per-sample frequency multiplier
    phase: float;
    rng: u32;
    noise: float;
}

stream: *SDL_AudioStream;
audio_failed: bool;
voices: [16] Voice;
buffer: [4096] float;

sample :: (v: *Voice) -> float {
    s := *v.sound;
    v.t += 1.0 / AUDIO_RATE;
    if v.t < 0 return 0;
    if v.t > s.attack + s.decay { v.active = false; return 0; }

    envelope := ifx v.t < s.attack then v.t / s.attack else 1 - (v.t - s.attack) / s.decay;
    v.freq *= v.sweep;
    v.phase += v.freq / AUDIO_RATE;
    if v.phase >= 1 {
        v.phase -= floor(v.phase);
        v.rng ^= v.rng << 13;
        v.rng ^= v.rng >> 17;
        v.rng ^= v.rng << 5;
        v.noise = cast(float) v.rng / 2147483648 - 1;
    }
    sine := sin(v.phase * TAU);
    square := ifx v.phase < 0.5 then 1.0 else -1.0;
    tone := sine + (square - sine) * s.square;
    return (tone + (v.noise - tone) * s.noise) * envelope * envelope * s.volume;
}

#import "Basic";
#import "Math";
#import "SDL3";
