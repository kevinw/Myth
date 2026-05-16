
using System.Text;
using System.Text.Json.Nodes;
using Foster.Framework;

namespace TinyLink;

public class Room
{
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

	public readonly record struct LightOccluderPolygon(Point2[] Points, RectInt Bounds);

	public static readonly Point2 DefaultSize = new(Game.Columns, Game.Rows);
	public static readonly Color DefaultAmbientColor = new(0.42f, 0.42f, 0.46f, 1.0f);
	public const float DefaultAmbientIntensity = 1.0f;
	public const float DefaultLightNoise = 0.30f;

	/// <summary>
	/// Stable room identity, also used as the filename.
	/// </summary>
	public string Id;

	/// <summary>
	/// Compatibility screen-grid location of the Room.
	/// </summary>
	public Point2 Cell => new(Position.X / Game.Columns, Position.Y / Game.Rows);

	/// <summary>
	/// Tile-space location of the room in the world.
	/// </summary>
	public Point2 Position;

	/// <summary>
	/// Tile-space size of the room.
	/// </summary>
	public Point2 Size;

	/// <summary>
	/// Grid of Tiles
	/// </summary>
	public char[,] Tiles;

	/// <summary>
	/// Actor specs placed directly in this room.
	/// </summary>
	public readonly List<JsonObject> Actors = [];

	/// <summary>
	/// World-space rectangles built from adjacent movement-blocking tiles.
	/// </summary>
	public readonly List<RectInt> CollisionRects = [];

	/// <summary>
	/// Slope tiles keep their own geometry because collision follows the inferred surface line.
	/// </summary>
	public readonly List<SlopeTile> SlopeTiles = [];

	private readonly Dictionary<Point2, SlopeTile> slopeLookup = [];

	/// <summary>
	/// World-space rectangles built from adjacent light-occluding tiles.
	/// </summary>
	public readonly List<RectInt> LightOccluders = [];

	/// <summary>
	/// World-space convex polygons for light occluders that are not rectangular.
	/// </summary>
	public readonly List<LightOccluderPolygon> LightOccluderPolygons = [];

	/// <summary>
	/// World-space rectangles built from adjacent sprite-reflecting tiles.
	/// </summary>
	public readonly List<RectInt> ReflectionRects = [];

	/// <summary>
	/// Optional FMOD background music Area parameter label.
	/// </summary>
	public string? MusicArea;
	public Color AmbientColor = DefaultAmbientColor;
	public float AmbientIntensity = DefaultAmbientIntensity;
	public float LightNoise = DefaultLightNoise;

	/// <summary>
	/// Bounds in World Space in Pixels
	/// </summary>
	public RectInt WorldBounds => new(Position.X * Game.TileSize, Position.Y * Game.TileSize, Size.X * Game.TileSize, Size.Y * Game.TileSize);

	public Room(Point2 cell)
		: this(Guid.NewGuid().ToString("N"), new Point2(cell.X * Game.Columns, cell.Y * Game.Rows), DefaultSize)
	{
	}

	public Room(Point2 position, Point2 size)
		: this(Guid.NewGuid().ToString("N"), position, size)
	{
	}

	public Room(string path)
		: this(Path.GetFileNameWithoutExtension(path), Point2.Zero, DefaultSize, path)
	{
	}

	public Room CloneRuntime()
	{
		var clone = new Room(Id, Position, Size)
		{
			MusicArea = MusicArea,
			AmbientColor = AmbientColor,
			AmbientIntensity = AmbientIntensity,
			LightNoise = LightNoise
		};

		for (var x = 0; x < Size.X; x ++)
		for (var y = 0; y < Size.Y; y ++)
			clone.Tiles[x, y] = Tiles[x, y];
		foreach (var actor in Actors)
			clone.Actors.Add(ActorSpecs.CloneObject(actor));

		clone.RebuildGeometry();
		return clone;
	}

