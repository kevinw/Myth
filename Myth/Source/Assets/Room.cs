
using System.Buffers;
using System.Numerics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Foster.Framework;

namespace Myth;

public class Room
{
	public enum StorageKind
	{
		Bundled,
		User
	}

	[Flags]
	public enum TileFlags
	{
		None = 0,
		Breakable = 1 << 0,
		BreakOnStand = 1 << 1
	}

	public enum SlopeDirection
	{
		DownRight,
		UpRight
	}

	public enum SlopeFill
	{
		BelowLine,
		AboveLine
	}

	public readonly record struct SlopeTile(Point2 Tile, int RunLength, int RunIndex, SlopeDirection Direction, SlopeFill Fill)
	{
		public RectInt WorldBounds(Room room) => new(
			room.WorldBounds.X + Tile.X * Game.TileSize,
			room.WorldBounds.Y + Tile.Y * Game.TileSize,
			Game.TileSize,
			Game.TileSize);
	}

	public readonly record struct LightOccluderPolygon(StackList8<Point2> Points, RectInt Bounds);

	public static readonly Point2 DefaultSize = new(Game.Columns, Game.Rows);
	public static readonly Color DefaultBackdropColor = new(0x150e22);
	public static readonly Color DefaultAmbientColor = new(0.42f, 0.42f, 0.46f, 1.0f);
	public const float DefaultAmbientIntensity = 1.0f;
	public const float DefaultLightNoise = 0.30f;
	public const float DefaultReverb = 0.0f;
	public static readonly Color DefaultSunColor = new(1.0f, 0.9f, 0.72f, 1.0f);
	public static readonly Vector2 DefaultSunDirection = Vector2.Normalize(new Vector2(1, 1));

	/// Stable room identity, also used as the filename.
	public string Id;

	public StorageKind Storage { get; internal set; } = StorageKind.Bundled;
	public string? SourcePath { get; internal set; }

	/// Compatibility screen-grid location of the Room.
	public Point2 Cell => new(TileSpacePosition.X / Game.Columns, TileSpacePosition.Y / Game.Rows);

	/// Tile-space location of the room in the world.
	public Point2 TileSpacePosition;

	/// <summary>
	/// Tile-space size of the room.
	/// </summary>
	public Point2 Size;

	/// Tile grids
	public char[,] Tiles;
	public char[,] BackgroundTiles;
	private Factory.Entry?[,] tileEntries;
	private Factory.Entry?[,] backgroundTileEntries;
	private TileFlags[,] tileFlags;

	/// Actor specs placed directly in this room.
	public readonly List<JsonObject> Actors = [];

	/// World-space rectangles built from adjacent movement-blocking tiles.
	public readonly List<RectInt> CollisionRects = [];

	/// Per-tile solid mask (BlocksMovement and not a slope), row-major Size.X*Size.Y.
	/// Mirrors <see cref="CollisionRects"/> for O(tiles-covered) collision queries.
	public bool[] SolidTiles = [];

	/// Slope tiles keep their own geometry because collision follows the inferred surface line.
	public readonly List<SlopeTile> SlopeTiles = [];

	private readonly Dictionary<Point2, SlopeTile> slopeLookup = [];
	private readonly Dictionary<Point2, StackList8<Point2>> slopePolygonLookup = [];
	private byte[,] dualTerrainJoinMasks = new byte[0, 0];

	/// <summary>
	/// World-space rectangles built from adjacent light-occluding tiles.
	/// </summary>
	public readonly List<RectInt> LightOccluders = [];

	/// World-space convex polygons for light occluders that are not rectangular.
	public readonly List<LightOccluderPolygon> LightOccluderPolygons = [];

	/// Cached world-space segments used by visibility-polygon shadow tests.
	public readonly List<VisibilityPolygon.Segment> VisibilitySegments = [];

	public readonly struct ReflectionRect(RectInt bounds, float opacity, bool distorted)
	{
		public readonly RectInt Bounds = bounds;
		public readonly float Opacity = opacity;
		public readonly bool Distorted = distorted;
	}

	/// World-space rectangles built from adjacent sprite-reflecting tiles.
	public readonly List<ReflectionRect> ReflectionRects = [];

	/// Optional FMOD background music Area parameter label.
	public string? MusicArea;
	public Color BackdropColor = DefaultBackdropColor;
	public Color? BackdropColor2;
	public Color AmbientColor = DefaultAmbientColor;
	public float AmbientIntensity = DefaultAmbientIntensity;
	public float LightNoise = DefaultLightNoise;
	public bool ReverbAuto = true;
	public float Reverb = DefaultReverb;
	public Color SunColor = DefaultSunColor;
	public float SunStrength;
	public Vector2 SunDirection = DefaultSunDirection;
	public string Notes = "";

	/// Bounds in World Space in Pixels
	public RectInt WorldBounds => new(TileSpacePosition.X * Game.TileSize, TileSpacePosition.Y * Game.TileSize, Size.X * Game.TileSize, Size.Y * Game.TileSize);

	public Room(Point2 cell)
		: this(Assets.GenerateRandomRoomId(), new Point2(cell.X * Game.Columns, cell.Y * Game.Rows), DefaultSize)
	{
	}

	public Room(Point2 position, Point2 size)
		: this(Assets.GenerateRandomRoomId(), position, size)
	{
	}

	public Room(string path)
		: this(Path.GetFileNameWithoutExtension(path), Point2.Zero, DefaultSize, path)
	{
	}

