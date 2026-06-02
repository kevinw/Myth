using TinyLink;

#if BROWSER
var manager = new Manager(dumpAtlases: args.Contains("--dump-atlases"));
#else
using var manager = new Manager(dumpAtlases: args.Contains("--dump-atlases"));
#endif
manager.Run();
