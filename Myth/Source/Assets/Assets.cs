
using System.Text;
using System.Numerics;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Foster.Framework;

namespace Myth;


public static class TileQuadrants
{
	public const int TopLeft = 1;
	public const int BottomLeft = 2;
	public const int TopRight = 4;
	public const int BottomRight = 8;
	public const int All = TopLeft | BottomLeft | TopRight | BottomRight;
}

public static partial class Assets
{
	public const int MaxSlopeRun = 10;

	public static SpriteFont? Font { get; private set; }
	public static SpriteFont? SpeechFont { get; private set; }
	public static Texture? Atlas { get; private set; }
	public static Texture? LightingNoise { get; private set; }
	public static readonly Dictionary<string, Sprite> Sprites = new(StringComparer.OrdinalIgnoreCase);
	public static readonly Dictionary<string, Tileset> Tilesets = [];
	public static readonly Dictionary<string, Subtexture> Subtextures = [];
	public static readonly Dictionary<(int Sublayer, Point2 Cell), Room> Rooms = [];
	public static readonly Dictionary<string, Room> RoomsById = [];
	public static event Action<Room, string>? RoomSaveConflict;
	public static event Action<string>? RoomFileDeleted;

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
	static readonly Dictionary<(string Sprite, string Tag), int> InbetweenAnimations = new();

	// Transition in-betweens whitelist: (sprite, from, to) -> frame count.
	// This is the source of truth for baking transitions.
	static readonly Dictionary<(string Sprite, string From, string To), int> InbetweenTransitions = new();

	public static string INBETWEENS_JSON_FILENAME => Path.Join(SourcesPath, "inbetweens.json");

	// Populated during the visual bake and serialized into sprite animations.
	public static readonly Dictionary<string, List<GeneratedSpriteTransition>> GeneratedTransitions = new();

	// Expanded frame durations for sprites with inbetweens
	static readonly Dictionary<string, float[]> ExpandedFrameDurations = new();

	// Maps original frame index -> index in the expanded (in-betweened) timeline,
	// for sprites that had within-animation in-betweens baked. Replaces the old
	// uniform "tag.From * (count + 1)" math now that counts vary per tag.
	static readonly Dictionary<string, int[]> ExpandedFrameMap = new();

	const string assetsFolderName = "Assets";
	const string roomsFolderName = "Rooms";
	const string packedVisualsFolderName = "Packed/Visuals";
	const string packedVisualsManifestName = "manifest.bin";
	const uint visualAssetPackMagic = 0x5356494D; // MIVS, little-endian.
	const int visualAssetPackVersion = 13; // v13: browser PNG decode, surface textures packed PNG
	const string socketLayerSuffix = "_socket";
	const string defaultWorldId = "Myth";
	const string defaultStartRoomId = "start";
	const string deletedRoomsFolderName = ".deleted";
	static string? path = null;
	static string? userRoomsPath = null;
	static string? visualAssetPackPath = null;
	static bool useVisualAssetPack = false;
	static bool? bundledRoomsWritable = null;
	static string currentWorldId = defaultWorldId;

	public static string AssetsPath
	{
		get
		{
			// During development we search up from the working directory to find the Assets
			// folder (instead of copying all the Assets to the build directory). A bundled
			// app (Myth.app launched from Finder) starts with cwd=/, so fall back to probing
			// up from the executable's directory, where the bundle carries Assets.
			path ??= FindAssetsFrom(Environment.CurrentDirectory)
				?? FindAssetsFrom(AppContext.BaseDirectory)
				?? throw new Exception("Unable to find Assets path");
			return path;
		}
	}

	static string? FindAssetsFrom(string start)
	{
		var directory = Path.GetFullPath(start);
		for (var depth = 0; depth < 10 && directory != null; depth++)
		{
			var candidate = Path.Join(directory, assetsFolderName);
			if (Directory.Exists(candidate))
				return candidate;
			// macOS bundle layout: the executable lives in Contents/MacOS while data must
			// sit in Contents/Resources (codesign rejects non-code under MacOS).
			var resources = Path.Join(directory, "Resources", assetsFolderName);
			if (Directory.Exists(resources))
				return resources;
			directory = Path.GetDirectoryName(directory);
		}
		return null;
	}

	public static string RoomsPath => Path.Join(AssetsPath, roomsFolderName);
	// Authoring inputs (.ase sources, kit specs, bake config). Read at runtime in dev and
	// by the pack bake; excluded wholesale from web bundles, where the pack replaces them.
	public static string SourcesPath => Path.Join(AssetsPath, "Sources");

	const string contentZipName = "Content.zip";
	static StorageContainer? content;
	static string? contentZipOverride;

	/// <summary>
	/// Read-only bundled content, addressed by Assets-relative keys ("Rooms/Myth/x.json").
	/// Packaged builds read Assets/Content.zip (the Assets folder then carries only the zip
	/// plus loose FMOD banks); dev and editor builds read the Assets folder directly, so
	/// authoring and file watching keep working. Writable flows (user rooms, editor room
	/// saves, pack bakes) never go through here — they use the real file system.
	/// </summary>
	public static StorageContainer Content => content ??= OpenContent();

	/// True when bundled content is being served from Content.zip (always read-only).
	/// Resolves Content first, so it is correct however early it is read.
	public static bool ContentIsZip
	{
		get
		{
			_ = Content;
			return contentIsZip;
		}
	}

	static bool contentIsZip;

	/// Forces content to load from the given zip (the --content-zip flag), including in
	/// editor builds, which otherwise ignore Content.zip so a stale pack can't shadow the
	/// files being edited.
	public static void ConfigureContentZip(string? zipPath)
	{
		contentZipOverride = string.IsNullOrWhiteSpace(zipPath) ? null : zipPath;
		content?.Dispose();
		content = null;
	}