	public Room CloneRuntime()
	{
		var clone = new Room(Id, TileSpacePosition, Size)
		{
			MusicArea = MusicArea,
			BackdropColor = BackdropColor,
			BackdropColor2 = BackdropColor2,
			AmbientColor = AmbientColor,
			AmbientIntensity = AmbientIntensity,
			LightNoise = LightNoise,
			ReverbAuto = ReverbAuto,
			Reverb = Reverb,
			SunColor = SunColor,
			SunStrength = SunStrength,
			SunDirection = SunDirection
		};

		for (var x = 0; x < Size.X; x++)
			for (var y = 0; y < Size.Y; y++)
			{
				clone.Tiles[x, y] = Tiles[x, y];
				clone.BackgroundTiles[x, y] = BackgroundTiles[x, y];
				clone.tileFlags[x, y] = tileFlags[x, y];
			}
		foreach (var actor in Actors)
			clone.Actors.Add(ActorSpecs.CloneObject(actor));

		clone.RebuildGeometry();
		return clone;
	}

	public Room(string id, Point2 position, Point2 size, string? path = null)
	{
		SourcePath = path;
		JsonObject? json = null;
		if (path != null)
		{
			if (!string.Equals(Path.GetExtension(path), ".json", StringComparison.OrdinalIgnoreCase))
				throw new InvalidOperationException($"Room files must be JSON: {path}");

			json = JsonNode.Parse(File.ReadAllText(path!))?.AsObject();
			if (json != null)
			{
				position = JsonUtil.ReadPoint(json, "position") ?? position;
				if (JsonUtil.ReadPoint(json, "size") is { } jsonSize && jsonSize.X > 0 && jsonSize.Y > 0)
					size = jsonSize;
			}
		}

		Id = id;
		TileSpacePosition = position;
		Size = size;
		Tiles = new char[Size.X, Size.Y];
		BackgroundTiles = new char[Size.X, Size.Y];
		tileEntries = new Factory.Entry?[Size.X, Size.Y];
		backgroundTileEntries = new Factory.Entry?[Size.X, Size.Y];
		tileFlags = new TileFlags[Size.X, Size.Y];

		for (int x = 0; x < Size.X; x++)
			for (int tileY = 0; tileY < Size.Y; tileY++)
			{
				SetTileUnchecked(x, tileY, '0');
				SetBackgroundTileUnchecked(x, tileY, '0');
			}

		if (path == null)
		{
			RebuildGeometry();
			return;
		}

		if (json != null)
		{
			LoadJson(json);
			RebuildGeometry();
		}
	}

	private void LoadJson(JsonObject json)
	{
		MusicArea = JsonUtil.ReadString(json, "musicArea");
		if (string.IsNullOrWhiteSpace(MusicArea))
			MusicArea = null;
		BackdropColor = JsonUtil.ReadColor(json, "backdropColor") ?? DefaultBackdropColor;
		BackdropColor2 = JsonUtil.ReadColor(json, "backdropColor2");
		AmbientColor = JsonUtil.ReadColor(json, "ambientColor") ?? DefaultAmbientColor;
		AmbientIntensity = JsonUtil.ReadFloat(json, "ambientIntensity", DefaultAmbientIntensity);
		LightNoise = JsonUtil.ReadFloat(json, "lightNoise", DefaultLightNoise);
		ReverbAuto = JsonUtil.ReadBool(json, "reverbAuto", true);
		Reverb = Clamp01(JsonUtil.ReadFloat(json, "reverb", DefaultReverb));
		SunColor = DefaultSunColor;
		SunStrength = 0.0f;
		SunDirection = DefaultSunDirection;
		Notes = JsonUtil.ReadString(json, "notes", "");
		if (json.TryGetPropertyValue("sun", out var sunNode) && sunNode is JsonObject sun)
		{
			SunColor = JsonUtil.ReadColor(sun, "color") ?? DefaultSunColor;
			SunStrength = Math.Max(0, JsonUtil.ReadFloat(sun, "strength", 0.0f));
			SunDirection = NormalizeOrDefault(JsonUtil.ReadVector2(sun, "direction") ?? DefaultSunDirection, DefaultSunDirection);
		}

		if (json.TryGetPropertyValue("tiles", out var tilesNode) && tilesNode is JsonArray rows)
		{
			for (var y = 0; y < rows.Count && y < Size.Y; y++)
			{
				var row = rows[y]?.GetValue<string>() ?? "";
				for (var x = 0; x < row.Length && x < Size.X; x++)
					SetTileUnchecked(x, y, row[x]);
			}
		}

		if (json.TryGetPropertyValue("backgroundTiles", out var backgroundTilesNode) && backgroundTilesNode is JsonArray backgroundRows)
		{
			for (var y = 0; y < backgroundRows.Count && y < Size.Y; y++)
			{
				var row = backgroundRows[y]?.GetValue<string>() ?? "";
				for (var x = 0; x < row.Length && x < Size.X; x++)
					SetBackgroundTileUnchecked(x, y, row[x]);
			}
		}

		if (json.TryGetPropertyValue("breakable", out var breakableNode) && breakableNode is JsonArray breakableCells)
		{
			foreach (var node in breakableCells)
				if (JsonUtil.ReadPoint(node) is { } tile && ContainsTile(tile.X, tile.Y))
					tileFlags[tile.X, tile.Y] |= TileFlags.Breakable;
		}

		NormalizeTileLayers();

		if (json.TryGetPropertyValue("actors", out var actorsNode) && actorsNode is JsonArray actorArray)
		{
			foreach (var node in actorArray)
				if (node is JsonObject actor)
					Actors.Add(ActorSpecs.CloneObject(actor));
		}
	}

	public void Set(Point2 tile, char ch, bool rebuildGeometry = true)
	{
		if (tile.X >= 0 && tile.Y >= 0 && tile.X < Size.X && tile.Y < Size.Y)
		{
			SetTileUnchecked(tile.X, tile.Y, ch);
			if (rebuildGeometry)
				RebuildGeometry();
		}
	}

	public void SetBackground(Point2 tile, char ch, bool rebuildGeometry = true)
	{
		if (tile.X >= 0 && tile.Y >= 0 && tile.X < Size.X && tile.Y < Size.Y)
		{
			SetBackgroundTileUnchecked(tile.X, tile.Y, ch);
			if (rebuildGeometry)
				RebuildGeometry();
		}
	}

