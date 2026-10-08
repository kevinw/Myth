using Foster.Framework;
using System.Numerics;

namespace Myth;

// A stationary solid strip that drags whatever stands on it sideways. Direction comes from
// Facing (flip in the editor), speed and the two stripe colors from the inspector. Riders move
// with the same pixel-exact, collision-aware push as MovingPlatform (Actor.MovePixels), so they
// scrape to a stop against walls instead of clipping through.
[GameActor(Tags = ActorTag.Obstacle, Sprite = "Editor/conveyor_belt", Anchor = SpriteAnchor.TopLeft,
	LivePositionEditable = true, LiveResizable = true)]
public class ConveyorBelt : Actor, IResizable, IPenDrawn
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
			if (CanCarry(actor) && actor.OverlapsAny(Point2.Down, this) && !actor.OverlapsAny(Point2.Zero, this))
				actor.MovePixels(new Point2(step, 0));
	}

	internal override Vector2 LocalLiftVelocity => new(Facing * Speed, 0);

	void IPenDrawn.Draw(Pen pen)
	{
		if (Size.X <= 0 || Size.Y <= 0)
			return;

		var middle = Size.Y / 2f;
		pen.Tube(new Vector2(0, middle), new Vector2(Size.X, middle), Size.Y, Size.Y, Vector2.Zero, ColorA,
			Color.Lerp(ColorA, Color.Black, 0.5f));

		// Alternate dashes scroll at the carry speed, so the surface visibly moves at the same rate it drags
		// riders -- and each dash leads with a groove, so light ripples along the belt at that speed too.
		var period = DashLength * 2;
		var offset = (Facing * Speed * Time.SecondsF) % period;
		if (offset < 0)
			offset += period;
		var seam = Color.Lerp(ColorA, Color.Black, 0.35f);
		for (var x = offset - period; x < Size.X; x += period)
		{
			var left = MathF.Max(x, 0);
			var right = MathF.Min(x + DashLength, Size.X);
			if (right > left)
				pen.Stroke(new Vector2((left + right) / 2, 0), new Vector2((left + right) / 2, Size.Y), right - left, ColorB);
			if (x >= 0.5f)
				pen.Groove(new Vector2(x, 0), new Vector2(x, Size.Y), 1, seam);
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
