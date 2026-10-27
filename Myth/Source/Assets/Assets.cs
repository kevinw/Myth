
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
	static readonly Dictionary<string, Sprite.EmissiveLight> BakedEmissiveLights = new(StringComparer.OrdinalIgnoreCase);
	static readonly Dictionary<string, Vector2> EmissiveLightCenters = new(StringComparer.OrdinalIgnoreCase);

	const string assetsFolderName = "Assets";
	const string roomsFolderName = "Rooms";
	const string packedVisualsFolderName = "Packed/Visuals";
	const string packedVisualsManifestName = "manifest.bin";
	const uint visualAssetPackMagic = 0x5356494D; // MIVS, little-endian.
	const int visualAssetPackVersion = 16; // v16: baked sprite emissive lights
	const string socketLayerSuffix = "_socket";
	const string defaultWorldId = "Myth";
	const string worldManifestName = "world.json";
	const string defaultStartRoomId = "start";
	static string? path = null;
	static string? visualAssetPackPath = null;
	static bool useVisualAssetPack = false;
	static string currentWorldId = defaultWorldId;

	public static string AssetsPath
	{
		get
		{
			// Search up from the executable (dev: bin/<config> under the project; bundle:
			// Contents/MacOS next to Resources), then from the working directory.
			path ??= FindAssetsFrom(AppContext.BaseDirectory)
				?? FindAssetsFrom(Environment.CurrentDirectory)
				?? throw new DirectoryNotFoundException(
					$"No {assetsFolderName} folder containing {roomsFolderName}/ or {contentPakName} above " +
					$"'{AppContext.BaseDirectory}' or '{Environment.CurrentDirectory}'.");
			return path;
		}
	}

	static string? FindAssetsFrom(string start)
	{
		var directory = Path.GetFullPath(start);
		for (var depth = 0; depth < 10 && directory != null; depth++)
		{
			var candidate = Path.Join(directory, assetsFolderName);
			if (IsAssetsFolder(candidate))
				return candidate;
			// macOS bundle layout: the executable lives in Contents/MacOS while data must
			// sit in Contents/Resources (codesign rejects non-code under MacOS).
			var resources = Path.Join(directory, "Resources", assetsFolderName);
			if (IsAssetsFolder(resources))
				return resources;
			directory = Path.GetDirectoryName(directory);
		}
		return null;
	}

	// Any folder named Assets (an empty ~/Assets, say) isn't ours: a dev tree has Rooms/, a
	// packaged build has Content.pak.
	static bool IsAssetsFolder(string candidate)
		=> Directory.Exists(Path.Join(candidate, roomsFolderName)) || File.Exists(Path.Join(candidate, contentPakName));

	public static string RoomsPath => Path.Join(AssetsPath, roomsFolderName);
	// Authoring inputs (.ase sources, kit specs, bake config). Read at runtime in dev and
	// by the pack bake; excluded wholesale from web bundles, where the pack replaces them.
	public static string SourcesPath => Path.Join(AssetsPath, "Sources");

	const string contentPakName = "Content.pak";
	const uint contentPakKey = 0x5ac1e7b9;
	static StorageContainer? content;
	static string? contentPakOverride;

	/// <summary>
	/// Read-only bundled content, addressed by Assets-relative keys ("Rooms/Myth/x.json").
	/// Packaged builds read Assets/Content.pak (the Assets folder then carries only the zip
	/// plus loose FMOD banks); dev and editor builds read the Assets folder directly, so
	/// authoring and file watching keep working. Writable flows (editor room saves, pack
	/// bakes) never go through here — they use the real file system.
	/// </summary>
	public static StorageContainer Content => content ??= OpenContent();

	/// True when bundled content is being served from Content.pak (always read-only).
	/// Resolves Content first, so it is correct however early it is read.
	public static bool ContentIsPak
	{
		get
		{
			_ = Content;
			return contentIsPak;
		}
	}

	static bool contentIsPak;

	/// Forces content to load from the given zip (the --content-pak flag), including in
	/// editor builds, which otherwise ignore Content.pak so a stale pack can't shadow the
	/// files being edited.
	public static void ConfigureContentPak(string? zipPath)
	{
		contentPakOverride = string.IsNullOrWhiteSpace(zipPath) ? null : zipPath;
		content?.Dispose();
		content = null;
	}

	// Writes a target's complete shipped Assets folder to obj/Content/<target>/Assets --
	// Content.pak plus the loose FMOD banks (FMOD opens banks by real path) -- and audits it.
	// Publish scripts and the browser bundle copy that folder wholesale.
	public static void StageContent(string target, string? worldId)
	{
		target = target.Trim().ToLowerInvariant();
		worldId = string.IsNullOrWhiteSpace(worldId) ? defaultWorldId : worldId.Trim();
		var stage = Path.GetFullPath(Path.Join(AssetsPath, "..", "obj", "Content", target, assetsFolderName));
		if (Directory.Exists(stage))
			Directory.Delete(stage, recursive: true);
		BuildContentPak(Path.Join(stage, contentPakName), target, worldId);

		var banks = Path.Join("Sounds", "FMOD", "Build", BankFolder(target));
		Directory.CreateDirectory(Path.Join(stage, banks));
		foreach (var bank in Directory.EnumerateFiles(Path.Join(AssetsPath, banks), "*.bank"))
		{
			var dest = Path.Join(stage, banks, Path.GetFileName(bank));
			File.Copy(bank, dest);
			// deploy-deck.sh rsyncs the publish output; fresh mtimes would resend every bank.
			File.SetLastWriteTimeUtc(dest, File.GetLastWriteTimeUtc(bank));
		}

		AuditStagedContent(stage, target, worldId);
		Console.WriteLine($"Staged {target}/{worldId} content: {stage}");
	}

	static string ShaderExtension(string target) => target switch
	{
		"mac" => "msl",
		"windows" => "dxil",
		"linux" => "spv",
		"browser" => "wgsl",
		_ => throw new ArgumentException($"Unknown content target '{target}'; expected mac, windows, linux, or browser."),
	};

	static string BankFolder(string target) => target == "browser" ? "Mobile" : "Desktop";

	// Allowlist audit of everything a target ships, so a content bug (authoring sources, another
	// world's rooms, the wrong shader flavor, LFS pointers, stray build output) fails staging
	// instead of shipping. A new content folder means adding a rule here. Reads the pak the way
	// the runtime does, so it also proves the game can open it.
	static void AuditStagedContent(string stage, string target, string worldId)
	{
		var world = System.Text.RegularExpressions.Regex.Escape(worldId);
		(string Pattern, string[] Extensions)[] looseRules =
		[
			(@"^Content\.pak$", []),
			($"^Sounds/FMOD/Build/{BankFolder(target)}/[^/]+$", ["bank"]),
		];
		(string Pattern, string[] Extensions)[] pakRules =
		[
			(@"^world\.json$", []),
			(@"^(Actors|Attract|Particles|Storylets|Voice)/[^/]+$", ["json"]),
			(@"^Attribution/[^/]+$", ["png"]),
			($"^{BlobContent.Folder}/[^/]+$", ["png"]),
			(@"^Fonts/[^/]+$", ["ttf", "txt"]),
			(@"^NCA/(DynamicTexture/)?[^/]+$", ["bin"]),
			(@"^Packed/Visuals/(manifest\.bin|atlas\.qoi|lighting-noise\.qoi)$", []),
			($@"^Packed/Visuals/{WorldMap.Folder}/{world}\.(qoi|json)$", []),
			(@"^Packed/Visuals/textures/", ["png"]),
			(@"^Packed/Visuals/(surfaces|costumes|heads|ribbon-styles)/[^/]+$", ["json"]),
			(@"^Rigs/[^/]+$", ["json", "ozzpack"]),
			($@"^Rooms/{world}\.regions\.json$", []),
			($"^Rooms/{world}/", ["json"]),
			(@"^Shaders/Compiled/[^/]+$", [ShaderExtension(target)]),
		];

		var problems = new List<string>();
		var folders = new SortedDictionary<string, (int Count, long Size)>();
		void Check(string name, long size, Func<Stream> open, (string Pattern, string[] Extensions)[] rules)
		{
			var extension = Path.GetExtension(name).TrimStart('.');
			var rule = rules.FirstOrDefault(rule => System.Text.RegularExpressions.Regex.IsMatch(name, rule.Pattern));
			if (name.Split('/').Any(part => part.StartsWith('.')))
				problems.Add($"{name}: dot-prefixed file");
			else if (rule.Pattern == null)
				problems.Add($"{name}: not in an allowed location");
			else if (rule.Extensions.Length > 0 && !rule.Extensions.Contains(extension))
				problems.Add($"{name}: .{extension} not allowed here (expected {string.Join(", ", rule.Extensions)})");
			if (size == 0)
				problems.Add($"{name}: empty");
			else if (size < 200)
			{
				using var stream = open();
				if (IsGitLfsPointer(stream))
					problems.Add($"{name}: Git LFS pointer, not content");
			}
			var folder = name.Contains('/') ? name[..name.IndexOf('/')] : "(root)";
			var total = folders.GetValueOrDefault(folder);
			folders[folder] = (total.Count + 1, total.Size + size);
		}

		foreach (var file in Directory.EnumerateFiles(stage, "*", SearchOption.AllDirectories))
			if (Path.GetFileName(file) != contentPakName)
				Check(Path.GetRelativePath(stage, file).Replace(Path.DirectorySeparatorChar, '/'), new FileInfo(file).Length, () => File.OpenRead(file), looseRules);
		if (!folders.ContainsKey("Sounds"))
			problems.Add("no FMOD banks; build the FMOD project");

		var pakPath = Path.Join(stage, contentPakName);
		using var pak = OpenContentPak(pakPath);
		if ((pak is Kit.ScrambleStream) == (target == "browser"))
			problems.Add(target == "browser" ? "browser pak is scrambled (the network tab exposes it anyway)" : $"{target} pak is not scrambled");
		using var zip = new System.IO.Compression.ZipArchive(pak, System.IO.Compression.ZipArchiveMode.Read);
		var seen = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		foreach (var entry in zip.Entries)
		{
			if (!seen.TryAdd(entry.FullName, entry.FullName))
				problems.Add($"{entry.FullName}: collides with {seen[entry.FullName]} (content lookups ignore case)");
			Check(entry.FullName, entry.Length, entry.Open, pakRules);
		}
		if (zip.GetEntry(worldManifestName) is not { } worldEntry)
			problems.Add($"{worldManifestName} is missing");
		else
		{
			using var worldStream = worldEntry.Open();
			var packedWorld = JsonNode.Parse(worldStream)?["world"]?.GetValue<string>();
			if (packedWorld != worldId)
				problems.Add($"{worldManifestName} names '{packedWorld}', expected '{worldId}'");
		}
		if (!zip.Entries.Any(entry => entry.FullName.StartsWith($"Rooms/{worldId}/", StringComparison.Ordinal)))
			problems.Add($"no rooms for world '{worldId}'");

		Console.WriteLine($"Staged content audit ({target}/{worldId}), pak {new FileInfo(pakPath).Length / 1024} KB:");
		foreach (var (folder, (count, size)) in folders.OrderByDescending(pair => pair.Value.Size))
			Console.WriteLine($"  {size / 1024,7} KB  {count,4}  {folder}");
		if (problems.Count > 0)
			throw new InvalidOperationException($"Staged content audit failed ({problems.Count}):\n  {string.Join("\n  ", problems)}");
	}

	public static void UnscrambleContentPak(string pakPath)
	{
		var zipPath = Path.ChangeExtension(pakPath, ".zip");
		using (var pak = OpenContentPak(pakPath))
		using (var zip = File.Create(zipPath))
			pak.CopyTo(zip);
		Console.WriteLine($"Wrote unscrambled zip: {zipPath}");
	}

	// Zips the runtime-loaded slice of the Assets folder into a Content.pak for packaged
	// builds. Keeps only the requested world and shader backend. Excluded: Sources/
	// (authoring; the visual pack replaces it), Sounds/ (banks ship loose, see StageContent),
	// editor fonts, uncompiled shaders, and dot-prefixed litter.
	// Entry names are Assets-relative with forward slashes — the same keys Content serves.
	static void BuildContentPak(string outputPath, string target, string worldId)
	{
		ActorSpecs.ClearActorTypes();
		GeneratedActors.RegisterActorSpecs();
		ActorSpecs.LoadSpecs();
		NctTiles.LoadSpecs(Path.Join(SourcesPath, "Tilesets"));
		var manifestPath = Path.Join(DefaultVisualAssetPackPath, packedVisualsManifestName);
		if (!File.Exists(manifestPath))
			throw new InvalidOperationException("Visual asset pack is missing. Build it with --build-asset-pack first.");
		using (var reader = new BinaryReader(File.OpenRead(manifestPath)))
		{
			if (reader.BaseStream.Length < sizeof(uint) + sizeof(int) || reader.ReadUInt32() != visualAssetPackMagic)
				throw new InvalidOperationException("Visual asset pack manifest is invalid. Rebuild it with --build-asset-pack.");
			var version = reader.ReadInt32();
			if (version != visualAssetPackVersion)
				throw new InvalidOperationException($"Visual asset pack is stale. Expected version {visualAssetPackVersion}, found {version}. Rebuild it with --build-asset-pack.");
		}

		var shaderExtension = ShaderExtension(target);
		if (worldId.IndexOfAny(['/', '\\']) >= 0 || !Directory.Exists(Path.Join(RoomsPath, worldId)))
			throw new ArgumentException($"Unknown content world '{worldId}'.");

		outputPath = Path.GetFullPath(outputPath);
		if (Path.GetDirectoryName(outputPath) is { Length: > 0 } outputDirectory)
			Directory.CreateDirectory(outputDirectory);

		var dependencies = CollectContentDependencies(worldId);
		dependencies.Validate(AssetsPath);
		var packedWeights = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

		var packedSurfaces = 0;
		var packedSurfaceTextures = 0;
		var skippedSurfaceAssets = 0;
		long skippedSurfaceBytes = 0;

		using Stream stream = target == "browser" ? File.Create(outputPath) : new Kit.ScrambleStream(File.Create(outputPath), contentPakKey);
		using var zip = new System.IO.Compression.ZipArchive(stream, System.IO.Compression.ZipArchiveMode.Create);
		using (var worldStream = zip.CreateEntry(worldManifestName).Open())
		using (var writer = new Utf8JsonWriter(worldStream))
		{
			writer.WriteStartObject();
			writer.WriteString("world", worldId);
			writer.WriteEndObject();
		}

		var count = 1;
		var skippedWeights = 0;
		long skippedWeightBytes = 0;
		foreach (var file in Directory.EnumerateFiles(AssetsPath, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
		{
			var key = Path.GetRelativePath(AssetsPath, file).Replace(Path.DirectorySeparatorChar, '/');
			if (!ContentPakIncludes(key, shaderExtension, worldId))
				continue;
			if (key.StartsWith($"{BlobContent.Folder}/", StringComparison.Ordinal) && !dependencies.Includes(key))
				continue;

			var surface = IsPackSurfaceFile(key, out _);
			var surfaceTexture = IsPackTextureFile(key, out _);
			if ((surface || surfaceTexture) && !dependencies.Includes(key))
			{
				skippedSurfaceAssets++;
				skippedSurfaceBytes += new FileInfo(file).Length;
				continue;
			}
			if (surface)
				packedSurfaces++;
			else if (surfaceTexture)
				packedSurfaceTextures++;

			if (IsDynamicTextureWeight(key, out var weightName))
			{
				if (!dependencies.Includes(key))
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

		var missingWeights = dependencies.Files
			.Where(key => IsDynamicTextureWeight(key, out var name) && !packedWeights.Contains(name))
			.ToArray();
		if (missingWeights.Length > 0)
			throw new InvalidOperationException($"Missing referenced neural texture models: {string.Join(", ", missingWeights)}. Run 'git lfs pull' and check the room/NCT spec references.");

		Console.WriteLine($"Wrote {target}/{worldId} content pak: {outputPath} ({count} entries, {new FileInfo(outputPath).Length / 1024} KB); "
			+ $"packed {packedWeights.Count} neural texture models, skipped {skippedWeights} unreferenced ({skippedWeightBytes / 1024} KB); "
			+ $"surfaces: kept {packedSurfaces} + {packedSurfaceTextures} textures, skipped {skippedSurfaceAssets} unreferenced ({skippedSurfaceBytes / 1024} KB).");
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
		using var stream = File.OpenRead(path);
		return IsGitLfsPointer(stream);
	}

	static bool IsGitLfsPointer(Stream stream)
	{
		const string signature = "version https://git-lfs.github.com/spec/v1";
		Span<byte> buffer = stackalloc byte[signature.Length];
		return stream.ReadAtLeast(buffer, buffer.Length, throwOnEndOfStream: false) == buffer.Length && Encoding.ASCII.GetString(buffer) == signature;
	}

	// Generated declarations cover code defaults; resolved prefab/room JSON covers authored
	// overrides. Kinds expand typed references into files (and later transitive dependencies).
	static ContentDependencyCollector CollectContentDependencies(string worldId)
	{
		var collector = new ContentDependencyCollector();
		GeneratedContentReferences.Collect(collector);
		foreach (var spec in NctTiles.Specs)
		{
			collector.Add(new ContentRef<NeuralWeightsContent>(spec.Interior), $"NCT tile '{spec.Name}' Interior");
			if (!string.IsNullOrEmpty(spec.Edge))
				collector.Add(new ContentRef<NeuralWeightsContent>(spec.Edge), $"NCT tile '{spec.Name}' Edge");
		}

		var worldRoomsPath = Path.Join(RoomsPath, worldId);
		if (!Directory.Exists(worldRoomsPath))
			return collector;

		foreach (var (id, spec) in ActorSpecs.Specs)
			ActorSpecs.CollectContentReferences(spec, collector, $"actor prefab '{id}'");

		foreach (var file in Directory.EnumerateFiles(worldRoomsPath, "*.json", SearchOption.AllDirectories))
		{
			JsonNode? root;
			try
			{
				root = JsonUtil.Parse(File.ReadAllText(file));
			}
			catch (Exception exception)
			{
				throw new InvalidDataException($"Could not parse room '{file}' while collecting content dependencies.", exception);
			}

			var room = Path.GetRelativePath(worldRoomsPath, file).Replace(Path.DirectorySeparatorChar, '/');
			if (root?["backdropSurface"]?.GetValue<string>() is { Length: > 0 } backdrop)
				collector.Add(new ContentRef<SurfaceContent>(backdrop), $"room '{room}' backdropSurface");
			if (root?["colorFields"] is JsonObject colorFields)
				foreach (var (layer, blob) in colorFields)
					collector.Add(new ContentRef<BlobContent>(blob!.GetValue<string>()), $"room '{room}' {layer} color field");

			if (root?["actors"] is not JsonArray actors)
				continue;

			for (var index = 0; index < actors.Count; index++)
				if (actors[index] is JsonObject spec)
				{
					var actor = ActorSpecs.GetActorId(spec) ?? ActorSpecs.GetTypeName(spec) ?? index.ToString();
					ActorSpecs.CollectContentReferences(spec, collector, $"room '{room}' actor '{actor}'");
				}
		}
		return collector;
	}

	static bool ContentPakIncludes(string key, string shaderExtension, string worldId)
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
		// Only this world's map, not other worlds' or the bake fingerprints.
		if (key.StartsWith($"{packedVisualsFolderName}/{WorldMap.Folder}/", StringComparison.OrdinalIgnoreCase))
			return key.Equals($"{packedVisualsFolderName}/{WorldMap.ImageFile(worldId)}", StringComparison.OrdinalIgnoreCase)
				|| key.Equals($"{packedVisualsFolderName}/{WorldMap.BoundsFile(worldId)}", StringComparison.OrdinalIgnoreCase);
		if (key.StartsWith("Shaders/", StringComparison.OrdinalIgnoreCase))
			return key.StartsWith("Shaders/Compiled/", StringComparison.OrdinalIgnoreCase)
				&& key.EndsWith($".{shaderExtension}", StringComparison.OrdinalIgnoreCase);
		if (key.Equals(worldManifestName, StringComparison.OrdinalIgnoreCase))
			return false;

		return true;
	}

	// Browser paks are plain zips (the network tab exposes them anyway); the rest are scrambled.
	static Stream OpenContentPak(string path)
	{
		var file = File.OpenRead(path);
		Span<byte> magic = stackalloc byte[2];
		file.ReadExactly(magic);
		file.Position = 0;
		return magic is [(byte)'P', (byte)'K'] ? file : new Kit.ScrambleStream(file, contentPakKey);
	}

	static StorageContainer OpenContent()
	{
		var zipPath = contentPakOverride;
		if (zipPath == null)
		{
			// Packaged builds (desktop .app, browser wasm FS) carry
			// Assets/Content.pak and no loose Rooms folder. A dev tree always has Rooms/,
			// so it keeps reading live files even when a stale zip is lying around; a zip
			// can still be forced there with --content-pak.
			var bundledZip = Path.Join(AssetsPath, contentPakName);
			if (File.Exists(bundledZip) && !Directory.Exists(RoomsPath))
				zipPath = bundledZip;
		}
		if (zipPath != null)
		{
			var fullPath = Path.GetFullPath(zipPath);
			contentIsPak = true;
			Console.WriteLine($"Bundled content: pak ({fullPath})");
			// Case-insensitive lookups match dev-mode behavior on macOS/Windows, where the
			// filesystem forgives casing mismatches that would otherwise surface only in
			// packaged builds.
			return new ZipStorage(() => OpenContentPak(fullPath), ignoreCase: true);
		}

		contentIsPak = false;
		Console.WriteLine($"Bundled content: filesystem ({AssetsPath})");
		return new FolderStorage(AssetsPath);
	}
	public static string DefaultVisualAssetPackPath => Path.Join(AssetsPath, packedVisualsFolderName);
	public static string CurrentWorldId => currentWorldId;
	public static string WorldRoomsPath => Path.Join(RoomsPath, currentWorldId);
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
		Console.WriteLine($"Selected world: {worldId}");
	}

	public static void SelectPackagedWorld()
	{
		if (!Content.FileExists(worldManifestName))
			throw new InvalidDataException("Content.pak is missing world.json. Rebuild the content pak.");
		var worldId = JsonUtil.Parse(Content.ReadAllBytes(worldManifestName))?["world"]?.GetValue<string>();
		if (string.IsNullOrWhiteSpace(worldId) || worldId.IndexOfAny(['/', '\\']) >= 0)
			throw new InvalidDataException("Content.pak has an invalid world in world.json.");
		SelectWorld(worldId);
	}

	public static void Load(GraphicsDevice gfx, bool dumpPackedAtlases = false)
		=> Reload(gfx, true, dumpPackedAtlases, buildPackPath: null);

	public static void Reload(GraphicsDevice gfx)
		=> Reload(gfx, false, dumpPackedAtlases: false, buildPackPath: null);

	public static void BuildVisualAssetPack(string? path = null, bool dumpPackedAtlases = false)
	{
		using var bake = BakeVisualAssets();
		if (dumpPackedAtlases)
			DumpPackedAtlasPages(bake.Output);
		var directory = path ?? DefaultVisualAssetPackPath;
		WriteVisualAssetPack(directory, bake);
		ReportWorldMapState(directory);
	}

	/// The world map is a GPU render of every room, so this build (which runs headless, with no
	/// graphics device) can only leave alone whatever a previous in-app bake wrote. It is written
	/// into the pack folder and never deleted by a rebuild, so a rebuilt pack keeps its map.
	static void ReportWorldMapState(string directory)
	{
		var maps = Directory.Exists(Path.Join(directory, WorldMap.Folder))
			? Directory.GetFiles(Path.Join(directory, WorldMap.Folder), "*.qoi").Select(Path.GetFileNameWithoutExtension).Order().ToArray()
			: [];
		Console.WriteLine(maps.Length > 0
			? $"World maps: kept existing bakes for {string.Join(", ", maps)} (rebake in the editor if rooms changed)."
			: $"World maps: none in {directory}. Bake one from the editor toolbar (Bake Map).");
	}

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
		var packOnly = useVisualAssetPack || OperatingSystem.IsBrowser() || ContentIsPak || !Directory.Exists(SourcesPath);
		if (buildPackPath == null && packOnly && TryLoadVisualAssetPack(gfx, reloadRooms))
			return;
#if MYTHSHIP
		throw new InvalidDataException("Shipped builds load visuals only from the visual asset pack.");
#else
		using var bake = BakeVisualAssets();
		var output = bake.Output;

		// load main font file
#if !BROWSER
		EnsureFonts(gfx);
#endif

		if (dumpPackedAtlases)
			DumpPackedAtlasPages(output);

		// create texture file
		var atlas = new Texture(gfx, output.Pages[0], name: "Atlas");
		var lightingNoise = Assets.LoadAsepriteTexture(gfx, Path.Join("Sources", "Textures", "noise_tess2_seamless.ase"), "LightingNoise");
		var textures2D = Assets.LoadTextureBucket(gfx, Content, "Sources/Textures/Patterns", "patterns");
		LoadVisualAssetPackManifest(atlas, lightingNoise, textures2D, BuildVisualAssetPackManifest(bake, "", null));
		BuildSurfaces(gfx, Content, "Sources/Surfaces");
		LoadCostumes(Content, "Sources/Costumes");
		LoadHeadShapes(Content, "Sources/Heads");
		LoadRibbonStyles(Content, "Sources/RibbonStyles");

		if (buildPackPath != null)
			WriteVisualAssetPack(buildPackPath, bake);

		ActorSpecs.LoadSpecs();
		ParticleEffectAssets.Load();
		if (reloadRooms)
			LoadRooms();
		LoadWorldMap(gfx);
#endif
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
		var kitSourceFiles = new Dictionary<string, Aseprite>(StringComparer.OrdinalIgnoreCase);
		var generatedTileImages = new Dictionary<string, List<Image>>();
		var generatedNormalImages = new Dictionary<string, List<Image>>();
		var generatedOccluders = new Dictionary<string, List<List<Tileset.OccluderSegment[]>>>();
		var generatedSlopeEdgeImages = new Dictionary<string, List<Image>>();
		var kitGeneratedTilesets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

		GeneratedTransitions.Clear();
		ExpandedFrameDurations.Clear();
		ExpandedFrameMap.Clear();
		BakedEmissiveLights.Clear();
		EmissiveLightCenters.Clear();
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
			if (ase.Frames.Length == 0)
				continue;
			// Everything under Kits/ is kit source art read through slices, never a tileset of its own
			// -- packing it would burn atlas space on pixels no room ever draws.
			if (name.StartsWith($"{KitSourceFolder}/", StringComparison.OrdinalIgnoreCase))
				kitSourceFiles.Add(name[(KitSourceFolder.Length + 1)..], ase);
			else
				tilesetFiles.Add(name, ase);
		}

		foreach (var (name, ase) in tilesetFiles.ToArray())
		{
			if (!name.EndsWith("-kit", StringComparison.OrdinalIgnoreCase))
				continue;

			GenerateDualTilesetFromKit(name, ase, out var baseName, out var baseTiles, out var baseSlopeEdges, out var baseNormals, out var dualName, out var dualVariants, out var dualNormals, out var dualOccluders);
			generatedTileImages[baseName] = baseTiles;
			generatedTileImages[dualName] = dualVariants;
			generatedNormalImages[dualName] = dualNormals;
			if (baseNormals.Count > 0)
				generatedNormalImages[baseName] = baseNormals;
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

			GenerateDualTilesetFromSpec(spec, maskKit, KitSourceFor(spec, kitSourceFiles), out var baseTiles, out var baseSlopeEdges, out var baseNormals, out var dualVariants, out var dualNormals, out var dualOccluders);
			generatedTileImages[spec.Name] = baseTiles;
			generatedTileImages[spec.DualName] = dualVariants;
			generatedNormalImages[spec.DualName] = dualNormals;
			if (baseNormals.Count > 0)
				generatedNormalImages[spec.Name] = baseNormals;
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

			using var baseNormal = normal != null ? ExtractTile(normal, new RectInt(0, 0, 1, 1) * Game.TileSize) : null;
			var hasSlopes = Factory.HasSlopeTiles(name);
			var variantIndex = 0;
			foreach (var layer in variantLayers)
			{
				var variant = ase.RenderFrame(0, it => it == layer);
				for (int x = 0; x < columns; x++)
					for (int y = 0; y < rows; y++)
						packer.Add($"tilesets/{name}/variants/{variantIndex}/{x}x{y}", variant, new RectInt(x, y, 1, 1) * Game.TileSize);
				if (hasSlopes)
				{
					using var variantBaseTile = ExtractTile(variant, new RectInt(0, 0, 1, 1) * Game.TileSize);
					AddSlopeSubtextures(packer, name, variantBaseTile, variantIndex: variantIndex, normal: baseNormal);
				}
				variantIndex++;
			}

			if (hasSlopes)
			{
				using var baseTile = ExtractTile(image, new RectInt(0, 0, 1, 1) * Game.TileSize);
				AddSlopeSubtextures(packer, name, baseTile, normal: baseNormal);
			}
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

				var normals = generatedNormalImages.GetValueOrDefault(name);
				var normal = normals != null && variantIndex < normals.Count ? normals[variantIndex] : null;
				var slopeEdges = generatedSlopeEdgeImages.GetValueOrDefault(name);
				var slopeEdge = slopeEdges != null && variantIndex < slopeEdges.Count ? slopeEdges[variantIndex] : null;
				if (image.Width == Game.TileSize && image.Height == Game.TileSize && Factory.HasSlopeTiles(name))
					AddSlopeSubtextures(packer, name, image, slopeEdge, normal, variantIndex);
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
		WorldMap.Load(gfx, pack, CurrentWorldId);
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
		LoadCostumes(pack, "costumes");
		LoadHeadShapes(pack, "heads");
		LoadRibbonStyles(pack, "ribbon-styles");
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
	// builds have them (Sources/ is not shipped). The whole set goes in; Content.pak
	// prunes to what a world references (see BuildContentPak). Layout mirrors the pack
	// load: textures/<key>.png and surfaces/<id>.json.
	static void WriteSurfaceAssetsToPack(string directory)
	{
		var texturesDir = Path.Join(directory, "textures");
		var surfacesDir = Path.Join(directory, "surfaces");
		if (Directory.Exists(texturesDir))
			Directory.Delete(texturesDir, recursive: true);

		var sourceTextures = Path.Join(SourcesPath, "Textures", "Patterns");
		if (Directory.Exists(sourceTextures))
			foreach (var file in Directory.EnumerateFiles(sourceTextures, "*.png", SearchOption.AllDirectories))
			{
				var rel = Path.GetRelativePath(sourceTextures, file);
				var dest = Path.Join(texturesDir, "patterns", rel);
				Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
				File.Copy(file, dest, overwrite: true);
			}

		CopyJsonFolder(Path.Join(SourcesPath, "Surfaces"), surfacesDir);
		CopyJsonFolder(Path.Join(SourcesPath, "Costumes"), Path.Join(directory, "costumes"));
		CopyJsonFolder(Path.Join(SourcesPath, "Heads"), Path.Join(directory, "heads"));
		CopyJsonFolder(Path.Join(SourcesPath, "RibbonStyles"), Path.Join(directory, "ribbon-styles"));
	}

	static void CopyJsonFolder(string source, string destination)
	{
		if (Directory.Exists(destination))
			Directory.Delete(destination, recursive: true);
		if (!Directory.Exists(source))
			return;
		Directory.CreateDirectory(destination);
		foreach (var file in Directory.EnumerateFiles(source, "*.json"))
			File.Copy(file, Path.Join(destination, Path.GetFileName(file)), overwrite: true);
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
				LoadOccluderGroups(packed.OccluderVariants),
				packed.FillTiles);
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
			BuildSocketPoses,
			key => BakedEmissiveLights.GetValueOrDefault(key));

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
			// Only a kit's generated dual tileset carries a multi-tile fill; its base tileset is
			// the single fill that slopes are cut from.
			FillTiles = TileKits.Specs.FirstOrDefault(it => it.DualName == name)?.FillTiles ?? 1,
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
				WritePackedVector(writer, frame.EmissiveLight.Position);
				writer.Write(frame.EmissiveLight.Color);
				writer.Write(frame.EmissiveLight.Energy);
				writer.Write(frame.EmissiveLight.Extent);
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
					writer.Write(pose.Visible);
				}
			}
		}

		WriteCount(writer, manifest.Tilesets.Count, "tilesets");
		foreach (var tileset in manifest.Tilesets)
		{
			writer.Write(tileset.Name);
			writer.Write(tileset.Columns);
			writer.Write(tileset.Rows);
			writer.Write(tileset.FillTiles);
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
					UserData = reader.ReadString() is { Length: > 0 } text ? text : null,
					EmissiveLight = new()
					{
						Position = ReadPackedVector(reader),
						Color = reader.ReadUInt32(),
						Energy = reader.ReadSingle(),
						Extent = reader.ReadSingle()
					}
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
					poses.Add(new()
					{
						Position = ReadPackedVector(reader),
						Normal = ReadPackedVector(reader),
						Visible = reader.ReadBoolean()
					});
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
				FillTiles = reader.ReadInt32(),
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
		public int FillTiles { get; set; } = 1;
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

	static void GenerateDualTilesetFromKit(string kitName, Aseprite ase, out string baseName, out List<Image> baseTiles, out List<Image> baseSlopeEdges, out List<Image> baseNormals, out string dualName, out List<Image> dualVariants, out List<Image> dualNormals, out List<List<Tileset.OccluderSegment[]>> dualOccluders)
	{
		baseName = kitName[..^"-kit".Length];
		baseTiles = [];
		baseSlopeEdges = [];
		baseNormals = [];
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

		GenerateDualTilesetCore(mask, fills, heights, edges, edgeTops, edgeSides, edgeBottoms, edgeWidth, wobble, 1, baseTiles, baseSlopeEdges, baseNormals, dualVariants, dualNormals, dualOccluders);
	}

	/// Generates the tileset outputs for a JSON kit spec: images come from the spec's
	/// palette/patterns/noise instead of an .ase, and the silhouette mask is borrowed from
	/// the referenced .ase kit. Everything downstream is shared with the .ase path.
	public const string KitSourceFolder = "Kits";

	/// Editor: every slice in every kit source file that could become a kit, newest files last. Rim
	/// strips and debug slices are left out, so what comes back is the list of drawable tiles -- read
	/// from disk on demand so a slice added in Aseprite a second ago is already here.
	public static List<(string Source, string Slice, Point2 Size)> KitSourceSlices()
	{
		var result = new List<(string, string, Point2)>();
		var directory = Path.Join(SourcesPath, "Tilesets", KitSourceFolder);
		if (!Directory.Exists(directory))
			return result;

		foreach (var file in EnumerateAsepriteFiles(directory).Order(StringComparer.OrdinalIgnoreCase))
		{
			var source = Path.GetFileNameWithoutExtension(file)!;
			foreach (var slice in GetKitSlices(new Aseprite(file)).Values)
				if (!IsRimSliceName(slice.Name))
					result.Add((source, slice.Name.Trim(), GetSliceBounds(slice).Size));
		}
		return result;
	}

	static bool IsRimSliceName(string name)
		=> name.EndsWith("-rim", StringComparison.OrdinalIgnoreCase)
			|| name.EndsWith("-rimtop", StringComparison.OrdinalIgnoreCase)
			|| name.EndsWith("-rimbottom", StringComparison.OrdinalIgnoreCase);

	static Aseprite KitSourceFor(TileKitSpec spec, Dictionary<string, Aseprite> sources)
	{
		if (spec.Source is not { } source)
			throw new InvalidOperationException($"Tile kit '{spec.Name}' has no \"source\": every kit reads its art from a .ase under Tilesets/{KitSourceFolder}.");
		if (!sources.TryGetValue(source, out var ase))
			throw new InvalidOperationException($"Tile kit '{spec.Name}' names source '{source}', but Tilesets/{KitSourceFolder}/{source}.ase does not exist. Found: {(sources.Count == 0 ? "none" : string.Join(", ", sources.Keys.Order()))}.");
		return ase;
	}

	/// Pulls a kit's fills and relief out of its slice in the shared source file. The slice is a grid
	/// of tile cells; with fillTiles > 1 each macro block is a contiguous group, so a hole in one
	/// would silently shift every later group and is rejected instead.
	static (List<Image> Fills, List<Image> Heights) ExtractKitSourceTiles(TileKitSpec spec, Image art, Image? relief, RectInt bounds)
	{
		var block = spec.FillTiles * Game.TileSize;
		if (bounds.Width % block != 0 || bounds.Height != block)
			throw new InvalidOperationException(
				$"Tile kit '{spec.Name}': slice '{spec.Name}' in {spec.Source}.ase is {bounds.Width}x{bounds.Height}, but a fillTiles {spec.FillTiles} kit needs a width that is a multiple of {block} and a height of exactly {block}.");

		var fills = new List<Image>();
		var heights = new List<Image>();
		var groups = bounds.Width / block;
		for (var group = 0; group < groups; group++)
			for (var phase = 0; phase < spec.FillTiles * spec.FillTiles; phase++)
			{
				var cell = new RectInt(
					bounds.X + group * block + phase % spec.FillTiles * Game.TileSize,
					bounds.Y + phase / spec.FillTiles * Game.TileSize,
					Game.TileSize, Game.TileSize);
				if (!HasOpaquePixel(art, cell))
				{
					if (spec.FillTiles == 1)
						continue; // a gap between variants is fine when a cell is a whole tile
					throw new InvalidOperationException(
						$"Tile kit '{spec.Name}': slice '{spec.Name}' in {spec.Source}.ase has an empty cell at block {group}, tile {phase}. A fillTiles {spec.FillTiles} kit needs every cell of a block painted.");
				}
				fills.Add(ExtractTile(art, cell));
				// No relief painted for a cell is flat, not an error -- a kit can be part way through.
				heights.Add(relief != null ? ExtractTile(relief, cell) : new Image(Game.TileSize, Game.TileSize));
			}

		if (fills.Count == 0)
			throw new InvalidOperationException($"Tile kit '{spec.Name}': slice '{spec.Name}' in {spec.Source}.ase has no painted tiles.");
		return (fills, heights);
	}

	static Rim? ExtractKitRim(TileKitSpec spec, Image art, Dictionary<string, Aseprite.Slice> slices)
	{
		if (SliceImage($"{spec.Name}-rim") is not { } side)
			return null;
		return new Rim(side, SliceImage($"{spec.Name}-rimtop"), SliceImage($"{spec.Name}-rimbottom"));

		Image? SliceImage(string name)
			=> slices.TryGetValue(NormalizeLayerName(name), out var slice) ? ExtractTile(art, GetSliceBounds(slice)) : null;
	}

	static void GenerateDualTilesetFromSpec(TileKitSpec spec, Aseprite maskKit, Aseprite source, out List<Image> baseTiles, out List<Image> baseSlopeEdges, out List<Image> baseNormals, out List<Image> dualVariants, out List<Image> dualNormals, out List<List<Tileset.OccluderSegment[]>> dualOccluders)
	{
		baseTiles = [];
		baseSlopeEdges = [];
		baseNormals = [];
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

		var sourceSlices = GetKitSlices(source);
		if (!sourceSlices.TryGetValue(NormalizeLayerName(spec.Name), out var fillSlice))
			throw new InvalidOperationException(
				$"Tile kit '{spec.Name}': {spec.Source}.ase has no slice named '{spec.Name}'. Slices there: {(sourceSlices.Count == 0 ? "none" : string.Join(", ", sourceSlices.Keys.Order()))}.");

		var sourceLayers = GetNonDebugLayers(source);
		var sourceHeightLayers = GetLayerBranch(source, "height");
		sourceLayers.ExceptWith(sourceHeightLayers);
		using var art = RenderLayerFrame(source, sourceLayers, 0);
		using var relief = sourceHeightLayers.Count > 0 ? RenderLayerFrame(source, sourceHeightLayers, 0) : null;

		var (fills, heights) = ExtractKitSourceTiles(spec, art, relief, GetSliceBounds(fillSlice));
		using var rim = ExtractKitRim(spec, art, sourceSlices);

		GenerateDualTilesetCore(mask, fills, heights, [], [], [], [], (0, 0), spec.Wobble, spec.FillTiles, baseTiles, baseSlopeEdges, baseNormals, dualVariants, dualNormals, dualOccluders, spec.NormalBake, rim);
	}

	// Shared dual-tileset generation. Takes ownership of every image list (disposed here);
	// the mask stays owned by the caller.
	// Contour wobble is picked per group (a group is one macro block's worth of fills), so a kit
	// with a single group would wear the same silhouette on every tile. Pad multi-tile kits out to
	// this many groups, cycling the fills, so their edges still break up.
	const int MinWobbleGroups = 4;

	static void GenerateDualTilesetCore(Image mask, List<Image> fills, List<Image> heights, List<Image> edges, List<Image> edgeTops, List<Image> edgeSides, List<Image> edgeBottoms, (int Min, int Max) edgeWidthRange, float wobble, int fillTiles, List<Image> baseTiles, List<Image> baseSlopeEdges, List<Image> baseNormals, List<Image> dualVariants, List<Image> dualNormals, List<List<Tileset.OccluderSegment[]>> dualOccluders, NormalBake? normalBake = null, Rim? rim = null)
	{
		var bake = normalBake ?? NormalBake.Default;
		static Image? Pick(List<Image> tiles, int i) => tiles.Count > 0 ? tiles[i % tiles.Count] : null;

		// Variants are laid out group-major, phase-minor: the renderer hashes a world tile to a
		// group and reads the phase straight off the tile's position, so one group's fills
		// reassemble into their original multi-tile pattern on the ground.
		var stride = fillTiles * fillTiles;
		var sourceGroups = Math.Max(1, fills.Count / stride);
		var groups = stride > 1 ? Math.Max(sourceGroups, MinWobbleGroups) : sourceGroups;

		for (var group = 0; group < groups; group++)
		{
			// Group 0 keeps the authored silhouette; later groups get a jittered copy so the
			// terrain contour doesn't repeat. Color, normals and occluders all derive from the
			// same group mask, so lighting bevels and shadows follow the wobble -- and every
			// phase in a group shares it, which keeps a macro block's outline coherent.
			var groupMask = group > 0 && wobble > 0 ? WobbleMask(mask, group, wobble) : mask;

			for (var phase = 0; phase < stride; phase++)
			{
				var fillIndex = group % sourceGroups * stride + phase;
				var variant = group * stride + phase;
				var fill = fills[fillIndex];
				if (variant == 0)
				{
					baseTiles.Add(ExtractTile(fill, fill.Bounds));
					if (heights.Count > 0)
						baseNormals.Add(GenerateNormalImage(heights[fillIndex], wrap: true, bake));
					if (edges.Count > 0)
						baseSlopeEdges.Add(ExtractTile(edges[0], edges[0].Bounds));
				}
				var edgeWidth = edgeWidthRange.Min + variant % (edgeWidthRange.Max - edgeWidthRange.Min + 1);
				var side = Pick(edgeSides, variant) ?? Pick(edges, variant);
				dualVariants.Add(GenerateDualTilesetVariant(fill, groupMask, Pick(edgeTops, variant) ?? side, side, Pick(edgeBottoms, variant) ?? side, edgeWidth, rim, variant));
				dualNormals.Add(GenerateDualTilesetNormalVariant(groupMask, heights.Count > 0 ? heights[fillIndex] : null, bake));
				dualOccluders.Add(GenerateDualTilesetOccluderVariant(groupMask));
			}

			if (groupMask != mask)
				groupMask.Dispose();
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
		var source = new Aseprite(Path.Join(SourcesPath, "Tilesets", KitSourceFolder, $"{spec.Source}.ase"));
		GenerateDualTilesetFromSpec(spec, maskKit, source, out var baseTiles, out var baseSlopeEdges, out var baseNormals, out var dualVariants, out var dualNormals, out _);

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

				var worldTile = new Point2(x, y);
				var tile = DualGrid.MaskToTile[mask];
				var seed = Kit.Hash32.TileVariantSeed(Tileset.FillBlock(worldTile, spec.FillTiles), spec.Name[0]);
				var variant = dualVariants[Tileset.VariantSlot(seed, Tileset.FillPhase(worldTile, spec.FillTiles), dualVariants.Count, spec.FillTiles)];
				for (var py = 0; py < Game.TileSize; py++)
					for (var px = 0; px < Game.TileSize; px++)
					{
						var color = variant[tile.X * Game.TileSize + px, tile.Y * Game.TileSize + py];
						if (color.A > 0)
							output[x * Game.TileSize + px, y * Game.TileSize + py] = color;
					}
			}

		foreach (var list in (List<Image>[])[baseTiles, baseSlopeEdges, baseNormals, dualVariants, dualNormals])
			foreach (var image in list)
				image.Dispose();
		return output;
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
				result[frameIndex] = previous with { Visible = false };
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
		return new(Vector2.Lerp(from.Position, to.Position, t),
			new Vector2(MathF.Cos(angle), MathF.Sin(angle)), from.Visible && to.Visible);
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

				var tileHeights = HeightField(height, tile);
				for (var y = 0; y < Game.TileSize; y++)
					for (var x = 0; x < Game.TileSize; x++)
					{
						var heightAlpha = AlphaAt(height, tile.X + x, tile.Y + y);
						var colorAlpha = AlphaAt(color, tile.X + x, tile.Y + y);
						if (heightAlpha <= 0 || colorAlpha <= 0)
							continue;

						var normal = NormalFromHeights(tileHeights, tile.Width, tile.Height, x, y, wrap: true);
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

	static Image GenerateDualTilesetVariant(Image fill, Image mask, Image? edgeTop, Image? edgeSide, Image? edgeBottom, int edgeWidth, Rim? rim, int variant)
	{
		var output = new Image(Game.TileSize * 4, Game.TileSize * 4);
		for (var tileMask = 1; tileMask < TileQuadrants.All + 1; tileMask++)
		{
			var tile = DualGrid.MaskToTile[tileMask];
			var source = new RectInt(tile.X, tile.Y, 1, 1) * Game.TileSize;
			GenerateDualTile(output, source, fill, tileMask, mask, edgeTop, edgeSide, edgeBottom, edgeWidth, rim, variant);
		}
		return output;
	}

	static Image GenerateDualTilesetNormalVariant(Image mask, Image? height, NormalBake bake)
	{
		var output = new Image(Game.TileSize * 4, Game.TileSize * 4);
		for (var tileMask = 1; tileMask < TileQuadrants.All + 1; tileMask++)
		{
			var tile = DualGrid.MaskToTile[tileMask];
			var source = new RectInt(tile.X, tile.Y, 1, 1) * Game.TileSize;
			if (height != null)
				GenerateDualHeightNormalTile(output, source, mask, height, bake);
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
			var tile = DualGrid.MaskToTile[tileMask];
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

	static void GenerateDualHeightNormalTile(Image output, RectInt source, Image alphaMask, Image height, NormalBake bake)
	{
		var heights = HeightField(height, height.Bounds);
		for (var y = 0; y < Game.TileSize; y++)
			for (var x = 0; x < Game.TileSize; x++)
			{
				var maskAlpha = AlphaAt(alphaMask, source.X + x, source.Y + y);
				var heightAlpha = AlphaAt(height, x, y);
				if (maskAlpha <= 0 || heightAlpha <= 0)
					continue;

				var normal = NormalFromHeights(heights, height.Width, height.Height, x, y, wrap: true, bake);
				output[source.X + x, source.Y + y] = new Color(
					normal.X * 0.5f + 0.5f,
					normal.Y * 0.5f + 0.5f,
					normal.Z * 0.5f + 0.5f,
					maskAlpha * heightAlpha);
			}
	}

	static void GenerateDualNormalTile(Image output, RectInt source, int mask, Image alphaMask)
	{
		var tile = DualGrid.MaskToTile[mask];
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

	static void GenerateDualTile(Image output, RectInt source, Image fill, int mask, Image alphaMask, Image? edgeTop, Image? edgeSide, Image? edgeBottom, int edgeWidth, Rim? rim, int variant)
	{
		var tile = DualGrid.MaskToTile[mask];
		var destination = new Point2(tile.X * Game.TileSize, tile.Y * Game.TileSize);
		var hasEdge = edgeTop != null || edgeSide != null || edgeBottom != null;

		for (var y = 0; y < Game.TileSize; y++)
			for (var x = 0; x < Game.TileSize; x++)
			{
				var alpha = alphaMask[source.X + x, source.Y + y].A;
				if (alpha == 0)
					continue;

				var color = fill[x, y];
				if (rim != null)
				{
					// Painted rim: depth picks the column, a per-pixel jitter picks the row, and a
					// transparent cell leaves the fill showing through -- that is the ragged edge.
					var direction = ClassifyMaskEdge(alphaMask, source, x, y, rim.Depth, out var depth);
					if (direction != EdgeDirection.None)
					{
						var rimColor = rim.Sample(direction, depth, RimJitter(x, y, variant));
						if (rimColor.A != 0)
							color = rimColor;
					}
				}
				else if (hasEdge && edgeWidth > 0)
				{
					var chosen = ClassifyMaskEdge(alphaMask, source, x, y, edgeWidth, out _) switch
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
	/// A kit's painted rim: x is depth from the silhouette, y is the jitter variant a stretch of
	/// contour drew. A transparent cell means no rim at that depth, so rows that stop short at
	/// different depths are what make an edge ragged instead of banded.
	sealed class Rim(Image side, Image? top, Image? bottom) : IDisposable
	{
		public int Depth => side.Width;

		public Color Sample(EdgeDirection direction, int depth, int jitter)
		{
			var strip = direction switch
			{
				EdgeDirection.Top => top ?? side,
				EdgeDirection.Bottom => bottom ?? side,
				_ => side,
			};
			return strip[Math.Clamp(depth - 1, 0, strip.Width - 1), jitter % strip.Height];
		}

		public void Dispose()
		{
			side.Dispose();
			top?.Dispose();
			bottom?.Dispose();
		}
	}

	// Which jitter row a rim pixel draws. Deterministic per pixel and variant, so a rebake is stable
	// and neighbouring tiles don't share a seam of identical rim.
	static int RimJitter(int x, int y, int variant)
	{
		var hash = (uint)(x * 374761393 + y * 668265263 + variant * 2147483647);
		hash = (hash ^ (hash >> 13)) * 1274126177;
		return (int)((hash ^ (hash >> 16)) & 0x7FFFFFFF);
	}

	static EdgeDirection ClassifyMaskEdge(Image mask, RectInt source, int x, int y, int width, out int depth)
	{
		var vx = 0f;
		var vy = 0f;
		var found = false;
		// How far in from the silhouette this pixel sits: the nearest empty neighbour's Manhattan
		// distance, 1 for a pixel right on the contour. A rim strip is indexed by it.
		depth = int.MaxValue;
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
				depth = Math.Min(depth, distance);
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

	// Internal: the tile kit round trip renders the same way, so a layer left hidden in Aseprite still
	// counts. A 'height' layer is data, and you hide it to see your art, not to switch it off.
	internal static Image RenderLayerFrame(Aseprite ase, HashSet<Aseprite.Layer> layers, int frame)
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
		if (emissives != null)
			EmissiveLightCenters[name] = FindEmissiveLightCenter(emissives);

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
				{
					packer.Add($"{name}/emissive/{i}", expEmissives[i]);
					BakedEmissiveLights[$"{name}/emissive/{i}"] = BakeEmissiveLight(expEmissives[i], EmissiveLightCenters[name]);
				}
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
				{
					packer.Add($"{name}/emissive/{i}", emissives[i]);
					BakedEmissiveLights[$"{name}/emissive/{i}"] = BakeEmissiveLight(emissives[i], EmissiveLightCenters[name]);
				}
			}
		}

		GenerateTransitionFrames(name, ase, frames, normals, heights, emissives, packer);
	}

	static Vector2 FindEmissiveLightCenter(Image[] frames)
	{
		var weightedPosition = Vector2.Zero;
		float totalWeight = 0;
		foreach (var frame in frames)
			for (var i = 0; i < frame.Data.Length; i++)
			{
				var color = frame.Data[i];
				var weight = color.A / 255f * Math.Max(color.R, Math.Max(color.G, color.B)) / 255f;
				weightedPosition += new Vector2(i % frame.Width + 0.5f, i / frame.Width + 0.5f) * weight;
				totalWeight += weight;
			}
		return totalWeight > 0 ? weightedPosition / totalWeight : Vector2.Zero;
	}

	static Sprite.EmissiveLight BakeEmissiveLight(Image frame, Vector2 position)
	{
		var colorSum = Vector3.Zero;
		float energy = 0;
		float distanceSquaredSum = 0;
		for (var i = 0; i < frame.Data.Length; i++)
		{
			var color = frame.Data[i];
			var alpha = color.A / 255f;
			var rgb = color.ToVector3();
			var weight = alpha * Math.Max(rgb.X, Math.Max(rgb.Y, rgb.Z));
			if (weight <= 0)
				continue;
			var offset = new Vector2(i % frame.Width + 0.5f, i / frame.Width + 0.5f) - position;
			colorSum += rgb * weight;
			energy += weight;
			distanceSquaredSum += offset.LengthSquared() * weight;
		}
		if (energy <= 0)
			return default;

		var colorValue = colorSum / energy;
		colorValue /= Math.Max(colorValue.X, Math.Max(colorValue.Y, colorValue.Z));
		return new(position, new Color(colorValue), energy, MathF.Sqrt(distanceSquaredSum / energy));
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
				{
					packer.Add($"{prefix}/emissive/{j}", emissiveResults[i][j]);
					BakedEmissiveLights[$"{prefix}/emissive/{j}"] = BakeEmissiveLight(emissiveResults[i][j], EmissiveLightCenters[name]);
				}
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


	static void LoadRooms()
	{
		Rooms.Clear();
		RoomsById.Clear();
		MusicRegions.Load();

		var worldRoomsKey = $"{roomsFolderName}/{currentWorldId}";
		if (Content.DirectoryExists(worldRoomsKey))
			foreach (var key in Content.EnumerateDirectory(worldRoomsKey, "*.json", SearchOption.AllDirectories))
			{
				var json = Content.ReadAllBytes(key);
				var room = LoadRoomJson(Path.GetFileNameWithoutExtension(key), json);
				room.DiskRevision = RoomRevision(json);
				room.Sublayer = RoomSublayerFromPath(worldRoomsKey, key);
				// Zip content has no per-room file to rename/delete; SourcePath stays null there.
				room.SourcePath = ContentIsPak ? null : Path.Join(AssetsPath, key);
				AddRoom(room);
			}

		if (RoomsById.Count == 0)
		{
			if (ContentIsPak)
				throw new InvalidDataException($"World '{currentWorldId}' has no rooms in Content.pak.");
			CreateDefaultStartRoom();
		}
	}

	static Room LoadRoomJson(string id, ReadOnlySpan<byte> json)
		=> new(id, Point2.Zero, Room.DefaultSize, JsonUtil.NormalizeStringValues(JsonUtil.Parse(json)?.AsObject() ?? []));

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
		if (RoomsById.TryGetValue(room.Id, out var duplicate))
			throw new InvalidDataException($"Duplicate room id '{room.Id}' in '{duplicate.SourcePath}' and '{room.SourcePath}'.");

		Rooms[(room.Sublayer, room.Cell)] = room;
		RoomsById[room.Id] = room;
	}

	public static bool SaveRoom(Room room, ReadOnlySpan<byte> json, bool overwriteExternal = false)
	{
		if (ContentIsPak)
			throw new InvalidOperationException($"Cannot save room '{room.Id}': rooms are read-only in Content.pak.");
		var path = BundledRoomPath(room.Id, room.Sublayer);
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
		room.SourcePath = path;
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
		room.Id = id;
		RoomsById.Remove(oldId);
		RoomsById[id] = room;
		// The save writes the room under the new id, leaving the old file to clean up below. A
		// refused save (see SaveRoom's disk-conflict check) wrote nothing, so put the id back
		// instead of deleting the file it still lives in.
		if (!room.Save())
		{
			room.Id = oldId;
			RoomsById.Remove(id);
			RoomsById[oldId] = room;
			return false;
		}

		if (!RenamedOntoItself(oldPath, room.SourcePath))
			DeleteRoomFile(oldPath);

		return true;
	}

	// A rename that only changes case (which CanRenameRoom allows) lands back on the same file on
	// Windows and macOS, where the save just overwrote it -- deleting the "old" path there would
	// throw away the file that was written.
	static bool RenamedOntoItself(string? oldPath, string? newPath)
		=> oldPath != null && newPath != null && PathsEqual(oldPath, newPath);

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

		DeleteRoomFile(room.SourcePath);

		return true;
	}

	static string BundledRoomPath(string id, int sublayer = 0)
		=> Path.Join(WorldRoomsPath, RoomSublayerDirectory(sublayer), $"{id}.json");

	static string RoomSublayerDirectory(int sublayer) => sublayer > 0 ? $"Sub{sublayer}" : "";

	static bool RoomFileExists(string id)
		=> Directory.Exists(WorldRoomsPath) && Directory.EnumerateFiles(WorldRoomsPath, $"{id}.json", SearchOption.AllDirectories).Any();

	static void DeleteRoomFile(string? path)
	{
		if (path == null || !File.Exists(path))
			return;

		File.Delete(path);
		RoomFileDeleted?.Invoke(path);
	}

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
		var heights = HeightField(height, height.Bounds);
		for (var y = 0; y < source.Height; y++)
			for (var x = 0; x < source.Width; x++)
			{
				var alpha = AlphaAt(source, x, y);
				if (alpha <= 0)
					continue;

				var n = NormalFromHeights(heights, height.Width, height.Height, x, y, wrap: false);
				normal[x, y] = new Color(
					n.X * 0.5f + 0.5f,
					n.Y * 0.5f + 0.5f,
					n.Z * 0.5f + 0.5f,
					alpha);
			}
		return normal;
	}

	static Image ExtractTile(Image source, RectInt sourceRect)
	{
		var output = new Image(sourceRect.Width, sourceRect.Height);
		for (var y = 0; y < sourceRect.Height; y++)
			for (var x = 0; x < sourceRect.Width; x++)
				output[x, y] = source[sourceRect.X + x, sourceRect.Y + y];
		return output;
	}

	// Writes into a reused scratch tile: the packer copies pixels on Add, and at ~32k slope
	// subtextures a fresh Image per shape is ~32k SDL surface allocs and frees.
	static void GenerateSlopeTile(Image output, Image source, SlopeMask mask, Image? edge)
	{
		for (var y = 0; y < Game.TileSize; y++)
			for (var x = 0; x < Game.TileSize; x++)
			{
				var i = y * Game.TileSize + x;
				output[x, y] = !mask.Solid[i] ? Color.Transparent
					: edge != null && mask.Edge[i] ? edge[x, y]
					: source[x, y];
			}
	}

	static bool IsSlopeMaskSolid(int x, int y, Room.SlopeDirection direction, Room.SlopeFill fill, int runLength, int runIndex)
	{
		if (x < 0 || y < 0 || x >= Game.TileSize || y >= Game.TileSize)
			return false;

		return IsSlopeSurfaceSolid(x, y, direction, fill, runLength, runIndex);
	}

	static bool IsSlopeSurfaceSolid(int x, int y, Room.SlopeDirection direction, Room.SlopeFill fill, int runLength, int runIndex)
	{
		var totalWidth = runLength * Game.TileSize;
		var runX = runIndex * Game.TileSize + x + 0.5f;
		var t = runX / totalWidth;
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

	static void GenerateSlopeNormals(Image output, Image color, Image? normal, SlopeMask mask)
	{
		for (var y = 0; y < Game.TileSize; y++)
			for (var x = 0; x < Game.TileSize; x++)
			{
				var alpha = color[x, y].A;
				if (alpha <= 0)
				{
					output[x, y] = Color.Transparent;
					continue;
				}

				var i = y * Game.TileSize + x;
				var n = mask.Bevel[i].A > 0 ? mask.Bevel[i]
					: normal != null && normal[x, y].A > 0 ? normal[x, y]
					: new Color(0.5f, 0.5f, 1f, 1f);
				n.A = alpha;
				output[x, y] = n;
			}
	}

	static void AddSlopeSubtextures(Packer packer, string tilesetName, Image baseTile, Image? edge = null, Image? normal = null, int? variantIndex = null)
	{
		var colors = slopeColorScratch ??= CreateSlopeScratch();
		var normals = slopeNormalScratch ??= CreateSlopeScratch();

		// Generation is per-shape independent; packing stays serial so the atlas layout is the
		// same on every bake.
		Par.For(0, SlopeShapes.Length, i =>
		{
			GenerateSlopeTile(colors[i], baseTile, SlopeShapes[i].Mask, edge);
			GenerateSlopeNormals(normals[i], colors[i], normal, SlopeShapes[i].Mask);
		});

		for (var i = 0; i < SlopeShapes.Length; i++)
		{
			var (direction, fill, run, index, _) = SlopeShapes[i];
			var name = variantIndex is { } variant
				? SlopeVariantSubtextureName(tilesetName, variant, direction, fill, run, index)
				: SlopeSubtextureName(tilesetName, direction, fill, run, index);
			packer.Add(name, colors[i]);
			packer.Add($"{name}/normal", normals[i]);
		}
	}

	// The mask a slope tile is cut with is a pure function of (direction, fill, run, index) -- the
	// tileset only supplies the pixels underneath it. So the shapes are derived once for the whole
	// bake rather than per tileset, which is where the edge neighbourhood test used to be re-run
	// ~74 times over.
	sealed class SlopeMask
	{
		public readonly bool[] Solid = new bool[Game.TileSize * Game.TileSize];
		public readonly bool[] Edge = new bool[Game.TileSize * Game.TileSize];
		// Bevel normal where the silhouette turns; A == 0 means "flat here, use the tileset's own".
		public readonly Color[] Bevel = new Color[Game.TileSize * Game.TileSize];
	}

	static readonly (Room.SlopeDirection Direction, Room.SlopeFill Fill, int Run, int Index, SlopeMask Mask)[] SlopeShapes = BuildSlopeShapes();

	// One scratch tile per shape, reused across every tileset's bake.
	static Image[]? slopeColorScratch;
	static Image[]? slopeNormalScratch;

	static Image[] CreateSlopeScratch()
	{
		var scratch = new Image[SlopeShapes.Length];
		for (var i = 0; i < scratch.Length; i++)
			scratch[i] = new Image(Game.TileSize, Game.TileSize);
		return scratch;
	}

	static (Room.SlopeDirection, Room.SlopeFill, int, int, SlopeMask)[] BuildSlopeShapes()
	{
		var shapes = new List<(Room.SlopeDirection, Room.SlopeFill, int, int, SlopeMask)>();
		for (var run = 1; run <= MaxSlopeRun; run++)
			for (var index = 0; index < run; index++)
				foreach (var direction in Enum.GetValues<Room.SlopeDirection>())
					foreach (var fill in Enum.GetValues<Room.SlopeFill>())
						shapes.Add((direction, fill, run, index, BuildSlopeMask(direction, fill, run, index)));
		return [.. shapes];
	}

	static SlopeMask BuildSlopeMask(Room.SlopeDirection direction, Room.SlopeFill fill, int run, int index)
	{
		var mask = new SlopeMask();
		// Sample the continuous slope across tile boundaries, avoiding bevels at internal seams.
		float Solid(int x, int y) => IsSlopeSurfaceSolid(x, y, direction, fill, run, index) ? 1 : 0;

		for (var y = 0; y < Game.TileSize; y++)
			for (var x = 0; x < Game.TileSize; x++)
			{
				var i = y * Game.TileSize + x;
				mask.Solid[i] = IsSlopeMaskSolid(x, y, direction, fill, run, index);
				mask.Edge[i] = IsSlopeMaskEdge(x, y, direction, fill, run, index);

				var sx = Solid(x - 1, y) - Solid(x + 1, y);
				var sy = Solid(x, y - 1) - Solid(x, y + 1);
				if (sx == 0 && sy == 0)
					continue;
				var n = Vector3.Normalize(new Vector3(sx * 1.25f, sy * 1.25f, 1));
				mask.Bevel[i] = new Color(n.X * 0.5f + 0.5f, n.Y * 0.5f + 0.5f, n.Z * 0.5f + 0.5f, 1f);
			}

		return mask;
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

	public static Subtexture GetSlopeSubtexture(string tilesetName, Room.SlopeDirection direction, Room.SlopeFill fill, int runLength, int runIndex, int seed, bool normals = false)
	{
		var clampedRun = Math.Clamp(runLength, 1, MaxSlopeRun);
		var clampedIndex = Math.Clamp(runIndex, 0, clampedRun - 1);

		if (GetTileset(tilesetName) is { Variants.Count: > 0 } tileset)
		{
			var variantIndex = (seed & int.MaxValue) % tileset.Variants.Count;
			var name = SlopeVariantSubtextureName(tilesetName, variantIndex, direction, fill, clampedRun, clampedIndex);
			var variant = GetSubtexture(normals ? $"{name}/normal" : name);
			if (variant.Texture != null)
				return variant;
		}

		var baseName = SlopeSubtextureName(tilesetName, direction, fill, clampedRun, clampedIndex);
		return GetSubtexture(normals ? $"{baseName}/normal" : baseName);
	}

	static string SlopeSubtextureName(string tilesetName, Room.SlopeDirection direction, Room.SlopeFill fill, int runLength, int runIndex)
		=> $"slopes/{tilesetName}/{direction}/{fill}/{runLength}/{runIndex}";

	static string SlopeVariantSubtextureName(string tilesetName, int variantIndex, Room.SlopeDirection direction, Room.SlopeFill fill, int runLength, int runIndex)
		=> $"slopes/{tilesetName}/variants/{variantIndex}/{direction}/{fill}/{runLength}/{runIndex}";

}