	public void SetTile(Factory.TileLayer layer, Point2 tile, char ch, bool rebuildGeometry = true)
	{
		switch (layer)
		{
			case Factory.TileLayer.Foreground:
				SetForeground(tile, ch, rebuildGeometry);
				break;
			case Factory.TileLayer.Background:
				SetBackground(tile, ch, rebuildGeometry);
				break;
			default:
				throw new NotImplementedException();
		}
	}

	public void AddActorSpec(string specId, Point2 worldPosition)
	{
		var localPosition = worldPosition - WorldBounds.TopLeft;
		AddActorSpecLocal(specId, localPosition);
	}

	public void AddActorType(string typeName, Point2 worldPosition)
	{
		var localPosition = worldPosition - WorldBounds.TopLeft;
		AddActorTypeLocal(typeName, localPosition);
	}

	public void AddActorSpecLocal(string specId, Point2 localPosition)
	{
		Actors.Add(new JsonObject
		{
			["Id"] = ActorSpecs.GenerateActorId(),
			["From"] = specId,
			["Position"] = new JsonArray(localPosition.X, localPosition.Y)
		});
	}

	public void AddActorTypeLocal(string typeName, Point2 localPosition)
	{
		Actors.Add(new JsonObject
		{
			["Id"] = ActorSpecs.GenerateActorId(),
			["Type"] = typeName,
			["Position"] = new JsonArray(localPosition.X, localPosition.Y)
		});
	}

	public void RebuildGeometry()
	{
		RebuildTileEntryCache();
		CollisionRects.Clear();
		SlopeTiles.Clear();
		slopeLookup.Clear();
		slopePolygonLookup.Clear();
		LightOccluders.Clear();
		LightOccluderPolygons.Clear();
		VisibilitySegments.Clear();
		ReflectionRects.Clear();

		// Slopes first: the passes below only read the slope lookups (never mutate them).
		BuildSlopeTiles();

		// These passes each read only the prebuilt tile/slope caches and write a distinct
		// output collection, so they run concurrently on desktop/iOS (serial on wasm).
		Par.Invoke(
			BuildDualTerrainJoinMasks,
			() => BuildRects(CollisionRects, static entry => entry.BlocksMovement && !entry.IsSlope),
			BuildSolidTileMask,
			() =>
			{
				BuildRects(LightOccluders, static entry => entry.CastsShadow && !entry.IsSlope && !HasBakedDualOccluders(entry));
				BuildSlopeLightOccluders();
			},
			BuildReflectionRects);

		// Reads LightOccluders + LightOccluderPolygons produced by the parallel phase.
		BuildVisibilitySegments();

		if (ReverbAuto)
			Reverb = InferReverb();
	}

	private static bool HasBakedDualOccluders(Factory.Entry entry)
	{
		if (entry.DualTilesetName == null || Assets.GetTileset(entry.DualTilesetName) is not { } tileset)
			return false;

		return tileset.OccluderTiles.Count > 0 || tileset.OccluderVariants.Count > 0;
	}

	public bool TryGetSlopeTile(Point2 tile, out SlopeTile slope)
		=> slopeLookup.TryGetValue(tile, out slope);

	public int GetDualTerrainJoinMask(int vertexX, int vertexY)
	{
		if (vertexX < 0 || vertexY < 0 || vertexX >= dualTerrainJoinMasks.GetLength(0) || vertexY >= dualTerrainJoinMasks.GetLength(1))
			return 0;

		return dualTerrainJoinMasks[vertexX, vertexY];
	}

	private bool IsTerrainJoinedToDualVertex(int tileX, int tileY, int vertexX, int vertexY)
	{
		if (!ContainsTile(tileX, tileY))
			return IsOffscreenFullBlockJoinTile(tileX, tileY);

		if (IsFullBlockTile(tileX, tileY))
			return true;

		if (!slopeLookup.TryGetValue(new Point2(tileX, tileY), out var slope))
			return false;

		var vertex = new Vector2(
			vertexX * Game.TileSize + WorldBounds.X,
			vertexY * Game.TileSize + WorldBounds.Y);
		var polygon = GetSlopePolygon(slope);
		if (PointInPolygonInclusive(vertex, polygon))
			return true;

		var sample = new Vector2(
			((tileX + vertexX) * 0.5f + 0.25f) * Game.TileSize + WorldBounds.X,
			((tileY + vertexY) * 0.5f + 0.25f) * Game.TileSize + WorldBounds.Y);
		return PointInPolygonInclusive(sample, polygon);
	}

	private bool IsOffscreenFullBlockJoinTile(int tileX, int tileY)
	{
		if (Size.X <= 0 || Size.Y <= 0)
			return false;

		var clampedX = Math.Clamp(tileX, 0, Size.X - 1);
		var clampedY = Math.Clamp(tileY, 0, Size.Y - 1);
		return IsFullBlockTile(clampedX, clampedY);
	}

	public void MoveTo(Point2 position)
	{
		TileSpacePosition = position;
		RebuildGeometry();
	}

