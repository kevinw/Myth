using System.Numerics;
using System.Runtime.InteropServices;
using Foster.Framework;

namespace Myth;

[GameActor(Icon = "seedling", BoundsX = -4, BoundsY = -4, BoundsW = 8, BoundsH = 8,
	Tags = ActorTag.Decoration, ShowFacing = false, LivePositionEditable = true)]
public class CurveRibbon : PointPathActor, IActivatable
{
	public static ContentRef<RibbonStyleContent> DefaultStyle => new("root");
	public ContentRef<RibbonStyleContent> Style = DefaultStyle;
	public ContentRef<SurfaceContent> SurfaceOverride = new("");
	public bool FollowCurveTexture = true;
	public Color Tint = Color.White;
	[I(1, 64)] public float Width = 12;
	[I(0, 1)] public float EndWidth = 0.15f;
	[I(0, 128)] public float BranchSpacing = 28;
	[I(2, 96)] public float BranchLength = 24;
	[I(0.05f, 1)] public float BranchWidth = 0.35f;
	// Frequency sets noise cells per 16 world pixels; authored path points remain fixed.
	[I(0, 1)] public float WobbleFrequency;
	[I(0, 24)] public float WobbleIntensity;
	[I(0, 4)] public float WobbleSpeed;
	[I(0, 1)] public float Roundness = 0.8f;
	[I(1, 9999)] public int Seed = 1;
	// A flat strip turning about its spine: one full turn every TwistPeriod px (0: no twist).
	[InspectorGroup("Twist"), I(0, 512)] public float TwistPeriod;
	[I(-4, 4)] public float TwistSpeed;
	// Grow shows the ribbon from its base out to that fraction of its length. Activation grows it fully;
	// GrowTo drives it anywhere. Both move the growth front at GrowSpeed px/second.
	[InspectorGroup("Grow"), I(0, 1)] public float Grow = 1;
	[I(1, 512)] public float GrowSpeed = 96;

	readonly record struct NormalTriangle(Vector2 A, Vector2 B, Vector2 C, Color AColor, Color BColor, Color CColor);
	// Sign: +1 on the left edge, -1 on the right, 0 on the spine.
	readonly record struct StampInstance(RibbonStamp Stamp, RibbonEdge Edge, float Sign);
	readonly record struct BuildKey(Point2 Position, RibbonStyle? Style, bool FollowCurveTexture, float Width, float EndWidth,
		float BranchSpacing, float BranchLength, float BranchWidth, float WobbleFrequency, float WobbleIntensity, float WobblePhase,
		float Roundness, int Seed, float Grown, float TwistPeriod, float TwistPhase);

	readonly List<SplineSample> spline = [];
	readonly List<Vector2> spine = [];
	readonly List<Vector2> grownSpine = [];
	readonly List<RibbonEdge> spineEdges = [];
	readonly List<float> spineTwist = []; // turn angle per spine edge, empty when untwisted
	readonly List<Vector2> branch = [];
	readonly List<RibbonEdge> branchEdges = [];
	readonly List<Surface.Vertex> triangles = [];
	readonly List<NormalTriangle> normalTriangles = [];
	readonly List<StampInstance> stamps = [];
	Point2[] builtPoints = [];
	BuildKey builtKey;
	float wobblePhase, twistPhase;
	Rect visualBounds;
	readonly HashSet<Actor> activators = [];
	float? growOverride;
	float grown = -1; // snaps to its target on the first update, so a room loads already grown
	float growthLength; // distance the growth front travels: the path plus time for the last branches to sprout
	float grownDistance;
	int growParticles = -1;
	string growParticlesName = "";
	SoundInstance? growSound;

	public CurveRibbon()
	{
		Sprite = null;
		CollidesWithSolids = false;
		Depth = 9;
	}

	public override bool CastsSpriteShadow => false;
	public override bool CastsReflection => false;
	RibbonStyle? ActiveStyle => Assets.GetRibbonStyle(Style);
	float Grown => grown < 0 ? Grow : grown;

