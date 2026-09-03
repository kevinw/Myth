# Audio Mixing Guide

This is a practical starting point for mixing a game in FMOD Studio. The numbers here are **reference ranges, not hard standards**. The most important thing is the relationship between sounds, not forcing every event or bus to hit a specific dB value.

## The Most Important Distinction

The dB value on an FMOD fader is **gain**, not absolute loudness.

- `0 dB` means **unity gain**: FMOD is not making the signal louder or quieter.
- A bus set to `0 dB` is not "at maximum volume."
- Actual loudness depends on the source audio, event processing, bus processing, and everything else upstream.

A common mistake is to turn every bus and event down "for headroom" until the entire game ends up unnecessarily quiet.

As a default:

- Leave the **Master bus at 0 dB**.
- Let major category buses such as **SFX** and **Music** start around `0 dB`.
- Create headroom by mixing the actual sources sensibly and watching the combined master output.

## Useful Starting Reference

| Element | Rough starting point |
| --- | --- |
| Master bus fader | `0 dB` |
| Music bus fader | roughly `0 to -6 dB` |
| SFX bus fader | roughly `0 dB` |
| Ambience bus fader | perhaps `-3 to -10 dB` relative to SFX |
| Normal gameplay master peaks | roughly `-6 to -3 dBFS` |
| Occasional largest peaks | around `-2 to -1 dBFS` |
| Final true-peak ceiling | around `-1 dBTP` |
| Busy-gameplay master RMS | very roughly `-20 to -14 dBFS` |

These are only smell-test numbers. They are more useful than rules such as "all footsteps should be -12 dB."

## How to Read FMOD's Meters

FMOD's meters provide both average and peak information.

- The filled meter gives you a useful sense of the recent average level.
- The peak indicator shows brief transient peaks.

Watch the **Master bus during real gameplay**, not only individual sounds while auditioning them in isolation.

A representative gameplay section should include things like:

- music
- ambience
- player movement
- machinery or environmental sounds
- common gameplay effects
- occasional large impacts or dramatic events

## Recommended Mixing Order

### 1. Reset the basic gain structure

Start with:

- Master: `0 dB`
- SFX: `0 dB`
- Music: `0 dB`

Do not immediately add a heavy compressor or limiter to make the game louder.

### 2. Establish the music level

Pick a representative music track and get it to a comfortable listening level.

Professionally mastered music may already be relatively dense and loud. Perceptual loudness measurements such as **LUFS** are more meaningful for sustained material than peak level alone.

Do not worry about matching a single LUFS target for every music asset. The purpose is to establish a stable musical reference against which the rest of the game can be mixed.

### 3. Mix SFX against the music

Do not mix every effect against silence.

Once the music is at a reasonable level, play the game normally and mix effects in context.

For a platformer, a useful perceptual hierarchy might be:

#### Hero sounds

These should clearly command attention.

Examples:

- player damage
- death
- major impacts
- large machinery
- explosions
- major discoveries
- dramatic environmental events

#### Gameplay vocabulary

These need to remain clearly intelligible during music.

Examples:

- jump
- land
- grab
- dash
- interaction
- attacks
- important buddy-character actions

#### Texture

These should add physicality without constantly demanding attention.

Examples:

- footsteps
- clothing
- small debris
- minor mechanical noises
- subtle contact sounds

#### Ambience

Ambience should often sit low enough that the player stops consciously noticing it.

A good test is that muting the ambience suddenly makes the environment feel empty, even though the ambience did not previously feel prominent.

## Master-Bus Sanity Check

Run through a representative busy gameplay section.

A reasonable-looking master meter might resemble:

### Ordinary moments

- RMS: roughly `-20 to -16 dBFS`
- Peaks: roughly `-12 to -6 dBFS`

### Busy or loud moments

- RMS: roughly `-18 to -14 dBFS`
- Peaks: roughly `-6 to -3 dBFS`

### Rare enormous event

- Peak: roughly `-2 to -1 dBFS`

Again, these are not requirements.

They are useful for spotting a mix that is globally much too quiet.

For example, if:

- ordinary gameplay barely peaks above `-15 dBFS`
- the master spends most of its time around `-30 to -22 dBFS`
- even the largest effects never get much above `-12 dBFS`

