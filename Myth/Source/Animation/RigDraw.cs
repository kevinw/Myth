using System.Numerics;
using Foster.Framework;

namespace Myth;

enum RigDrawKind { Bone, Hand, Head, Body, Wire, Accessory, Costume }
[Flags] enum JointRings { None = 0, From = 1, To = 2 }
readonly record struct BoneUV(Vector2 A, Vector2 B, Vector2 C, Vector2 D);

interface IRigBone
{
	int Parent { get; }
}

// One depth-ordered piece of a rig. Bones carry resolved geometry and untinted color;
// Index is the bone, joint, wire, or accessory item; Rings marks which bone ends draw an outline ring.
readonly record struct RigDrawItem(RigDrawKind Kind, int Index, bool BehindPlayer = false,
	Vector2 From = default, Vector2 To = default, float FromWidth = 0, float ToWidth = 0,
	Color Color = default, JointRings Rings = JointRings.None, bool JoinsParent = false, CostumePiece? Piece = null);

// The texture a pass paints over rig fills: a surface, an NCA texture, or nothing. Skin UVs address the rig's
// Size-by-Origin reference box; surfaces sample world space anchored at MaterialOrigin.
readonly record struct RigSkin(Surface? Surface, Texture? Texture, float Amount, bool Normals,
	Point2 Size, Point2 Origin, Vector2 MaterialOrigin, float Time, Color? Tint = null)
{
	public void Push(Batcher batcher)
	{
		if (Texture != null)
			batcher.PushSampler(new(TextureFilter.Nearest, TextureWrap.Clamp));
	}

	public void Pop(Batcher batcher)
	{
		if (Texture != null)
			batcher.PopSampler();
	}

	public void Bone(Batcher batcher, Vector2 from, Vector2 to, float fromWidth, float toWidth, BoneUV uv, Color color)
	{
		RigDraw.BoneQuad(from, to, fromWidth, toWidth, out var a, out var b, out var c, out var d);
		if (Surface != null)
			Render(batcher, stackalloc Surface.Vertex[]
			{
				new(a, World(uv.A)), new(b, World(uv.B)), new(c, World(uv.C)),
				new(a, World(uv.A)), new(c, World(uv.C)), new(d, World(uv.D)),
			});
		else if (Texture != null)
			batcher.Quad(Texture, a, b, c, d, uv.A, uv.B, uv.C, uv.D, color * Amount);
	}

	public void Circle(Batcher batcher, Vector2 center, float radius, Vector2 uvCenter, Vector2 uvRadius, Color color, int steps = 14)
	{
		if (Surface == null && Texture == null)
			return;
		Span<Surface.Vertex> triangles = stackalloc Surface.Vertex[steps * 3];
		var previous = center + Vector2.UnitX * radius;
		var previousUV = uvCenter + Vector2.UnitX * uvRadius;
		for (var i = 1; i <= steps; i++)
		{
			var direction = Calc.AngleToVector(i / (float)steps * MathF.Tau);
			var next = center + direction * radius;
			var nextUV = uvCenter + direction * uvRadius;
			if (Surface != null)
			{
				var at = (i - 1) * 3;
				triangles[at] = new(center, World(uvCenter));
				triangles[at + 1] = new(previous, World(previousUV));
				triangles[at + 2] = new(next, World(nextUV));
			}
			else
				batcher.Triangle(Texture!, center, previous, next, uvCenter, previousUV, nextUV, color * Amount);
			previous = next;
			previousUV = nextUV;
		}
		if (Surface != null)
			Render(batcher, triangles);
	}

	public void Joint(Batcher batcher, Vector2 center, float radius, Vector2 uvCenter, Color color)
		=> Circle(batcher, center, radius, uvCenter, new Vector2(radius) / (Vector2)Size, color, RigDraw.JointSteps(radius));

	public void Triangle(Batcher batcher, Vector2 a, Vector2 b, Vector2 c, Vector2 uvA, Vector2 uvB, Vector2 uvC)
	{
		if (Surface != null)
			Render(batcher, stackalloc Surface.Vertex[] { new(a, World(uvA)), new(b, World(uvB)), new(c, World(uvC)) });
	}

	void Render(Batcher batcher, ReadOnlySpan<Surface.Vertex> triangles)
	{
		if (Normals)
			Surface!.RenderNormals(batcher, triangles, Time);
		else
			Surface!.Render(batcher, triangles, Time, tint: Tint);
	}

	Vector2 World(Vector2 uv) => MaterialOrigin + uv * (Vector2)Size + (Vector2)Origin;
}

