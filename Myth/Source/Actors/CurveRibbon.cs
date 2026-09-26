using System.Numerics;
using System.Runtime.InteropServices;
using Foster.Framework;

namespace Myth;

// Top: stand on it and jump up through it (a jumpthru). Solid: blocks from every side. Hazard: hurts on contact.
public enum RibbonCollision { None, Top, Solid, Hazard }

[GameActor(Icon = "seedling", BoundsX = -4, BoundsY = -4, BoundsW = 8, BoundsH = 8,
	Tags = ActorTag.Decoration, ShowFacing = false, LivePositionEditable = true)]
public class CurveRibbon : PointPathActor, IActivatable
{
	public static ContentRef<RibbonStyleContent> DefaultStyle => new("root");
	public ContentRef<RibbonStyleContent> Style = DefaultStyle;
	// Foreground draws over the foreground tiles and with them, so a ribbon the player can stand on reads as part of the play space.
	public Factory.TileLayer Layer = Factory.TileLayer.Background;
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
	// Roundness is how strongly the generated shading applies; Bulge is how steeply the cross-section curves
	// away toward the edges (1: 45° at the edge, higher: closer to a cylinder's sideways-facing edges).
	[I(0, 1)] public float Roundness = 0.8f;
	[I(0, 8)] public float Bulge = 1;
	[I(1, 9999)] public int Seed = 1;
	// A flat strip turning about its spine: one full turn every TwistPeriod px (0: no twist).
	[InspectorGroup("Twist"), I(0, 512)] public float TwistPeriod;
	[I(-4, 4)] public float TwistSpeed;
	// Grow shows the ribbon from its base out to that fraction of its length. Activation grows it fully;
	// GrowTo drives it anywhere. Both limit the growth front to GrowSpeed px/second.
	[InspectorGroup("Grow"), I(0, 1)] public float Grow = 1;
	[I(1, 512)] public float GrowSpeed = 96;
	// Built from the unanimated shape (no wobble motion or twist), so a swaying ribbon is a steady floor.
	[InspectorGroup("Collision")] public RibbonCollision Collision;

	readonly record struct NormalTriangle(Vector2 A, Vector2 B, Vector2 C, Color AColor, Color BColor, Color CColor);
	// Sign: +1 on the left edge, -1 on the right, 0 on the spine.
	readonly record struct StampInstance(RibbonStamp Stamp, RibbonEdge Edge, float Sign);
	readonly record struct BuildKey(Point2 Position, RibbonStyle? Style, bool FollowCurveTexture, float Width, float EndWidth,
		float BranchSpacing, float BranchLength, float BranchWidth, float WobbleFrequency, float WobbleIntensity, float WobblePhase,
		float Roundness, float Bulge, int Seed, float Grown, float TwistPeriod, float TwistPhase, RibbonCollision Collision);
	readonly record struct CollisionKey(RibbonCollision Collision, float Width, float EndWidth, bool Taper,
		float WobbleFrequency, float WobbleIntensity, int Seed, float Front);

	readonly List<SplineSample> spline = [];
	readonly List<Vector2> spine = [];
	readonly List<Vector2> grownSpine = [];
	readonly List<Vector2> collisionSpine = [];
	readonly List<Vector2> collisionTrimmed = [];
	readonly List<RibbonEdge> collisionEdges = [];
	readonly PixelMask collisionMask = new(new RectInt());
	readonly CapsuleChain collisionCapsules = new();
	CollisionKey builtCollisionKey;
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
	float growVelocity;
	float growthLength; // distance the growth front travels: the path plus time for the last branches to sprout
	float grownDistance;
	int growParticles = -1;
	string growParticlesName = "";
	SoundInstance? growSound;

	public CurveRibbon()
	{
		Sprite = null;
		CollidesWithSolids = false;
	}

	public override void Added()
	{
		base.Added();
		Depth = Layer == Factory.TileLayer.Background ? Depths.BackgroundProp : Depths.ForegroundProp;
	}

	public override bool CastsSpriteShadow => false;
	public override bool CastsReflection => false;
	RibbonStyle? ActiveStyle => Assets.GetRibbonStyle(Style);
	public override string? SurfaceSubstance => ActiveStyle?.Substance is { Length: > 0 } substance ? substance : null;
	float Grown => grown < 0 ? Grow : grown;
	static float GrowEaseTime => 0.15f;

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
		if (grown < 0)
			grown = target;
		else if (Time.Delta > 0)
		{
			grown = Calc.SmoothDamp(grown, target, ref growVelocity, GrowEaseTime,
				GrowSpeed / MathF.Max(1, growthLength), Time.Delta);
			if (MathF.Abs(grown - target) * growthLength < 0.01f && MathF.Abs(growVelocity) * growthLength < 0.01f)
			{
				grown = target;
				growVelocity = 0;
			}
		}
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

