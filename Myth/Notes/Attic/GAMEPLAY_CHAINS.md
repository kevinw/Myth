# Gameplay Chains

Backlog of movement/combat changes that let the player chain moves together for more
dynamic, expressive traversal.

**Design rule:** a move should *exit into another move's entry, preserving momentum*. The
moves that already feel best follow this — roll (roll-jump carries speed via
`rollJumpCarryTimer`) and burrow (pops into a wall slide, see `PopOutOfBurrow`). Most other
moves are "terminators" that dump you back to Normal with little/no momentum; the items
below convert those into connectors.

---

## 1. Strike-bounce carries horizontal / re-aims at the next target

**Problem.** A successful `BlinkStrike` zeroes horizontal velocity on the bounce
(`Player.Blink.cs` — `Velocity.X = 0` right after `StartJump(BlinkStrikeBounceForce, …)`).
So an aerial pogo throws you straight up: you can't lance across a row of enemies or bounce
toward a wall to continue a chain.

**Change.**
- On the bounce, keep/redirect some horizontal instead of zeroing it — bias toward
  `input.MoveX` (player intent) so the bounce flows the direction you're holding.
- Optionally re-run `FindBlinkStrikeTarget()` immediately after a successful strike; if
  another enemy is in range, aim the carried horizontal toward it so strikes **chain**
  (strike → strike → strike down a line of enemies), and/or shorten the recovery so the
  next strike buffers cleanly.

**Hooks.** `Player.Blink.cs`: `BlinkStrikeState` (the `blinkStrikeResolved` branch where the
bounce is applied), `FindBlinkStrikeTarget`, `TryBlinkStrike`. New tunables alongside the
existing `BlinkStrike*` getters, e.g. `BlinkStrikeBounceCarry` (fraction of horizontal kept)
and a re-aim speed.

**Synergy.** Pairs with #2 — re-aimed bounces feel best when the follow-up strike input is
buffered. Also feeds wall slides: a bounce carried into a wall → wall slide → wall jump.

**Risk.** Low–medium. Tune the carry so chains stay skillful, not auto-piloted. Playtest
against clustered enemies.

---

## 2. Universal action buffering

**Problem.** Only *jump* is buffered (`jumpBufferTimer` / `JumpBufferTime`, set on
`input.JumpPressed` in `Player.Update`). Dash/roll/burrow/strike inputs pressed a few frames
early — i.e. during the tail of another move — are dropped, so tight chains feel like they
"eat" inputs.

**Change.** Generalize the jump buffer into a small "last action pressed + timestamp" record
(an action enum + a timer), and have each `TryStart*` / state-entry check consume a buffered
action the instant its state opens. This is the invisible glue that makes chains forgiving;
it amplifies every other item here.

**Hooks.** `Player.Update` (where `jumpBufferTimer` is set and decayed), the `TryStart*`
entry points (`TryStartRoll`, `TryStartDash` — if dash stays —, burrow arming in
`Player.cs`/`Player.Burrow.cs`, `TryStartPawnAction`). Keep the existing jump buffer behavior
as the first consumer so nothing regresses.

**Risk.** Low. Mostly additive; the main care is not double-firing an action across the
state transition (consume-and-clear on use).

---

## 3. Close the burrow ↔ wall-jump loop  *(burrows + walls)*

**Problem.** Burrow now pops into a wall slide (front half of a loop), but the back half is
blocked: `CanBurrow()` returns false while `wallSliding`, and `burrowNeedsGrounding` (set in
`PopOutOfBurrow`) forces a ground touch before the next burrow. So you can't go
`burrow up → slide → wall-jump across → burrow up the next wall`.

**Change.** Let a successful **wall-jump clear `burrowNeedsGrounding`** (one re-arm per
wall-jump). The slide itself still can't burrow (keep the `wallSliding` gate), so a burrow
always costs a real wall-jump — horizontal displacement between climbs, not a free elevator.

**Hooks.** `CanBurrow()` (`Player.cs`), `burrowNeedsGrounding`, the wall-jump branch in
`NormalState` (the `canWallJump` block that sets `wallJumpInputLockTimer`).

**Synergy.** This is the marquee traversal tech — completes the loop the wall-slide exit
opened. Pairs with #5 (forgiving wall-jumps make the loop landable).

**Risk.** Medium — playtest for unintended infinite climbs; the wall-jump cost is the guard.

---

## 4. Roll → burrow on wall contact  *(rolls + burrows)*

