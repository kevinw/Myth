# Ableton → FMOD dynamic music workflow

Exploration notes, started 2026-09-24. Nothing here is built yet. Goal: author adaptive music in Ableton and generate the FMOD side (parameters, effects, automation, timeline logic) instead of rebuilding it by hand after every WAV export.

## Where we are today

- Music is exported from Ableton as WAVs and placed into FMOD by hand.
- `event:/background_music` is an **Area** label parameter sheet holding six nested events (Calm, Intense, Curious, Resting, Strange, HQ). Each is one WAV with a transition marker looping back to a named marker. Changing Area switches songs immediately, not on the beat.
- `RoomMusicMuffle` (see `ApplyRoomMusicMuffle` in `Game.Audio.cs`) is a hand-built case of this idea: a global parameter automating ThreeEQ mid/high gains on the music.
- `Myth/Assets/Sources/Sounds/FMOD/Scripts/` already holds FMOD Studio JS menu scripts (`Myth\New SFX Event`), so the FMOD half of a pipeline fits an existing pattern.

## Prior art and building blocks

- **FMOD Studio script server**: TCP port 3663, accepts JavaScript and evaluates it in the running editor (Preferences → Interface → Script Server). MCP wrappers exist: [raffyknowsnothing/fmod-studio-mcp](https://github.com/raffyknowsnothing/fmod-studio-mcp), [Dans_Fmod_Studio_MCP](https://github.com/xDarkzx/Dans_Fmod_Studio_MCP). References: [Scripting API basics](https://alessandrofama.com/tutorials/fmod/fmod-studio/scripting-api-basics), [Eden Jeffrey's FMOD JS scripts](https://edenjeffrey.com/2023/10/26/fmod-automation-js-scripts/).
- **`.als` is gzipped XML**: devices, rack macros, macro mapping ranges, locators, scenes, follow actions and automation can all be read without opening Live ([format notes](https://github.com/Qpai/ableton-als-file-format), [ableton-parser](https://github.com/jake-g/ableton-parser), [ableton-project-processor](https://github.com/hodel33/ableton-project-processor)).
- **The usual industry workflow is manual**: export stems, then rebuild layers and automation in FMOD ([vertical re-orchestration](https://alessandrofama.com/tutorials/fmod/fmod-studio/vertical-reorchestration)). We didn't find a tool that carries Ableton's mapping intent into FMOD.
- **Ableton can't render from a script**: neither the Live Object Model nor our Ableton MCP has an export call. The MCP *can* set device parameters.
- Unverified: the exact FMOD JS calls for creating automators and curves. Check them in the 3663 terminal before designing around them.

## Export: one manual step, and that's fine

The mapping comes from the `.als`, so only audio needs a render, and it's already a manual step.

- **Stale-stem detection**: hash each track's XML in the `.als` at sync time and report which stems changed since they were last exported.
- **Render variant**: the tool writes a sibling `Song.fmod-render.als` (plain XML edits):
  - `fmod:` macros set to neutral, which gives dry stems.
  - Session scenes laid out end-to-end in the Arrangement, with a few bars of gap after each for reverb/delay tails. (Export Audio only renders the Arrangement.)
  - Export all individual tracks from it once; the tool slices the WAVs per scene, since it knows the offsets.
- Full automation is possible but not worth starting with: real-time bouncing by recording into the Arrangement through Live's Python API, or AppleScript driving the export dialog.

## Vertical: rack macros are FMOD parameters

Wrap devices in an Audio Effect Rack and name a macro `fmod:Danger` (or `fmod:global:Danger`).

| Ableton | FMOD |
|---|---|
| Macro name `fmod:Danger` | Parameter `Danger`, local or global |
| Macro → device param, min/max range | Effect on that track, automated against the parameter |
| Macro mapping range and curve | Automation curve points |
| Macro automation in the arrangement | Guide for what the game should do / preview |

Turning the macro in Ableton approximates what the game will do.

### Translate vs. bake, chosen per macro (e.g. `fmod:Danger` vs `fmod:Danger~bake`)

- **Translate**: rebuild the effect with FMOD's DSP. Cheap, one stream, sweeps continuously. It only suits simple devices and won't sound identical.
- **Bake**: render the stem at macro 0 and at macro 1, then crossfade between them in FMOD. Sounds exactly like Ableton and works for any device. It costs 2× streams and memory (watch the browser build), and crossfading a filtered copy against a dry one smears phase midway, so it's wrong for filter sweeps.

### Device mapping

Clean 1:1 matches (translate them):

| Ableton | FMOD |
|---|---|
| Utility gain / track volume | Fader volume or Gain |
| Utility pan / width | Panner or Channel Mix |
| EQ Three / EQ Eight (few bands) | ThreeEQ / Multiband EQ |
| Auto Filter LP/HP, Clean circuit | Multiband EQ lowpass/highpass |
| Auto Pan in tremolo mode | Tremolo |
| Chorus-Ensemble, Phaser-Flanger | Chorus, Flanger |
| Delay | Delay |
| Compressor / Glue, Limiter | Compressor, Limiter |
| Sidechain compression | Sidechain + Compressor. Ducking stays correct even when the source layer is muted or swapped at runtime. |
| Clip transpose | Pitch Shifter or event pitch |

Modulators: LFOs in Auto Filter, Auto Pan or Max for Live map to FMOD's LFO modulator, so gameplay can drive rate or depth. Volume envelopes map to the ADSR modulator.

Impulse-response capture: put a one-sample click through a track's static (linear, unmodulated) chain in Ableton, export it with the stems, and load it into FMOD's Convolution Reverb. That reproduces reverb, EQ curves and fixed filters exactly.

Poor or no match, so bake: Saturator/Roar (FMOD Distortion is crude), Redux, Grain Delay, spectral effects, synth timbre macros.

## Horizontal: Session View scenes become the FMOD timeline

Probably the biggest win, because timeline logic is tedious to build by hand and translates exactly.

| Session View | FMOD (one event per Ableton set) |
|---|---|
| Scene | Timeline section: destination marker + loop region |
| Clip in a scene | Instrument on that track's audio track within the section |
| Scene name | Marker name, and the **Area** label the game already sets |
| Scene follow action (Next / Jump / Any, with chance %) | Transition marker at section end with a probability condition |
| Launch quantization | Transition region quantization |
| Scene launched to move between two states | Bridge section: transition region → bridge → falls through to target |
| Scene tempo / time signature | Tempo markers |
| Clip follow actions within a track (fill variations) | Multi instrument playlist |

Game code doesn't change: `music.SetParameterLabel("Area", area)` stays, and transitions become bar-quantized with composed bridges.

### Scene naming grammar (draft)

- `Calm`: a state. Area == Calm targets it.
- `Calm.2`: a variation of Calm, reached through follow actions.
- `Calm>Tense`: bridge from Calm to Tense; follow action Next falls through to Tense.

```
             Pads   Bass   Drums  Lead
Calm         ▶      ▶      ·      ·      follow: 8 bars → 60% again / 40% Calm.2
Calm.2       ▶      ▶      ▶      ·      follow: 8 bars → Calm
Calm>Tense   ▶      ·      fill   ·      follow: Next
Tense        ▶      ▶      ▶      ▶      loops
```

generates

```
timeline │Calm·········│Calm.2·······│Calm>Tense│Tense··············│
loop     │[==========] │             │          │[=================]│
logic    │  end◆ 40%→Calm.2  end◆→Calm   end◆→Tense                  │
regions  │[Area==Tense → Calm>Tense, quantize: bar ]                 │
         │[Area==Calm  ←─────────────────────────────── quantize: bar]
```

Launching scenes in Ableton with the same quantization and follow actions is the FMOD behaviour, so the adaptive logic gets auditioned while composing.

FMOD allows one probability condition per marker, so the tool turns "Any, 40/30/30" into a chain of markers (40%, then 50% of the remainder, ...).

### Open questions to prototype

- **Tails on jumps**: when FMOD jumps sections, ringing audio gets cut off (Ableton avoids this because its devices keep running). Candidates: put the rendered tail on the transition timeline, or play it as an async, non-cutoff instrument. Test one real transition first, because this decides whether transitions sound composed or edited.
- **Clips shorter than their scene** (e.g. 3 bars in a 4-bar scene): instruments with looping playback, but verify.
- **Which layer owns what**: scenes handle horizontal resequencing and `fmod:` macros handle vertical changes within a scene, both authored in the same set.

## Structure mapping outside Session View

Arrangement locators such as `loop:Explore`, `dest:Combat` and `>Combat` become loop regions, destination markers and transition regions. Group tracks become FMOD group tracks, and track colors carry over.

## Cross-system ideas

- **Music → visuals**: automation lanes on a macro like `game:TorchPulse` export as a curve keyed to the music's timeline position, and the game samples it using the event's timeline position. Torch flicker and emissive props could follow the song's actual dynamics. FMOD beat/marker callbacks also provide downbeats.
- **Game → Ableton while composing**: the running game sends Area changes to Live (MCP or OSC), which launches the matching scene. Play the real level with Ableton as the music engine and tune bridges and quantization before exporting.

## Next steps

1. Pilot: rebuild `RoomMusicMuffle` through the macro path, since we know what the correct result sounds like.
2. Throwaway `.als` dump of one existing song: devices, macros, locators, scenes and follow actions. This shows how much of the current writing would map.
3. Session prototype: two areas plus one bridge, including the tails question.
4. Pieces to build: a reader in `Myth/tools/` (`.als` → JSON), plus an idempotent `Myth\Sync From Ableton` FMOD script that matches tracks, effects and parameters by name.
