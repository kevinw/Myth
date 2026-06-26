# OutLoud

This is intended to be a subproject usable by Myth and other future games.

## Overview

Myth's Bud character already follows you around and says things via text on the screen. We'd like to make it more compelling by adding generated voice sounds to each utterance, inspired by the game "Tux and Fanny," where the characters speak with subtitles, but a Russian translation in computerized-sounding syllables:

"Yes, it is Russian. Albert can share the secret of the voice sources, but for the game voices, I took a few lines from the film and sliced them up into tiny single syllable sounds. Then when any character says something, I randomly play back a collection of those syllables one after the other. It's not totally random - I think I seed it with the length of the line of dialog so interacting with the same thing multiple times will always play the same collection of syllables."

I'd like to do this, but for German, since that's the language I've spent the most time learning, and so it will hopefully not be too much appropriating or exoticizing.

## Technical / Experiential Wishlist

- Use existing libraries when possible.
- Not looking for modern-style fully human-sounding voices.
- Bake syllables: offline
- Ideally - Play syllables: online. This could keep the build size low and allow expressivity and reactivity to game state.
- Questions should sound different than statements.
- Text can be augmented with: pauses (shown visually too), emotion (excited, sad, disappointed) which get reflected in, for example, the pitch selection.
- We currently use FMOD for audio. Can we drive FMOD at "a lower level" to play syllables efficiently and programmatically?

## Design

We don't need FMOD Core / low-level: Foster's `Audio` wrapper is FMOD Studio only (and the
browser build is a thin JS bridge with the same surface), so the cross-platform-safe move is
to do the per-syllable *sequencing* in C# and fire each syllable as a tiny Studio event. The
reveal cursor of the speech bubble is the master clock, so there's no audio/text drift and no
need for per-clip duration metadata.

- **`OutLoud.cs`** — engine-agnostic core, no Foster/FMOD dependency. `Utterance.Build(text,
  emotion, profile)` turns a line into a deterministic reveal+blip plan; `Advance(elapsed, sink)`
  reveals characters and fires blips through an `ISyllableSink`.
  - Determinism: blip syllables are picked from an FNV-1a hash of the line + xorshift RNG, so the
    same line always sounds the same (and equal-length lines don't collide).
  - Subtitles are **English**; only the synthesized voice is computerized German. Each blip plays
    a baked German syllable clip, timed to the English text's reveal — one blip per **vowel group**
    (≈ one spoken syllable of the line); consonant runs stay silent.
  - Pitch is in **semitones**. Emotion shifts base pitch / variance / reveal rate; a trailing `?`
    lifts the last few blips, statements settle. Punctuation inserts reveal+audio pauses (which
    are visible too, since they pause the typewriter).
- **Myth side** (`Game.Audio.cs` `VoiceSink`/`PlayVoiceSyllable`, `Game.Story.cs` reveal,
  `GameView.cs` typewriter render) — the only FMOD-aware code. Each speaker has a voice; the sink
  fires `event:/voice/<Name>` (e.g. `event:/voice/Kev`) with local `syllable` + `pitch` parameters.
  The syllable inventory (labels/vowels) is shared across voices. Storylets may carry an `"emotion"`.

## Vowel-melody colouring

The babble's *timing and count* come from the English subtitle (so it stays glued to the
typewriter), but each blip's *vowel* is walked (cycled) from the line's German translation.
So "Come on." → "Komm schon." biases the blips toward o-ish clips. This needs two baked assets:
per-clip vowel tags (`manifest.json`) and an English→German cache (`voice-de.json`), both under
`Myth/Assets/Voice/` and loaded by `Game.LoadVoice()`. Until they exist, babble falls back to
seeded-random clips. `VoiceSyllableCount` auto-derives from the manifest.

## Workflow (bake offline, play online)

**One command per voice:** `tools/make-voice.sh <Name>` runs the whole pipeline below (needs a
running Audacity with `mod-script-pipe`, and FMOD Studio). It slices, bakes, translates, writes
the voices list, then builds the FMOD events + banks. The only manual bit is pitch automation
(see below). The individual steps, for reference:

1. **Record + label** the syllables in Audacity (one label per syllable, the label text *is* the
   spelling: `ba`, `scho`, `mü`). Save the project under `Myth/Production/Audacity/<Name>/`.
2. `tools/slice-voice.py --project … --name <Name>` — drives a running Audacity to export each
   label as `<text>.ogg` into `Myth/Assets/Sounds/FMOD/Assets/Voice/<Name>/` (inside the FMOD tree
   so `importAudioFile` can bind it). Needs `mod-script-pipe` enabled.
3. `tools/bake-syllables.sh <ogg_dir> Myth/Assets/Voice` — writes `manifest.json` (count + per-clip
   names + vowels). One shared manifest; voices are expected to share the same syllable inventory.
4. `tools/translate-voice.py` — collects every English line (`.Say(...)` literals + `bud.json`
   text), translates new ones to German via MyMemory (free, no key), writes `voice-de.json`.
5. Add `<Name>` to the `VOICES` array in `fmod-create-voice-event.js`, then run it via
   `fmodstudiocl -script … Myth.fspro` — it builds `event:/voice/<Name>` for each voice (params +
   clips, idempotent). Add each event's pitch automation by hand (below), then File > Build.

Point a speaker at a voice with `Game.BudVoiceName` (or the `voice` arg to `ShowBudSpeech`).

## Pitch automation (one manual step per event)

The script can't reliably script automation curves, so per voice event: right-click the master
track's **Pitch** dial → Add Automation → `pitch`, then set the curve linear from `-12` st (left)
to `+12` st (right). Survives until the event is rebuilt (re-running the script recreates events,
so re-add it then).
