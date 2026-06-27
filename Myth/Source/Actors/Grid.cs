using System.Numerics;
using Foster.Framework;

namespace Myth;

public class Grid : Actor
{
	bool[,] grid = new bool[Game.Columns, Game.Rows];
	Subtexture[,] tilemap = new Subtexture[Game.Columns, Game.Rows];
	float[,] alpha = NewAlphaMap(Game.Columns, Game.Rows);
	public Factory.TileLayer Layer { get; set; } = Factory.TileLayer.Foreground;

	public Grid()
	{
		Hitbox = new(grid);
	}

	public override bool CastsReflection => false;

	public void Set(int x, int y, Subtexture subtexture, float opacity = 1.0f)
	{
		EnsureSize(x + 1, y + 1);
		tilemap[x, y] = subtexture;
		grid[x, y] = true;
		alpha[x, y] = opacity;
	}

	public void Clear()
	{
		Array.Clear(tilemap);
		Array.Clear(grid);
		FillAlpha(alpha);
	}

	public void Resize(Point2 size)
	{
		grid = new bool[size.X, size.Y];
		tilemap = new Subtexture[size.X, size.Y];
		alpha = NewAlphaMap(size.X, size.Y);
		Hitbox = new(grid);
	}

	public override void Render(Batcher batcher)
	{
		for (int x = 0; x < grid.GetLength(0); x++)
			for (int y = 0; y < grid.GetLength(1); y++)
			{
				if (grid[x, y])
					batcher.Image(tilemap[x, y], new Vector2(x, y) * Game.TileSize, Color.White * alpha[x, y]);
			}
	}

	void EnsureSize(int width, int height)
	{
		if (width <= grid.GetLength(0) && height <= grid.GetLength(1))
			return;

		var newWidth = Math.Max(width, grid.GetLength(0));
		var newHeight = Math.Max(height, grid.GetLength(1));
		var nextGrid = new bool[newWidth, newHeight];
		var nextTilemap = new Subtexture[newWidth, newHeight];
		var nextAlpha = NewAlphaMap(newWidth, newHeight);

		for (var x = 0; x < grid.GetLength(0); x++)
			for (var y = 0; y < grid.GetLength(1); y++)
			{
				nextGrid[x, y] = grid[x, y];
				nextTilemap[x, y] = tilemap[x, y];
				nextAlpha[x, y] = alpha[x, y];
			}

		grid = nextGrid;
		tilemap = nextTilemap;
		alpha = nextAlpha;
		Hitbox = new(grid);
	}

	static float[,] NewAlphaMap(int width, int height)
	{
		var result = new float[width, height];
		FillAlpha(result);
		return result;
	}

	static void FillAlpha(float[,] target)
	{
		for (var x = 0; x < target.GetLength(0); x++)
			for (var y = 0; y < target.GetLength(1); y++)
				target[x, y] = 1.0f;
	}


}
