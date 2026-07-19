/*
Lets the player burrow inside solid surfaces by defining a series of line segments the burrow can follow.
*/
using System.Numerics;
using Foster.Framework;

namespace Myth;

[GameActor(Sprite = "Editor/burrow_line", BoundsX = -4, BoundsY = -4, BoundsW = 8, BoundsH = 8, Tags = ActorTag.System, Anchor = SpriteAnchor.Center)]
public class BurrowLine : PointPathActor
{
	public bool RenderProcedural;
	public float ProceduralWidth = 3f;
	public Color ProceduralOuterColor = new(0x454a52);
	public Color ProceduralInnerColor = new(0x959ba5);

	public BurrowLine()
	{
		Sprite = null;
		CollidesWithSolids = false;
	}

	public override void Render(Batcher batcher)
	{
		if (!RenderProcedural || ProceduralWidth <= 0)
			return;

		RenderRoundedPath(batcher, ProceduralWidth, ProceduralOuterColor);
		RenderRoundedPath(batcher, ProceduralWidth * 0.45f, ProceduralInnerColor);
	}

	public override void RenderNormals(Batcher batcher)
	{
		if (!RenderProcedural || ProceduralWidth <= 0)
			return;

		var radius = ProceduralWidth * 0.5f;
		for (var i = 1; i < Points.Length; i++)
			RenderSegmentNormals(batcher, Points[i - 1], Points[i], radius);
		for (var i = 0; i < Points.Length; i++)
			RenderRoundNormals(batcher, Points[i], radius);
	}

	void RenderRoundedPath(Batcher batcher, float width, Color color)
	{
		var radius = width * 0.5f;
		for (var i = 1; i < Points.Length; i++)
			if (Points[i - 1] != Points[i])
				batcher.Line(Points[i - 1], Points[i], width, color);
		for (var i = 0; i < Points.Length; i++)
			batcher.Circle(Points[i], radius, 12, color);
	}

	static void RenderSegmentNormals(Batcher batcher, Vector2 from, Vector2 to, float radius)
	{
		var delta = to - from;
		if (delta == Vector2.Zero)
			return;

		var side = new Vector2(-delta.Y, delta.X).Normalized();
		var edgeA = NormalColor(side);
		var edgeB = NormalColor(-side);
		var flat = new Color(0x8080ff);
		var offset = side * radius;
		batcher.Quad(from + offset, from, to, to + offset, edgeA, flat, flat, edgeA);
		batcher.Quad(from, from - offset, to - offset, to, flat, edgeB, edgeB, flat);
	}

	static void RenderRoundNormals(Batcher batcher, Vector2 center, float radius)
	{
		const int steps = 12;
		var flat = new Color(0x8080ff);
		for (var i = 0; i < steps; i++)
		{
			var a = Calc.AngleToVector(i / (float)steps * Calc.TAU);
			var b = Calc.AngleToVector((i + 1) / (float)steps * Calc.TAU);
			batcher.Triangle(center, center + a * radius, center + b * radius,
				flat, NormalColor(a), NormalColor(b));
		}
	}

	static Color NormalColor(Vector2 direction)
		=> new(direction.X * 0.5f + 0.5f, direction.Y * 0.5f + 0.5f, 0.5f, 1f);

	public bool TryGetEndpoint(Vector2 near, float maxDistance, out int segment, out float t, out int direction, out Vector2 point, out Vector2 heading)
	{
		segment = 0;
		t = 0;
		direction = 1;
		point = Position;
		heading = Vector2.UnitX;
		if (Points.Length < 2)
			return false;

		var first = WorldPoint(0);
		var last = WorldPoint(Points.Length - 1);
		var firstDistance = Vector2.DistanceSquared(near, first);
		var lastDistance = Vector2.DistanceSquared(near, last);
		if (MathF.Min(firstDistance, lastDistance) > maxDistance * maxDistance)
			return false;

		if (firstDistance <= lastDistance)
		{
			point = first;
			heading = SegmentHeading(0, 1);
			return true;
		}

		segment = Points.Length - 2;
		t = 1;
		direction = -1;
		point = last;
		heading = SegmentHeading(segment, -1);
		return true;
	}

	public bool NearEndpoint(Vector2 point, float distance)
		=> Points.Length > 0 &&
			(MathF.Min(Vector2.DistanceSquared(point, WorldPoint(0)), Vector2.DistanceSquared(point, WorldPoint(Points.Length - 1))) <= distance * distance);

	public bool Advance(ref int segment, ref float t, int direction, float distance, out Vector2 point, out Vector2 heading)
	{
		var reachedEnd = false;
		while (distance > 0 && segment >= 0 && segment < Points.Length - 1)
		{
			var length = Vector2.Distance(Points[segment], Points[segment + 1]);
			if (length <= 0)
			{
				if (!NextSegment(ref segment, ref t, direction))
				{
					reachedEnd = true;
					break;
				}
				continue;
			}

			var available = (direction > 0 ? 1 - t : t) * length;
			if (distance < available)
			{
				t += direction * distance / length;
				distance = 0;
			}
			else
			{
				t = direction > 0 ? 1 : 0;
				distance -= available;
				if (!NextSegment(ref segment, ref t, direction))
				{
					reachedEnd = true;
					break;
				}
			}
		}

		point = Position + Vector2.Lerp(Points[segment], Points[segment + 1], t);
		heading = SegmentHeading(segment, direction);
		return reachedEnd;
	}

	public void Trace(int segment, float t, float distance, List<Vector2> points, List<Vector2> backward)
	{
		points.Clear();
		backward.Clear();
		TraceDirection(segment, t, 1, distance, points);
		TraceDirection(segment, t, -1, distance, backward);
		for (var i = 1; i < backward.Count; i++)
			points.Insert(0, backward[i]);
	}

	internal void TraceDirection(int segment, float t, int direction, float distance, List<Vector2> into, bool includeStart = true)
	{
		if (includeStart)
			into.Add(Position + Vector2.Lerp(Points[segment], Points[segment + 1], t));
		while (distance > 0)
		{
			var from = (Vector2)Points[segment];
			var to = (Vector2)Points[segment + 1];
			var length = Vector2.Distance(from, to);
			var available = (direction > 0 ? 1 - t : t) * length;
			if (distance < available && length > 0)
			{
				t += direction * distance / length;
				into.Add(Position + Vector2.Lerp(from, to, t));
				return;
			}

			distance -= available;
			into.Add(Position + (direction > 0 ? to : from));
			if (!NextSegment(ref segment, ref t, direction))
				return;
		}
	}

	bool NextSegment(ref int segment, ref float t, int direction)
	{
		segment += direction;
		if (segment < 0 || segment >= Points.Length - 1)
		{
			segment = Math.Clamp(segment, 0, Points.Length - 2);
			t = direction > 0 ? 1 : 0;
			return false;
		}

		t = direction > 0 ? 0 : 1;
		return true;
	}

	Vector2 SegmentHeading(int segment, int direction)
	{
		var delta = (Vector2)(Points[segment + 1] - Points[segment]);
		return delta.LengthSquared() > 0 ? Vector2.Normalize(delta) * direction : Vector2.UnitX * direction;
	}
}
