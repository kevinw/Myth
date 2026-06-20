
using System.Text;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Foster.Framework;

namespace Myth;

public static class Assets
{
	public const int MaxSlopeRun = 10;

	public static SpriteFont? Font { get; private set; }
	public static SpriteFont? SpeechFont { get; private set; }
	public static Texture? Atlas { get; private set; }
	public static Texture? LightingNoise { get; private set; }
	public static readonly Dictionary<string, Sprite> Sprites = new();
	public static readonly Dictionary<string, Tileset> Tilesets = new();
	public static readonly Dictionary<string, Subtexture> Subtextures = new();
	public static readonly Dictionary<Point2, Room> Rooms = new();
	public static readonly Dictionary<string, Room> RoomsById = new();

	internal static string GenerateRandomRoomId()
	{
		while (true)
		{
			var dictWord1 = WordDictionary.GetRandomWord();
			var dictWord2 = WordDictionary.GetRandomWord();
			if (dictWord1 == null || dictWord2 == null)
			{
				break;
			}

			var candidate = $"{dictWord1}_{dictWord2}".Trim();
			if (!RoomsById.ContainsKey(candidate))
				return candidate;
		}

		return Guid.NewGuid().ToString("N");
	}


	// Within-animation in-betweens whitelist: (sprite, tag) -> frame count.
	// A tag of "*" applies to every tag of that sprite. Loaded from
	// inbetweens.json; empty means no within-animation in-betweens are baked.
	private static readonly Dictionary<(string Sprite, string Tag), int> InbetweenAnimations = new();

	// Transition in-betweens whitelist: (sprite, from, to) -> frame count.
	// This is the source of truth for baking transitions.
	private static readonly Dictionary<(string Sprite, string From, string To), int> InbetweenTransitions = new();

	public static string INBETWEENS_JSON_FILENAME => Path.Join(AssetsPath, "inbetweens.json");

	// Populated during the visual bake and serialized into sprite animations.
	public record struct GeneratedTransition(string FromTag, string ToTag, int FrameCount, float[] Durations);
	public static readonly Dictionary<string, List<GeneratedTransition>> GeneratedTransitions = new();

	// Expanded frame durations for sprites with inbetweens
	private static readonly Dictionary<string, float[]> ExpandedFrameDurations = new();

	// Maps original frame index -> index in the expanded (in-betweened) timeline,
	// for sprites that had within-animation in-betweens baked. Replaces the old
	// uniform "tag.From * (count + 1)" math now that counts vary per tag.
	private static readonly Dictionary<string, int[]> ExpandedFrameMap = new();

	private const string assetsFolderName = "Assets";
	private const string roomsFolderName = "Rooms";
	private const string packedVisualsFolderName = "Packed/Visuals";
	private const string packedVisualsManifestName = "manifest.bin";
	private const uint visualAssetPackMagic = 0x5356494D; // MIVS, little-endian.
	private const int visualAssetPackVersion = 2;
	private const string defaultWorldId = "Myth";
	private const string defaultStartRoomId = "start";
	private const string deletedRoomsFolderName = ".deleted";
	private static string? path = null;
	private static string? userRoomsPath = null;
	private static string? visualAssetPackPath = null;
	private static bool useVisualAssetPack = false;
	private static bool? bundledRoomsWritable = null;
	private static string currentWorldId = defaultWorldId;

	public static string AssetsPath
	{
		get
		{
			// during development we search up from the build directory to find the Assets folder
			// (instead of copying all the Assets to the build directory).
			if (path == null)
			{
				var up = "";
				while (!Directory.Exists(Path.Join(up, assetsFolderName)) && up.Length < 10)
					up = Path.Join(up, "..");
				path = Path.Join(up, assetsFolderName);
			}

			return path ?? throw new Exception("Unable to find Assets path");
		}
	}

	public static string RoomsPath => Path.Join(AssetsPath, roomsFolderName);
	public static string DefaultVisualAssetPackPath => Path.Join(AssetsPath, packedVisualsFolderName);
	public static string CurrentWorldId => currentWorldId;
	public static string WorldRoomsPath => Path.Join(RoomsPath, currentWorldId);
	public static string? UserRoomsPath => userRoomsPath;
	public static string? UserWorldRoomsPath => userRoomsPath != null ? Path.Join(userRoomsPath, currentWorldId) : null;

	public static void ConfigureUserRoomsPath(string path)
	{
		userRoomsPath = path;
		bundledRoomsWritable = null;
	}

	public static void ConfigureVisualAssetPack(string? path, bool enabled)
	{
		visualAssetPackPath = string.IsNullOrWhiteSpace(path) ? null : path;
		useVisualAssetPack = enabled;
	}

	public static IReadOnlyList<string> GetWorldIds()
	{
		if (!Directory.Exists(RoomsPath))
			return Array.Empty<string>();

		return Directory.EnumerateDirectories(RoomsPath)
			.Select(Path.GetFileName)
			.Where(it => !string.IsNullOrWhiteSpace(it))
			.Select(it => it!)
			.Order(StringComparer.OrdinalIgnoreCase)
			.ToArray();
	}

	public static void SelectWorld(string worldId)
	{
		worldId = worldId.Trim();
		if (string.IsNullOrEmpty(worldId))
			worldId = defaultWorldId;

		currentWorldId = worldId;
		bundledRoomsWritable = null;
	}

	public static void Load(GraphicsDevice gfx, bool dumpPackedAtlases = false)
		=> Reload(gfx, true, dumpPackedAtlases, buildPackPath: null);

	public static void Reload(GraphicsDevice gfx)
		=> Reload(gfx, false, dumpPackedAtlases: false, buildPackPath: null);

	public static void BuildVisualAssetPack(string? path = null, bool dumpPackedAtlases = false)
	{
		using var bake = BakeVisualAssets();
#if !IOS
		if (dumpPackedAtlases)
			DumpPackedAtlasPages(bake.Output);
#endif
		WriteVisualAssetPack(path ?? DefaultVisualAssetPackPath, bake);
	}

	public static void BuildVisualAssetPack(GraphicsDevice gfx, string? path = null, bool dumpPackedAtlases = false)
		=> BuildVisualAssetPack(path, dumpPackedAtlases);

	public static void ReloadRooms()
		=> LoadRooms();

	private static void Reload(GraphicsDevice gfx, bool reloadRooms, bool dumpPackedAtlases, string? buildPackPath)
	{
		if (buildPackPath == null && (useVisualAssetPack || OperatingSystem.IsBrowser()) && TryLoadVisualAssetPack(gfx, visualAssetPackPath ?? DefaultVisualAssetPackPath, reloadRooms))
			return;

		var actorsPath = Path.Join(AssetsPath, "Actors");
		using var bake = BakeVisualAssets();
		var output = bake.Output;

		// load main font file
#if !BROWSER
		EnsureFonts(gfx);
#endif

#if !IOS
		if (dumpPackedAtlases)
			DumpPackedAtlasPages(output);
#endif

		// create texture file
		var atlas = new Texture(gfx, output.Pages[0], name: "Atlas");
		var lightingNoise = Assets.LoadAsepriteTexture(gfx, Path.Join("Textures", "noise_tess2_seamless.ase"), "LightingNoise");
		LoadVisualAssetPackManifest(atlas, lightingNoise, BuildVisualAssetPackManifest(bake, "", null));

		if (buildPackPath != null)
			WriteVisualAssetPack(buildPackPath, bake);

		ActorSpecs.LoadSpecs(actorsPath);
		ParticleEffectAssets.Load();
		if (reloadRooms)
			LoadRooms();
	}

	private static VisualAssetBake BakeVisualAssets()
	{
		var spritesPath = Path.Join(AssetsPath, "Sprites");
		var tilesetsPath = Path.Join(AssetsPath, "Tilesets");
		var spriteFiles = new Dictionary<string, Aseprite>();
		var generatedSprites = new List<(string Name, Aseprite Source, Func<Predicate<Aseprite.Layer>> LayerFilter)>();
		var tilesetFiles = new Dictionary<string, Aseprite>();
		var generatedTileImages = new Dictionary<string, List<Image>>();
		var generatedNormalImages = new Dictionary<string, List<Image>>();
		var generatedOccluders = new Dictionary<string, List<List<Tileset.OccluderSegment[]>>>();
		var generatedSlopeEdgeImages = new Dictionary<string, List<Image>>();
		var kitGeneratedTilesets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

		GeneratedTransitions.Clear();
		ExpandedFrameDurations.Clear();
		ExpandedFrameMap.Clear();
		LoadInbetweenConfig();

		foreach (var file in EnumerateAsepriteFiles(spritesPath))
		{
			var name = Path.ChangeExtension(Path.GetRelativePath(spritesPath, file), null);
			var ase = new Aseprite(file);
			if (ase.Frames.Length > 0)
				spriteFiles.Add(name, ase);
		}

		if (!spriteFiles.ContainsKey("bud") && spriteFiles.TryGetValue("player", out var playerSprite))
			generatedSprites.Add(("bud", playerSprite, CreateBudPlayerLayerFilter));

		foreach (var file in EnumerateAsepriteFiles(tilesetsPath))
		{
			var name = Path.ChangeExtension(Path.GetRelativePath(tilesetsPath, file), null);
			var ase = new Aseprite(file);
			if (ase.Frames.Length > 0)
				tilesetFiles.Add(name, ase);
		}

		foreach (var (name, ase) in tilesetFiles.ToArray())
		{
			if (!name.EndsWith("-kit", StringComparison.OrdinalIgnoreCase))
				continue;

			GenerateDualTilesetFromKit(name, ase, out var baseName, out var baseTiles, out var baseSlopeEdges, out var dualName, out var dualVariants, out var dualNormals, out var dualOccluders);
			generatedTileImages[baseName] = baseTiles;
			generatedTileImages[dualName] = dualVariants;
			generatedNormalImages[dualName] = dualNormals;
			generatedOccluders[dualName] = dualOccluders;
			if (baseSlopeEdges.Count > 0)
				generatedSlopeEdgeImages[baseName] = baseSlopeEdges;
			kitGeneratedTilesets.Add(baseName);
			kitGeneratedTilesets.Add(dualName);
		}

		var packer = new Packer();

		foreach (var (name, ase) in spriteFiles)
		{
			var frames = ase.RenderAllFrames();
			PackSpriteFrames(name, ase, frames, packer);
		}

		foreach (var (name, ase, layerFilter) in generatedSprites)
		{
			var frames = ase.RenderAllFrames(layerFilter());
			PackSpriteFrames(name, ase, frames, packer);
		}

		foreach (var (name, ase) in tilesetFiles)
		{
			if (name.EndsWith("-kit", StringComparison.OrdinalIgnoreCase) || kitGeneratedTilesets.Contains(name))
				continue;

			var nonDebugLayers = GetNonDebugLayers(ase);
			var variantLayers = GetVariantLayers(ase, nonDebugLayers);
			var image = ase.RenderFrame(0, nonDebugLayers.Contains);
			var columns = image.Width / Game.TileSize;
			var rows = image.Height / Game.TileSize;

			for (int x = 0; x < columns; x++)
				for (int y = 0; y < rows; y++)
					packer.Add($"tilesets/{name}{x}x{y}", image, new RectInt(x, y, 1, 1) * Game.TileSize);

			var variantIndex = 0;
			foreach (var layer in variantLayers)
			{
				var variant = ase.RenderFrame(0, it => it == layer);
				for (int x = 0; x < columns; x++)
					for (int y = 0; y < rows; y++)
						packer.Add($"tilesets/{name}/variants/{variantIndex}/{x}x{y}", variant, new RectInt(x, y, 1, 1) * Game.TileSize);
				using var variantBaseTile = ExtractTile(variant, new RectInt(0, 0, 1, 1) * Game.TileSize);
				AddSlopeSubtextures(packer, name, variantBaseTile, variantIndex);
				variantIndex++;
			}

			using var baseTile = ExtractTile(image, new RectInt(0, 0, 1, 1) * Game.TileSize);
			AddSlopeSubtextures(packer, name, baseTile);
		}

		foreach (var (name, variants) in generatedTileImages)
		{
			for (var variantIndex = 0; variantIndex < variants.Count; variantIndex++)
			{
				var image = variants[variantIndex];
				var columns = image.Width / Game.TileSize;
				var rows = image.Height / Game.TileSize;

				for (int x = 0; x < columns; x++)
					for (int y = 0; y < rows; y++)
					{
						if (variantIndex == 0)
							packer.Add($"tilesets/{name}{x}x{y}", image, new RectInt(x, y, 1, 1) * Game.TileSize);
						packer.Add($"tilesets/{name}/variants/{variantIndex}/{x}x{y}", image, new RectInt(x, y, 1, 1) * Game.TileSize);
					}

				var slopeEdges = generatedSlopeEdgeImages.GetValueOrDefault(name);
				var slopeEdge = slopeEdges != null && variantIndex < slopeEdges.Count ? slopeEdges[variantIndex] : null;
				if (variantIndex == 0 && image.Width == Game.TileSize && image.Height == Game.TileSize)
					AddSlopeSubtextures(packer, name, image, slopeEdge);
				if (image.Width == Game.TileSize && image.Height == Game.TileSize)
					AddSlopeSubtextures(packer, name, image, variantIndex, slopeEdge);
			}
		}

		foreach (var (name, variants) in generatedNormalImages)
		{
			for (var variantIndex = 0; variantIndex < variants.Count; variantIndex++)
			{
				var image = variants[variantIndex];
				var columns = image.Width / Game.TileSize;
				var rows = image.Height / Game.TileSize;

				for (int x = 0; x < columns; x++)
					for (int y = 0; y < rows; y++)
					{
						if (variantIndex == 0)
							packer.Add($"tilesets/{name}/normal/{x}x{y}", image, new RectInt(x, y, 1, 1) * Game.TileSize);
						packer.Add($"tilesets/{name}/normal/variants/{variantIndex}/{x}x{y}", image, new RectInt(x, y, 1, 1) * Game.TileSize);
					}
			}
		}

		var output = packer.Pack();
		return new(output, spriteFiles, generatedSprites, tilesetFiles, generatedTileImages, generatedNormalImages, generatedOccluders, kitGeneratedTilesets);
	}

