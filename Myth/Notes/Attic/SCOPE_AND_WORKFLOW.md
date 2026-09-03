# Scope and workflow notes

## What I reviewed

I reviewed every commit reachable from every local ref authored by `Kevin Watters <kevinwatters@gmail.com>`, from May 9 through June 28, 2026. This includes the project while it was named TinyLink and after it became Myth.

- 374 authored commits: 359 non-merge and 15 merge commits
- 51 calendar days, with commits on 48 of them
- 117 commits in May and 257 in June
- Weekly totals rose from 49 to 87 commits across the full weeks represented
- Median non-merge commit: 4 files and 141 added/deleted lines
- 90th percentile non-merge commit: 24 files and 1,141 lines
- 268 commits touched game/editor source
- 160 touched playable content such as rooms, sprites, sounds, or particles
- 138 touched both source and content
- 62 touched platforms or CI; 53 touched tooling or build infrastructure
- Current tree: 21 Myth rooms and 44 FrogWorld rooms
- No release or milestone tags

The raw line totals are not useful: checked-in binaries, generated shaders, asset packs, and submodule changes dominate them. Commit subjects and file paths are much more informative.

## Diagnosis

The project does not have a pace problem. It has a concurrency and stopping-rule problem.

In seven weeks it gained a room editor, lighting, shadows, reflections, slopes, multiple worlds, audio, browser and iOS builds, CI deployment, mobile controls, many traversal abilities, enemies, a boss, particles, procedural growth systems, Bud navigation, a gym harness, and dozens of rooms. That is exceptional prototype velocity.

The same history shows five products advancing at once:

1. The actual platform game and its content.
2. A custom level editor and asset-authoring workflow.
3. A renderer/engine research project.
4. Desktop, browser, mobile-browser, and native iOS products.
5. A companion-navigation simulation and automated gym.

All five are interesting. Finishing a small game requires choosing one as the product and treating the others as support code with strict budgets.

## What is working well

### You build vertical slices

The 138 commits touching both source and content are a strength. Features usually appear in rooms quickly instead of remaining abstract systems. The initial JSON-room/editor work (`3e9d56d`), burrow iterations (`303611c`, `dde502d`), and RevealDoor consolidation (`25eb679`) all crossed code and content boundaries.

Keep this. A feature is not done when its class compiles; it is done when it improves a room in the release path.

### You established distribution early

Browser Pages deployment arrived June 6 (`73c895e`) and TestFlight automation June 7 (`54e44b8`). Mobile play produced a concrete TODO pass on June 28 (`ce1b3e6`). This is exactly the feedback loop small games need.

The finishing move is to stop improving the deployment machinery unless a release-blocking defect appears. It is already good enough to deliver playtests.

### The gym is an excellent model for “done”

The Bud work progressed through explicit scores: 9/9, 10/13, 12/13, 13/13, then 14/15. Commits such as `310d3d6` and `e79bb4b` state the failing case, the behavioral hypothesis, and the new result.

Apply this form to the whole game:

- `8/10 release rooms completable on touch`
- `10/10 deaths return to the expected checkpoint`
- `3/3 supported input methods can finish the golden path`
- `0 blockers in a 20-minute fresh-save run`

### You are willing to delete

The history removes Shapes, Peter, old scripts, duplicate systems, and finally HiddenWall in favor of RevealDoor. The HiddenWall removal deleted more than it added. That is a real finishing skill.

Use it earlier. Deleting one competing mechanic is often worth more than polishing both.

## Where scope is escaping

### Output is accelerating instead of converging

The weekly commit count rises toward the present, culminating in 87 commits in week 26. June 23 alone has 27 Kevin commits, largely a substantial Game/GameView refactor and performance sequence. June 25–28 immediately returns to new gameplay, editor, audio, mobile, particle, and HUD work.

That rhythm has no stabilization phase. A release needs periods where the number of possible changes falls each day.

Commit count is not the goal. Track open release blockers, incomplete rooms, and fresh-run failures. Those must trend down even when commits do not.

### Infrastructure keeps competing with the game

The history contains 62 platform/CI commits and 53 tooling/build commits. Many were necessary, especially to make browser and TestFlight builds real. Their marginal value is now lower than fixing respawn, simplifying wall jumping, establishing checkpoints, and finishing a beginning-to-ending run.

For the first release:

- Primary platform: browser.
- Secondary smoke test: desktop.
- Native iOS: keep building if it already builds, but defer platform-specific improvement.
- Editor: bug fixes only.
- Renderer: correctness fixes only.
- Build/CI: release blockers only.

### Exploration is not separated from commitment

The history contains healthy experiments—Saw, two zoom approaches, AOT, procedural tiles, generated sprites, multiple hidden-area systems—but they often coexist with the shipping path. Examples include “framebuffer is what we settled on, but leaving in a camera matrix zoom too,” “experiment with AOT (not working yet),” and “disable saw for now.”

An experiment needs one of three explicit outcomes:

- Adopt: it enters the release contract and receives polish.
- Replace: it deletes the old solution.
- Archive: it leaves the shipping path.

“Keep both for now” should expire after a few sessions.

### The TODO is an idea reservoir, not a release plan

The current TODO mixes critical defects, platform polish, visual research, mechanics, story ideas, external resources, and speculative systems. “Save checkpoints” sits beside water physics, art research, coins, 3D castle topology, and future Bud interactions.

Split it into:

