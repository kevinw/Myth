# Encrypted final-puzzle blob (design notes, 2026-07)

The idea: a hidden "final puzzle" in the metroidbrainia tradition (FEZ monolith,
Tunic golden path, Animal Well layer-3). The player's recent actions are hashed into
a decryption key and tested against an encrypted blob shipped in the build. Wrong
sequence ⇒ nothing happens, ever, and datamining cannot reveal the content. Right
sequence ⇒ the blob decrypts to extra content (rooms, cutscenes, story).

The action vocabulary itself is still under development — these notes cover the
crypto envelope and the blob format / execution model, which are independent of it.

## Threat model

The moment we ship, an attacker has: the blob, the checking code, the KDF
parameters, and the salt (NativeAOT binaries disassemble fine in Ghidra). Kerckhoffs
applies — **the only secret is the action sequence**. Security = sequence entropy ×
cost per guess.

Two distinct threats, only one of which crypto can address:

- **Offline brute force.** FEZ's monolith fell this way — the input space was small
  enough (~6 symbols, short sequence) that fans brute-forced it in days, before
  anyone found the intended clue. Fixable with keyspace + a memory-hard KDF.
- **Clue-space search.** A fair puzzle is derivable from in-game clues, so the
  community searches clue-space, not keyspace. Tunic's golden path fell in ~a week
  *by design*. Crypto cannot slow this down; only puzzle design tunes it. The
  crypto's job is narrower: guarantee the intended solve path is the *only* path.

## Crypto recipe

- **KDF:** Argon2id over the canonicalized action sequence (memory-hard, ~50–100 ms,
  as much memory as the weakest shipping platform tolerates — see caveats). This
  cuts GPU-rig throughput by ~10⁶ vs a bare hash, so even 2⁴⁸ raw keyspace becomes
  decades of compute and 2⁶⁴ is unreachable.
- **Cipher:** AEAD (ChaCha20-Poly1305 or AES-GCM) so a wrong key is a clean,
  authenticated failure — no oracle, no partial decryption.
