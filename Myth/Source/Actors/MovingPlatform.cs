using Foster.Framework;
using System.Numerics;
namespace Myth;

[GameActor(Tags = ActorTag.Obstacle, Sprite = "editor_moving_platform", Anchor = SpriteAnchor.TopLeft)]
class MovingPlatform : Actor, IResizable, IShapeOccluder
{
	public static readonly Point2 DefaultSize = new(48, 32);

	public Point2 Size { get; set; } = DefaultSize;

	// Dual (marching-squares) tileset the platform is skinned with -- supplies color + normals so it
	// lights and reflects like solid tiles. Editor-pickable; see Manager.Editor material picker.
	public string Material = "metal-dual";
	public float WaitTime = WAIT_TIME;
	const float WAIT_TIME = 0.856f;
	public float Speed = 30f;
	public float StartFriction = 8f;
	float currentSpeed = 0;
	public int TravelDistance = 80;


	int direction = 1;
	float waitTimer = WAIT_TIME;
	float moveRemainder;               // sub-pixel travel accumulator (we move in whole pixels)
	static readonly List<Actor> riders = [];  // reusable scratch: actors standing on top this frame
	Point2 originalPosition;

	public MovingPlatform()
	{
		Mask = Masks.Solid;
		CollidesWithSolids = true;
	}

	public override void Added()
	{
		base.Added();
		// Size is applied from the spec before Added (editor resizes write it), so build the
		// hitbox here to match the authored footprint rather than the construction-time default.
		Hitbox = new Hitbox(new RectInt(0, 0, Size.X, Size.Y));
		originalPosition = Position;
	}

	public override void Update()
	{
		if (waitTimer > 0)
		{
			waitTimer -= Time.Delta;
			return;
		}

		if (direction == 0)
		{
			return;
		}

		// Accumulate fractional travel and move in whole pixels, clamped to the patrol range so
		// the platform and its riders never drift apart by rounding.
		currentSpeed = Calc.Approach(currentSpeed, Speed, StartFriction * Time.Delta);
		moveRemainder += direction * currentSpeed * Time.Delta;
		var step = (int)moveRemainder;
		moveRemainder -= step;

		var min = originalPosition.X;
		var max = originalPosition.X + TravelDistance;
		step = Math.Clamp(Position.X + step, min, max) - Position.X;
		if (step != 0)
			MoveSolid(new Point2(step, 0));

		// Only the bound we're heading toward ends the leg — checking both would trip the min
		// bound instantly, since the platform starts there.
		if ((direction > 0 && Position.X >= max) || (direction < 0 && Position.X <= min))
		{
			waitTimer = WaitTime;
			moveRemainder = 0;
			direction = -direction;
			currentSpeed = 0;
		}
	}

	// Move the platform by a whole-pixel delta, carrying actors riding on top and pushing any
	// caught in its path. While moving actors we go intangible (Mask cleared) so a carried rider
	//  isn't "grounded" on us (lets it follow us down) and a pushed actor only collides with
	// real walls (so it can squish, not snag on us).
	void MoveSolid(Point2 step)
	{
		riders.Clear();
		foreach (var actor in Game.Actors) if (IsCarryable(actor))
		{
			// Riding = standing on top, or — while airborne — hugging a side face (a wall-slide).
			// Carrying side huggers keeps the player glued as we move, so wall-slide contact
			// doesn't flicker open 1px at a time (which toggles the slow-slide gravity and makes
			// the player vibrate). The !Grounded gate excludes things merely standing beside us.
			if (actor.OverlapsAny(Point2.Down, this)
				|| (!actor.Grounded() && (actor.OverlapsAny(Point2.Left, this) || actor.OverlapsAny(Point2.Right, this))))
				riders.Add(actor);
		}

		var savedMask = Mask;
		Mask = Masks.None;

		Position += step;

		foreach (var actor in Game.Actors) if (IsCarryable(actor))
		{
			// Overlapping after the move => we ran into it: push. Otherwise, if it was riding on
			// top, carry it. Both move by the same delta with the actor's own collision, so they
			// scrape to a stop against walls instead of clipping through.
			if (actor.OverlapsAny(Point2.Zero, this) || riders.Contains(actor))
				StepActor(actor, step);
		}

		Mask = savedMask;
	}

	// Move an actor by an exact whole-pixel delta, collision-aware. Critically this uses
	// MovePixel rather than Actor.Move so it does NOT touch the actor's sub-pixel `remainder`
	// accumulator — that accumulator is also fed by the actor's own velocity (e.g. a player
	// pressing into us while wall-sliding), and routing the push through it lets a pending
	// fraction cancel the move, leaving the actor stuck 1px inside us (false grounding/vibration).
	static void StepActor(Actor actor, Point2 step)
	{
		for (var n = Math.Abs(step.X); n > 0; n--)
			actor.MovePixel(new Point2(Math.Sign(step.X), 0));
		for (var n = Math.Abs(step.Y); n > 0; n--)
			actor.MovePixel(new Point2(0, Math.Sign(step.Y)));
	}

	bool IsCarryable(Actor actor) => actor != this && actor is not Grid && !actor.Mask.Has(Masks.Solid);

	// Occlude light like the dual tiles it's skinned with: DualGrid traces occluder segments from the
	// tileset art, so the shadow follows the rounded corners instead of a bare rectangle.
	public bool CastsShadow => true;

	public void AppendOccluderSegments(List<VisibilityPolygon.Segment> segments)
	{
		if (Assets.GetTileset(Material) is { } tileset)
			DualGrid.AppendRectOccluders(segments, tileset, TileColumns, TileRows, new Vector2(Position.X, Position.Y));
		else
			OccluderShape.FromRect(new RectInt(Position.X, Position.Y, Size.X, Size.Y)).AppendSegments(segments);
	}

	public override void Render(Batcher batcher) => RenderMaterial(batcher, normals: false, Color.White);
	public override void RenderNormals(Batcher batcher) => RenderMaterial(batcher, normals: true, Color.White);

	public override bool CastsReflection => true;
	public override void RenderReflection(Batcher batcher, Color color) => RenderMaterial(batcher, normals: false, color);

	// Spriteless, so the sprite-based default is a zero rect; report the real footprint so
	// reflection culling (and any other visual-bounds consumer) sees the whole platform.
	public override Rect GetVisualBounds() => new(Position.X, Position.Y, Size.X, Size.Y);

	// When skinned with a reflective material the top face mirrors sprites like a metal floor would.
	// See Game.Rendering DrawPlatformSurfaceReflections.
	public const float SurfaceReflectionOpacity = 0.7f; // matches the metal tile's reflectionOpacity
	public bool ReflectsSurface => Material == "metal-dual";
	public RectInt SurfaceReflectionBounds => new(Position.X, Position.Y, Size.X, Size.Y);

	// Skin the platform with its dual tileset (DualGrid renders the cols x rows blob); fall back to a
	// flat fill if the material is missing.
	void RenderMaterial(Batcher batcher, bool normals, Color color)
	{
		if (Assets.GetTileset(Material) is { } tileset)
			DualGrid.RenderRect(batcher, tileset, TileColumns, TileRows, normals, color);
		else
			batcher.Rect(new Rect(0, 0, Size.X, Size.Y), color);
	}

	int TileColumns => Math.Max(1, Size.X / Game.TileSize);
	int TileRows => Math.Max(1, Size.Y / Game.TileSize);

}
