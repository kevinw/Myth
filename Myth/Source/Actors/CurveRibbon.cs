using System.Numerics;
using System.Runtime.InteropServices;
using Foster.Framework;

namespace Myth;

public enum CurveRibbonMode { Root, Cable }

[GameActor(Icon = "seedling", BoundsX = -4, BoundsY = -4, BoundsW = 8, BoundsH = 8,
	Tags = ActorTag.Decoration, ShowFacing = false, LivePositionEditable = true)]
public class CurveRibbon : PointPathActor
{
	public static ContentRef<SurfaceContent> DefaultSurface => new("curve-root");
	public static ContentRef<SurfaceContent> DefaultCableSurface => new("curve-cable");
	public CurveRibbonMode Mode;
	public ContentRef<SurfaceContent> SurfaceId = DefaultSurface;
	[VisibleWhen(nameof(Mode), CurveRibbonMode.Cable)]
	public ContentRef<SurfaceContent> CableSurfaceId = DefaultCableSurface;
	public bool FollowCurveTexture = true;
	public Color Tint = Color.White;
	[I(1, 64)] public float Width = 12;
	[I(0, 1)] public float EndWidth = 0.15f;
	[I(0, 128)] public float BranchSpacing = 28;
	[I(2, 96)] public float BranchLength = 24;
	[I(0.05f, 1)] public float BranchWidth = 0.35f;
	[VisibleWhen(nameof(Mode), CurveRibbonMode.Cable)]
	[I(6, 64)] public float CollarSpacing = 24;
	[VisibleWhen(nameof(Mode), CurveRibbonMode.Cable)]
	public Color CollarColor = new(0x8faab4);
	// Frequency sets noise cells per 16 world pixels; authored path points remain fixed.
	[I(0, 1)] public float WobbleFrequency;
	[I(0, 24)] public float WobbleIntensity;
	[I(0, 4)] public float WobbleSpeed;
	[I(0, 1)] public float Roundness = 0.8f;
	[I(1, 9999)] public int Seed = 1;

	readonly record struct NormalTriangle(Vector2 A, Vector2 B, Vector2 C, Color AColor, Color BColor, Color CColor);

	readonly List<Vector2> spine = [];
	readonly List<Vector2> branch = [];
	readonly List<Surface.Vertex> triangles = [];
	readonly List<NormalTriangle> normalTriangles = [];
	Point2[] builtPoints = [];
	Point2 builtPosition;
	CurveRibbonMode builtMode;
	bool builtFollowCurveTexture;
	float builtWidth, builtEndWidth, builtBranchSpacing, builtBranchLength, builtBranchWidth, builtWobbleFrequency, builtWobbleIntensity, builtWobblePhase, builtRoundness;
	float wobblePhase;
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
	ContentRef<SurfaceContent> ActiveSurface => Mode == CurveRibbonMode.Cable ? CableSurfaceId : SurfaceId;

	public override void Update()
	{
		base.Update();
		if (WobbleSpeed > 0 && WobbleFrequency > 0 && WobbleIntensity > 0)
			wobblePhase += Time.Delta * WobbleSpeed;
	}

	public static Vector2 Sample(Point2 before, Point2 start, Point2 end, Point2 after, float t)
		=> Calc.Bezier((Vector2)start, (Vector2)start + (end - before) / 6f,
			(Vector2)end - (after - start) / 6f, (Vector2)end, t);

	public override void Render(Batcher batcher)
	{
		EnsureGeometry();
		if (Assets.GetSurface(ActiveSurface) is { } surface)
			surface.Render(batcher, CollectionsMarshal.AsSpan(triangles), Time.SecondsF, tint: Tint);
		else
			for (var i = 0; i + 2 < triangles.Count; i += 3)
				batcher.Triangle(triangles[i].Pos, triangles[i + 1].Pos, triangles[i + 2].Pos, Tint);
		if (Mode == CurveRibbonMode.Cable)
			DrawCableDetails(batcher);
	}