	private Room(string id, Point2 position, Point2 size, string? path = null)
	{
		JsonObject? json = null;
		if (path != null)
		{
			if (!string.Equals(Path.GetExtension(path), ".json", StringComparison.OrdinalIgnoreCase))
				throw new InvalidOperationException($"Room files must be JSON: {path}");

			json = JsonNode.Parse(File.ReadAllText(path!))?.AsObject();
			if (json != null)
			{
				position = ActorSpecs.ReadPoint(json, "position") ?? position;
				if (ActorSpecs.ReadPoint(json, "size") is { } jsonSize && jsonSize.X > 0 && jsonSize.Y > 0)
					size = jsonSize;
			}
		}

		Id = id;
		Position = position;
		Size = size;
		Tiles = new char[Size.X, Size.Y];

		for (int x = 0; x < Size.X; x ++)
		for (int tileY = 0; tileY < Size.Y; tileY ++)
			Tiles[x, tileY] = '0';

		if (path == null)
		{
			RebuildGeometry();
			return;
		}

		if (json != null)
		{
			LoadJson(json);
			RebuildGeometry();
			return;
		}
	}

	private void LoadJson(JsonObject json)
	{
		MusicArea = ActorSpecs.ReadString(json, "musicArea");
		if (string.IsNullOrWhiteSpace(MusicArea))
			MusicArea = null;
		AmbientColor = ReadHexColor(json, "ambientColor") ?? DefaultAmbientColor;
		AmbientIntensity = ReadFloat(json, "ambientIntensity", DefaultAmbientIntensity);
		LightNoise = ReadFloat(json, "lightNoise", DefaultLightNoise);

		if (json.TryGetPropertyValue("tiles", out var tilesNode) && tilesNode is JsonArray rows)
		{
			for (var y = 0; y < rows.Count && y < Size.Y; y ++)
			{
				var row = rows[y]?.GetValue<string>() ?? "";
				for (var x = 0; x < row.Length && x < Size.X; x ++)
					Tiles[x, y] = row[x];
			}
		}

		if (json.TryGetPropertyValue("actors", out var actorsNode) && actorsNode is JsonArray actorArray)
		{
			foreach (var node in actorArray)
				if (node is JsonObject actor)
					Actors.Add(ActorSpecs.CloneObject(actor));
		}
	}

