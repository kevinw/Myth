namespace Myth;

#if BROWSER
using System.Runtime.InteropServices.JavaScript;

public static partial class PlatformSpecific
{
	[JSImport("setVirtualControllerVisible", "MythOverlay")]
	internal static partial void SetVirtualControllerVisible(bool visible);

	[JSImport("getAudioDisabled", "MythOverlay")]
	internal static partial bool GetAudioDisabled();

	public static void AdjustStartArgsFromEnvironment(ref StartArgs startArgs) {
		if (GetAudioDisabled()) {
			startArgs.audio = false;
		}
	}

	public static void ManagerSetup(Manager manager) {
		// Show/hide the on-screen touch controller (main.js / virtual-gamepad.js),
		// mirroring the iOS GCVirtualController wiring in MythIos/Program.cs.
		SetVirtualControllerVisible(manager.WantsVirtualGameController);
		manager.VirtualGameControllerVisibilityChanged += SetVirtualControllerVisible;
	}

	public static void ManagerCleanup(Manager _) {}

}
#else
public static class PlatformSpecific
{
	public static void AdjustStartArgsFromEnvironment(ref StartArgs _) { }
	public static void ManagerSetup(Manager _) { }
	public static void ManagerCleanup(Manager manager) { manager.Dispose(); }
}
#endif
