# Metroidbrainia Brainstorm

## Surface discoveries — additional brainstorm

The strongest discoveries make players reconsider familiar geometry: "Wait,
that ceiling is a launchpad." Existing ingredients include momentum entering
burrow, movement around corners, outward pops, and a shrinking, pulsing light
trail. The ideas below are proposed extensions, not claims about what already
works.

"Circle this block three times" is a secret handshake unless the block visibly
responds to circling. If each lap winds something up, and reversing unwinds it,
the player can discover a rule and carry that knowledge elsewhere.

### 1. Compress yourself, then launch

Hold toward the solid while burrowed to gather the line into a bright point;
release burrow to launch outward. Charge spends your remaining burrow time,
so positioning and charging compete.

Teach it beneath a low ceiling where accidentally holding into the floor
produces a noticeably harder bump. Later, the same move launches sideways
from walls, downward into brittle floors, or pushes a movable object. One
discovery changes every surface.

### 2. Turn a fall into horizontal travel

A hard landing becomes burrow speed. Fall down a shaft, enter the floor,
follow its curve, and emerge across a gap. The code already carries horizontal
entry momentum; extending that relationship to falling makes height another
resource.

Teach it with a bowl-shaped floor beneath an unavoidable drop. Let the player
overshoot somewhere safe. Later they look at a tall shaft and think, "That's
my run-up."

### 3. A corner lets you choose where momentum goes

Release before a corner to launch one way; ride around it and release to launch
another. Preserve enough travel momentum that a small isolated block becomes
a multidirectional launch point.

Existing corner handling and a generous corner-leap window mean this could be
more about emphasizing and tuning existing behavior. Teach it with two exits
around the same block: one obvious, one tantalizingly reachable. Avoid making
the discovery depend on frame-perfect timing.

### 4. A complete circuit winds up a mechanism

Orbit certain blocks to turn an axle. Clockwise winds; counterclockwise
unwinds. Each edge traversed moves the mechanism a little, with a ratchet
sound and visible tension.

Now "three laps opens the door" follows from something observable. The same
rule could rotate bridges, raise an elevator, or stretch a spring that
launches the block itself. A combination puzzle: lay a bomb while circling,
then let it release the mechanism after you've left.

### 5. You can leave light behind

The fading burrow trail continues to activate light-sensitive things briefly
after you pass. Players first assume a door opens because they touched its
sensor; later they realize it remains open while their trail covers it.

Teach this with two nearby sensors that overlap naturally during ordinary
movement. Later puzzles ask players to stretch a trail around a corner,
reverse across a short section, or leave a bomb to illuminate one sensor
while they reach another. The fading glow itself becomes the timer.

### 6. Being light changes what notices you

A creature that follows your body follows the brightest nearby light while
you're burrowed. Your bomb can become a decoy; a charged launch can draw its
attention; emerging makes you abruptly less interesting.

Teach it through a harmless creature visibly turning to watch your trail.
Later, use the same behavior to lure a dangerous creature off a doorway—or
coax a helpful one onto a switch. This makes the photon premise matter beyond
traversal.

### 7. Thin walls have another side

While compressed into a surface, pushing inward lets you seep through
sufficiently thin material and emerge on its opposite face. The far side
starts glowing before the transfer, giving immediate feedback.

Teach it with a freestanding wafer you can already walk around, so
experimentation is safe and both faces are visible. Later, familiar walls
become shortcuts. Give eligible thickness or material a consistent visual
language so this doesn't become "press against every wall."

### 8. A bomb can launch you through the surface

An explosion against the solid conducts a shove along its boundary. While
burrowed, you catch that pulse and accelerate; emerge to turn it into a jump.

Teach it on a small block where the player's first bomb naturally catches up
with them. This gives the existing hidden bomb ability a second discovery:
first it breaks things, then it moves you. Circling the block to meet the
pulse from the right direction combines the two lessons.

### Small-game prototype selection

Prototype compression launch, winding blocks, and persistent light trails
first. They ask for different kinds of understanding—direction, routes, and
timing—and all reuse things the player already sees.