	public void SetActive(Actor source, bool active)
	{
		if (active)
			activators.Add(source);
		else
			activators.Remove(source);
	}

	public void GrowTo(float value) => growOverride = Math.Clamp(value, 0, 1);
	ContentRef<SurfaceContent> ActiveSurface => SurfaceOverride.Name != "" ? SurfaceOverride : ActiveStyle?.Surface ?? "";

	public override void Update()
	{
		base.Update();
		if (WobbleSpeed > 0 && WobbleFrequency > 0 && WobbleIntensity > 0)
			wobblePhase += Time.Delta * WobbleSpeed;
		if (TwistPeriod > 0)
			twistPhase += Time.Delta * TwistSpeed;

		var target = activators.Count > 0 ? 1 : growOverride ?? Grow;
		var previous = grown < 0 ? target : grown;
		grown = Calc.Approach(previous, target, Time.Delta * GrowSpeed / MathF.Max(1, growthLength));
		UpdateGrowthFeedback((grown - previous) * growthLength);
	}

	public override void Destroyed()
	{
		StopAndReleaseSound(ref growSound);
		ReleaseParticleEffect(ref growParticles);
	}

	void UpdateGrowthFeedback(float moved)
	{
		EnsureGeometry();
		if (moved == 0 || Time.Delta <= 0 || spineEdges.Count == 0 || ActiveStyle is not { } style)
		{
			StopAndReleaseSound(ref growSound);
			return;
		}
		var front = spineEdges[^1];
		var direction = front.Tangent * MathF.Sign(moved);
		if (style.GrowParticles != growParticlesName)
		{
			ReleaseParticleEffect(ref growParticles);
			growParticlesName = style.GrowParticles;
			if (growParticlesName != "")
				growParticles = AcquireParticleEffect(growParticlesName);
		}
		const float ParticleSpacing = 6;
		var emitted = (int)(grownDistance / ParticleSpacing);
		grownDistance += MathF.Abs(moved);
		if (growParticles >= 0 && (int)(grownDistance / ParticleSpacing) > emitted)
			Game.Particles?.Emit(growParticles, Position + front.Center, (int)(grownDistance / ParticleSpacing) - emitted,
				MathF.Atan2(direction.Y, direction.X), 0.5f, MathF.PI * 0.5f);
		if (growSound == null && style.GrowSound != "")
			growSound = Game.CreateAudioEvent(style.GrowSound, Position + front.Center, start: true);
		Game.SetAudioEventPosition(growSound, Position + front.Center);
		Game.SetAudioEventParameter(growSound, "speed", MathF.Abs(moved) / Time.Delta);
	}

