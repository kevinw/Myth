/*
Lets the player burrow inside solid surfaces by defining a series of line segments the burrow can follow.
Touching path points form junctions; held input chooses an aligned branch, otherwise momentum wins.
*/
using System.Numerics;
using Foster.Framework;

namespace Myth;

[GameActor(Sprite = "Editor/burrow_line", BoundsX = -4, BoundsY = -4, BoundsW = 8, BoundsH = 8, Tags = ActorTag.System,
	Anchor = SpriteAnchor.Center, LivePositionEditable = true)]
public class BurrowLine : PointPathActor
{
	// First and Last identify Points[0] and Points[^1]; burrow doors are bidirectional.
	[Flags]
	public enum BurrowDoorPoints
	{
		None = 0,
		First = 1 << 0,
		Last = 1 << 1,
	}

	public static float JunctionDistance => 1f;
	internal const int MaxJunctionTransfers = 8;

	public BurrowDoorPoints BurrowDoors;
	public bool RenderProcedural;
	[I(0.5f, 12)] public float ProceduralWidth = 3f;
	public Color ProceduralOuterColor = new(0x454a52);
	public Color ProceduralInnerColor = new(0x959ba5);
	float ProceduralDoorRadius => MathF.Max(2f, ProceduralWidth);

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
		if (Points.Length == 0)
			return;
		if ((BurrowDoors & BurrowDoorPoints.First) != 0)
			batcher.Circle(Points[0], ProceduralDoorRadius, 12, new Color(0x17191d));
		if ((BurrowDoors & BurrowDoorPoints.Last) != 0)
			batcher.Circle(Points[^1], ProceduralDoorRadius, 12, new Color(0x17191d));
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
		if (Points.Length == 0)
			return;
		if ((BurrowDoors & BurrowDoorPoints.First) != 0)
			RenderRoundNormals(batcher, Points[0], ProceduralDoorRadius, concave: true);
		if ((BurrowDoors & BurrowDoorPoints.Last) != 0)
			RenderRoundNormals(batcher, Points[^1], ProceduralDoorRadius, concave: true);
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

	static void RenderRoundNormals(Batcher batcher, Vector2 center, float radius, bool concave = false)
	{
		const int steps = 12;
		var flat = new Color(0x8080ff);
		for (var i = 0; i < steps; i++)
		{
			var a = Calc.AngleToVector(i / (float)steps * Calc.TAU);
			var b = Calc.AngleToVector((i + 1) / (float)steps * Calc.TAU);
			batcher.Triangle(center, center + a * radius, center + b * radius,
				flat, NormalColor(concave ? -a : a), NormalColor(concave ? -b : b));
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

	public bool IsDoorPoint(int pointIndex)
		=> pointIndex == 0 && (BurrowDoors & BurrowDoorPoints.First) != 0 ||
			pointIndex == Points.Length - 1 && (BurrowDoors & BurrowDoorPoints.Last) != 0;

	public bool Advance(ref int segment, ref float t, int direction, float distance,
		out Vector2 point, out Vector2 heading, out float remaining, out int junctionPoint)
	{
		var reachedEnd = false;
		junctionPoint = -1;
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
				junctionPoint = direction > 0 ? segment + 1 : segment;
				if (Game.BurrowLinePointConnected(this, junctionPoint))
				{
					reachedEnd = true;
					break;
				}
				if (!NextSegment(ref segment, ref t, direction))
				{
					reachedEnd = true;
					break;
				}
			}
		}

		point = Position + Vector2.Lerp(Points[segment], Points[segment + 1], t);
		heading = SegmentHeading(segment, direction);
		remaining = reachedEnd ? distance : 0;
		return reachedEnd;
	}

	public bool TryFindJunction(int segment, float t, Vector2 steeringIntent, float maxDistance,
		int preferredDirection, out int direction, out float distance)
	{
		var forward = TryFindJunction(segment, t, 1, steeringIntent, maxDistance, out var forwardDistance);
		var backward = TryFindJunction(segment, t, -1, steeringIntent, maxDistance, out var backwardDistance);
		if (!forward && !backward)
		{
			direction = preferredDirection;
			distance = 0;
			return false;
		}

		direction = !backward || forward && (forwardDistance < backwardDistance ||
			forwardDistance == backwardDistance && preferredDirection > 0) ? 1 : -1;
		distance = direction > 0 ? forwardDistance : backwardDistance;
		return true;
	}

	bool TryFindJunction(int segment, float t, int direction, Vector2 steeringIntent,
		float maxDistance, out float distance)
	{
		distance = 0;
		while (segment >= 0 && segment < Points.Length - 1)
		{
			var length = Vector2.Distance(Points[segment], Points[segment + 1]);
			distance += (direction > 0 ? 1 - t : t) * length;
			if (distance > maxDistance)
				return false;

			var pointIndex = direction > 0 ? segment + 1 : segment;
			var travelHeading = SegmentHeading(segment, direction);
			if (Game.BurrowLinePointConnected(this, pointIndex) &&
				Game.TryContinueBurrowLine(this, pointIndex, segment, direction, travelHeading, steeringIntent,
					out _, out _, out _, out _, out _, out var branchHeading) &&
				Game.BurrowLineHeadingFollowsSteering(branchHeading, travelHeading, steeringIntent))
				return true;

			if (!NextSegment(ref segment, ref t, direction))
				return false;
		}

		return false;
	}

	public void Trace(int segment, float t, int travelDirection, Vector2 steeringIntent,
		float distance, List<Vector2> points, List<Vector2> backward)
	{
		points.Clear();
		backward.Clear();
		TraceDirection(segment, t, travelDirection, distance, points, steeringIntent: steeringIntent);
		TraceDirection(segment, t, -travelDirection, distance, backward);
		for (var i = 1; i < backward.Count; i++)
			points.Insert(0, backward[i]);
	}

	internal void TraceDirection(int segment, float t, int direction, float distance, List<Vector2> into,
		bool includeStart = true, Vector2 steeringIntent = default, int junctionsRemaining = MaxJunctionTransfers)
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
			var endpoint = Position + (direction > 0 ? to : from);
			var pointIndex = direction > 0 ? segment + 1 : segment;
			into.Add(endpoint);
			if (junctionsRemaining > 0 && Game.BurrowLinePointConnected(this, pointIndex) &&
				Game.TryContinueBurrowLine(this, pointIndex, segment, direction, SegmentHeading(segment, direction),
					steeringIntent, out var branch, out var branchSegment, out var branchT, out var branchDirection,
					out var branchPoint, out _))
			{
				if (Vector2.DistanceSquared(endpoint, branchPoint) > 0.001f)
					into.Add(branchPoint);
				branch!.TraceDirection(branchSegment, branchT, branchDirection, distance, into, includeStart: false,
					steeringIntent: steeringIntent, junctionsRemaining: junctionsRemaining - 1);
				return;
			}
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

	internal Vector2 SegmentHeading(int segment, int direction)
	{
		var delta = (Vector2)(Points[segment + 1] - Points[segment]);
		return delta.LengthSquared() > 0 ? Vector2.Normalize(delta) * direction : Vector2.UnitX * direction;
	}
}