Then build a room where the player charges on a wall, launches onto a winding
block, and leaves its sensor glowing long enough to escape. No new button or
upgrade needed; the room becomes possible because their understanding changed.

## Hiding abilities inside the burrow (Myth-specific)

Concrete moves to hide inside verbs the player already has, Super Metroid
wall-jump style: always available, gated only on knowing.

### The free input space

The buttons are nearly spent while burrowed — jump is the burrow itself
(release pops), attack lays a bomb, dash is 1.3x speed. What's actually free:

1. **The perpendicular axis.** `BurrowTangentDeadzone` explicitly discards any
   input that isn't along the line. That's a whole analog axis the player is
   already pushing and getting nothing back from.
2. **Time.** `UpdateBurrowIntervals` already flashes the line and plays
   `player/burrow_tick` three times per burrow — an unexplained metronome.
3. **Shape.** Where you went, which way you turned, how many times.
   `burrowSolidLeft` is literally a handedness bit.

2 and 3 are the good ones: knowledge, not dexterity, and unfindable by mashing.

### Tier 1 — build these first

**Winding number.** Don't special-case a block. Accumulate signed turn during a
burrow (each corner in `WalkSurface` / line junction is a ±90°) into a
`burrowWinding` float. A closed loop around any solid island is ±2π and falls
out for free — every island in the game becomes windable, no authored block
needed. Sign matters: clockwise winds, counter unwinds, so an accidental
reversal costs progress, which is exactly what makes people theorize. Feedback
is already owned: offset the glow hue by winding via `BurrowGlowHueScale`, and
pitch `burrow_tick` up per revolution — a kid doing laps for fun hears the
chime climb, and that's the whole discovery. Payloads: wound torches stay lit,
a wound `FlipBlock` freezes solid, a wound enemy island detaches, a wound bomb
gets a shaped blast.

**Beat-popping.** Pop within ~0.1s of an interval tick (`burrowPulseTimer`
already counts down from exactly that) for a perfect pop: bigger
`BurrowPopForce`, and crucially skip `burrowNeedsGrounding` so you can re-burrow
mid-air. That one flag is the difference between burrow-as-a-move and
burrow-as-a-flight-system, gated purely on knowing the rhythm is real. The world
already has a clock in `PatternClock`/`FlipBlock`: build the teaching room with
blocks pulsing at `BurrowDuration / BurrowIntervals` so the room and the body
are in phase. Follow-on: tapping dash *on the beat* compounds
`DashBurrowSpeedMultiplier` (1.3 → 1.5 → 1.8), pumping like the rope-pump verb
already in `ClimbState`; off-beat taps drop back to 1x.

**The orthogonal axis, both directions.** Gate on `burrowSpeed ≈ 0`, which
happens naturally every time you stop at a dead end still holding a direction.
- *Away from the solid, held*: charges the pop. `Shift` pulls further out, the
  glow arc contracts, sparks get sucked in instead of sprayed out, release for
  a launch well past `BurrowPopForce`.
- *Into the solid, held*: sinks past `BurrowSubmergeDepth` and phases you to the
  opposite face of a thin wall — which is just
  `burrowToward = -burrowToward; burrowSolidLeft = !burrowSolidLeft`, the same
  flip `TryToggleBurrowRoomInverse` already performs on entry. Walls get two
  sides; routes get a hidden topology.

### Tier 2 — strong, cheaper

**Torch constellations.** `FlickerTorchesAlongBurrow` currently only looks nice.
Passing N `Light`s within a single unbroken burrow latches them lit and links
them. The purest brainia object available: the feedback shipped before the
mechanic, so every player already has the memory of "the torches flicker."

**Mirror reflection.** A queued reversal brakes to a stop
(`BurrowReverseFlipSpeed`). Hidden: reverse *exactly at a corner* and keep full
speed — a photon off a mirror rather than a U-turn. Costs one interval.

**Aimed pop.** The pop is `-exitDir * force + travel`. Hold tangent at the
moment of release to bend the exit vector toward it — angle of incidence, angle
of reflection. Experts leave surfaces at angles geometry says are impossible.