	private static bool TryLoadVisualAssetPack(GraphicsDevice gfx, string directory, bool reloadRooms)
	{
		var manifestPath = Path.Join(directory, packedVisualsManifestName);
		if (!File.Exists(manifestPath))
			return false;

		using var manifestStream = File.OpenRead(manifestPath);
		var manifest = ReadVisualAssetPackManifest(manifestStream, manifestPath);

		EnsureFonts(gfx);

		using var atlasImage = new Image(Path.Join(directory, manifest.Atlas));
		var atlas = new Texture(gfx, atlasImage, name: "Atlas");
		Texture? lightingNoise = null;
		if (!string.IsNullOrEmpty(manifest.LightingNoise))
		{
			var lightingPath = Path.Join(directory, manifest.LightingNoise);
			if (File.Exists(lightingPath))
			{
				using var lighting = new Image(lightingPath);
				lightingNoise = new Texture(gfx, lighting, name: "LightingNoise");
			}
		}

		LoadVisualAssetPackManifest(atlas, lightingNoise, manifest);

		ActorSpecs.LoadSpecs(Path.Join(AssetsPath, "Actors"));
		ParticleEffectAssets.Load();
		if (reloadRooms)
			LoadRooms();

		Console.WriteLine($"Loaded visual asset pack: {manifestPath}");
		return true;
	}

	private static void WriteVisualAssetPack(string directory, VisualAssetBake bake)
	{
		var output = bake.Output;
		if (output.Pages.Count != 1)
			throw new InvalidOperationException($"Visual asset pack expects one atlas page, got {output.Pages.Count}.");

		Directory.CreateDirectory(directory);
		File.Delete(Path.Join(directory, "manifest.json"));
		File.Delete(Path.Join(directory, "atlas.png"));
		File.Delete(Path.Join(directory, "lighting-noise.png"));
		const string atlasFile = "atlas.qoi";
		const string lightingNoiseFile = "lighting-noise.qoi";
		output.Pages[0].WriteQoi(Path.Join(directory, atlasFile));

		string? lightingNoise = null;
		var noisePath = Path.Join(AssetsPath, "Textures", "noise_tess2_seamless.ase");
		if (File.Exists(noisePath))
		{
			var ase = new Aseprite(noisePath);
			if (ase.Frames.Length > 0)
			{
				using var image = ase.RenderFrame(0);
				image.WriteQoi(Path.Join(directory, lightingNoiseFile));
				lightingNoise = lightingNoiseFile;
			}
		}

		var manifestPath = Path.Join(directory, packedVisualsManifestName);
		WriteVisualAssetPackManifest(manifestPath, BuildVisualAssetPackManifest(bake, atlasFile, lightingNoise));
		Console.WriteLine($"Wrote visual asset pack: {manifestPath}");
	}

	private static VisualAssetPackManifest BuildVisualAssetPackManifest(VisualAssetBake bake, string atlas, string? lightingNoise)
	{
		var manifest = new VisualAssetPackManifest
		{
			Atlas = atlas,
			LightingNoise = lightingNoise
		};

		foreach (var (name, subtexture) in bake.Subtextures)
			manifest.Subtextures.Add(PackedNamedSubtexture.From(name, subtexture));

		foreach (var (name, ase) in bake.SpriteFiles)
			manifest.Sprites.Add(BuildPackedSpriteAsset(bake, name, ase));
		foreach (var (name, ase, _) in bake.GeneratedSprites)
			manifest.Sprites.Add(BuildPackedSpriteAsset(bake, name, ase));

		foreach (var (name, ase) in bake.TilesetFiles)
		{
			if (name.EndsWith("-kit", StringComparison.OrdinalIgnoreCase) || bake.KitGeneratedTilesets.Contains(name))
				continue;
			manifest.Tilesets.Add(BuildPackedTilesetAsset(bake, name, ase));
		}
		foreach (var (name, images) in bake.GeneratedTileImages)
			manifest.Tilesets.Add(BuildPackedGeneratedTilesetAsset(bake, name, images));

		return manifest;
	}

	private static void LoadVisualAssetPackManifest(Texture atlas, Texture? lightingNoise, VisualAssetPackManifest manifest)
	{
		var oldAtlas = Atlas;
		var oldLightingNoise = LightingNoise;
		Atlas = atlas;
		LightingNoise = lightingNoise;

		Subtextures.Clear();
		foreach (var packed in manifest.Subtextures)
			Subtextures[packed.Name] = packed.ToSubtexture(atlas);

		var foundSprites = new HashSet<string>();
		foreach (var packed in manifest.Sprites)
		{
			foundSprites.Add(packed.Name);
			if (!Sprites.TryGetValue(packed.Name, out var sprite))
			{
				sprite = new Sprite(packed.Name, packed.Origin.ToVector2());
				Sprites.Add(packed.Name, sprite);
			}

			var frames = new List<Sprite.Frame>(packed.Frames.Count);
			foreach (var frame in packed.Frames)
				frames.Add(new(
					frame.Subtexture.ToSubtexture(atlas),
					frame.VisibleSubtexture.ToSubtexture(atlas),
					frame.NormalSubtexture.ToSubtexture(atlas),
					frame.Duration));

			var animations = new List<Sprite.Animation>(packed.Animations.Count);
			foreach (var animation in packed.Animations)
				animations.Add(new(animation.Name, animation.FrameStart, animation.FrameCount, animation.Duration));

			sprite.Reload(packed.Origin.ToVector2(), frames, animations);
		}

		foreach (var (name, sprite) in Sprites.ToArray())
		{
			if (foundSprites.Contains(name))
				continue;
			sprite.Reload(Vector2.Zero, [], []);
			Sprites.Remove(name);
		}

		var foundTilesets = new HashSet<string>();
		foreach (var packed in manifest.Tilesets)
		{
			foundTilesets.Add(packed.Name);
			if (!Tilesets.TryGetValue(packed.Name, out var tileset))
			{
				tileset = new Tileset(packed.Name, packed.Columns, packed.Rows);
				Tilesets.Add(packed.Name, tileset);
			}

			tileset.Reload(
				packed.Columns,
				packed.Rows,
				LoadSubtextures(packed.Tiles, atlas),
				LoadSubtextureGroups(packed.Variants, atlas),
				LoadSubtextures(packed.NormalTiles, atlas),
				LoadSubtextureGroups(packed.NormalVariants, atlas),
				LoadOccluders(packed.OccluderTiles),
				LoadOccluderGroups(packed.OccluderVariants));
		}

		foreach (var (name, tileset) in Tilesets.ToArray())
		{
			if (foundTilesets.Contains(name))
				continue;
			tileset.Reload(0, 0, []);
			Tilesets.Remove(name);
		}

		oldAtlas?.Dispose();
		oldLightingNoise?.Dispose();
	}