	/// <summary>
	/// Zips the runtime-loaded slice of the Assets folder into a Content.zip for packaged
	/// builds. Keeps only the requested world and shader backend. Excluded: Sources/
	/// (authoring; the visual pack replaces it), Sounds/ (FMOD opens banks by real path, so
	/// banks ship as loose files), editor fonts, uncompiled shaders, and dot-prefixed litter.
	/// Entry names are Assets-relative with forward slashes — the same keys Content serves.
	/// </summary>
	public static void BuildContentZip(string outputPath, string target, string? worldId = null)
	{
		var manifestPath = Path.Join(DefaultVisualAssetPackPath, packedVisualsManifestName);
		if (!File.Exists(manifestPath))
			throw new InvalidOperationException("Visual asset pack is missing. Build it with --build-asset-pack before building Content.zip.");
		using (var reader = new BinaryReader(File.OpenRead(manifestPath)))
		{
			if (reader.BaseStream.Length < sizeof(uint) + sizeof(int) || reader.ReadUInt32() != visualAssetPackMagic)
				throw new InvalidOperationException("Visual asset pack manifest is invalid. Rebuild it with --build-asset-pack before building Content.zip.");
			var version = reader.ReadInt32();
			if (version != visualAssetPackVersion)
				throw new InvalidOperationException($"Visual asset pack is stale. Expected version {visualAssetPackVersion}, found {version}. Rebuild it with --build-asset-pack before building Content.zip.");
		}

		var shaderExtension = target.Trim().ToLowerInvariant() switch
		{
			"mac" => "msl",
			"ios" => "msl",
			"windows" => "dxil",
			"linux" => "spv",
			"browser" => "wgsl",
			_ => throw new ArgumentException($"Unknown content target '{target}'; expected mac, ios, windows, linux, or browser."),
		};
		worldId = string.IsNullOrWhiteSpace(worldId) ? defaultWorldId : worldId.Trim();
		if (worldId.IndexOfAny(['/', '\\']) >= 0 || !Directory.Exists(Path.Join(RoomsPath, worldId)))
			throw new ArgumentException($"Unknown content world '{worldId}'.");

		outputPath = Path.GetFullPath(outputPath);
		if (Path.GetDirectoryName(outputPath) is { Length: > 0 } outputDirectory)
			Directory.CreateDirectory(outputDirectory);

		// The DynamicTexture folder holds tens of MB of NCA weights, almost all unused; a
		// packaged build only needs models referenced by rooms or registered NCT tile specs.
		var referencedWeights = CollectReferencedNeuralTextureWeights(worldId);
		var packedWeights = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

		// Only the surfaces (and their textures) referenced by the shipped world's rooms.
		CollectReferencedSurfaceAssets(worldId, out var referencedSurfaces, out var referencedTextures);
		ValidateReferencedSurfaceAssets(referencedSurfaces, referencedTextures);
		var skippedSurfaceAssets = 0;
		long skippedSurfaceBytes = 0;

		using var stream = File.Create(outputPath);
		using var zip = new System.IO.Compression.ZipArchive(stream, System.IO.Compression.ZipArchiveMode.Create);

		var count = 0;
		var skippedWeights = 0;
		long skippedWeightBytes = 0;
		foreach (var file in Directory.EnumerateFiles(AssetsPath, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
		{
			var key = Path.GetRelativePath(AssetsPath, file).Replace(Path.DirectorySeparatorChar, '/');
			if (!ContentZipIncludes(key, shaderExtension, worldId))
				continue;

			if ((IsPackSurfaceFile(key, out var surfaceId) && !referencedSurfaces.Contains(surfaceId))
				|| (IsPackTextureFile(key, out var textureKey) && !referencedTextures.Contains(textureKey)))
			{
				skippedSurfaceAssets++;
				skippedSurfaceBytes += new FileInfo(file).Length;
				continue;
			}

			if (IsDynamicTextureWeight(key, out var weightName))
			{
				if (!referencedWeights.Contains(weightName))
				{
					skippedWeights++;
					skippedWeightBytes += new FileInfo(file).Length;
					continue;
				}
				if (IsGitLfsPointer(file))
					throw new InvalidOperationException($"Neural texture model '{key}' is still a Git LFS pointer. Run 'git lfs pull' before building content.");
				packedWeights.Add(weightName);
			}

			// PNGs are already compressed; storing them keeps reads cheap. Everything else
			// (JSON, shader text, QOI, weights) deflates well.
			var level = key.EndsWith(".png", StringComparison.OrdinalIgnoreCase)
				? System.IO.Compression.CompressionLevel.NoCompression
				: System.IO.Compression.CompressionLevel.Optimal;
			System.IO.Compression.ZipFileExtensions.CreateEntryFromFile(zip, file, key, level);
			count++;
		}

		var missingWeights = referencedWeights.Where(name => !packedWeights.Contains(name)).ToArray();
		if (missingWeights.Length > 0)
			throw new InvalidOperationException($"Missing referenced neural texture models: {string.Join(", ", missingWeights)}. Run 'git lfs pull' and check the room/NCT spec references.");

		Console.WriteLine($"Wrote {target}/{worldId} content zip: {outputPath} ({count} entries, {new FileInfo(outputPath).Length / 1024} KB); "
			+ $"packed {packedWeights.Count} neural texture models, skipped {skippedWeights} unreferenced ({skippedWeightBytes / 1024} KB); "
			+ $"surfaces: kept {referencedSurfaces.Count} + {referencedTextures.Count} textures, skipped {skippedSurfaceAssets} unreferenced ({skippedSurfaceBytes / 1024} KB).");
	}

	// Surfaces referenced by actors in the shipped world, and the texture keys those
	// surfaces use. Surface specs are read from the baked pack (their shipping location).
	static void CollectReferencedSurfaceAssets(string worldId, out HashSet<string> surfaces, out HashSet<string> textures)
	{
		surfaces = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		textures = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

		var worldRoomsPath = Path.Join(RoomsPath, worldId);
		if (Directory.Exists(worldRoomsPath))
			foreach (var file in Directory.EnumerateFiles(worldRoomsPath, "*.json", SearchOption.AllDirectories))
			{
				JsonNode? root;
				try { root = JsonNode.Parse(File.ReadAllText(file)); }
				catch (Exception e)
				{
					Console.WriteLine($"Warning: could not parse room '{file}' collecting surface references ({e.Message}); surfaces it uses may be dropped.");
					continue;
				}
				if (root?["backdropSurface"]?.GetValue<string>() is { Length: > 0 } backdropSurface)
					surfaces.Add(backdropSurface);
				if (root?["actors"] is JsonArray actors)
					foreach (var actor in actors)
					{
						if (actor?["SurfaceId"]?.GetValue<string>() is { Length: > 0 } id)
							surfaces.Add(id);
						if (actor is JsonObject proceduralWall && ActorSpecs.GetTypeName(proceduralWall) is { } wallTypeName &&
							ActorSpecs.GetActorType(wallTypeName) is { } wallType && typeof(ProceduralWall).IsAssignableFrom(wallType))
						{
							var defaultPattern = wallType == typeof(HyphaWall) ? "patterns/pattern_68" :
								wallType == typeof(StrataWall) ? "patterns/pattern_37" :
								wallType == typeof(CircuitWall) ? "patterns/pattern_12" :
								wallType == typeof(ArchiveWall) ? "patterns/pattern_57" :
								wallType == typeof(FleshWall) ? "patterns/pattern_34" :
								wallType == typeof(RuinsWall) ? "patterns/pattern_68" : ProceduralWallRenderer.DefaultPatternTexture;
							var defaultDetail = wallType == typeof(HyphaWall) ? "patterns/noise/Noise_036" :
								wallType == typeof(StrataWall) ? "patterns/noise/Noise_022" :
								wallType == typeof(CircuitWall) ? "patterns/noise/Noise_003" :
								wallType == typeof(ArchiveWall) ? "patterns/noise/Noise_041" :
								wallType == typeof(FleshWall) ? "patterns/noise/Noise_069" :
								wallType == typeof(RuinsWall) ? "patterns/noise/Noise_055" : ProceduralWallRenderer.DefaultDetailTexture;
							textures.Add(ActorSpecs.ReadResolvedString(proceduralWall, nameof(ProceduralWall.PatternTexture)) is { Length: > 0 } pattern
								? pattern : defaultPattern);
							textures.Add(ActorSpecs.ReadResolvedString(proceduralWall, nameof(ProceduralWall.DetailTexture)) is { Length: > 0 } detail
								? detail : defaultDetail);
						}
					}
			}

		var surfacesDir = Path.Join(DefaultVisualAssetPackPath, "surfaces");
		foreach (var id in surfaces)
		{
			var specPath = Path.Join(surfacesDir, $"{id}.json");
			if (!File.Exists(specPath))
				continue;
			try
			{
				foreach (var key in SurfaceSpec.Parse(File.ReadAllText(specPath), id).ReferencedTextures())
					textures.Add(key);
			}
			catch (Exception e)
			{
				Console.WriteLine($"Warning: could not parse surface '{specPath}' ({e.Message}); its textures may be dropped.");
			}
		}
	}

	static void ValidateReferencedSurfaceAssets(HashSet<string> surfaces, HashSet<string> textures)
	{
		var surfacesDir = Path.Join(DefaultVisualAssetPackPath, "surfaces");
		foreach (var id in surfaces)
			if (!File.Exists(Path.Join(surfacesDir, $"{id}.json")))
				throw new InvalidOperationException($"Missing referenced surface '{id}'. Rebuild the visual asset pack and check room SurfaceId references.");

		var texturesDir = Path.Join(DefaultVisualAssetPackPath, "textures");
		foreach (var key in textures)
			if (!File.Exists(Path.Join(texturesDir, key + ".png")))
				throw new InvalidOperationException($"Missing texture '{key}' referenced by a shipped surface. Rebuild the visual asset pack and check the Surface spec.");
	}

	// key = "Packed/Visuals/surfaces/<id>.json" -> id
	static bool IsPackSurfaceFile(string key, out string id)
	{
		id = "";
		var prefix = $"{packedVisualsFolderName}/surfaces/";
		if (!key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) || !key.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
			return false;
		id = key[prefix.Length..^".json".Length];
		return true;
	}

	// key = "Packed/Visuals/textures/<textureKey>.png" -> textureKey (e.g. "patterns/pattern_07")
	static bool IsPackTextureFile(string key, out string textureKey)
	{
		textureKey = "";
		var prefix = $"{packedVisualsFolderName}/textures/";
		if (!key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) || !key.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
			return false;
		textureKey = key[prefix.Length..^".png".Length];
		return true;
	}

	// True for a key naming a DynamicTexture NCA weight file, yielding its folder-relative name
	// (the same string a NeuralTextureActor stores in WeightsFile). Files sit directly in the
	// folder, so the name is just the basename.
	static bool IsDynamicTextureWeight(string key, out string name)
	{
		name = "";
		const string prefix = NeuralTextureActor.WeightsFolder + "/";
		if (!key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ||
			!key.EndsWith(".bin", StringComparison.OrdinalIgnoreCase))
			return false;
		name = key[prefix.Length..];
		return true;
	}

	static bool IsGitLfsPointer(string path)
	{
		const string signature = "version https://git-lfs.github.com/spec/v1";
		Span<byte> buffer = stackalloc byte[signature.Length];
		using var stream = File.OpenRead(path);
		return stream.Read(buffer) == buffer.Length && Encoding.ASCII.GetString(buffer) == signature;
	}

	// Weight files named by NCT tile specs and NeuralTextureActor instances in the shipped world,
	// seeded with the constructor default so a freshly placed actor still resolves.
	// Case-insensitive to match the zip's own lookups. Room references come from loose files
	// because this only runs while building the pack.
	static HashSet<string> CollectReferencedNeuralTextureWeights(string worldId)
	{
		var referenced = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
		{
			NeuralTextureActor.DefaultWeightsFile,
		};
		foreach (var spec in NctTiles.Specs)
		{
			referenced.Add(spec.Interior);
			if (!string.IsNullOrEmpty(spec.Edge))
				referenced.Add(spec.Edge);
		}

		var worldRoomsPath = Path.Join(RoomsPath, worldId);
		if (!Directory.Exists(worldRoomsPath))
			return referenced;

		string[] fields = ["WeightsFile", "WeightsFile1", "WeightsFile2", "WeightsFile3"];
		foreach (var file in Directory.EnumerateFiles(worldRoomsPath, "*.json", SearchOption.AllDirectories))
		{
			JsonNode? root;
			try
			{
				root = JsonNode.Parse(File.ReadAllText(file));
			}
			catch (Exception exception)
			{
				// A room we can't parse might reference anything, so we can't prove a model is
				// unused. Warn loudly — these are machine-written files, so this signals real
				// corruption — but keep going.
				Console.WriteLine($"Warning: could not parse room '{file}' while collecting neural texture references ({exception.Message}); models it uses may be dropped from the pack.");
				continue;
			}

			if (root?["actors"] is not JsonArray actors)
				continue;

			foreach (var actor in actors)
			{
				if (actor?["Type"]?.GetValue<string>() != nameof(NeuralTextureActor))
					continue;
				foreach (var field in fields)
					if (actor[field]?.GetValue<string>() is { Length: > 0 } value)
						referenced.Add(value);
			}
		}

		return referenced;
	}

	static bool ContentZipIncludes(string key, string shaderExtension, string worldId)
	{
		foreach (var part in key.Split('/'))
			if (part.StartsWith('.'))
				return false;

		if (key.StartsWith("Sources/", StringComparison.OrdinalIgnoreCase))
			return false;
		if (key.StartsWith("Sounds/", StringComparison.OrdinalIgnoreCase))
			return false;
		if (key.StartsWith("Rooms/", StringComparison.OrdinalIgnoreCase))
			return key.StartsWith($"Rooms/{worldId}/", StringComparison.OrdinalIgnoreCase)
				|| key.Equals($"Rooms/{worldId}.regions.json", StringComparison.OrdinalIgnoreCase);
		if (key.Equals("Fonts/Myna-Regular.ttf", StringComparison.OrdinalIgnoreCase))
			return false;
		if (key.StartsWith("Shaders/", StringComparison.OrdinalIgnoreCase))
			return key.StartsWith("Shaders/Compiled/", StringComparison.OrdinalIgnoreCase)
				&& key.EndsWith($".{shaderExtension}", StringComparison.OrdinalIgnoreCase);
		if (key.Equals(contentZipName, StringComparison.OrdinalIgnoreCase))
			return false;

		return true;
	}

	static StorageContainer OpenContent()
	{
		var zipPath = contentZipOverride;
		if (zipPath == null)
		{
			// Packaged builds (desktop .app, iOS bundle, browser wasm FS) carry
			// Assets/Content.zip and no loose Rooms folder. A dev tree always has Rooms/,
			// so it keeps reading live files even when a stale zip is lying around; a zip
			// can still be forced there with --content-zip.
			var bundledZip = Path.Join(AssetsPath, contentZipName);
			if (File.Exists(bundledZip) && !Directory.Exists(RoomsPath))
				zipPath = bundledZip;
		}
		if (zipPath != null)
		{
			var fullPath = Path.GetFullPath(zipPath);
			contentIsZip = true;
			Console.WriteLine($"Bundled content: zip ({fullPath})");
			// Case-insensitive lookups match dev-mode behavior on macOS/Windows, where the
			// filesystem forgives casing mismatches that would otherwise surface only in
			// packaged builds.
			return new ZipStorage(() => File.OpenRead(fullPath), ignoreCase: true);
		}

		contentIsZip = false;
		Console.WriteLine($"Bundled content: filesystem ({AssetsPath})");
		return new FolderStorage(AssetsPath);
	}
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
		if (!Content.DirectoryExists(roomsFolderName))
			return Array.Empty<string>();

		return Content.EnumerateDirectory(roomsFolderName)
			.Where(Content.DirectoryExists)
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
		var directory = path ?? DefaultVisualAssetPackPath;
		WriteVisualAssetPack(directory, bake);
		ReportWorldMapState(directory);
	}

	/// The world map is a GPU render of every room, so this build (which runs headless, with no
	/// graphics device) can only leave alone whatever a previous in-app bake wrote. It is written
	/// into the pack folder and never deleted by a rebuild, so a rebuilt pack keeps its map.
	static void ReportWorldMapState(string directory)
	{
		var hasMap = File.Exists(Path.Join(directory, WorldMap.ImageFile))
			&& File.Exists(Path.Join(directory, WorldMap.BoundsFile));
		Console.WriteLine(hasMap
			? $"World map: kept existing {WorldMap.ImageFile} (rebake it in the editor if rooms changed)."
			: $"World map: none in {directory}. Bake it from the editor toolbar (Bake Map).");
	}

	public static void BuildVisualAssetPack(GraphicsDevice gfx, string? path = null, bool dumpPackedAtlases = false)
		=> BuildVisualAssetPack(path, dumpPackedAtlases);

	public static void ReloadRooms()
		=> LoadRooms();

	public static void ReloadWorldMap(GraphicsDevice gfx)
		=> LoadWorldMap(gfx);

	static void Reload(GraphicsDevice gfx, bool reloadRooms, bool dumpPackedAtlases, string? buildPackPath)
	{
		// Kit specs load on both paths: the visual-asset-pack path skips image generation
		// (the baked tilesets are in the pack) but Factory still registers from the specs.
		TileKits.LoadSpecs(Path.Join(SourcesPath, "Tilesets"));
		NctTiles.LoadSpecs(Path.Join(SourcesPath, "Tilesets"));

		// Shipped bundles carry the pack and no Assets/Sources (like the browser), so a
		// missing sources tree implies pack mode without needing a command-line flag.
		// Zip content always implies pack mode: the zip bundles the pack, never sources.
		var packOnly = useVisualAssetPack || OperatingSystem.IsBrowser() || ContentIsZip || !Directory.Exists(SourcesPath);
		if (buildPackPath == null && packOnly && TryLoadVisualAssetPack(gfx, reloadRooms))
			return;

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
		var lightingNoise = Assets.LoadAsepriteTexture(gfx, Path.Join("Sources", "Textures", "noise_tess2_seamless.ase"), "LightingNoise");
		var textures2D = Assets.LoadTextureBucket(gfx, Content, "Sources/Textures/Patterns", "patterns");
		LoadVisualAssetPackManifest(atlas, lightingNoise, textures2D, BuildVisualAssetPackManifest(bake, "", null));
		BuildSurfaces(gfx, Content, "Sources/Surfaces");

		if (buildPackPath != null)
			WriteVisualAssetPack(buildPackPath, bake);

		ActorSpecs.LoadSpecs();
		ParticleEffectAssets.Load();
		if (reloadRooms)
			LoadRooms();
		LoadWorldMap(gfx);
	}

	static VisualAssetBake BakeVisualAssets()
	{
		var spritesPath = Path.Join(SourcesPath, "Sprites");
		var tilesetsPath = Path.Join(SourcesPath, "Tilesets");

		// The bake can be reached without Reload (BuildVisualAssetPack), and generating the
		// kit tilesets needs the specs; reloading them here is cheap and idempotent.
		TileKits.LoadSpecs(tilesetsPath);
		NctTiles.LoadSpecs(tilesetsPath);
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
			var name = Path.ChangeExtension(Path.GetRelativePath(spritesPath, file), null)!.Replace(Path.DirectorySeparatorChar, '/');
			var ase = new Aseprite(file);
			if (ase.Frames.Length > 0)
				spriteFiles.Add(name, ase);
		}

		if (!spriteFiles.ContainsKey("bud") && spriteFiles.TryGetValue("player", out var playerSprite))
			generatedSprites.Add(("bud", playerSprite, CreateSwordlessPlayerLayerFilter));

		foreach (var file in EnumerateAsepriteFiles(tilesetsPath))
		{
			var name = Path.ChangeExtension(Path.GetRelativePath(tilesetsPath, file), null)!.Replace(Path.DirectorySeparatorChar, '/');
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

		foreach (var spec in TileKits.Specs)
		{
			if (tilesetFiles.ContainsKey(spec.Name) || kitGeneratedTilesets.Contains(spec.Name))
				throw new InvalidOperationException($"Tile kit '{spec.Name}' collides with an existing tileset of the same name.");
			if (!tilesetFiles.TryGetValue(spec.MaskKit, out var maskKit))
				throw new InvalidOperationException($"Tile kit '{spec.Name}' references missing mask kit '{spec.MaskKit}'.");

			GenerateDualTilesetFromSpec(spec, maskKit, out var baseTiles, out var baseSlopeEdges, out var dualVariants, out var dualNormals, out var dualOccluders);
			generatedTileImages[spec.Name] = baseTiles;
			generatedTileImages[spec.DualName] = dualVariants;
			generatedNormalImages[spec.DualName] = dualNormals;
			generatedOccluders[spec.DualName] = dualOccluders;
			if (baseSlopeEdges.Count > 0)
				generatedSlopeEdgeImages[spec.Name] = baseSlopeEdges;
			kitGeneratedTilesets.Add(spec.Name);
			kitGeneratedTilesets.Add(spec.DualName);
		}

		var packer = new Packer();

		foreach (var (name, ase) in spriteFiles)
		{
			var (frames, normals, heights, emissives) = RenderSpriteFrames(ase, name == "player" ? CreateSwordlessPlayerLayerFilter() : null);
			PackSpriteFrames(name, ase, frames, normals, heights, emissives, packer);
		}

		foreach (var (name, ase, layerFilter) in generatedSprites)
		{
			var (frames, normals, heights, emissives) = RenderSpriteFrames(ase, layerFilter());
			PackSpriteFrames(name, ase, frames, normals, heights, emissives, packer);
		}

		foreach (var (name, ase) in tilesetFiles)
		{
			if (name.EndsWith("-kit", StringComparison.OrdinalIgnoreCase) || kitGeneratedTilesets.Contains(name))
				continue;

			var nonDebugLayers = GetNonDebugLayers(ase);
			var heightLayers = GetLayerBranch(ase, "height");
			nonDebugLayers.ExceptWith(heightLayers);
			var variantLayers = GetVariantLayers(ase, nonDebugLayers);
			var image = ase.RenderFrame(0, nonDebugLayers.Contains);
			var height = heightLayers.Count > 0 ? RenderLayerFrame(ase, heightLayers, 0) : null;
			var columns = image.Width / Game.TileSize;
			var rows = image.Height / Game.TileSize;
			var normal = height != null ? GenerateTilesetHeightNormals(name, height, image, columns, rows) : null;

			for (int x = 0; x < columns; x++)
				for (int y = 0; y < rows; y++)
				{
					packer.Add($"tilesets/{name}{x}x{y}", image, new RectInt(x, y, 1, 1) * Game.TileSize);
					if (normal != null)
						packer.Add($"tilesets/{name}/normal/{x}x{y}", normal, new RectInt(x, y, 1, 1) * Game.TileSize);
				}

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

	/// An explicit --asset-pack path reads a pack directory on the real file system;
	/// otherwise the pack lives inside bundled content at Packed/Visuals.
	static StorageContainer OpenVisualAssetPack()
		=> visualAssetPackPath != null
			? new FolderStorage(visualAssetPackPath)
			: new RelativeStorage(Content, packedVisualsFolderName);

	/// The baked world map ships beside the pack on both paths (dev builds read the same
	/// folder the pack is written to), so it loads the same way whether or not pack mode is on.
	static void LoadWorldMap(GraphicsDevice gfx)
	{
		using var pack = OpenVisualAssetPack();
		WorldMap.Load(gfx, pack);
	}

	static bool TryLoadVisualAssetPack(GraphicsDevice gfx, bool reloadRooms)
	{
		using var pack = OpenVisualAssetPack();

		if (!pack.FileExists(packedVisualsManifestName))
			return false;

		var manifestLabel = visualAssetPackPath ?? Path.Join(packedVisualsFolderName, packedVisualsManifestName);
		using var manifestStream = new MemoryStream(pack.ReadAllBytes(packedVisualsManifestName));
		var manifest = ReadVisualAssetPackManifest(manifestStream, manifestLabel);

		EnsureFonts(gfx);

		using var atlasImage = new Image(pack.ReadAllBytes(manifest.Atlas));
		var atlas = new Texture(gfx, atlasImage, name: "Atlas");
		Texture? lightingNoise = null;
		if (!string.IsNullOrEmpty(manifest.LightingNoise) && pack.FileExists(manifest.LightingNoise))
		{
			using var lighting = new Image(pack.ReadAllBytes(manifest.LightingNoise));
			lightingNoise = new Texture(gfx, lighting, name: "LightingNoise");
		}

		// Surface textures + specs are baked into the pack under textures/ and surfaces/;
		// their storage layout already encodes the key, so no prefix.
		var textures2D = Assets.LoadTextureBucket(gfx, pack, "textures", "");
		LoadVisualAssetPackManifest(atlas, lightingNoise, textures2D, manifest);
		BuildSurfaces(gfx, pack, "surfaces");
		// Registration data ships inside the pack (baked with the images), so pack mode
		// never depends on spec files being present or current.
		TileKits.SetRegistrations(manifest.TileKits);
		NctTiles.SetSpecs(manifest.NctTiles);

		ActorSpecs.LoadSpecs();
		ParticleEffectAssets.Load();
		if (reloadRooms)
			LoadRooms();
		LoadWorldMap(gfx);

		Console.WriteLine($"Loaded visual asset pack: {manifestLabel}");
		return true;
	}

	static void WriteVisualAssetPack(string directory, VisualAssetBake bake)
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
		var noisePath = Path.Join(SourcesPath, "Textures", "noise_tess2_seamless.ase");
		if (!File.Exists(noisePath))
			throw new SystemException("expected file to exist: " + noisePath);
		var ase = new Aseprite(noisePath);
		if (ase.Frames.Length > 0)
		{
			using var image = ase.RenderFrame(0);
			image.WriteQoi(Path.Join(directory, lightingNoiseFile));
			lightingNoise = lightingNoiseFile;
		}

		WriteSurfaceAssetsToPack(directory);

		var manifestPath = Path.Join(directory, packedVisualsManifestName);
		WriteVisualAssetPackManifest(manifestPath, BuildVisualAssetPackManifest(bake, atlasFile, lightingNoise));
		Console.WriteLine($"Wrote visual asset pack: {manifestPath}");
	}

	// Bakes the direct-PNG texture bucket and Surface specs into the pack so packaged
	// builds have them (Sources/ is not shipped). The whole set goes in; Content.zip
	// prunes to what a world references (see BuildContentZip). Layout mirrors the pack
	// load: textures/<key>.png and surfaces/<id>.json.
	static void WriteSurfaceAssetsToPack(string directory)
	{
		var texturesDir = Path.Join(directory, "textures");
		var surfacesDir = Path.Join(directory, "surfaces");
		if (Directory.Exists(texturesDir))
			Directory.Delete(texturesDir, recursive: true);
		if (Directory.Exists(surfacesDir))
			Directory.Delete(surfacesDir, recursive: true);

		var sourceTextures = Path.Join(SourcesPath, "Textures", "Patterns");
		if (Directory.Exists(sourceTextures))
			foreach (var file in Directory.EnumerateFiles(sourceTextures, "*.png", SearchOption.AllDirectories))
			{
				var rel = Path.GetRelativePath(sourceTextures, file);
				var dest = Path.Join(texturesDir, "patterns", rel);
				Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
				File.Copy(file, dest, overwrite: true);
			}

		var sourceSurfaces = Path.Join(SourcesPath, "Surfaces");
		if (Directory.Exists(sourceSurfaces))
		{
			Directory.CreateDirectory(surfacesDir);
			foreach (var file in Directory.EnumerateFiles(sourceSurfaces, "*.json"))
				File.Copy(file, Path.Join(surfacesDir, Path.GetFileName(file)), overwrite: true);
		}
	}

	static VisualAssetPackManifest BuildVisualAssetPackManifest(VisualAssetBake bake, string atlas, string? lightingNoise)
	{
		var manifest = new VisualAssetPackManifest
		{
			Atlas = atlas,
			LightingNoise = lightingNoise
		};
		manifest.TileKits.AddRange(TileKits.Registrations);
		foreach (var spec in NctTiles.Specs)
			manifest.NctTiles.Add(NctTiles.Clone(spec));

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

	static void LoadVisualAssetPackManifest(Texture atlas, Texture? lightingNoise, TextureBucket? textures2D, VisualAssetPackManifest manifest)
	{
		var oldAtlas = Atlas;
		var oldLightingNoise = LightingNoise;
		var oldTextures2D = Textures2D;
		Atlas = atlas;
		LightingNoise = lightingNoise;
		Textures2D = textures2D;

		Subtextures.Clear();
		foreach (var packed in manifest.Subtextures)
			Subtextures[packed.Name] = packed.ToSubtexture(atlas);

		PackedSpriteLoader.Load(atlas, manifest.Sprites, Sprites);

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
		oldTextures2D?.Dispose();
	}

	static PackedSprite BuildPackedSpriteAsset(VisualAssetBake bake, string name, Aseprite ase)
		=> AsepriteSpriteBaker.BuildPackedSprite(
			name,
			ase,
			bake.Subtexture,
			bake.SubtextureOrDefault,
			ExpandedFrameDurations,
			ExpandedFrameMap,
			GeneratedTransitions,
			BuildSocketPoses);

	static PackedTileset BuildPackedTilesetAsset(VisualAssetBake bake, string name, Aseprite ase)
	{
		var columns = ase.Width / Game.TileSize;
		var rows = ase.Height / Game.TileSize;
		var heightLayers = GetLayerBranch(ase, "height");
		var colorLayers = GetNonDebugLayers(ase);
		colorLayers.ExceptWith(heightLayers);
		var variantLayers = GetVariantLayers(ase, colorLayers);
		var packed = new PackedTileset
		{
			Name = name,
			Columns = columns,
			Rows = rows,
			Tiles = PackTileSubtextures(bake, $"tilesets/{name}", columns, rows)
		};

		for (var variantIndex = 0; variantIndex < variantLayers.Count; variantIndex++)
			packed.Variants.Add(PackTileSubtextures(bake, $"tilesets/{name}/variants/{variantIndex}/", columns, rows));
		if (heightLayers.Count > 0)
			packed.NormalTiles = PackTileSubtextures(bake, $"tilesets/{name}/normal/", columns, rows);
		return packed;
	}

	static PackedTileset BuildPackedGeneratedTilesetAsset(VisualAssetBake bake, string name, List<Image> images)
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

	static List<PackedSubtexture> PackTileSubtextures(VisualAssetBake bake, string prefix, int columns, int rows)
	{
		var result = new List<PackedSubtexture>(columns * rows);
		for (var x = 0; x < columns; x++)
			for (var y = 0; y < rows; y++)
				result.Add(bake.Subtexture($"{prefix}{x}x{y}"));
		return result;
	}

	static void WriteVisualAssetPackManifest(string path, VisualAssetPackManifest manifest)
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
				WritePackedSubtexture(writer, frame.EmissiveSubtexture);
				writer.Write(frame.Duration);
				writer.Write(frame.UserData ?? "");
			}

			WriteCount(writer, sprite.Animations.Count, $"{sprite.Name} animations");
			foreach (var animation in sprite.Animations)
			{
				writer.Write(animation.Name);
				writer.Write(animation.FrameStart);
				writer.Write(animation.FrameCount);
				writer.Write(animation.Duration);
			}

			WriteCount(writer, sprite.Slices.Count, $"{sprite.Name} slices");
			foreach (var (sliceName, bounds) in sprite.Slices)
			{
				writer.Write(sliceName);
				writer.Write(bounds.X);
				writer.Write(bounds.Y);
				writer.Write(bounds.Width);
				writer.Write(bounds.Height);
			}

			WriteCount(writer, sprite.Sockets.Count, $"{sprite.Name} sockets");
			foreach (var (socketName, poses) in sprite.Sockets)
			{
				writer.Write(socketName);
				WriteCount(writer, poses.Count, $"{sprite.Name} {socketName} socket poses");
				foreach (var pose in poses)
				{
					WritePackedVector(writer, pose.Position);
					WritePackedVector(writer, pose.Normal);
				}
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

		WriteCount(writer, manifest.TileKits.Count, "tile kits");
		foreach (var kit in manifest.TileKits)
		{
			writer.Write(kit.Name);
			writer.Write((int)kit.PreferredChar);
			writer.Write((int)kit.SlopePreferredChar);
			writer.Write(kit.Substance);
			writer.Write((int)kit.Layer);
			writer.Write((int)kit.Reflection);
			writer.Write(kit.ReflectionOpacity);
			writer.Write(kit.AcousticReflectivity);
			writer.Write(kit.HasSlope);
		}

		WriteCount(writer, manifest.NctTiles.Count, "NCT tiles");
		foreach (var spec in manifest.NctTiles)
		{
			writer.Write(spec.Name);
			writer.Write((int)spec.PreferredChar);
			writer.Write(spec.Interior);
			writer.Write(spec.Edge);
			writer.Write(spec.EdgeBandCells);
			writer.Write(spec.RoundingPixels);
			writer.Write(spec.OutsetPixels);
			writer.Write(spec.FreezeSteps);
			writer.Write(spec.StepsPerSecond);
			writer.Write(spec.FireRate);
			writer.Write(spec.Contrast);
			writer.Write(spec.Saturation);
			writer.Write(spec.Brightness);
			var tint = spec.Tint.ToVector4();
			writer.Write(tint.X);
			writer.Write(tint.Y);
			writer.Write(tint.Z);
			writer.Write(spec.NormalStrength);
			writer.Write(spec.SilhouetteNormalStrength);
			writer.Write(spec.BlendCurve);
			writer.Write(spec.EdgeWeight);
			writer.Write(spec.DetailScale);
			writer.Write(spec.DetailPhase.X);
			writer.Write(spec.DetailPhase.Y);
			writer.Write(spec.PatchFrequency);
			writer.Write(spec.SirenFrequency);
			writer.Write(spec.LatentGain);
			writer.Write(spec.LatentBias);
			writer.Write(spec.NearestStateSampling);
			writer.Write((int)spec.Layer);
			writer.Write(spec.Solid);
			writer.Write(spec.Substance);
			writer.Write(spec.HasSlope);
		}
	}

	static VisualAssetPackManifest ReadVisualAssetPackManifest(Stream stream, string path)
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
					EmissiveSubtexture = ReadPackedSubtexture(reader),
					Duration = reader.ReadSingle(),
					UserData = reader.ReadString() is { Length: > 0 } text ? text : null
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

			var sliceCount = ReadCount(reader, $"{sprite.Name} slices");
			for (var sliceIndex = 0; sliceIndex < sliceCount; sliceIndex++)
				sprite.Slices[reader.ReadString()] = new RectInt(reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32());

			var socketCount = ReadCount(reader, $"{sprite.Name} sockets");
			for (var socketIndex = 0; socketIndex < socketCount; socketIndex++)
			{
				var socketName = reader.ReadString();
				var poseCount = ReadCount(reader, $"{sprite.Name} {socketName} socket poses");
				var poses = new List<PackedSocketPose>(poseCount);
				for (var poseIndex = 0; poseIndex < poseCount; poseIndex++)
					poses.Add(new() { Position = ReadPackedVector(reader), Normal = ReadPackedVector(reader) });
				sprite.Sockets[socketName] = poses;
			}

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

		var tileKitCount = ReadCount(reader, "tile kits");
		for (var i = 0; i < tileKitCount; i++)
			manifest.TileKits.Add(new(
				Name: reader.ReadString(),
				PreferredChar: (char)reader.ReadInt32(),
				SlopePreferredChar: (char)reader.ReadInt32(),
				Substance: reader.ReadString(),
				Layer: (Factory.TileLayer)reader.ReadInt32(),
				Reflection: (Factory.ReflectionMode)reader.ReadInt32(),
				ReflectionOpacity: reader.ReadSingle(),
				AcousticReflectivity: reader.ReadSingle(),
				HasSlope: reader.ReadBoolean()));

		var nctTileCount = ReadCount(reader, "NCT tiles");
		for (var i = 0; i < nctTileCount; i++)
			manifest.NctTiles.Add(new()
			{
				Name = reader.ReadString(),
				PreferredChar = (char)reader.ReadInt32(),
				Interior = reader.ReadString(),
				Edge = reader.ReadString(),
				EdgeBandCells = reader.ReadInt32(),
				RoundingPixels = reader.ReadSingle(),
				OutsetPixels = reader.ReadSingle(),
				FreezeSteps = reader.ReadInt32(),
				StepsPerSecond = reader.ReadSingle(),
				FireRate = reader.ReadSingle(),
				Contrast = reader.ReadSingle(),
				Saturation = reader.ReadSingle(),
				Brightness = reader.ReadSingle(),
				Tint = new Color(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), 1f),
				NormalStrength = reader.ReadSingle(),
				SilhouetteNormalStrength = reader.ReadSingle(),
				BlendCurve = reader.ReadSingle(),
				EdgeWeight = reader.ReadSingle(),
				DetailScale = reader.ReadSingle(),
				DetailPhase = new Vector2(reader.ReadSingle(), reader.ReadSingle()),
				PatchFrequency = reader.ReadSingle(),
				SirenFrequency = reader.ReadSingle(),
				LatentGain = reader.ReadSingle(),
				LatentBias = reader.ReadSingle(),
				NearestStateSampling = reader.ReadBoolean(),
				Layer = (Factory.TileLayer)reader.ReadInt32(),
				Solid = reader.ReadBoolean(),
				Substance = reader.ReadString(),
				HasSlope = reader.ReadBoolean()
			});

		return manifest;
	}

