# Triggers

An idea for authoring relationships between actors without building a general component
system or teaching every source actor about every possible target.

## The useful split

A triggered interaction has three separate parts:

1. A **condition** becomes true: an actor is destroyed, a player enters an area, a switch is
   pressed, and so on.
2. Something recognizes both the current state and the live transition. For persistent
   destruction this means checking the save when the room loads and noticing the actor
   disappear while the room is running.
3. A target-specific **reaction** happens: shut down an NCA, disable a prop's emissive layer,
   reveal a platform, start a story sequence, etc.

`ActorDestructionTrigger` currently encapsulates the second part for the first condition.
The reaction still lives on the target actor, where its meaning and implementation are easy
to understand.

## Proposed authoring pattern

Keep `IActivatable` for reversible state controlled by things such as held buttons:

```csharp
public interface IActivatable
{
	void SetActive(Actor source, bool active);
}
```

Add a separate capability for one-shot events:

```csharp
public interface ITriggerable
{
	void Trigger(Actor source);
}
```

Then add a small editor-placeable system actor, tentatively called
`ActorDestroyedTrigger`, with two references:

```csharp
[ActorRef] public string SourceActorId = "";
[ActorRef(typeof(ITriggerable))] public string TargetActorId = "";
```

It owns an `ActorDestructionTrigger`. When the condition becomes true, it resolves the
target and calls `Trigger(this)`.

Possible target implementations:

- `NeuralTextureActor.Trigger()` begins its shutdown.
- `Prop.Trigger()` applies its configured `PropChangeType`.
- `MovingPlatform.Trigger()` reveals a platform that starts hidden.
- A story or effects actor starts its sequence.

One source can drive several targets by placing several trigger actors. Several sources can
drive one target in the same way. Neither side needs hard-coded knowledge of the other.

The editor should eventually draw the links between source, trigger, and target. The wiring
then becomes visible room structure rather than inspector fields whose effects are hard to
discover.

## Persistence and room locality

The trigger actor only needs to exist while its room exists. On `Added`, its
`ActorDestructionTrigger` reconstructs an already-satisfied persistent condition from the
save. During play it detects the live transition. This preserves the behavior when returning
to a room without requiring actors in unloaded rooms to exist.

Initially, keep source, trigger, and target in the same room. Cross-room effects need a
different persistent world-state concept and should not emerge accidentally from actor-ID
wiring.

## Why not general actor components yet?

Myth already has two useful kinds of composition:

- `ActorSettings` flattens reusable configuration into an actor's normal JSON and inspector.
- Small runtime helpers such as `ActorDestructionTrigger`, `HumanoidRigVisual`, and
  `NeuralSkinRuntime` share implementation while actors retain explicit behavior.

An editor-facing “Add Component” system would also require polymorphic component JSON,
component registration for trimming/AOT, add/remove/reorder UI, prefab inheritance rules,
live-sync support, lifecycle and update ordering, and a way to inspect the resulting call
path. That is a large second object model beside actors.

A lightweight **code-only** behavior mechanism may become worthwhile if many unrelated
actors acquire the same lifecycle boilerplate. For example:

```csharp
WatchActorDestroyed(SourceActorId, OnSourceDestroyed);
```

`Actor` or `Game` could centrally update registered watches so an actor cannot forget to
poll one. This should be introduced only when repeated real uses establish its lifecycle,
ordering, and cleanup requirements. It does not need to become an authorable component
array.

## Questions to settle before implementing

- Is `ITriggerable.Trigger` enough, or do targets need a small typed signal?
- Should an already-persisted condition invoke the same animated reaction or an immediate
  settled-state variant?
- What should happen when the target cannot be found: retry, report once, or fail visibly in
  the editor?
- Does one-shot mean once per actor lifetime, once per room visit, or persistently once?
- Should the trigger actor draw arrows and status in both edit and play modes?
- Can a trigger target another trigger to form relays, delays, AND conditions, or sequences,
  or should those remain purpose-built actors?

The first implementation should answer only what the destruction-to-target use cases need.
Avoid a generic condition/action language until authored rooms demonstrate that it would
remove more complexity than it creates.