	public void ResizeTo(RectInt bounds)
	{
		if (bounds.Width <= 0 || bounds.Height <= 0)
			return;

		var previousPosition = TileSpacePosition;
		var previousSize = Size;
		var previousTiles = Tiles;
		var previousBackgroundTiles = BackgroundTiles;
		var previousTileFlags = tileFlags;

		TileSpacePosition = bounds.Position;
		Size = bounds.Size;
		Tiles = new char[Size.X, Size.Y];
		BackgroundTiles = new char[Size.X, Size.Y];
		tileEntries = new Factory.Entry?[Size.X, Size.Y];
		backgroundTileEntries = new Factory.Entry?[Size.X, Size.Y];
		tileFlags = new TileFlags[Size.X, Size.Y];

		for (var x = 0; x < Size.X; x++)
			for (var y = 0; y < Size.Y; y++)
			{
				var oldX = TileSpacePosition.X + x - previousPosition.X;
				var oldY = TileSpacePosition.Y + y - previousPosition.Y;
				SetTileUnchecked(x, y, oldX >= 0 && oldY >= 0 && oldX < previousSize.X && oldY < previousSize.Y
					? previousTiles[oldX, oldY]
					: '0');
				SetBackgroundTileUnchecked(x, y, oldX >= 0 && oldY >= 0 && oldX < previousSize.X && oldY < previousSize.Y
					? previousBackgroundTiles[oldX, oldY]
					: '0');
				tileFlags[x, y] = oldX >= 0 && oldY >= 0 && oldX < previousSize.X && oldY < previousSize.Y
					? previousTileFlags[oldX, oldY]
					: TileFlags.None;
			}

		var actorOffset = previousPosition - TileSpacePosition;
		if (actorOffset != Point2.Zero)
		{
			foreach (var actor in Actors)
			{
				var position = ActorSpecs.ReadPoint(actor, "Position") ?? Point2.Zero;
				actor["Position"] = new JsonArray(position.X + actorOffset.X * Game.TileSize, position.Y + actorOffset.Y * Game.TileSize);
			}
		}

		RebuildGeometry();
	}

	public static Vector2 NormalizeOrDefault(Vector2 direction, Vector2 fallback)
	{
		if (direction.LengthSquared() <= 0.0001f)
			return fallback;

		return Vector2.Normalize(direction);
	}

	private float InferReverb()
	{
		var totalTiles = Math.Max(1, Size.X * Size.Y);
		var terrainTiles = 0;
		var reflectiveTerrain = 0.0f;
		var reflectiveShadow = 0.0f;
		var openBorderTiles = 0;
		var borderTiles = 0;
		var ceilingColumns = 0;
		var reflectiveCeilingColumns = 0.0f;
		var openSkyColumns = 0;
		var ceilingRows = Math.Max(1, (int)MathF.Ceiling(Size.Y * 0.25f));

		for (var y = 0; y < Size.Y; y++)
			for (var x = 0; x < Size.X; x++)
			{
				var entry = GetTileEntry(x, y);
				var terrain = IsTerrainJoinTile(x, y);
				var shadow = entry is { CastsShadow: true };
				if (terrain)
				{
					terrainTiles++;
					reflectiveTerrain += entry?.AcousticReflectivity ?? 1.0f;
				}
				if (shadow)
				{
					reflectiveShadow += entry?.AcousticReflectivity ?? 1.0f;
				}

				if (x == 0 || y == 0 || x == Size.X - 1 || y == Size.Y - 1)
				{
					borderTiles++;
					if (!terrain && !shadow)
						openBorderTiles++;
				}
			}

		for (var x = 0; x < Size.X; x++)
		{
			var topOpen = !IsTerrainJoinTile(x, 0) && GetTileEntry(x, 0) is not { CastsShadow: true };
			var hasCeiling = false;
			var ceilingReflectivity = 0.0f;
			for (var y = 0; y < ceilingRows; y++)
			{
				var entry = GetTileEntry(x, y);
				if (IsTerrainJoinTile(x, y) || entry is { CastsShadow: true })
				{
					hasCeiling = true;
					ceilingReflectivity = entry?.AcousticReflectivity ?? 1.0f;
					break;
				}
			}

			if (hasCeiling)
			{
				ceilingColumns++;
				reflectiveCeilingColumns += ceilingReflectivity;
			}
			else if (topOpen)
				openSkyColumns++;
		}

		var skyOpenRatio = Size.X > 0 ? openSkyColumns / (float)Size.X : 1.0f;
		var areaBonus = InverseLerp(120.0f, 600.0f, totalTiles) * (1.0f - 0.35f * skyOpenRatio);
		var heightBonus = InverseLerp(9.0f, 24.0f, Size.Y) * (1.0f - 0.75f * skyOpenRatio);
		var widthBonus = InverseLerp(16.0f, 40.0f, Size.X);
		var solidRatio = terrainTiles / (float)totalTiles;
		var reflectiveSolidRatio = reflectiveTerrain / totalTiles;
		var reflectiveShadowRatio = reflectiveShadow / totalTiles;
		var openEdgeRatio = borderTiles > 0 ? openBorderTiles / (float)borderTiles : 1.0f;
		var ceilingRatio = Size.X > 0 ? ceilingColumns / (float)Size.X : 0.0f;
		var reflectiveCeilingRatio = Size.X > 0 ? reflectiveCeilingColumns / Size.X : 0.0f;
		var clutterPenalty = InverseLerp(0.35f, 0.70f, solidRatio);

		var score =
			0.05f +
			0.20f * areaBonus +
			0.20f * heightBonus +
			0.15f * widthBonus +
			0.10f * ceilingRatio +
			0.15f * reflectiveCeilingRatio +
			0.15f * reflectiveShadowRatio +
			0.10f * reflectiveSolidRatio -
			0.45f * skyOpenRatio -
			0.35f * openEdgeRatio -
			0.20f * clutterPenalty;

		return MathF.Round(Clamp01(score), 3);
	}

	private static float InverseLerp(float from, float to, float value)
	{
		if (MathF.Abs(to - from) <= 0.0001f)
			return value >= to ? 1.0f : 0.0f;

		return Clamp01((value - from) / (to - from));
	}

	private static float Clamp01(float value)
		=> Math.Clamp(value, 0.0f, 1.0f);