	private static PackedSprite BuildPackedSpriteAsset(VisualAssetBake bake, string name, Aseprite ase)
	{
		var origin = Vector2.Zero;
		if (ase.Slices.Count > 0 && ase.Slices[0].Keys.Length > 0 && ase.Slices[0].Keys[0].Pivot.HasValue)
			origin = ase.Slices[0].Keys[0].Pivot!.Value;

		var packed = new PackedSprite
		{
			Name = name,
			Origin = PackedVector.From(origin)
		};

		var useExpanded = ExpandedFrameDurations.TryGetValue(name, out var expDurations);
		var frameCount = useExpanded ? expDurations!.Length : ase.Frames.Length;
		for (var i = 0; i < frameCount; i++)
			packed.Frames.Add(new()
			{
				Subtexture = bake.Subtexture($"{name}/{i}"),
				VisibleSubtexture = PackedSubtexture.VisibleFrom(bake.Subtexture($"{name}/{i}")),
				NormalSubtexture = bake.Subtexture($"{name}/normal/{i}"),
				Duration = useExpanded ? expDurations![i] : ase.Frames[i].Duration / 1000.0f
			});

		foreach (var tag in ase.Tags)
		{
			if (string.IsNullOrEmpty(tag.Name))
				continue;

			int animFrom, animCount;
			if (useExpanded && ExpandedFrameMap.TryGetValue(name, out var map))
			{
				animFrom = map[tag.From];
				var animEnd = tag.To + 1 < map.Length ? map[tag.To + 1] : frameCount;
				animCount = animEnd - animFrom;
			}
			else
			{
				animFrom = tag.From;
				animCount = tag.To - tag.From + 1;
			}

			var duration = 0.0f;
			for (var i = animFrom; i < animFrom + animCount && i < packed.Frames.Count; i++)
				duration += packed.Frames[i].Duration;
			packed.Animations.Add(new() { Name = tag.Name, FrameStart = animFrom, FrameCount = animCount, Duration = duration });
		}

		if (GeneratedTransitions.TryGetValue(name, out var transitions))
		{
			var transFrameStart = packed.Frames.Count;
			foreach (var trans in transitions)
			{
				float duration = 0;
				for (var i = 0; i < trans.FrameCount; i++)
				{
					var subtexture = bake.Subtexture($"{name}/transition/{trans.FromTag}->{trans.ToTag}/{i}");
					packed.Frames.Add(new()
					{
						Subtexture = subtexture,
						VisibleSubtexture = PackedSubtexture.VisibleFrom(subtexture),
						NormalSubtexture = bake.Subtexture($"{name}/transition/{trans.FromTag}->{trans.ToTag}/normal/{i}"),
						Duration = trans.Durations[i]
					});
					duration += trans.Durations[i];
				}
				packed.Animations.Add(new() { Name = $"{trans.FromTag}->{trans.ToTag}", FrameStart = transFrameStart, FrameCount = trans.FrameCount, Duration = duration });
				transFrameStart += trans.FrameCount;
			}
		}

		return packed;
	}

	private static PackedTileset BuildPackedTilesetAsset(VisualAssetBake bake, string name, Aseprite ase)
	{
		var columns = ase.Width / Game.TileSize;
		var rows = ase.Height / Game.TileSize;
		var variantLayers = GetVariantLayers(ase, GetNonDebugLayers(ase));
		var packed = new PackedTileset
		{
			Name = name,
			Columns = columns,
			Rows = rows,
			Tiles = PackTileSubtextures(bake, $"tilesets/{name}", columns, rows)
		};

		for (var variantIndex = 0; variantIndex < variantLayers.Count; variantIndex++)
			packed.Variants.Add(PackTileSubtextures(bake, $"tilesets/{name}/variants/{variantIndex}/", columns, rows));
		return packed;
	}

	private static PackedTileset BuildPackedGeneratedTilesetAsset(VisualAssetBake bake, string name, List<Image> images)
	{
		var columns = images[0].Width / Game.TileSize;
		var rows = images[0].Height / Game.TileSize;
		var packed = new PackedTileset
		{
			Name = name,
			Columns = columns,
			Rows = rows,
			Tiles = PackTileSubtextures(bake, $"tilesets/{name}", columns, rows)
		};

		for (var variantIndex = 0; variantIndex < images.Count; variantIndex++)
			packed.Variants.Add(PackTileSubtextures(bake, $"tilesets/{name}/variants/{variantIndex}/", columns, rows));

		if (bake.GeneratedNormalImages.TryGetValue(name, out var normals))
		{
			packed.NormalTiles = PackTileSubtextures(bake, $"tilesets/{name}/normal/", columns, rows);
			for (var variantIndex = 0; variantIndex < normals.Count; variantIndex++)
				packed.NormalVariants.Add(PackTileSubtextures(bake, $"tilesets/{name}/normal/variants/{variantIndex}/", columns, rows));
		}

		if (bake.GeneratedOccluders.TryGetValue(name, out var occluders))
		{
			packed.OccluderTiles = PackOccluders(occluders[0]);
			packed.OccluderVariants = PackOccluderGroups(occluders);
		}

		return packed;
	}

	private static List<PackedSubtexture> PackTileSubtextures(VisualAssetBake bake, string prefix, int columns, int rows)
	{
		var result = new List<PackedSubtexture>(columns * rows);
		for (var x = 0; x < columns; x++)
			for (var y = 0; y < rows; y++)
				result.Add(bake.Subtexture($"{prefix}{x}x{y}"));
		return result;
	}

	private static void WriteVisualAssetPackManifest(string path, VisualAssetPackManifest manifest)
	{
		using var stream = File.Create(path);
		using var writer = new BinaryWriter(stream, Encoding.UTF8);
		writer.Write(visualAssetPackMagic);
		writer.Write(visualAssetPackVersion);
		writer.Write(manifest.Atlas);
		writer.Write(manifest.LightingNoise != null);
		if (manifest.LightingNoise != null)
			writer.Write(manifest.LightingNoise);

		WriteCount(writer, manifest.Subtextures.Count, "subtextures");
		foreach (var subtexture in manifest.Subtextures)
		{
			writer.Write(subtexture.Name);
			WritePackedSourceRect(writer, subtexture.Source);
			WritePackedFrameRect(writer, subtexture.Frame);
		}

		WriteCount(writer, manifest.Sprites.Count, "sprites");
		foreach (var sprite in manifest.Sprites)
		{
			writer.Write(sprite.Name);
			WritePackedVector(writer, sprite.Origin);

			WriteCount(writer, sprite.Frames.Count, $"{sprite.Name} frames");
			foreach (var frame in sprite.Frames)
			{
				WritePackedSubtexture(writer, frame.Subtexture);
				WritePackedSubtexture(writer, frame.VisibleSubtexture);
				WritePackedSubtexture(writer, frame.NormalSubtexture);
				writer.Write(frame.Duration);
			}

			WriteCount(writer, sprite.Animations.Count, $"{sprite.Name} animations");
			foreach (var animation in sprite.Animations)
			{
				writer.Write(animation.Name);
				writer.Write(animation.FrameStart);
				writer.Write(animation.FrameCount);
				writer.Write(animation.Duration);
			}
		}

		WriteCount(writer, manifest.Tilesets.Count, "tilesets");
		foreach (var tileset in manifest.Tilesets)
		{
			writer.Write(tileset.Name);
			writer.Write(tileset.Columns);
			writer.Write(tileset.Rows);
			WritePackedSubtextures(writer, tileset.Tiles, $"{tileset.Name} tiles");
			WritePackedSubtextureGroups(writer, tileset.Variants, $"{tileset.Name} variants");
			WritePackedSubtextures(writer, tileset.NormalTiles, $"{tileset.Name} normal tiles");
			WritePackedSubtextureGroups(writer, tileset.NormalVariants, $"{tileset.Name} normal variants");
			WritePackedOccluders(writer, tileset.OccluderTiles, $"{tileset.Name} occluders");
			WritePackedOccluderGroups(writer, tileset.OccluderVariants, $"{tileset.Name} occluder variants");
		}
	}

	private static VisualAssetPackManifest ReadVisualAssetPackManifest(Stream stream, string path)
	{
		using var reader = new BinaryReader(stream, Encoding.UTF8);
		var magic = reader.ReadUInt32();
		var version = reader.ReadInt32();
		if (magic != visualAssetPackMagic || version != visualAssetPackVersion)
			throw new InvalidOperationException($"Unsupported visual asset pack manifest: {path}");

		var manifest = new VisualAssetPackManifest
		{
			Atlas = reader.ReadString(),
			LightingNoise = reader.ReadBoolean() ? reader.ReadString() : null
		};

		var subtextureCount = ReadCount(reader, "subtextures");
		for (var i = 0; i < subtextureCount; i++)
			manifest.Subtextures.Add(new()
			{
				Name = reader.ReadString(),
				Source = ReadPackedSourceRect(reader),
				Frame = ReadPackedFrameRect(reader)
			});

		var spriteCount = ReadCount(reader, "sprites");
		for (var i = 0; i < spriteCount; i++)
		{
			var sprite = new PackedSprite
			{
				Name = reader.ReadString(),
				Origin = ReadPackedVector(reader)
			};

			var frameCount = ReadCount(reader, $"{sprite.Name} frames");
			for (var frameIndex = 0; frameIndex < frameCount; frameIndex++)
				sprite.Frames.Add(new()
				{
					Subtexture = ReadPackedSubtexture(reader),
					VisibleSubtexture = ReadPackedSubtexture(reader),
					NormalSubtexture = ReadPackedSubtexture(reader),
					Duration = reader.ReadSingle()
				});

			var animationCount = ReadCount(reader, $"{sprite.Name} animations");
			for (var animationIndex = 0; animationIndex < animationCount; animationIndex++)
				sprite.Animations.Add(new()
				{
					Name = reader.ReadString(),
					FrameStart = reader.ReadInt32(),
					FrameCount = reader.ReadInt32(),
					Duration = reader.ReadSingle()
				});

			manifest.Sprites.Add(sprite);
		}

		var tilesetCount = ReadCount(reader, "tilesets");
		for (var i = 0; i < tilesetCount; i++)
		{
			var tileset = new PackedTileset
			{
				Name = reader.ReadString(),
				Columns = reader.ReadInt32(),
				Rows = reader.ReadInt32(),
				Tiles = ReadPackedSubtextures(reader, "tiles"),
				Variants = ReadPackedSubtextureGroups(reader, "variants"),
				NormalTiles = ReadPackedSubtextures(reader, "normal tiles"),
				NormalVariants = ReadPackedSubtextureGroups(reader, "normal variants"),
				OccluderTiles = ReadPackedOccluders(reader, "occluders"),
				OccluderVariants = ReadPackedOccluderGroups(reader, "occluder variants")
			};
			manifest.Tilesets.Add(tileset);
		}

		return manifest;
	}

	private static void WritePackedSubtexture(BinaryWriter writer, PackedSubtexture subtexture)
	{
		WritePackedSourceRect(writer, subtexture.Source);
		WritePackedFrameRect(writer, subtexture.Frame);
	}

	private static PackedSubtexture ReadPackedSubtexture(BinaryReader reader)
		=> new() { Source = ReadPackedSourceRect(reader), Frame = ReadPackedFrameRect(reader) };

	private static void WritePackedSubtextures(BinaryWriter writer, List<PackedSubtexture> subtextures, string name)
	{
		WriteCount(writer, subtextures.Count, name);
		foreach (var subtexture in subtextures)
			WritePackedSubtexture(writer, subtexture);
	}

	private static List<PackedSubtexture> ReadPackedSubtextures(BinaryReader reader, string name)
	{
		var count = ReadCount(reader, name);
		var result = new List<PackedSubtexture>(count);
		for (var i = 0; i < count; i++)
			result.Add(ReadPackedSubtexture(reader));
		return result;
	}

	private static void WritePackedSubtextureGroups(BinaryWriter writer, List<List<PackedSubtexture>> groups, string name)
	{
		WriteCount(writer, groups.Count, name);
		foreach (var group in groups)
			WritePackedSubtextures(writer, group, name);
	}

	private static List<List<PackedSubtexture>> ReadPackedSubtextureGroups(BinaryReader reader, string name)
	{
		var count = ReadCount(reader, name);
		var result = new List<List<PackedSubtexture>>(count);
		for (var i = 0; i < count; i++)
			result.Add(ReadPackedSubtextures(reader, name));
		return result;
	}

	private static void WritePackedOccluders(BinaryWriter writer, List<List<PackedOccluderSegment>> occluders, string name)
	{
		WriteCount(writer, occluders.Count, name);
		foreach (var tile in occluders)
		{
			WriteCount(writer, tile.Count, name);
			foreach (var segment in tile)
			{
				WritePackedVector(writer, segment.A);
				WritePackedVector(writer, segment.B);
			}
		}
	}