**Bomb on the clock.** `BurrowBomb`'s 2s fuse is arbitrary; tie it to the
interval clock. Then bombs laid in one burrow detonate together, a bomb you loop
around blasts along your path instead of radially, and a bomb parked on a
`BurrowSwitches` point charges it instead of exploding.

**Eject Bud.** `burrowPassenger` already rides the line. Pop while holding down
and Bud launches along the heading instead of you — a key thrown down a corridor
you can't fit through, or a body parked on a switch point to hold it open.

**Listening.** Duck and hold motionless ~2s (the predicate already exists as
`AuraActive`) and nearby burrow lines render faintly through walls. The ability
to see secrets is itself a secret, available since frame one, costing only
patience.

### Scaffolding that makes it a brainia and not moon logic

- **Bud is the etecoon.** Bud already has the full burrow code path. Have Bud
  perform each hidden move once, unprompted, in a safe room — no text, no
  prompt. Highest-leverage item on this list.
- **Near-misses must be audible.** Every hidden move needs a partial-credit
  signal that reads as ambience until you know: the winding chime, the interval
  flash, the charge glow. A silent 70% attempt teaches nothing.
- **One secret per region.** Two hidden moves behind one door destroys
  attribution; the player learns nothing from opening it.
- **Log the accident.** First time a hidden move fires, record it as a garbled
  glyph entry on the pause screen that resolves as you repeat it. Says "that was
  real, do it again" without saying what it was.

Caution: winding and beat-popping both need the burrow to outlast 1.13s to be
expressible. `BurrowUpgrade` is the dial — the real progression curve is
expressive room for the hidden verbs, not just reach.

## What-if prompts


Conceptual "what if" prompts for the knowledge-gated corner of the genre —
where you're gated by what *you* understand (Outer Wilds, Tunic, Animal Well,
Fez, The Witness), not by items you've collected.

## Batch 1 — the core takes

1. What if, a metroidbrainia, but the map is a language, and fluency is the only key.
2. What if, a metroidbrainia, but every locked door explains exactly how to open it — in a script you can't yet read.
3. What if, a metroidbrainia, but the ability you unlock is *noticing* a thing that was always on screen.
4. What if, a metroidbrainia, but knowledge decays, and you must physically re-learn a forgotten region.
5. What if, a metroidbrainia, but two players share one world and can only progress by teaching each other.
6. What if, a metroidbrainia, but the "upgrades" are misconceptions you must later unlearn to pass.
7. What if, a metroidbrainia, but the world is a single room whose meaning changes as you understand it.
8. What if, a metroidbrainia, but the boss is a puzzle that only unlocks once you can explain *why* it works.
9. What if, a metroidbrainia, but every NPC lies, and truth is the traversal ability.
10. What if, a metroidbrainia, but the map is drawn by the player, and wrong maps physically don't work.
11. What if, a metroidbrainia, but the knowledge you need is spoiled in the credits you can watch anytime.
12. What if, a metroidbrainia, but the secret is that the tutorial was the final exam.
13. What if, a metroidbrainia, but you gate progress by mastering a real skill (mental math, birdsong, knots).
14. What if, a metroidbrainia, but the world is a codebase and understanding the source unlocks the doors.
15. What if, a metroidbrainia, but each insight retroactively re-opens every earlier area with new meaning.
16. What if, a metroidbrainia, but the game never confirms you're right — only the world quietly responds.
17. What if, a metroidbrainia, but knowledge is contagious: what one save file learns, all files inherit.
18. What if, a metroidbrainia, but the currency is questions, and asking the wrong one costs you.
19. What if, a metroidbrainia, but you play the archaeologist decoding a *previous* player's discoveries.
20. What if, a metroidbrainia, but there are no abilities at all — only the growing suspicion of a rule.
21. What if, a metroidbrainia, but every region teaches a grammar rule and the finale is a sentence.
22. What if, a metroidbrainia, but understanding a mechanic *disables* it, forcing new understanding.
23. What if, a metroidbrainia, but the walls are made of your own past wrong guesses, made solid.
24. What if, a metroidbrainia, but it's cooperative-asymmetric: one player sees, one player knows.
25. What if, a metroidbrainia, but the real map is the community wiki, and the game watches it form.
26. What if, a metroidbrainia, but you can hand a stranger the one fact that finishes their run.
27. What if, a metroidbrainia, but the theme is grief, and each unlock is an acceptance.
28. What if, a metroidbrainia, but time only advances when you've genuinely comprehended something.
29. What if, a metroidbrainia, but the whole game is a magic trick and the ending reveals the method.
30. What if, a metroidbrainia, but the last locked door opens only when you admit you were wrong about the first.