- `SHIP.md`: ordered, testable release blockers only.
- `NEXT.md`: improvements allowed after the first release.
- `IDEAS.md`: unlimited parking lot with no implied commitment.

If an item cannot fail a release test, it does not belong in `SHIP.md`.

## A credible small-game scope

### Product sentence

Write one sentence and use it to reject work. A plausible version from the history is:

> A short atmospheric platformer where the player explores a compact castle, masters burrowing through surfaces, uncovers secrets, and reaches one final encounter—with Bud as companion only if he is essential to that experience.

The final clause requires a decision. Bud has received serious engineering and now has measurable reliability, but sunk cost is not the same as product necessity. If Bud is in the pitch, freeze his behavior set. If he is not in the pitch, remove him from the first release rather than continuing to improve navigation invisibly.

### Content cut

Use one world: `Myth`.

Select 8–12 of its existing 21 rooms:

- 1 opening/tutorial room
- 4–6 traversal rooms that teach and combine the retained mechanics
- 1 secret branch with a meaningful reward
- 1 recovery/checkpoint room
- 1 finale or boss room
- optionally 1 short ending room

FrogWorld is a quarry, not a second campaign. Copy only rooms that fill a named hole in this path. Do not promise to migrate all 44.

### Mechanic cut

Keep the smallest set that supports the product sentence:

- Run and jump
- One simplified wall interaction if required by selected rooms
- Burrow as the signature traversal mechanic
- One reliable attack
- RevealDoor/hidden areas

Treat dash, Saw, rewind, additional wall-jump rules, water physics, new currencies, new enemy families, and new Bud behaviors as post-release unless a selected room already depends on them and tests well.

The current TODO says wall jumping is too difficult. Simplifying or removing required wall-jump sequences is shipping work. Designing another wall-jump variant is not.

## Release contract

Call the first deliverable `0.1` and tag it. It is done when all of these are true:

1. A new player can start, reach the ending, and understand that they finished.
2. The golden path takes roughly 15–30 minutes.
3. Death and respawn always return to an intentional checkpoint.
4. Every required jump is consistently possible with keyboard and virtual D-pad.
5. Browser audio recovers from suspend/resume acceptably.
6. No room transition can strand the player or lose progress.
7. One fresh-save browser run completes with zero known blockers.
8. One person other than the developer completes it without coaching.
9. Desktop receives one smoke run.
10. The exact build is tagged and published.

Visual unevenness, imperfect architecture, optional-room bugs, and missing speculative mechanics do not block this release unless they violate the contract.

## Suggested workflow for the next ten focused sessions

### Session 1: lock scope

- Write the product sentence.
- Choose the release rooms and put them in order.
- Decide Bud yes/no for `0.1`.
- Move everything else out of the ship list.
- Tag the current state as `prototype-cut`.

### Sessions 2–3: make the golden path complete

- Add the minimum opening, progression, checkpoint, finale, and ending glue.
- Delete or bypass rooms and mechanics that prevent a complete run.
- Do not polish optional content yet.

### Sessions 4–6: reliability

- Fix checkpoint/respawn behavior.
- Simplify difficult wall interactions.
- Test every release room with keyboard and D-pad.
- Record pass/fail counts like the gym.

### Session 7: external playtest

- Publish the browser build.
- Watch one uncoached run.
- Record observed blockers separately from suggestions.
- Do not implement ideas during the test.

### Sessions 8–9: fix only observed release failures

- Work in severity order: progression loss, controls, crashes, confusion, then presentation.
- No new systems.

### Session 10: release candidate

- Fresh-save browser run.
- Desktop smoke run.
- Publish and tag `0.1.0`.
- Only then reopen `NEXT.md`.

## Daily operating rules

### Limit work in progress

At any time, allow:

- one release blocker being fixed;
- one room/content pass;
- zero or one explicitly time-boxed experiment.

Do not concurrently improve editor architecture, rendering architecture, platform infrastructure, and gameplay.

### Use a scope gate

Before starting work, ask:

1. Which release-contract item does this serve?
2. Which selected room becomes better or completable?
3. Can I test the result today?
4. Does this replace something, or merely add another option?
5. What will I stop doing to make room for it?

If the first three answers are weak, park it.

### Budget the finishing phase

Until `0.1.0`:

- 60% playtesting and bug fixing
- 30% room/content completion and communication polish
- 10% infrastructure, only when it blocks publishing or testing
- 0% unbounded engine research

### Keep commits small, but optimize for recoverability

The median commit is already reasonably small. Improve boundaries rather than chasing fewer lines:

- Separate generated assets/binaries from logic when practical.
- Keep refactors separate from behavior changes.
- Put the room demonstrating a behavior in the same vertical-slice commit.
- State the acceptance result in the subject/body, as the gym commits do.
- Tag playable milestones; commits alone do not mark a shippable state.

## General lessons

- A small game is defined more by what it refuses than by its code size.
- Tooling pays off only when it makes content or testing faster; after that point it is another product.
- Real-device playtests beat speculative platform work.
- Quantified acceptance tests turn tuning into convergence.
- A release candidate should become more boring every day.
- Preserve ideas aggressively in an icebox so you can cut implementation without feeling that the idea was lost.
- Shipping `0.1` does not end experimentation. It creates a stable place from which experiments can be judged.

The history demonstrates that you can build almost any individual piece quickly. The next skill to exercise is refusing pieces until one complete path is public.
