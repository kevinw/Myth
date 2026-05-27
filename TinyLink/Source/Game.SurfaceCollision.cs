using System.Numerics;
using Foster.Framework;

namespace TinyLink;

public readonly record struct SurfaceHit(Vector2 Point, Vector2 Normal, float Distance, Actor? Actor, Room.SlopeTile? Slope, RectInt? Rect);

public partial class Game
{
	public bool OverlapsSolid(Actor actor, in RectInt bounds)
	{
		if (OverlapsRoomCollision(bounds))
			return true;

		foreach (var other in Actors)
		{
			if (other == actor || other is Grid || !other.Mask.Has(Actor.Masks.Solid))
				continue;

			if (other.Hitbox.Overlaps(bounds - other.Position))
				return true;
		}

		return false;
	}

	public bool TryFindSurface(Actor actor, in RectInt bounds, Vector2 direction, float distance, out SurfaceHit hit)
	{
		hit = default;
		if (direction.LengthSquared() <= 0.0001f || distance < 0)
			return false;

		direction = Vector2.Normalize(direction);
		var steps = MathF.Ceiling(distance);
		var previousOffset = Point2.Zero;

		for (var i = 0; i <= steps; i ++)
		{
			var offset = Round(direction * i);
			if (offset == previousOffset && i > 0)
				continue;

			previousOffset = offset;
			var candidate = bounds + offset;
			if (TryGetSurfaceAt(actor, candidate, direction, i, out hit))
				return true;
		}

		return false;
	}

	private bool TryGetSurfaceAt(Actor actor, in RectInt bounds, Vector2 searchDirection, float distance, out SurfaceHit hit)
	{
		if (room != null)
		{
			foreach (var slope in room.SlopeTiles)
			{
				if (!OverlapsSlope(bounds, room, slope))
					continue;

				room.GetSlopeSurface(slope, out var left, out var right);
				var normal = SlopeNormal(left, right, slope.Fill);
				hit = new SurfaceHit(SurfacePoint(bounds, normal), normal, distance, null, slope, null);
				return true;
			}

			foreach (var rect in room.CollisionRects)
			{
				if (!bounds.Overlaps(rect))
					continue;

				var normal = AxisNormal(searchDirection);
				hit = new SurfaceHit(SurfacePoint(bounds, normal), normal, distance, null, null, rect);
				return true;
			}
		}

		foreach (var other in Actors)
		{
			if (other == actor || other is Grid || !other.Mask.Has(Actor.Masks.Solid))
				continue;

			if (!other.Hitbox.Overlaps(bounds - other.Position))
				continue;

			var normal = AxisNormal(searchDirection);
			hit = new SurfaceHit(SurfacePoint(bounds, normal), normal, distance, other, null, other.Hitbox.Rect + other.Position);
			return true;
		}

		hit = default;
		return false;
	}

	private static Vector2 SlopeNormal(Vector2 left, Vector2 right, Room.SlopeFill fill)
	{
		var tangent = Vector2.Normalize(right - left);
		var normal = new Vector2(tangent.Y, -tangent.X);
		return fill == Room.SlopeFill.BelowLine ? normal : -normal;
	}

	private static Vector2 AxisNormal(Vector2 searchDirection)
	{
		if (MathF.Abs(searchDirection.X) > MathF.Abs(searchDirection.Y))
			return new Vector2(-MathF.Sign(searchDirection.X), 0);

		return new Vector2(0, -MathF.Sign(searchDirection.Y));
	}

	private static Vector2 SurfacePoint(RectInt bounds, Vector2 normal)
	{
		var center = (Vector2)bounds.Center;
		return new Vector2(
			normal.X < 0 ? bounds.Left : normal.X > 0 ? bounds.Right : center.X,
			normal.Y < 0 ? bounds.Top : normal.Y > 0 ? bounds.Bottom : center.Y);
	}

	private static Point2 Round(Vector2 value)
		=> new((int)MathF.Round(value.X), (int)MathF.Round(value.Y));
}