static class RigDraw
{
	static float JointRadiusScale => 0.5f;
	static int NormalFacets => 3;

	public static float JointRadius(float width) => MathF.Max(1.2f, width * JointRadiusScale);
	public static int JointSteps(float radius) => Math.Clamp((int)(radius * MathF.Tau / 2f), 4, 14);

	public static void AddCostumeItems(List<RigDrawItem> items, Costume? costume, RigTags tags)
	{
		if (costume == null)
			return;
		var bone = items[^1];
		foreach (var piece in costume.Pieces)
			if (piece.Matches(tags))
				items.Add(bone with { Kind = RigDrawKind.Costume, Piece = piece });
	}

	public static Rect CostumeBounds(in RigDrawItem item)
	{
		var piece = item.Piece!;
		var radius = MathF.Max(item.FromWidth, item.ToWidth) * piece.Width *
			(piece.Kind == CostumePieceKind.Spikes ? 0.5f + MathF.Abs(piece.Length) : 0.5f) + 1;
		return Rect.Between(item.From, item.To).Inflate(radius);
	}

	public static void DrawCostume(Batcher batcher, in RigDrawItem item, BoneUV uv, Color tint, Color outline,
		in RigSkin baseSkin, bool silhouette = false)
	{
		var piece = item.Piece!;
		var flat = new Color(0x8080ff);
		var fill = silhouette ? tint : baseSkin.Normals ? flat : Multiply(piece.Fill, tint);
		outline = silhouette ? tint : baseSkin.Normals ? flat : Multiply(outline, tint);
		var surface = !silhouette && piece.Surface != "" ? Assets.GetSurface(piece.Surface) : null;
		var skin = baseSkin with
		{
			Surface = baseSkin.Normals && surface?.HasNormal != true ? null : surface,
			Texture = null, Tint = fill
		};
		var along = (item.To - item.From).Normalized(Vector2.UnitY);
		var across = along.TurnLeft();
		for (var i = 0; i < piece.Count; i++)
		{
			var start = float.Lerp(piece.From, piece.To, i / (float)piece.Count);
			var end = float.Lerp(piece.From, piece.To, (i + (piece.Count == 1 ? 1 : 0.6f)) / piece.Count);
			var from = Vector2.Lerp(item.From, item.To, start);
			var to = Vector2.Lerp(item.From, item.To, end);
			var fromWidth = float.Lerp(item.FromWidth, item.ToWidth, start) * piece.Width;
			var toWidth = float.Lerp(item.FromWidth, item.ToWidth, end) * piece.Width;
			var partUV = new BoneUV(Vector2.Lerp(uv.A, uv.D, start), Vector2.Lerp(uv.B, uv.C, start),
				Vector2.Lerp(uv.B, uv.C, end), Vector2.Lerp(uv.A, uv.D, end));
			if (piece.Kind == CostumePieceKind.Sleeve)
			{
				DrawBoneQuad(batcher, from, to, fromWidth + 2, toWidth + 2, outline);
				DrawBoneQuad(batcher, from, to, fromWidth, toWidth, fill);
				skin.Bone(batcher, from, to, fromWidth, toWidth, partUV, fill);
			}
			else if (piece.Kind == CostumePieceKind.Stud)
			{
				var center = (from + to) * 0.5f;
				var radius = (fromWidth + toWidth) * 0.25f;
				batcher.Circle(center, radius + 1, 10, outline);
				batcher.Circle(center, radius, 10, fill);
				skin.Joint(batcher, center, radius, (partUV.A + partUV.B + partUV.C + partUV.D) * 0.25f, fill);
			}
			else
			{
				var side = across * MathF.Sign(piece.Length);
				var a = from + side * fromWidth * 0.5f;
				var b = to + side * toWidth * 0.5f;
				var c = (a + b) * 0.5f + side * MathF.Abs(piece.Length) * (fromWidth + toWidth) * 0.5f;
				batcher.Triangle(a - along, b + along, c + side, outline);
				batcher.Triangle(a, b, c, fill);
				if (skin.Surface != null)
					skin.Triangle(batcher, a, b, c, partUV.A, partUV.D, (partUV.A + partUV.D) * 0.5f +
						side * MathF.Abs(piece.Length) * fromWidth / (Vector2)skin.Size);
			}
		}
	}

	// A bone rings the parent end when its parent draws later, and the child end when no child drew before it.
	public static JointRings Rings<TBone>(ReadOnlySpan<TBone> bones, int index, ReadOnlySpan<bool> drawn)
		where TBone : IRigBone
	{
		var rings = JointRings.None;
		if (bones[index].Parent >= 0 && !drawn[bones[index].Parent])
			rings |= JointRings.From;
		for (var i = 0; i < bones.Length; i++)
			if (bones[i].Parent == index && drawn[i])
				return rings;
		return rings | JointRings.To;
	}