**Problem.** Rolling into a wall just stops (`OnCollideX`'s Roll branch → `OnRollStopped` +
`State = Normal`). All that roll speed is dumped at the wall.

**Change.** On a Roll hitting a wall, attempt a burrow up it instead of stopping.
`TryStartBurrow` already seeds `burrowSpeed` from incoming horizontal velocity, so a fast
roll dives into a fast wall-climb for free — the momentum-preserving exit the design rule asks
for.

**Hooks.** `OnCollideX` (`Player.cs`, the `State == States.Roll` branch), `TryStartBurrow`
(reads `previousVelocity.X`), `CanBurrow()`.

**Risk.** Low–medium. Gate on actually facing a burrowable surface so a roll into a normal
wall without burrow charge still just stops.

---

## 5. Wall coyote time + wall-jump buffer  *(walls + wall jumps)*

**Problem.** Ground jumps get coyote time (`CoyoteJumpTime`) and buffering, but wall-jumps
get neither — leave the wall a few frames early, or press a hair late, and the wall-jump is
lost. Wall chains feel stricter than ground ones.

**Change.** Mirror the ground affordances for walls: a short grace window where
`GetWallJumpDirection()` keeps returning the last wall after you've left it (wall coyote), and
let the jump buffer fire a wall-jump the instant you re-touch.

**Hooks.** `GetWallJumpDirection` / `ComputeWallJumpDirection` (the left/right solid probes),
`wallJumpInputLockTimer`, the buffer plumbing from #2.

**Synergy.** Direct multiplier on #3 — the burrow↔wall loop only feels good if the wall-jumps
in it are forgiving.

**Risk.** Low. Keep the grace window tight so it reads as forgiveness, not auto-cling.

---

## 6. Directional wall-jump: climb vs leap  *(wall jumps)*

**Problem.** The wall-jump is one fixed vector — `Velocity.X = wallJumpDirection *
WallJumpSpeed` with the standard `JumpForce`. No expressive control over arc.

**Change.** Branch on input at the jump: holding **toward** the wall → a steep *climb* jump
(more vertical, less horizontal) for scaling a single wall; holding **away** → a *leap* (more
horizontal) to cross a gap. Same button, intent-driven arc.

**Hooks.** The `canWallJump` branch in `NormalState` (where `wallJumpDirection` and
`Velocity.X` are set), `IsWallHugging` (already distinguishes toward/away), new tunables for
the two arcs.

**Risk.** Low–medium. Two tunables to dial; make the default (neutral) match today's feel.

---

## 7. Hit-confirm attack cancels  *(attacks)*

**Problem.** Attacks play out their full recovery whether or not they connect, so landing a
hit doesn't buy a faster follow-up — combat doesn't flow into movement.

**Change.** On a **landed** hit, open an early cancel window: the attack's recovery can cancel
into roll / jump / wall-jump / burrow. Rewards connecting and turns a kill into a re-launch.

**Hooks.** `OnPerformHit` (set a `hitConfirmTimer`), the attack/`BlinkStrike` states (allow
state changes while the timer is live), the buffer from #2 so the follow-up is forgiving.

**Synergy.** With #1, a re-aimed strike-bounce that hit-confirms chains strike→strike→move.

**Risk.** Medium. Cancel windows are powerful; keep them short and gated on a *confirmed* hit.

---

## 8. Pogo off terrain & hazards, not just enemies  *(attacks)*

**Problem.** `FindBlinkStrikeTarget` only considers `Masks.Enemy`, so the aerial pogo bounce
only exists over enemies. Over geometry or a hazard you just fall.

**Change.** Allow a downward strike to bounce off solid terrain (and optionally hazards, for a
risk/reward "spike-dance"). Extends the pogo into a traversal tool, not only a combat one.

**Hooks.** `FindBlinkStrikeTarget` / the bounce in `BlinkStrikeState`; add a downward variant
that probes room collision (and `Masks.Hazard`) beneath the player.

**Risk.** Medium. Bouncing off hazards needs clear telegraphing and i-frame interplay so it
reads as skill, not a damage trap.

---

## Notes
- **Dash** is being removed, so ignore dash-specific hooks above.
- Keep new constants as `static float X => …` getters (live-tunable under `dotnet watch`).
- Preserve existing invariants when relaxing gates — e.g. burrow's `burrowNeedsGrounding`
  prevents free infinite wall-climbs; any re-arm should still cost a real wall contact.
