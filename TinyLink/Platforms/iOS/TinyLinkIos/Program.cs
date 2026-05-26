using SDL3;
using Foster.Framework;
using Foundation;
using GameController;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using TinyLink;
using ImGuiNET;
using static SDL3.SDL;

namespace TinyLinkIos;

internal static class Program
{
	private static GCVirtualController? virtualController;

	public static unsafe int Main(string[] args)
	{
		NativeLibrary.SetDllImportResolver(typeof(SDL).Assembly, ResolveNativeLibrary);
		NativeLibrary.SetDllImportResolver(typeof(ImGui).Assembly, ResolveNativeLibrary);
		return SDL_RunAppAot(0, nint.Zero, &RunTinyLink, nint.Zero);
	}

	[UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
	private static int RunTinyLink(int argc, nint argv)
	{
		try
		{
			using var manager = new Manager();
			manager.VirtualGameControllerVisibilityChanged += SetVirtualControllerVisible;
			SetVirtualControllerVisible(manager.WantsVirtualGameController);
			LogControllerConnections(manager);
			manager.Run();
			DisconnectVirtualController();
			return 0;
		}
		catch (Exception e)
		{
			Console.Error.WriteLine(e);
			return 1;
		}
	}

	private static void ConnectVirtualController()
	{
		if (virtualController != null)
			return;

		var configuration = new GCVirtualControllerConfiguration
		{
			Elements = new NSSet<NSString>(
				GCInputDirectionPadName.LeftThumbstick.GetConstant()!,
				GCInputButtonName.ButtonA.GetConstant()!,
				GCInputButtonName.ButtonB.GetConstant()!,
				GCInputButtonName.ButtonX.GetConstant()!,
				GCInputButtonName.ButtonY.GetConstant()!,
				GCInputButtonName.LeftShoulder.GetConstant()!,
				GCInputButtonName.RightShoulder.GetConstant()!)
		};

		virtualController = new GCVirtualController(configuration);
		virtualController.Connect(error =>
		{
			if (error != null)
				Console.WriteLine($"TinyLink iOS virtual controller failed: {error.LocalizedDescription}");
			else
				Console.WriteLine("TinyLink iOS virtual controller connected.");
		});
	}

	private static void SetVirtualControllerVisible(bool visible)
	{
		if (visible)
			ConnectVirtualController();
		else
			DisconnectVirtualController();
	}

	private static void DisconnectVirtualController()
	{
		virtualController?.Disconnect();
		virtualController?.Dispose();
		virtualController = null;
	}

	private static void LogControllerConnections(Manager manager)
	{
		manager.Input.OnControllerConnected += id =>
		{
			var controller = manager.Input.GetController(id);
			Console.WriteLine(
				$"TinyLink controller connected: id={id.Value} name={controller?.Name ?? "unknown"} gamepad={controller?.IsGamepad ?? false}");
		};
		manager.Input.OnControllerDisconnected += id =>
			Console.WriteLine($"TinyLink controller disconnected: id={id.Value}");
	}

	private static nint ResolveNativeLibrary(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
	{
		if (libraryName == "SDL3")
		{
			var frameworkPath = Path.Combine(AppContext.BaseDirectory, "Frameworks", "SDL3.framework", "SDL3");
			if (NativeLibrary.TryLoad(frameworkPath, out var handle))
				return handle;
		}

		if (libraryName is "cimgui" or "libcimgui")
		{
			var frameworkPath = Path.Combine(AppContext.BaseDirectory, "Frameworks", "cimgui.framework", "cimgui");
			if (NativeLibrary.TryLoad(frameworkPath, out var handle))
				return handle;
		}

		return nint.Zero;
	}
}
