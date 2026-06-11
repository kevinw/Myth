using TinyLink;

var startArgs = new StartArgs(args);

var manager = new Manager(startArgs);
manager.Run();

#if !BROWSER
manager.Dispose();
#endif
