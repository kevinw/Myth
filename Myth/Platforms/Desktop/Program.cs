//
// TODO: move this file out of Platforms/Desktop -- it is now the entry point for all platforms
//
using Myth;

var startArgs = new StartArgs(args);
PlatformSpecific.AdjustStartArgsFromEnvironment(ref startArgs);
if (startArgs.buildAssetPack)
{
	Assets.BuildVisualAssetPack(startArgs.assetPackPath, startArgs.dumpAtlases);
	return;
}

var manager = Manager.Create(startArgs);
PlatformSpecific.ManagerSetup(manager);
manager.Run();
PlatformSpecific.ManagerCleanup(manager);