	public override void RenderEmissive(Batcher batcher) => DrawWalkableEdge(batcher);

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
			WobbleFrequency, WobbleIntensity, phase, Roundness, Bulge, Seed, Grown, TwistPeriod, TwistPeriod > 0 ? twistPhase : 0, Collision);
		var spineChanged = SpineChanged;
		if (key == builtKey && !spineChanged)
			return;

		builtKey = key;
		if (spineChanged)
			CaptureSpine();
		spine.Clear();
		spineEdges.Clear();
		spineTwist.Clear();
		triangles.Clear();
		normalTriangles.Clear();
		stamps.Clear();

		PrepareSpine();
		AppendSpine(spine, phase);
		if (spine.Count < 2)
		{
			visualBounds = new Rect(Position, Vector2.Zero);
			return;
		}

		var branches = style?.Branches != false && BranchSpacing > 0;
		var pathLength = PathLength(spine);
		growthLength = pathLength + (branches ? BranchLength : 0);
		var front = Grown * growthLength;
		var taper = style?.Taper != false;
		var collisionKey = new CollisionKey(Collision, Width, EndWidth, taper, WobbleFrequency, WobbleIntensity, Seed, front);
		if (spineChanged || collisionKey != builtCollisionKey)
		{
			builtCollisionKey = collisionKey;
			BuildCollision(front, taper);
		}

		Ribbon.AppendEdges(spineEdges, Trimmed(spine, front, grownSpine), Width, taper ? Width * EndWidth : Width);
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

	// --- spine source ---------------------------------------------------------------------
	// Everything above this point works on `spine`, a list of local-space points, and cares only
	// that it exists. Authored Points are one way to produce it; a rope simulation, an IK chain
	// reaching for a target, or a motion trail are others -- override these three and the whole
	// ribbon (width profile, twist, grow, branches, stamps, collision, surface) comes along.
	//
	// It is two steps so the animated shape and the steady collision shape can share the
	// expensive part: PrepareSpine runs once per geometry build, then AppendSpine can be asked
	// for the shape at any phase.
	protected virtual void PrepareSpine()
	{
		var wobble = WobbleFrequency > 0 && WobbleIntensity > 0;
		spline.Clear();
		Ribbon.AppendSpline(spline, Points, wobble ? 4 : 12, wobble ? 256 : 128);
	}

	// phase advances time-based sway; BuildCollision asks for 0 so a swaying ribbon is still a
	// steady floor. A source that simulates has no such steady shape and can ignore it.
	protected virtual void AppendSpine(List<Vector2> into, float phase)
	{
		var wobble = WobbleFrequency > 0 && WobbleIntensity > 0;
		var traveled = 0f;
		for (var i = 0; i < spline.Count; i++)
		{
			var (point, tangent, t) = spline[i];
			if (i > 0)
				traveled += Vector2.Distance(spline[i - 1].Position, point);
			if (wobble && t is > 0 and < 1 && tangent.LengthSquared() > 0.0001f)
				point += Vector2.Normalize(tangent.TurnRight()) *
					(SmoothNoise.Signed(traveled * WobbleFrequency / 16f + phase, (uint)Seed) * WobbleIntensity * MathF.Sin(MathF.PI * t));
			into.Add(point);
		}
	}

	// Whether the source would build a different spine than the one already built. Authored points
	// change only when edited; a simulated source changes every frame (and so rebuilds collision
	// every frame, the same cost an animating Grow already pays).
	protected virtual bool SpineChanged => !builtPoints.AsSpan().SequenceEqual(Points);
	protected virtual void CaptureSpine() => builtPoints = [.. Points];

	static float PathLength(List<Vector2> points)
	{
		var length = 0f;
		for (var i = 1; i < points.Count; i++)
			length += Vector2.Distance(points[i - 1], points[i]);
		return length;
	}

	// points cut off at length along them (into scratch), or all of them when they're no longer.
	static ReadOnlySpan<Vector2> Trimmed(List<Vector2> points, float length, List<Vector2> scratch)
	{
		var traveled = 0f;
		for (var i = 1; i < points.Count; i++)
		{
			var step = Vector2.Distance(points[i - 1], points[i]);
			if (traveled + step > length)
			{
				scratch.Clear();
				scratch.AddRange(CollectionsMarshal.AsSpan(points)[..i]);
				scratch.Add(Vector2.Lerp(points[i - 1], points[i], (length - traveled) / step));
				return CollectionsMarshal.AsSpan(scratch);
			}
			traveled += step;
		}
		return CollectionsMarshal.AsSpan(points);
	}

	void BuildCollision(float front, bool taper)
	{
		Mask = Collision switch
		{
			RibbonCollision.Top => Masks.Jumpthru,
			RibbonCollision.Solid => Masks.Solid,
			RibbonCollision.Hazard => Masks.Hazard,
			_ => Masks.None,
		};
		Hitbox = new();
		if (Collision == RibbonCollision.None)
			return;

		collisionSpine.Clear();
		collisionEdges.Clear();
		AppendSpine(collisionSpine, 0);
		Ribbon.AppendEdges(collisionEdges, Trimmed(collisionSpine, front, collisionTrimmed), Width, taper ? Width * EndWidth : Width);
		if (collisionEdges.Count < 2)
			return;

		if (UseCapsuleCollision)
		{
			collisionCapsules.Clear();
			foreach (var edge in collisionEdges)
				collisionCapsules.Add(edge.Center, edge.HalfWidth);
			Hitbox = new(collisionCapsules);
			return;
		}

		var min = new Vector2(float.PositiveInfinity);
		var max = new Vector2(float.NegativeInfinity);
		foreach (var edge in collisionEdges)
		{
			min = Vector2.Min(min, Vector2.Min(edge.Left, edge.Right));
			max = Vector2.Max(max, Vector2.Max(edge.Left, edge.Right));
		}
		// Reused rather than rebuilt: an animating Grow rebuilds this every frame.
		collisionMask.Reset(RectInt.Between(min.FloorToPoint2(), max.CeilingToPoint2()));
		for (var i = 1; i < collisionEdges.Count; i++)
		{
			var (previous, current) = (collisionEdges[i - 1], collisionEdges[i]);
			collisionMask.FillTriangle(previous.Left, previous.Right, current.Left);
			collisionMask.FillTriangle(current.Left, previous.Right, current.Right);
		}
		Hitbox = new(collisionMask);
	}

	// A shape that reshapes every frame pays, through a pixel mask, for the whole area it spans;
	// a capsule chain costs the length of its line instead -- around 30x less for a body that
	// crosses a room. A static spine keeps the mask, which is built once and then answers from an
	// array, so it is the cheaper of the two to ask.
	protected virtual bool UseCapsuleCollision => false;

	// The collision cross-section nearest this point, oriented upward. Growth moves the surface along
	// this normal, so a rider needs it to know how much vertical travel a given swell really means.
	public override Vector2 SurfaceNormalAt(Point2 worldPosition)
	{
		EnsureGeometry();
		var local = (Vector2)(worldPosition - Position);
		var nearest = float.PositiveInfinity;
		var normal = -Vector2.UnitY;
		foreach (var edge in collisionEdges)
		{
			var distance = Vector2.DistanceSquared(edge.Center, local);
			if (distance >= nearest)
				continue;
			nearest = distance;
			normal = edge.Side.Y > 0 ? -edge.Side : edge.Side;
		}
		return normal;
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
			if (Roundness <= 0)
				continue;
			for (var column = 1; column < NormalColumns.Length; column++)
			{
				var (u0, u1) = (NormalColumns[column - 1], NormalColumns[column]);
				AddNormalTriangle(Across(previous, u0), Across(previous, u1), Across(current, u0),
					ColumnNormal(previous, twist, i - 1, u0), ColumnNormal(previous, twist, i - 1, u1), ColumnNormal(current, twist, i, u0));
				AddNormalTriangle(Across(current, u0), Across(previous, u1), Across(current, u1),
					ColumnNormal(current, twist, i, u0), ColumnNormal(previous, twist, i - 1, u1), ColumnNormal(current, twist, i, u1));
			}
		}
	}

	// Positions across the strip (-1 right edge, +1 left edge) that carry their own normal, so a high
	// Bulge curves across the face instead of interpolating flat between edge and center.
	static readonly float[] normalColumns = [-1, -0.5f, 0, 0.5f, 1];
	static ReadOnlySpan<float> NormalColumns => normalColumns;

	static Vector2 Across(RibbonEdge edge, float u) => edge.Center + edge.Side * edge.HalfWidth * u;

	static float MaterialHalfWidth(RibbonEdge edge, ReadOnlySpan<float> twist, int index)
		=> twist.IsEmpty ? edge.HalfWidth : edge.HalfWidth / FaceScale(twist[index]) * (MathF.Cos(twist[index]) < 0 ? -1 : 1);

	// A twisted strip is flat across its face, which turns toward the viewer on both sides of the twist.
	Color ColumnNormal(RibbonEdge edge, ReadOnlySpan<float> twist, int index, float u)
	{
		if (twist.IsEmpty)
			return SlopeNormal(edge.Side * u);
		var facing = MathF.Cos(twist[index]) < 0 ? -1 : 1;
		return NormalColor(new Vector3(edge.Side * MathF.Sin(twist[index]) * facing, MathF.Abs(MathF.Cos(twist[index]))));
	}

	void AppendRoundCap(RibbonEdge edge, Vector2 forward)
	{
		const int Segments = 8;
		if (edge.HalfWidth <= 0.5f)
			return;
		var flat = SlopeNormal(Vector2.Zero);
		var previous = edge.Side;
		for (var i = 1; i <= Segments; i++)
		{
			var angle = MathF.PI * i / Segments;
			var current = edge.Side * MathF.Cos(angle) + forward * MathF.Sin(angle);
			AddTriangle(edge.Center, edge.Center + previous * edge.HalfWidth, edge.Center + current * edge.HalfWidth,
				CapUv(edge, Vector2.Zero), CapUv(edge, previous * edge.HalfWidth), CapUv(edge, current * edge.HalfWidth));
			if (Roundness > 0)
				AddNormalTriangle(edge.Center, edge.Center + previous * edge.HalfWidth, edge.Center + current * edge.HalfWidth,
					flat, SlopeNormal(previous), SlopeNormal(current));
			previous = current;
		}
	}

	static Vector2 CapUv(RibbonEdge edge, Vector2 offset)
		=> new(Vector2.Dot(offset, edge.Side), edge.Distance + Vector2.Dot(offset, edge.Tangent));

	// outward runs from 0 at the spine to a unit vector at the edge.
	Color SlopeNormal(Vector2 outward) => NormalColor(Vector3.Normalize(new Vector3(outward * Bulge, 1)));

	// Encodes a unit normal, premultiplied by Roundness so it blends over the normals beneath.
	Color NormalColor(Vector3 normal)
		=> new Color(normal.X * 0.5f + 0.5f, normal.Y * 0.5f + 0.5f, normal.Z * 0.5f + 0.5f, 1) * (Roundness * 0.9f);

	void AddNormalTriangle(Vector2 a, Vector2 b, Vector2 c, Color aColor, Color bColor, Color cColor)
		=> normalTriangles.Add(new(a, b, c, aColor, bColor, cColor));

	void AddTriangle(Vector2 a, Vector2 b, Vector2 c, Vector2 aUv, Vector2 bUv, Vector2 cUv)
	{
		triangles.Add(new(a, FollowCurveTexture ? aUv : Position + a));
		triangles.Add(new(b, FollowCurveTexture ? bUv : Position + b));
		triangles.Add(new(c, FollowCurveTexture ? cUv : Position + c));
	}

	// Lights the top edge where it's flat enough to walk up: brightest when level, fading out toward
	// the 45° a walker can step up.
	void DrawWalkableEdge(Batcher batcher)
	{
		if (Collision is not (RibbonCollision.Top or RibbonCollision.Solid) || ActiveStyle is not { } style)
			return;
		var edges = CollectionsMarshal.AsSpan(spineEdges);
		var color = RigDraw.Multiply(style.WalkableEdgeFill, Tint);
		for (var i = 1; i < edges.Length; i++)
		{
			var walkable = Math.Clamp((MathF.Abs(edges[i].Side.Y) - 0.707f) / 0.2f, 0, 1);
			if (walkable > 0)
				batcher.Line(TopEdge(edges[i - 1]), TopEdge(edges[i]), 1, color * walkable);
		}

		static Vector2 TopEdge(RibbonEdge edge) => edge.Side.Y < 0 ? edge.Left : edge.Right;
	}

	void DrawDetails(Batcher batcher)
	{
		DrawWalkableEdge(batcher);
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
