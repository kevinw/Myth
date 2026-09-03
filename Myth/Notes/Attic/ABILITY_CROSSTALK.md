# Ability Cross-Talk

Ideas for new obstacle types, enemy types, and gameplay chains that make the existing
moveset (burrow, swim, wall jump, roll, blink strike, attack) talk to the world and to
each other. Companion to `GAMEPLAY_CHAINS.md`, which tunes move→move handoffs; this doc
is about what the moves *meet* — things to burrow through, bounce off, flood, and break.

**Design rule carried over:** every interaction should preserve or convert momentum, and
reward arriving *with* a move rather than walking up neutral.

---

## Grounded (buildable on current systems)

### 1. Meat tunnels — burrowable enemies
A big, slow, armored enemy whose body registers as burrowable solid. Burrow in, travel its
bulk, pop out the far side: the pop kills it from within and launches you with the full
`BurrowPopForce`. The enemy is simultaneously a threat and a movement node.
*Grounds:* burrow's boundary walk (`Game.SurfaceCollision.cs`), `PopOutOfBurrow`.

### 2. Carve-through — burrow consumes Breakable tiles
Burrowing through `TileFlags.Breakable` ground destroys the tiles you pass, leaving a real
tunnel. Dig shortcuts, drop debris on enemies below, open drains. Rooms already rebuild
geometry live (`RoomGeoChanged` / `RefreshRoomTiles`), so the tunnel persists per session.
*Grounds:* `TileFlags.Breakable`, runtime tile destruction, burrow traversal.

### 3. Swim ⇄ burrow momentum handoff
A burrow route that ends at a pool wall pops you underwater with `burrowSpeed` carried as
swim velocity; symmetrically, swimming full-speed into a burrowable bank arms a burrow
seeded from your swim speed (mirroring how `TryStartBurrow` seeds from
`previousVelocity.X`). Water and earth become one continuous medium for a speed line.
*Grounds:* `burrowSpeed` seeding, `inWater` handling in `Player.cs`.

### 4. Wave-crest launches
Jumping out of water at a wave crest adds the surface's local velocity to your launch. The
`WaterSurface` sim already knows displacement per column; sample it on exit. Then: gaps
sized so only a crest-timed exit crosses them, and enemies/objects that slap the water to
*make* waves you surf out of.
*Grounds:* `WaterSurface` wave sim, the surface-exit transition (`wasInWater`).

### 5. Stone-skipping rolls
Hit water fast and shallow while rolling (downhill rolls reach `DownhillRollSpeed`) and you
skip off the surface like a stone — two or three dampened bounces with spray. Crossing wide
water without swimming becomes a reward for arriving in a downhill roll.
*Grounds:* `RollState`, `DownhillRollSpeed`, water-entry detection, splash particles.

### 6. Spit-pogo — bullets as platforms
Let blink strike target `WallSpitter` bullets (extend `FindBlinkStrikeTarget` past
`Masks.Enemy`). A spitter becomes a rhythm platform dispenser: vertical shafts you climb by
pogoing its own shots back up the way they came.
*Grounds:* `FindBlinkStrikeTarget`, `Bullet`, strike-bounce (`Player.Blink.cs`).

### 7. Saw-shared burrow walls
A burrow route along a wall that a `Saw` patrols (both follow the same surface —
`WallFollowerActor` walks the outside, burrow walks the inside). The blade bites one tile
deep, so a burrowed player under its path takes the hit: burrow in behind the saw, race or
wait out its lap, pop before it returns. A timing puzzle with no new movement code.
*Grounds:* `WallFollowerActor`, burrow boundary state, saw hazard timing.

### 8. Fungal inoculation
Burrowing through `TileFlags.Fungal` ground inoculates it: mushroom caps (temporary
jumpthrus) bloom along your exit line for a few seconds. You literally seed your own
platforms by choosing where to tunnel — strongest in rooms where the next ledge is
unreachable until you've burrowed beneath it.
*Grounds:* `TileFlags.Fungal`, `FungalGrowthActor`, `Jumpthru`.

