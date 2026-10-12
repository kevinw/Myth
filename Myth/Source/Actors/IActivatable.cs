namespace Myth;

public interface IActivatable
{
	void SetActive(Actor source, bool active);
}

public static class Activation
{
	// Sets every target; false while any of them can't be found yet (not spawned), so a latched
	// source knows to try again.
	public static bool SetTargets(this Actor source, string[] targetIds, bool active)
	{
		var found = true;
		foreach (var id in targetIds)
			if (source.Game.GetActorById(id) is IActivatable target)
				target.SetActive(source, active);
			else
				found = false;
		return found;
	}
}

// A source whose activation latches in the save (see Game.IsButtonPressed), so whatever it
// opens stays open: targets can retire themselves for good rather than re-opening every visit.
public interface IActivationSource
{
	bool ActivationIsPersistent { get; }
}