	static void WritePackedSubtexture(BinaryWriter writer, PackedSubtexture subtexture)
	{
		WritePackedSourceRect(writer, subtexture.Source);
		WritePackedFrameRect(writer, subtexture.Frame);
	}

	static PackedSubtexture ReadPackedSubtexture(BinaryReader reader)
		=> new() { Source = ReadPackedSourceRect(reader), Frame = ReadPackedFrameRect(reader) };

	static void WritePackedSubtextures(BinaryWriter writer, List<PackedSubtexture> subtextures, string name)
	{
		WriteCount(writer, subtextures.Count, name);
		foreach (var subtexture in subtextures)
			WritePackedSubtexture(writer, subtexture);
	}

	static List<PackedSubtexture> ReadPackedSubtextures(BinaryReader reader, string name)
	{
		var count = ReadCount(reader, name);
		var result = new List<PackedSubtexture>(count);
		for (var i = 0; i < count; i++)
			result.Add(ReadPackedSubtexture(reader));
		return result;
	}

	static void WritePackedSubtextureGroups(BinaryWriter writer, List<List<PackedSubtexture>> groups, string name)
	{
		WriteCount(writer, groups.Count, name);
		foreach (var group in groups)
			WritePackedSubtextures(writer, group, name);
	}

	static List<List<PackedSubtexture>> ReadPackedSubtextureGroups(BinaryReader reader, string name)
	{
		var count = ReadCount(reader, name);
		var result = new List<List<PackedSubtexture>>(count);
		for (var i = 0; i < count; i++)
			result.Add(ReadPackedSubtextures(reader, name));
		return result;
	}

