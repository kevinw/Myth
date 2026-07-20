using Myth;

var startArgs = new StartArgs(args);
PlatformSpecific.AdjustStartArgsFromEnvironment(ref startArgs);
if (startArgs.buildAssetPack || startArgs.buildContentZipPath != null)
{
	if (startArgs.buildAssetPack)
		Assets.BuildVisualAssetPack(startArgs.assetPackPath, startArgs.dumpAtlases);
	if (startArgs.buildContentZipPath != null)
	{
		if (string.IsNullOrWhiteSpace(startArgs.contentTarget))
			throw new ArgumentException("--build-content-zip requires --content-target mac, ios, windows, linux, or browser.");
		Assets.BuildContentZip(startArgs.buildContentZipPath, startArgs.contentTarget, startArgs.contentWorld);
	}
	return;
}

#if !BROWSER
// Route the windowed game's logs to %APPDATA%\Mycelon\logs so the console-less shipped build
// stays diagnosable. Headless/CLI runs keep plain console output for their tooling.
if (!startArgs.headless)
	LogFile.Install(Manager.APPLICATION_NAME);
#endif

var manager = Manager.Create(startArgs);
PlatformSpecific.ManagerSetup(manager);
manager.Run();
PlatformSpecific.ManagerCleanup(manager);
