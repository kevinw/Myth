using System.Numerics;
using Foster.Framework;

namespace Myth;

sealed class AlienBugRigVisual(AlienBug owner) : IDisposable
{
	readonly record struct BoneSpec(string From, string To, float Width);
	readonly record struct TerminalBoneSpec(string Previous, string From, float Width, float LengthScale = 1);
	readonly record struct Bone(int From, int To, float Width);
	readonly record struct TerminalBone(int Previous, int From, int To, float LengthScale);
	readonly record struct BoneUV(Vector2 A, Vector2 B, Vector2 C, Vector2 D);

	static readonly BoneSpec[] BoneSpecs =
	[
		new("Body", "Head", 11),
		new("Body", "LegF_L.000", 5), new("LegF_L.000", "LegF_L.001", 4), new("LegF_L.001", "LegF_L.002", 2.5f),
		new("Body", "LegB_L.000", 5), new("LegB_L.000", "LegB_L.001", 4), new("LegB_L.001", "LegB_L.002", 2.5f),
		new("Body", "LegF_R.000", 5), new("LegF_R.000", "LegF_R.001", 4), new("LegF_R.001", "LegF_R.002", 2.5f),
		new("Body", "LegB_R.000", 5), new("LegB_R.000", "LegB_R.001", 4), new("LegB_R.001", "LegB_R.002", 2.5f),
		new("Body", "Tail.000", 7), new("Tail.000", "Tail.001", 6),
		new("Tail.001", "Tail.002", 4.5f), new("Tail.002", "Tail.003", 2.5f),
	];
	static readonly TerminalBoneSpec[] TerminalBoneSpecs =
	[
		new("LegF_L.001", "LegF_L.002", 2), new("LegB_L.001", "LegB_L.002", 2),
		new("LegF_R.001", "LegF_R.002", 2), new("LegB_R.001", "LegB_R.002", 2),
		new("Tail.002", "Tail.003", 1.5f, 0.8f),
	];

	static Point2 NeuralSize => new(56, 48);
	static Point2 NeuralOrigin => new(-28, -42);
	static float BlendDuration => 0.12f;

	OzzRig rig = null!;
	OzzAnimator animator = null!;
	Vector3[] pose = [];
	Quaternion[] rotations = [];
	Vector2[] projected = [];
	float[] depth = [];
	Bone[] bones = [];
	TerminalBone[] terminalBones = [];
	BoneUV[] boneUVs = [];
	int[] boneOrder = [];
	int body;
	int head;
	Vector2 bodyUV;
	Vector2 headUV;
	Vector2 materialOrigin;
	NeuralTextureActor? neuralSurface;
	string activeWeightsFile = "";
	float accumulator;
	float neuralHitRemaining;
	float nextJostle;

	public OzzRig.Clip ActiveClip => animator.ActiveClip;
	public float Time => animator.Time;

	public void Initialize()
	{
		rig = RigLibrary.Get(owner.Rig);
		animator = new(rig, "Idle");
		pose = new Vector3[rig.JointCount];
		rotations = new Quaternion[rig.JointCount];
		projected = new Vector2[rig.JointCount + TerminalBoneSpecs.Length];
		depth = new float[projected.Length];
		bones = new Bone[BoneSpecs.Length + TerminalBoneSpecs.Length];
		terminalBones = new TerminalBone[TerminalBoneSpecs.Length];
		boneUVs = new BoneUV[bones.Length];
		boneOrder = new int[bones.Length];
		for (var i = 0; i < BoneSpecs.Length; i++)
		{
			var spec = BoneSpecs[i];
			bones[i] = new(RequiredJoint(spec.From), RequiredJoint(spec.To), spec.Width);
			boneOrder[i] = i;
		}
		for (var i = 0; i < TerminalBoneSpecs.Length; i++)
		{
			var spec = TerminalBoneSpecs[i];
			var terminal = terminalBones[i] = new(RequiredJoint(spec.Previous), RequiredJoint(spec.From),
				rig.JointCount + i, spec.LengthScale);
			var bone = BoneSpecs.Length + i;
			bones[bone] = new(terminal.From, terminal.To, spec.Width);
			boneOrder[bone] = bone;
		}
		body = RequiredJoint("Body");
		head = RequiredJoint("Head");
		Sample();
		CaptureReference();
		materialOrigin = owner.Position;
		CreateNeuralSurface();
	}

	int RequiredJoint(string name)
	{
		var result = rig.FindJoint(name);
		return result >= 0 ? result : throw new InvalidDataException($"{owner.Rig} rig is missing joint '{name}'.");
	}