	private static List<List<PackedOccluderSegment>> ReadPackedOccluders(BinaryReader reader, string name)
	{
		var count = ReadCount(reader, name);
		var result = new List<List<PackedOccluderSegment>>(count);
		for (var i = 0; i < count; i++)
		{
			var segmentCount = ReadCount(reader, name);
			var tile = new List<PackedOccluderSegment>(segmentCount);
			for (var segmentIndex = 0; segmentIndex < segmentCount; segmentIndex++)
				tile.Add(new() { A = ReadPackedVector(reader), B = ReadPackedVector(reader) });
			result.Add(tile);
		}
		return result;
	}

	private static void WritePackedOccluderGroups(BinaryWriter writer, List<List<List<PackedOccluderSegment>>> groups, string name)
	{
		WriteCount(writer, groups.Count, name);
		foreach (var group in groups)
			WritePackedOccluders(writer, group, name);
	}

	private static List<List<List<PackedOccluderSegment>>> ReadPackedOccluderGroups(BinaryReader reader, string name)
	{
		var count = ReadCount(reader, name);
		var result = new List<List<List<PackedOccluderSegment>>>(count);
		for (var i = 0; i < count; i++)
			result.Add(ReadPackedOccluders(reader, name));
		return result;
	}

	private static void WritePackedSourceRect(BinaryWriter writer, PackedRect rect)
	{
		writer.Write(PackU16(rect.X, "source rect x"));
		writer.Write(PackU16(rect.Y, "source rect y"));
		writer.Write(PackU16(rect.W, "source rect w"));
		writer.Write(PackU16(rect.H, "source rect h"));
	}

	private static PackedRect ReadPackedSourceRect(BinaryReader reader)
		=> new()
		{
			X = reader.ReadUInt16(),
			Y = reader.ReadUInt16(),
			W = reader.ReadUInt16(),
			H = reader.ReadUInt16()
		};

	private static void WritePackedFrameRect(BinaryWriter writer, PackedRect rect)
	{
		writer.Write(PackI16(rect.X, "frame rect x"));
		writer.Write(PackI16(rect.Y, "frame rect y"));
		writer.Write(PackU16(rect.W, "frame rect w"));
		writer.Write(PackU16(rect.H, "frame rect h"));
	}

	private static PackedRect ReadPackedFrameRect(BinaryReader reader)
		=> new()
		{
			X = reader.ReadInt16(),
			Y = reader.ReadInt16(),
			W = reader.ReadUInt16(),
			H = reader.ReadUInt16()
		};

	private static void WritePackedVector(BinaryWriter writer, PackedVector vector)
	{
		writer.Write(vector.X);
		writer.Write(vector.Y);
	}

	private static PackedVector ReadPackedVector(BinaryReader reader)
		=> new() { X = reader.ReadSingle(), Y = reader.ReadSingle() };

	private static void WriteCount(BinaryWriter writer, int count, string name)
	{
		if (count > ushort.MaxValue)
			throw new InvalidOperationException($"Visual asset pack has too many {name}: {count}");
		writer.Write((ushort)count);
	}

	private static int ReadCount(BinaryReader reader, string name)
	{
		var count = reader.ReadUInt16();
		return count;
	}

	private static ushort PackU16(float value, string name)
	{
		var rounded = MathF.Round(value);
		if (!float.IsFinite(value) || MathF.Abs(value - rounded) > 0.001f || rounded < 0 || rounded > ushort.MaxValue)
			throw new InvalidOperationException($"Visual asset pack {name} does not fit ushort pixel coordinates: {value}");
		return (ushort)rounded;
	}

	private static short PackI16(float value, string name)
	{
		var rounded = MathF.Round(value);
		if (!float.IsFinite(value) || MathF.Abs(value - rounded) > 0.001f || rounded < short.MinValue || rounded > short.MaxValue)
			throw new InvalidOperationException($"Visual asset pack {name} does not fit short pixel coordinates: {value}");
		return (short)rounded;
	}

	private static void EnsureFonts(GraphicsDevice gfx)
	{
		if (Font == null)
		{
			Font = new SpriteFont(gfx, Path.Join(AssetsPath, "Fonts", "dogica.ttf"), 8);
			Font.LineGap = 4;
		}
		if (SpeechFont == null)
		{
			SpeechFont = new SpriteFont(gfx, Path.Join(AssetsPath, "Fonts", "m3x6.ttf"), 16, premultiplyAlpha: true, pixelPerfect: true);
			SpeechFont.LineGap = 0;
		}
	}

	private static List<Subtexture> LoadSubtextures(List<PackedSubtexture> packed, Texture atlas)
	{
		var result = new List<Subtexture>(packed.Count);
		foreach (var item in packed)
			result.Add(item.ToSubtexture(atlas));
		return result;
	}

	private static List<List<Subtexture>> LoadSubtextureGroups(List<List<PackedSubtexture>> packed, Texture atlas)
	{
		var result = new List<List<Subtexture>>(packed.Count);
		foreach (var group in packed)
			result.Add(LoadSubtextures(group, atlas));
		return result;
	}

	private static List<Tileset.OccluderSegment[]> LoadOccluders(List<List<PackedOccluderSegment>> packed)
	{
		var result = new List<Tileset.OccluderSegment[]>(packed.Count);
		foreach (var tile in packed)
		{
			var segments = new Tileset.OccluderSegment[tile.Count];
			for (var i = 0; i < tile.Count; i++)
				segments[i] = tile[i].ToSegment();
			result.Add(segments);
		}
		return result;
	}

	private static List<List<Tileset.OccluderSegment[]>> LoadOccluderGroups(List<List<List<PackedOccluderSegment>>> packed)
	{
		var result = new List<List<Tileset.OccluderSegment[]>>(packed.Count);
		foreach (var group in packed)
			result.Add(LoadOccluders(group));
		return result;
	}

	private sealed class VisualAssetPackManifest
	{
		public string Atlas { get; set; } = "atlas.png";
		public string? LightingNoise { get; set; }
		public List<PackedNamedSubtexture> Subtextures { get; set; } = [];
		public List<PackedSprite> Sprites { get; set; } = [];
		public List<PackedTileset> Tilesets { get; set; } = [];
	}

	private sealed class VisualAssetBake : IDisposable
	{
		public Packer.Output Output { get; }
		public Dictionary<string, Aseprite> SpriteFiles { get; }
		public List<(string Name, Aseprite Source, Func<Predicate<Aseprite.Layer>> LayerFilter)> GeneratedSprites { get; }
		public Dictionary<string, Aseprite> TilesetFiles { get; }
		public Dictionary<string, List<Image>> GeneratedTileImages { get; }
		public Dictionary<string, List<Image>> GeneratedNormalImages { get; }
		public Dictionary<string, List<List<Tileset.OccluderSegment[]>>> GeneratedOccluders { get; }
		public HashSet<string> KitGeneratedTilesets { get; }
		public Dictionary<string, PackedSubtexture> Subtextures { get; } = [];

		public VisualAssetBake(
			Packer.Output output,
			Dictionary<string, Aseprite> spriteFiles,
			List<(string Name, Aseprite Source, Func<Predicate<Aseprite.Layer>> LayerFilter)> generatedSprites,
			Dictionary<string, Aseprite> tilesetFiles,
			Dictionary<string, List<Image>> generatedTileImages,
			Dictionary<string, List<Image>> generatedNormalImages,
			Dictionary<string, List<List<Tileset.OccluderSegment[]>>> generatedOccluders,
			HashSet<string> kitGeneratedTilesets)
		{
			Output = output;
			SpriteFiles = spriteFiles;
			GeneratedSprites = generatedSprites;
			TilesetFiles = tilesetFiles;
			GeneratedTileImages = generatedTileImages;
			GeneratedNormalImages = generatedNormalImages;
			GeneratedOccluders = generatedOccluders;
			KitGeneratedTilesets = kitGeneratedTilesets;

			foreach (var entry in output.Entries)
				Subtextures[entry.Name] = PackedSubtexture.From(entry.Source, entry.Frame);
		}

		public PackedSubtexture Subtexture(string name)
			=> Subtextures[name];

		public void Dispose()
		{
			foreach (var images in GeneratedTileImages.Values)
				foreach (var image in images)
					image.Dispose();
			foreach (var images in GeneratedNormalImages.Values)
				foreach (var image in images)
					image.Dispose();
		}
	}

	private sealed class PackedNamedSubtexture : PackedSubtexture
	{
		public string Name { get; set; } = "";

		public static PackedNamedSubtexture From(string name, PackedSubtexture subtexture)
			=> new() { Name = name, Source = subtexture.Source, Frame = subtexture.Frame };
	}

	private class PackedSubtexture
	{
		public PackedRect Source { get; set; } = new();
		public PackedRect Frame { get; set; } = new();

		public static PackedSubtexture From(Rect source, Rect frame)
			=> new() { Source = PackedRect.From(source), Frame = PackedRect.From(frame) };

		public static PackedSubtexture VisibleFrom(PackedSubtexture subtexture)
			=> new()
			{
				Source = subtexture.Source,
				Frame = new() { X = 0, Y = 0, W = subtexture.Source.W, H = subtexture.Source.H }
			};

		public Subtexture ToSubtexture(Texture atlas)
			=> new(atlas, Source.ToRect(), Frame.ToRect());
	}

	private sealed class PackedSprite
	{
		public string Name { get; set; } = "";
		public PackedVector Origin { get; set; } = new();
		public List<PackedSpriteFrame> Frames { get; set; } = [];
		public List<PackedSpriteAnimation> Animations { get; set; } = [];
	}

	private sealed class PackedSpriteFrame
	{
		public PackedSubtexture Subtexture { get; set; } = new();
		public PackedSubtexture VisibleSubtexture { get; set; } = new();
		public PackedSubtexture NormalSubtexture { get; set; } = new();
		public float Duration { get; set; }
	}

	private sealed class PackedSpriteAnimation
	{
		public string Name { get; set; } = "";
		public int FrameStart { get; set; }
		public int FrameCount { get; set; }
		public float Duration { get; set; }
	}

	private sealed class PackedTileset
	{
		public string Name { get; set; } = "";
		public int Columns { get; set; }
		public int Rows { get; set; }
		public List<PackedSubtexture> Tiles { get; set; } = [];
		public List<List<PackedSubtexture>> Variants { get; set; } = [];
		public List<PackedSubtexture> NormalTiles { get; set; } = [];
		public List<List<PackedSubtexture>> NormalVariants { get; set; } = [];
		public List<List<PackedOccluderSegment>> OccluderTiles { get; set; } = [];
		public List<List<List<PackedOccluderSegment>>> OccluderVariants { get; set; } = [];
	}

	private sealed class PackedRect
	{
		public float X { get; set; }
		public float Y { get; set; }
		public float W { get; set; }
		public float H { get; set; }

		public static PackedRect From(Rect rect)
			=> new() { X = rect.X, Y = rect.Y, W = rect.Width, H = rect.Height };

		public Rect ToRect()
			=> new(X, Y, W, H);
	}

	private sealed class PackedVector
	{
		public float X { get; set; }
		public float Y { get; set; }

