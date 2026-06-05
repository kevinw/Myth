using TinyLink;

#if BROWSER
var manager = new Manager(dumpAtlases: args.Contains("--dump-atlases"), gym: args.Contains("--gym"), headless: args.Contains("--headless"), unthrottled: args.Contains("--unthrottled"), gymExitAfterAll: args.Contains("--gym-exit-after-all"));
#else
using var manager = new Manager(dumpAtlases: args.Contains("--dump-atlases"), gym: args.Contains("--gym"), headless: args.Contains("--headless"), unthrottled: args.Contains("--unthrottled"), gymExitAfterAll: args.Contains("--gym-exit-after-all"));
#endif
manager.Run();
