using System.Numerics;
using Foster.Framework;

namespace TinyLink;

public class Grid : Actor
{
	private bool[,] grid = new bool[Game.Columns, Game.Rows];
	private Subtexture[,] tilemap = new Subtexture[Game.Columns, Game.Rows];

	public Grid()
	{
		Hitbox = new(grid);
	}

	public void Set(int x, int y, Subtexture subtexture)
	{
		EnsureSize(x + 1, y + 1);
		tilemap[x, y] = subtexture;
		grid[x, y] = true;
	}

	public void Clear()
	{
		Array.Clear(tilemap);
		Array.Clear(grid);
	}

	public override void Render(Batcher batcher)
	{
		for (int x = 0; x < grid.GetLength(0); x ++)
		for (int y = 0; y < grid.GetLength(1); y ++)
		{
			if (grid[x, y])
				batcher.Image(tilemap[x, y], new Vector2(x, y) * Game.TileSize, Color.White);
		}
	}

	private void EnsureSize(int width, int height)
	{
		if (width <= grid.GetLength(0) && height <= grid.GetLength(1))
			return;

		var newWidth = Math.Max(width, grid.GetLength(0));
		var newHeight = Math.Max(height, grid.GetLength(1));
		var nextGrid = new bool[newWidth, newHeight];
		var nextTilemap = new Subtexture[newWidth, newHeight];

		for (var x = 0; x < grid.GetLength(0); x ++)
		for (var y = 0; y < grid.GetLength(1); y ++)
		{
			nextGrid[x, y] = grid[x, y];
			nextTilemap[x, y] = tilemap[x, y];
		}

		grid = nextGrid;
		tilemap = nextTilemap;
		Hitbox = new(grid);
	}
}
