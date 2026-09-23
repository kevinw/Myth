using System.Numerics;
using System.Runtime.InteropServices;
using Foster.Framework;

namespace Myth;

[GameActor(Icon = "seedling", BoundsX = -4, BoundsY = -4, BoundsW = 8, BoundsH = 8,
	Tags = ActorTag.Decoration, ShowFacing = false, LivePositionEditable = true)]
public class CurveRibbon : PointPathActor
{
	public static ContentRef<SurfaceContent> DefaultSurface => new("curve-root");
	public ContentRef<SurfaceContent> SurfaceId = DefaultSurface;
	public Color Tint = Color.White;
	[I(1, 64)] public float Width = 12;
	[I(0, 1)] public float EndWidth = 0.15f;
	[I(0, 128)] public float BranchSpacing = 28;
	[I(2, 96)] public float BranchLength = 24;
	[I(0.05f, 1)] public float BranchWidth = 0.35f;
	[I(1, 9999)] public int Seed = 1;

	readonly List<Vector2> spine = [];
	readonly List<Vector2> branch = [];
	readonly List<Surface.Vertex> triangles = [];
	Point2[] builtPoints = [];
	Point2 builtPosition;
	float builtWidth, builtEndWidth, builtBranchSpacing, builtBranchLength, builtBranchWidth;
	int builtSeed;
	Rect visualBounds;

	public CurveRibbon()
	{
		Sprite = null;
		CollidesWithSolids = false;
		Depth = 9;
	}

	public override bool CastsSpriteShadow => false;
	public override bool CastsReflection => false;

	public static Vector2 Sample(Point2 before, Point2 start, Point2 end, Point2 after, float t)
		=> Calc.Bezier((Vector2)start, (Vector2)start + (end - before) / 6f,
			(Vector2)end - (after - start) / 6f, (Vector2)end, t);

	public override void Render(Batcher batcher)
	{
		EnsureGeometry();
		if (Assets.GetSurface(SurfaceId) is { } surface)
			surface.Render(batcher, CollectionsMarshal.AsSpan(triangles), Time.SecondsF, tint: Tint);
		else
			for (var i = 0; i + 2 < triangles.Count; i += 3)
				batcher.Triangle(triangles[i].Pos, triangles[i + 1].Pos, triangles[i + 2].Pos, Tint);
	}

	public override void RenderNormals(Batcher batcher)
	{
		if (Assets.GetSurface(SurfaceId) is not { HasNormal: true } surface)
			return;
		EnsureGeometry();
		surface.RenderNormals(batcher, CollectionsMarshal.AsSpan(triangles), Time.SecondsF);
	}

	public override void RenderShadow(Batcher batcher, Color color) { }
	public override void RenderReflection(Batcher batcher, Color color) { }

	public override Rect GetVisualBounds()
	{
		EnsureGeometry();
		return visualBounds;
	}

	void EnsureGeometry()
	{
		if (builtPosition == Position && builtWidth == Width && builtEndWidth == EndWidth &&
			builtBranchSpacing == BranchSpacing && builtBranchLength == BranchLength &&
			builtBranchWidth == BranchWidth && builtSeed == Seed && builtPoints.AsSpan().SequenceEqual(Points))
			return;

		builtPosition = Position;
		builtWidth = Width;
		builtEndWidth = EndWidth;
		builtBranchSpacing = BranchSpacing;
		builtBranchLength = BranchLength;
		builtBranchWidth = BranchWidth;
		builtSeed = Seed;
		builtPoints = (Point2[])Points.Clone();
		spine.Clear();
		triangles.Clear();
		if (Points.Length < 2)
		{
			visualBounds = new Rect(Position, Vector2.Zero);
			return;
		}

		for (var segment = 0; segment < Points.Length - 1; segment++)
		{
			var before = Points[Math.Max(0, segment - 1)];
			var start = Points[segment];
			var end = Points[segment + 1];
			var after = Points[Math.Min(Points.Length - 1, segment + 2)];
			var steps = Math.Clamp((int)MathF.Ceiling((Vector2.Distance(before, start) +
				Vector2.Distance(start, end) + Vector2.Distance(end, after)) / 12), 4, 128);
			for (var step = segment == 0 ? 0 : 1; step <= steps; step++)
				spine.Add(Sample(before, start, end, after, step / (float)steps));
		}

		if (BranchSpacing > 0)
			AppendBranches();
		AppendRibbon(CollectionsMarshal.AsSpan(spine), MathF.Max(0, Width), MathF.Max(0, Width * EndWidth));

		var min = new Vector2(float.PositiveInfinity);
		var max = new Vector2(float.NegativeInfinity);
		foreach (var vertex in triangles)
		{
			min = Vector2.Min(min, vertex.World);
			max = Vector2.Max(max, vertex.World);
		}
		visualBounds = triangles.Count > 0 ? Rect.Between(min, max) : new Rect(Position, Vector2.Zero);
	}

