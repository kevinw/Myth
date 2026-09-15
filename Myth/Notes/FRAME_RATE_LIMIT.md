# Frame Rate Limit

## Goal

Add an optional frame-rate limit for controlling power, heat, and runaway rendering when VSync is disabled, without changing Myth's simulation timestep.

The default remains:

```text
VSync: on
Frame limit: off
```

With that combination, presentation at the display refresh rate already provides the normal frame limit.

## Do not reuse fixed-step updates

Myth's existing `fps` console command selects `UpdateMode.FixedStep(fps)`. That changes both the simulation timestep and render rate, so using 30 or 40 FPS can affect physics, game feel, and replay results.

The Options-menu setting should be a real frame-loop limiter. Gameplay should remain on `UpdateMode.UnlockedStep()` with its normal variable delta; the limiter only controls when the next complete update/render iteration begins.

## Foster API

Implement the reusable functionality in Foster:

```csharp
public int FrameRateLimit { get; set; } // 0 means unlimited
```

The limiter should:

- Use an absolute next-frame deadline rather than repeatedly adding a relative sleep, avoiding accumulated timing drift.
- Wait at the beginning of the frame, immediately before polling input, so input is sampled after the wait and as close to simulation as possible.
- Recover cleanly after a late frame instead of trying to catch up with several uncapped frames.
- Use SDL's precise delay support on desktop. Browser and platform-controlled presentation paths may need to ignore or implement the setting differently.

Waiting after input, simulation, or rendering would make the freshly built frame sit idle and increase input age.

## Myth option

Add a persistent Options-menu button which cycles through:

```text
frame limit: off
frame limit: 30
frame limit: 60
frame limit: 90
frame limit: 120
```

Persist the selected value in `window.json`, like VSync. A matching CLI argument would make performance tests repeatable:

```text
--fps-cap off
--fps-cap 60
--fps-cap 90
```

## VSync interaction

| VSync | Frame limit | Result |
|---|---:|---|
| On | Off | Recommended default; display refresh controls pacing |
| Off | Off | Lowest latency/highest throughput, with tearing and maximum power use |
| Off | 30/60/90/120 | Lower power and heat, but tearing remains possible |
| On | Below refresh | Tear-free battery saving, but only smooth when the cap and display refresh cooperate |

An FPS cap and frames-in-flight solve different problems. Frames-in-flight limits how much submitted GPU work may queue ahead; an FPS cap limits how frequently the game produces frames.

## Steam Deck

The Steam Deck performance menu can coordinate its display refresh rate and frame limiter more effectively than the game. For example, 40 FPS has even cadence when the display is running at 40 or 80 Hz, but not on a fixed 60 Hz display.

For that reason, do not initially offer 40 or 45 FPS in Myth. Deck users can select those rates together with a compatible display refresh through the system UI. The in-game limiter remains useful on desktop and as a fallback.

If Foster later exposes the current display refresh rate, Myth could offer refresh-aware limits rather than a fixed list.

## Possible later work

Foster currently exposes VSync as a boolean:

- On selects FIFO/VSync presentation.
- Off prefers Immediate, then falls back to Mailbox and finally FIFO.

A future explicit presentation-mode option could distinguish FIFO, Mailbox, and Immediate. Mailbox combined with an appropriate cap may offer a useful tear-free, lower-latency mode, but that is separate from the initial frame limiter.

## Implementation order

1. Add the independent frame limiter to Foster's main loop.
2. Verify that waiting happens before event polling and input sampling.
3. Add the persisted Myth Options-menu setting.
4. Add the optional `--fps-cap` CLI override.
5. Test VSync on/off and one/two frames in flight on Steam Deck and desktop.