	public override void Render(Batcher batcher)
	{
		EnsureGeometry();
		if (Assets.GetSurface(ActiveSurface) is { } surface)
			surface.Render(batcher, CollectionsMarshal.AsSpan(triangles), Time.SecondsF, tint: Tint);
		else
			for (var i = 0; i + 2 < triangles.Count; i += 3)
				batcher.Triangle(triangles[i].Pos, triangles[i + 1].Pos, triangles[i + 2].Pos, Tint);
		DrawDetails(batcher);
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
		var style = ActiveStyle;
		var phase = WobbleSpeed > 0 ? wobblePhase : 0;
		var key = new BuildKey(Position, style, FollowCurveTexture, Width, EndWidth, BranchSpacing, BranchLength, BranchWidth,
			WobbleFrequency, WobbleIntensity, phase, Roundness, Seed, Grown, TwistPeriod, TwistPeriod > 0 ? twistPhase : 0);
		var pointsChanged = !builtPoints.AsSpan().SequenceEqual(Points);
		if (key == builtKey && !pointsChanged)
			return;

		builtKey = key;
		if (pointsChanged)
			builtPoints = [.. Points];
		spline.Clear();
		spine.Clear();
		grownSpine.Clear();
		spineEdges.Clear();
		spineTwist.Clear();
		triangles.Clear();
		normalTriangles.Clear();
		stamps.Clear();
		if (Points.Length < 2)
		{
			visualBounds = new Rect(Position, Vector2.Zero);
			return;
		}

		var wobble = WobbleFrequency > 0 && WobbleIntensity > 0;
		Ribbon.AppendSpline(spline, Points, wobble ? 4 : 12, wobble ? 256 : 128);
		var traveled = 0f;
		for (var i = 0; i < spline.Count; i++)
		{
			var (point, tangent, t) = spline[i];
			if (i > 0)
				traveled += Vector2.Distance(spline[i - 1].Position, point);
			if (wobble && t is > 0 and < 1 && tangent.LengthSquared() > 0.0001f)
				point += Vector2.Normalize(tangent.TurnRight()) *
					(SmoothNoise.Signed(traveled * WobbleFrequency / 16f + phase, (uint)Seed) * WobbleIntensity * MathF.Sin(MathF.PI * t));
			spine.Add(point);
		}

		var branches = style?.Branches != false && BranchSpacing > 0;
		var pathLength = 0f;
		for (var i = 1; i < spine.Count; i++)
			pathLength += Vector2.Distance(spine[i - 1], spine[i]);
		growthLength = pathLength + (branches ? BranchLength : 0);
		var front = Grown * growthLength;
		var visible = CollectionsMarshal.AsSpan(spine);
		if (front < pathLength)
		{
			TrimSpine(front);
			visible = CollectionsMarshal.AsSpan(grownSpine);
		}

		Ribbon.AppendEdges(spineEdges, visible, Width, style?.Taper != false ? Width * EndWidth : Width);
		if (TwistPeriod > 0)
			for (var i = 0; i < spineEdges.Count; i++)
			{
				var angle = MathF.Tau * (spineEdges[i].Distance / TwistPeriod + twistPhase);
				spineTwist.Add(angle);
				spineEdges[i] = spineEdges[i] with { HalfWidth = spineEdges[i].HalfWidth * FaceScale(angle) };
			}
		if (branches)
			AppendBranches(front);
		AppendStrip(CollectionsMarshal.AsSpan(spineEdges), CollectionsMarshal.AsSpan(spineTwist));
		if (style?.Cap == RibbonCap.Round && spineEdges.Count > 1)
		{
			AppendRoundCap(spineEdges[0], -spineEdges[0].Tangent);
			AppendRoundCap(spineEdges[^1], spineEdges[^1].Tangent);
		}

		var reach = 0f;
		if (style != null)
			foreach (var stamp in style.Stamps)
				reach = MathF.Max(reach, PlaceStamps(stamp));

		var min = new Vector2(float.PositiveInfinity);
		var max = new Vector2(float.NegativeInfinity);
		foreach (var vertex in triangles)
		{
			min = Vector2.Min(min, Position + vertex.Pos);
			max = Vector2.Max(max, Position + vertex.Pos);
		}
		visualBounds = triangles.Count > 0 ? Rect.Between(min, max).Inflate(reach) : new Rect(Position, Vector2.Zero);
	}

	void TrimSpine(float length)
	{
		grownSpine.Add(spine[0]);
		var traveled = 0f;
		for (var i = 1; i < spine.Count; i++)
		{
			var step = Vector2.Distance(spine[i - 1], spine[i]);
			if (traveled + step >= length)
			{
				grownSpine.Add(Vector2.Lerp(spine[i - 1], spine[i], step > 0 ? (length - traveled) / step : 1));
				return;
			}
			traveled += step;
			grownSpine.Add(spine[i]);
		}
	}

