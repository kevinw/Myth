using Foster.Framework;

namespace Kit;

public sealed class SpriteCatalog : IDisposable
{
	const float ReloadDelay = 0.25f;

	readonly GraphicsDevice graphicsDevice;
	readonly string spritesPath;
	readonly Lock reloadLock = new();
	FileSystemWatcher? watcher;
	bool reloadRequested;
	float reloadTimer;

	public Texture? Atlas { get; private set; }
	public readonly Dictionary<string, Sprite> Sprites = new(StringComparer.OrdinalIgnoreCase);

	public SpriteCatalog(GraphicsDevice graphicsDevice, string spritesPath)
	{
		this.graphicsDevice = graphicsDevice;
		this.spritesPath = spritesPath;
	}

	public Sprite? Get(string name, bool logMissing = true)
	{
		var sprite = Sprites.GetValueOrDefault(name);
		if (logMissing && sprite == null)
			Console.WriteLine($"Error: Sprite '{name}' not found");
		return sprite;
	}

	public void Load()
	{
		var spriteFiles = LoadSpriteFiles();
		var packer = new Packer();
		foreach (var (name, ase) in spriteFiles)
		{
			var socketLayers = GetSocketLayers(ase);
			var frames = ase.RenderAllFrames(layer => !socketLayers.Contains(layer) && !IsDebugLayer(layer));
			try
			{
				for (var i = 0; i < frames.Length; i++)
				{
					packer.Add($"{name}/{i}", frames[i]);
					packer.Add($"{name}/normal/{i}", frames[i]);
				}
			}
			finally
			{
				foreach (var frame in frames)
					frame.Dispose();
			}
		}

		var output = packer.Pack();
		var packedSprites = new List<PackedSprite>(spriteFiles.Count);
		if (output.Pages.Count > 1)
			throw new InvalidOperationException($"SpriteCatalog expects one atlas page, got {output.Pages.Count}.");

		var subtextures = new Dictionary<string, PackedSubtexture>(StringComparer.OrdinalIgnoreCase);
		foreach (var entry in output.Entries)
			subtextures[entry.Name] = PackedSubtexture.From(entry.Source, entry.Frame);

		foreach (var (name, ase) in spriteFiles)
			packedSprites.Add(AsepriteSpriteBaker.BuildPackedSprite(
				name,
				ase,
				key => subtextures[key],
				key => subtextures.GetValueOrDefault(key) ?? new(),
				new Dictionary<string, float[]>(),
				new Dictionary<string, int[]>(),
				new Dictionary<string, List<GeneratedSpriteTransition>>(),
				static (_, _) => []));

		var oldAtlas = Atlas;
		Atlas = output.Pages.Count > 0 ? new Texture(graphicsDevice, output.Pages[0], name: "SpriteCatalogAtlas") : null;
		if (Atlas != null)
			PackedSpriteLoader.Load(Atlas, packedSprites, Sprites);
		else
			Sprites.Clear();
		oldAtlas?.Dispose();
	}

	public void Watch()
	{
		if (!Directory.Exists(spritesPath))
			return;

		watcher = new FileSystemWatcher(Path.GetFullPath(spritesPath))
		{
			IncludeSubdirectories = true,
			NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.CreationTime
		};
		watcher.Filters.Add("*.ase");
		watcher.Filters.Add("*.aseprite");
		watcher.Changed += (_, _) => RequestReload();
		watcher.Created += (_, _) => RequestReload();
		watcher.Deleted += (_, _) => RequestReload();
		watcher.Renamed += (_, _) => RequestReload();
		watcher.EnableRaisingEvents = true;
	}

	public void Update(float dt)
	{
		var reload = false;
		lock (reloadLock)
		{
			if (reloadRequested)
			{
				reloadTimer -= dt;
				if (reloadTimer <= 0)
				{
					reloadRequested = false;
					reload = true;
				}
			}
		}

		if (!reload)
			return;

		try
		{
			Load();
			Console.WriteLine($"Reloaded sprites: {spritesPath}");
		}
		catch (Exception e)
		{
			Console.Error.WriteLine($"Failed to reload sprites: {e.Message}");
			RequestReload();
		}
	}

	void RequestReload()
	{
		lock (reloadLock)
		{
			reloadRequested = true;
			reloadTimer = ReloadDelay;
		}
	}

	Dictionary<string, Aseprite> LoadSpriteFiles()
	{
		var result = new Dictionary<string, Aseprite>(StringComparer.OrdinalIgnoreCase);
		foreach (var file in EnumerateAsepriteFiles(spritesPath))
		{
			var name = Path.ChangeExtension(Path.GetRelativePath(spritesPath, file), null)!.Replace(Path.DirectorySeparatorChar, '/');
			var ase = new Aseprite(file);
			if (ase.Frames.Length > 0)
				result.Add(name, ase);
		}
		return result;
	}

	static IEnumerable<string> EnumerateAsepriteFiles(string path)
	{
		if (!Directory.Exists(path))
			yield break;

		foreach (var file in Directory.EnumerateFiles(path, "*.ase", SearchOption.AllDirectories))
			yield return file;
		foreach (var file in Directory.EnumerateFiles(path, "*.aseprite", SearchOption.AllDirectories))
			yield return file;
	}

	static bool IsDebugLayer(Aseprite.Layer layer)
		=> layer.Name?.Contains("debug", StringComparison.OrdinalIgnoreCase) == true;

	static HashSet<Aseprite.Layer> GetSocketLayers(Aseprite ase)
	{
		var result = new HashSet<Aseprite.Layer>();
		foreach (var layer in ase.Layers)
			if ((layer.Name ?? "").Trim().Replace(" ", "", StringComparison.Ordinal).Replace("-", "", StringComparison.Ordinal).EndsWith("_socket", StringComparison.OrdinalIgnoreCase))
				result.Add(layer);
		return result;
	}

	public void Dispose()
	{
		watcher?.Dispose();
		Atlas?.Dispose();
	}
}
