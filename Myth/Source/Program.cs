using Myth;

var startArgs = new StartArgs(args);
PlatformSpecific.AdjustStartArgsFromEnvironment(ref startArgs);
if (startArgs.buildAssetPack || startArgs.buildContentZipPath != null)
{
	if (startArgs.buildAssetPack)
		Assets.BuildVisualAssetPack(startArgs.assetPackPath, startArgs.dumpAtlases);
	if (startArgs.buildContentZipPath != null)
		Assets.BuildContentZip(startArgs.buildContentZipPath);
	return;
}

var manager = Manager.Create(startArgs);
PlatformSpecific.ManagerSetup(manager);
manager.Run();
PlatformSpecific.ManagerCleanup(manager);
