using Myth;

var startArgs = new StartArgs(args);
PlatformSpecific.AdjustStartArgsFromEnvironment(ref startArgs);
if (startArgs.buildAssetPack)
{
	Assets.BuildVisualAssetPack(startArgs.assetPackPath, startArgs.dumpAtlases);
	return;
}

var manager = new Manager(startArgs);
PlatformSpecific.ManagerSetup(manager);
manager.Run();
PlatformSpecific.ManagerCleanup(manager);
