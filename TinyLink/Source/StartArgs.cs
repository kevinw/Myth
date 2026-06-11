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

	public StartArgs(string[] args)
	{
		maxFrames = int.MaxValue;
		for (int i = 0; i < args.Length - 1; i++)
			if (args[i] == "--max-frames" && int.TryParse(args[i + 1], out var mf))
				maxFrames = mf;

		dumpAtlases = args.Contains("--dump-atlases");
		gym = args.Contains("--gym");
		headless = args.Contains("--headless");
		unthrottled = args.Contains("--unthrottled");
		gymExitAfterAll = args.Contains("--gym-exit-after-all");
		simple = args.Contains("--simple");
		bench = args.Contains("--bench");
		play = args.Contains("--play");
	}
}
