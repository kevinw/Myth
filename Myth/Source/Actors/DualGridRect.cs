using System.Numerics;
using Foster.Framework;

namespace Myth;

// A solid rectangle of actor skinned with a dual-grid tileset, so it lights, shadows, dithers and
// reflects like the solid tiles around it (MovingPlatform, Crusher, FlipBlock). The render passes in
// Game.Rendering walk actors as these rather than by type.
abstract class DualGridRect : Actor, IResizable, IShapeOccluder
{
	// Matches the metal tile's reflectionOpacity.
	public const float SurfaceReflectionOpacity = 0.7f;

	public Point2 Size { get; set; }
	// Editor-pickable; see Manager.Editor material picker.
	public string Material = "metal-dual";

	protected Point2 originalPosition;

	protected DualGridRect(Point2 defaultSize)
	{
		Size = defaultSize;
		Mask = Masks.Solid;
	}

	public override void Added()
	{
		base.Added();
		// Size is applied from the spec before Added (editor resizes write it), so build the
		// hitbox here to match the authored footprint rather than the construction-time default.
		Hitbox = new Hitbox(new RectInt(0, 0, Size.X, Size.Y));
		originalPosition = Position;
	}

	// Tile variants are picked from the authored cell, so a block that moves keeps its look.
	public virtual Point2 VariantTileOrigin => originalPosition / Game.TileSize;

	// How far the skin has travelled from where it was authored, so the dither pattern travels with it instead
	// of swimming underneath it. Keyed off RenderPosition, so a tremble carries the pattern along too.
	public virtual Vector2 DitherAnchorOffset => originalPosition - RenderPosition;

	public int TileColumns() => Math.Max(1, Size.X / Game.TileSize);
	public int TileRows() => Math.Max(1, Size.Y / Game.TileSize);
	public RectInt Bounds() => new(Position, Size);

	// The top face mirrors sprites the way a metal floor would. See DrawPlatformSurfaceReflections.
	public bool ReflectsSurface() => Material == "metal-dual";

	// The tile character whose dual tileset is this material; ties the skin to the tile kit so it
	// picks up the same variants and field colors as hand-placed tiles.
	public char MaterialTile()
	{
		var result = '0';
		var found = false;
		foreach (var (tile, entry) in Factory.Entries)
			if (entry.DualTilesetName == Material && (!found || tile < result))
				(found, result) = (true, tile);
		return result;
	}

	// Draws the cols x rows blob, or a flat fill when the material is missing.
	protected void RenderMaterial(Batcher batcher, bool normals, Color color)
	{
		if (Assets.GetTileset(Material) is { } tileset)
			DualGrid.RenderRect(batcher, tileset, TileColumns(), TileRows(), normals, color, VariantTileOrigin, MaterialTile());
		else
			batcher.Rect(new Rect(0, 0, Size.X, Size.Y), color);
	}

	// Stamps into the TileField with per-tile field colors, so its dither variation matches the
	// stone next to it instead of reading as one flat block.
	public void RenderTileField(Batcher batcher, Func<Point2, Color> colorForTile)
	{
		if (Assets.GetTileset(Material) is { } tileset)
			DualGrid.RenderRectTileField(batcher, tileset, TileColumns(), TileRows(), VariantTileOrigin, MaterialTile(), colorForTile);
		else
			batcher.Rect(new Rect(0, 0, Size.X, Size.Y), colorForTile(VariantTileOrigin));
	}

	// Occlude light like the tiles it is skinned with: DualGrid traces occluder segments from the
	// tileset art, so the shadow follows the rounded corners instead of a bare rectangle.
	public virtual bool CastsShadow => Visible;

	public void AppendOccluderSegments(List<ShadowSegment> segments)
	{
		// From RenderPosition, so a trembling block's shadow trembles with it rather than standing still.
		if (Assets.GetTileset(Material) is { } tileset)
			DualGrid.AppendRectOccluders(segments, tileset, TileColumns(), TileRows(), (Vector2)RenderPosition, VariantTileOrigin, MaterialTile());
		else
			OccluderShape.FromRect(Bounds()).AppendSegments(segments);
	}

	public override void Render(Batcher batcher) => RenderMaterial(batcher, normals: false, Color.White);
	// Spriteless, so the Actor default RenderShadow (which stamps the sprite silhouette into the
	// particle-collision coverage mask) draws nothing and particles pass straight through. Stamp the
	// dual-tile art instead: the CoverageMask shader keys off texture alpha, so the mask picks up the
	// rounded blob corners rather than a bare rectangle.
	public override void RenderShadow(Batcher batcher, Color color) => RenderMaterial(batcher, normals: false, color);
	public override void RenderNormals(Batcher batcher) => RenderMaterial(batcher, normals: true, Color.White);
	public override bool CastsReflection => true;
	public override void RenderReflection(Batcher batcher, Color color) => RenderMaterial(batcher, normals: false, color);

	// Spriteless, so the sprite-based default is a zero rect; report the real footprint so
	// reflection culling (and any other visual-bounds consumer) sees the whole block.
	public override Rect GetVisualBounds() => new(Position.X, Position.Y, Size.X, Size.Y);
}
