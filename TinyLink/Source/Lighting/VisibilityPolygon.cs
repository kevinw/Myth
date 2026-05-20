using System.Numerics;
using Foster.Framework;

namespace TinyLink;

public static class VisibilityPolygon
{
	private const float RayAngleEpsilon = 0.0005f;
	private const float IntersectionEpsilon = 0.0001f;
	private const int BoundaryRayCount = 96;

	public readonly record struct Segment(Vector2 A, Vector2 B)
	{
		public readonly Rect Bounds => Rect.Between(Vector2.Min(A, B), Vector2.Max(A, B));
	}

	public readonly record struct Hit(float Angle, Vector2 Point, int SegmentIndex, bool HitSegment);

	public static List<Hit> Compute(Vector2 origin, float radius, IReadOnlyList<Segment> segments)
	{
		var candidates = new List<RayCandidate>(segments.Count * 6);
		for (var i = 0; i < BoundaryRayCount; i++)
			candidates.Add(new(i / (float)BoundaryRayCount * MathF.Tau));

		for (var i = 0; i < segments.Count; i++)
		{
			AddCandidates(candidates, origin, segments[i].A);
			AddCandidates(candidates, origin, segments[i].B);
		}

		candidates.Sort(static (a, b) => a.Angle.CompareTo(b.Angle));
		RemoveDuplicateAngles(candidates);

		var hits = new List<Hit>(candidates.Count);
		foreach (var candidate in candidates)
			hits.Add(CastRay(origin, radius, candidate.Angle, segments));

		return hits;
	}

	public static bool CircleOverlaps(Segment segment, Vector2 center, float radius)
	{
		var ab = segment.B - segment.A;
		var lengthSquared = ab.LengthSquared();
		var t = lengthSquared <= IntersectionEpsilon
			? 0
			: Math.Clamp(Vector2.Dot(center - segment.A, ab) / lengthSquared, 0, 1);
		var closest = segment.A + ab * t;
		return Vector2.DistanceSquared(center, closest) <= radius * radius;
	}

	private static void AddCandidates(List<RayCandidate> candidates, Vector2 origin, Vector2 point)
	{
		var delta = point - origin;
		if (delta.LengthSquared() <= IntersectionEpsilon)
			return;

		var angle = MathF.Atan2(delta.Y, delta.X);
		candidates.Add(new(NormalizeAngle(angle - RayAngleEpsilon)));
		candidates.Add(new(NormalizeAngle(angle)));
		candidates.Add(new(NormalizeAngle(angle + RayAngleEpsilon)));
	}

	private static void RemoveDuplicateAngles(List<RayCandidate> candidates)
	{
		if (candidates.Count <= 1)
			return;

		var write = 1;
		for (var read = 1; read < candidates.Count; read++)
		{
			if (MathF.Abs(candidates[read].Angle - candidates[write - 1].Angle) <= IntersectionEpsilon)
				continue;
			candidates[write++] = candidates[read];
		}

		if (write > 1 && MathF.Abs(candidates[0].Angle + MathF.Tau - candidates[write - 1].Angle) <= IntersectionEpsilon)
			write--;

		candidates.RemoveRange(write, candidates.Count - write);
	}

	private static Hit CastRay(Vector2 origin, float radius, float angle, IReadOnlyList<Segment> segments)
	{
		var direction = new Vector2(MathF.Cos(angle), MathF.Sin(angle));
		var closestDistance = radius;
		var closestSegment = -1;
		var hitSegment = false;

		for (var i = 0; i < segments.Count; i++)
		{
			if (TryRaySegmentIntersection(origin, direction, segments[i], out var distance) &&
			    distance < closestDistance)
			{
				closestDistance = distance;
				closestSegment = i;
				hitSegment = true;
			}
		}

		return new Hit(NormalizeAngle(angle), origin + direction * closestDistance, closestSegment, hitSegment);
	}

	private static bool TryRaySegmentIntersection(Vector2 rayOrigin, Vector2 rayDirection, Segment segment, out float distance)
	{
		var segmentVector = segment.B - segment.A;
		var denominator = Cross(rayDirection, segmentVector);
		if (MathF.Abs(denominator) <= IntersectionEpsilon)
		{
			distance = 0;
			return false;
		}

		var delta = segment.A - rayOrigin;
		var rayDistance = Cross(delta, segmentVector) / denominator;
		var segmentAmount = Cross(delta, rayDirection) / denominator;
		if (rayDistance < 0 || segmentAmount < 0 || segmentAmount > 1)
		{
			distance = 0;
			return false;
		}

		distance = rayDistance;
		return true;
	}

	private static float Cross(Vector2 a, Vector2 b)
		=> a.X * b.Y - a.Y * b.X;

	private static float NormalizeAngle(float angle)
	{
		var result = angle % MathF.Tau;
		return result < 0 ? result + MathF.Tau : result;
	}

	private readonly record struct RayCandidate(float Angle);
}
