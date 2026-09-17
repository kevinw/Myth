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

		const string contentFolder = "Particles";
		if (Assets.Content.DirectoryExists(contentFolder))
		{
			foreach (var key in Assets.Content.EnumerateDirectory(contentFolder, "*.json", SearchOption.AllDirectories))
			{
				try
				{
					if (JsonUtil.Parse(Assets.Content.ReadAllText(key))?.AsObject() is not { } json)
						continue;
					var name = Path.GetFileNameWithoutExtension(key);
					effects[name] = ParticleEffect.FromJson(json);
					// The tuning window writes effects back to their source file; zip content
					// has none, so saves there fall back to a fresh Assets/Particles path.
					if (!Assets.ContentIsPak)
						paths[name] = Path.Join(Assets.AssetsPath, key);
				}
				catch (Exception e)
				{
					Console.Error.WriteLine($"Failed to load particle effect '{key}': {e}");
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

	public static bool IsValidName(string name)
	{
		if (string.IsNullOrWhiteSpace(name))
			return false;
		foreach (var c in name)
			if (!char.IsAsciiLetterOrDigit(c) && c != '_' && c != '-')
				return false;
		return true;
	}

	public static bool TryDuplicate(string name, out string copyName, out string? error)
	{
		if (!effects.TryGetValue(name, out var effect))
		{
			copyName = "";
			error = $"Particle effect '{name}' no longer exists.";
			return false;
		}
		copyName = $"{name}_copy";
		for (var suffix = 2; Exists(copyName) || File.Exists(Path.Join(FolderPath, $"{copyName}.json")); suffix++)
			copyName = $"{name}_copy{suffix}";

		try
		{
			Set(copyName, ParticleEffect.FromJson(effect.ToJson()));
			Save(copyName);
			error = null;
			return true;
		}
		catch (Exception exception)
		{
			effects.Remove(copyName);
			paths.Remove(copyName);
			error = exception.Message;
			return false;
		}
	}

	public static bool TryRename(string name, string newName, out string? error)
	{
		if (!effects.TryGetValue(name, out var effect))
		{
			error = $"Particle effect '{name}' no longer exists.";
			return false;
		}
		if (!IsValidName(newName))
		{
			error = "Use only letters, numbers, underscores, and hyphens.";
			return false;
		}
		if (string.Equals(name, newName, StringComparison.OrdinalIgnoreCase) || Exists(newName))
		{
			error = $"Particle effect '{newName}' already exists.";
			return false;
		}

		var oldPath = paths.GetValueOrDefault(name) ?? Path.Join(FolderPath, $"{name}.json");
		var newPath = Path.Join(FolderPath, $"{newName}.json");
		if (File.Exists(newPath))
		{
			error = $"File '{newName}.json' already exists.";
			return false;
		}

		try
		{
			Save(name);
			File.Move(oldPath, newPath);
		}
		catch (Exception exception)
		{
			error = exception.Message;
			return false;
		}

		effects.Remove(name);
		paths.Remove(name);
		effects[newName] = effect;
		paths[newName] = newPath;
		error = null;
		return true;
	}
}
