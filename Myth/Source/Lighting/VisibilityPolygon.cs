using System.Numerics;
using Foster.Framework;

namespace Myth;

public static class VisibilityPolygon
{
	private const float RayAngleEpsilon = 0.0005f;
	private const float IntersectionEpsilon = 0.0001f;

	// Boundary rays evenly tessellate the light's unoccluded rim (the part that reaches full radius
	// without hitting a segment). They set only how *round* the free arc looks — never silhouette
	// accuracy, which the per-endpoint rays own. Each ray costs O(segments), so a flat max is wasteful
	// for the small lights that dominate the scene. The count to still look round scales with on-screen
	// radius via the sagitta R(1 - cos(pi/N)); sqrt(radius) tracks that with a floor that keeps a
	// lone (segment-free) light convincingly circular rather than a polygon. See BoundaryRaysForRadius.
	public const int MaxBoundaryRays = 96;
	private const int MinBoundaryRays = 16;
	private const float BoundaryRaysPerSqrtRadius = 4f;

	public static int BoundaryRaysForRadius(float radius)
		=> Math.Clamp(
			(int)MathF.Round(BoundaryRaysPerSqrtRadius * MathF.Sqrt(MathF.Max(radius, 0f))),
			MinBoundaryRays,
			MaxBoundaryRays);

	public readonly record struct Segment(Vector2 A, Vector2 B)
	{
		public readonly Rect Bounds => Rect.Between(Vector2.Min(A, B), Vector2.Max(A, B));
	}

	public readonly record struct Hit(float Angle, Vector2 Point, int SegmentIndex, bool HitSegment);

	private static readonly List<RayCandidate> candidates = new List<RayCandidate>();

	public static List<Hit> Compute(Vector2 origin, float radius, IReadOnlyList<Segment> segments, List<Hit> hits, int boundaryRayCount = MaxBoundaryRays)
	{
		candidates.Clear();
		candidates.EnsureCapacity(segments.Count * 4 + boundaryRayCount);

		for (var i = 0; i < boundaryRayCount; i++)
			candidates.Add(new(i / (float)boundaryRayCount * MathF.Tau));

		for (var i = 0; i < segments.Count; i++)
		{
			AddCandidates(candidates, origin, segments[i].A);
			AddCandidates(candidates, origin, segments[i].B);
		}

		candidates.Sort(static (a, b) => a.Angle.CompareTo(b.Angle));
		RemoveDuplicateAngles(candidates);

		BuildRayIndex(origin, segments);

		hits.Clear();
		hits.EnsureCapacity(candidates.Count);
		foreach (var candidate in candidates)
			hits.Add(CastRay(origin, radius, candidate.Angle, segments));

		return hits;
	}

	// Angular index over the segments, rebuilt per Compute. Each bucket covers an equal slice of
	// [0, tau) around the origin and lists the segments whose subtended arc touches that slice, so a
	// ray only tests its own bucket instead of every segment. Entries are inserted in ascending
	// order of the segment's closest distance to the origin (mirrored in minDistances, indexed by
	// segment index), letting CastRay stop at the first entry that can't beat its current hit.
	private const int BucketCount = 64;
	private static readonly List<int>[] rayBuckets = CreateRayBuckets();
	private static readonly List<float> minDistances = new List<float>();
	private static readonly List<(float MinDistance, int Index)> segmentsByDistance = new List<(float, int)>();

	private static List<int>[] CreateRayBuckets()
	{
		var buckets = new List<int>[BucketCount];
		for (var i = 0; i < buckets.Length; i++)
			buckets[i] = new List<int>();
		return buckets;
	}

	private static int BucketIndex(float angle)
		=> Math.Clamp((int)(angle * (BucketCount / MathF.Tau)), 0, BucketCount - 1);

	private static void BuildRayIndex(Vector2 origin, IReadOnlyList<Segment> segments)
	{
		foreach (var bucket in rayBuckets)
			bucket.Clear();

		segmentsByDistance.Clear();
		minDistances.Clear();
		for (var i = 0; i < segments.Count; i++)
		{
			var minDistance = MinDistanceToPoint(segments[i], origin);
			segmentsByDistance.Add((minDistance, i));
			minDistances.Add(minDistance);
		}
		segmentsByDistance.Sort(static (a, b) => a.MinDistance.CompareTo(b.MinDistance));

		foreach (var (_, index) in segmentsByDistance)
		{
			var segment = segments[index];
			var angleA = NormalizeAngle(MathF.Atan2(segment.A.Y - origin.Y, segment.A.X - origin.X));
			var angleB = NormalizeAngle(MathF.Atan2(segment.B.Y - origin.Y, segment.B.X - origin.X));

			// A segment subtends the shorter arc between its endpoint angles (always < pi for an
			// origin off the segment).
			var span = angleB - angleA;
			if (span < 0)
				span += MathF.Tau;
			var start = angleA;
			if (span > MathF.PI)
			{
				start = angleB;
				span = MathF.Tau - span;
			}

			// Pad one bucket to each side: candidate rays sit epsilon outside the endpoint angles,
			// which can floor into the neighboring bucket when an endpoint lies on a bucket boundary.
			var first = BucketIndex(start) - 1;
			var last = (int)((start + span) * (BucketCount / MathF.Tau)) + 1;
			if (last - first >= BucketCount)
			{
				first = 0;
				last = BucketCount - 1;
			}
			for (var b = first; b <= last; b++)
				rayBuckets[(b + BucketCount) % BucketCount].Add(index);
		}
	}

	private static float MinDistanceToPoint(Segment segment, Vector2 point)
	{
		var ab = segment.B - segment.A;
		var lengthSquared = ab.LengthSquared();
		var t = lengthSquared <= IntersectionEpsilon
			? 0
			: Math.Clamp(Vector2.Dot(point - segment.A, ab) / lengthSquared, 0, 1);
		return Vector2.Distance(point, segment.A + ab * t);
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

		// The epsilon pair brackets the endpoint's silhouette edge: one ray lands on the segment,
		// the other slips past it to whatever lies behind. A third ray at the exact angle would land
		// within epsilon of the first and add nothing.
		var angle = MathF.Atan2(delta.Y, delta.X);
		candidates.Add(new(NormalizeAngle(angle - RayAngleEpsilon)));
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

		var bucket = rayBuckets[BucketIndex(angle)];
		for (var i = 0; i < bucket.Count; i++)
		{
			var index = bucket[i];
			if (minDistances[index] >= closestDistance)
				break;

			if (TryRaySegmentIntersection(origin, direction, segments[index], out var distance) &&
			    distance < closestDistance)
			{
				closestDistance = distance;
				closestSegment = index;
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