		public static PackedVector From(Vector2 vector)
			=> new() { X = vector.X, Y = vector.Y };

		public Vector2 ToVector2()
			=> new(X, Y);
	}

	private sealed class PackedOccluderSegment
	{
		public PackedVector A { get; set; } = new();
		public PackedVector B { get; set; } = new();

		public static PackedOccluderSegment From(Tileset.OccluderSegment segment)
			=> new() { A = PackedVector.From(segment.A), B = PackedVector.From(segment.B) };

		public Tileset.OccluderSegment ToSegment()
			=> new(A.ToVector2(), B.ToVector2());
	}

	private static List<List<PackedOccluderSegment>> PackOccluders(List<Tileset.OccluderSegment[]> occluders)
	{
		var result = new List<List<PackedOccluderSegment>>(occluders.Count);
		foreach (var tile in occluders)
		{
			var packed = new List<PackedOccluderSegment>(tile.Length);
			foreach (var segment in tile)
				packed.Add(PackedOccluderSegment.From(segment));
			result.Add(packed);
		}
		return result;
	}

	private static List<List<List<PackedOccluderSegment>>> PackOccluderGroups(List<List<Tileset.OccluderSegment[]>> groups)
	{
		var result = new List<List<List<PackedOccluderSegment>>>(groups.Count);
		foreach (var group in groups)
			result.Add(PackOccluders(group));
		return result;
	}

	internal static Texture? LoadAsepriteTexture(GraphicsDevice gfx, string relativePath, string name)
	{
		var path = Path.Join(AssetsPath, relativePath);
		if (!File.Exists(path))
			return null;

		var ase = new Aseprite(path);
		if (ase.Frames.Length <= 0)
			return null;

		using var image = ase.RenderFrame(0);
		return new Texture(gfx, image, name);
	}

	private static IEnumerable<string> EnumerateAsepriteFiles(string path)
	{
		if (!Directory.Exists(path))
			yield break;

		foreach (var file in Directory.EnumerateFiles(path, "*.ase", SearchOption.AllDirectories))
			yield return file;
		foreach (var file in Directory.EnumerateFiles(path, "*.aseprite", SearchOption.AllDirectories))
			yield return file;
	}

	private static bool IsDebugLayer(Aseprite.Layer layer)
		=> layer.Name?.Contains("debug", StringComparison.OrdinalIgnoreCase) == true;

	private static HashSet<Aseprite.Layer> GetNonDebugLayers(Aseprite ase)
	{
		var result = new HashSet<Aseprite.Layer>();
		var debugChildLevel = int.MaxValue;
		foreach (var layer in ase.Layers)
		{
			if (layer.ChildLevel <= debugChildLevel)
				debugChildLevel = int.MaxValue;

			if (debugChildLevel != int.MaxValue || IsDebugLayer(layer))
			{
				debugChildLevel = Math.Min(debugChildLevel, layer.ChildLevel);
				continue;
			}

			result.Add(layer);
		}

		return result;
	}

	private static List<Aseprite.Layer> GetVariantLayers(Aseprite ase, HashSet<Aseprite.Layer> nonDebugLayers)
		=> ase.Layers
			.Where(it => nonDebugLayers.Contains(it) && it.Visible && it.Type == Aseprite.LayerType.Normal)
			.ToList();

	private static void GenerateDualTilesetFromKit(string kitName, Aseprite ase, out string baseName, out List<Image> baseTiles, out List<Image> baseSlopeEdges, out string dualName, out List<Image> dualVariants, out List<Image> dualNormals, out List<List<Tileset.OccluderSegment[]>> dualOccluders)
	{
		baseName = kitName[..^"-kit".Length];
		baseTiles = [];
		baseSlopeEdges = [];
		dualName = $"{baseName}-dual";
		dualVariants = [];
		dualNormals = [];
		dualOccluders = [];

		var slices = GetKitSlices(ase);
		if (!slices.TryGetValue("fill", out var fillSlice))
			throw new InvalidOperationException($"Dual tileset kit '{kitName}' is missing required 'fill' slice.");
		if (!slices.TryGetValue("mask", out var maskSlice))
			throw new InvalidOperationException($"Dual tileset kit '{kitName}' is missing required 'mask' slice.");
		slices.TryGetValue("edge", out var edgeSlice);

		var maskBounds = GetSliceBounds(maskSlice);
		if (maskBounds.Size != new Point2(Game.TileSize * 4, Game.TileSize * 4))
			throw new InvalidOperationException($"Dual tileset kit '{kitName}' mask slice must be exactly {Game.TileSize * 4}x{Game.TileSize * 4} pixels.");

		using var rendered = ase.RenderFrame(0, GetNonDebugLayers(ase).Contains);
		using var mask = ExtractTile(rendered, maskBounds);
		var fills = ExtractSliceTiles(rendered, fillSlice, kitName, required: true);
		var edges = edgeSlice != null
			? ExtractSliceTiles(rendered, edgeSlice, kitName, required: false)
			: [];

		if (edges.Count == 0 && edgeSlice != null)
			throw new InvalidOperationException($"Dual tileset kit '{kitName}' edge slice does not contain any non-empty {Game.TileSize}x{Game.TileSize} tile.");

		for (var i = 0; i < fills.Count; i++)
		{
			var fill = fills[i];
			if (i == 0)
			{
				baseTiles.Add(ExtractTile(fill, fill.Bounds));
				if (edges.Count > 0)
					baseSlopeEdges.Add(ExtractTile(edges[i % edges.Count], edges[i % edges.Count].Bounds));
			}
			dualVariants.Add(GenerateDualTilesetVariant(fill, mask, edges.Count > 0 ? edges[i % edges.Count] : null));
			dualNormals.Add(GenerateDualTilesetNormalVariant(mask));
			dualOccluders.Add(GenerateDualTilesetOccluderVariant(mask));
		}

		foreach (var fill in fills)
			fill.Dispose();
		foreach (var edge in edges)
			edge.Dispose();
	}

	private static string NormalizeLayerName(string? name)
		=> (name ?? "").Trim().Replace(" ", "", StringComparison.Ordinal).Replace("-", "", StringComparison.Ordinal).ToLowerInvariant();

	private static Dictionary<string, Aseprite.Slice> GetKitSlices(Aseprite ase)
	{
		var result = new Dictionary<string, Aseprite.Slice>(StringComparer.OrdinalIgnoreCase);
		foreach (var slice in ase.Slices)
		{
			var name = NormalizeLayerName(slice.Name);
			if (string.IsNullOrEmpty(name) || name.Contains("debug", StringComparison.OrdinalIgnoreCase))
				continue;
			result.TryAdd(name, slice);
		}
		return result;
	}

	private static RectInt GetSliceBounds(Aseprite.Slice slice)
	{
		if (slice.Keys.Length <= 0)
			throw new InvalidOperationException($"Aseprite slice '{slice.Name}' has no keys.");
		return slice.Keys[0].Bounds;
	}

	private static List<Image> ExtractSliceTiles(Image source, Aseprite.Slice slice, string kitName, bool required)
	{
		var bounds = GetSliceBounds(slice);
		if (bounds.Width % Game.TileSize != 0 || bounds.Height % Game.TileSize != 0)
			throw new InvalidOperationException($"Dual tileset kit '{kitName}' slice '{slice.Name}' dimensions must be multiples of {Game.TileSize} pixels.");

		var tiles = new List<Image>();
		var columns = bounds.Width / Game.TileSize;
		var rows = bounds.Height / Game.TileSize;
		for (var y = 0; y < rows; y++)
			for (var x = 0; x < columns; x++)
			{
				var rect = bounds.Position + new Point2(x, y) * Game.TileSize;
				var tileRect = new RectInt(rect.X, rect.Y, Game.TileSize, Game.TileSize);
				if (HasOpaquePixel(source, tileRect))
					tiles.Add(ExtractTile(source, tileRect));
			}

		if (required && tiles.Count <= 0)
			throw new InvalidOperationException($"Dual tileset kit '{kitName}' slice '{slice.Name}' does not contain any non-empty {Game.TileSize}x{Game.TileSize} tile.");
		return tiles;
	}

	private static bool HasOpaquePixel(Image image, RectInt rect)
	{
		for (var y = rect.Y; y < rect.Bottom; y++)
			for (var x = rect.X; x < rect.Right; x++)
				if (image[x, y].A > 0)
					return true;
		return false;
	}

	private static Image GenerateDualTilesetVariant(Image fill, Image mask, Image? edge)
	{
		var output = new Image(Game.TileSize * 4, Game.TileSize * 4);
		for (var tileMask = 1; tileMask < TileQuadrants.All + 1; tileMask++)
		{
			var tile = MaskToDualTile(tileMask);
			var source = new RectInt(tile.X, tile.Y, 1, 1) * Game.TileSize;
			GenerateDualTile(output, source, fill, tileMask, mask, edge);
		}
		return output;
	}

	private static Image GenerateDualTilesetNormalVariant(Image mask)
	{
		var output = new Image(Game.TileSize * 4, Game.TileSize * 4);
		for (var tileMask = 1; tileMask < TileQuadrants.All + 1; tileMask++)
		{
			var tile = MaskToDualTile(tileMask);
			var source = new RectInt(tile.X, tile.Y, 1, 1) * Game.TileSize;
			GenerateDualNormalTile(output, source, tileMask, mask);
		}
		return output;
	}

	private static List<Tileset.OccluderSegment[]> GenerateDualTilesetOccluderVariant(Image mask)
	{
		var result = new Tileset.OccluderSegment[Game.TileSize * 2][];
		for (var i = 0; i < result.Length; i++)
			result[i] = [];

		for (var tileMask = 1; tileMask < TileQuadrants.All + 1; tileMask++)
		{
			var tile = MaskToDualTile(tileMask);
			var source = new RectInt(tile.X, tile.Y, 1, 1) * Game.TileSize;
			result[tile.X * 4 + tile.Y] = GenerateMaskOccluderSegments(mask, source);
		}

		return result.ToList();
	}

	private static Tileset.OccluderSegment[] GenerateMaskOccluderSegments(Image mask, RectInt source)
	{
		var segments = new List<Tileset.OccluderSegment>();

		for (var y = 0; y < Game.TileSize; y++)
		{
			MergeHorizontalMaskEdges(segments, mask, source, y, top: true);
			MergeHorizontalMaskEdges(segments, mask, source, y, top: false);
		}

		for (var x = 0; x < Game.TileSize; x++)
		{
			MergeVerticalMaskEdges(segments, mask, source, x, left: true);
			MergeVerticalMaskEdges(segments, mask, source, x, left: false);
		}

		return segments.ToArray();
	}

	private static void MergeHorizontalMaskEdges(List<Tileset.OccluderSegment> segments, Image mask, RectInt source, int y, bool top)
	{
		var start = -1;
		for (var x = 0; x <= Game.TileSize; x++)
		{
			var edge = x < Game.TileSize &&
				IsMaskSolid(mask, source, x, y) &&
				!IsMaskSolid(mask, source, x, y + (top ? -1 : 1));

			if (edge && start < 0)
				start = x;
			else if (!edge && start >= 0)
			{
				var edgeY = y + (top ? 0 : 1);
				segments.Add(new(new Vector2(start, edgeY), new Vector2(x, edgeY)));
				start = -1;
			}
		}
	}

