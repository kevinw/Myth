#if EDITOR
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Myth;

static class ActorZedLauncher
{
	public static void OpenActor(Factory.ActorPaletteEntry entry)
		=> OpenActorType(entry.ActorTypeName);

	public static void OpenActor(JsonObject actor)
		=> OpenActorType(ActorSpecs.GetTypeName(actor));

	public static string? ActorSourceFilename(JsonObject actor)
		=> SourceTypeName(ActorSpecs.GetTypeName(actor)) is { } name ? $"{name}.cs" : null;

	public static void OpenActorProperty(Type declaringType, string memberName)
	{
		if (FindSourceFile(declaringType.Name) is not { } filename)
			return;

		var declaration = new Regex($@"\b{Regex.Escape(memberName)}\b\s*(?:=|;|\{{|=>)");
		var lineNumber = 0;
		foreach (var line in File.ReadLines(filename))
		{
			lineNumber++;
			if (line.Contains("public", StringComparison.Ordinal) && declaration.IsMatch(line))
			{
				ZedLauncher.OpenFile(filename, lineNumber);
				return;
			}
		}

		ZedLauncher.OpenFile(filename);
	}

	static void OpenActorType(string? actorTypeName)
	{
		if (SourceTypeName(actorTypeName) is not { } clrTypeName)
			return;

		if (FindSourceFile(clrTypeName) is { } filename)
			ZedLauncher.OpenFile(filename);
	}

	static string? FindSourceFile(string clrTypeName)
	{
		var sourceRoot = Path.GetFullPath(Path.Join(Assets.AssetsPath, "..", "Source"));
		if (!Directory.Exists(sourceRoot))
		{
			Console.Error.WriteLine($"Could not find actor source directory at '{sourceRoot}'.");
			return null;
		}

		foreach (var candidate in Directory.EnumerateFiles(sourceRoot, $"{clrTypeName}.cs", SearchOption.AllDirectories))
			return candidate;

		Console.Error.WriteLine($"Could not find the C# source for actor type '{clrTypeName}'.");
		return null;
	}

	static string? SourceTypeName(string? actorTypeName)
		=> actorTypeName is { Length: > 0 }
			? ActorSpecs.GetActorType(actorTypeName)?.Name ?? actorTypeName
			: null;
}
#endif
