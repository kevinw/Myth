using Foster.Framework;
using SDL3;
using System.Reflection;
using System.Runtime.InteropServices;

namespace FosterIosSmoke;

internal static class Program
{
	public static void Main(string[] args)
	{
		NativeLibrary.SetDllImportResolver(typeof(SDL).Assembly, ResolveNativeLibrary);
		using var app = new SmokeApp();
		app.Run();
	}

	private static nint ResolveNativeLibrary(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
	{
		if (libraryName == "SDL3")
		{
			var frameworkPath = Path.Combine(AppContext.BaseDirectory, "Frameworks", "SDL3.framework", "SDL3");
			if (NativeLibrary.TryLoad(frameworkPath, out var handle))
				return handle;
		}

		return nint.Zero;
	}
}

internal sealed class SmokeApp : App
{
	private readonly Batcher batcher;
	private float hue;

	public SmokeApp()
		: base(new AppConfig(
			ApplicationName: "TinyLinkSmoke",
			WindowTitle: "TinyLink Smoke",
			Width: 320,
			Height: 180,
			Fullscreen: true,
			Resizable: false,
			PreferredGraphicsDriver: GraphicsDriver.Metal))
	{
		batcher = new(GraphicsDevice);
	}

	protected override void Startup()
	{
	}

	protected override void Update()
	{
		hue = (hue + Time.Delta * 0.18f) % 1.0f;
	}

	protected override void Render()
	{
		Window.Clear(Color.Black);
		var pulse = 0.45f + MathF.Sin(hue * MathF.Tau) * 0.20f;
		batcher.Rect(new Rect(0, 0, Window.WidthInPixels, Window.HeightInPixels), new Color(0.08f, pulse, 0.42f, 1.0f));
		batcher.Rect(new Rect(24, 24, Window.WidthInPixels - 48, Window.HeightInPixels - 48), new Color(0.95f, 0.86f, 0.42f, 1.0f));
		batcher.Render(Window);
		batcher.Clear();
	}

	protected override void Shutdown()
	{
		batcher.Dispose();
	}
}
