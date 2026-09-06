namespace Myth;

public interface IActivatable
{
	void SetActive(Actor source, bool active);
}

// A source whose activation latches in the save (see Game.IsButtonPressed), so whatever it
// opens stays open: targets can retire themselves for good rather than re-opening every visit.
public interface IActivationSource
{
	bool ActivationIsPersistent { get; }
}
