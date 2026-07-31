namespace Myth;

public struct StartArgs
{
	public bool dumpAtlases;
	public bool gym;
	public bool audio = true;
	public bool headless;
	public bool unthrottled;
	public bool gymExitAfterAll;
	public bool traceNav;
	public bool traceNavFrames;
	public bool simple;
	public bool bench;
	public bool? fullscreen;
	// Force the 4:3 portrait-style presentation on desktop for testing (--aspect-four-three):
	// pillarbox the 16:9 game into a centered 4:3 rect and crop the sides (tap/hold to un-crop).
	public bool aspectFourThree;
	public bool play; // if true, play immediately on startup even in desktop/editor-capable environments
	// Skip the boot shader/pipeline warmup + loading screen (--no-warmup). For A/B testing the
	// warmup against a cold driver shader cache: with this set, pipelines compile lazily on first
	// room entry (the original hitch) instead of up front. See tools/windows/clear-system-shader-caches.mjs.
	public bool noWarmup;
	public bool buildAssetPack;
	public bool useAssetPack;
	public string? assetPackPath;
	// Load bundled content from this zip instead of the Assets folder (--content-zip).
	public string? contentZipPath;
	// Write a Content.zip of the runtime Assets slice to this path and exit (--build-content-zip).
	public string? buildContentZipPath;
	public string? contentTarget;
	public string? contentWorld;
	public int maxFrames = int.MaxValue;

	// Exit after this many game seconds (for automated/profiling runs).
	public float maxTimeSeconds = float.PositiveInfinity;
	// Start play immediately in this room (by Room.Id) on startup.
	public string? playRoomId;
	public string? traceJsonPath;
	public string? traceRoomId;
	public string? traceGoalId;
	public string[] gymGoalIds = [];
	public int? traceEdgeFrom;
	public int? traceEdgeTo;

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
			else if (args[i] == "--content-zip" && next != null)
				contentZipPath = next;
			else if (args[i] == "--build-content-zip" && next != null)
				buildContentZipPath = next;
			else if (args[i] == "--content-target" && next != null)
				contentTarget = next;
			else if (args[i] == "--content-world" && next != null)
				contentWorld = next;
			else if (args[i] == "--max-time" && next != null && float.TryParse(next, out var mt))
				maxTimeSeconds = mt;
			else if ((args[i] == "--play-room" || args[i] == "--room") && next != null)
				playRoomId = next;
			else if (args[i] == "--trace-json" && next != null)
				traceJsonPath = next;
			else if (args[i] == "--trace-room" && next != null)
				traceRoomId = next;
			else if (args[i] == "--trace-goal" && next != null)
				traceGoalId = next;
			else if (args[i] == "--gym-goal" && next != null)
				gymGoalIds = [..gymGoalIds, ..ParseList(next)];
			else if (args[i] == "--trace-edge" && next != null)
				ParseTraceEdge(next, out traceEdgeFrom, out traceEdgeTo);
		}

		dumpAtlases = args.Contains("--dump-atlases");
		buildAssetPack = args.Contains("--build-asset-pack");
		useAssetPack = args.Contains("--asset-pack");
		var gymCli = args.Contains("--gym-cli");
		gym = gymCli || args.Contains("--gym");
		headless = gymCli || args.Contains("--headless");
		unthrottled = gymCli || args.Contains("--unthrottled");
		gymExitAfterAll = gymCli || args.Contains("--gym-exit-after-all");
		traceNav = args.Contains("--trace-nav") || traceJsonPath != null;
		traceNavFrames = args.Contains("--trace-nav-frames");
		simple = args.Contains("--simple");
		bench = args.Contains("--bench");
		fullscreen = args.Contains("--fullscreen") ? true : null;
		aspectFourThree = args.Contains("--aspect-four-three");
		noWarmup = args.Contains("--no-warmup");
		play = args.Contains("--play") || screenshotPath != null || playRoomId != null;
	}

	private static IEnumerable<string> ParseList(string value)
	{
		foreach (var part in value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
			yield return part;
	}

	private static void ParseTraceEdge(string value, out int? from, out int? to)
	{
		from = null;
		to = null;
		var parts = value.Replace("->", ":", StringComparison.Ordinal).Split(':', StringSplitOptions.TrimEntries);
		if (parts.Length == 2)
		{
			if (int.TryParse(parts[0], out var parsedFrom))
				from = parsedFrom;
			if (int.TryParse(parts[1], out var parsedTo))
				to = parsedTo;
		}
	}
}