	public static void DrawBone(Batcher batcher, in RigDrawItem item, BoneUV uv, Color color, Color outline,
		in RigSkin skin)
	{
		var fromRadius = JointRadius(item.FromWidth);
		var toRadius = JointRadius(item.ToWidth);
		batcher.Capsule(item.From, item.To, fromRadius + 1, toRadius + 1, JointSteps(toRadius + 1), outline,
			fromCap: item.Rings.Has(JointRings.From), toCap: item.Rings.Has(JointRings.To));
		batcher.Capsule(item.From, item.To, fromRadius, toRadius, JointSteps(toRadius), color,
			fromCap: item.JoinsParent);
		DrawBoneSkin(batcher, item, uv, color, skin);
	}

	public static void DrawBoneSilhouette(Batcher batcher, in RigDrawItem item, Color tint)
	{
		var toRadius = JointRadius(item.ToWidth);
		batcher.Capsule(item.From, item.To, JointRadius(item.FromWidth) + 1, toRadius + 1,
			JointSteps(toRadius + 1), tint, fromCap: false);
	}

	public static void DrawBoneNormals(Batcher batcher, in RigDrawItem item, BoneUV uv, float roundness,
		float irregularity, uint phaseSeed, in RigSkin skin)
	{
		var fromRadius = JointRadius(item.FromWidth);
		var toRadius = JointRadius(item.ToWidth);
		var seed = (uint)item.Index * 0x9e3779b9u;
		if (roundness > 0)
		{
			var facetSeed = Hash32.Mix(phaseSeed ^ seed);
			for (var facet = 0; facet < NormalFacets; facet++)
			{
				var start = facet / (float)NormalFacets;
				var end = (facet + 1) / (float)NormalFacets;
				DrawCylinderNormals(batcher, Vector2.Lerp(item.From, item.To, start), Vector2.Lerp(item.From, item.To, end),
					float.Lerp(item.FromWidth + 2, item.ToWidth + 2, (start + end) * 0.5f),
					roundness, irregularity, facetSeed ^ (uint)facet * 0x85ebca6bu);
			}
			if (item.JoinsParent)
				DrawSphereNormals(batcher, item.From, fromRadius + 1, JointSteps(fromRadius + 1), roundness, irregularity, seed);
			DrawSphereNormals(batcher, item.To, toRadius + 1, JointSteps(toRadius + 1), roundness, irregularity, seed);
		}
		else
		{
			var flat = new Color(0x8080ff);
			batcher.Capsule(item.From, item.To, fromRadius + 1, toRadius + 1, JointSteps(toRadius + 1), flat,
				fromCap: item.JoinsParent);
		}
		DrawBoneSkin(batcher, item, uv, Color.White, skin);
	}

	static void DrawBoneSkin(Batcher batcher, in RigDrawItem item, BoneUV uv, Color color, in RigSkin skin)
	{
		if (skin.Surface == null && skin.Texture == null)
			return;
		skin.Bone(batcher, item.From, item.To, item.FromWidth, item.ToWidth, uv, color);
		if (item.JoinsParent)
			skin.Joint(batcher, item.From, JointRadius(item.FromWidth), (uv.A + uv.B) * 0.5f, color);
		skin.Joint(batcher, item.To, JointRadius(item.ToWidth), (uv.C + uv.D) * 0.5f, color);
	}

	public static Color Multiply(Color color, Color tint) => new(
		(byte)(color.R * tint.R / 255), (byte)(color.G * tint.G / 255),
		(byte)(color.B * tint.B / 255), (byte)(color.A * tint.A / 255));

	public static float HashSigned(uint value)
		=> (Hash32.Mix(value) & 0xffff) / 32767.5f - 1;

	public static void BoneQuad(Vector2 from, Vector2 to, float fromWidth, float toWidth,
		out Vector2 a, out Vector2 b, out Vector2 c, out Vector2 d)
	{
		var along = (to - from).Normalized(Vector2.UnitY);
		var across = along.TurnLeft();
		a = from + across * fromWidth * 0.5f;
		b = from - across * fromWidth * 0.5f;
		c = to - across * toWidth * 0.5f;
		d = to + across * toWidth * 0.5f;
	}

	public static void DrawBoneQuad(Batcher batcher, Vector2 from, Vector2 to, float fromWidth, float toWidth, Color color)
	{
		BoneQuad(from, to, fromWidth, toWidth, out var a, out var b, out var c, out var d);
		batcher.Quad(a, b, c, d, color);
	}