	public void Set(Point2 tile, char ch)
	{
		if (tile.X >= 0 && tile.Y >= 0 && tile.X < Size.X && tile.Y < Size.Y)
		{
			Tiles[tile.X, tile.Y] = ch;
			RebuildGeometry();
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
			["from"] = specId,
			["position"] = new JsonArray(localPosition.X, localPosition.Y)
		});
	}

	public void AddActorTypeLocal(string typeName, Point2 localPosition)
	{
		Actors.Add(new JsonObject
		{
			["type"] = typeName,
			["position"] = new JsonArray(localPosition.X, localPosition.Y)
		});
	}

	public void RebuildGeometry()
	{
		CollisionRects.Clear();
		SlopeTiles.Clear();
		slopeLookup.Clear();
		LightOccluders.Clear();
		LightOccluderPolygons.Clear();
		ReflectionRects.Clear();

		BuildSlopeTiles();
		BuildRects(CollisionRects, static entry => entry.BlocksMovement && !entry.IsSlope);
		BuildRects(LightOccluders, static entry => entry.CastsShadow && !entry.IsSlope);
		BuildSlopeLightOccluders();
		BuildRects(ReflectionRects, static entry => entry.ReflectsSprites);
	}

	public bool TryGetSlopeTile(Point2 tile, out SlopeTile slope)
		=> slopeLookup.TryGetValue(tile, out slope);

	public void MoveTo(Point2 position)
	{
		Position = position;
		RebuildGeometry();
	}

	public void ResizeTo(RectInt bounds)
	{
		if (bounds.Width <= 0 || bounds.Height <= 0)
			return;

		var previousPosition = Position;
		var previousSize = Size;
		var previousTiles = Tiles;

		Position = bounds.Position;
		Size = bounds.Size;
		Tiles = new char[Size.X, Size.Y];

		for (var x = 0; x < Size.X; x ++)
		for (var y = 0; y < Size.Y; y ++)
		{
			var oldX = Position.X + x - previousPosition.X;
			var oldY = Position.Y + y - previousPosition.Y;
			Tiles[x, y] = oldX >= 0 && oldY >= 0 && oldX < previousSize.X && oldY < previousSize.Y
				? previousTiles[oldX, oldY]
				: '0';
		}

		var actorOffset = previousPosition - Position;
		if (actorOffset != Point2.Zero)
		{
			foreach (var actor in Actors)
			{
				var position = ActorSpecs.ReadPoint(actor, "position") ?? Point2.Zero;
				actor["position"] = new JsonArray(position.X + actorOffset.X * Game.TileSize, position.Y + actorOffset.Y * Game.TileSize);
			}
		}

		RebuildGeometry();
	}

	private static Color? ReadHexColor(JsonObject json, string key)
	{
		if (!json.TryGetPropertyValue(key, out var node) || node == null)
			return null;

		var text = node.GetValue<string>().TrimStart('#');
		return uint.TryParse(text, System.Globalization.NumberStyles.HexNumber, null, out var hex)
			? text.Length <= 6 ? new Color((int)hex) : new Color(hex)
			: null;
	}

	private static float ReadFloat(JsonObject json, string key, float fallback)
	{
		if (!json.TryGetPropertyValue(key, out var node) || node == null)
			return fallback;

		return float.TryParse(node.ToString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var value)
			? value
			: fallback;
	}

	private static string ColorToHex(Color color)
		=> $"{color.R:X2}{color.G:X2}{color.B:X2}";

	private void BuildSlopeTiles()
	{
		for (var y = 0; y < Size.Y; y ++)
		for (var x = 0; x < Size.X;)
		{
			if (!IsSlopeTile(x, y))
			{
				x ++;
				continue;
			}

			var start = x;
			var slopeChar = Tiles[x, y];
			while (x < Size.X && Tiles[x, y] == slopeChar && IsSlopeTile(x, y))
				x ++;

			var runLength = x - start;
			var (direction, fill) = InferSlopeShape(start, y, runLength);
			for (var i = 0; i < runLength; i ++)
			{
				var slope = new SlopeTile(new Point2(start + i, y), runLength, i, direction, fill);
				SlopeTiles.Add(slope);
				slopeLookup[slope.Tile] = slope;
			}
		}
	}

	private (SlopeDirection Direction, SlopeFill Fill) InferSlopeShape(int startX, int y, int runLength)
	{
		var leftHigh = IsFullSolidTile(startX - 1, y);
		var leftLow = IsFullSolidTile(startX - 1, y + 1);
		var rightHigh = IsFullSolidTile(startX + runLength, y);
		var rightLow = IsFullSolidTile(startX + runLength, y + 1);
		var aboveLeft = IsFullSolidTile(startX, y - 1);
		var aboveRight = IsFullSolidTile(startX + runLength - 1, y - 1);

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

		var belowLeft = IsFullSolidTile(startX, y + 1);
		var belowRight = IsFullSolidTile(startX + runLength - 1, y + 1);
		if (belowRight && !belowLeft)
			return (SlopeDirection.DownRight, SlopeFill.BelowLine);
		if (belowLeft && !belowRight)
			return (SlopeDirection.UpRight, SlopeFill.BelowLine);

		return (SlopeDirection.DownRight, SlopeFill.BelowLine);
	}

	private bool IsSlopeTile(int x, int y)
		=> x >= 0 && y >= 0 && x < Size.X && y < Size.Y && Factory.Find(Tiles[x, y]) is { IsSlope: true };

	private bool IsFullSolidTile(int x, int y)
		=> x >= 0 && y >= 0 && x < Size.X && y < Size.Y && Factory.Find(Tiles[x, y]) is { BlocksMovement: true, IsSlope: false };

	private void BuildSlopeLightOccluders()
	{
		foreach (var slope in SlopeTiles)
			LightOccluderPolygons.Add(new(GetSlopePolygon(slope), slope.WorldBounds(this)));
	}

	public Point2[] GetSlopePolygon(SlopeTile slope)
	{
		var rect = slope.WorldBounds(this);
		var leftHeight = SlopeSurfaceHeight(slope, slope.RunIndex);
		var rightHeight = SlopeSurfaceHeight(slope, slope.RunIndex + 1);
		var leftSurface = new Point2(rect.Left, rect.Top + leftHeight);
		var rightSurface = new Point2(rect.Right, rect.Top + rightHeight);
		var points = new List<Point2>();

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

		return points.ToArray();
	}

	private static int SlopeSurfaceHeight(SlopeTile slope, int runEdgeIndex)
	{
		if (slope.Direction == SlopeDirection.DownRight)
			return runEdgeIndex * Game.TileSize / slope.RunLength;

		return (slope.RunLength - runEdgeIndex) * Game.TileSize / slope.RunLength;
	}

	private void BuildRects(List<RectInt> output, Func<Factory.Entry, bool> predicate)
	{
		var used = new bool[Size.X, Size.Y];

		for (var y = 0; y < Size.Y; y ++)
		for (var x = 0; x < Size.X; x ++)
		{
			if (used[x, y] || !Matches(x, y, predicate))
				continue;

			var width = 1;
			while (x + width < Size.X && !used[x + width, y] && Matches(x + width, y, predicate))
				width ++;

			var height = 1;
			while (y + height < Size.Y && RowMatches(x, y + height, width, used, predicate))
				height ++;

			for (var yy = y; yy < y + height; yy ++)
			for (var xx = x; xx < x + width; xx ++)
				used[xx, yy] = true;

			output.Add(new RectInt(
				WorldBounds.X + x * Game.TileSize,
				WorldBounds.Y + y * Game.TileSize,
				width * Game.TileSize,
				height * Game.TileSize));
		}
	}

	private bool RowMatches(int x, int y, int width, bool[,] used, Func<Factory.Entry, bool> predicate)
	{
		for (var xx = x; xx < x + width; xx ++)
		{
			if (used[xx, y] || !Matches(xx, y, predicate))
				return false;
		}

		return true;
	}

	private bool Matches(int x, int y, Func<Factory.Entry, bool> predicate)
	{
		if (Factory.Find(Tiles[x, y]) is not { } entry)
			return false;

		return predicate(entry);
	}

	public void Save()
	{
		File.WriteAllText(
			Path.Join(Assets.AssetsPath, "Rooms", $"{Id}.json"),
			ToJsonString());
	}

	public string ToJsonString()
	{
		var json = new JsonObject
		{
			["position"] = new JsonArray(Position.X, Position.Y),
			["size"] = new JsonArray(Size.X, Size.Y)
		};

		if (!string.IsNullOrEmpty(MusicArea))
			json["musicArea"] = MusicArea;
		if (AmbientColor != DefaultAmbientColor)
			json["ambientColor"] = ColorToHex(AmbientColor);
		if (AmbientIntensity != DefaultAmbientIntensity)
			json["ambientIntensity"] = AmbientIntensity;
		if (LightNoise != DefaultLightNoise)
			json["lightNoise"] = LightNoise;

		var rows = new JsonArray();
		for (var y = 0; y < Size.Y; y ++)
		{
			var row = new StringBuilder(Size.X);
			for (var x = 0; x < Size.X; x ++)
				row.Append(Tiles[x, y]);
			rows.Add(row.ToString());
		}
		json["tiles"] = rows;

		if (Actors.Count > 0)
		{
			var actors = new JsonArray();
			foreach (var actor in Actors)
				actors.Add(ActorSpecs.PruneForSave(actor));
			json["actors"] = actors;
		}

		return ActorSpecs.ToJsonString(json);
	}

	public void LoadJsonState(string jsonText)
	{
		var json = JsonNode.Parse(jsonText)?.AsObject() ?? [];
		Position = ActorSpecs.ReadPoint(json, "position") ?? Position;
		if (ActorSpecs.ReadPoint(json, "size") is { } size && size.X > 0 && size.Y > 0 && size != Size)
		{
			Size = size;
			Tiles = new char[Size.X, Size.Y];
		}

		for (var x = 0; x < Size.X; x ++)
		for (var y = 0; y < Size.Y; y ++)
			Tiles[x, y] = '0';
		Actors.Clear();
		LoadJson(json);
		RebuildGeometry();
	}
}
