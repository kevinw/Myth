using System.Numerics;
using Foster.Framework;

namespace Myth;

/// Tracks a direction-toward-surface for an actor and provides incremental
/// movement along that surface, including outside-corner and slope handling.
/// Used by burrowing players and (eventually) wall-following enemies.
public sealed class SurfaceFollower
{
	private readonly Actor actor;

	public Vector2 TowardSurface = Vector2.UnitY;
	public float SurfaceProbeDistance = 10;
	public float CornerProbeDistance = 4;
	public float MaxSurfaceSnapDistance = 1;
	public bool ClimbsWallsAndCeilings = true;

	/// Local rect (relative to actor.Position) used for surface probing. Lets an
	/// actor keep a ground-locked follow point separate from a hitbox that has
	/// drifted elsewhere (e.g. a body that rides on its legs). Empty = use Hitbox.
	public RectInt ProbeRect;

	public Vector2 Remainder;

	public SurfaceFollower(Actor actor)
	{
		this.actor = actor;
	}

	/// Direction along the surface (90° CCW from TowardSurface).
	public Vector2 Tangent => new(TowardSurface.Y, -TowardSurface.X);

	/// Initial / re-attach: snap actor toward TowardSurface up to `distance` pixels,
	/// rotating TowardSurface to match whatever surface was found.
	public bool Attach(float distance, float maxSnap = float.PositiveInfinity)
	{
		var bounds = Bounds();
		if (!actor.Game.TryFindSurface(actor, bounds, TowardSurface, distance, out var hit))
			return false;

		var nextSurface = -hit.Normal;
		if (!CanUseSurface(nextSurface))
			return false;

		TowardSurface = nextSurface;
		if (hit.Slope.HasValue)
		{
			actor.Position += RoundCorrection(hit.Correction, maxSnap);
			return true;
		}

		var rawSnap = MathF.Max(0, hit.Distance - 1);
		var snap = MathF.Min(rawSnap, maxSnap);
		if (snap > 0)
			actor.Position += Round(TowardSurface * snap);
		return true;
	}

	/// Move along Tangent * directionSign by `amount` pixels, handling
	/// inside corners (re-attach in same dir) and outside corners (rotate).
	public void Follow(int directionSign, float amount)
	{
		Attach(CornerProbeDistance, MaxSurfaceSnapDistance);
		if (directionSign == 0)
			return;

		var desired = Tangent * (directionSign * amount);
		Remainder += desired;
		var move = (Point2)Remainder;
		Remainder -= (Vector2)move;

		var steps = Math.Max(Math.Abs(move.X), Math.Abs(move.Y));
		for (var i = 0; i < steps; i++)
		{
			var step = new Point2(
				i < Math.Abs(move.X) ? Math.Sign(move.X) : 0,
				i < Math.Abs(move.Y) ? Math.Sign(move.Y) : 0);
			if (!TryMoveStep(step))
				break;
		}
	}

	private bool TryMoveStep(Point2 step)
	{
		if (step == Point2.Zero)
			return true;

		var bounds = Bounds();
		var nextBounds = bounds + step;
		if (actor.Game.OverlapsSolid(actor, nextBounds))
		{
			if (actor.Game.TryFindSurface(actor, bounds, (Vector2)step, 1, out var surfaceHit))
			{
				var nextSurface = -surfaceHit.Normal;
				if (!CanUseSurface(nextSurface))
					return false;
				TowardSurface = nextSurface;
				if (surfaceHit.Slope.HasValue)
					return TryMoveBlockedSlopeStep(step, surfaceHit);
			}
			return false;
		}

		actor.Position += step;
		if (Attach(SurfaceProbeDistance, MaxSurfaceSnapDistance))
			return true;

		var previousTowardSurface = TowardSurface;
		TowardSurface = -NormalizeOrDefault((Vector2)step, TowardSurface);
		if (!CanUseSurface(TowardSurface))
		{
			actor.Position -= step;
			TowardSurface = previousTowardSurface;
			Remainder = Vector2.Zero;
			return false;
		}
		if (TryMoveAroundOutsideCorner(previousTowardSurface))
			return true;

		if (Attach(CornerProbeDistance, MaxSurfaceSnapDistance))
			return true;

		actor.Position -= step;
		TowardSurface = previousTowardSurface;
		Remainder = Vector2.Zero;
		Attach(CornerProbeDistance, MaxSurfaceSnapDistance);
		return false;
	}

	private bool TryMoveBlockedSlopeStep(Point2 step, in SurfaceHit hit)
	{
		if (TryMoveAndReattachToSlope(step))
			return true;

		var tangentStep = Round(Tangent);
		if (tangentStep != Point2.Zero)
		{
			if (TryMoveAndReattachToSlope(tangentStep))
				return true;

			if (actor.Game.TryFindSurface(actor, Bounds(), (Vector2)tangentStep, 1, out var tangentHit) &&
				tangentHit.Slope.HasValue)
			{
				var tangentCorrection = RoundCorrection(tangentHit.Correction, MaxSurfaceSnapDistance);
				if (tangentCorrection != Point2.Zero)
				{
					actor.Position += tangentCorrection;
					return true;
				}
			}
		}

		var correction = RoundCorrection(hit.Correction, MaxSurfaceSnapDistance);
		if (correction != Point2.Zero)
		{
			actor.Position += correction;
			return true;
		}

		return false;
	}

	private bool TryMoveAndReattachToSlope(Point2 step)
	{
		if (step == Point2.Zero)
			return false;

		actor.Position += step;
		if (Attach(SurfaceProbeDistance, MaxSurfaceSnapDistance))
			return true;

		actor.Position -= step;
		return false;
	}

	private bool TryMoveAroundOutsideCorner(Vector2 previousTowardSurface)
	{
		var cornerStep = Round(previousTowardSurface);
		if (cornerStep == Point2.Zero)
			return false;

		var bounds = Bounds();
		if (actor.Game.OverlapsSolid(actor, bounds + cornerStep))
			return false;

		actor.Position += cornerStep;
		if (Attach(CornerProbeDistance, MaxSurfaceSnapDistance))
			return true;

		actor.Position -= cornerStep;
		return false;
	}

	private RectInt Bounds() => (ProbeRect.Width == 0 ? actor.Hitbox.Rect : ProbeRect) + actor.Position;

	bool CanUseSurface(Vector2 direction) => ClimbsWallsAndCeilings || direction.Y > 0.5f;

	private static Vector2 NormalizeOrDefault(Vector2 value, Vector2 fallback)
		=> value.LengthSquared() <= 0.0001f ? fallback : Vector2.Normalize(value);

	private static Point2 RoundCorrection(Vector2 correction, float maxSnap)
	{
		var move = Round(correction);
		if (float.IsPositiveInfinity(maxSnap))
			return move;

		var max = (int)MathF.Ceiling(maxSnap);
		return new Point2(
			Math.Clamp(move.X, -max, max),
			Math.Clamp(move.Y, -max, max));
	}

	private static Point2 Round(Vector2 value)
		=> new((int)MathF.Round(value.X), (int)MathF.Round(value.Y));
}