- **Keyspace target:** ≥ 2⁶⁴ raw. `keyspace = V^N`:

  | Vocabulary V | N for ~2⁶⁴ | N for ~2⁸⁰ |
  |---|---|---|
  | 8 | 22 | 27 |
  | 16 | 16 | 20 |
  | 64 (actions-in-context) | 11 | 14 |

  Discrete *world actions* (rang bell #3, faced statue west) beat raw button inputs:
  more entropy per symbol and far easier to canonicalize across input devices.

- **Gating the check:** a ~100 ms KDF can't run per-input on a sliding window. Gate
  the expensive check behind a cheap condition **independent of the key material** —
  standing on a tile, holding an item, being in a room. Puzzle-flavored anyway.
  **Never** a cheap partial-key precheck (CRC of a prefix, etc.) — that's an oracle
  that collapses V^N into V×N divide-and-conquer.
- **Canonicalization (when vocab lands):** exact alphabet, debounced,
  timing-insensitive; keyboard / pad / replay must serialize identically.

## Why the blob is data, not code

Desktop ships NativeAOT (`Myth.csproj` `PublishAot=true`): **no JIT, no IL
interpreter** — `Assembly.Load(byte[])` throws `PlatformNotSupportedException`.
There is no "interpret just this DLL" mode; that's a Mono capability (the browser
build's interp fallback), with no desktop NativeAOT equivalent. Options considered:

- **Encrypted native dylib/so, decrypt + `NativeLibrary.Load`.** Works, rejected:
  per-platform payloads, macOS hardened-runtime/notarization refuses unsigned
  libraries, and "decrypt a blob then execute it" is textbook malware shape to
  Windows Defender heuristics. Buys nothing over data + interpreter except pain.
- **Secret code compiled into the binary, only assets encrypted.** Weakest: dormant
  code paths are findable in disassembly. Acceptable only if the wow is purely
  content; even then the interesting part should live in data.
- **Data interpreted by machinery already in the shipped binary.** Chosen. Also the
  best *secrecy*: dataminers see that an interpreter exists but learn nothing about
  what the scripts do.

## Blob format

A single AEAD-sealed container that decrypts to a **content pack** — the same
formats the engine already loads, mounted as a second content root:

```
[magic][format version][argon2 params + salt][nonce][ciphertext][auth tag]
          └── plaintext header: attacker knows it anyway; keeps checker honest
ciphertext (after decrypt) = compressed archive:
  manifest.json          — entry list + integrity hashes
  rooms/…                — level data, normal format
  storylets/…            — storylet JSON + StoryExpression logic
  images/…               — PNG or QOI only (Foster's decoder reads nothing else)
  audio/…
```

- **Compress-then-encrypt** (ciphertext doesn't compress). One deflate/zstd pass
  over the whole archive before sealing.
- **Size leaks.** The blob's byte count is public and invites speculation — that's
  fine (a visible locked door is the point), but pad to a round size so content
  iteration between patches doesn't leak "the secret grew."
- **Mount, don't special-case.** The loader should expose decrypted entries through
  the same path the normal content pipeline uses, so secret rooms/storylets need
  zero bespoke code after unlock — and so hot-reload/editor tooling can work on the
  pre-encryption source folder during development.
- **Everything else stays inside.** No secret strings, achievement names, or asset
  references outside the blob. Steam achievement lists are datamined on day one; if
  the unlock has an achievement, name it something inert.

## Execution model: grow Storylets, not a new VM

`Source/Storylets/` already ships an interpreter: `StoryExpression` is a real
tokenizer/parser/evaluator over a context dictionary, storylets are JSON-driven,
and there's hot reload. The blob's "code" should be storylet content plus, if the
secret needs scripted *sequences* (cutscenes), a small **command list** — an array
of ops (`move actor`, `play sound`, `wait`, `set flag`, `camera to`) executed by a
runner compiled into the main binary. This is the classic cutscene architecture and
it's AOT-proof by construction.

Escalate to a general bytecode VM only if the secret content's *mechanics* demand
it — and note the hard limit either way: **new actor types, shaders, or physics
cannot come from the blob.** They must ship compiled (inert) in the binary. Design
the secret around existing systems recombined by data, keeping the interesting part
in the data.

## NativeAOT / platform caveats

- **No reflection-based deserialization of blob types.** Everything decoded from
  the blob must go through the existing source-gen `JsonSerializerContext` (already
  the codebase pattern) or a hand-rolled binary format. A trimmed-type crash here
  would be found by exactly one player: the first solver. Saddest bug report ever.
- **CI round-trip test:** seal a test blob, decrypt with the known key, load every
  entry through the real content path — in the *published AOT build*, not just
  `dotnet run`. Plus a dev-mode key override for playtesting the content itself.
- **KDF params are bounded by the weakest platform.** iOS and browser-wasm must run
  the same Argon2id parameters; a 256 MB memory cost may be fine on desktop and
  brutal on a phone/browser tab. Pick params that take ~100 ms on the weakest
  target; the table above shows N absorbs the difference cheaply.
- **AEAD availability off-desktop needs verification.** `AesGcm` /
  `ChaCha20Poly1305` are not supported on browser-wasm (and Argon2id isn't in the
  BCL anywhere). Likely answer: a managed implementation (e.g. BouncyCastle, or
  Konscious for Argon2) used on all platforms — slow is irrelevant for a
  once-per-gate check, and one code path beats three.

## Prior art

- **Animal Well (2024) — the direct precedent, shipped.** Basso encrypted optional
  playable sections with AES; the key is not in the code and is "input through
  playing the game" ([Game File interview](https://www.gamefile.news/p/indie-developer-has-a-plan-to-keep)).
  It worked: the deep layers were solved through communal play, not leaks.
- **Noita eye messages — over-hardened cautionary tale.** Shipped ciphertext (2019),
  devs confirm a real message, no in-game trigger per full decompilation —
  [still unsolved in 2026](https://noita.wiki.gg/wiki/Eye_Messages). The opposite
  failure from FEZ: tuned so strong it may outlive the community's patience.
- **FEZ monolith — under-hardened cautionary tale.** Tiny keyspace, plaintext-adjacent
  check; brute-forced in days, before the intended clue was found.
- **Server-key cousins:** Fortnite / WoW / Destiny 2 ship encrypted assets with keys
  released server-side at content launch; Overwatch's Sombra ARG dripped key
  material through puzzle stages. Validates blob-vs-dataminers at industrial scale;
  our twist is moving key authority from the server to the player's hands.
- **Plaintext knowledge-gates (the vulnerable cousins):** Tunic golden path, Outer
  Wilds ending, Void Stranger brands, La-Mulana rituals — same design goal, content
  in plaintext, all datamined ahead of legitimate solves. See Wagar,
  [Designing Secrets that Stay Hidden](https://critpoints.net/2017/03/23/designing-secrets-that-stay-hidden/).

## WILDCARD_IDEAS

Unvetted offshoots. Each is compatible with the envelope above unless noted; the
oracle rule (no partial-key checks) still applies to all of them.

### Where the key comes from

- **The pilgrimage key.** Don't hash the last N inputs — hash the *entire
  canonical run trace*. We already have deterministic-sim infrastructure (the gym,
  `PlayerRewinder`); a canonicalized replay digest means the key is a whole
  playthrough performed a particular way (no damage, a specific route, a vow kept).
  The ultimate deed-gate: even with the solution public, every player must
  personally perform the pilgrimage. Feasibility hinges on how deterministic the
  trace canonicalization can be made across platforms.
- **World-state-as-combination-lock.** Key material from a snapshot of arranged
  world state rather than an input sequence: every crate position, every shrine
  lit, every enemy spared. The world itself is the dial; the player *sculpts* the
  key. Canonicalization is the whole problem (quantize positions, sort entity
  lists), but `SaveState` already serializes most of this.
- **The melody key.** Key entered by performing music on an in-game instrument —
  vocabulary becomes notes, and the clue trail becomes ear training. Pairs
  naturally with hiding fragments of the melody in area themes
  (`Manager.MusicRegions` already segments the score). Animal Well hid a bunny in
  an OST spectrogram; this is that, but performable.
- **Cross-build ritual halves.** Desktop, browser, and iOS builds each embed a
  different key *shard*; the full key requires performing ritual halves on two
  platforms and carrying a share-code between them. The demo build knowing half
  the secret of the full game is a marketing beat disguised as cryptography.
- **The conlang KDF.** When the vocabulary lands, make it a diegetic glyph script
  taught by the game (Tunic-style). Learning to *read* is the clue trail, and the
  effective keyspace is exactly "can you compose a sentence in the dead language."

### Splitting the key across people

- **Shamir threshold secret.** Split the key k-of-n across distinct in-game
  achievements/routes so no single playthrough can produce it — players must
  compare notes to combine shares. Community cooperation isn't a hope, it's a
  mathematical requirement. (Shamir's scheme is ~50 lines and AOT-trivial.)
- **Screenshots as couriers.** Photo-mode screenshots steganographically carry a
  per-save key shard. Players trading screenshots on Discord are unknowingly
  trading key material; the community solves it by doing what communities already
  do. Caveat: platforms recompress images — shard must survive that, or live in
  metadata/share-codes instead.
- **First-solver sovereignty.** The blob contains two sealed variants; whichever
  key the first solver performs becomes canon (we honor it in a patch, or the two
  keys simply unlock different content and the community discovers both truths
  exist). Dataminers can't even enumerate how many endings there are.

### What's inside the blob

- **Matryoshka blobs.** The decrypted content contains the *next* ciphertext plus
  its clues. Community pacing becomes architectural: each layer is a fresh
  campaign, and we can tune later layers' difficulty after watching layer 1 fall.
  This is Animal Well's layer structure made cryptographically literal.
- **Decoy blobs.** Ship several blobs; some decrypt under "obvious" candidate
  solutions to taunts, red herrings, or fake endings. The community can never be
  sure which blob is terminal — including after solving one. Cheap: decoys can be
  small and the padding rule already hides relative sizes.
- **The remix seed.** The blob decrypts not to rooms but to a *modifier ruleset* —
  a data-driven remix (NG+ layer) of existing rooms via the storylet/room systems.
  Maximum wow per authored byte; the secret is a whole second game that costs
  almost no new content.
- **The poison key.** A second valid sequence *seals* the door: re-encrypts the
  blob under a key derived from entropy that's then discarded, permanently, for
  that save. Mythically flavored player choice ("some doors should stay shut"),
  trivially cheap, and the community discovering that a destroy-ritual exists is
  its own event. Purely local — never do anything global/irreversible for real.

### Time as a component

- **Scheduled entropy decay.** Publish "hint tokens" over the years (patch notes,
  ARG drops) that XOR into the key derivation, each halving the remaining search
  space. A difficulty dial that turns *after* shipping — the anti-Noita insurance
  policy. Requires designing the KDF input layout for it up front.
- **The commitment stone.** At launch, publish a hash commitment of the solution
  (in-game, on a literal stone). Proves forever that the puzzle was real and fixed
  from day one — no "they invented it later" accusations — while revealing zero
  bits. Free, and it *is* content: the stone is the community's North Star.
- **Dead-developer's switch.** The master key sits in escrow (a lawyer, a sealed
  physical artifact, a time-locked service) with the public promise: if the studio
  ever dies, the key publishes. The game outlives us politely. Companion piece:
  Rohrer buried *A Game for Someone* in the desert; Trials Evolution's ARG ends
  with letters that can't be opened until 2113.

## Open questions

- The action vocabulary and its canonicalization (under development — punted).
- The gate condition: what cheap, key-independent state arms the check?
- Solve-time tuning: what does the clue trail want — weeks? months? This is pure
  puzzle design; nothing in this file helps with it.
- Whether the browser build ships the blob at all, or the secret is desktop-only
  (blob is pure data, so parity is free *if* the crypto caveat above resolves).
