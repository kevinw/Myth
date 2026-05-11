using System.Numerics;
using Foster.Framework;

namespace TinyLink;

public class DualGrid : Actor
{
	private static readonly Point2[] MaskToTile =
	[
		new(0, 3),
		new(3, 3),
		new(0, 0),
		new(3, 2),
		new(0, 2),
		new(1, 2),
		new(2, 3),
		new(3, 1),
		new(1, 3),
		new(0, 1),
		new(3, 0),
		new(2, 0),
		new(1, 0),
		new(2, 2),
		new(1, 1),
		new(2, 1),
	];

	private readonly Subtexture[,] tilemap = new Subtexture[Game.Columns + 1, Game.Rows + 1];
	private Tileset? tileset;

	public void Build(Room room, char tileType, Tileset tileset)
	{
		this.tileset = tileset;

		for (var x = 0; x <= Game.Columns; x ++)
		for (var y = 0; y <= Game.Rows; y ++)
		{
			var mask = 0;
			if (IsFilled(room, tileType, x - 1, y - 1))
				mask |= 1;
			if (IsFilled(room, tileType, x - 1, y))
				mask |= 2;
			if (IsFilled(room, tileType, x, y - 1))
				mask |= 4;
			if (IsFilled(room, tileType, x, y))
				mask |= 8;

			if (mask == 0)
				continue;

			var tile = MaskToTile[mask];
			tilemap[x, y] = tileset.GetTile(tile.X, tile.Y);
		}
	}

	public void Clear()
	{
		Array.Clear(tilemap);
	}

	private static bool IsFilled(Room room, char tileType, int x, int y)
	{
		return x >= 0 && y >= 0 && x < Game.Columns && y < Game.Rows && room.Tiles[x, y] == tileType;
	}

	public override void Render(Batcher batcher)
	{
		if (tileset == null)
			return;

		for (var x = 0; x <= Game.Columns; x ++)
		for (var y = 0; y <= Game.Rows; y ++)
		{
			var tile = tilemap[x, y];
			if (tile.Texture != null)
				batcher.Image(tile, new Vector2(x - 0.5f, y - 0.5f) * Game.TileSize, Color.White);
		}
	}
}