	private static void MergeVerticalMaskEdges(List<Tileset.OccluderSegment> segments, Image mask, RectInt source, int x, bool left)
	{
		var start = -1;
		for (var y = 0; y <= Game.TileSize; y++)
		{
			var edge = y < Game.TileSize &&
				IsMaskSolid(mask, source, x, y) &&
				!IsMaskSolid(mask, source, x + (left ? -1 : 1), y);

			if (edge && start < 0)
				start = y;
			else if (!edge && start >= 0)
			{
				var edgeX = x + (left ? 0 : 1);
				segments.Add(new(new Vector2(edgeX, start), new Vector2(edgeX, y)));
				start = -1;
			}
		}
	}

	private static bool IsMaskSolid(Image mask, RectInt source, int x, int y)
	{
		if (x < 0 || y < 0 || x >= Game.TileSize || y >= Game.TileSize)
			return false;

		return mask[source.X + x, source.Y + y].A > 0;
	}

	private static void GenerateDualNormalTile(Image output, RectInt source, int mask, Image alphaMask)
	{
		var tile = MaskToDualTile(mask);
		var destination = new Point2(tile.X * Game.TileSize, tile.Y * Game.TileSize);
		const float bend = 1.25f;

		for (var y = 0; y < Game.TileSize; y++)
			for (var x = 0; x < Game.TileSize; x++)
			{
				var alpha = AlphaAt(alphaMask, source.X + x, source.Y + y);
				if (alpha <= 0)
					continue;

				var sx =
					AlphaAtClampedToSource(alphaMask, source, x - 1, y - 1) +
					AlphaAtClampedToSource(alphaMask, source, x - 1, y) * 2 +
					AlphaAtClampedToSource(alphaMask, source, x - 1, y + 1) -
					AlphaAtClampedToSource(alphaMask, source, x + 1, y - 1) -
					AlphaAtClampedToSource(alphaMask, source, x + 1, y) * 2 -
					AlphaAtClampedToSource(alphaMask, source, x + 1, y + 1);
				var sy =
					AlphaAtClampedToSource(alphaMask, source, x - 1, y - 1) +
					AlphaAtClampedToSource(alphaMask, source, x, y - 1) * 2 +
					AlphaAtClampedToSource(alphaMask, source, x + 1, y - 1) -
					AlphaAtClampedToSource(alphaMask, source, x - 1, y + 1) -
					AlphaAtClampedToSource(alphaMask, source, x, y + 1) * 2 -
					AlphaAtClampedToSource(alphaMask, source, x + 1, y + 1);

				var strength = Math.Clamp(MathF.Sqrt(sx * sx + sy * sy) * 0.5f, 0, 1) * alpha;
				if (strength <= 0)
					continue;

				var normal = Vector3.Normalize(new Vector3(sx * bend, sy * bend, 1));
				output[destination.X + x, destination.Y + y] = new Color(
					normal.X * 0.5f + 0.5f,
					normal.Y * 0.5f + 0.5f,
					normal.Z * 0.5f + 0.5f,
					strength);
			}
	}

	private static float AlphaAtClampedToSource(Image image, RectInt source, int x, int y)
	{
		x = Math.Clamp(x, 0, source.Width - 1);
		y = Math.Clamp(y, 0, source.Height - 1);
		return AlphaAt(image, source.X + x, source.Y + y);
	}

	private static void GenerateDualTile(Image output, RectInt source, Image fill, int mask, Image alphaMask, Image? edge)
	{
		var tile = MaskToDualTile(mask);
		var destination = new Point2(tile.X * Game.TileSize, tile.Y * Game.TileSize);
		const int edgeWidth = 2;

		for (var y = 0; y < Game.TileSize; y++)
			for (var x = 0; x < Game.TileSize; x++)
			{
				var alpha = alphaMask[source.X + x, source.Y + y].A;
				if (alpha == 0)
					continue;

				var color = fill[x, y];
				if (edge != null && IsMaskEdge(alphaMask, source, x, y, edgeWidth))
					color = edge[x, y];

				color.A = (byte)(color.A * alpha / 255);
				output[destination.X + x, destination.Y + y] = color;
			}
	}

	private static bool IsMaskEdge(Image mask, RectInt source, int x, int y, int width)
	{
		for (var oy = -width; oy <= width; oy++)
			for (var ox = -width; ox <= width; ox++)
			{
				if (Math.Abs(ox) + Math.Abs(oy) > width)
					continue;
				var sampleX = source.X + x + ox;
				var sampleY = source.Y + y + oy;
				if (sampleX < source.X || sampleY < source.Y || sampleX >= source.Right || sampleY >= source.Bottom)
					continue;
				if (mask[sampleX, sampleY].A == 0)
					return true;
			}
		return false;
	}

	private static Point2 MaskToDualTile(int mask)
	{
		var index = Array.FindIndex(DualMaskToTile, it => it.Mask == mask);
		return index >= 0 ? DualMaskToTile[index].Tile : Point2.Zero;
	}

	private static readonly (int Mask, Point2 Tile)[] DualMaskToTile =
	[
		(0, new(0, 3)),
		(TileQuadrants.TopLeft, new(3, 3)),
		(TileQuadrants.BottomLeft, new(0, 0)),
		(TileQuadrants.TopLeft | TileQuadrants.BottomLeft, new(3, 2)),
		(TileQuadrants.TopRight, new(0, 2)),
		(TileQuadrants.TopLeft | TileQuadrants.TopRight, new(1, 2)),
		(TileQuadrants.BottomLeft | TileQuadrants.TopRight, new(2, 3)),
		(TileQuadrants.TopLeft | TileQuadrants.BottomLeft | TileQuadrants.TopRight, new(3, 1)),
		(TileQuadrants.BottomRight, new(1, 3)),
		(TileQuadrants.TopLeft | TileQuadrants.BottomRight, new(0, 1)),
		(TileQuadrants.BottomLeft | TileQuadrants.BottomRight, new(3, 0)),
		(TileQuadrants.TopLeft | TileQuadrants.BottomLeft | TileQuadrants.BottomRight, new(2, 0)),
		(TileQuadrants.TopRight | TileQuadrants.BottomRight, new(1, 0)),
		(TileQuadrants.TopLeft | TileQuadrants.TopRight | TileQuadrants.BottomRight, new(2, 2)),
		(TileQuadrants.BottomLeft | TileQuadrants.TopRight | TileQuadrants.BottomRight, new(1, 1)),
		(TileQuadrants.All, new(2, 1)),
	];

	// Loads the in-betweens whitelist (within-animation + transitions) from
	// inbetweens.json. This is the source of truth for what gets baked.
	private static void LoadInbetweenConfig()
	{
		InbetweenAnimations.Clear();
		InbetweenTransitions.Clear();

		var path = INBETWEENS_JSON_FILENAME;
		if (!File.Exists(path))
			return;

		try
		{
			using var doc = JsonDocument.Parse(File.ReadAllBytes(path));
			var root = doc.RootElement;

			if (root.TryGetProperty("animations", out var anims) && anims.ValueKind == JsonValueKind.Array)
			{
				foreach (var item in anims.EnumerateArray())
				{
					var sprite = item.TryGetProperty("sprite", out var s) ? s.GetString() : null;
					var tag = item.TryGetProperty("tag", out var t) ? t.GetString() : "*";
					int count = item.TryGetProperty("count", out var c) ? c.GetInt32() : 0;
					if (sprite != null && tag != null && count > 0)
						InbetweenAnimations[(sprite, tag)] = count;
				}
			}

			if (root.TryGetProperty("transitions", out var trans) && trans.ValueKind == JsonValueKind.Array)
			{
				foreach (var item in trans.EnumerateArray())
				{
					var sprite = item.TryGetProperty("sprite", out var s) ? s.GetString() : null;
					var from = item.TryGetProperty("from", out var f) ? f.GetString() : null;
					var to = item.TryGetProperty("to", out var tt) ? tt.GetString() : null;
					int count = item.TryGetProperty("count", out var c) ? c.GetInt32() : 0;
					if (sprite != null && from != null && to != null && count > 0)
						InbetweenTransitions[(sprite, from, to)] = count;
				}
			}
		}
		catch (Exception e)
		{
			Console.Error.WriteLine($"Failed to load {path}: {e.Message}");
		}
	}

	// Expands the frames of one sprite (inserting whitelisted within-animation
	// in-betweens) and packs them, then bakes any whitelisted transitions.
	private static void PackSpriteFrames(string name, Aseprite ase, Image[] frames, Packer packer)
	{
		if (TryBuildInbetweenSpec(name, ase, frames.Length, out var spec, out var durationsSec))
		{
			var (expFrames, expDurations, map) = Inbetweens.GenerateExpanded(frames, durationsSec, spec);
			ExpandedFrameDurations[name] = expDurations;
			ExpandedFrameMap[name] = map;
			for (int i = 0; i < expFrames.Length; i++)
			{
				packer.Add($"{name}/{i}", expFrames[i]);
				packer.Add($"{name}/normal/{i}", GenerateNormalMap(expFrames[i]));
			}
		}
		else
		{
			for (int i = 0; i < frames.Length; i++)
			{
				packer.Add($"{name}/{i}", frames[i]);
				packer.Add($"{name}/normal/{i}", GenerateNormalMap(frames[i]));
			}
		}

		GenerateTransitionFrames(name, ase, frames, packer);
	}

	// Builds the per-frame in-between spec for a sprite from the InbetweenAnimations
	// whitelist. spec[i] = (count, target) describes in-betweens inserted after
	// original frame i. Returns false (and leaves the sprite unexpanded) when no
	// tag of this sprite is whitelisted.
	private static bool TryBuildInbetweenSpec(
		string name, Aseprite ase, int frameCount,
		out (int Count, int Target)[] spec, out float[] durationsSec)
	{
		spec = new (int Count, int Target)[frameCount];
		durationsSec = new float[frameCount];
		for (int i = 0; i < frameCount; i++)
			durationsSec[i] = ase.Frames[i].Duration / 1000f;

		int wildcard = InbetweenAnimations.TryGetValue((name, "*"), out var w) ? w : 0;
		bool any = false;

		foreach (var tag in ase.Tags)
		{
			if (string.IsNullOrEmpty(tag.Name))
				continue;

			int count = InbetweenAnimations.TryGetValue((name, tag.Name), out var c) ? c : wildcard;
			if (count <= 0)
				continue;

			// In-betweens between each consecutive pair within the tag.
			for (int i = tag.From; i < tag.To && i + 1 < frameCount; i++)
			{
				spec[i] = (count, i + 1);
				any = true;
			}

			// Looping tags also morph the last frame back to the first.
			if (tag.Repeat == 0 && tag.To > tag.From && tag.To < frameCount)
			{
				spec[tag.To] = (count, tag.From);
				any = true;
			}
		}

		return any;
	}

