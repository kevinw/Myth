using TinyLink;

var maxFrames = int.MaxValue;
for (int i = 0; i < args.Length - 1; i++)
	if (args[i] == "--max-frames" && int.TryParse(args[i + 1], out var mf))
		maxFrames = mf;

#if BROWSER
var manager = new Manager(
	dumpAtlases: args.Contains("--dump-atlases"),
	gym: args.Contains("--gym"),
	headless: args.Contains("--headless"),
	unthrottled: args.Contains("--unthrottled"),
	gymExitAfterAll: args.Contains("--gym-exit-after-all"),
	simple: args.Contains("--simple"),
	bench: args.Contains("--bench"),
	maxFrames: maxFrames);
#else
using var manager = new Manager(
	dumpAtlases: args.Contains("--dump-atlases"),
	gym: args.Contains("--gym"),
	headless: args.Contains("--headless"),
	unthrottled: args.Contains("--unthrottled"),
	gymExitAfterAll: args.Contains("--gym-exit-after-all"),
	simple: args.Contains("--simple"),
	bench: args.Contains("--bench"),
	maxFrames: maxFrames);
#endif
manager.Run();
