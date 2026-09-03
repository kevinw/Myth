# Area Prototyping Lessons

Notes from reviewing the Aeolian Cloister prototype. This is a brief for the next time we rapidly prototype a non-linear area in Myth, not a prescription for every finished area.

## Creative target

An area should have a specific identity beyond being assembled from the existing Myth vocabulary. The desired emotional register is:

- sacred, techno, and mournful;
- dark overall, punctuated by legible pools of torchlight and other motivated lights;
- cohesive, but varied enough that each branch has its own spiritual and mechanical character;
- authored with a visible sense of humor: visual jokes, mechanical reversals, odd creatures, misleading architecture, absurd rewards, and quiet background details are all welcome.

We can assume the player has every current traversal ability. Prototypes do not need to gate or teach burrow, roll, wall jumps, ropes, or swimming unless teaching one is itself the room's point.

When existing machinery or art vocabulary cannot express the idea, it is fine to add an Actor, procedural visual, simple art asset, persistent state, or other supporting system. Version control makes experimentation cheap.

## What Aeolian Cloister got right

- Playing a genuinely new connected area was exciting in itself. Rapidly authored spaces are valuable even when they are only a first pass.
- The basic hub-and-loops structure was perceptible.
- The belfry was the strongest room. Climbing the rope toward a secret made the traversal, spatial composition, and reward reinforce one another.
- Mixing water, ropes, moving platforms, wind, burrow lines, and secrets produced useful variety.
- The area was a good small-scale proof of the intended open format: a baby version of the larger goal rather than a dead end.

These qualities should be preserved: mechanical variety, traversal through different media, secrets embedded in movement, and loops that return to a recognizable place.

## What held it back

### It borrowed a visual language instead of inventing one

The blue glyph lighting copied a use already established elsewhere. It fit Myth, but it did not give the area a new thesis. Recombining known motifs is not enough when the brief asks for surprise.

Next time, begin by inventing one new visual rule. This might be a procedural object, a novel use of an existing tile, a new background motif, or a small new asset. Repeat and transform that rule across the area so it becomes an identity rather than a novelty.

### The foreground geometry felt constructed from platforms

There were too many one-tile-high ledges and not enough convincing masses. The result read as a platforming diagram decorated afterward.

Organic spaces need:

- thick, irregular masses;
- asymmetry and changing wall thickness;
- slopes, overhangs, pockets, shelves, stalactites, and stalagmites built from mixed tile materials;
- negative spaces with recognizable silhouettes;
- platforms that feel eroded from, suspended within, or grown out of the environment;
- mysterious background structures that imply inaccessible depth and history.

A one-tile platform is still useful when its fragility, artificial construction, or mechanical role is intentional. It should not be the default grammar.

### Darkness was too uniform

The target is dark, not unreadable. Traversal surfaces and threats should normally be legible. The darkest values belong behind geometry, inside secrets, at the edge of explored space, and in intentionally atmospheric pauses.

Lighting should be composed as a path of attention:

- motivated pools of light identify important landings, junctions, and rewards;
- warm torches can punctuate colder ambient machinery;
- darkness separates layers and conceals optional space;
- occasional bright or strangely colored moments become meaningful because they are rare.

### The loops were too short and mechanically shallow

The branches returned to the hub, but they did not travel far enough—spatially, mechanically, or emotionally—to make the return feel earned. Roots was the clearest example: it had a burrow path, but not a developed sequence of discoveries or challenges.

A good default loop is three to five rooms. A single large room can also contain a complete loop if it has comparable duration and progression. A branch should usually have:

1. an invitation or readable mystery;
2. a clear mechanical premise;
3. two or more escalating transformations of that premise;
4. a climax, reversal, or surprising combination;
5. a reward, revelation, or quiet release;
6. a shortcut or changed route back.

Different branches can vary this structure, but each needs enough substance to feel like a journey away from the hub.

### Challenges were placed, not fully designed

Several rooms mixed useful Actors without developing an intentional platforming problem. Some jumps were simply too high. Screenshot inspection and JSON validation proved that rooms loaded and looked coherent, but did not prove they played well.

Each challenge room should be expressible in one sentence, such as: “A horizontal wind makes safe platforms dangerous on the outward trip and helpful on the return.” Geometry, enemies, lighting, and reward placement should all serve that sentence.

The default approach should be one mechanic explored through escalating variations. Some rooms can deliberately be chaotic mixtures, but that should be a contrast rather than the baseline.

### Combat was insufficiently coupled to traversal

Enemies should not be sprinkled into a completed platform layout. Their movement, attack shape, and defensive behavior should alter how the room's traversal mechanic is understood.

Examples:

- a shield enemy occupies the only stable landing in a wind shaft;
- a flying enemy pressures the pause needed to time a moving platform;
- a projectile crosses a rope's swing arc;
- an enemy becomes easier to approach by using the room's burrow route;
- destroying an enemy safely requires exploiting water, crumble tiles, or a one-way route.

## Opening the way back

The first shortcut vocabulary to explore is a permanent destructible wall seen from its inaccessible side early in a loop. Later, the player reaches its breakable side, destroys it, and recognizes the earlier space beyond it. Its destroyed state should persist in the save.

This creates several useful feelings at once:

- anticipation when the blocked connection is first seen;
- spatial understanding when the player approaches it from behind;
- a tactile climax when the barrier breaks;
- relief and mastery when the return route becomes short and safe;
- visible evidence in the hub or adjacent rooms that the world remembers the player.

Future shortcut vocabulary can add switches that permanently reshape rooms, deployed bridges, drained water, changed winds, or activated machinery. Start with the destructible wall because it is readable and needs the least new machinery.

## A stronger process for the next prototype

### 1. Write the area's thesis before making rooms

Define:

- one memorable image;
- one new visual rule;
- one recurring spatial shape;
- one primary traversal relationship;
- the emotional progression of each branch;
- how humor interrupts or complicates the dominant mood.

For example, “a drowned machine-cathedral whose stone formations imitate antennae” is more generative than “a blue cave with wind.”

### 2. Design topology and transformation together

Sketch the hub, branches, cross-connections, secrets, and return shortcuts before tile placement. Mark what the player sees early but reaches later. A loop should not merely reconnect on the world map; returning should reveal a changed understanding or a permanently changed route.

### 3. Give every room a mechanical sentence and a memorable image

Before building it, name:

- the room's primary challenge;
- its escalation relative to the previous room;
- its landmark or silhouette;
- how combat participates;
- its optional observation, joke, or secret;
- its exit condition and intended emotional beat.

If a room has no strong answer, merge it into a neighboring room or redesign it before authoring hundreds of tiles.

### 4. Establish the area's grammar in one vertical-slice room

Make one excellent room first, including final-ish massing, background mystery, lighting, traversal, combat, and one secret. Use it to discover the area's tile grammar and supporting Actors. Then vary that grammar across the remaining rooms.

This is not a request for user approval mid-task when autonomous work was requested. It is an internal quality gate: do not multiply a weak vocabulary across nine rooms.

### 5. Greybox with player physics, not visual intuition

- Calculate or measure jump height, horizontal reach, wall-jump needs, and safe landing width.
- Test starts and exits from both directions.
- Test entering with momentum, falling from an adjacent room, carrying Bud, and approaching while hurt or pursued.
- Treat a jump that only barely works as an intentional advanced challenge, never an accidental connector.
- Use PlatformNavGraph as supporting evidence, but personally exercise Player-only mechanics and ability combinations that Bud's navigation cannot validate.

### 6. Replace diagram geometry with environmental massing

After the critical path works, make a dedicated silhouette pass. Thicken platforms into formations, carve cavities, introduce slopes and irregular edges, and compose background forms independently of collision. Preserve traversal metrics while making the geometry feel found, grown, ruined, or engineered.

### 7. Integrate lighting, enemies, and secrets before calling the layout done

Lighting should guide the route without flattening the darkness. Enemy placement should modify the central traversal idea. Secrets should be spatially foreshadowed and satisfying to reach, as in the belfry rope climb.

### 8. Play loops as complete journeys

Do not validate rooms only in isolation. Play from the hub, through the full branch, through its reward and shortcut, and back into the hub. Ask:

- Did the branch develop an idea?
- Did it contain a surprising turn?
- Did the return feel earned?
- Did the hub feel different on return?
- Was there a memorable image, not just a sequence of obstacles?

## Prototype completion checklist

Before presenting a new area prototype:

- The area has a visual thesis not copied wholesale from an existing room.
- Foreground silhouettes contain substantial irregular masses, not predominantly thin platforms.
- Backgrounds contain at least one mysterious, inaccessible visual idea.
- Traversal remains readable within the intentionally dark lighting.
- Every required jump has been exercised with actual Player physics.
- Each branch has an introduction, escalation, climax or reversal, reward, and meaningful return.
- At least one shortcut visibly and permanently opens the way back.
- Combat uses the terrain's central mechanic.
- Water, ropes, moving geometry, and other media are used where they deepen the idea, not merely for variety.
- Secrets are foreshadowed and integrated into traversal.
- The dominant sacred/techno/mournful tone contains at least one authored joke or surprise.
- The full loops have been played in both directions where applicable.

The goal is not more content per prototype. It is fewer generic decisions, a stronger thesis, and enough mechanical development that open structure and artistic cohesion reinforce one another.