	private static void GenerateTransitionFrames(string name, Aseprite ase, Image[] frames, Packer packer)
	{
		var ownTransitions = InbetweenTransitions
			.Where(kv => kv.Key.Sprite == name && kv.Value > 0)
			.ToList();

		if (ownTransitions.Count == 0)
			return;

		// Resolve tag indices for all pairs (sequential, quick)
		var pairs = new List<(string FromTag, string ToTag, int FromIdx, int ToIdx, int Count)>();
		foreach (var (key, count) in ownTransitions)
		{
			Aseprite.Tag? fromTag = null, toTag = null;
			foreach (var tag in ase.Tags)
			{
				if (tag.Name == key.From) fromTag = tag;
				if (tag.Name == key.To) toTag = tag;
			}
			if (fromTag == null || toTag == null)
				continue;

			int fromLastIdx = fromTag.Value.To;
			int toFirstIdx = toTag.Value.From;

			if (fromLastIdx >= frames.Length || toFirstIdx >= frames.Length)
				continue;

			pairs.Add((key.From, key.To, fromLastIdx, toFirstIdx, count));
		}

		if (pairs.Count == 0)
			return;

		// Generate all in-betweens in parallel (ComputeFlow / Warp are pure, thread-safe)
		var results = new (Image[] Frames, float[] Durations)?[pairs.Count];
		Parallel.For(0, pairs.Count, i =>
		{
			var p = pairs[i];
			results[i] = Inbetweens.GenerateForPair(frames[p.FromIdx], frames[p.ToIdx], p.Count);
		});

		// Pack results sequentially (packer.Add is not thread-safe)
		var transitions = new List<GeneratedTransition>();
		for (int i = 0; i < pairs.Count; i++)
		{
			if (results[i] is not (var transFrames, var transDurations))
				continue;

			var p = pairs[i];
			// Console.WriteLine($"[Transition] generated {transFrames.Length} frames for {name}: {p.FromTag} -> {p.ToTag}");

			string prefix = $"{name}/transition/{p.FromTag}->{p.ToTag}";
			for (int j = 0; j < transFrames.Length; j++)
			{
				packer.Add($"{prefix}/{j}", transFrames[j]);
				packer.Add($"{prefix}/normal/{j}", GenerateNormalMap(transFrames[j]));
			}

			transitions.Add(new(p.FromTag, p.ToTag, transFrames.Length, transDurations));
		}

		if (transitions.Count > 0)
			GeneratedTransitions[name] = transitions;
	}

	private static Texture? LoadTexture(GraphicsDevice gfx, string relativePath, string name)
	{
		var path = Path.Join(AssetsPath, relativePath);
		if (!File.Exists(path))
			return null;

		using var image = new Image(path);
		return new Texture(gfx, image, name);
	}

	private static Predicate<Aseprite.Layer> CreateBudPlayerLayerFilter()
	{
		var hiddenChildLevel = int.MaxValue;
		return layer =>
		{
			if (layer.ChildLevel <= hiddenChildLevel)
				hiddenChildLevel = int.MaxValue;

			var layerName = NormalizeLayerName(layer.Name);
			if (hiddenChildLevel != int.MaxValue || layerName == "sword" || layerName.StartsWith("sword", StringComparison.Ordinal))
			{
				hiddenChildLevel = Math.Min(hiddenChildLevel, layer.ChildLevel);
				return false;
			}

			return true;
		};
	}

#if !IOS
	private static void DumpPackedAtlasPages(Packer.Output output)
	{
		try
		{
			var directory = Path.GetFullPath(Path.Join(AssetsPath, "..", "AtlasDumps"));
			Directory.CreateDirectory(directory);

			foreach (var oldFile in Directory.EnumerateFiles(directory, "batcher-atlas-*.png"))
				File.Delete(oldFile);

			for (var i = 0; i < output.Pages.Count; i++)
			{
				var page = output.Pages[i];
				var file = Path.Join(directory, $"batcher-atlas-{i:00}.png");
				page.WritePng(file);
				Console.WriteLine($"Wrote packed Batcher atlas: {file} ({page.Width}x{page.Height})");
			}
		}
		catch (Exception e)
		{
			Console.Error.WriteLine($"Failed to write packed Batcher atlas PNGs: {e}");
		}
	}

#endif

	internal static bool UseUserRooms = false;

	private static void LoadRooms()
	{
		Rooms.Clear();
		RoomsById.Clear();

		var deleted = UseUserRooms ? LoadUserRoomTombstones() : [];

		if (Directory.Exists(WorldRoomsPath))
			foreach (var file in Directory.EnumerateFiles(WorldRoomsPath, "*.json"))
			{
				var id = Path.GetFileNameWithoutExtension(file);
				if (deleted.Contains(id))
					continue;

				AddRoom(new Room(file)
				{
					Storage = Room.StorageKind.Bundled,
					SourcePath = file
				});
			}

		if (UseUserRooms && UserWorldRoomsPath is { } userWorldRoomsPath && Directory.Exists(userWorldRoomsPath))
			foreach (var file in Directory.EnumerateFiles(userWorldRoomsPath, "*.json"))
				AddRoom(new Room(file)
				{
					Storage = Room.StorageKind.User,
					SourcePath = file
				});

		if (RoomsById.Count == 0)
			CreateDefaultStartRoom();
	}

	private static void CreateDefaultStartRoom()
	{
		var room = new Room(defaultStartRoomId, Point2.Zero, Room.DefaultSize);
		AddRoom(room);
		room.Save();
	}

	private static void AddRoom(Room room)
	{
		if (RoomsById.TryGetValue(room.Id, out var existing) &&
			Rooms.TryGetValue(existing.Cell, out var byOldCell) &&
			byOldCell == existing)
			Rooms.Remove(existing.Cell);

		Rooms[room.Cell] = room;
		RoomsById[room.Id] = room;
	}

	public static void SaveRoom(Room room, string json)
	{
		var path = GetRoomSavePath(room);
		Directory.CreateDirectory(Path.GetDirectoryName(path)!);
		File.WriteAllText(path, json);
		room.Storage = IsUserRoomPath(path) ? Room.StorageKind.User : Room.StorageKind.Bundled;
		room.SourcePath = path;
		if (room.Storage == Room.StorageKind.User)
			ClearUserRoomTombstone(room.Id);
	}

	public static bool RenameRoom(Room room, string id)
	{
		id = id.Trim();
		if (!CanRenameRoom(room, id))
			return false;

		var oldId = room.Id;
		if (oldId == id)
			return true;

		var oldPath = room.SourcePath;
		var oldBundledPath = BundledRoomPath(oldId);
		var oldHadBundledRoom = File.Exists(oldBundledPath);
		room.Id = id;
		RoomsById.Remove(oldId);
		RoomsById[id] = room;
		room.Save();

		DeleteRoomFile(oldPath, oldId);
		if (oldHadBundledRoom && !string.Equals(oldPath, oldBundledPath, StringComparison.Ordinal))
			WriteUserRoomTombstone(oldId);

		return true;
	}

	public static bool CanRenameRoom(Room room, string id)
	{
		id = id.Trim();
		if (string.IsNullOrEmpty(id))
			return false;
		if (RoomsById.TryGetValue(id, out var existing) && existing != room)
			return false;

		if (RoomFileExists(id) && !string.Equals(room.Id, id, StringComparison.OrdinalIgnoreCase))
			return false;

		return true;
	}

	public static bool DeleteRoom(Room room)
	{
		if (!RoomsById.Remove(room.Id))
			return false;

		if (Rooms.TryGetValue(room.Cell, out var byCell) && byCell == room)
			Rooms.Remove(room.Cell);

		var bundledPath = BundledRoomPath(room.Id);
		var hadBundledRoom = File.Exists(bundledPath);
		DeleteRoomFile(room.SourcePath, room.Id);
		if (hadBundledRoom && !string.Equals(room.SourcePath, bundledPath, StringComparison.Ordinal))
			WriteUserRoomTombstone(room.Id);

		return true;
	}

	private static string GetRoomSavePath(Room room)
	{
		if (room.Storage == Room.StorageKind.User)
			return UserRoomPath(room.Id);

		var bundledPath = BundledRoomPath(room.Id);
		if (CanWriteBundledRooms())
			return bundledPath;

		return UserRoomPath(room.Id);
	}

	private static string BundledRoomPath(string id)
		=> Path.Join(WorldRoomsPath, $"{id}.json");

	private static string UserRoomPath(string id)
	{
		if (userRoomsPath == null)
			throw new InvalidOperationException("User room path has not been configured.");

		return Path.Join(userRoomsPath, currentWorldId, $"{id}.json");
	}

