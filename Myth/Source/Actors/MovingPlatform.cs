using Foster.Framework;
using System.Numerics;
namespace Myth;

[GameActor(Tags = ActorTag.Obstacle, Sprite = "editor_moving_platform", Anchor = SpriteAnchor.TopLeft)]
class MovingPlatform: Actor {

	int direction = 1;
	float waitTimer = 0;
	float moveRemainder;                 // sub-pixel travel accumulator (we move in whole pixels)
	readonly List<Actor> riders = [];    // reusable scratch: actors standing on top this frame

	public Point2 Size = new(48, 24);
	public float WaitTime = 0.856f;
	public float Speed = 30f;
	public int TravelDistance = 80;

	public MovingPlatform()
	{
		Mask = Masks.Solid;
		CollidesWithSolids = true;
		Hitbox = new Hitbox(new RectInt(0, 0, Size.X, Size.Y));
	}

	Point2 originalPosition;

	public override void Added() {
		base.Added();
		originalPosition = Position;
	}

	public override void Update() {
		if (waitTimer > 0) {
			waitTimer -= Time.Delta;
			if (waitTimer <= 0)
				direction = -direction;
			return;
		}
		if (direction == 0)
			return;

		// Accumulate fractional travel and move in whole pixels, clamped to the patrol range so
		// the platform and its riders never drift apart by rounding.
		moveRemainder += direction * Speed * Time.Delta;
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
		}
	}

	// Move the platform by a whole-pixel delta, carrying actors riding on top and pushing any
	// caught in its path — the standard moving-solid pattern. While moving actors we go
	// intangible (Mask cleared) so a carried rider isn't "grounded" on us (lets it follow us
	// down) and a pushed actor only collides with real walls (so it can squish, not snag on us).
	void MoveSolid(Point2 step)
	{
		riders.Clear();
		foreach (var actor in Game.Actors)
		{
			if (!IsCarryable(actor))
				continue;
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

		foreach (var actor in Game.Actors)
		{
			if (!IsCarryable(actor))
				continue;
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

	public override void Render(Batcher batcher) {
		batcher.Rect(new (0, 0, Size.X, Size.Y), Color.FloralWhite);
	}

}
