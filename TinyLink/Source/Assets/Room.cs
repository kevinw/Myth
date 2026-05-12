
using System.Text;
using Foster.Framework;

namespace TinyLink;

public class Room
{
	/// <summary>
	/// World Location of the Room
	/// </summary>
	public readonly Point2 Cell;

	/// <summary>
	/// Grid of Tiles
	/// </summary>
	public readonly char[,] Tiles = new char[Game.Columns, Game.Rows];

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
	public RectInt WorldBounds => new(Cell.X * Game.Width, Cell.Y * Game.Height, Game.Width, Game.Height);

	public Room(Point2 cell)
	{
		Cell = cell;
		Text = string.Empty;
		for (int x = 0; x < Game.Columns; x ++)
		for (int y = 0; y < Game.Rows; y ++)
			Tiles[x, y] = '0';

		RebuildGeometry();
	}

	public Room(Point2 cell, string path)
	{
		Cell = cell;

		var lines = File.ReadAllLines(path);
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
				// parse row of tiles
				else if (y < Game.Rows)
				{
					for (int x = 0; x < line.Length && x < Game.Columns; x ++)
						Tiles[x, y] = line[x];
					y++;
			}
		}

		Text = text.ToString();
		RebuildGeometry();
	}

	public void Set(Point2 tile, char ch)
	{
		if (tile.X >= 0 && tile.Y >= 0 && tile.X < Game.Columns && tile.Y < Game.Rows)
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
		var used = new bool[Game.Columns, Game.Rows];

		for (var y = 0; y < Game.Rows; y ++)
		for (var x = 0; x < Game.Columns; x ++)
		{
			if (used[x, y] || !Matches(x, y, predicate))
				continue;

			var width = 1;
			while (x + width < Game.Columns && !used[x + width, y] && Matches(x + width, y, predicate))
				width ++;

			var height = 1;
			while (y + height < Game.Rows && RowMatches(x, y + height, width, used, predicate))
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

			// write tile grid next
		for (int y = 0; y < Game.Rows; y ++)
		{
			for (int x = 0; x < Game.Columns; x ++)
				result.Append(Tiles[x, y]);
			result.AppendLine();
		}

		// output to file
		File.WriteAllText(
			Path.Join(Assets.AssetsPath, "Rooms", $"{Cell.X}x{Cell.Y}.txt"),
			result.ToString());
	}
}
