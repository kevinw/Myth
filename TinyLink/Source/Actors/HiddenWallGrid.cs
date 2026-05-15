using Foster.Framework;

namespace TinyLink;

public class HiddenWallGrid : Actor
{
	private bool[,] hidden = new bool[0, 0];
	private float[,] alpha = new float[0, 0];
	private Subtexture[,] tilemap = new Subtexture[0, 0];
	private Point2 size;

	public HiddenWallGrid()
	{
		Depth = 4;
		CollidesWithSolids = false;
	}

	public override bool CastsSpriteShadow => false;
	public override bool CastsReflection => false;

	public void Resize(Point2 roomSize)
	{
		size = roomSize;
		hidden = new bool[size.X, size.Y];
		alpha = new float[size.X, size.Y];
		tilemap = new Subtexture[size.X, size.Y];
	}

	public void AddRect(Point2 localPosition, Point2 pixelSize)
	{
		if (Assets.GetTileset("castle") is not { } tileset)
			return;

		var left = Math.Clamp((int)MathF.Floor(localPosition.X / (float)Game.TileSize), 0, size.X);
		var top = Math.Clamp((int)MathF.Floor(localPosition.Y / (float)Game.TileSize), 0, size.Y);
		var right = Math.Clamp((int)MathF.Ceiling((localPosition.X + pixelSize.X) / (float)Game.TileSize), 0, size.X);
		var bottom = Math.Clamp((int)MathF.Ceiling((localPosition.Y + pixelSize.Y) / (float)Game.TileSize), 0, size.Y);

		for (var y = top; y < bottom; y ++)
		for (var x = left; x < right; x ++)
		{
			if (hidden[x, y])
				continue;

			Rng rng = new(x + y * size.X);
			hidden[x, y] = true;
			alpha[x, y] = 1.0f;
			tilemap[x, y] = tileset.GetRandomTile(ref rng);
		}
	}

	public override void Update()
	{
		base.Update();

		if (Game.GetFirst<Player>() is { } player && player.Hitbox.Shape == Hitbox.Shapes.Rect)
		{
			var playerBounds = player.Hitbox.Rect + player.Position - Position;
			for (var y = 0; y < size.Y; y ++)
			for (var x = 0; x < size.X; x ++)
			{
				if (!hidden[x, y] || alpha[x, y] <= 0)
					continue;

				var cell = new RectInt(x * Game.TileSize, y * Game.TileSize, Game.TileSize, Game.TileSize);
				if (playerBounds.Overlaps(cell))
					RevealConnected(new Point2(x, y));
			}
		}

		for (var y = 0; y < size.Y; y ++)
		for (var x = 0; x < size.X; x ++)
			if (hidden[x, y] && alpha[x, y] < 1.0f)
				alpha[x, y] = Calc.Approach(alpha[x, y], 0, Time.Delta * 5.0f);
	}

	private void RevealConnected(Point2 start)
	{
		var queue = new Queue<Point2>();
		queue.Enqueue(start);

		while (queue.Count > 0)
		{
			var tile = queue.Dequeue();
			if (tile.X < 0 || tile.Y < 0 || tile.X >= size.X || tile.Y >= size.Y)
				continue;
			if (!hidden[tile.X, tile.Y] || alpha[tile.X, tile.Y] < 1.0f)
				continue;

			alpha[tile.X, tile.Y] = 0.99f;
			queue.Enqueue(tile + Point2.Left);
			queue.Enqueue(tile + Point2.Right);
			queue.Enqueue(tile + Point2.Up);
			queue.Enqueue(tile + Point2.Down);
		}
	}

	public override void Render(Batcher batcher)
	{
		for (var y = 0; y < size.Y; y ++)
		for (var x = 0; x < size.X; x ++)
		{
			if (!hidden[x, y] || alpha[x, y] <= 0.01f)
				continue;

			batcher.Image(tilemap[x, y], new Point2(x * Game.TileSize, y * Game.TileSize), Color.White * alpha[x, y]);
		}
	}

	public void ForEachVisibleCell(Action<RectInt, float, Subtexture> action)
	{
		for (var y = 0; y < size.Y; y ++)
		for (var x = 0; x < size.X; x ++)
		{
			if (!hidden[x, y] || alpha[x, y] <= 0.01f)
				continue;

			action(
				new RectInt(Position.X + x * Game.TileSize, Position.Y + y * Game.TileSize, Game.TileSize, Game.TileSize),
				alpha[x, y],
				tilemap[x, y]);
		}
	}
}