	private void BuildSlopeTiles()
	{
		for (var y = 0; y < Size.Y; y++)
			for (var x = 0; x < Size.X;)
			{
				if (!IsSlopeTile(x, y))
				{
					x++;
					continue;
				}

				var start = x;
				var slopeChar = Tiles[x, y];
				while (x < Size.X && Tiles[x, y] == slopeChar && IsSlopeTile(x, y))
					x++;

				var runLength = x - start;
				var (direction, fill) = InferSlopeShape(start, y, runLength);
				for (var i = 0; i < runLength; i++)
				{
					var slope = new SlopeTile(new Point2(start + i, y), runLength, i, direction, fill);
					SlopeTiles.Add(slope);
					slopeLookup[slope.Tile] = slope;
					slopePolygonLookup[slope.Tile] = BuildSlopePolygon(slope);
				}
			}
	}

	private void BuildDualTerrainJoinMasks()
	{
		dualTerrainJoinMasks = new byte[Size.X + 1, Size.Y + 1];
		for (var x = 0; x <= Size.X; x++)
			for (var y = 0; y <= Size.Y; y++)
			{
				var mask = 0;
				if (IsTerrainJoinedToDualVertex(x - 1, y - 1, x, y))
					mask |= TileQuadrants.TopLeft;
				if (IsTerrainJoinedToDualVertex(x - 1, y, x, y))
					mask |= TileQuadrants.BottomLeft;
				if (IsTerrainJoinedToDualVertex(x, y - 1, x, y))
					mask |= TileQuadrants.TopRight;
				if (IsTerrainJoinedToDualVertex(x, y, x, y))
					mask |= TileQuadrants.BottomRight;
				dualTerrainJoinMasks[x, y] = (byte)mask;
			}
	}

	private (SlopeDirection Direction, SlopeFill Fill) InferSlopeShape(int startX, int y, int runLength)
	{
		var leftHigh = IsTerrainJoinTile(startX - 1, y);
		var leftLow = IsTerrainJoinTile(startX - 1, y + 1);
		var rightHigh = IsTerrainJoinTile(startX + runLength, y);
		var rightLow = IsTerrainJoinTile(startX + runLength, y + 1);
		var aboveLeft = IsTerrainJoinTile(startX, y - 1);
		var aboveRight = IsTerrainJoinTile(startX + runLength - 1, y - 1);

		if (rightHigh && (aboveLeft || aboveRight))
			return (SlopeDirection.DownRight, SlopeFill.AboveLine);
		if (leftHigh && (aboveLeft || aboveRight))
			return (SlopeDirection.UpRight, SlopeFill.AboveLine);

		if (leftHigh && rightLow)
			return (SlopeDirection.DownRight, SlopeFill.BelowLine);
		if (leftLow && rightHigh)
			return (SlopeDirection.UpRight, SlopeFill.BelowLine);
		if (rightHigh && !leftHigh)
			return (SlopeDirection.UpRight, SlopeFill.BelowLine);
		if (leftHigh && !rightHigh)
			return (SlopeDirection.DownRight, SlopeFill.BelowLine);

		var belowLeft = IsTerrainJoinTile(startX, y + 1);
		var belowRight = IsTerrainJoinTile(startX + runLength - 1, y + 1);
		if (belowRight && !belowLeft)
			return (SlopeDirection.DownRight, SlopeFill.BelowLine);
		if (belowLeft && !belowRight)
			return (SlopeDirection.UpRight, SlopeFill.BelowLine);

		return (SlopeDirection.DownRight, SlopeFill.BelowLine);
	}

	private bool IsSlopeTile(int x, int y)
		=> GetTileEntry(x, y) is { IsSlope: true };

	private bool IsFullBlockTile(int x, int y)
		=> GetTileEntry(x, y) is { BlocksMovement: true, IsSlope: false };

	public bool IsDualMaterialFilled(char tileType, Factory.TileLayer layer, int x, int y)
	{
		if (Size.X <= 0 || Size.Y <= 0)
			return false;

		x = Math.Clamp(x, 0, Size.X - 1);
		y = Math.Clamp(y, 0, Size.Y - 1);
		return (layer == Factory.TileLayer.Foreground ? Tiles[x, y] : BackgroundTiles[x, y]) == tileType;
	}

	private bool IsTerrainJoinTile(int x, int y)
		=> IsFullBlockTile(x, y) || IsSlopeTile(x, y);

	private Factory.Entry? GetTileEntry(int x, int y)
		=> ContainsTile(x, y) ? tileEntries[x, y] : null;

	public Factory.Entry? GetEntry(Point2 tile)
		=> GetTileEntry(tile.X, tile.Y);

	public Factory.Entry? GetBackgroundEntry(Point2 tile)
		=> ContainsTile(tile.X, tile.Y) ? backgroundTileEntries[tile.X, tile.Y] : null;

	private bool ContainsTile(int x, int y)
		=> x >= 0 && y >= 0 && x < Size.X && y < Size.Y;

	private void SetTileUnchecked(int x, int y, char tile)
	{
		if (TryGetLegacyBreakableTileBase(tile, out var baseTile))
		{
			tile = baseTile;
			tileFlags[x, y] |= TileFlags.Breakable;
		}
		else
		{
			tileFlags[x, y] = TileFlags.None;
		}

		Tiles[x, y] = tile;
		tileEntries[x, y] = Factory.Find(tile);
	}

	private static bool TryGetLegacyBreakableTileBase(char tile, out char baseTile)
	{
		baseTile = tile switch
		{
			'x' => '1',
			'X' => 'G',
			_ => '\0'
		};
		return baseTile != '\0';
	}

	private void SetBackgroundTileUnchecked(int x, int y, char tile)
	{
		BackgroundTiles[x, y] = tile;
		backgroundTileEntries[x, y] = Factory.Find(tile);
	}

