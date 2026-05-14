
using System.Numerics;
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
	private static string? path = null;

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

	public static void Load(GraphicsDevice gfx)
		=> Reload(gfx, true);

	public static void Reload(GraphicsDevice gfx)
		=> Reload(gfx, false);

	private static void Reload(GraphicsDevice gfx, bool reloadRooms)
	{
		var spritesPath = Path.Join(AssetsPath, "Sprites");
		var tilesetsPath = Path.Join(AssetsPath, "Tilesets");
		var actorsPath = Path.Join(AssetsPath, "Actors");
		var spriteFiles = new Dictionary<string, Aseprite>();
		var tilesetFiles = new Dictionary<string, Aseprite>();

		// load main font file
		if (Font == null)
		{
			Font = new SpriteFont(gfx, Path.Join(AssetsPath, "Fonts", "dogica.ttf"), 8);
			Font.LineGap = 4;
		}

		// get sprite files
		foreach (var file in Directory.EnumerateFiles(spritesPath, "*.ase", SearchOption.AllDirectories))
		{
			var name = Path.ChangeExtension(Path.GetRelativePath(spritesPath, file), null);
			var ase = new Aseprite(file);
			if (ase.Frames.Length > 0)
				spriteFiles.Add(name, ase);
		}

		// get tileset files
		foreach (var file in Directory.EnumerateFiles(tilesetsPath, "*.ase", SearchOption.AllDirectories))
		{
			var name = Path.ChangeExtension(Path.GetRelativePath(tilesetsPath, file), null);
			var ase = new Aseprite(file);
			if (ase.Frames.Length > 0)
				tilesetFiles.Add(name, ase);
		}

		// pack all the sprites & tilesets
		Packer.Output output;
		{
			var packer = new Packer();

			foreach (var (name, ase) in spriteFiles)
			{
				var frames = ase.RenderAllFrames();
				for (int i = 0; i < frames.Length; i ++)
				{
					packer.Add($"{name}/{i}", frames[i]);
					packer.Add($"{name}/normal/{i}", GenerateNormalMap(frames[i]));
				}
			}

			foreach (var (name, ase) in tilesetFiles)
			{
				var image = ase.RenderFrame(0);
				var columns = image.Width / Game.TileSize;
				var rows = image.Height / Game.TileSize;

				for (int x = 0; x < columns; x ++)
					for (int y = 0; y < rows; y ++)
						packer.Add($"tilesets/{name}{x}x{y}", image, new RectInt(x, y, 1, 1) * Game.TileSize);

				using var baseTile = ExtractTile(image, new RectInt(0, 0, 1, 1) * Game.TileSize);
				for (var run = 1; run <= MaxSlopeRun; run ++)
				{
					for (var index = 0; index < run; index ++)
					{
						packer.Add(SlopeSubtextureName(name, Room.SlopeDirection.DownRight, run, index), GenerateSlopeTile(baseTile, Room.SlopeDirection.DownRight, run, index));
						packer.Add(SlopeSubtextureName(name, Room.SlopeDirection.UpRight, run, index), GenerateSlopeTile(baseTile, Room.SlopeDirection.UpRight, run, index));
					}
				}
			}

			output = packer.Pack();
		}

		// create texture file
		var atlas = new Texture(gfx, output.Pages[0], name: "Atlas");

		// create subtextures
		var subtextures = new Dictionary<string, Subtexture>();
		foreach (var it in output.Entries)
			subtextures.Add(it.Name, new Subtexture(atlas, it.Source, it.Frame));

		var oldAtlas = Atlas;
		var oldLightingNoise = LightingNoise;
		Atlas = atlas;
		LightingNoise = LoadTexture(gfx, Path.Join("Textures", "noise_tess2.png"), "LightingNoise");

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
			for (int i = 0; i < ase.Frames.Length; i ++)
				frames.Add(new(
					subtextures[$"{name}/{i}"],
					subtextures[$"{name}/normal/{i}"],
					ase.Frames[i].Duration / 1000.0f));

			// add animations
			var animations = new List<Sprite.Animation>();
			foreach (var tag in ase.Tags)
			{
				if (!string.IsNullOrEmpty(tag.Name))
				{
					var duration = 0.0f;
					for (int i = tag.From; i <= tag.To && i < frames.Count; i ++)
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
			foundTilesets.Add(name);

			var columns = ase.Width / Game.TileSize;
			var rows = ase.Height / Game.TileSize;
			if (!Tilesets.TryGetValue(name, out var tileset))
			{
				tileset = new Tileset(name, columns, rows);
				Tilesets.Add(name, tileset);
			}

			var tiles = new List<Subtexture>();
			for (int x = 0; x < columns; x ++)
				for (int y = 0; y < rows; y ++)
					tiles.Add(subtextures[$"tilesets/{name}{x}x{y}"]);

			tileset.Reload(columns, rows, tiles);
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

		ActorSpecs.LoadSpecs(actorsPath);
		if (reloadRooms)
			LoadRooms();
	}

	private static Texture? LoadTexture(GraphicsDevice gfx, string relativePath, string name)
	{
		var path = Path.Join(AssetsPath, relativePath);
		if (!File.Exists(path))
			return null;

		using var image = new Image(path);
		return new Texture(gfx, image, name);
	}

	private static void LoadRooms()
	{
		Rooms.Clear();
		RoomsById.Clear();

		var roomsPath = Path.Join(AssetsPath, "Rooms");
		foreach (var file in Directory.EnumerateFiles(roomsPath, "*.json"))
			AddRoom(new Room(file));
	}

	private static void AddRoom(Room room)
	{
		Rooms[room.Cell] = room;
		RoomsById[room.Id] = room;
	}

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

	private static Image GenerateSlopeTile(Image source, Room.SlopeDirection direction, int runLength, int runIndex)
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
			output[x, y] = y + 0.5f >= surface ? source[x, y] : Color.Transparent;
		}

		return output;
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
	public static Tileset? GetTileset(string name) => Tilesets.GetValueOrDefault(name);
	public static Room? GetRoom(Point2 cell) => Rooms.GetValueOrDefault(cell);
	public static Room? GetRoom(string id) => RoomsById.GetValueOrDefault(id);

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

	public static Subtexture GetSlopeSubtexture(string tilesetName, Room.SlopeDirection direction, int runLength, int runIndex)
	{
		var clampedRun = Math.Clamp(runLength, 1, MaxSlopeRun);
		var clampedIndex = Math.Clamp(runIndex, 0, clampedRun - 1);
		return GetSubtexture(SlopeSubtextureName(tilesetName, direction, clampedRun, clampedIndex));
	}

	private static string SlopeSubtextureName(string tilesetName, Room.SlopeDirection direction, int runLength, int runIndex)
		=> $"slopes/{tilesetName}/{direction}/{runLength}/{runIndex}";

}
