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

	private readonly List<DrawCommand> drawCommands = [];
	public override bool CastsReflection => false;

	public void Build(Room room, char tileType, Tileset tileset)
	{
		for (var x = 0; x <= room.Size.X; x ++)
		for (var y = 0; y <= room.Size.Y; y ++)
		{
			var materialMask = 0;
			if (IsFilled(room, tileType, x - 1, y - 1))
				materialMask |= TileQuadrants.TopLeft;
			if (IsFilled(room, tileType, x - 1, y))
				materialMask |= TileQuadrants.BottomLeft;
			if (IsFilled(room, tileType, x, y - 1))
				materialMask |= TileQuadrants.TopRight;
			if (IsFilled(room, tileType, x, y))
				materialMask |= TileQuadrants.BottomRight;

			if (materialMask == 0)
				continue;

			var mask = room.GetDualTerrainJoinMask(x, y);
			if (mask == 0)
				continue;

			var tile = MaskToTile[mask];
			var seed = StableVariantSeed(room, tileType, x, y);
			var subtexture = tileset.GetVariantTile(tile.X, tile.Y, seed);
			if (subtexture.Texture == null)
				continue;

			drawCommands.Add(new DrawCommand(
				subtexture,
				new Vector2(x - 0.5f, y - 0.5f) * Game.TileSize,
				materialMask));
		}
	}

	public void Clear()
	{
		drawCommands.Clear();
	}

	private static bool IsFilled(Room room, char tileType, int x, int y)
	{
		return x >= 0 && y >= 0 && x < room.Size.X && y < room.Size.Y && room.Tiles[x, y] == tileType;
	}

	private static int StableVariantSeed(Room room, char tileType, int x, int y)
	{
		unchecked
		{
			var worldTile = room.WorldBounds.TopLeft / Game.TileSize + new Point2(x, y);
			var hash = (uint)worldTile.X * 0x8da6b343u;
			hash ^= (uint)worldTile.Y * 0xd8163841u;
			hash ^= tileType * 0xcb1ab31fu;
			hash ^= hash >> 16;
			hash *= 0x7feb352du;
			hash ^= hash >> 15;
			hash *= 0x846ca68bu;
			hash ^= hash >> 16;
			return (int)hash;
		}
	}

	public override void Render(Batcher batcher)
	{
		foreach (var command in drawCommands)
		{
			if (command.MaterialMask == TileQuadrants.All)
			{
				batcher.Image(command.Tile, command.Position, Color.White);
				continue;
			}

			DrawQuadrant(batcher, command, TileQuadrants.TopLeft, new Rect(0, 0, Game.TileSize / 2, Game.TileSize / 2));
			DrawQuadrant(batcher, command, TileQuadrants.BottomLeft, new Rect(0, Game.TileSize / 2, Game.TileSize / 2, Game.TileSize / 2));
			DrawQuadrant(batcher, command, TileQuadrants.TopRight, new Rect(Game.TileSize / 2, 0, Game.TileSize / 2, Game.TileSize / 2));
			DrawQuadrant(batcher, command, TileQuadrants.BottomRight, new Rect(Game.TileSize / 2, Game.TileSize / 2, Game.TileSize / 2, Game.TileSize / 2));
		}
	}

	private static void DrawQuadrant(Batcher batcher, DrawCommand command, int mask, Rect clip)
	{
		if ((command.MaterialMask & mask) == 0)
			return;

		batcher.Image(command.Tile, clip, command.Position + clip.Position, Vector2.Zero, Vector2.One, 0, Color.White);
	}

	private readonly struct DrawCommand(Subtexture tile, Vector2 position, int materialMask)
	{
		public readonly Subtexture Tile = tile;
		public readonly Vector2 Position = position;
		public readonly int MaterialMask = materialMask;
	}
}