	// Branches sprout to full size over BranchLength behind the growth front.
	void AppendBranches(float front)
	{
		var rng = new Rng(Seed);
		var spacing = MathF.Max(4, BranchSpacing);
		var cursor = 0;
		for (var at = spacing * 0.75f; Ribbon.TryEdgeAt(CollectionsMarshal.AsSpan(spineEdges), ref cursor, at, out var edge);
			at += spacing * rng.Float(0.75f, 1.25f))
		{
			var tangent = edge.Tangent;
			var side = edge.Side * (rng.Chance(0.5f) ? 1 : -1);
			var direction = Vector2.Normalize(tangent * rng.Float(0.15f, 0.5f) + side * rng.Float(0.75f, 1.25f));
			var sprout = Math.Clamp((front - at) / BranchLength, 0, 1);
			var reach = BranchLength * rng.Float(0.65f, 1.25f) * sprout;
			if (sprout <= 0)
				continue;
			var control = edge.Center + direction * reach * 0.55f;
			var tip = edge.Center + direction * reach + tangent * reach * 0.2f;
			branch.Clear();
			for (var step = 0; step <= 6; step++)
				branch.Add(Calc.Bezier(edge.Center, control, tip, step / 6f));
			branchEdges.Clear();
			Ribbon.AppendEdges(branchEdges, CollectionsMarshal.AsSpan(branch), Width * BranchWidth * sprout, 0.5f, at);
			AppendStrip(CollectionsMarshal.AsSpan(branchEdges));
		}
	}

	// Returns how far the stamps reach past the spine, for visual bounds.
	float PlaceStamps(RibbonStamp stamp)
	{
		var rng = new Rng(Seed + stamps.Count);
		var cursor = 0;
		var alternate = -1f;
		for (var at = stamp.Spacing * 0.5f; Ribbon.TryEdgeAt(CollectionsMarshal.AsSpan(spineEdges), ref cursor, at, out var edge);
			at += stamp.Spacing * (1 + rng.Float(-stamp.Jitter, stamp.Jitter)))
			stamps.Add(new(stamp, edge, stamp.Side switch
			{
				RibbonSide.Left => 1,
				RibbonSide.Right => -1,
				RibbonSide.Alternate => alternate = -alternate,
				_ => 0,
			}));
		return stamp.Kind == RibbonStampKind.Band
			? Width * stamp.Size * 0.5f + MathF.Max(2, Width * stamp.Outline)
			: Assets.GetSprite(stamp.Sprite) is { Frames.Count: > 0 } sprite
				? MathF.Max(sprite.Frames[0].Subtexture.Width, sprite.Frames[0].Subtexture.Height) * stamp.Size + Width * 0.5f
				: 0;
	}

	// Seen edge-on, a twisted strip keeps a sliver of width rather than vanishing.
	static float FaceScale(float twist) => MathF.Max(0.1f, MathF.Abs(MathF.Cos(twist)));

	// twist (one angle per edge, or empty) narrows the strip to its projected width, squashes the texture
	// rather than cropping it, mirrors it on the back face, and tilts the normals flat across the face.
	void AppendStrip(ReadOnlySpan<RibbonEdge> edges, ReadOnlySpan<float> twist = default)
	{
		for (var i = 1; i < edges.Length; i++)
		{
			var (previous, current) = (edges[i - 1], edges[i]);
			var previousU = MaterialHalfWidth(previous, twist, i - 1);
			var currentU = MaterialHalfWidth(current, twist, i);
			var leftBefore = new Vector2(previousU, previous.Distance);
			var rightBefore = new Vector2(-previousU, previous.Distance);
			var leftAfter = new Vector2(currentU, current.Distance);
			var rightAfter = new Vector2(-currentU, current.Distance);
			AddTriangle(previous.Left, previous.Right, current.Left, leftBefore, rightBefore, leftAfter);
			AddTriangle(current.Left, previous.Right, current.Right, leftAfter, rightBefore, rightAfter);
			if (Roundness > 0)
			{
				var (previousLeft, previousCenter, previousRight) = EdgeNormals(previous, twist, i - 1);
				var (currentLeft, currentCenter, currentRight) = EdgeNormals(current, twist, i);
				AddNormalTriangle(previous.Left, previous.Center, current.Left, previousLeft, previousCenter, currentLeft);
				AddNormalTriangle(current.Left, previous.Center, current.Center, currentLeft, previousCenter, currentCenter);
				AddNormalTriangle(previous.Center, previous.Right, current.Center, previousCenter, previousRight, currentCenter);
				AddNormalTriangle(current.Center, previous.Right, current.Right, currentCenter, previousRight, currentRight);
			}
		}
	}