	static void WritePackedOccluders(BinaryWriter writer, List<List<PackedOccluderSegment>> occluders, string name)
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

	static List<List<PackedOccluderSegment>> ReadPackedOccluders(BinaryReader reader, string name)
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

	static void WritePackedOccluderGroups(BinaryWriter writer, List<List<List<PackedOccluderSegment>>> groups, string name)
	{
		WriteCount(writer, groups.Count, name);
		foreach (var group in groups)
			WritePackedOccluders(writer, group, name);
	}

	static List<List<List<PackedOccluderSegment>>> ReadPackedOccluderGroups(BinaryReader reader, string name)
	{
		var count = ReadCount(reader, name);
		var result = new List<List<List<PackedOccluderSegment>>>(count);
		for (var i = 0; i < count; i++)
			result.Add(ReadPackedOccluders(reader, name));
		return result;
	}

	static void WritePackedSourceRect(BinaryWriter writer, PackedRect rect)
	{
		writer.Write(PackU16(rect.X, "source rect x"));
		writer.Write(PackU16(rect.Y, "source rect y"));
		writer.Write(PackU16(rect.W, "source rect w"));
		writer.Write(PackU16(rect.H, "source rect h"));
	}

	static PackedRect ReadPackedSourceRect(BinaryReader reader)
		=> new()
		{
			X = reader.ReadUInt16(),
			Y = reader.ReadUInt16(),
			W = reader.ReadUInt16(),
			H = reader.ReadUInt16()
		};

	static void WritePackedFrameRect(BinaryWriter writer, PackedRect rect)
	{
		writer.Write(PackI16(rect.X, "frame rect x"));
		writer.Write(PackI16(rect.Y, "frame rect y"));
		writer.Write(PackU16(rect.W, "frame rect w"));
		writer.Write(PackU16(rect.H, "frame rect h"));
	}

	static PackedRect ReadPackedFrameRect(BinaryReader reader)
		=> new()
		{
			X = reader.ReadInt16(),
			Y = reader.ReadInt16(),
			W = reader.ReadUInt16(),
			H = reader.ReadUInt16()
		};

	static void WritePackedVector(BinaryWriter writer, PackedVector vector)
	{
		writer.Write(vector.X);
		writer.Write(vector.Y);
	}

	static PackedVector ReadPackedVector(BinaryReader reader)
		=> new() { X = reader.ReadSingle(), Y = reader.ReadSingle() };

	static void WriteCount(BinaryWriter writer, int count, string name)
	{
		if (count > ushort.MaxValue)
			throw new InvalidOperationException($"Visual asset pack has too many {name}: {count}");
		writer.Write((ushort)count);
	}

	static int ReadCount(BinaryReader reader, string name)
	{
		var count = reader.ReadUInt16();
		return count;
	}

	static ushort PackU16(float value, string name)
	{
		var rounded = MathF.Round(value);
		if (!float.IsFinite(value) || MathF.Abs(value - rounded) > 0.001f || rounded < 0 || rounded > ushort.MaxValue)
			throw new InvalidOperationException($"Visual asset pack {name} does not fit ushort pixel coordinates: {value}");
		return (ushort)rounded;
	}

	static short PackI16(float value, string name)
	{
		var rounded = MathF.Round(value);
		if (!float.IsFinite(value) || MathF.Abs(value - rounded) > 0.001f || rounded < short.MinValue || rounded > short.MaxValue)
			throw new InvalidOperationException($"Visual asset pack {name} does not fit short pixel coordinates: {value}");
		return (short)rounded;
	}

	static void EnsureFonts(GraphicsDevice gfx)
	{
		if (Font == null)
		{
			using var stream = Content.OpenRead("Fonts/dogica.ttf");
			Font = new SpriteFont(gfx, stream, 8);
			Font.LineGap = 4;
		}
		if (SpeechFont == null)
		{
			using var stream = Content.OpenRead("Fonts/m3x6.ttf");
			SpeechFont = new SpriteFont(gfx, stream, 16, premultiplyAlpha: true, pixelPerfect: true);
			SpeechFont.LineGap = 0;
		}
	}

	/// Reads a compiled shader blob from bundled content, or null when the shader is absent
	public static byte[]? TryReadCompiledShader(string fileName)
	{
		var key = $"Shaders/Compiled/{fileName}";
		if (Content.FileExists(key))
		{
			return Content.ReadAllBytes(key);
		}

		Console.Error.WriteLine("Shader file not found: " + key);
		return null;
	}

	static List<Subtexture> LoadSubtextures(List<PackedSubtexture> packed, Texture atlas)
	{
		var result = new List<Subtexture>(packed.Count);
		foreach (var item in packed)
			result.Add(item.ToSubtexture(atlas));
		return result;
	}

	static List<List<Subtexture>> LoadSubtextureGroups(List<List<PackedSubtexture>> packed, Texture atlas)
	{
		var result = new List<List<Subtexture>>(packed.Count);
		foreach (var group in packed)
			result.Add(LoadSubtextures(group, atlas));
		return result;
	}

	static List<Tileset.OccluderSegment[]> LoadOccluders(List<List<PackedOccluderSegment>> packed)
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

	static List<List<Tileset.OccluderSegment[]>> LoadOccluderGroups(List<List<List<PackedOccluderSegment>>> packed)
	{
		var result = new List<List<Tileset.OccluderSegment[]>>(packed.Count);
		foreach (var group in packed)
			result.Add(LoadOccluders(group));
		return result;
	}

	sealed class VisualAssetPackManifest
	{
		public string Atlas { get; set; } = "atlas.png";
		public string? LightingNoise { get; set; }
		public List<PackedNamedSubtexture> Subtextures { get; set; } = [];
		public List<PackedSprite> Sprites { get; set; } = [];
		public List<PackedTileset> Tilesets { get; set; } = [];
		public List<TileKitRegistration> TileKits { get; set; } = [];
		public List<NctTileSpec> NctTiles { get; set; } = [];
	}

	sealed class VisualAssetBake : IDisposable
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

		public PackedSubtexture SubtextureOrDefault(string name)
			=> Subtextures.GetValueOrDefault(name) ?? new();

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

	sealed class PackedTileset
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

	sealed class PackedOccluderSegment
	{
		public PackedVector A { get; set; } = new();
		public PackedVector B { get; set; } = new();

		public static PackedOccluderSegment From(Tileset.OccluderSegment segment)
			=> new() { A = PackedVector.From(segment.A), B = PackedVector.From(segment.B) };

		public Tileset.OccluderSegment ToSegment()
			=> new(A.ToVector2(), B.ToVector2());
	}

	static List<List<PackedOccluderSegment>> PackOccluders(List<Tileset.OccluderSegment[]> occluders)
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

	static List<List<List<PackedOccluderSegment>>> PackOccluderGroups(List<List<Tileset.OccluderSegment[]>> groups)
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

	// Box-filter downscale of a source image region into dst at the given origin.
	static void BoxDownscaleInto(Image dst, Point2 origin, ReadOnlySpan<Color> src, int srcWidth, int srcHeight, int dstWidth, int dstHeight)
	{
		for (var ty = 0; ty < dstHeight; ty++)
		{
			var sy0 = ty * srcHeight / dstHeight;
			var sy1 = Math.Max(sy0 + 1, (ty + 1) * srcHeight / dstHeight);
			for (var tx = 0; tx < dstWidth; tx++)
			{
				var sx0 = tx * srcWidth / dstWidth;
				var sx1 = Math.Max(sx0 + 1, (tx + 1) * srcWidth / dstWidth);

				int r = 0, g = 0, b = 0, a = 0, n = 0;
				for (var sy = sy0; sy < sy1; sy++)
				{
					var rowOffset = sy * srcWidth;
					for (var sx = sx0; sx < sx1; sx++)
					{
						var c = src[rowOffset + sx];
						r += c.R; g += c.G; b += c.B; a += c.A; n++;
					}
				}
				n = Math.Max(1, n);
				dst[origin.X + tx, origin.Y + ty] = new Color((byte)(r / n), (byte)(g / n), (byte)(b / n), (byte)(a / n));
			}
		}
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

	static HashSet<Aseprite.Layer> GetNonDebugLayers(Aseprite ase)
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

	static List<Aseprite.Layer> GetVariantLayers(Aseprite ase, HashSet<Aseprite.Layer> nonDebugLayers)
		=> [.. ase.Layers.Where(it => nonDebugLayers.Contains(it) && it.Visible && it.Type == Aseprite.LayerType.Normal)];

	static void GenerateDualTilesetFromKit(string kitName, Aseprite ase, out string baseName, out List<Image> baseTiles, out List<Image> baseSlopeEdges, out string dualName, out List<Image> dualVariants, out List<Image> dualNormals, out List<List<Tileset.OccluderSegment[]>> dualOccluders)
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

		var nonDebugLayers = GetNonDebugLayers(ase);
		var heightLayers = GetLayerBranch(ase, "height");
		using var rendered = ase.RenderFrame(0, layer => nonDebugLayers.Contains(layer) && !heightLayers.Contains(layer));
		using var renderedHeight = heightLayers.Count > 0 ? RenderLayerFrame(ase, heightLayers, 0) : null;
		using var mask = ExtractTile(rendered, maskBounds);
		var fills = ExtractSliceTiles(rendered, fillSlice, kitName, required: true);
		var heights = renderedHeight != null ? ExtractMatchingSliceTiles(renderedHeight, rendered, fillSlice, kitName, "height") : [];
		var edges = edgeSlice != null
			? ExtractSliceTiles(rendered, edgeSlice, kitName, required: false)
			: [];

		if (edges.Count == 0 && edgeSlice != null)
			throw new InvalidOperationException($"Dual tileset kit '{kitName}' edge slice does not contain any non-empty {Game.TileSize}x{Game.TileSize} tile.");

		// Optional directional rims: pixels on up-facing contours use edge-top, down-facing
		// use edge-bottom, the rest edge-side; anything missing falls back to the plain edge.
		var edgeTops = ExtractOptionalEdgeSlice(rendered, slices, "edgetop", kitName);
		var edgeSides = ExtractOptionalEdgeSlice(rendered, slices, "edgeside", kitName);
		var edgeBottoms = ExtractOptionalEdgeSlice(rendered, slices, "edgebottom", kitName);

		var edgeWidth = ParseEdgeWidthRange(kitName, edgeSlice);
		var wobble = ParseMaskWobble(kitName, maskSlice);

		GenerateDualTilesetCore(mask, fills, heights, edges, edgeTops, edgeSides, edgeBottoms, edgeWidth, wobble, baseTiles, baseSlopeEdges, dualVariants, dualNormals, dualOccluders);
	}