	public void Play(string clip, bool restart = false)
		=> animator.Play(clip, BlendDuration, restart);

	public void Update(bool loop)
	{
		EnsureNeuralSurface();
		var step = 1 / MathF.Max(1, owner.VisualFPS);
		accumulator += owner.Time.Delta;
		var ticks = Math.Min((int)(accumulator / step), 4);
		if (ticks <= 0)
			return;
		accumulator -= ticks * step;
		if (accumulator >= step)
			accumulator %= step;
		for (var i = 0; i < ticks; i++)
		{
			animator.Advance(step * owner.PlaybackSpeed, step, loop);
			Sample();
			neuralHitRemaining = MathF.Max(0, neuralHitRemaining - step);
		}
	}

	void Sample()
	{
		animator.Sample(pose, rotations);
		var angle = owner.ProjectionYaw * MathF.PI / 180f;
		var cos = MathF.Cos(angle);
		var sin = MathF.Sin(angle);
		var mirror = -(int)owner.Facing;
		for (var i = 0; i < pose.Length; i++)
		{
			projected[i] = new Vector2((-pose[i].Z * cos + pose[i].X * sin) * mirror, -pose[i].Y) * owner.PixelsPerMeter;
			depth[i] = pose[i].X * cos + pose[i].Z * sin;
		}
		for (var i = 0; i < terminalBones.Length; i++)
		{
			var terminal = terminalBones[i];
			var length = Vector3.Distance(pose[terminal.Previous], pose[terminal.From]) * terminal.LengthScale;
			var tip = pose[terminal.From] + Vector3.Transform(Vector3.UnitY * length, rotations[terminal.From]);
			projected[terminal.To] = new Vector2((-tip.Z * cos + tip.X * sin) * mirror, -tip.Y) * owner.PixelsPerMeter;
			depth[terminal.To] = tip.X * cos + tip.Z * sin;
		}
		SortBones();
	}

	void SortBones()
	{
		for (var i = 1; i < boneOrder.Length; i++)
		{
			var value = boneOrder[i];
			var valueDepth = BoneDepth(bones[value]);
			var at = i;
			while (at > 0 && BoneDepth(bones[boneOrder[at - 1]]) > valueDepth)
			{
				boneOrder[at] = boneOrder[at - 1];
				at--;
			}
			boneOrder[at] = value;
		}
	}

	float BoneDepth(Bone bone) => (depth[bone.From] + depth[bone.To]) * 0.5f;

	void CaptureReference()
	{
		for (var i = 0; i < bones.Length; i++)
		{
			var bone = bones[i];
			BoneQuad(projected[bone.From], projected[bone.To], bone.Width * owner.Thickness,
				out var a, out var b, out var c, out var d);
			boneUVs[i] = new(NeuralUV(a), NeuralUV(b), NeuralUV(c), NeuralUV(d));
		}
		bodyUV = NeuralUV(projected[body]);
		headUV = NeuralUV(projected[head]);
	}

	void CreateNeuralSurface()
	{
		activeWeightsFile = owner.NeuralWeightsFile;
		neuralSurface = owner.Game.Create<NeuralTextureActor>(owner.Position + NeuralOrigin, it =>
		{
			it.Visible = false;
			it.Size = NeuralSize;
			it.SimulationSize = NeuralSize;
			it.WeightsFile = activeWeightsFile;
			it.NearestStateSampling = true;
			it.EdgeTrim = 1;
		});
		SyncNeuralSurface();
		neuralSurface.SimulateFromSeed(31);
	}

	void EnsureNeuralSurface()
	{
		if (neuralSurface != null && activeWeightsFile != owner.NeuralWeightsFile)
		{
			owner.Game.Destroy(neuralSurface);
			neuralSurface = null;
		}
		if (neuralSurface == null)
			CreateNeuralSurface();
		SyncNeuralSurface();
	}

	void SyncNeuralSurface()
	{
		if (neuralSurface == null)
			return;
		neuralSurface.Position = owner.Position + NeuralOrigin;
		neuralSurface.KeepOnRoomTransition = owner.KeepOnRoomTransition;
		neuralSurface.StepsPerSecond = owner.SkinMode == RigSkinMode.NCA ? owner.NeuralFPS : 0;
		neuralSurface.FireRate = owner.NeuralFireRate;
		neuralSurface.Contrast = owner.NeuralContrast;
		neuralSurface.Saturation = owner.NeuralSaturation;
		neuralSurface.Brightness = owner.NeuralBrightness;
	}

	public void Hit() => neuralHitRemaining = 0.24f;