### 9. Brittle ceilings for the pop-out jump
`BreakOnStand` in reverse: tiles that shatter when hit from *below* by a burrow pop (or a
rising strike). A pop-out jump through a brittle ceiling into a hidden region chains the
game's best moment (the pop) into its best reward (a secret).
*Grounds:* `TileFlags.BreakOnStand` plumbing, `BurrowPopForce`, hidden regions.

### 10. PotatoMan geysers
Lean into the arm-then-erupt shape it already has (`OnWasHit` → `ReadyingAttack`): hitting
one starts a windup, then it erupts violently upward. Stand on it at eruption and ride the
launch. Arm it with an attack, roll away, circle back, ride — the enemy is a fuse you light.
*Grounds:* `PotatoMan`'s state machine, existing launch/juice plumbing.

### 11. Current jets
Underwater flow volumes that shove swimmers along. Fight them with strokes, ride them for
free speed, or burrow through the floor beneath them to bypass entirely — burrow as the
"under the weather" option. Jets aimed up out of the water double as fountains that fling
you into air chains.
*Grounds:* swim stroke model (`WaterSwimStroke`, capped sink), burrow, room water rects.

### 12. Crusher shafts with burrow-dodge pockets
`MovingPlatform`s traversing wall-jump chimneys as periodic crushers. Small burrowable
pockets in the shaft wall are your dodge slots: slide, read the platform, duck into the
wall as it passes, pop and continue. Burrow becomes an i-frame with a location.
*Grounds:* `MovingPlatform`, wall slide/jump, burrow arm/pop.

### 13. Lurkers in hidden cover
An enemy that lives inside unrevealed `HiddenCoverGrid` regions — only a faint glow leaks
through the cover tiles. Blink-striking into the cover reveals the region (the reveal
plumbing already cascades to camera blockers) and connects the hit. Secrets that fight back.
*Grounds:* `HiddenCoverGrid` reveal flow, `Light`/emissive leak, blink strike.

### 14. Shell-flip beetles
An armored enemy immune to attacks head-on, flipped helpless when you *roll through* it.
Roll becomes a combat opener, and the roll→attack cancel finally has a designed target:
roll in, flip, cancel to attack before it rights itself.
*Grounds:* roll state collision, `OnWasHit` patterns, attack cancel.

### 15. Diving fish arcs
A fish enemy that porpoises between water and air on readable arcs. Its apex is a pogo
platform: bounce off it to cross water you'd otherwise swim. Kill it mid-water with a swim
attack, or use it as transport and leave it alive — the room asks which you value.
*Grounds:* blink-strike pogo, water surface transitions, simple arc AI.

### 16. Root switches (burrow-only)
Switch nodes embedded *inside* solid ground, on the burrow boundary path. Brushing one
while burrowed toggles doors elsewhere (`Door`/`LinkedDoor` infra). Rooms whose surface is
trivial but whose underground is the actual puzzle: plan a tunnel tour that hits three
roots in one arming.
*Grounds:* burrow path traversal, door/actor trigger plumbing.

### 17. Momentum gates
`RevealDoor` variants that only yield to speed: burrow arrival near `BurrowMaxSpeed`, or a
downhill roll above a threshold. A visual "cracked" telegraph reads as *hit me harder*.
These teach and test chain-keeping — the whole doc's design rule as an obstacle.
*Grounds:* `burrowSpeed`, `DownhillRollSpeed`, reveal-door flow.

### 18. Ride-able vent bubbles
Giant `AirBubble`s rising from floor vents. Swim into one to ride it up fast water columns;
it pops on solids (the small ones already do), ejecting you with its rise speed — time the
surface arrival and stroke out for extra height. Elevator, air refill fiction, and launch
pad in one sprite.
*Grounds:* `AirBubble` behavior scaled up, swim state, water surface exit.

### 19. Corrosive spit opens walls
`WallSpitter` shots destroy `Breakable` tiles they hit. Now the player can *aim the enemy*:
stand so the spit line crosses the brittle wall, dodge, and let the enemy excavate your
shortcut. Enemies as level-editing tools.
*Grounds:* `Bullet` collision, `TileFlags.Breakable`, `RoomGeoChanged`.

