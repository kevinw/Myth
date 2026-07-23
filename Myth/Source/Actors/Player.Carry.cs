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

	// Carried cargo rides on a damped spring driven by the player's acceleration, like the
	// hat (UpdateHat) but heavier: more lag, softer spring, a bigger cap. Render-only — the
	// offset lives in the cargo's Shift, never its Position, so collision is unaffected.
	static Vector2 CarrySpring => new(90f, 70f);
	static Vector2 CarryDamping => new(18f, 9f);
	static float CarryMotionResponse => 0.3f;
	static float CarryMaxOffset => 4f;
	static float CarryMaxVelocityDelta => 250f;

	Actor? carried; // always an ICarryable (Carryable cargo or Bud)
	int carriedHeight;
	Vector2 carryShift;
	Vector2 carryShiftVelocity;

	protected override bool IsCarrying => carried != null;

	protected override RectInt CurrentStandingHitboxRect => carried == null
		? StandingHitboxRect
		: new(StandingHitboxRect.X, StandingHitboxRect.Y - carriedHeight,
			StandingHitboxRect.Width, StandingHitboxRect.Height + carriedHeight);

	// Cargo rides with its hitbox bottom at the top of the standing hitbox.
	Point2 CarryPosition => Position + new Point2(0, StandingHitboxRect.Y);

	bool TryCatchCarryable()
	{
		var probe = CarryCatchRect + Position;
		Actor? best = null;
		foreach (var actor in Game.Actors)
		{
			if (actor is not ICarryable cargo || !cargo.CanBeCaught || Game.destroying.Contains(actor))
				continue;
			if (!actor.Hitbox.Overlaps(probe - actor.Position))
				continue;

			// Inert cargo wins a tie over a pawn (Bud) sharing the probe.
			if (best == null || best is not Carryable && actor is Carryable)
				best = actor;
		}
		if (best == null)
			return false;

		carried = best;
		carriedHeight = ((ICarryable)best).CarryHeight;
		carryShift = Vector2.Zero;
		carryShiftVelocity = Vector2.Zero;
		((ICarryable)best).OnPickedUp(this);
		best.Position = CarryPosition;
		return true;
	}

	void ReleaseCarried(Actor cargo, Point2 position, Vector2 velocity)
	{
		carried = null;
		carriedHeight = 0;
		cargo.Shift = Vector2.Zero;
		((ICarryable)cargo).Release(position, velocity);
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

		var inherit = Velocity * ThrowVelocityInherit;
		var velocity = input.MoveY < 0
			? new Vector2(inherit.X, ThrowUpForce + MathF.Min(inherit.Y, 0))
			: new Vector2((int)Facing * ThrowSpeedX + inherit.X, ThrowUpKick);
		ReleaseCarried(cargo, CarryPosition, velocity);
		Squish = new Vector2(0.8f, 1.2f);
	}

	// Held-Down release: set the cargo down in front of the feet instead of flinging it.
	bool TryDropCarriedAtFeet(Actor cargo)
	{
		var reach = StandingHitboxRect.Width / 2 + cargo.Hitbox.Rect.Width / 2 + 1;
		var target = Position + new Point2((int)Facing * reach, 0);
		if (Game.OverlapsSolid(cargo, cargo.Hitbox.Rect + target))
			return false;

		ReleaseCarried(cargo, target, Vector2.Zero);
		return true;
	}

	void DropCarriedFromHurt()
	{
		if (carried == null)
			return;

		var cargo = carried;
		ReleaseCarried(cargo, CarryPosition, new Vector2(-(int)Facing * HurtDropVelocity.X, HurtDropVelocity.Y));
	}

	void UpdateCarriedPosition(Vector2 previousVelocity)
	{
		if (carried == null)
			return;

		carried.Position = CarryPosition;

		var velocityDelta = Vector2.Clamp(Velocity - previousVelocity, new(-CarryMaxVelocityDelta), new(CarryMaxVelocityDelta));
		carryShiftVelocity -= velocityDelta * CarryMotionResponse;

		var dt = Math.Min(Time.Delta, 1f / 30f);
		carryShiftVelocity.X += -carryShift.X * CarrySpring.X * dt;
		carryShiftVelocity.Y += -carryShift.Y * CarrySpring.Y * dt;
		carryShiftVelocity.X *= MathF.Exp(-CarryDamping.X * dt);
		carryShiftVelocity.Y *= MathF.Exp(-CarryDamping.Y * dt);
		carryShift += carryShiftVelocity * dt;

		if (carryShift.LengthSquared() > CarryMaxOffset * CarryMaxOffset)
		{
			var outward = Vector2.Normalize(carryShift);
			carryShift = outward * CarryMaxOffset;
			carryShiftVelocity -= outward * MathF.Max(0, Vector2.Dot(carryShiftVelocity, outward));
		}

		carried.Shift = carryShift;
	}

	// Debug overlay while the carry/burrow-ride interplay is being verified.
	void ShowCarryDebug()
	{
		var carrying = carried == null ? "-" : $"{carried.GetType().Name}@{carried.Position}";
		var player = $"{State}{(ducking ? " duck" : "")} passenger={burrowPassenger != null} " +
			$"hitbox={CurrentStandingHitboxRect} pos={Position}";
		var bud = Game.GetFirst<Bud>()?.CarryDebugState ?? "-";
		Game.Show<string, string, string>(carrying, player, bud);
	}
}