	public void PerturbNeuralTexture(NeuralTextureActor.PerturbContext context)
	{
		if (owner.SkinMode != RigSkinMode.NCA || context.Texture != neuralSurface)
			return;
		var center = (Vector2)owner.Position + projected[body];
		if (neuralHitRemaining > 0)
			context.AddStamp(center, center, 8, owner.NeuralHitStrength);
		if (owner.NeuralJostleInterval <= 0 || owner.NeuralJostleStrength <= 0 || owner.Time.SecondsF < nextJostle)
			return;
		nextJostle = owner.Time.SecondsF + owner.NeuralJostleInterval;
		var tip = projected[bones[boneOrder[(int)(owner.Time.SecondsF * 13) % boneOrder.Length]].To];
		context.AddStamp((Vector2)owner.Position + tip, (Vector2)owner.Position + tip,
			owner.NeuralJostleRadius, owner.NeuralJostleStrength);
	}

	public void Draw(Batcher batcher, Color tint, bool silhouette)
	{
		var surface = !silhouette && owner.SkinMode == RigSkinMode.Surface ? Assets.GetSurface(owner.SurfaceId) : null;
		var texture = !silhouette && owner.SkinMode == RigSkinMode.NCA ? neuralSurface?.OutputTexture : null;
		if (texture != null)
			batcher.PushSampler(new(TextureFilter.Nearest, TextureWrap.Clamp));
		for (var sorted = 0; sorted < boneOrder.Length; sorted++)
		{
			var index = boneOrder[sorted];
			var bone = bones[index];
			var width = bone.Width * owner.Thickness;
			var amount = Math.Clamp(BoneDepth(bone) * 2 + 0.5f, 0, 1);
			var color = silhouette ? tint : Color.Lerp(owner.FarColor, owner.NearColor, amount);
			batcher.Line(projected[bone.From], projected[bone.To], width + 2, silhouette ? tint : owner.OutlineColor);
			batcher.Line(projected[bone.From], projected[bone.To], width, color);
			if (surface != null)
				DrawSurfaceBone(batcher, surface, projected[bone.From], projected[bone.To], width, boneUVs[index], normals: false);
			else if (texture != null)
				DrawNeuralBone(batcher, texture, projected[bone.From], projected[bone.To], width, boneUVs[index],
					color * owner.NeuralColorAmount);
		}
		DrawBody(batcher, surface, texture, body, owner.BodySize, bodyUV, silhouette ? tint : owner.CoreColor, silhouette);
		DrawBody(batcher, surface, texture, head, owner.HeadSize, headUV, silhouette ? tint : owner.NearColor, silhouette);
		if (texture != null)
			batcher.PopSampler();
		if (!silhouette)
		{
			var look = (owner.Game.GetFirst<Player>()?.Position - owner.Position - projected[head])?.Normalized() ?? Vector2.UnitX;
			batcher.Circle(projected[head] + look * owner.HeadSize * 0.55f, 1.2f, 8, owner.EyeColor);
		}
	}

	void DrawBody(Batcher batcher, Surface? surface, Texture? texture, int joint, float radius, Vector2 uv, Color color, bool silhouette)
	{
		batcher.Circle(projected[joint], radius + 1.5f, 14, silhouette ? color : owner.OutlineColor);
		batcher.Circle(projected[joint], radius, 14, color);
		if (surface != null)
			DrawSurfaceCircle(batcher, surface, projected[joint], radius, uv,
				new Vector2(radius) / (Vector2)NeuralSize, normals: false);
		else if (texture != null)
			DrawNeuralCircle(batcher, texture, projected[joint], radius, uv,
				new Vector2(radius) / (Vector2)NeuralSize, color * owner.NeuralColorAmount);
	}

	public void DrawNormals(Batcher batcher)
	{
		var flat = new Color(0x8080ff);
		var surface = owner.SkinMode == RigSkinMode.Surface && Assets.GetSurface(owner.SurfaceId) is { HasNormal: true } found
			? found : null;
		var texture = owner.SkinMode == RigSkinMode.NCA ? neuralSurface?.OutputNormalTexture : null;
		if (texture != null)
			batcher.PushSampler(new(TextureFilter.Nearest, TextureWrap.Clamp));
		for (var sorted = 0; sorted < boneOrder.Length; sorted++)
		{
			var index = boneOrder[sorted];
			var bone = bones[index];
			var width = bone.Width * owner.Thickness;
			batcher.Line(projected[bone.From], projected[bone.To], width + 2, flat);
			if (surface != null)
				DrawSurfaceBone(batcher, surface, projected[bone.From], projected[bone.To], width, boneUVs[index], normals: true);
			else if (texture != null)
				DrawNeuralBone(batcher, texture, projected[bone.From], projected[bone.To], width, boneUVs[index],
					Color.White * owner.NeuralNormalAmount);
		}
		batcher.Circle(projected[body], owner.BodySize + 1.5f, 14, flat);
		batcher.Circle(projected[head], owner.HeadSize + 1.5f, 14, flat);
		if (surface != null)
		{
			DrawSurfaceCircle(batcher, surface, projected[body], owner.BodySize, bodyUV,
				new Vector2(owner.BodySize) / (Vector2)NeuralSize, normals: true);
			DrawSurfaceCircle(batcher, surface, projected[head], owner.HeadSize, headUV,
				new Vector2(owner.HeadSize) / (Vector2)NeuralSize, normals: true);
		}
		if (texture != null)
			batcher.PopSampler();
	}

