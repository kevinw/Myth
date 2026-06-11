namespace TinyLink;

public struct StartArgs
{
	public bool dumpAtlases;
	public bool gym;
	public bool headless;
	public bool unthrottled;
	public bool gymExitAfterAll;
	public bool simple;
	public bool bench;
	public bool play; // if true, play immediately on startup even in desktop/editor-capable environments
	public int maxFrames = int.MaxValue;

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
		for (int i = 0; i < args.Length - 1; i++)
		{
			if (args[i] == "--max-frames" && int.TryParse(args[i + 1], out var mf))
				maxFrames = mf;
			else if (args[i] == "--screenshot")
				screenshotPath = args[i + 1];
			else if (args[i] == "--screenshot-frame" && int.TryParse(args[i + 1], out var sf))
				screenshotFrame = sf;
			else if (args[i] == "--screenshot-interval" && int.TryParse(args[i + 1], out var si))
				screenshotInterval = si;
			else if (args[i] == "--screenshot-count" && int.TryParse(args[i + 1], out var sc))
				screenshotCount = sc;
		}

		dumpAtlases = args.Contains("--dump-atlases");
		gym = args.Contains("--gym");
		headless = args.Contains("--headless");
		unthrottled = args.Contains("--unthrottled");
		gymExitAfterAll = args.Contains("--gym-exit-after-all");
		simple = args.Contains("--simple");
		bench = args.Contains("--bench");
		play = args.Contains("--play") || screenshotPath != null;
	}
}
