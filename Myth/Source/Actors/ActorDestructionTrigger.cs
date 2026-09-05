namespace Myth;

sealed class ActorDestructionTrigger(Actor owner, string sourceActorId)
{
	public bool Triggered { get; private set; } = owner.Game.WasActorDestroyedPersistently(owner.Room, sourceActorId);
	bool sourceSeen = owner.Game.GetActorById(sourceActorId) != null;

	public bool Update()
	{
		if (Triggered)
			return false;
		if (owner.Game.GetActorById(sourceActorId) != null)
		{
			sourceSeen = true;
			return false;
		}
		if (!sourceSeen)
			return false;
		Triggered = true;
		return true;
	}
}
