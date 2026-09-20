namespace Kit;

/// Awards achievements by asking the game whether each one is *currently* earned, instead of firing
/// them where the thing happens. Each rule is a predicate over the save, and a sweep re-checks the
/// ones Steam still has locked, so: a rule added after release lights up on an old save that already
/// qualifies, an unlock missed while Steam was down heals on the next sweep, and the game never has
/// to record the same fact twice (once for itself, once for Steam).
///
/// Steam holds the "already unlocked" state, so there is nothing to persist here. Sweeping is cheap
/// but not free -- predicates may walk the save -- so call it at a debounced point, such as when the
/// save is flushed, rather than every frame.
public static class Achievements
{
	static readonly List<(string Id, Func<bool> Earned)> rules = [];

	static Achievements() => Steam.StatsReceived += Sweep;

	public static void Register(string id, Func<bool> earned)
		=> rules.Add((id, earned));

	public static void Sweep()
	{
		// A failed read means Steam hasn't delivered stats yet: skip, and the next sweep retries.
		foreach (var (id, earned) in rules)
			if (Steam.TryGetAchievement(id, out var unlocked) && !unlocked && earned())
				Steam.SetAchievement(id);
	}
}
