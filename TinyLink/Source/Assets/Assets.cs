
using System.Numerics;
using System.Text.Json.Nodes;
using Foster.Framework;

namespace TinyLink;

public static class Assets
{
	public const int MaxSlopeRun = 10;

	public static SpriteFont? Font { get; private set; }
	public static Texture? Atlas { get; private set; }
	public static Texture? LightingNoise { get; private set; }
	public static readonly Dictionary<string, Sprite> Sprites = new();
	public static readonly Dictionary<string, Tileset> Tilesets = new();
	public static readonly Dictionary<string, Subtexture> Subtextures = new();
	public static readonly Dictionary<Point2, Room> Rooms = new();
	public static readonly Dictionary<string, Room> RoomsById = new();

	private const string assetsFolderName = "Assets";
	private const string roomsFolderName = "Rooms";
	private const string defaultWorldId = "FrogWorld";
	private const string defaultStartRoomId = "start";
	private const string deletedRoomsFolderName = ".deleted";
	private static string? path = null;
	private static string? userRoomsPath = null;
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
	public static string CurrentWorldId => currentWorldId;
	public static string WorldRoomsPath => Path.Join(RoomsPath, currentWorldId);
	public static string? UserRoomsPath => userRoomsPath;
	public static string? UserWorldRoomsPath => userRoomsPath != null ? Path.Join(userRoomsPath, currentWorldId) : null;

