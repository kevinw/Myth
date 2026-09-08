namespace Myth;

public interface IContentKind
{
	static abstract string ContentKey(string name);
	static abstract void Collect(ContentDependencyCollector collector, string name, string origin);
}

interface IContentReference
{
	string Name { get; }
	string ContentKey { get; }
	IContentReference WithName(string name);
	void Collect(ContentDependencyCollector collector, string origin);
}

public readonly record struct ContentRef<T>(string Name) : IContentReference where T : IContentKind
{
	public string ContentKey => T.ContentKey(Name);

	IContentReference IContentReference.WithName(string name) => new ContentRef<T>(name);
	void IContentReference.Collect(ContentDependencyCollector collector, string origin) => T.Collect(collector, Name, origin);

	public void Collect(ContentDependencyCollector collector, string origin) => T.Collect(collector, Name, origin);

	public override string ToString() => Name;

	public static implicit operator ContentRef<T>(string name) => new(name);
}

public readonly struct NeuralWeightsContent : IContentKind
{
	public static string Folder => NeuralTextureActor.WeightsFolder;
	public static string ContentKey(string name) => $"{Folder}/{name}";
	public static void Collect(ContentDependencyCollector collector, string name, string origin)
		=> collector.IncludeFile(ContentKey(name), origin);
	public static ContentRef<NeuralWeightsContent> NeuralTextureDefault => new("chequered_0050_circular.bin");
	public static ContentRef<NeuralWeightsContent> RigDefault => new("rock035_pair_circular_screen.bin");
	public static ContentRef<NeuralWeightsContent> AlienBugDefault => new("bubbles_circular.bin");
	public static ContentRef<NeuralWeightsContent> MagneticCrawlerDefault => new("pseudocamo_tight_converge.bin");
}

public readonly struct NcaWeightsContent : IContentKind
{
	public static string Folder => "NCA";
	public static string ContentKey(string name) => $"{Folder}/{name}";
	public static void Collect(ContentDependencyCollector collector, string name, string origin)
		=> collector.IncludeFile(ContentKey(name), origin);
	public static ContentRef<NcaWeightsContent> NeuralCellularDefault => new("lizard_weights.bin");
	public static ContentRef<NcaWeightsContent> LayerDefault => new("fungus_weights.bin");
}

public readonly struct SurfaceContent : IContentKind
{
	public static string ContentKey(string name) => $"Packed/Visuals/surfaces/{name}.json";

	public static void Collect(ContentDependencyCollector collector, string name, string origin)
	{
		var key = ContentKey(name);
		if (!collector.IncludeFile(key, origin))
			return;
		var path = Path.Join(Assets.AssetsPath, key.Replace('/', Path.DirectorySeparatorChar));
		if (!File.Exists(path))
			return;
		SurfaceSpec spec;
		try { spec = SurfaceSpec.Parse(File.ReadAllText(path), name); }
		catch (Exception exception)
		{
			throw new InvalidDataException($"Could not parse surface '{name}' referenced by {origin}.", exception);
		}
		foreach (var texture in spec.ReferencedTextures())
			collector.Add(new ContentRef<VisualTextureContent>(texture), $"{origin} -> surface '{name}' texture '{texture}'");
	}

	public static ContentRef<SurfaceContent> BouncerDefault => new("bouncer");
}

public readonly struct VisualTextureContent : IContentKind
{
	public static string ContentKey(string name) => $"Packed/Visuals/textures/{name}.png";
	public static void Collect(ContentDependencyCollector collector, string name, string origin)
		=> collector.IncludeFile(ContentKey(name), origin);
}

public sealed class ContentDependencyCollector
{
	readonly Dictionary<string, string> files = new(StringComparer.OrdinalIgnoreCase);

	public IEnumerable<string> Files => files.Keys;

	public void Add<T>(ContentRef<T> reference, string origin) where T : IContentKind
	{
		if (!string.IsNullOrWhiteSpace(reference.Name))
			reference.Collect(this, origin);
	}

	public bool IncludeFile(string key, string origin)
	{
		key = key.Replace('\\', '/').TrimStart('/');
		if (key.Length == 0 || key.Split('/').Contains(".."))
			throw new InvalidDataException($"Invalid content path '{key}' from {origin}.");
		return files.TryAdd(key, origin);
	}

	public bool Includes(string key) => files.ContainsKey(key);

	public string Origin(string key) => files.GetValueOrDefault(key) ?? "unknown reference";

	public void Validate(string assetsPath)
	{
		foreach (var (key, origin) in files)
			if (!File.Exists(Path.Join(assetsPath, key.Replace('/', Path.DirectorySeparatorChar))))
				throw new InvalidDataException($"Missing content '{key}' referenced by {origin}.");
	}
}
