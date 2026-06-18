using Myth;

var startArgs = new StartArgs(args);
if (startArgs.buildAssetPack)
{
	Assets.BuildVisualAssetPack(startArgs.assetPackPath, startArgs.dumpAtlases);
	return;
}

var manager = new Manager(startArgs);
manager.Run();

#if !BROWSER
manager.Dispose();
#endif