	public static void ConfigureUserRoomsPath(string path)
	{
		userRoomsPath = path;
		bundledRoomsWritable = null;
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
		=> Reload(gfx, true, dumpPackedAtlases);

	public static void Reload(GraphicsDevice gfx)
		=> Reload(gfx, false, dumpPackedAtlases: false);

	public static void ReloadRooms()
		=> LoadRooms();

	private static void Reload(GraphicsDevice gfx, bool reloadRooms, bool dumpPackedAtlases)
	{
		var spritesPath = Path.Join(AssetsPath, "Sprites");
		var tilesetsPath = Path.Join(AssetsPath, "Tilesets");
		var actorsPath = Path.Join(AssetsPath, "Actors");
		var spriteFiles = new Dictionary<string, Aseprite>();
		var tilesetFiles = new Dictionary<string, Aseprite>();
		var generatedTileImages = new Dictionary<string, List<Image>>();
		var generatedNormalImages = new Dictionary<string, List<Image>>();
		var generatedOccluders = new Dictionary<string, List<List<Tileset.OccluderSegment[]>>>();
		var kitGeneratedTilesets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

		// load main font file
#if !BROWSER
		if (Font == null)
		{
			Font = new SpriteFont(gfx, Path.Join(AssetsPath, "Fonts", "dogica.ttf"), 8);
			Font.LineGap = 4;
		}
#endif

		// get sprite files
		foreach (var file in EnumerateAsepriteFiles(spritesPath))
		{
			var name = Path.ChangeExtension(Path.GetRelativePath(spritesPath, file), null);
			var ase = new Aseprite(file);
			if (ase.Frames.Length > 0)
				spriteFiles.Add(name, ase);
		}

		// get tileset files
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

			GenerateDualTilesetFromKit(name, ase, out var baseName, out var baseTiles, out var dualName, out var dualVariants, out var dualNormals, out var dualOccluders);
			generatedTileImages[baseName] = baseTiles;
			generatedTileImages[dualName] = dualVariants;
			generatedNormalImages[dualName] = dualNormals;
			generatedOccluders[dualName] = dualOccluders;
			kitGeneratedTilesets.Add(baseName);
			kitGeneratedTilesets.Add(dualName);
		}

		// pack all the sprites & tilesets
		Packer.Output output;
		{
			var packer = new Packer();

			foreach (var (name, ase) in spriteFiles)
			{
				var frames = ase.RenderAllFrames();
				for (int i = 0; i < frames.Length; i++)
				{
					packer.Add($"{name}/{i}", frames[i]);
					packer.Add($"{name}/normal/{i}", GenerateNormalMap(frames[i]));
				}
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

					if (variantIndex == 0 && image.Width == Game.TileSize && image.Height == Game.TileSize)
						AddSlopeSubtextures(packer, name, image);
					if (image.Width == Game.TileSize && image.Height == Game.TileSize)
						AddSlopeSubtextures(packer, name, image, variantIndex);
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

			output = packer.Pack();
		}

#if !IOS
		if (dumpPackedAtlases)
			DumpPackedAtlasPages(output);
#endif

		// create texture file
		var atlas = new Texture(gfx, output.Pages[0], name: "Atlas");

		// create subtextures
		var subtextures = new Dictionary<string, Subtexture>();
		foreach (var it in output.Entries)
			subtextures.Add(it.Name, new Subtexture(atlas, it.Source, it.Frame));

		var oldAtlas = Atlas;
		var oldLightingNoise = LightingNoise;
		Atlas = atlas;

		LightingNoise = Assets.LoadAsepriteTexture(gfx, Path.Join("Textures", "noise_tess2_seamless.ase"), "LightingNoise");

		Subtextures.Clear();
		foreach (var (name, subtexture) in subtextures)
			Subtextures.Add(name, subtexture);

		// create sprite assets
		var foundSprites = new HashSet<string>();
		foreach (var (name, ase) in spriteFiles)
		{
			foundSprites.Add(name);

			// find origin
			Vector2 origin = Vector2.Zero;

			// Aseprite files can define an origin with their first slice's pivot.
			if (ase.Slices.Count > 0 && ase.Slices[0].Keys.Length > 0 && ase.Slices[0].Keys[0].Pivot.HasValue)
			{
				origin = ase.Slices[0].Keys[0].Pivot!.Value;
			}

			if (!Sprites.TryGetValue(name, out var sprite))
			{
				sprite = new Sprite(name, origin);
				Sprites.Add(name, sprite);
			}

			// add frames
			var frames = new List<Sprite.Frame>();
			for (int i = 0; i < ase.Frames.Length; i++)
			{
				var subtexture = subtextures[$"{name}/{i}"];
				frames.Add(new(
					subtexture,
					new Subtexture(subtexture.Texture, subtexture.Source),
					subtextures[$"{name}/normal/{i}"],
					ase.Frames[i].Duration / 1000.0f));
			}

			// add animations
			var animations = new List<Sprite.Animation>();
			foreach (var tag in ase.Tags)
			{
				if (!string.IsNullOrEmpty(tag.Name))
				{
					var duration = 0.0f;
					for (int i = tag.From; i <= tag.To && i < frames.Count; i++)
						duration += frames[i].Duration;
					animations.Add(new(tag.Name, tag.From, tag.To - tag.From + 1, duration));
				}
			}

			sprite.Reload(origin, frames, animations);
		}

		foreach (var (name, sprite) in Sprites.ToArray())
		{
			if (foundSprites.Contains(name))
				continue;

			sprite.Reload(Vector2.Zero, [], []);
			Sprites.Remove(name);
		}

		// create tileset assets
		var foundTilesets = new HashSet<string>();
		foreach (var (name, ase) in tilesetFiles)
		{
			if (name.EndsWith("-kit", StringComparison.OrdinalIgnoreCase) || kitGeneratedTilesets.Contains(name))
				continue;

			foundTilesets.Add(name);

			var columns = ase.Width / Game.TileSize;
			var rows = ase.Height / Game.TileSize;
			if (!Tilesets.TryGetValue(name, out var tileset))
			{
				tileset = new Tileset(name, columns, rows);
				Tilesets.Add(name, tileset);
			}

			var tiles = new List<Subtexture>();
			for (int x = 0; x < columns; x++)
				for (int y = 0; y < rows; y++)
					tiles.Add(subtextures[$"tilesets/{name}{x}x{y}"]);

			var variantLayers = GetVariantLayers(ase, GetNonDebugLayers(ase));
			var variants = new List<List<Subtexture>>();
			for (var variantIndex = 0; variantIndex < variantLayers.Count; variantIndex++)
			{
				var variant = new List<Subtexture>();
				for (int x = 0; x < columns; x++)
					for (int y = 0; y < rows; y++)
						variant.Add(subtextures[$"tilesets/{name}/variants/{variantIndex}/{x}x{y}"]);
				variants.Add(variant);
			}

			tileset.Reload(columns, rows, tiles, variants);
		}

		foreach (var (name, images) in generatedTileImages)
		{
			foundTilesets.Add(name);

			var columns = images[0].Width / Game.TileSize;
			var rows = images[0].Height / Game.TileSize;
			if (!Tilesets.TryGetValue(name, out var tileset))
			{
				tileset = new Tileset(name, columns, rows);
				Tilesets.Add(name, tileset);
			}

			var tiles = new List<Subtexture>();
			for (int x = 0; x < columns; x++)
				for (int y = 0; y < rows; y++)
					tiles.Add(subtextures[$"tilesets/{name}{x}x{y}"]);

			var variants = new List<List<Subtexture>>();
			for (var variantIndex = 0; variantIndex < images.Count; variantIndex++)
			{
				var variant = new List<Subtexture>();
				for (int x = 0; x < columns; x++)
					for (int y = 0; y < rows; y++)
						variant.Add(subtextures[$"tilesets/{name}/variants/{variantIndex}/{x}x{y}"]);
				variants.Add(variant);
			}

			List<Subtexture>? normalTiles = null;
			List<List<Subtexture>>? normalVariants = null;
			List<Tileset.OccluderSegment[]>? occluderTiles = null;
			List<List<Tileset.OccluderSegment[]>>? occluderVariants = null;
			if (generatedNormalImages.TryGetValue(name, out var normals))
			{
				normalTiles = [];
				for (int x = 0; x < columns; x++)
					for (int y = 0; y < rows; y++)
						normalTiles.Add(subtextures[$"tilesets/{name}/normal/{x}x{y}"]);

				normalVariants = [];
				for (var variantIndex = 0; variantIndex < normals.Count; variantIndex++)
				{
					var variant = new List<Subtexture>();
					for (int x = 0; x < columns; x++)
						for (int y = 0; y < rows; y++)
							variant.Add(subtextures[$"tilesets/{name}/normal/variants/{variantIndex}/{x}x{y}"]);
					normalVariants.Add(variant);
				}
			}
			if (generatedOccluders.TryGetValue(name, out var occluders))
			{
				occluderTiles = occluders[0];
				occluderVariants = occluders;
			}

			tileset.Reload(columns, rows, tiles, variants, normalTiles, normalVariants, occluderTiles, occluderVariants);
		}

		foreach (var (name, tileset) in Tilesets.ToArray())
		{
			if (foundTilesets.Contains(name))
				continue;

			tileset.Reload(0, 0, []);
			Tilesets.Remove(name);
		}

		foreach (var images in generatedTileImages.Values)
			foreach (var image in images)
				image.Dispose();
		foreach (var images in generatedNormalImages.Values)
			foreach (var image in images)
				image.Dispose();

		oldAtlas?.Dispose();
		oldLightingNoise?.Dispose();

		ActorSpecs.LoadSpecs(actorsPath);
		if (reloadRooms)
			LoadRooms();
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

	private static void GenerateDualTilesetFromKit(string kitName, Aseprite ase, out string baseName, out List<Image> baseTiles, out string dualName, out List<Image> dualVariants, out List<Image> dualNormals, out List<List<Tileset.OccluderSegment[]>> dualOccluders)
	{
		baseName = kitName[..^"-kit".Length];
		baseTiles = [];
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
				baseTiles.Add(ExtractTile(fill, fill.Bounds));
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
		for (var y = 0; y < rows; y ++)
		for (var x = 0; x < columns; x ++)
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
		for (var y = rect.Y; y < rect.Bottom; y ++)
		for (var x = rect.X; x < rect.Right; x ++)
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

	private static Texture? LoadTexture(GraphicsDevice gfx, string relativePath, string name)
	{
		var path = Path.Join(AssetsPath, relativePath);
		if (!File.Exists(path))
			return null;

		using var image = new Image(path);
		return new Texture(gfx, image, name);
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

	private static void LoadRooms()
	{
		Rooms.Clear();
		RoomsById.Clear();

		var deleted = LoadUserRoomTombstones();

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

		if (UserWorldRoomsPath is { } userWorldRoomsPath && Directory.Exists(userWorldRoomsPath))
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

		for (var y = 0; y < source.Height; y ++)
		for (var x = 0; x < source.Width; x ++)
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
		for (var y = 0; y < sourceRect.Height; y ++)
		for (var x = 0; x < sourceRect.Width; x ++)
			output[x, y] = source[sourceRect.X + x, sourceRect.Y + y];
		return output;
	}

	private static Image GenerateSlopeTile(Image source, Room.SlopeDirection direction, Room.SlopeFill fill, int runLength, int runIndex)
	{
		var output = new Image(source.Width, source.Height);
		var totalWidth = runLength * Game.TileSize;

		for (var y = 0; y < source.Height; y ++)
		for (var x = 0; x < source.Width; x ++)
		{
			var runX = runIndex * Game.TileSize + x + 0.5f;
			var t = Math.Clamp(runX / totalWidth, 0, 1);
			var surface = direction == Room.SlopeDirection.DownRight
				? t * Game.TileSize
				: (1 - t) * Game.TileSize;
			var filled = fill == Room.SlopeFill.BelowLine
				? y + 0.5f >= surface
				: y + 0.5f <= surface;
			output[x, y] = filled ? source[x, y] : Color.Transparent;
		}

		return output;
	}

	private static void AddSlopeSubtextures(Packer packer, string tilesetName, Image baseTile)
	{
		for (var run = 1; run <= MaxSlopeRun; run ++)
		for (var index = 0; index < run; index ++)
		{
			packer.Add(SlopeSubtextureName(tilesetName, Room.SlopeDirection.DownRight, Room.SlopeFill.BelowLine, run, index), GenerateSlopeTile(baseTile, Room.SlopeDirection.DownRight, Room.SlopeFill.BelowLine, run, index));
			packer.Add(SlopeSubtextureName(tilesetName, Room.SlopeDirection.UpRight, Room.SlopeFill.BelowLine, run, index), GenerateSlopeTile(baseTile, Room.SlopeDirection.UpRight, Room.SlopeFill.BelowLine, run, index));
			packer.Add(SlopeSubtextureName(tilesetName, Room.SlopeDirection.DownRight, Room.SlopeFill.AboveLine, run, index), GenerateSlopeTile(baseTile, Room.SlopeDirection.DownRight, Room.SlopeFill.AboveLine, run, index));
			packer.Add(SlopeSubtextureName(tilesetName, Room.SlopeDirection.UpRight, Room.SlopeFill.AboveLine, run, index), GenerateSlopeTile(baseTile, Room.SlopeDirection.UpRight, Room.SlopeFill.AboveLine, run, index));
		}
	}

	private static void AddSlopeSubtextures(Packer packer, string tilesetName, Image baseTile, int variantIndex)
	{
		for (var run = 1; run <= MaxSlopeRun; run ++)
		for (var index = 0; index < run; index ++)
		{
			packer.Add(SlopeVariantSubtextureName(tilesetName, variantIndex, Room.SlopeDirection.DownRight, Room.SlopeFill.BelowLine, run, index), GenerateSlopeTile(baseTile, Room.SlopeDirection.DownRight, Room.SlopeFill.BelowLine, run, index));
			packer.Add(SlopeVariantSubtextureName(tilesetName, variantIndex, Room.SlopeDirection.UpRight, Room.SlopeFill.BelowLine, run, index), GenerateSlopeTile(baseTile, Room.SlopeDirection.UpRight, Room.SlopeFill.BelowLine, run, index));
			packer.Add(SlopeVariantSubtextureName(tilesetName, variantIndex, Room.SlopeDirection.DownRight, Room.SlopeFill.AboveLine, run, index), GenerateSlopeTile(baseTile, Room.SlopeDirection.DownRight, Room.SlopeFill.AboveLine, run, index));
			packer.Add(SlopeVariantSubtextureName(tilesetName, variantIndex, Room.SlopeDirection.UpRight, Room.SlopeFill.AboveLine, run, index), GenerateSlopeTile(baseTile, Room.SlopeDirection.UpRight, Room.SlopeFill.AboveLine, run, index));
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
