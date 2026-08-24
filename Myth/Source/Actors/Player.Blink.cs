using System.Numerics;
using Foster.Framework;

namespace Myth;

public partial class Player
{
	private static float BlinkStrikeWindup => 0.18f;
	private static float BlinkStrikeRange => 96;
	private static float BlinkStrikeHeight => 6;
	private static float BlinkStrikeBounceForce => -118;
	private static float BlinkStrikeBounceTime => 0.12f;

	private Actor? blinkStrikeTarget;
	private bool blinkStrikeResolved;

	private void BlinkStrikeState()
	{
		ducking = false;
		Play(GetAttackAnimation(), false);
		Velocity.X = Calc.Approach(Velocity.X, 0, AttackFriction * Time.Delta);

		if (blinkStrikeTarget != null && !Game.Actors.Contains(blinkStrikeTarget))
			blinkStrikeTarget = null;

		if (!blinkStrikeResolved && state.Time >= BlinkStrikeWindup)
		{
			blinkStrikeResolved = true;
			if (blinkStrikeTarget != null && TryBlinkStrike(blinkStrikeTarget))
			{
				StartJump(BlinkStrikeBounceForce, BlinkStrikeBounceTime);
				Velocity.X = 0;
				Game.HitStun(0.08f);
				Game.Shake(0.05f);
				AudioEvent("sword");
			}
		}

		if (state.Time >= BlinkStrikeWindup + 0.12f)
		{
			blinkStrikeTarget = null;
			State = States.Normal;
		}
	}

	private Actor? FindBlinkStrikeTarget()
	{
		var bestDistance = float.MaxValue;
		Actor? best = null;
		var origin = Position + new Point2(0, -6);
		var beam = Facing == Signs.Positive
			? new RectInt((int)origin.X, (int)(origin.Y - BlinkStrikeHeight * 0.5f), (int)BlinkStrikeRange, (int)BlinkStrikeHeight)
			: new RectInt((int)(origin.X - BlinkStrikeRange), (int)(origin.Y - BlinkStrikeHeight * 0.5f), (int)BlinkStrikeRange, (int)BlinkStrikeHeight);

		foreach (var actor in Game.Actors)
		{
			if (actor == this || !actor.Mask.Has(Masks.Enemy))
				continue;

			var bounds = actor.Hitbox.Rect + actor.Position;
			if (!actor.OverlapsCombatBounds(beam))
				continue;
			var distance = Facing == Signs.Positive ? bounds.Left - origin.X : origin.X - bounds.Right;
			if (distance < bestDistance)
			{
				bestDistance = distance;
				best = actor;
			}
		}

		return best;
	}

	private bool TryBlinkStrike(Actor target)
	{
		if (target.Hitbox.Shape != Hitbox.Shapes.Rect)
			return false;

		var placement = FindBlinkStrikePlacement(target);
		if (placement == null)
			return false;

		var from = Position + new Point2(0, -6);
		var to = target.Position + target.Hitbox.Rect.Center;
		Game.CollisionRectsThisFrame.Add(LineBounds(from, to));
		EmitPlasmaStrike(from, to);
		Position = placement.Value;
		Facing = target.Position.X >= Position.X ? Signs.Positive : Signs.Negative;
		Hit(target);
		return true;
	}

	private Point2? FindBlinkStrikePlacement(Actor target)
	{
		var targetBounds = target.Hitbox.Rect + target.Position;
		var basePosition = new Point2(targetBounds.Center.X, targetBounds.Top - 2);
		var offsets = new[]
		{
			new Point2(0, 0),
			new Point2(0, -8),
			new Point2(0, -16),
			new Point2(-8, -8),
			new Point2(8, -8),
			new Point2(-16, -12),
			new Point2(16, -12),
		};

		foreach (var offset in offsets)
		{
			var candidate = basePosition + offset;
			var bounds = StandingHitboxRect + candidate;
			if (Game.OverlapsRoomCollision(bounds))
				continue;

			var overlapsSolid = false;
			foreach (var actor in Game.Actors)
			{
				if (actor == this || actor == target || actor is Grid || !actor.Mask.Has(Masks.Solid))
					continue;
				if (actor.Hitbox.Overlaps(bounds - actor.Position))
				{
					overlapsSolid = true;
					break;
				}
			}
			if (!overlapsSolid)
				return candidate;
		}

		return null;
	}

	private static RectInt LineBounds(Vector2 from, Vector2 to)
	{
		var left = (int)MathF.Floor(MathF.Min(from.X, to.X));
		var top = (int)MathF.Floor(MathF.Min(from.Y, to.Y));
		var right = (int)MathF.Ceiling(MathF.Max(from.X, to.X));
		var bottom = (int)MathF.Ceiling(MathF.Max(from.Y, to.Y));
		return new RectInt(left, top, Math.Max(1, right - left), Math.Max(1, bottom - top));
	}

	private void RenderBlink(Batcher batcher)
	{
		if (State != States.BlinkStrike || blinkStrikeResolved)
			return;

		var from = new Vector2(0, -6);
		var to = blinkStrikeTarget != null && Game.Actors.Contains(blinkStrikeTarget)
			? blinkStrikeTarget.Position + blinkStrikeTarget.Hitbox.Rect.Center - Position
			: from + new Vector2(Facing * BlinkStrikeRange, 0);
		batcher.Line(from, to, 1, Color.White * 0.85f);
		batcher.Line(from + new Vector2(0, 1), to + new Vector2(0, 1), 1, 0x5fcde4);
	}
}
