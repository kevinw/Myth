using TinyLink;

#if BROWSER
var manager = new Manager(dumpAtlases: args.Contains("--dump-atlases"), gym: args.Contains("--gym"), headless: args.Contains("--headless"), unthrottled: args.Contains("--unthrottled"), gymExitAfterAll: args.Contains("--gym-exit-after-all"), simple: args.Contains("--simple"));
#else
using var manager = new Manager(dumpAtlases: args.Contains("--dump-atlases"), gym: args.Contains("--gym"), headless: args.Contains("--headless"), unthrottled: args.Contains("--unthrottled"), gymExitAfterAll: args.Contains("--gym-exit-after-all"), simple: args.Contains("--simple"));
#endif
manager.Run();