	public override void RenderNormals(Batcher batcher)
	{
		EnsureGeometry();
		if (Assets.GetSurface(ActiveSurface) is { HasNormal: true } surface)
			surface.RenderNormals(batcher, CollectionsMarshal.AsSpan(triangles), Time.SecondsF, FollowCurveTexture);
		foreach (var triangle in normalTriangles)
			batcher.Triangle(triangle.A, triangle.B, triangle.C,
				triangle.AColor, triangle.BColor, triangle.CColor);
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
		var phase = WobbleSpeed > 0 ? wobblePhase : 0;
		var pointsChanged = !builtPoints.AsSpan().SequenceEqual(Points);
		if (builtPosition == Position && builtMode == Mode && builtFollowCurveTexture == FollowCurveTexture &&
			builtWidth == Width && builtEndWidth == EndWidth &&
			builtBranchSpacing == BranchSpacing && builtBranchLength == BranchLength &&
			builtBranchWidth == BranchWidth && builtWobbleFrequency == WobbleFrequency &&
			builtWobbleIntensity == WobbleIntensity && builtWobblePhase == phase && builtRoundness == Roundness && builtSeed == Seed &&
			!pointsChanged)
			return;

		builtPosition = Position;
		builtMode = Mode;
		builtFollowCurveTexture = FollowCurveTexture;
		builtWidth = Width;
		builtEndWidth = EndWidth;
		builtBranchSpacing = BranchSpacing;
		builtBranchLength = BranchLength;
		builtBranchWidth = BranchWidth;
		builtWobbleFrequency = WobbleFrequency;
		builtWobbleIntensity = WobbleIntensity;
		builtWobblePhase = phase;
		builtRoundness = Roundness;
		builtSeed = Seed;
		if (pointsChanged)
		{
			if (builtPoints.Length != Points.Length)
				builtPoints = new Point2[Points.Length];
			Points.CopyTo(builtPoints, 0);
		}
		spine.Clear();
		triangles.Clear();
		normalTriangles.Clear();
		if (Points.Length < 2)
		{
			visualBounds = new Rect(Position, Vector2.Zero);
			return;
		}

		var traveled = 0f;
		var previous = (Vector2)Points[0];
		var wobble = WobbleFrequency > 0 && WobbleIntensity > 0;
		for (var segment = 0; segment < Points.Length - 1; segment++)
		{
			var before = Points[Math.Max(0, segment - 1)];
			var start = Points[segment];
			var end = Points[segment + 1];
			var after = Points[Math.Min(Points.Length - 1, segment + 2)];
			var steps = Math.Clamp((int)MathF.Ceiling((Vector2.Distance(before, start) +
				Vector2.Distance(start, end) + Vector2.Distance(end, after)) / (wobble ? 4 : 12)), 4, wobble ? 256 : 128);
			for (var step = segment == 0 ? 0 : 1; step <= steps; step++)
			{
				var t = step / (float)steps;
				var point = Sample(before, start, end, after, t);
				traveled += Vector2.Distance(previous, point);
				previous = point;
				if (wobble && step > 0 && step < steps)
				{
					var tangent = Sample(before, start, end, after, MathF.Min(1, t + 0.01f)) -
						Sample(before, start, end, after, MathF.Max(0, t - 0.01f));
					if (tangent.LengthSquared() > 0.0001f)
					{
						var side = Vector2.Normalize(new Vector2(-tangent.Y, tangent.X));
						point += side * (WobbleNoise(traveled * WobbleFrequency / 16f + phase) * WobbleIntensity * MathF.Sin(MathF.PI * t));
					}
				}
				spine.Add(point);
			}
		}

		if (Mode == CurveRibbonMode.Root && BranchSpacing > 0)
			AppendBranches();
		AppendRibbon(CollectionsMarshal.AsSpan(spine), MathF.Max(0, Width),
			MathF.Max(0, Width * (Mode == CurveRibbonMode.Cable ? 1 : EndWidth)));

		var min = new Vector2(float.PositiveInfinity);
		var max = new Vector2(float.NegativeInfinity);
		foreach (var vertex in triangles)
		{
			min = Vector2.Min(min, Position + vertex.Pos);
			max = Vector2.Max(max, Position + vertex.Pos);
		}
		visualBounds = triangles.Count > 0 ? Rect.Between(min, max).Inflate(Mode == CurveRibbonMode.Cable ? MathF.Max(2, Width * 0.12f) : 0)
			: new Rect(Position, Vector2.Zero);
	}