	static void BoneQuad(Vector2 from, Vector2 to, float width,
		out Vector2 a, out Vector2 b, out Vector2 c, out Vector2 d)
	{
		var along = (to - from).Normalized(Vector2.UnitY);
		var across = along.TurnLeft() * width * 0.5f;
		a = from + across;
		b = from - across;
		c = to - across;
		d = to + across;
	}

	static void DrawNeuralBone(Batcher batcher, Texture texture, Vector2 from, Vector2 to, float width, BoneUV uv, Color color)
	{
		BoneQuad(from, to, width, out var a, out var b, out var c, out var d);
		batcher.Quad(texture, a, b, c, d, uv.A, uv.B, uv.C, uv.D, color);
	}

	void DrawSurfaceBone(Batcher batcher, Surface surface, Vector2 from, Vector2 to, float width, BoneUV uv, bool normals)
	{
		BoneQuad(from, to, width, out var a, out var b, out var c, out var d);
		Span<Surface.Vertex> triangles = stackalloc Surface.Vertex[]
		{
			new(a, SurfaceWorld(uv.A)), new(b, SurfaceWorld(uv.B)), new(c, SurfaceWorld(uv.C)),
			new(a, SurfaceWorld(uv.A)), new(c, SurfaceWorld(uv.C)), new(d, SurfaceWorld(uv.D)),
		};
		if (normals)
			surface.RenderNormals(batcher, triangles, owner.Time.SecondsF);
		else
			surface.Render(batcher, triangles, owner.Time.SecondsF);
	}

	void DrawSurfaceCircle(Batcher batcher, Surface surface, Vector2 center, float radius,
		Vector2 uvCenter, Vector2 uvRadius, bool normals)
	{
		Span<Surface.Vertex> triangles = stackalloc Surface.Vertex[14 * 3];
		var previous = center + Vector2.UnitX * radius;
		var previousUV = uvCenter + Vector2.UnitX * uvRadius;
		for (var i = 1; i <= 14; i++)
		{
			var direction = Calc.AngleToVector(i / 14f * MathF.Tau);
			var next = center + direction * radius;
			var nextUV = uvCenter + direction * uvRadius;
			var at = (i - 1) * 3;
			triangles[at] = new(center, SurfaceWorld(uvCenter));
			triangles[at + 1] = new(previous, SurfaceWorld(previousUV));
			triangles[at + 2] = new(next, SurfaceWorld(nextUV));
			previous = next;
			previousUV = nextUV;
		}
		if (normals)
			surface.RenderNormals(batcher, triangles, owner.Time.SecondsF);
		else
			surface.Render(batcher, triangles, owner.Time.SecondsF);
	}

	Vector2 SurfaceWorld(Vector2 uv) => materialOrigin + uv * (Vector2)NeuralSize + (Vector2)NeuralOrigin;

	static void DrawNeuralCircle(Batcher batcher, Texture texture, Vector2 center, float radius,
		Vector2 uvCenter, Vector2 uvRadius, Color color)
	{
		var previous = center + Vector2.UnitX * radius;
		var previousUV = uvCenter + Vector2.UnitX * uvRadius;
		for (var i = 1; i <= 14; i++)
		{
			var direction = Calc.AngleToVector(i / 14f * MathF.Tau);
			var next = center + direction * radius;
			var nextUV = uvCenter + direction * uvRadius;
			batcher.Triangle(texture, center, previous, next, uvCenter, previousUV, nextUV, color);
			previous = next;
			previousUV = nextUV;
		}
	}

	static Vector2 NeuralUV(Vector2 position) => (position - (Vector2)NeuralOrigin) / (Vector2)NeuralSize;

	public void Dispose()
	{
		animator?.Dispose();
		if (neuralSurface != null && !owner.Game.destroying.Contains(neuralSurface))
			owner.Game.Destroy(neuralSurface);
		neuralSurface = null;
	}
}
