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

	private readonly List<Layer> layers = [];
	public override bool CastsReflection => false;

	public void Build(Room room, char tileType, Tileset tileset)
	{
		var layer = new Layer(tileset);
		layer.EnsureSize(room.Size.X + 1, room.Size.Y + 1);

		for (var x = 0; x <= room.Size.X; x ++)
		for (var y = 0; y <= room.Size.Y; y ++)
		{
			var materialMask = 0;
			if (IsFilled(room, tileType, x - 1, y - 1))
				materialMask |= 1;
			if (IsFilled(room, tileType, x - 1, y))
				materialMask |= 2;
			if (IsFilled(room, tileType, x, y - 1))
				materialMask |= 4;
			if (IsFilled(room, tileType, x, y))
				materialMask |= 8;

			if (materialMask == 0)
				continue;

			var mask = 0;
			if (room.IsTerrainJoinedToDualVertex(x - 1, y - 1, x, y))
				mask |= 1;
			if (room.IsTerrainJoinedToDualVertex(x - 1, y, x, y))
				mask |= 2;
			if (room.IsTerrainJoinedToDualVertex(x, y - 1, x, y))
				mask |= 4;
			if (room.IsTerrainJoinedToDualVertex(x, y, x, y))
				mask |= 8;

			if (mask == 0)
				continue;

			var tile = MaskToTile[mask];
			var seed = StableVariantSeed(room, tileType, x, y);
			layer.Tilemap[x, y] = new TileSlot(tileset.GetVariantTile(tile.X, tile.Y, seed), materialMask);
		}

		layers.Add(layer);
	}

	public void Clear()
	{
		layers.Clear();
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
		foreach (var layer in layers)
		{
			for (var x = 0; x < layer.Tilemap.GetLength(0); x ++)
			for (var y = 0; y < layer.Tilemap.GetLength(1); y ++)
			{
				var slot = layer.Tilemap[x, y];
				if (slot.Tile.Texture == null)
					continue;

				var position = new Vector2(x - 0.5f, y - 0.5f) * Game.TileSize;
				DrawQuadrant(batcher, slot, 1, position, new Rect(0, 0, Game.TileSize / 2, Game.TileSize / 2));
				DrawQuadrant(batcher, slot, 2, position, new Rect(0, Game.TileSize / 2, Game.TileSize / 2, Game.TileSize / 2));
				DrawQuadrant(batcher, slot, 4, position, new Rect(Game.TileSize / 2, 0, Game.TileSize / 2, Game.TileSize / 2));
				DrawQuadrant(batcher, slot, 8, position, new Rect(Game.TileSize / 2, Game.TileSize / 2, Game.TileSize / 2, Game.TileSize / 2));
			}
		}
	}

	private static void DrawQuadrant(Batcher batcher, TileSlot slot, int mask, Vector2 position, Rect clip)
	{
		if ((slot.MaterialMask & mask) == 0)
			return;

		batcher.Image(slot.Tile, clip, position + clip.Position, Vector2.Zero, Vector2.One, 0, Color.White);
	}

	private readonly struct TileSlot(Subtexture tile, int materialMask)
	{
		public readonly Subtexture Tile = tile;
		public readonly int MaterialMask = materialMask;
	}

	private sealed class Layer(Tileset tileset)
	{
		public readonly Tileset Tileset = tileset;
		public TileSlot[,] Tilemap = new TileSlot[Game.Columns + 1, Game.Rows + 1];

		public void EnsureSize(int width, int height)
		{
			if (Tilemap.GetLength(0) == width && Tilemap.GetLength(1) == height)
				return;

			Tilemap = new TileSlot[width, height];
		}
	}
}
