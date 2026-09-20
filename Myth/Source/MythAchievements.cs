namespace Myth;

// Steam achievement ids and the save-state questions that earn them. The ids below belong to
// Spacewar (app 480), Valve's public test app, and stay until Human Questions has its own appid
// and its own achievement list -- at which point only the strings here change.
//
// A rule is a question about the save, never an event: see Kit.Achievements.
static class MythAchievements
{
	const string SkullBossDefeated = "ACH_WIN_ONE_GAME";

	// The skull boss dies persistently (EyeLaserPuppet.DeathIsPersistent), so the save already knows
	// it is dead and nothing has to be recorded at the kill. Reads the Manager's save rather than a
	// live Game so a sweep still answers on the title screen.
	public static void Register(Manager manager)
		=> Achievements.Register(SkullBossDefeated,
			() => Game.AnyPersistentlyDestroyedOfType(manager.CurrentSave, nameof(EyeLaserPuppet)));
}