	/// Generates the tileset outputs for a JSON kit spec: images come from the spec's
	/// palette/patterns/noise instead of an .ase, and the silhouette mask is borrowed from
	/// the referenced .ase kit. Everything downstream is shared with the .ase path.
	static void GenerateDualTilesetFromSpec(TileKitSpec spec, Aseprite maskKit, out List<Image> baseTiles, out List<Image> baseSlopeEdges, out List<Image> dualVariants, out List<Image> dualNormals, out List<List<Tileset.OccluderSegment[]>> dualOccluders)
	{
		baseTiles = [];
		baseSlopeEdges = [];
		dualVariants = [];
		dualNormals = [];
		dualOccluders = [];

		var slices = GetKitSlices(maskKit);
		if (!slices.TryGetValue("mask", out var maskSlice))
			throw new InvalidOperationException($"Tile kit '{spec.Name}' mask kit '{spec.MaskKit}' has no 'mask' slice.");
		var maskBounds = GetSliceBounds(maskSlice);
		if (maskBounds.Size != new Point2(Game.TileSize * 4, Game.TileSize * 4))
			throw new InvalidOperationException($"Tile kit '{spec.Name}' mask kit '{spec.MaskKit}' mask slice must be exactly {Game.TileSize * 4}x{Game.TileSize * 4} pixels.");

		var nonDebugLayers = GetNonDebugLayers(maskKit);
		var heightLayers = GetLayerBranch(maskKit, "height");
		using var rendered = maskKit.RenderFrame(0, layer => nonDebugLayers.Contains(layer) && !heightLayers.Contains(layer));
		using var mask = ExtractTile(rendered, maskBounds);

		var fills = TileKits.RasterizeFills(spec);
		var heights = TileKits.RasterizeHeights(spec);
		var (edges, edgeTops, edgeBottoms) = TileKits.RasterizeEdges(spec);
		var edgeWidth = ParseEdgeWidthValue(spec.Name, spec.EdgeWidth);

		GenerateDualTilesetCore(mask, fills, heights, edges, edgeTops, [], edgeBottoms, edgeWidth, spec.Wobble, baseTiles, baseSlopeEdges, dualVariants, dualNormals, dualOccluders);
	}

	// Shared dual-tileset generation. Takes ownership of every image list (disposed here);
	// the mask stays owned by the caller.
	static void GenerateDualTilesetCore(Image mask, List<Image> fills, List<Image> heights, List<Image> edges, List<Image> edgeTops, List<Image> edgeSides, List<Image> edgeBottoms, (int Min, int Max) edgeWidthRange, float wobble, List<Image> baseTiles, List<Image> baseSlopeEdges, List<Image> dualVariants, List<Image> dualNormals, List<List<Tileset.OccluderSegment[]>> dualOccluders)
	{
		static Image? Pick(List<Image> tiles, int i) => tiles.Count > 0 ? tiles[i % tiles.Count] : null;

		for (var i = 0; i < fills.Count; i++)
		{
			var fill = fills[i];
			if (i == 0)
			{
				baseTiles.Add(ExtractTile(fill, fill.Bounds));
				if (edges.Count > 0)
					baseSlopeEdges.Add(ExtractTile(edges[i % edges.Count], edges[i % edges.Count].Bounds));
			}
			// Variant 0 keeps the authored silhouette; later variants get a jittered copy so
			// the terrain contour doesn't repeat. Color, normals and occluders all derive from
			// the same variant mask, so lighting bevels and shadows follow the wobble.
			var variantMask = i > 0 && wobble > 0 ? WobbleMask(mask, i, wobble) : mask;
			var edgeWidth = edgeWidthRange.Min + i % (edgeWidthRange.Max - edgeWidthRange.Min + 1);
			var side = Pick(edgeSides, i) ?? Pick(edges, i);
			dualVariants.Add(GenerateDualTilesetVariant(fill, variantMask, Pick(edgeTops, i) ?? side, side, Pick(edgeBottoms, i) ?? side, edgeWidth));
			dualNormals.Add(GenerateDualTilesetNormalVariant(variantMask, heights.Count > 0 ? heights[i] : null));
			dualOccluders.Add(GenerateDualTilesetOccluderVariant(variantMask));
			if (variantMask != mask)
				variantMask.Dispose();
		}

		foreach (var list in (List<Image>[])[fills, edges, heights, edgeTops, edgeSides, edgeBottoms])
			foreach (var image in list)
				image.Dispose();
	}

	/// Editor tile-creator preview: bakes the spec in memory and composes it onto a small
	/// demo terrain ('#' rows mark solid tiles), picking variants per dual cell the way the
	/// in-game renderer does. Never touches the atlas; the caller owns the returned image.
	public static Image GenerateTileKitPreviewImage(TileKitSpec spec, string[] solidMap)
	{
		var maskKit = new Aseprite(Path.Join(SourcesPath, "Tilesets", $"{spec.MaskKit}.ase"));
		GenerateDualTilesetFromSpec(spec, maskKit, out var baseTiles, out var baseSlopeEdges, out var dualVariants, out var dualNormals, out _);

		var rows = solidMap.Length;
		var columns = solidMap.Max(row => row.Length);
		bool Solid(int x, int y) => y >= 0 && y < rows && x >= 0 && x < solidMap[y].Length && solidMap[y][x] == '#';

		// Dual cells sit on tile corners: cell (x,y) reads the four tiles around it and is
		// drawn half a tile up-left; the preview folds that offset into the image origin.
		var output = new Image((columns + 1) * Game.TileSize, (rows + 1) * Game.TileSize);
		for (var y = 0; y <= rows; y++)
			for (var x = 0; x <= columns; x++)
			{
				var mask = 0;
				if (Solid(x - 1, y - 1)) mask |= TileQuadrants.TopLeft;
				if (Solid(x - 1, y)) mask |= TileQuadrants.BottomLeft;
				if (Solid(x, y - 1)) mask |= TileQuadrants.TopRight;
				if (Solid(x, y)) mask |= TileQuadrants.BottomRight;
				if (mask == 0)
					continue;

				var tile = MaskToDualTile(mask);
				var variant = dualVariants[PreviewVariantIndex(x, y, dualVariants.Count)];
				for (var py = 0; py < Game.TileSize; py++)
					for (var px = 0; px < Game.TileSize; px++)
					{
						var color = variant[tile.X * Game.TileSize + px, tile.Y * Game.TileSize + py];
						if (color.A > 0)
							output[x * Game.TileSize + px, y * Game.TileSize + py] = color;
					}
			}

		foreach (var list in (List<Image>[])[baseTiles, baseSlopeEdges, dualVariants, dualNormals])
			foreach (var image in list)
				image.Dispose();
		return output;
	}

	static int PreviewVariantIndex(int x, int y, int count)
	{
		unchecked
		{
			var hash = (uint)(x * 374761393 + y * 668265263);
			hash = (hash ^ (hash >> 13)) * 1274126177u;
			return (int)(hash % (uint)count);
		}
	}

	static List<Image> ExtractOptionalEdgeSlice(Image rendered, Dictionary<string, Aseprite.Slice> slices, string key, string kitName)
	{
		if (!slices.TryGetValue(key, out var slice))
			return [];
		var tiles = ExtractSliceTiles(rendered, slice, kitName, required: false);
		if (tiles.Count == 0)
			throw new InvalidOperationException($"Dual tileset kit '{kitName}' slice '{slice.Name}' does not contain any non-empty {Game.TileSize}x{Game.TileSize} tile.");
		return tiles;
	}

	static string NormalizeLayerName(string? name)
		=> (name ?? "").Trim().Replace(" ", "", StringComparison.Ordinal).Replace("-", "", StringComparison.Ordinal).ToLowerInvariant();

	static HashSet<Aseprite.Layer> GetSocketLayers(Aseprite ase)
	{
		var result = new HashSet<Aseprite.Layer>();
		foreach (var layer in ase.Layers)
		{
			var name = NormalizeLayerName(layer.Name);
			if (!name.EndsWith(socketLayerSuffix, StringComparison.Ordinal))
				continue;
			if (layer.Type != Aseprite.LayerType.Normal)
				throw new InvalidOperationException($"Socket layer '{layer.Name}' must be an image layer");
			if (name.Length == socketLayerSuffix.Length)
				throw new InvalidOperationException($"Socket layer '{layer.Name}' has no socket name before '{socketLayerSuffix}'");
			result.Add(layer);
		}
		return result;
	}

	static Dictionary<string, Sprite.Socket[]> BuildSocketPoses(string spriteName, Aseprite ase)
	{
		var result = new Dictionary<string, Sprite.Socket[]>(StringComparer.OrdinalIgnoreCase);
		foreach (var layer in GetSocketLayers(ase))
		{
			var layerName = NormalizeLayerName(layer.Name);
			var socketName = layerName[..^socketLayerSuffix.Length];
			if (result.ContainsKey(socketName))
				throw SocketError(spriteName, layer, -1, $"duplicate socket name '{socketName}'");

			var source = ReadSourceSocketPoses(spriteName, ase, layer);
			var poses = new List<Sprite.Socket>();
			if (ExpandedFrameDurations.ContainsKey(spriteName) &&
				TryBuildInbetweenSpec(spriteName, ase, source.Length, out var spec, out _))
			{
				for (var i = 0; i < source.Length; i++)
				{
					poses.Add(source[i]);
					for (var j = 1; j <= spec[i].Count; j++)
						poses.Add(LerpSocket(source[i], source[spec[i].Target], j / (float)(spec[i].Count + 1)));
				}
			}
			else
				poses.AddRange(source);

			if (GeneratedTransitions.TryGetValue(spriteName, out var transitions))
				foreach (var transition in transitions)
				{
					var from = FindTag(ase, transition.FromTag);
					var to = FindTag(ase, transition.ToTag);
					if (from == null || to == null)
						throw SocketError(spriteName, layer, -1, $"can't resolve generated transition {transition.FromTag}->{transition.ToTag}");
					for (var i = 0; i < transition.FrameCount; i++)
						poses.Add(LerpSocket(source[from.Value.To], source[to.Value.From], (i + 1f) / (transition.FrameCount + 1f)));
				}

			result[socketName] = [.. poses];
		}
		return result;
	}

	static Sprite.Socket[] ReadSourceSocketPoses(string spriteName, Aseprite ase, Aseprite.Layer layer)
	{
		if (layer.Opacity != byte.MaxValue)
			throw SocketError(spriteName, layer, -1, "layer opacity must be 255");

		var result = new Sprite.Socket[ase.Frames.Length];
		var previous = default(Sprite.Socket);
		var hasPrevious = false;
		Span<Point2> positions = stackalloc Point2[2];
		Span<Color> colors = stackalloc Color[2];
		for (var frameIndex = 0; frameIndex < ase.Frames.Length; frameIndex++)
		{
			Aseprite.Cel? cel = null;
			foreach (var candidate in ase.Frames[frameIndex].Cels)
				if (candidate.Layer == layer)
				{
					cel = candidate;
					break;
				}

			var markerCount = 0;
			if (cel != null)
			{
				if (cel.Opacity != byte.MaxValue)
					throw SocketError(spriteName, layer, frameIndex, "cel opacity must be 255");
				for (var pixelIndex = 0; pixelIndex < cel.Image.PixelCount; pixelIndex++)
				{
					var color = cel.Image[pixelIndex];
					if (color.A == 0)
						continue;
					if (color.A != byte.MaxValue)
						throw SocketError(spriteName, layer, frameIndex, "marker pixels must be fully opaque");
					if (markerCount >= 2)
						throw SocketError(spriteName, layer, frameIndex, "expected at most two marker pixels");
					positions[markerCount] = cel.Pos + new Point2(pixelIndex % cel.Image.Width, pixelIndex / cel.Image.Width);
					colors[markerCount++] = color;
				}
			}

			if (markerCount == 0)
			{
				if (!hasPrevious)
					throw SocketError(spriteName, layer, frameIndex, "the first frame must define a socket marker");
				result[frameIndex] = previous;
				continue;
			}

			if (markerCount == 1)
				previous = new(positions[0], Vector2.UnitY);
			else
			{
				if (colors[0].RGBA == colors[1].RGBA)
					throw SocketError(spriteName, layer, frameIndex, "the two marker pixels must have different colors");

				var luminance0 = SocketMarkerLuminance(colors[0]);
				var luminance1 = SocketMarkerLuminance(colors[1]);
				if (luminance0 == luminance1)
					throw SocketError(spriteName, layer, frameIndex, "the two marker colors have equal luminance");

				var dark = luminance0 < luminance1 ? 0 : 1;
				var light = 1 - dark;
				previous = new(positions[dark], Vector2.Normalize(positions[light] - positions[dark]));
			}

			hasPrevious = true;
			result[frameIndex] = previous;
		}

		return result;
	}

	static int SocketMarkerLuminance(Color color)
		=> color.R * 2126 + color.G * 7152 + color.B * 722;

	static Sprite.Socket LerpSocket(Sprite.Socket from, Sprite.Socket to, float t)
	{
		var fromAngle = MathF.Atan2(from.Normal.Y, from.Normal.X);
		var toAngle = MathF.Atan2(to.Normal.Y, to.Normal.X);
		var delta = MathF.Atan2(MathF.Sin(toAngle - fromAngle), MathF.Cos(toAngle - fromAngle));
		var angle = fromAngle + delta * t;
		return new(Vector2.Lerp(from.Position, to.Position, t), new Vector2(MathF.Cos(angle), MathF.Sin(angle)));
	}

	static Aseprite.Tag? FindTag(Aseprite ase, string name)
	{
		foreach (var tag in ase.Tags)
			if (tag.Name == name)
				return tag;
		return null;
	}

	static InvalidOperationException SocketError(string spriteName, Aseprite.Layer layer, int frame, string message)
		=> new($"Sprite '{spriteName}' socket layer '{layer.Name}'{(frame >= 0 ? $" frame {frame}" : "")}: {message}");

	static Dictionary<string, Aseprite.Slice> GetKitSlices(Aseprite ase)
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

	static RectInt GetSliceBounds(Aseprite.Slice slice)
	{
		if (slice.Keys.Length <= 0)
			throw new InvalidOperationException($"Aseprite slice '{slice.Name}' has no keys.");
		return slice.Keys[0].Bounds;
	}

	static List<Image> ExtractSliceTiles(Image source, Aseprite.Slice slice, string kitName, bool required)
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