	public static void DrawCylinderNormals(Batcher batcher, Vector2 from, Vector2 to, float width,
		float roundness, float irregularity, uint seed)
	{
		var along = (to - from).Normalized(Vector2.UnitY);
		var across = along.TurnLeft();
		var halfWidth = width * 0.5f;
		var leftFrom = from + across * halfWidth;
		var leftTo = to + across * halfWidth;
		var rightFrom = from - across * halfWidth;
		var rightTo = to - across * halfWidth;
		var startTilt = along * (HashSigned(seed) * irregularity * 0.28f);
		var endTilt = along * (HashSigned(seed ^ 0xc2b2ae35u) * irregularity * 0.28f);
		var centerFrom = NormalColor(startTilt * 0.32f);
		var centerTo = NormalColor(endTilt * 0.32f);
		var leftStart = NormalColor((across + startTilt).Normalized(across) * roundness);
		var leftEnd = NormalColor((across + endTilt).Normalized(across) * roundness);
		var rightStart = NormalColor((-across + startTilt).Normalized(-across) * roundness);
		var rightEnd = NormalColor((-across + endTilt).Normalized(-across) * roundness);
		batcher.Quad(leftFrom, from, to, leftTo, leftStart, centerFrom, centerTo, leftEnd);
		batcher.Quad(from, rightFrom, rightTo, to, centerFrom, rightStart, rightEnd, centerTo);
	}

	public static void DrawSphereNormals(Batcher batcher, Vector2 center, float radius, int steps,
		float roundness, float irregularity, uint seed)
	{
		var fillMode = new Color(0, 0, 255, 0);
		var centerNormal = NormalColor(new Vector2(HashSigned(seed), HashSigned(seed ^ 0x27d4eb2du)) *
			(irregularity * 0.08f));
		Span<BatcherVertex> verts = stackalloc BatcherVertex[steps + 1];
		Span<int> idxs = stackalloc int[steps * 3];
		verts[0] = new(center, centerNormal, fillMode);
		for (var i = 0; i < steps; i++)
		{
			var a = Calc.AngleToVector(i / (float)steps * MathF.Tau);
			var aAmount = roundness * (1 + HashSigned(seed ^ (uint)i * 0x85ebca6bu) * irregularity * 0.12f);
			verts[i + 1] = new(center + a * radius, NormalColor(a * aAmount), fillMode);
			idxs[i * 3] = 0; idxs[i * 3 + 1] = i + 1; idxs[i * 3 + 2] = (i + 1) % steps + 1;
		}
		batcher.Vertices(verts, idxs);
	}

	// Screen-space lateral normal encoded as color: dir*0.5+0.5 for XY, z=0.5 (faces viewer).
	// Use for flat-facing surfaces (rotors, blade discs, etc.). For curved surfaces where nz matters,
	// use NormalColor() instead — it reconstructs z from the lateral length.
	public static Color ScreenNormalColor(Vector2 dir)
		=> new(dir.X * 0.5f + 0.5f, dir.Y * 0.5f + 0.5f, 0.5f, 1f);

	// Sphere normals fan: center faces viewer, edge vertices encode screen-space outward normals.
	// Pass concave=true to invert the rim normals (e.g. for an inset cup shape).
	public static void DrawSphereNormals(Batcher batcher, Vector2 center, float radius, int steps, bool concave = false)
	{
		var fillMode = new Color(0, 0, 255, 0);
		Span<BatcherVertex> verts = stackalloc BatcherVertex[steps + 1];
		Span<int> idxs = stackalloc int[steps * 3];
		verts[0] = new(center, new Color(0x8080ff), fillMode);
		for (var i = 0; i < steps; i++)
		{
			var a = Calc.AngleToVector(i / (float)steps * MathF.Tau);
			verts[i + 1] = new(center + a * radius, ScreenNormalColor(concave ? -a : a), fillMode);
			idxs[i * 3] = 0; idxs[i * 3 + 1] = i + 1; idxs[i * 3 + 2] = (i + 1) % steps + 1;
		}
		batcher.Vertices(verts, idxs);
	}

	static Color NormalColor(Vector2 lateral)
	{
		var length = lateral.Length();
		if (length > 0.999f)
			lateral *= 0.999f / length;
		var normal = new Vector3(lateral, MathF.Sqrt(MathF.Max(0, 1 - lateral.LengthSquared())));
		return new(normal.X * 0.5f + 0.5f, normal.Y * 0.5f + 0.5f, normal.Z * 0.5f + 0.5f, 1);
	}
}
