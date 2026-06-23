using System.Numerics;
using Foster.Framework;

namespace Myth;

public class DualGrid : Actor
{
	const int halfTileSize = Game.TileSize / 2;

	internal static readonly Point2[] MaskToTile =
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

	// Reused across Build() calls so RebuildGeometry (which fires per editor edit)
	// doesn't churn the GC. Cleared and refilled each merge instead of reallocated.
	private readonly List<VisibilityPolygon.Segment> mergeScratch = [];
	private readonly List<SegmentRun> horizontalRuns = [];
	private readonly List<SegmentRun> verticalRuns = [];

	private static readonly Comparison<SegmentRun> CompareRuns = static (a, b) =>
	{
		var axis = a.Axis.CompareTo(b.Axis);
		return axis != 0 ? axis : a.Start.CompareTo(b.Start);
	};
	public Factory.TileLayer Layer { get; set; } = Factory.TileLayer.Foreground;
	public override bool CastsReflection => false;

	public void Build(Room room, char tileType, Tileset tileset, Factory.TileLayer layer)
	{
		Layer = layer;
		for (var x = 0; x <= room.Size.X; x++)
			for (var y = 0; y <= room.Size.Y; y++)
			{
				var materialMask = 0;
				if (room.IsDualMaterialFilled(tileType, layer, x - 1, y - 1))
					materialMask |= TileQuadrants.TopLeft;
				if (room.IsDualMaterialFilled(tileType, layer, x - 1, y))
					materialMask |= TileQuadrants.BottomLeft;
				if (room.IsDualMaterialFilled(tileType, layer, x, y - 1))
					materialMask |= TileQuadrants.TopRight;
				if (room.IsDualMaterialFilled(tileType, layer, x, y))
					materialMask |= TileQuadrants.BottomRight;

				if (materialMask == 0)
					continue;

				var mask = layer == Factory.TileLayer.Foreground
					? room.GetDualTerrainJoinMask(x, y)
					: materialMask;
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

		MergeAxisAlignedVisibilitySegments();
	}

	public void Clear()
	{
		drawCommands.Clear();
		visibilitySegments.Clear();
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

			DrawQuadrant(batcher, command, tile, TileQuadrants.TopLeft, new Rect(0, 0, halfTileSize, halfTileSize));
			DrawQuadrant(batcher, command, tile, TileQuadrants.BottomLeft, new Rect(0, halfTileSize, halfTileSize, halfTileSize));
			DrawQuadrant(batcher, command, tile, TileQuadrants.TopRight, new Rect(halfTileSize, 0, halfTileSize, halfTileSize));
			DrawQuadrant(batcher, command, tile, TileQuadrants.BottomRight, new Rect(halfTileSize, halfTileSize, halfTileSize, halfTileSize));
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
			midpoint.X < halfTileSize
				? midpoint.Y < halfTileSize ? TileQuadrants.TopLeft : TileQuadrants.BottomLeft
				: midpoint.Y < halfTileSize ? TileQuadrants.TopRight : TileQuadrants.BottomRight;
		return (materialMask & quadrant) != 0;
	}

	private void MergeAxisAlignedVisibilitySegments()
	{
		if (visibilitySegments.Count <= 1)
			return;

		mergeScratch.Clear();
		horizontalRuns.Clear();
		verticalRuns.Clear();

		foreach (var segment in visibilitySegments)
		{
			if (NearlyEqual(segment.A.Y, segment.B.Y))
				horizontalRuns.Add(new(
					Quantize(segment.A.Y),
					MathF.Min(segment.A.X, segment.B.X),
					MathF.Max(segment.A.X, segment.B.X)));
			else if (NearlyEqual(segment.A.X, segment.B.X))
				verticalRuns.Add(new(
					Quantize(segment.A.X),
					MathF.Min(segment.A.Y, segment.B.Y),
					MathF.Max(segment.A.Y, segment.B.Y)));
			else
				mergeScratch.Add(segment);
		}

		horizontalRuns.Sort(CompareRuns);
		verticalRuns.Sort(CompareRuns);

		MergeRuns(horizontalRuns, mergeScratch, horizontal: true);
		MergeRuns(verticalRuns, mergeScratch, horizontal: false);

		visibilitySegments.Clear();
		visibilitySegments.AddRange(mergeScratch);
	}

	private static void MergeRuns(List<SegmentRun> runs, List<VisibilityPolygon.Segment> merged, bool horizontal)
	{
		if (runs.Count == 0)
			return;

		var current = runs[0];
		for (var i = 1; i < runs.Count; i++)
		{
			var next = runs[i];
			if (NearlyEqual(current.Axis, next.Axis) && next.Start <= current.End + 0.001f)
			{
				current = current with { End = MathF.Max(current.End, next.End) };
				continue;
			}

			if (current.End > current.Start)
				merged.Add(CreateSegment(current, horizontal));
			current = next;
		}

		if (current.End > current.Start)
			merged.Add(CreateSegment(current, horizontal));
	}

	private static VisibilityPolygon.Segment CreateSegment(SegmentRun run, bool horizontal)
		=> horizontal
			? new(new Vector2(run.Start, run.Axis), new Vector2(run.End, run.Axis))
			: new(new Vector2(run.Axis, run.Start), new Vector2(run.Axis, run.End));

	private static bool NearlyEqual(float a, float b)
		=> MathF.Abs(a - b) <= 0.001f;

	private static float Quantize(float value)
		=> MathF.Round(value * 1000.0f) / 1000.0f;

	private readonly record struct SegmentRun(float Axis, float Start, float End);

	private readonly struct DrawCommand(Subtexture tile, Subtexture normal, Vector2 position, int materialMask)
	{
		public readonly Subtexture Tile = tile;
		public readonly Subtexture Normal = normal;
		public readonly Vector2 Position = position;
		public readonly int MaterialMask = materialMask;
	}
}
