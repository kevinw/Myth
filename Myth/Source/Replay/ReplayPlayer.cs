namespace Myth;

// Drives a Game through a recorded ReplayClip: each sim frame feeds the recorded dt into the sim's
// clock and the recorded input into the player, then lets the normal Game.Update run — so AI,
// physics and transitions all regenerate deterministically. The room load + ReplayStart + seed that
// set up the initial state are done by Manager.StartReplay before this takes over.
sealed class ReplayPlayer(ReplayClip clip, int playUntil = -1)
{
	public ReplayClip Clip => clip;
	int cursor;
	int changeIndex;
	public int Cursor => cursor;
	// playUntil (-1 = the clip's natural end) lets an attract entry trim a boring tail.
	readonly int end = playUntil < 0 ? clip.FrameCount : Math.Min(playUntil, clip.FrameCount);
	public bool Done => cursor >= end;

	// Before Game.Update: advance the sim by the fixed replay timestep and install this frame's
	// input (the change-point still in effect at this frame).
	public void DriveBeforeUpdate(Game game)
	{
		if (Done) return;
		game.AdvanceReplayClock(ReplayClip.ReplayDelta);
		while (changeIndex + 1 < clip.Changes.Count && clip.Changes[changeIndex + 1].Frame <= cursor)
			changeIndex++;
		if (clip.Changes.Count > 0 && game.GetFirst<Player>() is { } player)
			player.ScriptedInput = clip.Changes[changeIndex].Input;
	}

	// After Game.Update: advance to the next recorded frame.
	public void DriveAfterUpdate() => cursor++;

	public static string? NewestClipPath(string userPath)
	{
		var dir = Path.Join(userPath, "replays");
		if (!Directory.Exists(dir)) return null;
		return new DirectoryInfo(dir).GetFiles("*.myrp")
			.OrderByDescending(f => f.LastWriteTimeUtc)
			.FirstOrDefault()?.FullName;
	}
}
