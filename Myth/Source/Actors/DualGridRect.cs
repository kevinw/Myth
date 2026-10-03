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
	static readonly List<Actor> riders = [];  // reusable scratch: actors carried along this step
	static readonly List<Actor> crushed = [];

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

	// An actor we pushed or carried is still inside us after the move: it was pinned against a solid.
	protected virtual void Pinned(Actor actor, Point2 step) { }

	// Move by a whole-pixel delta, carrying actors riding on top and pushing any caught in our path.
	// While moving actors we go intangible (Mask cleared) so a carried rider isn't "grounded" on us
	// (lets it follow us down) and a pushed actor only collides with real walls (so it can squish,
	// not snag on us).
	protected void MoveSolid(Point2 step)
	{
		// TODO: riders are re-derived from overlap probes every step, so each new way of holding on
		// (side huggers, burrowers) needs another probe here. The replacement is an explicit
		// attachment: whoever grabs a surface records the actor and its offset -- the burrow walk
		// already knows which actor's face it is on -- and applies that actor's per-frame delta
		// itself. That drops the guessing below, survives diagonal motion (circle mode steps X and Y
		// together, so a rider straddling a face can lose contact on both axes at once), and gives
		// riders a defined order against their own movement instead of one that depends on where the
		// platform lands in Game.Actors.
		riders.Clear();
		foreach (var actor in Game.Actors) if (CanCarry(actor))
		{
			// A burrower sealed in a seam rides only the side it's following: the crusher's outline, or the
			// solid the crusher pinned it to. With two crushers meeting, the other side is a crusher too, and
			// both carrying it would tear it off whichever one it's actually walking.
			if (actor is Pawn { BurrowSeam: { } seam } pawn)
			{
				if (pawn.FollowsBurrowSeam ? seam == this : seam != this && actor.OverlapsAny(Point2.Zero, this))
					riders.Add(actor);
				continue;
			}

			// Riding = standing on top, embedded in us, or — while airborne — hugging a side face (a
			// wall-slide). Carrying side huggers keeps the player glued as we move, so wall-slide
			// contact doesn't flicker open 1px at a time (which toggles the slow-slide gravity and
			// makes the player vibrate). The !Grounded gate excludes things merely standing beside us.
			//
			// Embedded is the burrowing player: its 2x2 hitbox straddles our face by a single pixel,
			// so a step away from the trailing face breaks contact outright. The post-move push below
			// only catches the leading face (we move into that one), and the wall-hug branch can't
			// help because a burrower on a vertical face reads as Grounded -- its box shifted down
			// still overlaps us. Left behind, its boundary walk finds no edge and the burrow pops.
			if (IsStandingOn(actor) || actor.OverlapsAny(Point2.Zero, this)
				|| (!actor.Grounded() && (actor.OverlapsAny(Point2.Left, this) || actor.OverlapsAny(Point2.Right, this))))
				riders.Add(actor);
		}

		var savedMask = Mask;
		Mask = Masks.None;

		Position += step;
		Game.ActorMoved(this, step);

		crushed.Clear();
		foreach (var actor in Game.Actors) if (CanCarry(actor))
		{
			// Overlapping after the move => we ran into it: push. Otherwise, if it was riding on
			// top, carry it. Both move by the same delta with the actor's own collision, so they
			// scrape to a stop against walls instead of clipping through.
			var carried = riders.Contains(actor);
			if (carried || actor is not Pawn { BurrowSeam: not null } && actor.OverlapsAny(Point2.Zero, this))
			{
				// A carried burrower is a 2x2 probe sitting on our face, not a body, and its own travel never
				// collides either. Pushing it with MovePixels loses the last pixel as two crushers close -- the
				// probe no longer fits in the closing gap, so it stops while our face moves on and ends up inside
				// us, where no seam reads apart (Game.ResolveBurrowSeam) and the burrow is stuck. It rides our
				// face exactly instead. A burrower we ran into is still pushed, so it can't be shoved through
				// whatever is behind it.
				if (carried && actor is Pawn { State: Pawn.States.Burrow })
					actor.MoveRaw(step);
				else
					actor.MovePixels(step);
				if (actor.OverlapsAny(Point2.Zero, this))
					crushed.Add(actor);
			}
		}

		Mask = savedMask;
		foreach (var actor in crushed)
			Pinned(actor, step);
	}

	protected bool IsStandingOn(Actor actor)
		=> actor.OverlapsAny(Point2.Down, this) && !actor.OverlapsAny(Point2.Zero, this);
}