	private static bool IsUserRoomPath(string path)
	{
		if (UserWorldRoomsPath is not { } userWorldRoomsPath)
			return false;

		var relative = Path.GetRelativePath(Path.GetFullPath(userWorldRoomsPath), Path.GetFullPath(path));
		return relative != ".." && !relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal);
	}

	private static bool RoomFileExists(string id)
	{
		if (File.Exists(BundledRoomPath(id)))
			return true;
		if (userRoomsPath != null && File.Exists(UserRoomPath(id)))
			return true;
		return false;
	}

	private static void DeleteRoomFile(string? path, string id)
	{
		if (path == null || !File.Exists(path))
			return;

		if (CanDeleteFile(path))
			File.Delete(path);
		else if (!IsUserRoomPath(path))
			WriteUserRoomTombstone(id);
	}

	private static bool CanDeleteFile(string path)
	{
		try
		{
			using (File.Open(path, FileMode.Open, FileAccess.Write))
			{
			}
			return true;
		}
		catch
		{
			return false;
		}
	}

	private static bool CanWriteBundledRooms()
	{
		if (bundledRoomsWritable is { } cached)
			return cached;

		try
		{
			Directory.CreateDirectory(WorldRoomsPath);
			var testPath = Path.Join(WorldRoomsPath, $".write-test-{Guid.NewGuid():N}");
			File.WriteAllText(testPath, string.Empty);
			File.Delete(testPath);
			bundledRoomsWritable = true;
		}
		catch
		{
			bundledRoomsWritable = false;
		}

		return bundledRoomsWritable.Value;
	}

	private static HashSet<string> LoadUserRoomTombstones()
	{
		var deleted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		var path = UserDeletedRoomsPath();
		if (path == null || !Directory.Exists(path))
			return deleted;

		foreach (var file in Directory.EnumerateFiles(path))
			deleted.Add(Path.GetFileName(file));

		return deleted;
	}

	private static void WriteUserRoomTombstone(string id)
	{
		var path = UserDeletedRoomsPath();
		if (path == null)
			return;

		Directory.CreateDirectory(path);
		File.WriteAllText(Path.Join(path, id), string.Empty);
	}

	private static void ClearUserRoomTombstone(string id)
	{
		var path = UserDeletedRoomsPath();
		if (path == null)
			return;

		var file = Path.Join(path, id);
		if (File.Exists(file))
			File.Delete(file);
	}

	private static string? UserDeletedRoomsPath()
		=> userRoomsPath != null ? Path.Join(userRoomsPath, currentWorldId, deletedRoomsFolderName) : null;

	public static void RebuildRoomGeometry()
	{
		foreach (var room in Rooms.Values)
			room.RebuildGeometry();
	}

	private static Image GenerateNormalMap(Image source)
	{
		var normal = new Image(source.Width, source.Height);
		const float bend = 1.25f;

		for (var y = 0; y < source.Height; y++)
			for (var x = 0; x < source.Width; x++)
			{
				var alpha = AlphaAt(source, x, y);
				if (alpha <= 0)
					continue;

				var sx = AlphaAt(source, x - 1, y) - AlphaAt(source, x + 1, y);
				var sy = AlphaAt(source, x, y - 1) - AlphaAt(source, x, y + 1);
				var n = Vector3.Normalize(new Vector3(sx * bend, sy * bend, 1));
				normal[x, y] = new Color(
					n.X * 0.5f + 0.5f,
					n.Y * 0.5f + 0.5f,
					n.Z * 0.5f + 0.5f,
					alpha);
			}

		return normal;
	}

	private static Image ExtractTile(Image source, RectInt sourceRect)
	{
		var output = new Image(sourceRect.Width, sourceRect.Height);
		for (var y = 0; y < sourceRect.Height; y++)
			for (var x = 0; x < sourceRect.Width; x++)
				output[x, y] = source[sourceRect.X + x, sourceRect.Y + y];
		return output;
	}

	private static Image GenerateSlopeTile(Image source, Room.SlopeDirection direction, Room.SlopeFill fill, int runLength, int runIndex, Image? edge = null)
	{
		var output = new Image(source.Width, source.Height);

		for (var y = 0; y < source.Height; y++)
			for (var x = 0; x < source.Width; x++)
			{
				if (!IsSlopeMaskSolid(x, y, direction, fill, runLength, runIndex))
				{
					output[x, y] = Color.Transparent;
					continue;
				}

				output[x, y] = edge != null && IsSlopeMaskEdge(x, y, direction, fill, runLength, runIndex)
					? edge[x, y]
					: source[x, y];
			}

		return output;
	}

	private static bool IsSlopeMaskSolid(int x, int y, Room.SlopeDirection direction, Room.SlopeFill fill, int runLength, int runIndex)
	{
		if (x < 0 || y < 0 || x >= Game.TileSize || y >= Game.TileSize)
			return false;

		var totalWidth = runLength * Game.TileSize;
		var runX = runIndex * Game.TileSize + x + 0.5f;
		var t = Math.Clamp(runX / totalWidth, 0, 1);
		var surface = direction == Room.SlopeDirection.DownRight
			? t * Game.TileSize
			: (1 - t) * Game.TileSize;
		return fill == Room.SlopeFill.BelowLine
			? y + 0.5f >= surface
			: y + 0.5f <= surface;
	}

	private static bool IsSlopeMaskEdge(int x, int y, Room.SlopeDirection direction, Room.SlopeFill fill, int runLength, int runIndex)
	{
		const int edgeWidth = 2;
		for (var oy = -edgeWidth; oy <= edgeWidth; oy++)
			for (var ox = -edgeWidth; ox <= edgeWidth; ox++)
			{
				if (Math.Abs(ox) + Math.Abs(oy) > edgeWidth)
					continue;

				var sampleX = x + ox;
				var sampleY = y + oy;
				if (sampleX < 0 || sampleY < 0 || sampleX >= Game.TileSize || sampleY >= Game.TileSize)
					continue;

				if (!IsSlopeMaskSolid(sampleX, sampleY, direction, fill, runLength, runIndex))
					return true;
			}

		return false;
	}

	private static void AddSlopeSubtextures(Packer packer, string tilesetName, Image baseTile, Image? edge = null)
	{
		for (var run = 1; run <= MaxSlopeRun; run++)
			for (var index = 0; index < run; index++)
			{
				packer.Add(SlopeSubtextureName(tilesetName, Room.SlopeDirection.DownRight, Room.SlopeFill.BelowLine, run, index), GenerateSlopeTile(baseTile, Room.SlopeDirection.DownRight, Room.SlopeFill.BelowLine, run, index, edge));
				packer.Add(SlopeSubtextureName(tilesetName, Room.SlopeDirection.UpRight, Room.SlopeFill.BelowLine, run, index), GenerateSlopeTile(baseTile, Room.SlopeDirection.UpRight, Room.SlopeFill.BelowLine, run, index, edge));
				packer.Add(SlopeSubtextureName(tilesetName, Room.SlopeDirection.DownRight, Room.SlopeFill.AboveLine, run, index), GenerateSlopeTile(baseTile, Room.SlopeDirection.DownRight, Room.SlopeFill.AboveLine, run, index, edge));
				packer.Add(SlopeSubtextureName(tilesetName, Room.SlopeDirection.UpRight, Room.SlopeFill.AboveLine, run, index), GenerateSlopeTile(baseTile, Room.SlopeDirection.UpRight, Room.SlopeFill.AboveLine, run, index, edge));
			}
	}

	private static void AddSlopeSubtextures(Packer packer, string tilesetName, Image baseTile, int variantIndex, Image? edge = null)
	{
		for (var run = 1; run <= MaxSlopeRun; run++)
			for (var index = 0; index < run; index++)
			{
				packer.Add(SlopeVariantSubtextureName(tilesetName, variantIndex, Room.SlopeDirection.DownRight, Room.SlopeFill.BelowLine, run, index), GenerateSlopeTile(baseTile, Room.SlopeDirection.DownRight, Room.SlopeFill.BelowLine, run, index, edge));
				packer.Add(SlopeVariantSubtextureName(tilesetName, variantIndex, Room.SlopeDirection.UpRight, Room.SlopeFill.BelowLine, run, index), GenerateSlopeTile(baseTile, Room.SlopeDirection.UpRight, Room.SlopeFill.BelowLine, run, index, edge));
				packer.Add(SlopeVariantSubtextureName(tilesetName, variantIndex, Room.SlopeDirection.DownRight, Room.SlopeFill.AboveLine, run, index), GenerateSlopeTile(baseTile, Room.SlopeDirection.DownRight, Room.SlopeFill.AboveLine, run, index, edge));
				packer.Add(SlopeVariantSubtextureName(tilesetName, variantIndex, Room.SlopeDirection.UpRight, Room.SlopeFill.AboveLine, run, index), GenerateSlopeTile(baseTile, Room.SlopeDirection.UpRight, Room.SlopeFill.AboveLine, run, index, edge));
			}
	}

	private static float AlphaAt(Image image, int x, int y)
	{
		if (x < 0 || y < 0 || x >= image.Width || y >= image.Height)
			return 0;

		return image[x, y].A / 255.0f;
	}

	public static void Unload()
	{
		Atlas?.Dispose();
		Atlas = null;
		LightingNoise?.Dispose();
		LightingNoise = null;
		Font = null;
		SpeechFont = null;

		Sprites.Clear();
		Tilesets.Clear();
		Subtextures.Clear();
		Rooms.Clear();
		RoomsById.Clear();
		ActorSpecs.ClearSpecs();
		ActorSpecs.ClearActorTypes();
	}

	public static Sprite? GetSprite(string name) => Sprites.GetValueOrDefault(name);
	public static Tileset? GetTileset(string name, bool logMissing = false)
	{
		var tileset = Tilesets.GetValueOrDefault(name);
		if (logMissing && tileset == null)
			Console.WriteLine($"Error: Tileset '{name}' not found");
		return tileset;
	}
	public static Room? GetRoom(Point2 cell) => Rooms.GetValueOrDefault(cell);
	public static Room? GetRoom(string id) => RoomsById.GetValueOrDefault(id);

	public static (Room Room, JsonObject Actor)? FindActorById(string id)
	{
		if (string.IsNullOrWhiteSpace(id))
			return null;

		foreach (var room in RoomsById.Values)
			foreach (var actor in room.Actors)
				if (string.Equals(ActorSpecs.ReadString(actor, "Id"), id, StringComparison.OrdinalIgnoreCase))
					return (room, actor);

		return null;
	}

	public static Room? FindAdjacentRoom(Room room, Point2 direction, Vector2 exitPosition)
	{
		var bounds = room.WorldBounds;
		Room? best = null;
		var bestDistance = float.MaxValue;

		foreach (var candidate in RoomsById.Values)
		{
			if (candidate == room)
				continue;

			var candidateBounds = candidate.WorldBounds;
			if (!TouchesExitEdge(bounds, candidateBounds, direction, exitPosition))
				continue;

			var distance = TransitionAxisDistance(candidateBounds, direction, exitPosition);
			if (distance < bestDistance)
			{
				best = candidate;
				bestDistance = distance;
			}
		}

		return best;
	}

	private static bool TouchesExitEdge(RectInt from, RectInt to, Point2 direction, Vector2 exitPosition)
	{
		if (direction.X > 0)
			return to.Left == from.Right && ContainsAxis(to.Top, to.Bottom, exitPosition.Y);
		if (direction.X < 0)
			return to.Right == from.Left && ContainsAxis(to.Top, to.Bottom, exitPosition.Y);
		if (direction.Y > 0)
			return to.Top == from.Bottom && ContainsAxis(to.Left, to.Right, exitPosition.X);
		if (direction.Y < 0)
			return to.Bottom == from.Top && ContainsAxis(to.Left, to.Right, exitPosition.X);

		return false;
	}

	private static bool ContainsAxis(float min, float max, float value)
		=> value >= min && value < max;

	private static float TransitionAxisDistance(RectInt bounds, Point2 direction, Vector2 exitPosition)
	{
		if (direction.X != 0)
			return MathF.Abs(bounds.CenterY - exitPosition.Y);

		return MathF.Abs(bounds.CenterX - exitPosition.X);
	}

	public static Subtexture GetSubtexture(string name)
	{
		return Subtextures.TryGetValue(name, out var value) ? value : new();
	}

	public static Subtexture GetSlopeSubtexture(string tilesetName, Room.SlopeDirection direction, Room.SlopeFill fill, int runLength, int runIndex)
	{
		var clampedRun = Math.Clamp(runLength, 1, MaxSlopeRun);
		var clampedIndex = Math.Clamp(runIndex, 0, clampedRun - 1);
		return GetSubtexture(SlopeSubtextureName(tilesetName, direction, fill, clampedRun, clampedIndex));
	}

	public static Subtexture GetSlopeSubtexture(string tilesetName, Room.SlopeDirection direction, Room.SlopeFill fill, int runLength, int runIndex, int seed)
	{
		var clampedRun = Math.Clamp(runLength, 1, MaxSlopeRun);
		var clampedIndex = Math.Clamp(runIndex, 0, clampedRun - 1);

		if (GetTileset(tilesetName) is { Variants.Count: > 0 } tileset)
		{
			var variantIndex = (seed & int.MaxValue) % tileset.Variants.Count;
			var variant = GetSubtexture(SlopeVariantSubtextureName(tilesetName, variantIndex, direction, fill, clampedRun, clampedIndex));
			if (variant.Texture != null)
				return variant;
		}

		return GetSubtexture(SlopeSubtextureName(tilesetName, direction, fill, clampedRun, clampedIndex));
	}

	private static string SlopeSubtextureName(string tilesetName, Room.SlopeDirection direction, Room.SlopeFill fill, int runLength, int runIndex)
		=> $"slopes/{tilesetName}/{direction}/{fill}/{runLength}/{runIndex}";

	private static string SlopeVariantSubtextureName(string tilesetName, int variantIndex, Room.SlopeDirection direction, Room.SlopeFill fill, int runLength, int runIndex)
		=> $"slopes/{tilesetName}/variants/{variantIndex}/{direction}/{fill}/{runLength}/{runIndex}";

}
