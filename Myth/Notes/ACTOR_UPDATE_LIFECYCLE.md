# Actor Update Lifecycle

## Problem

`Actor.Update()` currently does two unrelated things:

1. Moves the actor using its current `Velocity`.
2. Advances shared state such as animation, `Timer`, `Squish`, and `Skew`.

Derived actors choose when both happen by where they put `base.Update()`. If they calculate input, AI, gravity, or acceleration after that call, the new velocity is not integrated until the next simulation tick.

This is easy to miss because both orders look reasonable:

```csharp
public override void Update()
{
	base.Update();
	Velocity = ChooseVelocity(); // moves next tick
}
```

```csharp
public override void Update()
{
	Velocity = ChooseVelocity();
	base.Update(); // moves this tick
}
```

Pawn previously had the first form, adding one simulation tick between fresh player input and visible movement. Other actors are inconsistent too: Blob calculates jumps after moving, Mosquito steers after moving, and Bullet applies gravity after moving, while Pickup calculates its velocity before moving.

## Proposed flow

Make the update order a non-virtual template owned by `Actor`:

```csharp
internal void Tick()
{
	if (!ShouldUpdate())
		return;

	AdvanceFrame();
	UpdateBeforeMove();
	IntegrateMotion();
	UpdateAfterMove();
}

protected virtual bool ShouldUpdate() => true;
protected virtual void UpdateBeforeMove() { }
protected virtual void IntegrateMotion() => MoveByVelocity();
protected virtual void UpdateAfterMove() { }
```

`Game` calls `actor.Tick()`. Derived actors override phases and never call `base.Update()`, so they cannot accidentally place the movement boundary on the wrong side of their velocity calculation.

The phases mean:

- `UpdateBeforeMove`: read input, run AI and state machines, choose velocity, apply acceleration and gravity.
- `IntegrateMotion`: perform exactly one movement operation. Custom movers such as platforms, wall followers, ropes, and burrowing actors can override it.
- `UpdateAfterMove`: inspect the resulting position and collision-mutated velocity; handle grounding, landing, hazards, and carried-object positioning.
- `AdvanceFrame`: advance shared animation and timers automatically.
- `ShouldUpdate`: suspend the complete tick for cases such as rewind or the frozen Gym player.

`UpdateBeforeMove` and `UpdateAfterMove` are preferable to generic names like `PreUpdate` and `PostUpdate`: their relationship to collision and movement is explicit.

## Keep the phases per actor

Initially, preserve the existing sequential actor model:

```text
Actor A: BeforeMove -> Move -> AfterMove
Actor B: BeforeMove -> Move -> AfterMove
```

Do not turn this into three global passes yet:

```text
All BeforeMove -> All Move -> All AfterMove
```

Global passes could make some actor-to-actor decisions less dependent on list order, but they would also substantially change moving-platform, attachment, and collision behavior. That is a separate physics architecture project, not a prerequisite for fixing latency and update-order mistakes.

## Migration

1. Add the non-virtual `Tick()` template and have `Game` call it.
2. Migrate Pawn using the existing `AdvanceFrame()` / `MoveByVelocity()` split.
3. Migrate ordinary velocity-driven actors in small groups, deciding which work belongs before and after movement.
4. Override `IntegrateMotion()` for actors with custom movement.
5. Remove virtual `Update()` after all actors are migrated.

This migration will change some simulation and replay results. That is acceptable, but movement feel should be checked and tuned as each gameplay-sensitive group moves over.

## Expected result

- Fresh player input affects position in the same simulation tick.
- AI steering, jumps, acceleration, and gravity use one consistent integration order.
- Landing and collision consequences reliably observe the new position.
- Each actor has one obvious movement boundary.
- The update-order mistake becomes structurally impossible instead of relying on comments or remembering where to call `base.Update()`.
