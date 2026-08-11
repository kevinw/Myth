#if EDITOR
using System.Text.Json.Nodes;

namespace Myth;

static class ActorZedLauncher
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

		ZedLauncher.OpenFile(filename);
	}

	static string? SourceTypeName(string? actorTypeName)
		=> actorTypeName is { Length: > 0 }
			? ActorSpecs.GetActorType(actorTypeName)?.Name ?? actorTypeName
			: null;
}
#endif