	static List<Image> ExtractMatchingSliceTiles(Image source, Image reference, Aseprite.Slice slice, string kitName, string layerName)
	{
		var bounds = GetSliceBounds(slice);
		var tiles = new List<Image>();
		var columns = bounds.Width / Game.TileSize;
		var rows = bounds.Height / Game.TileSize;
		for (var y = 0; y < rows; y++)
			for (var x = 0; x < columns; x++)
			{
				var tileRect = new RectInt(bounds.Position + new Point2(x, y) * Game.TileSize, new Point2(Game.TileSize, Game.TileSize));
				var occupied = HasOpaquePixel(source, tileRect);
				if (occupied != HasOpaquePixel(reference, tileRect))
					throw new InvalidOperationException($"Dual tileset kit '{kitName}' {layerName} layer must match its fill cells; cell {x},{y} differs.");
				if (occupied)
					tiles.Add(ExtractTile(source, tileRect));
			}
		return tiles;
	}

	static bool HasOpaquePixel(Image image, RectInt rect)
	{
		for (var y = rect.Y; y < rect.Bottom; y++)
			for (var x = rect.X; x < rect.Right; x++)
				if (image[x, y].A > 0)
					return true;
		return false;
	}

	static Image GenerateTilesetHeightNormals(string name, Image height, Image color, int columns, int rows)
	{
		var output = new Image(color.Width, color.Height);
		for (var tileX = 0; tileX < columns; tileX++)
			for (var tileY = 0; tileY < rows; tileY++)
			{
				var tile = new RectInt(tileX, tileY, 1, 1) * Game.TileSize;
				if (HasOpaquePixel(height, tile) != HasOpaquePixel(color, tile))
					throw new InvalidOperationException($"Tileset '{name}' height layer must match its color cells; cell {tileX},{tileY} differs.");

				for (var y = 0; y < Game.TileSize; y++)
					for (var x = 0; x < Game.TileSize; x++)
					{
						var heightAlpha = AlphaAt(height, tile.X + x, tile.Y + y);
						var colorAlpha = AlphaAt(color, tile.X + x, tile.Y + y);
						if (heightAlpha <= 0 || colorAlpha <= 0)
							continue;

						var normal = NormalFromHeight(height, tile, x, y, wrap: true);
						output[tile.X + x, tile.Y + y] = new Color(
							normal.X * 0.5f + 0.5f,
							normal.Y * 0.5f + 0.5f,
							normal.Z * 0.5f + 0.5f,
							heightAlpha * colorAlpha);
					}
			}
		return output;
	}

	// Kit parameters are authored as user data text on the kit's slices (key=value pairs,
	// e.g. "width=1-3" on the edge slice, "wobble=0.8" on the mask slice).
	static Dictionary<string, string> ParseKitSliceParams(Aseprite.Slice? slice)
	{
		var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		var text = slice?.UserData.Text;
		if (string.IsNullOrWhiteSpace(text))
			return result;
		foreach (var token in text.Split([' ', ',', ';'], StringSplitOptions.RemoveEmptyEntries))
		{
			var eq = token.IndexOf('=');
			if (eq > 0)
				result[token[..eq].Trim()] = token[(eq + 1)..].Trim();
		}
		return result;
	}

	// Edge band thickness in pixels: "width=N", or "width=A-B" to cycle the range across
	// variants so some chunks of terrain read chunkier than others.
	static (int Min, int Max) ParseEdgeWidthRange(string kitName, Aseprite.Slice? slice)
	{
		const int DefaultEdgeWidth = 2;
		if (!ParseKitSliceParams(slice).TryGetValue("width", out var value))
			return (DefaultEdgeWidth, DefaultEdgeWidth);
		return ParseEdgeWidthValue(kitName, value);
	}

	/// Editor helper: the (min, max) rim width a kit's edgeWidth string describes.
	public static (int Min, int Max) EdgeWidthRangeForEditor(string kitName, string value)
		=> ParseEdgeWidthValue(kitName, value);

	static (int Min, int Max) ParseEdgeWidthValue(string kitName, string value)
	{
		var dash = value.IndexOf('-');
		if (dash > 0 && int.TryParse(value[..dash], out var lo) && int.TryParse(value[(dash + 1)..], out var hi) && lo >= 0 && hi >= lo)
			return (lo, hi);
		if (int.TryParse(value, out var width) && width >= 0)
			return (width, width);
		throw new InvalidOperationException($"Dual tileset kit '{kitName}' has invalid edge width '{value}' (expected \"N\" or \"A-B\").");
	}

	// Silhouette jitter strength, 0..1: "wobble=0" keeps every variant on the authored mask.
	static float ParseMaskWobble(string kitName, Aseprite.Slice slice)
	{
		const float DefaultMaskWobble = 0.6f;
		if (!ParseKitSliceParams(slice).TryGetValue("wobble", out var value))
			return DefaultMaskWobble;
		if (float.TryParse(value, System.Globalization.CultureInfo.InvariantCulture, out var wobble) && wobble >= 0)
			return Math.Clamp(wobble, 0f, 1f);
		throw new InvalidOperationException($"Dual tileset kit '{kitName}' mask slice has invalid wobble '{value}' (expected 0..1).");
	}

	// Per-variant silhouette jitter: nudges the mask contour in or out by one pixel using
	// smooth per-variant noise. Cell-border pixels never change (seams between neighboring
	// dual tiles must keep lining up), and only contour-adjacent pixels may flip, so the
	// silhouette never drifts more than 1px from the authored mask/collision.
	static Image WobbleMask(Image mask, int variant, float strength)
	{
		var result = new Image(mask.Width, mask.Height);
		var threshold = 1f - Math.Clamp(strength, 0f, 1f) * 0.8f;
		var solidColor = new Color(255, 0, 0, 255);

		for (var cellY = 0; cellY < 4; cellY++)
			for (var cellX = 0; cellX < 4; cellX++)
			{
				var cell = new RectInt(cellX, cellY, 1, 1) * Game.TileSize;
				for (var y = 0; y < Game.TileSize; y++)
					for (var x = 0; x < Game.TileSize; x++)
					{
						var color = mask[cell.X + x, cell.Y + y];
						var solid = color.A > 0;
						var interior = x > 0 && y > 0 && x < Game.TileSize - 1 && y < Game.TileSize - 1;
						if (interior && OnMaskContour(mask, cell, x, y, solid))
						{
							var noise = WobbleNoise(cell.X + x, cell.Y + y, variant);
							if (solid ? noise < -threshold : noise > threshold)
							{
								solid = !solid;
								color = solidColor;
							}
						}
						if (solid)
							result[cell.X + x, cell.Y + y] = color;
					}
			}
		return result;
	}

	// True when any 4-neighbor within the cell is on the other side of the silhouette.
	// Callers only pass interior pixels, so the neighbors never leave the cell.
	static bool OnMaskContour(Image mask, RectInt cell, int x, int y, bool solid)
		=> (mask[cell.X + x - 1, cell.Y + y].A > 0) != solid ||
			(mask[cell.X + x + 1, cell.Y + y].A > 0) != solid ||
			(mask[cell.X + x, cell.Y + y - 1].A > 0) != solid ||
			(mask[cell.X + x, cell.Y + y + 1].A > 0) != solid;

	// Bilinear value noise in [-1, 1] with ~3px features, deterministic per variant, so the
	// contour shifts in short runs instead of per-pixel dither.
	static float WobbleNoise(int x, int y, int variant)
	{
		const float scale = 1f / 3f;
		var fx = x * scale;
		var fy = y * scale;
		var x0 = (int)MathF.Floor(fx);
		var y0 = (int)MathF.Floor(fy);
		var tx = fx - x0;
		var ty = fy - y0;
		var top = WobbleHash(x0, y0, variant) + (WobbleHash(x0 + 1, y0, variant) - WobbleHash(x0, y0, variant)) * tx;
		var bottom = WobbleHash(x0, y0 + 1, variant) + (WobbleHash(x0 + 1, y0 + 1, variant) - WobbleHash(x0, y0 + 1, variant)) * tx;
		return (top + (bottom - top) * ty) * 2f - 1f;
	}

	static float WobbleHash(int x, int y, int variant)
	{
		unchecked
		{
			var hash = (uint)(x * 374761393 + y * 668265263 + variant * 2246822519);
			hash = (hash ^ (hash >> 13)) * 1274126177u;
			hash ^= hash >> 16;
			return (hash & 0xFFFFFF) / (float)0x1000000;
		}
	}

	static Image GenerateDualTilesetVariant(Image fill, Image mask, Image? edgeTop, Image? edgeSide, Image? edgeBottom, int edgeWidth)
	{
		var output = new Image(Game.TileSize * 4, Game.TileSize * 4);
		for (var tileMask = 1; tileMask < TileQuadrants.All + 1; tileMask++)
		{
			var tile = MaskToDualTile(tileMask);
			var source = new RectInt(tile.X, tile.Y, 1, 1) * Game.TileSize;
			GenerateDualTile(output, source, fill, tileMask, mask, edgeTop, edgeSide, edgeBottom, edgeWidth);
		}
		return output;
	}

	static Image GenerateDualTilesetNormalVariant(Image mask, Image? height)
	{
		var output = new Image(Game.TileSize * 4, Game.TileSize * 4);
		for (var tileMask = 1; tileMask < TileQuadrants.All + 1; tileMask++)
		{
			var tile = MaskToDualTile(tileMask);
			var source = new RectInt(tile.X, tile.Y, 1, 1) * Game.TileSize;
			if (height != null)
				GenerateDualHeightNormalTile(output, source, mask, height);
			GenerateDualNormalTile(output, source, tileMask, mask);
		}
		return output;
	}

	static List<Tileset.OccluderSegment[]> GenerateDualTilesetOccluderVariant(Image mask)
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

