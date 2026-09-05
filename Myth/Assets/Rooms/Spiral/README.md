# The Coil

A 30-chamber world prototype: 23 surface room files (the two-level `start` contains A and D) and a six-room optional root sublayer.

Run it directly with:

```sh
dotnet run --project Myth/Myth.csproj -- --world Spiral --play-room start
```

## Topology

The main route begins in lower `A`, curls through B and C into upper `D`, then descends through the switch-opened hatch inside the merged `start` room. The same durable switch opens A's west gate. Later rooms curl around that loop and add four more gated side connections.

```text
             O - N - M - L
         Q - P   D - C - K
         R - E - A - B - J
         S   F - G - H - I
         T - U - V - W - X
```

Main route: lower `A`, `B`, `C`, upper `D`, descend through `start` to lower `A`, then `E F G H I J K L M N O P Q R S T U V W X`.

Earned shortcuts: `A-E` from the latch in D, `J-B`, `K-C`, `P-D`, and `R-E`. L contains the root-sublayer entrance; its far terminal can return to L or exit at T.

## Room ideas

| Mark | Room | Idea |
| --- | --- | --- |
| A | start (lower) | Quiet arrival, wildlife, save point, and the visibly locked return gate |
| B | counterweight | Carryable weight driving a one-way vertical platform under a Dropper |
| C | wind harp | Wind-driven ropes over water with a moving landing |
| D | start (upper) | Compact ambush; a durable switch pulse opens the lit floor hatch into A |
| E | afterimage | Calm hidden chest alcove, camera reveal, and overgrown wall |
| F | rope ferry | Moving rope ferry over a contained basin, ending in a directional launch pad |
| G | live wire | Burrow zigzag through staggered lightning and aimed fire |
| H | saw orchard | Ceiling-crawling saw layer among radial bouncers and brambles |
| I | crosswind | Climb through a bounded crosswind that moves player, enemies, bullets, and rope together |
| J | orbit clock | Rotating laser enemy inside circular moving-platform cover |
| K | magnetic exchange | Metal-only crawler route crossed by opposing conveyors |
| L | archive descent | Calm save/story room and optional descent into the root memory |
| M | falling weather | Moving roofs alter the timing and landing places of Droppers |
| N | reflector works | Staggered lightning, reflective metal slopes, and lateral fire |
| O | cargo cannon | Bounce a carryable into a remote Switch to uncover a chest |
| P | skin route | Burrow perimeter shared with a wall-and-ceiling Spider |
| Q | inversion well | Burrowing swaps between mirrored platforms and enemies |
| R | still pool | Wildlife pond, save point, story beat, and another reconnecting gate |
| S | bent ballistics | Updraft changes enemy projectile trajectories around a central rope |
| T | moving spring | Directional bouncer attached to a circular platform under a Dropper |
| U | root rail | Fast-travel BurrowLine threaded through alternating lightning |
| V | lockdown | Door closes behind a conveyor arena of complementary enemies |
| W | listening gallery | Calm archive wall, coiled rope, and hidden chest alcove |
| X | conductor | Eye-laser duel, angled bouncer, fast escape rail, and upgrade reward |
| Root A | threshold | Quiet neural growth, wildlife, return door, and story |
| Root B | drowned cables | Wind-pumped ropes crossing a living pool |
| Root C | mirror seed | Inversion swaps mirrored bouncers, enemies, and route geometry |
| Root D | thrown signal | Carryable plus directional bouncer activates a remote switch |
| Root E | express vein | Three-times-speed BurrowLine with lightning and a Dropper |
| Root F | memory terminal | Calm archive, save, rare upgrade, story conclusion, and exit to T |
