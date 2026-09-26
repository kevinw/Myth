using System.Numerics;
using Foster.Framework;

namespace Kit;

// A polyline with a radius at each point: the shape swept by a thick line, with round joints.
//
// The cheap collider for something long, thin and moving. A PixelMask has to be refilled across
// its whole bounding box whenever the shape changes, so a creature whose body spans half a room
// pays for the area it spans even though it only occupies a sliver of it; this costs the length
// of the line instead, and needs no buffer at all.
//
// Like PixelMask, it is mutable and a Hitbox built from one caches its Bounds -- rebuild the
// Hitbox after reshaping the chain.
public sealed class CapsuleChain
{
	readonly List<Vector2> points = [];
	readonly List<float> radii = [];
	Vector2 min, max;

	public int Count => points.Count;

	// Integer bounds covering every capsule, for broad-phase rejects.
	public RectInt Bounds { get; private set; }

	public void Clear()
	{
		points.Clear();
		radii.Clear();
		Bounds = default;
	}

	public void Add(Vector2 point, float radius)
	{
		radius = MathF.Max(0, radius);
		var lo = point - new Vector2(radius);
		var hi = point + new Vector2(radius);
		(min, max) = points.Count == 0 ? (lo, hi) : (Vector2.Min(min, lo), Vector2.Max(max, hi));
		points.Add(point);
		radii.Add(radius);
		Bounds = RectInt.Between(min.FloorToPoint2(), max.CeilingToPoint2());
	}

	public bool Overlaps(in RectInt rect)
	{
		if (points.Count == 0 || !Bounds.Overlaps(rect))
			return false;
		if (points.Count == 1)
			return NearRect(points[0], points[0], radii[0], radii[0], rect);

		for (var i = 1; i < points.Count; i++)
			if (NearRect(points[i - 1], points[i], radii[i - 1], radii[i], rect))
				return true;
		return false;
	}

	public bool Contains(Vector2 point)
	{
		if (points.Count == 1)
			return Vector2.DistanceSquared(point, points[0]) <= radii[0] * radii[0];

		for (var i = 1; i < points.Count; i++)
		{
			var distance = DistanceSquaredToSegment(point, points[i - 1], points[i], out var t);
			var radius = float.Lerp(radii[i - 1], radii[i], t);
			if (distance <= radius * radius)
				return true;
		}
		return false;
	}

	// Whether one capsule reaches the rect. A segment crossing the rect overlaps at any radius;
	// otherwise the nearest approach between two convex shapes is at a rect corner or a segment
	// end, so checking those six points finds it -- and the radius is taken where that happens,
	// which keeps a tapered capsule honest along its length.
	static bool NearRect(Vector2 a, Vector2 b, float ra, float rb, in RectInt rect)
	{
		var reach = MathF.Max(ra, rb);
		if (MathF.Min(a.X, b.X) - reach > rect.Right || MathF.Max(a.X, b.X) + reach < rect.Left ||
			MathF.Min(a.Y, b.Y) - reach > rect.Bottom || MathF.Max(a.Y, b.Y) + reach < rect.Top)
			return false;
		if (SegmentHitsRect(a, b, rect))
			return true;

		var best = float.PositiveInfinity;
		var bestT = 0f;
		Consider(Vector2.DistanceSquared(a, ClampToRect(a, rect)), 0);
		Consider(Vector2.DistanceSquared(b, ClampToRect(b, rect)), 1);
		Corner(rect.Left, rect.Top);
		Corner(rect.Right, rect.Top);
		Corner(rect.Left, rect.Bottom);
		Corner(rect.Right, rect.Bottom);

		var radius = float.Lerp(ra, rb, bestT);
		return best <= radius * radius;

		void Corner(float x, float y)
			=> Consider(DistanceSquaredToSegment(new Vector2(x, y), a, b, out var t), t);

		void Consider(float distance, float t)
		{
			if (distance >= best)
				return;
			best = distance;
			bestT = t;
		}
	}

	static bool SegmentHitsRect(Vector2 a, Vector2 b, in RectInt rect)
	{
		float t0 = 0, t1 = 1;
		return Clip(a.X, b.X - a.X, rect.Left, rect.Right, ref t0, ref t1)
			&& Clip(a.Y, b.Y - a.Y, rect.Top, rect.Bottom, ref t0, ref t1);

		static bool Clip(float origin, float delta, float lo, float hi, ref float t0, ref float t1)
		{
			if (MathF.Abs(delta) < 0.000001f)
				return origin >= lo && origin <= hi;
			var near = (lo - origin) / delta;
			var far = (hi - origin) / delta;
			if (near > far)
				(near, far) = (far, near);
			t0 = MathF.Max(t0, near);
			t1 = MathF.Min(t1, far);
			return t0 <= t1;
		}
	}

	static Vector2 ClampToRect(Vector2 p, in RectInt r)
		=> new(Math.Clamp(p.X, r.Left, r.Right), Math.Clamp(p.Y, r.Top, r.Bottom));

	static float DistanceSquaredToSegment(Vector2 p, Vector2 a, Vector2 b, out float t)
	{
		var ab = b - a;
		var length = ab.LengthSquared();
		t = length > 0.000001f ? Math.Clamp(Vector2.Dot(p - a, ab) / length, 0, 1) : 0;
		return Vector2.DistanceSquared(p, a + ab * t);
	}

	public void Render(Batcher batcher, Color color)
	{
		for (var i = 1; i < points.Count; i++)
			batcher.Line(points[i - 1], points[i], MathF.Max(1, radii[i - 1] + radii[i]), color);
	}
}