### 20. Climbable waterfalls
Vertical water columns hugging walls. Inside one: wall-slide is slowed to a crawl, and swim
strokes work *upward* — you climb the fall with rhythm, then wall-jump out at the top lip.
Ties swim tech into vertical wall rooms, and reads instantly.
*Grounds:* water volume checks, wall-slide gates (`IsWallHugging`), swim stroke.

---

## Pie in the sky

### A. Mycelial memory
Every tunnel you have ever burrowed persists as a faint living vein — across rooms, across
the playthrough. Late game, the network wakes up: enter any vein and flow along your own
history at high speed. Fast travel you *drew* by playing, and a map of who you were.

### B. Water is conserved
Burrow through a lake bed and the lake *drains* through your tunnel into the room below —
permanently. Flooded rooms become swimmable, drained ones walkable. The world's water is a
finite, movable resource, and traversal decisions become irreversible terraforming.

### C. Sonar dark
Zones of true dark where light doesn't work — but sound does. Swim strokes, pops, and
attacks emit expanding pressure rings that illuminate momentarily and *alert everything*.
The blind ecosystem hunts noise; stillness is stealth; movement is vision.

### D. Pilot the beast
Burrow into a colossal enemy and stay: steer it from inside — its moveset becomes yours
(smash walls the player can't, wade lava, terrify lesser enemies) until it ejects you.
A movement powerup that walks around and fights back until you catch it.

### E. Tides
A slow global water clock. Rooms alternate between swim layouts and walk layouts over
real minutes; some doors only exist at low tide, some currents only at high. Route
planning becomes scheduling, and revisits feel like different rooms.

### F. The map room
The pause map becomes a *place*: a chamber where the minimap is the floor, your rooms
laid out as walkable tiles. Endgame ability: physically push a room tile to a new
adjacency, and the world re-stitches to match. Editing the world map is the final
traversal power.

### G. Molt
Leave a shed skin anywhere: a checkpoint-decoy hybrid. Snap back to it once (rewind
tech exists in the bones — `PlayerRewinder`), or let enemies attack the shell while the
real you flanks. Placing your own respawn is a puzzle input, not just safety.

### H. Fuse with Bud
Bud merges into you: heavier, stronger moveset variant (its nav smarts drive an
auto-grapple or carry). Split at will to be in two places — one body stands the switch,
the other burrows the gate — then alternate control. Companionship as a mechanic, not
an escort.

### I. Become water
Briefly liquefy: pour through cracks and jumpthrus, join a pool and *be* part of its
surface sim, ride a waterfall down, reform on landing. Everywhere water can go becomes
traversable space, and the wave sim becomes your body.

### J. Between the rooms
The dead rock between rooms on the world map becomes a free-dig zone: burrow off the edge
of a room into unmapped interstitial earth, with stamina as your leash. Uncharted caverns,
buried rooms that appear on no map, and the feeling that the world grid was always just
the part someone else had explored.

---

## The cross-talk matrix

Every ability, enemy, and obstacle against every other. Each intersection is a *new* idea
(none repeat the lists above). Read row × column; the matrix is symmetric, so only the
upper triangle is filled (`·` mirrors, `—` is self).

| | Burrow | Swim | Wall jump | Roll | Blink strike | Spitter | PotatoMan | Saw | Water | Breakable | Platform | Hidden cover |
|---|---|---|---|---|---|---|---|---|---|---|---|---|
| **Burrow** | — | tunnel under a pool drags an air seam: breathe along your own path | pop between close walls auto-ricochets — shaft pinball | pop lands into an instant roll: keep the line low under ceilings | down-strike the ground to drill straight into a burrow from the air | a burrowed player baits its aim downward — shots crater the dirt above you | pop beneath one launches it as a projectile at its friends | a saw drops into your open tunnel and adopts it as a patrol | surface bulges telegraph anything burrowed beneath — including you | breakables collapse behind you, sealing the tunnel against pursuit | a platform crossing your pop point compresses it: delayed, double-height pop | burrow into cover unseen; popping inside reveals the secret from within |
| **Swim** | · | — | one surface-tension kick per dive: wall-jump off the water itself | underwater roll is a torpedo; its bubble wake lifts whoever follows | targets refract at the surface — through-water strikes are bent trick shots | underwater it fires slow bubble-torpedoes your stroke wake can deflect | waterlogged, it bloats and floats: a pushable buoy platform | hitting water it throws a spray wall — swim behind the splash for cover | strokes leave a lingering current: lap a pool to build your own whirlpool | soaked breakables crumble at a touch — flood the room to soften it | underwater platforms push water: draft beneath them for a free current | hidden pools leak — drips through cover tiles betray the secret |
| **Wall jump** | · | · | — | roll low into a wall to kick up into a brief wall-run before the slide | striking mid-slide launches a flat horizontal lance — walls-only tech | its shots stick in walls as spikes: the shaft's jump route mutates as it fires | a launched PotatoMan wedges into a chimney as a temporary jumpable plug | brushing the saw's wake (not blade) on a shaft wall sparks a boosted jump | spray-wet walls are slick: faster slides, lower jumps; pick the dry route | brittle wall tiles survive two kicks and crumble on the third — count your jumps | kick off a mover's side to inherit its velocity — slingshot | cover tiles sound hollow when kicked: an audio tell the wall is a lie |
| **Roll** | · | · | · | — | striking during a roll aims backwards: a parting shot at what you passed | a rolling player reflects spit back along the travel line | rolling into a readying one triggers the eruption early — set and detonate in one motion | a frame-perfect roll under the blade slows time for a beat | shallow puddles hydroplane a roll: no friction, no steering, longer line | rolls smash shin-height breakables through; taller ones stop you — pick your holes | a roll on a mover adds its belt speed: conveyor launch | the roll's low profile fits cover openings standing height can't |
| **Blink strike** | · | · | · | · | — | strike it mid-shot and the loaded round detonates inside — overcharged bounce | pogo a readying one to cap the eruption: it spends its charge launching you | a dead-center strike stalls the blade a second — a parry for the patrol clock | strikes through the surface exit on a geyser: water assists the bounce | a breakable gives one free pogo, then shatters on the second | striking a platform's underside bumps it off-schedule: nudge the cycle | covered enemies ping as silhouettes; landing the strike drags you into the secret |
| **Spitter** | · | · | · | · | · | — | stray spit arms potato men: a spitter turns the field into a timed minefield | shots hitting the blade ricochet along its patrol wall — the saw routes bullets | spit stone-skips across pools: low shots gain hoppy extra range | spit plasters over broken tiles, repairing the level — kill order matters | riding a mover, its firing line sweeps the room on the platform's schedule | one nested in cover fires through it: bullets from nowhere are the tell |
| **PotatoMan** | · | · | · | · | · | · | — | the saw's lap shaves one awake; its eruption knocks the saw off the wall | underwater eruption is a depth charge: a shove-sphere that is hazard and lift at once | erupts through brittle ceilings — a demolition charge you arm and flee | the platform's turnaround bump arms it on a schedule: the room sets the fuse | erupting inside cover blows the region open from within — loud secrets |
| **Saw** | · | · | · | · | · | · | · | — | a dunked saw runs rusty and slow for one lap: a window you can flood open | it grinds through breakables, permanently rerouting its own patrol | it hops passing platforms and commutes to new walls: migrating patrols | its lap dips behind cover — the vanish rhythm traces the hidden outline |
| **Water** | · | · | · | · | · | · | · | · | — | pressure bursts soaked breakables outward when the room below drains | a mover diving into a pool sloshes the level rhythmically: surf the surge | behind-waterfall secrets show as a gap in the spray — look for dry mist |
| **Breakable** | · | · | · | · | · | · | · | · | · | — | movers carry breakable cargo: break it in transit to change what arrives | sometimes the cover *is* breakable — smash the disguise; loud but fast |
| **Platform** | · | · | · | · | · | · | · | · | · | · | — | a mover that rides into hidden cover mid-route: stay aboard to enter the wall |
| **Hidden cover** | · | · | · | · | · | · | · | · | · | · | · | — |
