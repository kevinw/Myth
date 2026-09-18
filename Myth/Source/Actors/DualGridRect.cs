using System.Numerics;
using Foster.Framework;

namespace Myth;

// A rectangle of actor skinned with a dual-grid tileset, so it lights, shadows, dithers and
// reflects like the solid tiles around it (MovingPlatform, FlipBlock). The render passes in
// Game.Rendering walk actors as these rather than by type.
public interface IDualGridRect
{
	Point2 Position { get; }
	Point2 Size { get; }
	bool Visible { get; }
	string Material { get; }

	// Tile variants are picked from the authored cell, so a block that moves keeps its look.
	Point2 VariantTileOrigin { get; }

	// How far the skin has travelled from where it was authored, so the dither pattern travels with
	// it instead of swimming underneath it.
	Vector2 DitherAnchorOffset { get; }
}

public static class DualGridRects
{
	// Matches the metal tile's reflectionOpacity.
	public const float SurfaceReflectionOpacity = 0.7f;

	public static int TileColumns(this IDualGridRect rect) => Math.Max(1, rect.Size.X / Game.TileSize);
	public static int TileRows(this IDualGridRect rect) => Math.Max(1, rect.Size.Y / Game.TileSize);
	public static RectInt Bounds(this IDualGridRect rect) => new(rect.Position.X, rect.Position.Y, rect.Size.X, rect.Size.Y);

	// The top face mirrors sprites the way a metal floor would. See DrawPlatformSurfaceReflections.
	public static bool ReflectsSurface(this IDualGridRect rect) => rect.Material == "metal-dual";

	// The tile character whose dual tileset is this material; ties the skin to the tile kit so it
	// picks up the same variants and field colors as hand-placed tiles.
	public static char MaterialTile(this IDualGridRect rect)
	{
		var result = '0';
		var found = false;
		foreach (var (tile, entry) in Factory.Entries)
			if (entry.DualTilesetName == rect.Material && (!found || tile < result))
				(found, result) = (true, tile);
		return result;
	}

	// Draws the cols x rows blob, or a flat fill when the material is missing.
	public static void Render(this IDualGridRect rect, Batcher batcher, bool normals, Color color)
	{
		if (Assets.GetTileset(rect.Material) is { } tileset)
			DualGrid.RenderRect(batcher, tileset, rect.TileColumns(), rect.TileRows(), normals, color, rect.VariantTileOrigin, rect.MaterialTile());
		else
			batcher.Rect(new Rect(0, 0, rect.Size.X, rect.Size.Y), color);
	}

	// Occlude light like the tiles it is skinned with, so shadows follow the rounded corners rather
	// than a bare rectangle.
	public static void AppendOccluders(this IDualGridRect rect, List<ShadowSegment> segments)
	{
		if (Assets.GetTileset(rect.Material) is { } tileset)
			DualGrid.AppendRectOccluders(segments, tileset, rect.TileColumns(), rect.TileRows(),
				new Vector2(rect.Position.X, rect.Position.Y), rect.VariantTileOrigin, rect.MaterialTile());
		else
			OccluderShape.FromRect(rect.Bounds()).AppendSegments(segments);
	}

	// Stamps into the TileField with per-tile field colors, so its dither variation matches the
	// stone next to it instead of reading as one flat block.
	public static void RenderTileField(this IDualGridRect rect, Batcher batcher, Func<Point2, Color> colorForTile)
	{
		if (Assets.GetTileset(rect.Material) is { } tileset)
			DualGrid.RenderRectTileField(batcher, tileset, rect.TileColumns(), rect.TileRows(), rect.VariantTileOrigin, rect.MaterialTile(), colorForTile);
		else
			batcher.Rect(new Rect(0, 0, rect.Size.X, rect.Size.Y), colorForTile(rect.VariantTileOrigin));
	}
}