	private void RebuildTileEntryCache()
	{
		if (tileEntries.GetLength(0) != Size.X || tileEntries.GetLength(1) != Size.Y)
			tileEntries = new Factory.Entry?[Size.X, Size.Y];
		if (backgroundTileEntries.GetLength(0) != Size.X || backgroundTileEntries.GetLength(1) != Size.Y)
			backgroundTileEntries = new Factory.Entry?[Size.X, Size.Y];
		if (tileFlags.GetLength(0) != Size.X || tileFlags.GetLength(1) != Size.Y)
			tileFlags = new TileFlags[Size.X, Size.Y];

		for (var x = 0; x < Size.X; x++)
			for (var y = 0; y < Size.Y; y++)
			{
				if (TryGetLegacyBreakableTileBase(Tiles[x, y], out var baseTile))
				{
					Tiles[x, y] = baseTile;
					tileFlags[x, y] |= TileFlags.Breakable;
				}

				tileEntries[x, y] = Factory.Find(Tiles[x, y]);
				backgroundTileEntries[x, y] = Factory.Find(BackgroundTiles[x, y]);
			}
	}

	public bool IsBreakable(Point2 tile)
		=> ContainsTile(tile.X, tile.Y) && (tileFlags[tile.X, tile.Y] & TileFlags.Breakable) != 0;

	public void SetBreakable(Point2 tile, bool breakable)
	{
		if (!ContainsTile(tile.X, tile.Y))
			return;

		if (breakable)
			tileFlags[tile.X, tile.Y] |= TileFlags.Breakable;
		else
			tileFlags[tile.X, tile.Y] &= ~TileFlags.Breakable;
	}

	private void NormalizeTileLayers()
	{
		for (var x = 0; x < Size.X; x++)
			for (var y = 0; y < Size.Y; y++)
			{
				if (tileEntries[x, y] is { Layer: Factory.TileLayer.Background })
				{
					if (BackgroundTiles[x, y] == '0')
						SetBackgroundTileUnchecked(x, y, Tiles[x, y]);
					SetTileUnchecked(x, y, '0');
				}

				if (backgroundTileEntries[x, y] is { Layer: Factory.TileLayer.Foreground })
					SetBackgroundTileUnchecked(x, y, '0');
			}
	}

	private void BuildSlopeLightOccluders()
	{
		foreach (var slope in SlopeTiles)
			LightOccluderPolygons.Add(new(GetSlopePolygon(slope), slope.WorldBounds(this)));
	}

	private void BuildVisibilitySegments()
	{
		var bounds = WorldBounds;
		AddVisibilitySegment(bounds.TopLeft, bounds.TopRight);
		AddVisibilitySegment(bounds.TopRight, bounds.BottomRight);
		AddVisibilitySegment(bounds.BottomRight, bounds.BottomLeft);
		AddVisibilitySegment(bounds.BottomLeft, bounds.TopLeft);

		foreach (var rect in LightOccluders)
		{
			AddVisibilitySegment(rect.TopLeft, rect.TopRight);
			AddVisibilitySegment(rect.TopRight, rect.BottomRight);
			AddVisibilitySegment(rect.BottomRight, rect.BottomLeft);
			AddVisibilitySegment(rect.BottomLeft, rect.TopLeft);
		}

		foreach (var polygon in LightOccluderPolygons)
		{
			for (var i = 0; i < polygon.Points.Count; i++)
				AddVisibilitySegment(polygon.Points[i], polygon.Points[(i + 1) % polygon.Points.Count]);
		}
	}

	private void AddVisibilitySegment(Vector2 a, Vector2 b)
		=> VisibilitySegments.Add(new(a, b));

	public StackList8<Point2> GetSlopePolygon(SlopeTile slope)
		=> slopePolygonLookup.TryGetValue(slope.Tile, out var polygon) ? polygon : BuildSlopePolygon(slope);

	public void GetSlopeSurface(SlopeTile slope, out Vector2 left, out Vector2 right)
	{
		var rect = slope.WorldBounds(this);
		var leftHeight = SlopeSurfaceHeight(slope, slope.RunIndex);
		var rightHeight = SlopeSurfaceHeight(slope, slope.RunIndex + 1);
		left = new Vector2(rect.Left, rect.Top + leftHeight);
		right = new Vector2(rect.Right, rect.Top + rightHeight);
	}

	private StackList8<Point2> BuildSlopePolygon(SlopeTile slope)
	{
		var rect = slope.WorldBounds(this);
		var leftHeight = SlopeSurfaceHeight(slope, slope.RunIndex);
		var rightHeight = SlopeSurfaceHeight(slope, slope.RunIndex + 1);
		var leftSurface = new Point2(rect.Left, rect.Top + leftHeight);
		var rightSurface = new Point2(rect.Right, rect.Top + rightHeight);
		var points = new StackList8<Point2>();

		if (slope.Fill == SlopeFill.BelowLine)
		{
			points.Add(leftSurface);
			points.Add(rightSurface);
			if (rect.BottomRight != rightSurface)
				points.Add(rect.BottomRight);
			if (rect.BottomLeft != leftSurface)
				points.Add(rect.BottomLeft);
		}
		else
		{
			if (rect.TopLeft != leftSurface)
				points.Add(rect.TopLeft);
			if (rect.TopRight != rightSurface)
				points.Add(rect.TopRight);
			points.Add(rightSurface);
			points.Add(leftSurface);
		}

		return points;
	}

	private static int SlopeSurfaceHeight(SlopeTile slope, int runEdgeIndex)
	{
		if (slope.Direction == SlopeDirection.DownRight)
			return runEdgeIndex * Game.TileSize / slope.RunLength;

		return (slope.RunLength - runEdgeIndex) * Game.TileSize / slope.RunLength;
	}

	private static bool PointInPolygon(Vector2 point, StackList8<Point2> polygon)
	{
		var inside = false;
		for (int i = 0, j = polygon.Count - 1; i < polygon.Count; j = i++)
		{
			var a = (Vector2)polygon[i];
			var b = (Vector2)polygon[j];
			if ((a.Y > point.Y) == (b.Y > point.Y))
				continue;

			var x = (b.X - a.X) * (point.Y - a.Y) / (b.Y - a.Y) + a.X;
			if (point.X < x)
				inside = !inside;
		}

		return inside;
	}

