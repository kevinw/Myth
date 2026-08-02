using Myth;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("Mycelon.Scenario")]

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
var stopWatch = manager.UserQuitRequested;
PlatformSpecific.ManagerCleanup(manager);
#if !BROWSER
if (stopWatch)
	WatchProcess.Stop();

static class WatchProcess
{
	const int InterruptSignal = 2;

	[DllImport("libc", EntryPoint = "kill")]
	static extern int Kill(int process, int signal);

	public static void Stop()
	{
		// When running as "dotnet watch" we actually want, when the user has pressed command-Q or closed the window,
		// to send a signal to stop, so that dotnet watch doesn't proceed to then resurrect our process when we edit
		// a file. the normal "keep the game open and edit stuff" flow remains unchanged.
		if (Environment.GetEnvironmentVariable("DOTNET_WATCH") == "1" &&
			(OperatingSystem.IsMacOS() || OperatingSystem.IsLinux()))
			Kill(0, InterruptSignal);
	}
}
#endif
