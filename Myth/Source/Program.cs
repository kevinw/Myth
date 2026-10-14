using Myth;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("Mycelon.Scenario")]

StartArgs startArgs;
try
{
	startArgs = new(args);
}
catch (ArgumentException e)
{
	Console.Error.WriteLine($"Argument error: {e.Message}");
	Environment.ExitCode = 2;
	return;
}
PlatformSpecific.AdjustStartArgsFromEnvironment(ref startArgs);
#if !BROWSER && !MYTHSHIP
if (startArgs.devCommand != null)
{
	Environment.ExitCode = DevHost.Send(startArgs.devCommand) ? 0 : 1;
	return;
}
#endif
#if !MYTHSHIP
if (startArgs.unscramblePakPath != null)
{
	Assets.UnscrambleContentPak(startArgs.unscramblePakPath);
	return;
}
if (startArgs.buildAssetPack || startArgs.stageContentTargets.Length > 0)
{
	Assets.BuildVisualAssetPack(startArgs.assetPackPath, startArgs.dumpAtlases);
	foreach (var target in startArgs.stageContentTargets)
		Assets.StageContent(target, startArgs.worldId);
	return;
}
#endif

#if !BROWSER
// Route the windowed game's logs to %APPDATA%\Mycelon\logs so the console-less shipped build
// stays diagnosable. Headless/CLI runs keep plain console output for their tooling.
if (!startArgs.IsToolRun)
	LogFile.Install(Manager.APPLICATION_NAME);
#endif

try
{
#if !BROWSER && !MYTHSHIP
	if (startArgs.devHost)
	{
		DevHost.Run(startArgs);
		return;
	}
#endif
#if EDITOR && !BROWSER
	if (!startArgs.IsToolRun)
		DevHost.Listen();
#endif
	var manager = Manager.Create(startArgs);
	PlatformSpecific.ManagerSetup(manager);
	manager.Run();
	var stopWatch = manager.UserQuitRequested;
	PlatformSpecific.ManagerCleanup(manager);
#if !BROWSER
	if (stopWatch)
		WatchProcess.Stop();
#endif
}
catch (Exception e)
{
	Console.Error.WriteLine($"[Crash] {e}");
	throw;
}

#if !BROWSER
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
