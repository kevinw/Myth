using System.Numerics;
using System.Runtime.InteropServices;
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
	private readonly List<ShadowSegment> shadowSegments = [];

	// Reused across Build() calls so RebuildGeometry (which fires per editor edit)
	// doesn't churn the GC. Cleared and refilled each merge instead of reallocated.
	private readonly List<ShadowSegment> mergeScratch = [];
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
				var materialMask = room.GetDualMaterialMask(tileType, layer, x, y);
				if (materialMask == 0)
					continue;

				var mask = room.GetDualTerrainJoinMask(layer, x, y);
				if (mask == 0)
					continue;

				var art = LookupArt(tileset, MaskToTile[mask], room.TileSpacePosition + new Point2(x, y), tileType);
				if (art.Tile.Texture == null)
					continue;
				var position = new Vector2(x - 0.5f, y - 0.5f) * Game.TileSize;

				// Boundary tiles are pre-split into their filled quadrants, so drawing is a plain quad per command.
				if (materialMask == TileQuadrants.All)
					drawCommands.Add(new DrawCommand(art.Tile, art.Normal, position));
				else
					foreach (var (quadrant, clip) in Quadrants)
						if ((materialMask & quadrant) != 0)
							drawCommands.Add(new DrawCommand(art.Tile.GetClipSubtexture(clip), art.Normal.GetClipSubtexture(clip), position + clip.Position));

				AppendWorldShadowSegments(position, materialMask, art.Occluders);
			}

		MergeAxisAlignedShadowSegments();
	}

	/// The art one dual cell resolves to. Every renderer that marches a dual grid -- the room's
	/// DualGrid actor, the tile-field pass, hidden cover -- resolves its variant through here, so
	/// they cannot land on different ones for the same tile and disagree on screen.
	internal readonly record struct DualArt(Subtexture Tile, Subtexture Normal, Tileset.OccluderSegment[] Occluders);

	internal static DualArt LookupArt(Tileset tileset, Point2 dualCell, Point2 worldTile, char tileType)
	{
		var seed = StableVariantSeed(tileset.FillBlock(worldTile), tileType);
		var phase = tileset.FillPhase(worldTile);
		return new(
			tileset.GetVariantTile(dualCell.X, dualCell.Y, seed, phase),
			tileset.GetVariantNormalTile(dualCell.X, dualCell.Y, seed, phase),
			tileset.GetVariantOccluders(dualCell.X, dualCell.Y, seed, phase));
	}

	public void Clear()
	{
		drawCommands.Clear();
		shadowSegments.Clear();
	}

	internal static int StableVariantSeed(Point2 worldTile, char tileType)
		=> Hash32.TileVariantSeed(worldTile, tileType);

	public override void Render(Batcher batcher) => RenderTiles(batcher, normals: false);

	public override void RenderNormals(Batcher batcher) => RenderTiles(batcher, normals: true);

	// drawCommands covers the whole room, so without culling a big room costs the same as a
	// big screen -- and these tiles are drawn three times a frame (color, emissive occluder,
	// normals). Cull to the view in the actor's local space.
	private void RenderTiles(Batcher batcher, bool normals)
	{
		var view = Game.VisibleWorldBounds - Position;
		foreach (var command in CollectionsMarshal.AsSpan(drawCommands))
		{
			var tile = normals ? command.Normal : command.Tile;
			if (tile.Texture != null && view.Overlaps(new Rect(command.Position, tile.Size)))
				batcher.Image(tile, command.Position, Color.White);
		}
	}

	static readonly (int Quadrant, Rect Clip)[] Quadrants =
	[
		(TileQuadrants.TopLeft, new Rect(0, 0, halfTileSize, halfTileSize)),
		(TileQuadrants.BottomLeft, new Rect(0, halfTileSize, halfTileSize, halfTileSize)),
		(TileQuadrants.TopRight, new Rect(halfTileSize, 0, halfTileSize, halfTileSize)),
		(TileQuadrants.BottomRight, new Rect(halfTileSize, halfTileSize, halfTileSize, halfTileSize)),
	];

	// Draw one dual-grid tile at a local position, clipping to only the quadrants its materialMask
	// fills (so a boundary tile's transition art lines up with the filled cells). A clipped subtexture
	// drawn at an offset is the same quad as Batcher's clip overload, without its per-quad matrix.
	internal static void DrawTile(Batcher batcher, Subtexture tile, Vector2 position, int materialMask, Color color)
	{
		if (materialMask == TileQuadrants.All)
		{
			batcher.Image(tile, position, color);
			return;
		}

		foreach (var (quadrant, clip) in Quadrants)
			if ((materialMask & quadrant) != 0)
				batcher.Image(tile.GetClipSubtexture(clip), position + clip.Position, color);
	}

	// Render a solid cols x rows tile rectangle skinned with a dual tileset, in local space (origin at
	// 0,0). The fixed-shape version of the room marching above, for actors that want a tile-skinned
	// solid (e.g. MovingPlatform) without a Room behind them. Tiles sit at the -0.5 vertex offset, so
	// the material lines up with the full rectangle rather than insetting half a tile.
	internal static void RenderRect(Batcher batcher, Tileset tileset, int cols, int rows, bool normals, Color color, Point2 variantTileOrigin, char tileType)
	{
		foreach (var vertex in new RectVertices(cols, rows))
		{
			var cell = MaskToTile[vertex.Mask];
			var worldTile = variantTileOrigin + new Point2(vertex.X, vertex.Y);
			var seed = StableVariantSeed(tileset.FillBlock(worldTile), tileType);
			var phase = tileset.FillPhase(worldTile);
			var tile = normals ? tileset.GetVariantNormalTile(cell.X, cell.Y, seed, phase) : tileset.GetVariantTile(cell.X, cell.Y, seed, phase);
			if (tile.Texture == null)
				continue;

			DrawTile(batcher, tile, VertexPosition(vertex), vertex.Mask, color);
		}
	}

	// TileField stamp of the same rectangle: same coverage tiles as RenderRect, but each cell gets
	// its own field color (the stamp shader keeps only the source alpha and writes our vertex color).
	// colorForTile receives the cell's anchored world tile so the red/variant channel varies per tile
	// like room dual-grid stone, instead of one flat color across the whole platform.
	internal static void RenderRectTileField(Batcher batcher, Tileset tileset, int cols, int rows, Point2 variantTileOrigin, char tileType, Func<Point2, Color> colorForTile)
	{
		foreach (var vertex in new RectVertices(cols, rows))
		{
			var cell = MaskToTile[vertex.Mask];
			var worldTile = variantTileOrigin + new Point2(vertex.X, vertex.Y);
			var tile = tileset.GetVariantTile(cell.X, cell.Y, StableVariantSeed(tileset.FillBlock(worldTile), tileType), tileset.FillPhase(worldTile));
			if (tile.Texture == null)
				continue;

			DrawTile(batcher, tile, VertexPosition(vertex), vertex.Mask, colorForTile(worldTile));
		}
	}

	// World-space occluder segments for the same rectangle, traced from the tileset's per-tile
	// occluders so a shadow follows the (rounded) art -- the moving counterpart of the room occluders.
	internal static void AppendRectOccluders(List<ShadowSegment> segments, Tileset tileset, int cols, int rows, Vector2 origin, Point2 variantTileOrigin, char tileType)
	{
		foreach (var vertex in new RectVertices(cols, rows))
		{
			var cell = MaskToTile[vertex.Mask];
			var command = origin + VertexPosition(vertex);
			var worldTile = variantTileOrigin + new Point2(vertex.X, vertex.Y);
			foreach (var local in tileset.GetVariantOccluders(cell.X, cell.Y, StableVariantSeed(tileset.FillBlock(worldTile), tileType), tileset.FillPhase(worldTile)))
				if (OwnsOccluderSegment(vertex.Mask, local))
					segments.Add(new(command + local.A, command + local.B));
		}
	}

	static Vector2 VertexPosition(RectVertex vertex)
		=> new((vertex.X - 0.5f) * Game.TileSize, (vertex.Y - 0.5f) * Game.TileSize);

	readonly record struct RectVertex(int X, int Y, int Mask);

	// Marching-squares vertices of a solid cols x rows tile rectangle. Yields each boundary/interior
	// vertex with its dual-grid corner mask (which of the 4 surrounding cells are inside), skipping
	// fully-empty corners. Struct enumerator so the per-frame render/occluder walks don't allocate.
	readonly struct RectVertices(int cols, int rows)
	{
		public Enumerator GetEnumerator() => new(cols, rows);

		public struct Enumerator(int cols, int rows)
		{
			int vx = -1;
			int vy;
			public RectVertex Current { get; private set; }

			public bool MoveNext()
			{
				while (true)
				{
					if (++vx > cols)
					{
						vx = 0;
						vy++;
					}
					if (vy > rows)
						return false;

					var mask = 0;
					if (Inside(vx - 1, vy - 1)) mask |= TileQuadrants.TopLeft;
					if (Inside(vx - 1, vy)) mask |= TileQuadrants.BottomLeft;
					if (Inside(vx, vy - 1)) mask |= TileQuadrants.TopRight;
					if (Inside(vx, vy)) mask |= TileQuadrants.BottomRight;
					if (mask == 0)
						continue;

					Current = new RectVertex(vx, vy, mask);
					return true;
				}
			}

			bool Inside(int x, int y) => x >= 0 && y >= 0 && x < cols && y < rows;
		}
	}

	public void AppendAllShadowSegments(List<ShadowSegment> segments)
		=> segments.AddRange(shadowSegments);

	private void AppendWorldShadowSegments(Vector2 commandPosition, int materialMask, Tileset.OccluderSegment[] occluders)
	{
		foreach (var local in occluders)
		{
			if (!OwnsOccluderSegment(materialMask, local))
				continue;

			shadowSegments.Add(new(
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

	private void MergeAxisAlignedShadowSegments()
	{
		if (shadowSegments.Count <= 1)
			return;

		mergeScratch.Clear();
		horizontalRuns.Clear();
		verticalRuns.Clear();

		foreach (var segment in shadowSegments)
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

		shadowSegments.Clear();
		shadowSegments.AddRange(mergeScratch);
	}

	private static void MergeRuns(List<SegmentRun> runs, List<ShadowSegment> merged, bool horizontal)
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

	private static ShadowSegment CreateSegment(SegmentRun run, bool horizontal)
		=> horizontal
			? new(new Vector2(run.Start, run.Axis), new Vector2(run.End, run.Axis))
			: new(new Vector2(run.Axis, run.Start), new Vector2(run.Axis, run.End));

	private static bool NearlyEqual(float a, float b)
		=> MathF.Abs(a - b) <= 0.001f;

	private static float Quantize(float value)
		=> MathF.Round(value * 1000.0f) / 1000.0f;

	private readonly record struct SegmentRun(float Axis, float Start, float End);

	private readonly struct DrawCommand(Subtexture tile, Subtexture normal, Vector2 position)
	{
		public readonly Subtexture Tile = tile;
		public readonly Subtexture Normal = normal;
		public readonly Vector2 Position = position;
	}
}
