namespace Myth;

sealed class PersistentDestructionGate(Actor owner, string sourceActorId)
{
	public bool IsOpen { get; private set; } = owner.Game.WasActorDestroyedPersistently(owner.Room, sourceActorId);
	bool sourceSeen = owner.Game.GetActorById(sourceActorId) != null;

	public bool TryOpen()
	{
		if (IsOpen)
			return false;
		if (owner.Game.GetActorById(sourceActorId) != null)
		{
			sourceSeen = true;
			return false;
		}
		if (!sourceSeen)
			return false;
		IsOpen = true;
		return true;
	}
}
