using System.Text.Json.Nodes;

namespace Myth;

public readonly record struct RigTake(
	string Name,
	string Clip,
	float Start,
	float End,
	bool Loop = false,
	float Speed = 1,
	bool FootIK = false,
	float UpperBodyLeanDegrees = 0,
	float ArmWidthScale = 1,
	float HandWidthScale = 1);

static class RigTakes
{
	const string AssetPath = "Rigs/ual2.takes.json";
	static readonly Dictionary<string, RigTake> takes = new(StringComparer.OrdinalIgnoreCase);
	static bool loaded;

	public static IReadOnlyCollection<RigTake> All
	{
		get
		{
			EnsureLoaded();
			return takes.Values;
		}
	}

	public static bool TryGet(string name, out RigTake take)
	{
		EnsureLoaded();
		return takes.TryGetValue(name, out take);
	}

	public static RigTake Get(string name)
		=> TryGet(name, out var take) ? take : throw new KeyNotFoundException($"Rig take '{name}' does not exist.");

	static void EnsureLoaded()
	{
		if (loaded)
			return;
		loaded = true;
		takes.Clear();
		if (!Assets.Content.FileExists(AssetPath))
			return;
		try
		{
			if (JsonNode.Parse(Assets.Content.ReadAllText(AssetPath)) is not JsonObject root)
				return;
			foreach (var (name, node) in root)
				if (node is JsonObject value && value["Clip"]?.GetValue<string>() is { Length: > 0 } clip)
					takes[name] = new(
						name,
						clip,
						value["Start"]?.GetValue<float>() ?? 0,
						value["End"]?.GetValue<float>() ?? float.MaxValue,
						value["Loop"]?.GetValue<bool>() ?? false,
						value["Speed"]?.GetValue<float>() ?? 1,
						value["FootIK"]?.GetValue<bool>() ?? false,
						value["UpperBodyLeanDegrees"]?.GetValue<float>() ?? 0,
						value["ArmWidthScale"]?.GetValue<float>() ?? 1,
						value["HandWidthScale"]?.GetValue<float>() ?? 1);
		}
		catch (Exception e)
		{
			Console.Error.WriteLine($"Failed to load {AssetPath}: {e.Message}");
		}
	}

#if EDITOR
	public static void Save(RigTake take)
	{
		if (string.IsNullOrWhiteSpace(take.Name) || string.IsNullOrWhiteSpace(take.Clip))
			throw new ArgumentException("Rig take name and clip are required.");
		EnsureLoaded();
		take = take with { Name = take.Name.Trim(), Speed = Math.Max(0.01f, take.Speed) };
		takes[take.Name] = take;

		var root = new JsonObject();
		var names = new List<string>(takes.Keys);
		names.Sort(StringComparer.OrdinalIgnoreCase);
		foreach (var name in names)
		{
			var item = takes[name];
			root[name] = new JsonObject
			{
				["Clip"] = item.Clip,
				["Start"] = item.Start,
				["End"] = item.End,
				["Loop"] = item.Loop,
				["Speed"] = item.Speed,
				["FootIK"] = item.FootIK,
				["UpperBodyLeanDegrees"] = item.UpperBodyLeanDegrees,
				["ArmWidthScale"] = item.ArmWidthScale,
				["HandWidthScale"] = item.HandWidthScale,
			};
		}
		var path = Path.Join(Assets.AssetsPath, AssetPath);
		Directory.CreateDirectory(Path.GetDirectoryName(path)!);
		File.WriteAllText(path, root.ToJsonString(new() { WriteIndented = true }) + "\n");
	}
#endif
}
