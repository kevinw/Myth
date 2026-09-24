using System.Numerics;
using Foster.Framework;

namespace Kit;

public readonly record struct SplineSample(Vector2 Position, Vector2 Tangent, float T);

// A cross-section of a strip: Side is the unit normal toward Left, Distance is arc length from the start.
public readonly record struct RibbonEdge(Vector2 Center, Vector2 Side, float HalfWidth, float Distance)
{
	public Vector2 Left => Center + Side * HalfWidth;
	public Vector2 Right => Center - Side * HalfWidth;
	public Vector2 Tangent => new(Side.Y, -Side.X);
}

public static class Ribbon
{
	// Catmull-Rom through start and end, shaped by their neighbors, as a cubic Bezier.
	public static Vector2 CatmullRom(Vector2 before, Vector2 start, Vector2 end, Vector2 after, float t)
		=> Calc.Bezier(start, start + (end - before) / 6f, end - (after - start) / 6f, end, t);

	static Vector2 CatmullRomTangent(Vector2 before, Vector2 start, Vector2 end, Vector2 after, float t)
	{
		var b = start + (end - before) / 6f;
		var c = end - (after - start) / 6f;
		var u = 1 - t;
		return 3 * u * u * (b - start) + 6 * u * t * (c - b) + 3 * t * t * (end - c);
	}

	// Samples a Catmull-Rom spline through every point, roughly every stepLength pixels. T is the
	// position within the sample's segment, so T is 0 or 1 exactly on authored points.
	public static void AppendSpline(List<SplineSample> output, ReadOnlySpan<Point2> points, float stepLength, int maxSteps)
	{
		for (var segment = 0; segment < points.Length - 1; segment++)
		{
			Vector2 before = points[Math.Max(0, segment - 1)], start = points[segment],
				end = points[segment + 1], after = points[Math.Min(points.Length - 1, segment + 2)];
			var steps = Math.Clamp((int)MathF.Ceiling((Vector2.Distance(before, start) + Vector2.Distance(start, end) +
				Vector2.Distance(end, after)) / stepLength), 4, maxSteps);
			for (var step = segment == 0 ? 0 : 1; step <= steps; step++)
			{
				var t = step / (float)steps;
				output.Add(new(CatmullRom(before, start, end, after, t), CatmullRomTangent(before, start, end, after, t), t));
			}
		}
	}

	// Cross-sections of a strip along points, width lerping from startWidth to endWidth by arc length.
	public static void AppendEdges(List<RibbonEdge> output, ReadOnlySpan<Vector2> points, float startWidth, float endWidth, float distanceOffset = 0)
	{
		var length = 0f;
		for (var i = 1; i < points.Length; i++)
			length += Vector2.Distance(points[i - 1], points[i]);
		if (points.Length < 2 || length <= 0.001f)
			return;

		var traveled = 0f;
		for (var i = 0; i < points.Length; i++)
		{
			if (i > 0)
				traveled += Vector2.Distance(points[i - 1], points[i]);
			var direction = points[Math.Min(i + 1, points.Length - 1)] - points[Math.Max(i - 1, 0)];
			var side = direction.LengthSquared() > 0.0001f ? Vector2.Normalize(direction.TurnRight()) : Vector2.UnitY;
			output.Add(new(points[i], side, MathF.Max(0, float.Lerp(startWidth, endWidth, traveled / length)) * 0.5f, traveled + distanceOffset));
		}
	}

	// The interpolated cross-section at distance. Walk with increasing distances and a shared cursor.
	public static bool TryEdgeAt(ReadOnlySpan<RibbonEdge> edges, ref int cursor, float distance, out RibbonEdge edge)
	{
		cursor = Math.Max(cursor, 1);
		for (; cursor < edges.Length; cursor++)
		{
			var (from, to) = (edges[cursor - 1], edges[cursor]);
			if (distance > to.Distance || to.Distance - from.Distance <= 0.001f)
				continue;
			var t = MathF.Max(0, (distance - from.Distance) / (to.Distance - from.Distance));
			var tangent = to.Center - from.Center;
			edge = new(Vector2.Lerp(from.Center, to.Center, t), Vector2.Normalize(tangent.TurnRight()),
				float.Lerp(from.HalfWidth, to.HalfWidth, t), distance);
			return true;
		}
		edge = default;
		return false;
	}
}
