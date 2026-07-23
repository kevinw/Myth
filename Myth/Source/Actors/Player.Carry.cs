using System.Numerics;
using Foster.Framework;

namespace Myth;

// Carrying: burrow-pop underneath a Carryable to hoist it overhead (PopOutOfBurrow ->
// TryCatchCarryable). While carrying, the standing hitbox swells upward to cover the cargo,
// and duck/roll/dash/burrow, the wall moves, and rope grabs are gated off (Pawn.IsCarrying).
// Attack throws: a forward arc, Up for a high toss, Down to set it down at the feet.
public partial class Player : Pawn
{
	static float ThrowSpeedX => 140f;
	static float ThrowUpKick => -60f;
	static float ThrowUpForce => -180f;
	static float ThrowVelocityInherit => 0.5f;
	static Vector2 HurtDropVelocity => new(30f, -70f); // x is applied away from Facing
	static RectInt CarryCatchRect => new(-4, -16, 8, 16); // probe above the burrow-pop point

	Carryable? carried;

	protected override bool IsCarrying => carried != null;

	protected override RectInt CurrentStandingHitboxRect => carried == null
		? StandingHitboxRect
		: new(StandingHitboxRect.X, StandingHitboxRect.Y - carried.CarryHeight,
			StandingHitboxRect.Width, StandingHitboxRect.Height + carried.CarryHeight);

	// Cargo rides with its hitbox bottom at the top of the standing hitbox.
	Point2 CarryPosition => Position + new Point2(0, StandingHitboxRect.Y);

	bool TryCatchCarryable()
	{
		var probe = CarryCatchRect + Position;
		foreach (var actor in Game.Actors)
		{
			if (actor is not Carryable carryable || carryable.CarriedBy != null || Game.destroying.Contains(actor))
				continue;
			if (!carryable.Hitbox.Overlaps(probe - carryable.Position))
				continue;

			carried = carryable;
			carryable.OnPickedUp(this);
			carryable.Position = CarryPosition;
			return true;
		}
		return false;
	}

	void ThrowCarried()
	{
		if (carried == null)
			return;

		var cargo = carried;
		if (input.MoveY > 0)
		{
			TryDropCarriedAtFeet(cargo); // denied when the space is blocked: keep carrying
			return;
		}

		carried = null;
		var inherit = Velocity * ThrowVelocityInherit;
		var velocity = input.MoveY < 0
			? new Vector2(inherit.X, ThrowUpForce + MathF.Min(inherit.Y, 0))
			: new Vector2((int)Facing * ThrowSpeedX + inherit.X, ThrowUpKick);
		cargo.Release(CarryPosition, velocity);
		Squish = new Vector2(0.8f, 1.2f);
	}

	// Held-Down release: set the cargo down in front of the feet instead of flinging it.
	bool TryDropCarriedAtFeet(Carryable cargo)
	{
		var reach = StandingHitboxRect.Width / 2 + cargo.Hitbox.Rect.Width / 2 + 1;
		var target = Position + new Point2((int)Facing * reach, 0);
		if (Game.OverlapsRoomCollision(cargo.Hitbox.Rect + target))
			return false;

		carried = null;
		cargo.Release(target, Vector2.Zero);
		return true;
	}

	void DropCarriedFromHurt()
	{
		if (carried == null)
			return;

		var cargo = carried;
		carried = null;
		cargo.Release(CarryPosition, new Vector2(-(int)Facing * HurtDropVelocity.X, HurtDropVelocity.Y));
	}

	void UpdateCarriedPosition()
	{
		if (carried != null)
			carried.Position = CarryPosition;
	}
}
