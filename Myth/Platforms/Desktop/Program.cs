using Myth;
#if BROWSER
using System.Runtime.InteropServices.JavaScript;
#endif

var startArgs = new StartArgs(args);
if (startArgs.buildAssetPack)
{
	Assets.BuildVisualAssetPack(startArgs.assetPackPath, startArgs.dumpAtlases);
	return;
}

var manager = new Manager(startArgs);

#if BROWSER
// Show/hide the on-screen touch controller (main.js / virtual-gamepad.js),
// mirroring the iOS GCVirtualController wiring in MythIos/Program.cs.
MythWebOverlay.SetVirtualControllerVisible(manager.WantsVirtualGameController);
manager.VirtualGameControllerVisibilityChanged += MythWebOverlay.SetVirtualControllerVisible;
#endif

manager.Run();

#if !BROWSER
manager.Dispose();
#endif

#if BROWSER
internal static partial class MythWebOverlay
{
	[System.Runtime.Versioning.SupportedOSPlatform("browser")]
	[JSImport("setVirtualControllerVisible", "MythOverlay")]
	internal static partial void SetVirtualControllerVisible(bool visible);
}
#endif
