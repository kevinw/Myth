# .mvmap.json

One file per game. Three tables; nothing else until a metric demands it.

```json
{
  "game": "Super Metroid",
  "source": "where the data came from, so future-you can audit it",
  "start": "landing-site",
  "rooms": [
    { "id": "landing-site", "rect": [0, 0, 4, 3], "region": "Crateria", "tags": ["save"] }
  ],
  "edges": [
    { "a": "landing-site", "b": "parlor", "kind": "open" },
    { "a": "parlor", "b": "climb", "kind": "gate:morph" },
    { "a": "climb", "b": "pit-room", "kind": "one-way" }
  ],
  "items": [
    { "ability": "morph", "room": "morph-ball-room" }
  ]
}
```

`rect` is `[x, y, w, h]` in any consistent unit (screens, tiles, metres) -- metrics that
care are scale-free. `region` groups rooms for interdigitation. `tags` are free-form;
`save`, `boss`, `upgrade`, `hub`, `transit`, `arena`, `vista`, `secret` are the ones
metrics look for.

`kind` is one of:

| kind | meaning |
| --- | --- |
| `open` | walk through, both ways |
| `door` | same, but a discrete threshold (counted separately for texture) |
| `one-way` | `a` to `b` only -- a drop, a shutter, a commitment |
| `gate:<ability>` | needs that ability, both ways once held |
| `hidden` | passable but must be discovered (bombable wall, false floor) |
| `trick` | passable only with tech the game never teaches |

`items` is what makes the state-space metrics work: an ability's `room` is where you
*get* it. Abilities not listed in any `items` entry are treated as held from the start.

## Qualitative layer (optional, same file)

```json
  "notes": [
    { "issued": "vista-room", "redeemed": "city-gate", "ability": "hook",
      "minutes": 34, "kind": "vista", "comment": "you see the city long before you can enter" }
  ]
```

A `note` is a promissory note: the game showed you something unreachable (`issued`),
and you came back (`redeemed`). `minutes` is wall-clock latency from your playthrough.
The distribution of these latencies is the headline metric of the whole study.