	static float MaterialHalfWidth(RibbonEdge edge, ReadOnlySpan<float> twist, int index)
		=> twist.IsEmpty ? edge.HalfWidth : edge.HalfWidth / FaceScale(twist[index]) * (MathF.Cos(twist[index]) < 0 ? -1 : 1);

	(Color Left, Color Center, Color Right) EdgeNormals(RibbonEdge edge, ReadOnlySpan<float> twist, int index)
	{
		if (twist.IsEmpty)
			return (NormalColor(edge.Side), NormalColor(Vector2.Zero), NormalColor(-edge.Side));
		var face = NormalColor(edge.Side * MathF.Sin(twist[index]) * (MathF.Cos(twist[index]) < 0 ? -1 : 1));
		return (face, face, face);
	}

	void AppendRoundCap(RibbonEdge edge, Vector2 forward)
	{
		const int Segments = 8;
		if (edge.HalfWidth <= 0.5f)
			return;
		var flat = NormalColor(Vector2.Zero);
		var previous = edge.Side;
		for (var i = 1; i <= Segments; i++)
		{
			var angle = MathF.PI * i / Segments;
			var current = edge.Side * MathF.Cos(angle) + forward * MathF.Sin(angle);
			AddTriangle(edge.Center, edge.Center + previous * edge.HalfWidth, edge.Center + current * edge.HalfWidth,
				CapUv(edge, Vector2.Zero), CapUv(edge, previous * edge.HalfWidth), CapUv(edge, current * edge.HalfWidth));
			if (Roundness > 0)
				AddNormalTriangle(edge.Center, edge.Center + previous * edge.HalfWidth, edge.Center + current * edge.HalfWidth,
					flat, NormalColor(previous), NormalColor(current));
			previous = current;
		}
	}

	static Vector2 CapUv(RibbonEdge edge, Vector2 offset)
		=> new(Vector2.Dot(offset, edge.Side), edge.Distance + Vector2.Dot(offset, edge.Tangent));

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

	void DrawDetails(Batcher batcher)
	{
		if (ActiveStyle is not { } style)
			return;
		var edges = CollectionsMarshal.AsSpan(spineEdges);
		foreach (var stripe in style.Stripes)
		{
			var color = RigDraw.Multiply(stripe.Fill, Tint);
			for (var i = 1; i < edges.Length; i++)
				batcher.Line(edges[i - 1].Center + edges[i - 1].Side * edges[i - 1].HalfWidth * 2 * stripe.Offset,
					edges[i].Center + edges[i].Side * edges[i].HalfWidth * 2 * stripe.Offset,
					MathF.Max(1, edges[i].HalfWidth * 2 * stripe.Thickness), color);
		}

		foreach (var (stamp, edge, sign) in stamps)
		{
			var width = edge.HalfWidth * 2;
			var center = edge.Center + edge.Side * edge.HalfWidth * sign;
			if (stamp.Kind == RibbonStampKind.Band)
			{
				var offset = edge.Side * width * stamp.Size * 0.5f;
				if (stamp.Outline > 0)
					batcher.Line(center - offset, center + offset, MathF.Max(2, width * stamp.Outline), RigDraw.Multiply(stamp.OutlineFill, Tint));
				batcher.Line(center - offset, center + offset, MathF.Max(1, width * stamp.Thickness), RigDraw.Multiply(stamp.Fill, Tint));
			}
			else if (Assets.GetSprite(stamp.Sprite) is { Frames.Count: > 0 } sprite)
			{
				var outward = sign < 0 ? -edge.Side : edge.Side;
				batcher.Image(sprite.Frames[0].Subtexture, center, sprite.Origin, new Vector2(stamp.Size),
					stamp.Align ? MathF.Atan2(outward.Y, outward.X) + MathF.PI / 2 : 0, RigDraw.Multiply(stamp.Fill, Tint));
			}
		}
	}
}
