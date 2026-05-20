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
	private readonly List<VisibilityPolygon.Segment> visibilitySegments = [];
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
			var normal = tileset.GetVariantNormalTile(tile.X, tile.Y, seed);
			var occluders = tileset.GetVariantOccluders(tile.X, tile.Y, seed);
			var position = new Vector2(x - 0.5f, y - 0.5f) * Game.TileSize;

			drawCommands.Add(new DrawCommand(
				subtexture,
				normal,
				position,
				materialMask));

			AppendWorldVisibilitySegments(position, materialMask, occluders);
		}
	}

	public void Clear()
	{
		drawCommands.Clear();
		visibilitySegments.Clear();
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
		=> RenderTiles(batcher, static command => command.Tile);

	public override void RenderNormals(Batcher batcher)
		=> RenderTiles(batcher, static command => command.Normal);

	private void RenderTiles(Batcher batcher, Func<DrawCommand, Subtexture> getTile)
	{
		foreach (var command in drawCommands)
		{
			var tile = getTile(command);
			if (tile.Texture == null)
				continue;

			if (command.MaterialMask == TileQuadrants.All)
			{
				batcher.Image(tile, command.Position, Color.White);
				continue;
			}

			DrawQuadrant(batcher, command, tile, TileQuadrants.TopLeft, new Rect(0, 0, Game.TileSize / 2, Game.TileSize / 2));
			DrawQuadrant(batcher, command, tile, TileQuadrants.BottomLeft, new Rect(0, Game.TileSize / 2, Game.TileSize / 2, Game.TileSize / 2));
			DrawQuadrant(batcher, command, tile, TileQuadrants.TopRight, new Rect(Game.TileSize / 2, 0, Game.TileSize / 2, Game.TileSize / 2));
			DrawQuadrant(batcher, command, tile, TileQuadrants.BottomRight, new Rect(Game.TileSize / 2, Game.TileSize / 2, Game.TileSize / 2, Game.TileSize / 2));
		}
	}

	private static void DrawQuadrant(Batcher batcher, DrawCommand command, Subtexture tile, int mask, Rect clip)
	{
		if ((command.MaterialMask & mask) == 0)
			return;

		batcher.Image(tile, clip, command.Position + clip.Position, Vector2.Zero, Vector2.One, 0, Color.White);
	}

	public void AppendVisibilitySegments(List<VisibilityPolygon.Segment> segments, Vector2 lightPosition, float radius)
	{
		foreach (var segment in visibilitySegments)
		{
			if (VisibilityPolygon.CircleOverlaps(segment, lightPosition, radius))
				segments.Add(segment);
		}
	}

	public void AppendAllVisibilitySegments(List<VisibilityPolygon.Segment> segments)
		=> segments.AddRange(visibilitySegments);

	private void AppendWorldVisibilitySegments(Vector2 commandPosition, int materialMask, Tileset.OccluderSegment[] occluders)
	{
		foreach (var local in occluders)
		{
			if (!OwnsOccluderSegment(materialMask, local))
				continue;

			visibilitySegments.Add(new(
				Position + commandPosition + local.A,
				Position + commandPosition + local.B));
		}
	}

	private static bool OwnsOccluderSegment(int materialMask, Tileset.OccluderSegment segment)
	{
		var midpoint = (segment.A + segment.B) * 0.5f;
		var quadrant =
			midpoint.X < Game.TileSize / 2
				? midpoint.Y < Game.TileSize / 2 ? TileQuadrants.TopLeft : TileQuadrants.BottomLeft
				: midpoint.Y < Game.TileSize / 2 ? TileQuadrants.TopRight : TileQuadrants.BottomRight;
		return (materialMask & quadrant) != 0;
	}

	private readonly struct DrawCommand(Subtexture tile, Subtexture normal, Vector2 position, int materialMask)
	{
		public readonly Subtexture Tile = tile;
		public readonly Subtexture Normal = normal;
		public readonly Vector2 Position = position;
		public readonly int MaterialMask = materialMask;
	}
}