## Batch 2 — stretch further

31. What if, a metroidbrainia, but the thing you're learning is the personality of a dead god, and the map is its corpse.
32. What if, a metroidbrainia, but you're learning the *developer's* private symbol system, and 100%-ing it means you now think like them.
33. What if, a metroidbrainia, but every player worldwide shares one persistent world, and it can only be solved *once*, ever.
34. What if, a metroidbrainia, but the knowledge is dangerous, and understanding the final rule deletes your save as an act of mercy.
35. What if, a metroidbrainia, but the gates are gated by *forgetting* — you must deliberately give up a truth to see past it.
36. What if, a metroidbrainia, but it's a game about a cult, and the deeper you understand, the more the game tries to recruit you for real.
37. What if, a metroidbrainia, but the world is your own long-term memory, and areas open as you recall your actual past.
38. What if, a metroidbrainia, but the map is a proof, and reaching the end means you've derived a real, checkable theorem.
39. What if, a metroidbrainia, but the entire game is a diegetic lie, and "beating" it is realizing none of the rooms were ever there.
40. What if, a metroidbrainia, but comprehension is measured by biometrics — the door opens only when your heart rate says you *believe* it.
41. What if, a metroidbrainia, but you inherit a stranger's half-finished save and must reverse-engineer what *they* understood to continue.
42. What if, a metroidbrainia, but the abilities are emotions, and you can't enter the flooded wing until you've actually felt dread.
43. What if, a metroidbrainia, but the world is generated fresh per player, so no guide can exist and the only wiki is *method*.
44. What if, a metroidbrainia, but every wrong theory you commit to becomes canon and permanently reshapes the world for you alone.
45. What if, a metroidbrainia, but the last ability is learning to read the save file, and the true final area is the disk itself.
46. What if, a metroidbrainia, but it's played across a lifetime — the game ships one impossible room, unsolvable until real science catches up.
47. What if, a metroidbrainia, but the mechanic is translation, and you slowly realize you're deciphering a message *sent to you specifically*.
48. What if, a metroidbrainia, but understanding is transitive: teach the mechanic to an NPC and *they* walk through the door for you.
49. What if, a metroidbrainia, but the map rearranges to stay exactly as hard as your current understanding — mastery is the only exit.
50. What if, a metroidbrainia, but there is no game — just a manual for one, and the "world" is the model you build in your head reading it.
51. What if, a metroidbrainia, but each region is a different player's *misunderstanding* of the same rule, and you must find the true one.
52. What if, a metroidbrainia, but the knowledge-gate is empathy: a door opens only once you can correctly predict what a character will do.
53. What if, a metroidbrainia, but you're an AI in the fiction, and "learning the rule" means the game watches you overfit, then punishes it.
54. What if, a metroidbrainia, but time in the world runs backward, and you must understand a catastrophe by watching it un-happen.
55. What if, a metroidbrainia, but the entire soundtrack is the puzzle, and you traverse space by singing back what a region taught you.
56. What if, a metroidbrainia, but every locked door is a bet, and you spend *certainty* to open it — wrong, and you lose the certainty forever.
57. What if, a metroidbrainia, but the win condition is convincing the game you don't need to play it anymore.
58. What if, a metroidbrainia, but each insight is irreversible and slightly ruins the magic, so 100% completion is a quiet tragedy.
59. What if, a metroidbrainia, but the world is a second person's dream, and you can only pass where your understanding matches theirs.
60. What if, a metroidbrainia, but the final revelation is that "understanding" was the item all along — a collectible you were farming, not feeling.