	private static bool PointInPolygonInclusive(Vector2 point, StackList8<Point2> polygon)
	{
		for (int i = 0, j = polygon.Count - 1; i < polygon.Count; j = i++)
		{
			if (PointOnSegment(point, polygon[j], polygon[i]))
				return true;
		}

		return PointInPolygon(point, polygon);
	}

	private static bool PointOnSegment(Vector2 point, Point2 segmentStart, Point2 segmentEnd)
	{
		var a = (Vector2)segmentStart;
		var b = (Vector2)segmentEnd;
		var ab = b - a;
		var ap = point - a;
		var cross = ab.X * ap.Y - ab.Y * ap.X;
		if (MathF.Abs(cross) > 0.001f)
			return false;

		var dot = Vector2.Dot(ap, ab);
		if (dot < -0.001f)
			return false;

		return dot <= Vector2.Dot(ab, ab) + 0.001f;
	}

	// Same predicate as the CollisionRects build, but kept as a flat per-tile
	// mask so collision queries can index directly instead of scanning rects.
	private void BuildSolidTileMask()
	{
		var count = Size.X * Size.Y;
		if (SolidTiles.Length != count)
			SolidTiles = new bool[count];
		else
			Array.Clear(SolidTiles);

		for (var y = 0; y < Size.Y; y++)
			for (var x = 0; x < Size.X; x++)
				SolidTiles[y * Size.X + x] = Matches(x, y, static entry => entry.BlocksMovement && !entry.IsSlope);
	}

	private void BuildRects(List<RectInt> output, Func<Factory.Entry, bool> predicate)
	{
		var used = new bool[Size.X, Size.Y]; // TODO alloc

		for (var y = 0; y < Size.Y; y++)
			for (var x = 0; x < Size.X; x++)
			{
				if (used[x, y] || !Matches(x, y, predicate))
					continue;

				var width = 1;
				while (x + width < Size.X && !used[x + width, y] && Matches(x + width, y, predicate))
					width++;

				var height = 1;
				while (y + height < Size.Y && RowMatches(x, y + height, width, used, predicate))
					height++;

				for (var yy = y; yy < y + height; yy++)
					for (var xx = x; xx < x + width; xx++)
						used[xx, yy] = true;

				output.Add(new RectInt(
					WorldBounds.X + x * Game.TileSize,
					WorldBounds.Y + y * Game.TileSize,
					width * Game.TileSize,
					height * Game.TileSize));
			}
	}

	// Per-thread scratch: BuildReflectionRects can run on a worker thread during the
	// parallel RebuildGeometry phase, and a shared static would race if two rooms rebuilt
	// at once.
	[ThreadStatic] static List<RectInt>? tempBounds;
	private void BuildReflectionRects()
	{
		var bounds = tempBounds ??= [];
		foreach (Factory.ReflectionMode mode in new[] { Factory.ReflectionMode.Flat, Factory.ReflectionMode.Distorted })
		{
			bounds.Clear();
			BuildRects(bounds, entry => entry.Reflection == mode);
			bool distorted = mode == Factory.ReflectionMode.Distorted;
			foreach (var rect in bounds)
			{
				var tileX = (rect.X - WorldBounds.X) / Game.TileSize;
				var tileY = (rect.Y - WorldBounds.Y) / Game.TileSize;
				var opacity = GetTileEntry(tileX, tileY)?.ReflectionOpacity ?? (distorted ? 0.5f : 0.7f);
				ReflectionRects.Add(new(rect, opacity, distorted));
			}
		}
	}

	private bool RowMatches(int x, int y, int width, bool[,] used, Func<Factory.Entry, bool> predicate)
	{
		for (var xx = x; xx < x + width; xx++)
		{
			if (used[xx, y] || !Matches(xx, y, predicate))
				return false;
		}

		return true;
	}

	private bool Matches(int x, int y, Func<Factory.Entry, bool> predicate)
	{
		if (GetTileEntry(x, y) is not { } entry)
			return false;

		return predicate(entry);
	}

	public void Save()
	{
		if (ReverbAuto)
			Reverb = InferReverb();

		var buffer = new ArrayBufferWriter<byte>();
		WriteJson(buffer);
		Assets.SaveRoom(this, buffer.WrittenSpan);
	}

	public string ToJsonString()
	{
		var buffer = new ArrayBufferWriter<byte>();
		WriteJson(buffer);
		return Encoding.UTF8.GetString(buffer.WrittenSpan);
	}