	void AppendBranches()
	{
		var rng = new Rng(Seed);
		var spacing = MathF.Max(4, BranchSpacing);
		var nextBranch = spacing * 0.75f;
		var distance = 0f;
		for (var i = 1; i < spine.Count; i++)
		{
			var from = spine[i - 1];
			var to = spine[i];
			var length = Vector2.Distance(from, to);
			if (length <= 0.001f)
				continue;
			while (nextBranch <= distance + length)
			{
				var origin = Vector2.Lerp(from, to, (nextBranch - distance) / length);
				var tangent = (to - from) / length;
				var side = new Vector2(-tangent.Y, tangent.X) * (rng.Chance(0.5f) ? 1 : -1);
				var direction = Vector2.Normalize(tangent * rng.Float(0.15f, 0.5f) + side * rng.Float(0.75f, 1.25f));
				var reach = BranchLength * rng.Float(0.65f, 1.25f);
				var control = origin + direction * reach * 0.55f;
				var tip = origin + direction * reach + tangent * reach * 0.2f;
				branch.Clear();
				for (var step = 0; step <= 6; step++)
					branch.Add(Calc.Bezier(origin, control, tip, step / 6f));
				AppendRibbon(CollectionsMarshal.AsSpan(branch), Width * BranchWidth, 0.5f);
				nextBranch += spacing * rng.Float(0.75f, 1.25f);
			}
			distance += length;
		}
	}

	void AppendRibbon(ReadOnlySpan<Vector2> points, float startWidth, float endWidth)
	{
		if (points.Length < 2)
			return;
		var length = 0f;
		for (var i = 1; i < points.Length; i++)
			length += Vector2.Distance(points[i - 1], points[i]);
		if (length <= 0.001f)
			return;

		var traveled = 0f;
		var previous = Edges(points, 0, 0, startWidth, endWidth);
		for (var i = 1; i < points.Length; i++)
		{
			traveled += Vector2.Distance(points[i - 1], points[i]);
			var current = Edges(points, i, traveled / length, startWidth, endWidth);
			AddTriangle(previous.Left, previous.Right, current.Left);
			AddTriangle(current.Left, previous.Right, current.Right);
			previous = current;
		}
	}

	static (Vector2 Left, Vector2 Right) Edges(ReadOnlySpan<Vector2> points, int index, float t, float startWidth, float endWidth)
	{
		var direction = points[Math.Min(index + 1, points.Length - 1)] - points[Math.Max(index - 1, 0)];
		var normal = direction.LengthSquared() > 0.0001f
			? Vector2.Normalize(new Vector2(-direction.Y, direction.X)) : Vector2.UnitY;
		var offset = normal * MathF.Max(0, float.Lerp(startWidth, endWidth, t)) * 0.5f;
		return (points[index] + offset, points[index] - offset);
	}

	void AddTriangle(Vector2 a, Vector2 b, Vector2 c)
	{
		triangles.Add(new(a, Position + a));
		triangles.Add(new(b, Position + b));
		triangles.Add(new(c, Position + c));
	}
}
