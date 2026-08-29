namespace Myth;

// Records real play into per-room ReplayClips. While armed, the app runs a fixed ReplayHz timestep
// (so dt is a constant we never store), and each room load starts a fresh clip at that clean
// boundary (capturing the player's entry state + the live RNG stream position + starting progression).
// Every sim frame the frame count advances and a change-point is appended only when the input
// differs from the previous frame. Leaving a room auto-saves and begins the next. Recording is pure
// observation — it never perturbs the live game (the seed is captured, not reseeded).
sealed class ReplayRecorder(Game game)
{
	static int MinFrames => 30; // drop sub-second fragments (transient rooms you pass straight through)

	public bool Recording { get; private set; }
	ReplayClip? clip;

	public void Toggle()
	{
		Recording = !Recording;
		if (Recording)
			game.Manager.EnterReplayFixedStep(); // capture at a fixed 60fps cadence; display drops to 60 while armed
		else
		{
			Flush();
			game.Manager.ExitReplayFixedStep();
		}
		game.DisplayMessage(Recording ? "Replay: armed (press R to start a clean take)" : "Replay: stopped");
	}

	// game.RoomChanged. Only full spawns (InitialLoad/Reload) are clean clip starts: the player is
	// freshly instantiated with default internal timers (grounded/coyote/jumpBuffer/state clock),
	// which ReplayStart(pos/vel/facing/state) fully reproduces. Door transitions stream the player
	// in carrying that live state we don't snapshot, so we keep appending across them (they replay
	// deterministically from the clean start) rather than beginning a dirty clip.
	public void OnRoomChanged(RoomChange change)
	{
		if (!Recording) return;
		if (change.Kind is not (RoomChangeKind.InitialLoad or RoomChangeKind.Reload)) return;
		Flush();
		if (game.GetFirst<Player>() is not { } player) { clip = null; return; }
		clip = new ReplayClip
		{
			Seed = game.RngSeed,               // capture live stream position, don't reseed
			RoomId = change.To.Id,
			Start = ReplayStart.Capture(player),
			SaveJson = game.Save.ToJsonString(),
		};
		game.DisplayMessage($"Replay: clip started ({change.To.Id})");
	}

	// Once per sim frame, after Game.Update (from Manager.CompleteGameFrame).
	public void Tick()
	{
		if (!Recording || clip == null || game.Simulating) return;
		if (game.GetFirst<Player>() is not { } player) return;
		var input = player.ConsumedInput;
		if (clip.Changes.Count == 0 || !input.Equals(clip.Changes[^1].Input))
			clip.Changes.Add(new InputChange { Frame = clip.FrameCount, Input = input });
		clip.FrameCount++;
	}

	void Flush()
	{
		if (clip is { } c && c.FrameCount >= MinFrames)
		{
			var dir = Path.Join(game.Manager.UserPath, "replays");
			var secs = (int)MathF.Round(c.DurationSeconds);
			var baseName = $"{c.RoomId}_{(secs >= 60 ? $"{secs / 60}m{secs % 60}s" : $"{secs}s")}";
			var path = Path.Join(dir, $"{baseName}.myrp");
			for (var n = 2; File.Exists(path); n++) // don't clobber a same-room, same-length take
				path = Path.Join(dir, $"{baseName}_{n}.myrp");
			try { c.Save(path); game.DisplayMessage($"Replay saved: {Path.GetFileName(path)} ({c.FrameCount}f, {c.Changes.Count} changes)"); }
			catch (Exception e) { game.DisplayMessage($"Replay save failed: {e.Message}"); }
		}
		clip = null;
	}
}