	// Serializes directly with Utf8JsonWriter instead of building a JsonObject DOM and
	// regex-compacting the result: no intermediate node tree, no per-row StringBuilder/
	// string, no whole-document regex pass. Two-int point arrays are written inline
	// ([x, y]) via WriteRawValue so the on-disk format is unchanged.
	private void WriteJson(IBufferWriter<byte> output)
	{
		using var writer = new Utf8JsonWriter(output, new JsonWriterOptions { Indented = true, NewLine = "\n" });
		writer.WriteStartObject();

		WriteIntPairProperty(writer, "position", TileSpacePosition.X, TileSpacePosition.Y);
		WriteIntPairProperty(writer, "size", Size.X, Size.Y);

		if (!string.IsNullOrEmpty(MusicArea))
			writer.WriteString("musicArea", MusicArea);
		if (BackdropColor != DefaultBackdropColor)
			writer.WriteString("backdropColor", JsonUtil.ColorToHex(BackdropColor));
		if (BackdropColor2.HasValue)
			writer.WriteString("backdropColor2", JsonUtil.ColorToHex(BackdropColor2.Value));
		if (AmbientColor != DefaultAmbientColor)
			writer.WriteString("ambientColor", JsonUtil.ColorToHex(AmbientColor));
		if (AmbientIntensity != DefaultAmbientIntensity)
			writer.WriteNumber("ambientIntensity", AmbientIntensity);
		if (LightNoise != DefaultLightNoise)
			writer.WriteNumber("lightNoise", LightNoise);
		if (!ReverbAuto)
			writer.WriteBoolean("reverbAuto", false);
		if (Reverb != DefaultReverb || !ReverbAuto)
			writer.WriteNumber("reverb", Reverb);

		if (SunStrength > 0.0f || SunColor != DefaultSunColor || SunDirection != DefaultSunDirection)
		{
			writer.WriteStartObject("sun");
			writer.WriteNumber("strength", SunStrength);
			writer.WriteString("color", JsonUtil.ColorToHex(SunColor));
			writer.WriteStartArray("direction");
			writer.WriteNumberValue(SunDirection.X);
			writer.WriteNumberValue(SunDirection.Y);
			writer.WriteEndArray();
			writer.WriteEndObject();
		}

		var rowBuffer = new char[Size.X];

		writer.WriteStartArray("tiles");
		for (var y = 0; y < Size.Y; y++)
		{
			for (var x = 0; x < Size.X; x++)
				rowBuffer[x] = Tiles[x, y];
			writer.WriteStringValue(rowBuffer);
		}
		writer.WriteEndArray();

		if (HasBackgroundTiles())
		{
			writer.WriteStartArray("backgroundTiles");
			for (var y = 0; y < Size.Y; y++)
			{
				for (var x = 0; x < Size.X; x++)
					rowBuffer[x] = BackgroundTiles[x, y];
				writer.WriteStringValue(rowBuffer);
			}
			writer.WriteEndArray();
		}

		if (HasBreakableTiles())
		{
			writer.WriteStartArray("breakable");
			for (var y = 0; y < Size.Y; y++)
				for (var x = 0; x < Size.X; x++)
					if ((tileFlags[x, y] & TileFlags.Breakable) != 0)
						WriteIntPairValue(writer, x, y);
			writer.WriteEndArray();
		}

		if (Actors.Count > 0)
		{
			writer.WriteStartArray("actors");
			foreach (var actor in Actors)
			{
				ActorSpecs.EnsureActorId(actor);
				WriteActorNode(writer, ActorSpecs.PruneForSave(actor));
			}
			writer.WriteEndArray();
		}

		writer.WriteEndObject();
	}

	private static void WriteIntPairProperty(Utf8JsonWriter writer, string name, int x, int y)
	{
		writer.WritePropertyName(name);
		WriteIntPairValue(writer, x, y);
	}

	// Writes [x, y] inline even under indented formatting (WriteRawValue is not re-indented),
	// matching the previous regex-compacted output without allocating a string per point.
	private static void WriteIntPairValue(Utf8JsonWriter writer, int x, int y)
	{
		Span<char> buffer = stackalloc char[32];
		var length = 0;
		buffer[length++] = '[';
		x.TryFormat(buffer[length..], out var written); length += written;
		buffer[length++] = ',';
		buffer[length++] = ' ';
		y.TryFormat(buffer[length..], out written); length += written;
		buffer[length++] = ']';
		writer.WriteRawValue(buffer[..length], skipInputValidation: true);
	}

	// Writes a pruned actor node, keeping the same inline-point convention for any
	// two-int arrays (e.g. Position) it contains, and deferring everything else to the node.
	private static void WriteActorNode(Utf8JsonWriter writer, JsonNode? node)
	{
		switch (node)
		{
			case JsonObject obj:
				writer.WriteStartObject();
				foreach (var (key, value) in obj)
				{
					writer.WritePropertyName(key);
					WriteActorNode(writer, value);
				}
				writer.WriteEndObject();
				break;
			case JsonArray array when IsIntPair(array, out var x, out var y):
				WriteIntPairValue(writer, x, y);
				break;
			case JsonArray array:
				writer.WriteStartArray();
				foreach (var item in array)
					WriteActorNode(writer, item);
				writer.WriteEndArray();
				break;
			case null:
				writer.WriteNullValue();
				break;
			default:
				node.WriteTo(writer);
				break;
		}
	}

	private static bool IsIntPair(JsonArray array, out int x, out int y)
	{
		x = y = 0;
		return array.Count == 2
			&& array[0] is JsonValue a && a.TryGetValue(out x)
			&& array[1] is JsonValue b && b.TryGetValue(out y);
	}

	public void LoadJsonState(string jsonText)
	{
		var json = JsonNode.Parse(jsonText)?.AsObject() ?? [];
		TileSpacePosition = JsonUtil.ReadPoint(json, "position") ?? TileSpacePosition;
		if (JsonUtil.ReadPoint(json, "size") is { } size && size.X > 0 && size.Y > 0 && size != Size)
		{
			Size = size;
			Tiles = new char[Size.X, Size.Y];
			BackgroundTiles = new char[Size.X, Size.Y];
			tileEntries = new Factory.Entry?[Size.X, Size.Y];
			backgroundTileEntries = new Factory.Entry?[Size.X, Size.Y];
			tileFlags = new TileFlags[Size.X, Size.Y];
		}

		for (var x = 0; x < Size.X; x++)
			for (var y = 0; y < Size.Y; y++)
			{
				SetTileUnchecked(x, y, '0');
				SetBackgroundTileUnchecked(x, y, '0');
				tileFlags[x, y] = TileFlags.None;
			}
		Actors.Clear();
		LoadJson(json);
		RebuildGeometry();
	}

	private bool HasBackgroundTiles()
	{
		for (var x = 0; x < Size.X; x++)
			for (var y = 0; y < Size.Y; y++)
				if (BackgroundTiles[x, y] != '0')
					return true;

		return false;
	}

	private bool HasBreakableTiles()
	{
		for (var x = 0; x < Size.X; x++)
			for (var y = 0; y < Size.Y; y++)
				if ((tileFlags[x, y] & TileFlags.Breakable) != 0)
					return true;

		return false;
	}
}