	float WobbleNoise(float coordinate)
	{
		var cell = (int)MathF.Floor(coordinate);
		var t = coordinate - cell;
		t = t * t * (3 - 2 * t);
		return float.Lerp(Value(cell), Value(cell + 1), t);

		float Value(int index) => (Hash32.Mix((uint)Seed ^ (uint)index * 0x9e3779b9u) & 0xffff) / 32767.5f - 1f;
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
				AppendRibbon(CollectionsMarshal.AsSpan(branch), Width * BranchWidth, 0.5f, nextBranch);
				nextBranch += spacing * rng.Float(0.75f, 1.25f);
			}
			distance += length;
		}
	}

	void AppendRibbon(ReadOnlySpan<Vector2> points, float startWidth, float endWidth, float textureOffset = 0)
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
			var previousDistance = traveled;
			traveled += Vector2.Distance(points[i - 1], points[i]);
			var current = Edges(points, i, traveled / length, startWidth, endWidth);
			var leftBefore = new Vector2(previous.HalfWidth, previousDistance + textureOffset);
			var rightBefore = new Vector2(-previous.HalfWidth, previousDistance + textureOffset);
			var leftAfter = new Vector2(current.HalfWidth, traveled + textureOffset);
			var rightAfter = new Vector2(-current.HalfWidth, traveled + textureOffset);
			AddTriangle(previous.Left, previous.Right, current.Left, leftBefore, rightBefore, leftAfter);
			AddTriangle(current.Left, previous.Right, current.Right, leftAfter, rightBefore, rightAfter);
			if (Roundness > 0)
			{
				var flat = NormalColor(Vector2.Zero);
				var previousLeft = NormalColor(previous.Side);
				var previousRight = NormalColor(-previous.Side);
				var currentLeft = NormalColor(current.Side);
				var currentRight = NormalColor(-current.Side);
				AddNormalTriangle(previous.Left, points[i - 1], current.Left, previousLeft, flat, currentLeft);
				AddNormalTriangle(current.Left, points[i - 1], points[i], currentLeft, flat, flat);
				AddNormalTriangle(points[i - 1], previous.Right, points[i], flat, previousRight, flat);
				AddNormalTriangle(points[i], previous.Right, current.Right, flat, previousRight, currentRight);
			}
			previous = current;
		}
	}

	static (Vector2 Left, Vector2 Right, Vector2 Side, float HalfWidth) Edges(ReadOnlySpan<Vector2> points, int index, float t, float startWidth, float endWidth)
	{
		var direction = points[Math.Min(index + 1, points.Length - 1)] - points[Math.Max(index - 1, 0)];
		var normal = direction.LengthSquared() > 0.0001f
			? Vector2.Normalize(new Vector2(-direction.Y, direction.X)) : Vector2.UnitY;
		var halfWidth = MathF.Max(0, float.Lerp(startWidth, endWidth, t)) * 0.5f;
		var offset = normal * halfWidth;
		return (points[index] + offset, points[index] - offset, normal, halfWidth);
	}

	Color NormalColor(Vector2 side)
		=> new Color(side.X * Roundness * 0.5f + 0.5f, side.Y * Roundness * 0.5f + 0.5f, 1, 1)
			* (Roundness * 0.9f);

	void AddNormalTriangle(Vector2 a, Vector2 b, Vector2 c, Color aColor, Color bColor, Color cColor)
		=> normalTriangles.Add(new(a, b, c, aColor, bColor, cColor));

	void AddTriangle(Vector2 a, Vector2 b, Vector2 c, Vector2 aUv, Vector2 bUv, Vector2 cUv)
	{
		triangles.Add(new(a, FollowCurveTexture ? aUv : Position + a));
		triangles.Add(new(b, FollowCurveTexture ? bUv : Position + b));
		triangles.Add(new(c, FollowCurveTexture ? cUv : Position + c));
	}

	void DrawCableDetails(Batcher batcher)
	{
		if (spine.Count < 2 || Width <= 0)
			return;
		var points = CollectionsMarshal.AsSpan(spine);
		var trim = RigDraw.Multiply(CollarColor, Tint);
		var traveled = 0f;
		var nextCollar = MathF.Max(6, CollarSpacing) * 0.5f;
		var previous = Edges(points, 0, 0, Width, Width);
		for (var i = 1; i < points.Length; i++)
		{
			var current = Edges(points, i, 1, Width, Width);
			batcher.Line(points[i - 1] + previous.Side * Width * 0.22f,
				points[i] + current.Side * Width * 0.22f, MathF.Max(1, Width * 0.08f), trim * 0.65f);
			var length = Vector2.Distance(points[i - 1], points[i]);
			if (length > 0.001f)
				while (nextCollar <= traveled + length)
				{
					var center = Vector2.Lerp(points[i - 1], points[i], (nextCollar - traveled) / length);
					var side = Vector2.Normalize(new Vector2(-(points[i] - points[i - 1]).Y, (points[i] - points[i - 1]).X));
					var offset = side * Width * 0.52f;
					batcher.Line(center - offset, center + offset, MathF.Max(2, Width * 0.18f), new Color(0x182026));
					batcher.Line(center - offset, center + offset, MathF.Max(1, Width * 0.08f), trim);
					nextCollar += MathF.Max(6, CollarSpacing);
				}
			traveled += length;
			previous = current;
		}
	}
}
