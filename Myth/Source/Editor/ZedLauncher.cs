#if EDITOR
using System.Diagnostics;

namespace Myth;

static class ZedLauncher
{
	public static void OpenActor(Factory.ActorPaletteEntry entry)
	{
		if (entry.ActorTypeName is not { Length: > 0 } actorTypeName)
			return;

		var clrTypeName = ActorSpecs.GetActorType(actorTypeName)?.Name ?? actorTypeName;
		var sourceRoot = Path.GetFullPath(Path.Join(Assets.AssetsPath, "..", "Source"));
		if (!Directory.Exists(sourceRoot))
		{
			Console.Error.WriteLine($"Could not find actor source directory at '{sourceRoot}'.");
			return;
		}

		string? filename = null;
		foreach (var candidate in Directory.EnumerateFiles(sourceRoot, $"{clrTypeName}.cs", SearchOption.AllDirectories))
		{
			filename = candidate;
			break;
		}

		if (filename == null)
		{
			Console.Error.WriteLine($"Could not find the C# source for actor type '{clrTypeName}'.");
			return;
		}

		try
		{
			Process.Start(CreateStartInfo("zed", "-a", filename));
		}
		catch (Exception e) when (OperatingSystem.IsMacOS())
		{
			try
			{
				Process.Start(CreateStartInfo("/usr/bin/open", "-a", "Zed", filename));
			}
			catch (Exception fallback)
			{
				Console.Error.WriteLine($"Failed to open '{filename}' in Zed: {e.Message}; fallback failed: {fallback.Message}");
			}
		}
		catch (Exception e)
		{
			Console.Error.WriteLine($"Failed to open '{filename}' in Zed: {e.Message}");
		}
	}

	static ProcessStartInfo CreateStartInfo(string executable, params string[] arguments)
	{
		var startInfo = new ProcessStartInfo(executable) { UseShellExecute = false };
		foreach (var argument in arguments)
			startInfo.ArgumentList.Add(argument);
		return startInfo;
	}
}
#endif
