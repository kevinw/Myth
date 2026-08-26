namespace Myth;

// Drives a Game through a recorded ReplayClip: each sim frame feeds the recorded dt into the sim's
// clock and the recorded input into the player, then lets the normal Game.Update run — so AI,
// physics and transitions all regenerate deterministically. The room load + ReplayStart + seed that
// set up the initial state are done by Manager.StartReplay before this takes over.
sealed class ReplayPlayer(ReplayClip clip, int playUntil = -1)
{
	public ReplayClip Clip => clip;
	int cursor;
	public int Cursor => cursor;
	// playUntil (-1 = the clip's natural end) lets an attract entry trim a boring tail.
	readonly int end = playUntil < 0 ? clip.Frames.Count : Math.Min(playUntil, clip.Frames.Count);
	public bool Done => cursor >= end;

	// Before Game.Update: install this frame's recorded dt + input.
	public void DriveBeforeUpdate(Game game)
	{
		if (Done) return;
		var frame = clip.Frames[cursor];
		game.AdvanceReplayClock(frame.Dt);
		if (game.GetFirst<Player>() is { } player)
			player.ScriptedInput = frame.Input;
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
