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
	public float Speed = 40f;
	// Jumpthru-masked instead of solid: the player can jump up through the belt and land on top.
	public bool JumpThrough;
	public Color ColorA = new(0x3a2416);
	public Color ColorB = new(0xb5885a);

	const float DashLength = 4f;

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
