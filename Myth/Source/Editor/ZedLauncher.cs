#if EDITOR
using System.Diagnostics;
using System.Text.Json.Nodes;

namespace Myth;

static class ZedLauncher
{
	public static void OpenActor(Factory.ActorPaletteEntry entry)
		=> OpenActorType(entry.ActorTypeName);

	public static void OpenActor(JsonObject actor)
		=> OpenActorType(ActorSpecs.GetTypeName(actor));

	public static string? ActorSourceFilename(JsonObject actor)
		=> SourceTypeName(ActorSpecs.GetTypeName(actor)) is { } name ? $"{name}.cs" : null;

	static void OpenActorType(string? actorTypeName)
	{
		if (SourceTypeName(actorTypeName) is not { } clrTypeName)
			return;

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

	static string? SourceTypeName(string? actorTypeName)
		=> actorTypeName is { Length: > 0 }
			? ActorSpecs.GetActorType(actorTypeName)?.Name ?? actorTypeName
			: null;

	static ProcessStartInfo CreateStartInfo(string executable, params string[] arguments)
	{
		var startInfo = new ProcessStartInfo(executable) { UseShellExecute = false };
		foreach (var argument in arguments)
			startInfo.ArgumentList.Add(argument);
		return startInfo;
	}
}
#endif
