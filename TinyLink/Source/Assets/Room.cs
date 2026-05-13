
using System.Text;
using Foster.Framework;

namespace TinyLink;

public class Room
{
	public static readonly Point2 DefaultSize = new(Game.Columns, Game.Rows);

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
	public readonly char[,] Tiles;

	/// <summary>
	/// World-space rectangles built from adjacent movement-blocking tiles.
	/// </summary>
	public readonly List<RectInt> CollisionRects = [];

	/// <summary>
	/// World-space rectangles built from adjacent light-occluding tiles.
	/// </summary>
	public readonly List<RectInt> LightOccluders = [];

	/// <summary>
	/// Optional Text string. There's no way to edit this in-game, but
	/// it is used to display title/ending text.
	/// </summary>
	public string Text = string.Empty;

	/// <summary>
	/// Optional FMOD background music Area parameter label.
	/// </summary>
	public string? MusicArea;

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

	public Room(Point2 cell, string path)
		: this(Path.GetFileNameWithoutExtension(path), new Point2(cell.X * Game.Columns, cell.Y * Game.Rows), DefaultSize, path)
	{
	}

	private Room(string id, Point2 position, Point2 size, string? path = null)
	{
		var lines = path != null ? File.ReadAllLines(path) : [];
		foreach (var line in lines)
		{
			if (line.StartsWith("@id ", StringComparison.OrdinalIgnoreCase))
			{
				var idValue = line["@id ".Length..].Trim();
				if (idValue.Length > 0)
					id = idValue;
			}
			else if (line.StartsWith("@position ", StringComparison.OrdinalIgnoreCase))
			{
				if (TryParsePoint(line["@position ".Length..], out var value))
					position = value;
			}
			else if (line.StartsWith("@size ", StringComparison.OrdinalIgnoreCase))
			{
				if (TryParsePoint(line["@size ".Length..], out var value) && value.X > 0 && value.Y > 0)
					size = value;
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
			Text = string.Empty;
			RebuildGeometry();
			return;
		}

		var text = new StringBuilder();
		var y = 0;

			foreach (var line in lines)
			{
				// parse text line
				if (line.StartsWith(":"))
				{
					text.AppendLine(line[1..]);
				}
				// parse metadata line
				else if (line.StartsWith("@area ", StringComparison.OrdinalIgnoreCase))
				{
					MusicArea = line["@area ".Length..].Trim();
					if (MusicArea.Length <= 0)
						MusicArea = null;
				}
				else if (line.StartsWith("@id ", StringComparison.OrdinalIgnoreCase) ||
				         line.StartsWith("@position ", StringComparison.OrdinalIgnoreCase) ||
				         line.StartsWith("@size ", StringComparison.OrdinalIgnoreCase))
				{
				}
				// parse row of tiles
				else if (y < Size.Y)
				{
					for (int x = 0; x < line.Length && x < Size.X; x ++)
						Tiles[x, y] = line[x];
					y++;
			}
		}

		Text = text.ToString();
		RebuildGeometry();
	}

	public void Set(Point2 tile, char ch)
	{
		if (tile.X >= 0 && tile.Y >= 0 && tile.X < Size.X && tile.Y < Size.Y)
		{
			Tiles[tile.X, tile.Y] = ch;
			RebuildGeometry();
		}
	}

	public void RebuildGeometry()
	{
		CollisionRects.Clear();
		LightOccluders.Clear();

		BuildRects(CollisionRects, static entry => entry.BlocksMovement);
		BuildRects(LightOccluders, static entry => entry.CastsShadow);
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
			StringBuilder result = new();

			// write text lines first
			foreach (var line in Text.Split(new string[] { "\r\n", "\r", "\n" }, StringSplitOptions.RemoveEmptyEntries))
				result.AppendLine($":{line}");

			if (!string.IsNullOrEmpty(MusicArea))
				result.AppendLine($"@area {MusicArea}");
			result.AppendLine($"@id {Id}");
			result.AppendLine($"@position {Position.X},{Position.Y}");
			result.AppendLine($"@size {Size.X},{Size.Y}");

			// write tile grid next
		for (int y = 0; y < Size.Y; y ++)
		{
			for (int x = 0; x < Size.X; x ++)
				result.Append(Tiles[x, y]);
			result.AppendLine();
		}

		// output to file
		File.WriteAllText(
			Path.Join(Assets.AssetsPath, "Rooms", $"{Id}.txt"),
			result.ToString());
	}

	private static bool TryParsePoint(string value, out Point2 point)
	{
		point = Point2.Zero;
		var parts = value.Split(',', StringSplitOptions.TrimEntries);
		if (parts.Length != 2)
			return false;

		if (!int.TryParse(parts[0], out var x) || !int.TryParse(parts[1], out var y))
			return false;

		point = new Point2(x, y);
		return true;
	}
}
