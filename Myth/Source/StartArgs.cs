namespace Myth;

public struct StartArgs
{
	public bool dumpAtlases;
	public bool gym;
	public bool audio = true;
	public bool headless;
	public bool unthrottled;
	public bool gymExitAfterAll;
	public bool simple;
	public bool bench;
	public bool play; // if true, play immediately on startup even in desktop/editor-capable environments
	public bool buildAssetPack;
	public bool useAssetPack;
	public string? assetPackPath;
	public int maxFrames = int.MaxValue;

	// Exit after this many game seconds (for automated/profiling runs).
	public float maxTimeSeconds = float.PositiveInfinity;
	// Start play immediately in this room (by Room.Id) on startup.
	public string? playRoomId;

	// Automated screenshot capture (desktop). When screenshotPath is set the
	// game plays immediately, captures Game.Screen at screenshotFrame and every
	// screenshotInterval frames after, screenshotCount times, then exits.
	public string? screenshotPath;
	public int screenshotFrame = 60;
	public int screenshotInterval = 60;
	public int screenshotCount = 1;

	public StartArgs(string[] args)
	{
		maxFrames = int.MaxValue;
		for (int i = 0; i < args.Length; i++)
		{
			var next = i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal) ? args[i + 1] : null;
			if (args[i] == "--max-frames" && next != null && int.TryParse(next, out var mf))
				maxFrames = mf;
			else if (args[i] == "--screenshot" && next != null)
				screenshotPath = next;
			else if (args[i] == "--screenshot-frame" && next != null && int.TryParse(next, out var sf))
				screenshotFrame = sf;
			else if (args[i] == "--screenshot-interval" && next != null && int.TryParse(next, out var si))
				screenshotInterval = si;
			else if (args[i] == "--screenshot-count" && next != null && int.TryParse(next, out var sc))
				screenshotCount = sc;
			else if ((args[i] == "--build-asset-pack" || args[i] == "--asset-pack") && next != null)
				assetPackPath = next;
			else if (args[i] == "--max-time" && next != null && float.TryParse(next, out var mt))
				maxTimeSeconds = mt;
			else if (args[i] == "--play-room" && next != null)
				playRoomId = next;
		}

		dumpAtlases = args.Contains("--dump-atlases");
		buildAssetPack = args.Contains("--build-asset-pack");
		useAssetPack = args.Contains("--asset-pack");
		gym = args.Contains("--gym");
		headless = args.Contains("--headless");
		unthrottled = args.Contains("--unthrottled");
		gymExitAfterAll = args.Contains("--gym-exit-after-all");
		simple = args.Contains("--simple");
		bench = args.Contains("--bench");
		play = args.Contains("--play") || screenshotPath != null || playRoomId != null;
	}
}