then the entire mix may simply be too conservative.

In that case, adding several dB to the overall gain structure can be completely reasonable.

## Do Not Mix Toward 0 dBFS

Do not try to make normal gameplay constantly hit `0 dBFS`.

Peak level is not the same thing as perceived loudness.

Two sounds with identical peaks can feel dramatically different in loudness because of:

- duration
- frequency content
- transient shape
- compression
- density
- masking from other sounds

Use the meters to avoid clipping and diagnose gain structure, not as the sole definition of whether something sounds loud enough.

## Use a Reference Game

One of the best ways to calibrate a mix is to compare it with a professionally mixed game that has a similar sonic density.

### Procedure

1. Pick a reference game.
2. Set your computer or console volume to a comfortable level while playing it.
3. Do **not** change the system volume afterward.
4. Alternate between the reference game and your game.
5. Compare perceived loudness and balance.

This will quickly reveal whether your whole mix is, for example, 3 dB or 10 dB quieter than expected.

More importantly, listen to the **relationships**.

You may discover things such as:

- their movement sounds are louder than yours
- their ambience is much quieter
- their music ducks slightly during important actions
- impacts have much stronger transients
- UI sounds sit unusually far forward
- background machinery occupies less frequency space than expected

Those relationships are the actual mix.

## Suggested FMOD Bus Structure

A simple starting hierarchy:

```text
Master
├── Music
├── SFX
├── Ambience
└── UI
```

You may later subdivide SFX into categories such as:

```text
SFX
├── Player
├── Buddy
├── Enemies
├── Environment
└── Interactions
```

Do not create a huge hierarchy until you actually need it.

## Use Snapshots for Contextual Mixing

Do not permanently turn an entire category down merely because another category occasionally needs to dominate.

FMOD snapshots are ideal for temporary relative mix changes.

For example, during a large machinery event:

- duck Music by roughly `2–4 dB`
- perhaps reduce Ambience slightly
- leave the important machinery effect forward

Then restore the normal mix afterward.

Other good snapshot uses include:

- underwater
- pause/menu
- player near death
- dialogue
- entering a large interior space
- dramatic discoveries
- slow motion
- environmental transitions

Small contextual changes can create much more clarity than simply making everything louder.

## Master Compression and Limiting

Get the basic balance right **before** relying on master processing.

A master limiter can be useful once the mix already works.

A reasonable final setup might use:

- a ceiling around `-1 dBTP`
- only occasional gain reduction
- enough limiting to catch unexpected peaks rather than flatten the entire game

If the limiter is constantly working hard, fix the mix or source dynamics first.

Heavy master compression can make a game initially seem louder while reducing:

- impact
- contrast
- transient clarity
- the difference between calm and intense moments

Dynamic range is useful in games.

## A Practical Rule of Thumb

When deciding whether a sound is correctly mixed, ask:

> What should the player's attention be on right now?

Then make the audio hierarchy reinforce that answer.

Do not make every sound equally audible.

A good mix intentionally allows some sounds to:

- dominate
- support
- blend
- disappear

## If the Whole Game Seems Too Quiet

If you have been cautiously reducing every event and bus, try this diagnostic:

1. Return Master to `0 dB`.
2. Return major buses close to `0 dB`.
3. Play a representative busy section.
4. Watch the Master meter.
5. Compare against a reference game at the same system volume.
6. Raise the overall source/event gain structure if the game remains obviously quiet.
7. Rebalance individual effects afterward.

If your mix has lots of unused headroom and sounds noticeably quieter than comparable games, there is no prize for keeping another 10 or 15 dB of empty space above it.

Use the available range while retaining enough headroom for genuinely large moments.

## References

- FMOD Studio documentation: Mixing  
  https://www.fmod.com/docs/2.03/studio/mixing.html
- FMOD Studio documentation: Effect Reference / meters  
  https://www.fmod.com/docs/2.03/studio/effect-reference.html
- AES Loudness Project: Loudness Basics  
  https://aes.org/resources/audio-topics/loudness-project/loudness-basics/
- ITU-T H.872: Safe Listening for Video Gameplay and Esports  
  https://www.itu.int/rec/T-REC-H.872
