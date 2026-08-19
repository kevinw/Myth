using Foster.Framework;
using System.Numerics;

namespace Myth;

// A stationary solid strip that drags whatever stands on it sideways. Direction comes from
// Facing (flip in the editor), speed and the two stripe colors from the inspector. Riders move
// with the same pixel-exact, collision-aware push as MovingPlatform (Actor.MovePixels), so they
// scrape to a stop against walls instead of clipping through.
[GameActor(Tags = ActorTag.Obstacle, Sprite = "Editor/conveyor_belt", Anchor = SpriteAnchor.TopLeft,
	LivePositionEditable = true, LiveResizable = true)]
public class ConveyorBelt : Actor, IResizable
{
	public static readonly Point2 DefaultSize = new(Game.TileSize * 6, Game.TileSize / 2);
	public static readonly ResizeAxes ResizableAxes = ResizeAxes.X;

	public Point2 Size { get; set; } = DefaultSize;
	[I(0, 160)] public float Speed = 40f;
	// Jumpthru-masked instead of solid: the player can jump up through the belt and land on top.
	public bool JumpThrough;
	public Color ColorA = new(0x3a2416);
	public Color ColorB = new(0xb5885a);

	const float DashLength = 4f;

	// Lands and footsteps read as wood, matching Jumpthru, whether or not the belt is jumpthru-masked.
	public override string? SurfaceSubstance => "Wood";

	float carryRemainder; // sub-pixel carry accumulator (riders move in whole pixels)

	public ConveyorBelt()
	{
		Sprite = null;
		Mask = Masks.Solid;
		CollidesWithSolids = false;
	}

	public override void Added()
	{
		base.Added();
		// Size and JumpThrough are applied from the spec before Added (editor writes them), so
		// size the hitbox and pick the mask here.
		Hitbox = new(new RectInt(0, 0, Size.X, Size.Y));
		Mask = JumpThrough ? Masks.Jumpthru : Masks.Solid;
	}

	public override void Update()
	{
		base.Update();
		carryRemainder += Facing * Speed * Time.Delta;
		var step = (int)carryRemainder;
		carryRemainder -= step;
		if (step == 0)
			return;

		// Standing on top = touching at the down-offset but not overlapping us — the same rule
		// jumpthru grounding uses, so an actor mid-way through a JumpThrough belt isn't dragged.
		foreach (var actor in Game.Actors)
			if ((IsCarryable(actor) || actor is ICarryable { CarriedBy: null }) && actor.OverlapsAny(Point2.Down, this) && !actor.OverlapsAny(Point2.Zero, this))
				actor.MovePixels(new Point2(step, 0));
	}

	public override void Render(Batcher batcher)
	{
		if (Size.X <= 0 || Size.Y <= 0)
			return;

		batcher.Rect(new Rect(0, 0, Size.X, Size.Y), ColorA);

		// Alternate dashes scroll at the carry speed, so the surface visibly moves at the same
		// rate it drags riders.
		var period = DashLength * 2;
		var offset = (Facing * Speed * Time.SecondsF) % period;
		if (offset < 0)
			offset += period;
		for (var x = offset - period; x < Size.X; x += period)
		{
			var left = MathF.Max(x, 0);
			var right = MathF.Min(x + DashLength, Size.X);
			if (right > left)
				batcher.Rect(new Rect(left, 0, right - left, Size.Y), ColorB);
		}
	}

	public override void RenderNormals(Batcher batcher)
	{
		if (Size.X <= 0 || Size.Y <= 0)
			return;

		var bevel = MathF.Min(Size.X, Size.Y) * 0.5f;
		var x0 = 0f;
		var x1 = bevel;
		var x2 = Size.X - bevel;
		var x3 = (float)Size.X;
		var y0 = 0f;
		var y1 = bevel;
		var y2 = Size.Y - bevel;
		var y3 = (float)Size.Y;
		var flat = new Color(0x8080ff);
		var left = NormalColor(-Vector2.UnitX);
		var right = NormalColor(Vector2.UnitX);
		var top = NormalColor(-Vector2.UnitY);
		var bottom = NormalColor(Vector2.UnitY);
		var topLeft = NormalColor(new Vector2(-1, -1).Normalized());
		var topRight = NormalColor(new Vector2(1, -1).Normalized());
		var bottomRight = NormalColor(Vector2.One.Normalized());
		var bottomLeft = NormalColor(new Vector2(-1, 1).Normalized());

		batcher.Quad(new(x0, y0), new(x1, y0), new(x1, y1), new(x0, y1), topLeft, top, flat, left);
		batcher.Quad(new(x1, y0), new(x2, y0), new(x2, y1), new(x1, y1), top, top, flat, flat);
		batcher.Quad(new(x2, y0), new(x3, y0), new(x3, y1), new(x2, y1), top, topRight, right, flat);
		batcher.Quad(new(x0, y1), new(x1, y1), new(x1, y2), new(x0, y2), left, flat, flat, left);
		batcher.Quad(new(x1, y1), new(x2, y1), new(x2, y2), new(x1, y2), flat);
		batcher.Quad(new(x2, y1), new(x3, y1), new(x3, y2), new(x2, y2), flat, right, right, flat);
		batcher.Quad(new(x0, y2), new(x1, y2), new(x1, y3), new(x0, y3), left, flat, bottom, bottomLeft);
		batcher.Quad(new(x1, y2), new(x2, y2), new(x2, y3), new(x1, y3), flat, flat, bottom, bottom);
		batcher.Quad(new(x2, y2), new(x3, y2), new(x3, y3), new(x2, y3), flat, right, bottomRight, bottom);
	}

	static Color NormalColor(Vector2 direction)
		=> new(direction.X * 0.5f + 0.5f, direction.Y * 0.5f + 0.5f, 0.5f, 1f);

	// Spriteless, so the sprite-based default is a zero rect; report the real footprint.
	public override Rect GetVisualBounds() => new(Position.X, Position.Y, Size.X, Size.Y);

#if EDITOR
	sealed class Editor : Manager.ActorEditor<ConveyorBelt>
	{
		internal override void Gizmos(Manager.EditorActor actor, ref Manager.EditorGizmos gizmos)
		{
			var center = actor.Bounds.CenterF;
			var direction = actor.GetFacing().CardinalX;
			var color = new Color(0x66d9ff);
			gizmos.Arrow(center, center + direction * 16f, gizmos.LineWeight * 2, color);
		}
	}
#endif
}
