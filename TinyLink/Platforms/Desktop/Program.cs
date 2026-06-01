using TinyLink;

using var manager = new Manager(dumpAtlases: args.Contains("--dump-atlases"));
manager.Run();
