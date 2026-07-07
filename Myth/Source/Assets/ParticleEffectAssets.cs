using System.Text.Json;
using System.Text.Json.Nodes;

namespace Myth;

/// <summary>
/// Registry of named <see cref="ParticleEffect"/> assets loaded from
/// Assets/Particles/*.json. Effects are referenced by name (the file name) from
/// <c>ParticleEmitter</c> actors and the tuning window.
/// </summary>
public static class ParticleEffectAssets
{
	private static readonly Dictionary<string, ParticleEffect> effects = new(StringComparer.OrdinalIgnoreCase);
	private static readonly Dictionary<string, string> paths = new(StringComparer.OrdinalIgnoreCase);

	public static string FolderPath => Path.Join(Assets.AssetsPath, "Particles");

	/// <summary>Effect names, sorted, for selectors.</summary>
	public static IReadOnlyList<string> Names => effects.Keys.OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();

	public static void Load()
	{
		effects.Clear();
		paths.Clear();

		var folder = FolderPath;
		if (Directory.Exists(folder))
		{
			foreach (var file in Directory.EnumerateFiles(folder, "*.json", SearchOption.AllDirectories))
			{
				try
				{
					if (JsonNode.Parse(File.ReadAllText(file))?.AsObject() is not { } json)
						continue;
					var name = Path.GetFileNameWithoutExtension(file);
					effects[name] = ParticleEffect.FromJson(json);
					paths[name] = file;
				}
				catch (Exception e)
				{
					Console.Error.WriteLine($"Failed to load particle effect '{file}': {e}");
				}
			}
		}

		// Always have at least one effect so selectors and emitters resolve.
		if (effects.Count == 0)
			effects["default"] = new ParticleEffect();
	}

	/// <summary>Returns the named effect, or the first available as a fallback.</summary>
	public static ParticleEffect Get(string? name)
	{
		if (name != null && effects.TryGetValue(name, out var effect))
			return effect;
		return effects.Count > 0 ? effects.Values.First() : new ParticleEffect();
	}

	public static bool Exists(string? name) => name != null && effects.ContainsKey(name);

	public static void Set(string name, ParticleEffect effect)
	{
		effects[name] = effect;
		paths.TryAdd(name, Path.Join(FolderPath, $"{name}.json"));
	}

	public static void Save(string name)
	{
		if (!effects.TryGetValue(name, out var effect))
			return;

		Directory.CreateDirectory(FolderPath);
		var path = paths.GetValueOrDefault(name) ?? Path.Join(FolderPath, $"{name}.json");
		paths[name] = path;
		File.WriteAllText(path, effect.ToJson().ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
	}
}