		return [.. result];
	}

	static Tileset.OccluderSegment[] GenerateMaskOccluderSegments(Image mask, RectInt source)
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

		return [.. segments];
	}

	static void MergeHorizontalMaskEdges(List<Tileset.OccluderSegment> segments, Image mask, RectInt source, int y, bool top)
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

	static void MergeVerticalMaskEdges(List<Tileset.OccluderSegment> segments, Image mask, RectInt source, int x, bool left)
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

	static bool IsMaskSolid(Image mask, RectInt source, int x, int y)
	{
		if (x < 0 || y < 0 || x >= Game.TileSize || y >= Game.TileSize)
			return false;

		return mask[source.X + x, source.Y + y].A > 0;
	}

	static void GenerateDualHeightNormalTile(Image output, RectInt source, Image alphaMask, Image height)
	{
		for (var y = 0; y < Game.TileSize; y++)
			for (var x = 0; x < Game.TileSize; x++)
			{
				var maskAlpha = AlphaAt(alphaMask, source.X + x, source.Y + y);
				var heightAlpha = AlphaAt(height, x, y);
				if (maskAlpha <= 0 || heightAlpha <= 0)
					continue;

				var normal = NormalFromHeight(height, height.Bounds, x, y, wrap: true);
				output[source.X + x, source.Y + y] = new Color(
					normal.X * 0.5f + 0.5f,
					normal.Y * 0.5f + 0.5f,
					normal.Z * 0.5f + 0.5f,
					maskAlpha * heightAlpha);
			}
	}

	static void GenerateDualNormalTile(Image output, RectInt source, int mask, Image alphaMask)
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

	static float AlphaAtClampedToSource(Image image, RectInt source, int x, int y)
	{
		x = Math.Clamp(x, 0, source.Width - 1);
		y = Math.Clamp(y, 0, source.Height - 1);
		return AlphaAt(image, source.X + x, source.Y + y);
	}

	static void GenerateDualTile(Image output, RectInt source, Image fill, int mask, Image alphaMask, Image? edgeTop, Image? edgeSide, Image? edgeBottom, int edgeWidth)
	{
		var tile = MaskToDualTile(mask);
		var destination = new Point2(tile.X * Game.TileSize, tile.Y * Game.TileSize);
		var hasEdge = edgeTop != null || edgeSide != null || edgeBottom != null;

		for (var y = 0; y < Game.TileSize; y++)
			for (var x = 0; x < Game.TileSize; x++)
			{
				var alpha = alphaMask[source.X + x, source.Y + y].A;
				if (alpha == 0)
					continue;

				var color = fill[x, y];
				if (hasEdge && edgeWidth > 0)
				{
					var chosen = ClassifyMaskEdge(alphaMask, source, x, y, edgeWidth) switch
					{
						EdgeDirection.Top => edgeTop ?? edgeSide,
						EdgeDirection.Bottom => edgeBottom ?? edgeSide,
						EdgeDirection.Side => edgeSide,
						_ => null,
					};
					if (chosen != null)
						color = chosen[x, y];
				}

				color.A = (byte)(color.A * alpha / 255);
				output[destination.X + x, destination.Y + y] = color;
			}
	}

	enum EdgeDirection { None, Top, Side, Bottom }

	// Rim membership + orientation in one pass: a pixel is rim when open air lies within
	// `width` (Manhattan) of it inside this cell, and its facing is the distance-weighted
	// average direction of that air, so pixels a couple deep under a top surface still
	// classify as Top. Out-of-cell samples count as solid, same as the old rim test: the
	// silhouette continues into the neighboring dual tile there.
	static EdgeDirection ClassifyMaskEdge(Image mask, RectInt source, int x, int y, int width)
	{
		var vx = 0f;
		var vy = 0f;
		var found = false;
		for (var oy = -width; oy <= width; oy++)
			for (var ox = -width; ox <= width; ox++)
			{
				var distance = Math.Abs(ox) + Math.Abs(oy);
				if (distance == 0 || distance > width)
					continue;
				var sampleX = source.X + x + ox;
				var sampleY = source.Y + y + oy;
				if (sampleX < source.X || sampleY < source.Y || sampleX >= source.Right || sampleY >= source.Bottom)
					continue;
				if (mask[sampleX, sampleY].A != 0)
					continue;
				found = true;
				var weight = 1f / distance;
				vx += ox * weight;
				vy += oy * weight;
			}

		if (!found)
			return EdgeDirection.None;
		if (MathF.Abs(vy) >= MathF.Abs(vx))
			return vy < 0 ? EdgeDirection.Top : vy > 0 ? EdgeDirection.Bottom : EdgeDirection.Side;
		return EdgeDirection.Side;
	}

	static Point2 MaskToDualTile(int mask)
	{
		var index = Array.FindIndex(DualMaskToTile, it => it.Mask == mask);
		return index >= 0 ? DualMaskToTile[index].Tile : Point2.Zero;
	}

	static readonly (int Mask, Point2 Tile)[] DualMaskToTile =
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
	static void LoadInbetweenConfig()
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

	static (Image[] Frames, Image[]? Normals, Image[]? Heights, Image[]? Emissives) RenderSpriteFrames(Aseprite ase, Predicate<Aseprite.Layer>? colorFilter)
	{
		var socketLayers = GetSocketLayers(ase);
		var normalLayers = GetLayerBranch(ase, "normals");
		var heightLayers = GetLayerBranch(ase, "height");
		var emissiveLayers = GetLayerBranch(ase, "emissive");
		normalLayers.ExceptWith(socketLayers);
		heightLayers.ExceptWith(socketLayers);
		emissiveLayers.ExceptWith(socketLayers);
		var frames = ase.RenderAllFrames(layer =>
			(colorFilter?.Invoke(layer) ?? true) && !socketLayers.Contains(layer) && !normalLayers.Contains(layer) && !heightLayers.Contains(layer) && !emissiveLayers.Contains(layer));
		var normals = normalLayers.Count > 0 ? RenderLayerFrames(ase, normalLayers) : null;
		var heights = normals == null && heightLayers.Count > 0 ? RenderLayerFrames(ase, heightLayers) : null;
		var emissives = emissiveLayers.Count > 0 ? RenderLayerFrames(ase, emissiveLayers) : null;
		return (frames, normals, heights, emissives);
	}

	static Image[] RenderLayerFrames(Aseprite ase, HashSet<Aseprite.Layer> layers)
		=> RenderLayersVisible(layers, () => ase.RenderAllFrames(layers.Contains));

	static Image RenderLayerFrame(Aseprite ase, HashSet<Aseprite.Layer> layers, int frame)
		=> RenderLayersVisible(layers, () => ase.RenderFrame(frame, layers.Contains));

	static T RenderLayersVisible<T>(HashSet<Aseprite.Layer> layers, Func<T> render)
	{
		var flags = new Aseprite.LayerFlags[layers.Count];
		var index = 0;
		foreach (var layer in layers)
		{
			flags[index++] = layer.Flags;
			layer.Flags |= Aseprite.LayerFlags.Visible;
		}
		try
		{
			return render();
		}
		finally
		{
			index = 0;
			foreach (var layer in layers)
				layer.Flags = flags[index++];
		}
	}

	static HashSet<Aseprite.Layer> GetLayerBranch(Aseprite ase, string branchName)
	{
		var result = new HashSet<Aseprite.Layer>();
		var branchLevel = int.MaxValue;
		foreach (var layer in ase.Layers)
		{
			if (layer.ChildLevel <= branchLevel)
				branchLevel = int.MaxValue;
			if (NormalizeLayerName(layer.Name) == branchName)
				branchLevel = layer.ChildLevel;
			if (branchLevel != int.MaxValue)
				result.Add(layer);
		}
		return result;
	}

	// Expands the frames of one sprite (inserting whitelisted within-animation
	// in-betweens) and packs them, then bakes any whitelisted transitions.
	static void PackSpriteFrames(string name, Aseprite ase, Image[] frames, Image[]? normals, Image[]? heights, Image[]? emissives, Packer packer)
	{
		var benchCount = InbetweenBenchCount(name);
		if (Inbetweens.BenchMode && benchCount > 0)
			_ = Inbetweens.Generate(name, frames, ase, benchCount);

		if (TryBuildInbetweenSpec(name, ase, frames.Length, out var spec, out var durationsSec))
		{
			var (expFrames, expDurations, map) = Inbetweens.GenerateExpanded(frames, durationsSec, spec);
			var expNormals = normals != null ? Inbetweens.GenerateExpanded(normals, durationsSec, spec).Frames : null;
			var expHeights = heights != null ? Inbetweens.GenerateExpanded(heights, durationsSec, spec).Frames : null;
			var expEmissives = emissives != null ? Inbetweens.GenerateExpanded(emissives, durationsSec, spec).Frames : null;
			ExpandedFrameDurations[name] = expDurations;
			ExpandedFrameMap[name] = map;
			for (int i = 0; i < expFrames.Length; i++)
			{
				packer.Add($"{name}/{i}", expFrames[i]);
				packer.Add($"{name}/normal/{i}", expNormals?[i] ??
					(expHeights != null ? GenerateNormalMap(expHeights[i], expFrames[i]) : GenerateNormalMap(expFrames[i])));
				if (expEmissives != null)
					packer.Add($"{name}/emissive/{i}", expEmissives[i]);
			}
		}
		else
		{
			for (int i = 0; i < frames.Length; i++)
			{
				packer.Add($"{name}/{i}", frames[i]);
				packer.Add($"{name}/normal/{i}", normals?[i] ??
					(heights != null ? GenerateNormalMap(heights[i], frames[i]) : GenerateNormalMap(frames[i])));
				if (emissives != null)
					packer.Add($"{name}/emissive/{i}", emissives[i]);
			}
		}

		GenerateTransitionFrames(name, ase, frames, normals, heights, emissives, packer);
	}

	static int InbetweenBenchCount(string name)
	{
		var result = 0;
		foreach (var (key, count) in InbetweenAnimations)
			if (key.Sprite == name)
				result = Math.Max(result, count);
		foreach (var (key, count) in InbetweenTransitions)
			if (key.Sprite == name)
				result = Math.Max(result, count);
		return result;
	}

	// Builds the per-frame in-between spec for a sprite from the InbetweenAnimations
	// whitelist. spec[i] = (count, target) describes in-betweens inserted after
	// original frame i. Returns false (and leaves the sprite unexpanded) when no
	// tag of this sprite is whitelisted.
	static bool TryBuildInbetweenSpec(
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

	static void GenerateTransitionFrames(string name, Aseprite ase, Image[] frames, Image[]? normals, Image[]? heights, Image[]? emissives, Packer packer)
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
		var normalResults = normals != null ? new Image[pairs.Count][] : null;
		var heightResults = heights != null ? new Image[pairs.Count][] : null;
		var emissiveResults = emissives != null ? new Image[pairs.Count][] : null;
		Parallel.For(0, pairs.Count, i =>
		{
			var p = pairs[i];
			results[i] = Inbetweens.GenerateForPair(frames[p.FromIdx], frames[p.ToIdx], p.Count);
			if (normalResults != null)
				normalResults[i] = Inbetweens.GenerateForPair(normals![p.FromIdx], normals[p.ToIdx], p.Count).Frames;
			if (heightResults != null)
				heightResults[i] = Inbetweens.GenerateForPair(heights![p.FromIdx], heights[p.ToIdx], p.Count).Frames;
			if (emissiveResults != null)
				emissiveResults[i] = Inbetweens.GenerateForPair(emissives![p.FromIdx], emissives[p.ToIdx], p.Count).Frames;
		});

		// Pack results sequentially (packer.Add is not thread-safe)
		var transitions = new List<GeneratedSpriteTransition>();
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
				packer.Add($"{prefix}/normal/{j}", normalResults?[i][j] ??
					(heightResults != null ? GenerateNormalMap(heightResults[i][j], transFrames[j]) : GenerateNormalMap(transFrames[j])));
				if (emissiveResults != null)
					packer.Add($"{prefix}/emissive/{j}", emissiveResults[i][j]);
			}

			transitions.Add(new(p.FromTag, p.ToTag, transFrames.Length, transDurations));
		}

		if (transitions.Count > 0)
			GeneratedTransitions[name] = transitions;
	}

	static Predicate<Aseprite.Layer> CreateSwordlessPlayerLayerFilter()
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
	static void DumpPackedAtlasPages(Packer.Output output)
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

	static void LoadRooms()
	{
		Rooms.Clear();
		RoomsById.Clear();
		MusicRegions.Load();

		var deleted = UseUserRooms ? LoadUserRoomTombstones() : [];

		var worldRoomsKey = $"{roomsFolderName}/{currentWorldId}";
		if (Content.DirectoryExists(worldRoomsKey))
			foreach (var key in Content.EnumerateDirectory(worldRoomsKey, "*.json", SearchOption.AllDirectories))
			{
				var id = Path.GetFileNameWithoutExtension(key);
				if (deleted.Contains(id))
					continue;

				var json = Content.ReadAllBytes(key);
				var room = LoadRoomJson(id, json);
				room.DiskRevision = RoomRevision(json);
				room.Sublayer = RoomSublayerFromPath(worldRoomsKey, key);
				room.Storage = Room.StorageKind.Bundled;
				// Zip content has no per-room file to rename/delete; SourcePath stays null there.
				room.SourcePath = ContentIsZip ? null : Path.Join(AssetsPath, key);
				LoadRoomColorFields(room, key);
				AddRoom(room);
			}

		if (UseUserRooms && UserWorldRoomsPath is { } userWorldRoomsPath && Directory.Exists(userWorldRoomsPath))
			foreach (var file in Directory.EnumerateFiles(userWorldRoomsPath, "*.json", SearchOption.AllDirectories))
			{
				var json = File.ReadAllBytes(file);
				var room = LoadRoomJson(Path.GetFileNameWithoutExtension(file), json);
				room.DiskRevision = RoomRevision(json);
				room.Sublayer = RoomSublayerFromPath(userWorldRoomsPath, file);
				room.Storage = Room.StorageKind.User;
				room.SourcePath = file;
				LoadRoomColorFields(room, file, fromContent: false);
				AddRoom(room);
			}

		if (RoomsById.Count == 0)
			CreateDefaultStartRoom();
	}

	static Room LoadRoomJson(string id, ReadOnlySpan<byte> json)
		=> new(id, Point2.Zero, Room.DefaultSize, JsonUtil.NormalizeStringValues(JsonNode.Parse(json)?.AsObject() ?? []));

	static int RoomSublayerFromPath(string root, string path)
	{
		var relative = Path.GetRelativePath(root, path);
		var separator = relative.IndexOfAny([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar]);
		var directory = separator < 0 ? "" : relative[..separator];
		return directory.StartsWith("Sub", StringComparison.OrdinalIgnoreCase) &&
			int.TryParse(directory.AsSpan(3), out var sublayer) && sublayer > 0 ? sublayer : 0;
	}

	static void CreateDefaultStartRoom()
	{
		var room = new Room(defaultStartRoomId, Point2.Zero, Room.DefaultSize);
		AddRoom(room);
		room.Save();
	}

	static void AddRoom(Room room)
	{
		if (RoomsById.TryGetValue(room.Id, out var duplicate) && duplicate.Storage == room.Storage)
			throw new InvalidDataException($"Duplicate room id '{room.Id}' in '{duplicate.SourcePath}' and '{room.SourcePath}'.");

		if (RoomsById.TryGetValue(room.Id, out var existing) &&
			Rooms.TryGetValue((existing.Sublayer, existing.Cell), out var byOldCell) &&
			byOldCell == existing)
			Rooms.Remove((existing.Sublayer, existing.Cell));

		Rooms[(room.Sublayer, room.Cell)] = room;
		RoomsById[room.Id] = room;
	}

	public static bool SaveRoom(Room room, ReadOnlySpan<byte> json, bool overwriteExternal = false)
	{
		var path = GetRoomSavePath(room);
		Directory.CreateDirectory(Path.GetDirectoryName(path)!);
		if (!overwriteExternal && room.SourcePath is { } sourcePath && PathsEqual(sourcePath, path) && room.DiskRevision != null &&
			!RevisionEquals(room.DiskRevision, ReadRoomRevision(path)))
		{
			Console.Error.WriteLine($"Room save blocked: '{room.Id}' changed on disk.");
			RoomSaveConflict?.Invoke(room, path);
			return false;
		}
		File.WriteAllBytes(path, json);
		room.DiskRevision = RoomRevision(json);
		room.Storage = IsUserRoomPath(path) ? Room.StorageKind.User : Room.StorageKind.Bundled;
		room.SourcePath = path;
		SaveRoomColorFields(room, path);
		if (room.Storage == Room.StorageKind.User)
			ClearUserRoomTombstone(room.Id);
		return true;
	}

	internal static bool IsCurrentWorldRoomPath(string path)
		=> IsPathWithin(path, WorldRoomsPath);

	internal static bool RoomFileMatchesLoadedRevision(string path)
	{
		foreach (var room in RoomsById.Values)
			if (room.SourcePath is { } sourcePath && PathsEqual(sourcePath, path))
				return room.DiskRevision != null && RevisionEquals(room.DiskRevision, ReadRoomRevision(path));
		return false;
	}

	static byte[] RoomRevision(ReadOnlySpan<byte> json) => SHA256.HashData(json);

	static byte[]? ReadRoomRevision(string path)
	{
		try
		{
			return File.Exists(path) ? RoomRevision(File.ReadAllBytes(path)) : null;
		}
		catch (IOException)
		{
			return null;
		}
	}

	static bool RevisionEquals(byte[] expected, byte[]? actual)
		=> actual != null && expected.AsSpan().SequenceEqual(actual);

	static bool PathsEqual(string a, string b)
		=> string.Equals(Path.GetFullPath(a), Path.GetFullPath(b),
			OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

	static bool IsPathWithin(string path, string directory)
	{
		var relative = Path.GetRelativePath(Path.GetFullPath(directory), Path.GetFullPath(path));
		return relative != ".." && !Path.IsPathRooted(relative) &&
			!relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal);
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
		var oldForegroundColorPath = room.ForegroundColorField.SourcePath;
		var oldBackgroundColorPath = room.BackgroundColorField.SourcePath;
		var oldBundledPath = BundledRoomPath(oldId, room.Sublayer);
		var oldHadBundledRoom = File.Exists(oldBundledPath);
		room.Id = id;
		RoomsById.Remove(oldId);
		RoomsById[id] = room;
		room.Save();

		DeleteRoomFile(oldPath, oldId);
		DeleteColorFieldFile(oldForegroundColorPath);
		DeleteColorFieldFile(oldBackgroundColorPath);
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

		if (Rooms.TryGetValue((room.Sublayer, room.Cell), out var byCell) && byCell == room)
			Rooms.Remove((room.Sublayer, room.Cell));

		var bundledPath = BundledRoomPath(room.Id, room.Sublayer);
		var hadBundledRoom = File.Exists(bundledPath);
		DeleteColorFieldFile(room.ForegroundColorField.SourcePath);
		DeleteColorFieldFile(room.BackgroundColorField.SourcePath);
		DeleteRoomFile(room.SourcePath, room.Id);
		if (hadBundledRoom && !string.Equals(room.SourcePath, bundledPath, StringComparison.Ordinal))
			WriteUserRoomTombstone(room.Id);

		return true;
	}

	static string GetRoomSavePath(Room room)
	{
		if (room.Storage == Room.StorageKind.User)
			return UserRoomPath(room.Id, room.Sublayer);

		var bundledPath = BundledRoomPath(room.Id, room.Sublayer);
		if (CanWriteBundledRooms())
			return bundledPath;

		return UserRoomPath(room.Id, room.Sublayer);
	}

	static string BundledRoomPath(string id, int sublayer = 0)
		=> Path.Join(WorldRoomsPath, RoomSublayerDirectory(sublayer), $"{id}.json");

	static string UserRoomPath(string id, int sublayer = 0)
	{
		if (userRoomsPath == null)
			throw new InvalidOperationException("User room path has not been configured.");

		return Path.Join(userRoomsPath, currentWorldId, RoomSublayerDirectory(sublayer), $"{id}.json");
	}

	static string RoomSublayerDirectory(int sublayer) => sublayer > 0 ? $"Sub{sublayer}" : "";

	static void LoadRoomColorFields(Room room, string roomPath, bool fromContent = true)
	{
		LoadRoomColorField(room.ForegroundColorField, RoomColorFieldPath(roomPath, Factory.TileLayer.Foreground), room.Size, fromContent);
		LoadRoomColorField(room.BackgroundColorField, RoomColorFieldPath(roomPath, Factory.TileLayer.Background), room.Size, fromContent);
	}

	static void LoadRoomColorField(RoomColorField field, string path, Point2 size, bool fromContent)
	{
		var legacyPath = LegacyRoomColorFieldPath(path);
		try
		{
			if (fromContent)
			{
				if (Content.FileExists(path))
					field.Load(Content.ReadAllBytes(path), size, ContentIsZip ? null : Path.Join(AssetsPath, path));
				else if (Content.FileExists(legacyPath))
					field.Load(Content.ReadAllBytes(legacyPath), size, ContentIsZip ? null : Path.Join(AssetsPath, legacyPath));
				else
					return;
			}
			else if (File.Exists(path))
				field.Load(File.ReadAllBytes(path), size, path);
			else if (File.Exists(legacyPath))
				field.Load(File.ReadAllBytes(legacyPath), size, legacyPath);
		}
		catch (Exception exception)
		{
			Console.Error.WriteLine($"Could not load room color field '{path}': {exception.Message}");
		}
	}

	static void SaveRoomColorFields(Room room, string roomPath)
	{
		SaveRoomColorField(room.ForegroundColorField, RoomColorFieldPath(roomPath, Factory.TileLayer.Foreground));
		SaveRoomColorField(room.BackgroundColorField, RoomColorFieldPath(roomPath, Factory.TileLayer.Background));
	}

	static void SaveRoomColorField(RoomColorField field, string path)
	{
		if (!field.Dirty && string.Equals(field.SourcePath, path, StringComparison.Ordinal))
			return;

		if (!field.HasPaint)
		{
			DeleteColorFieldFile(field.SourcePath);
			if (!string.Equals(field.SourcePath, path, StringComparison.Ordinal))
				DeleteColorFieldFile(path);
			field.MarkSaved(null);
			return;
		}

		Directory.CreateDirectory(Path.GetDirectoryName(path)!);
		File.WriteAllBytes(path, field.WritePng());
		DeleteColorFieldFile(LegacyRoomColorFieldPath(path));
		field.MarkSaved(path);
	}

	static string RoomColorFieldPath(string roomPath, Factory.TileLayer layer)
	{
		var extension = Path.GetExtension(roomPath);
		var stem = extension.Length > 0 ? roomPath[..^extension.Length] : roomPath;
		return $"{stem}.{(layer == Factory.TileLayer.Foreground ? "foreground" : "background")}-color.png";
	}

	static string LegacyRoomColorFieldPath(string path)
		=> Path.ChangeExtension(path, ".qoi");

	static void DeleteColorFieldFile(string? path)
	{
		if (path != null && File.Exists(path) && CanDeleteFile(path))
			File.Delete(path);
	}

	static bool IsUserRoomPath(string path)
	{
		if (UserWorldRoomsPath is not { } userWorldRoomsPath)
			return false;

		var relative = Path.GetRelativePath(Path.GetFullPath(userWorldRoomsPath), Path.GetFullPath(path));
		return relative != ".." && !relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal);
	}

	static bool RoomFileExists(string id)
	{
		if (Directory.Exists(WorldRoomsPath) && Directory.EnumerateFiles(WorldRoomsPath, $"{id}.json", SearchOption.AllDirectories).Any())
			return true;
		if (UserWorldRoomsPath is { } userWorldRoomsPath && Directory.Exists(userWorldRoomsPath) &&
			Directory.EnumerateFiles(userWorldRoomsPath, $"{id}.json", SearchOption.AllDirectories).Any())
			return true;
		return false;
	}

	static void DeleteRoomFile(string? path, string id)
	{
		if (path == null || !File.Exists(path))
			return;

		if (CanDeleteFile(path))
		{
			File.Delete(path);
			RoomFileDeleted?.Invoke(path);
		}
		else if (!IsUserRoomPath(path))
			WriteUserRoomTombstone(id);
	}

	static bool CanDeleteFile(string path)
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

	static bool CanWriteBundledRooms()
	{
		if (ContentIsZip)
			return false;

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

	static HashSet<string> LoadUserRoomTombstones()
	{
		var deleted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		var path = UserDeletedRoomsPath();
		if (path == null || !Directory.Exists(path))
			return deleted;

		foreach (var file in Directory.EnumerateFiles(path))
			deleted.Add(Path.GetFileName(file));

		return deleted;
	}

	static void WriteUserRoomTombstone(string id)
	{
		var path = UserDeletedRoomsPath();
		if (path == null)
			return;

		Directory.CreateDirectory(path);
		File.WriteAllText(Path.Join(path, id), string.Empty);
	}

	static void ClearUserRoomTombstone(string id)
	{
		var path = UserDeletedRoomsPath();
		if (path == null)
			return;

		var file = Path.Join(path, id);
		if (File.Exists(file))
			File.Delete(file);
	}

	static string? UserDeletedRoomsPath()
		=> userRoomsPath != null ? Path.Join(userRoomsPath, currentWorldId, deletedRoomsFolderName) : null;

	public static void RebuildRoomGeometry()
	{
		foreach (var room in Rooms.Values)
			room.RebuildGeometry();
	}

	static Image GenerateNormalMap(Image source)
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

	static float HeightNormalStrength => 1.25f;

	static Image GenerateNormalMap(Image height, Image source)
	{
		var normal = new Image(source.Width, source.Height);
		for (var y = 0; y < source.Height; y++)
			for (var x = 0; x < source.Width; x++)
			{
				var alpha = AlphaAt(source, x, y);
				if (alpha <= 0)
					continue;

				var n = NormalFromHeight(height, height.Bounds, x, y, wrap: false);
				normal[x, y] = new Color(
					n.X * 0.5f + 0.5f,
					n.Y * 0.5f + 0.5f,
					n.Z * 0.5f + 0.5f,
					alpha);
			}
		return normal;
	}

	static Vector3 NormalFromHeight(Image image, RectInt bounds, int x, int y, bool wrap)
	{
		var topLeft = HeightAt(image, bounds, x - 1, y - 1, wrap);
		var top = HeightAt(image, bounds, x, y - 1, wrap);
		var topRight = HeightAt(image, bounds, x + 1, y - 1, wrap);
		var left = HeightAt(image, bounds, x - 1, y, wrap);
		var right = HeightAt(image, bounds, x + 1, y, wrap);
		var bottomLeft = HeightAt(image, bounds, x - 1, y + 1, wrap);
		var bottom = HeightAt(image, bounds, x, y + 1, wrap);
		var bottomRight = HeightAt(image, bounds, x + 1, y + 1, wrap);
		var sx = topLeft + 2 * left + bottomLeft - topRight - 2 * right - bottomRight;
		var sy = topLeft + 2 * top + topRight - bottomLeft - 2 * bottom - bottomRight;
		return Vector3.Normalize(new Vector3(sx, sy, 4 / HeightNormalStrength));
	}

	static float HeightAt(Image image, RectInt bounds, int x, int y, bool wrap)
	{
		if (wrap)
		{
			x = (x + bounds.Width) % bounds.Width;
			y = (y + bounds.Height) % bounds.Height;
		}
		else if (x < 0 || y < 0 || x >= bounds.Width || y >= bounds.Height)
			return 0;
		var color = image[bounds.X + x, bounds.Y + y];
		return (color.R * 0.2126f + color.G * 0.7152f + color.B * 0.0722f) * color.A / (255 * 255);
	}

	static Image ExtractTile(Image source, RectInt sourceRect)
	{
		var output = new Image(sourceRect.Width, sourceRect.Height);
		for (var y = 0; y < sourceRect.Height; y++)
			for (var x = 0; x < sourceRect.Width; x++)
				output[x, y] = source[sourceRect.X + x, sourceRect.Y + y];
		return output;
	}

	static Image GenerateSlopeTile(Image source, Room.SlopeDirection direction, Room.SlopeFill fill, int runLength, int runIndex, Image? edge = null)
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

	static bool IsSlopeMaskSolid(int x, int y, Room.SlopeDirection direction, Room.SlopeFill fill, int runLength, int runIndex)
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

	static bool IsSlopeMaskEdge(int x, int y, Room.SlopeDirection direction, Room.SlopeFill fill, int runLength, int runIndex)
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

	static void AddSlopeSubtextures(Packer packer, string tilesetName, Image baseTile, Image? edge = null)
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

	static void AddSlopeSubtextures(Packer packer, string tilesetName, Image baseTile, int variantIndex, Image? edge = null)
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

	static float AlphaAt(Image image, int x, int y)
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

	public static Sprite? GetSprite(string name, bool logMissing = true)
	{
		var sprite = Sprites.GetValueOrDefault(name);
#if DEBUG
		if (logMissing && sprite == null) Console.WriteLine($"Error: Sprite '{name}' not found");
#endif
		return sprite;
	}

	public static Tileset? GetTileset(string name, bool logMissing = false)
	{
		var tileset = Tilesets.GetValueOrDefault(name);
		if (logMissing && tileset == null)
			Console.WriteLine($"Error: Tileset '{name}' not found");
		return tileset;
	}
	public static Room? GetRoom(Point2 cell, int sublayer = 0) => Rooms.GetValueOrDefault((sublayer, cell));
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

	public static Room? FindAdjacentRoomPlayMode(Room room, Point2 direction, Vector2 exitPosition)
	{
		var bounds = room.WorldBounds;
		Room? best = null;
		var bestDistance = float.MaxValue;

		foreach (var candidate in RoomsById.Values)
		{
			if (candidate == room || candidate.Sublayer != room.Sublayer)
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

	public static Room? FindAdjacentRoom(Room room, Point2 direction, Vector2 exitPosition)
	{
		var bounds = room.WorldBounds;
		Room? best = null;
		var bestArea = 0;

		var adjacentBounds = bounds + direction * bounds.Size;

		var attempts = 10;
		while (attempts > 0)
		{
			foreach (var candidate in RoomsById.Values) if (candidate != room && candidate.Sublayer == room.Sublayer)
			{
				var candidateBounds = candidate.WorldBounds;
				var intersection = candidateBounds.GetIntersection(adjacentBounds);
				var area = intersection.Area;
				if (area > bestArea)
				{
					best = candidate;
					bestArea = area;
				}
			}

			if (best != null) return best;

			attempts -= 1;
			adjacentBounds.Position += (Point2)(direction * bounds.Size * 0.5f);
			adjacentBounds.Inflate(Math.Min(adjacentBounds.Width, adjacentBounds.Height) * 0.5f);
		}

		return null;
	}

	static bool TouchesExitEdge(RectInt from, RectInt to, Point2 direction, Vector2 exitPosition)
	{
		if (direction.X > 0) return to.Left == from.Right && ContainsAxis(to.Top, to.Bottom, exitPosition.Y);
		if (direction.X < 0) return to.Right == from.Left && ContainsAxis(to.Top, to.Bottom, exitPosition.Y);
		if (direction.Y > 0) return to.Top == from.Bottom && ContainsAxis(to.Left, to.Right, exitPosition.X);
		if (direction.Y < 0) return to.Bottom == from.Top && ContainsAxis(to.Left, to.Right, exitPosition.X);
		return false;
	}

	static bool ContainsAxis(float min, float max, float value)
		=> value >= min && value < max;

	static float TransitionAxisDistance(RectInt bounds, Point2 direction, Vector2 exitPosition)
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

	static string SlopeSubtextureName(string tilesetName, Room.SlopeDirection direction, Room.SlopeFill fill, int runLength, int runIndex)
		=> $"slopes/{tilesetName}/{direction}/{fill}/{runLength}/{runIndex}";

	static string SlopeVariantSubtextureName(string tilesetName, int variantIndex, Room.SlopeDirection direction, Room.SlopeFill fill, int runLength, int runIndex)
		=> $"slopes/{tilesetName}/variants/{variantIndex}/{direction}/{fill}/{runLength}/{runIndex}";

}
