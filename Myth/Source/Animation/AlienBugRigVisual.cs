using System.Numerics;
using Foster.Framework;
using static Myth.RigDraw;

namespace Myth;

sealed class AlienBugRigVisual(AlienBug owner) : IDisposable
{
	readonly record struct BoneSpec(string From, string To, float Width, RigTags Tags);
	readonly record struct TerminalBoneSpec(string Previous, string From, float Width, RigTags Tags, float LengthScale = 1);
	readonly record struct Bone(int From, int To, float Width, RigTags Tags, int Parent = -1) : IRigBone;
	readonly record struct TerminalBone(int Previous, int From, int To, float LengthScale);

	static readonly BoneSpec[] BoneSpecs =
	[
		new("Body", "Head", 11, RigTags.Neck),
		new("Body", "LegF_L.000", 5, RigTags.Hip | RigTags.Left), new("LegF_L.000", "LegF_L.001", 4, RigTags.Thigh | RigTags.Left), new("LegF_L.001", "LegF_L.002", 2.5f, RigTags.Shin | RigTags.Left),
		new("Body", "LegB_L.000", 5, RigTags.Hip | RigTags.Left), new("LegB_L.000", "LegB_L.001", 4, RigTags.Thigh | RigTags.Left), new("LegB_L.001", "LegB_L.002", 2.5f, RigTags.Shin | RigTags.Left),
		new("Body", "LegF_R.000", 5, RigTags.Hip | RigTags.Right), new("LegF_R.000", "LegF_R.001", 4, RigTags.Thigh | RigTags.Right), new("LegF_R.001", "LegF_R.002", 2.5f, RigTags.Shin | RigTags.Right),
		new("Body", "LegB_R.000", 5, RigTags.Hip | RigTags.Right), new("LegB_R.000", "LegB_R.001", 4, RigTags.Thigh | RigTags.Right), new("LegB_R.001", "LegB_R.002", 2.5f, RigTags.Shin | RigTags.Right),
		new("Body", "Tail.000", 7, RigTags.Tail), new("Tail.000", "Tail.001", 6, RigTags.Tail),
		new("Tail.001", "Tail.002", 4.5f, RigTags.Tail), new("Tail.002", "Tail.003", 2.5f, RigTags.Tail),
	];
	static readonly TerminalBoneSpec[] TerminalBoneSpecs =
	[
		new("LegF_L.001", "LegF_L.002", 2, RigTags.Foot | RigTags.Left), new("LegB_L.001", "LegB_L.002", 2, RigTags.Foot | RigTags.Left),
		new("LegF_R.001", "LegF_R.002", 2, RigTags.Foot | RigTags.Right), new("LegB_R.001", "LegB_R.002", 2, RigTags.Foot | RigTags.Right),
		new("Tail.002", "Tail.003", 1.5f, RigTags.Tail, 0.8f),
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
	readonly List<RigDrawItem> drawItems = [];
	int body;
	int head;
	Vector2 bodyUV;
	Vector2 headUV;
	Vector2 materialOrigin;
	NeuralSkinRuntime neuralSkin = null!;
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
			bones[i] = new(RequiredJoint(spec.From), RequiredJoint(spec.To), spec.Width, spec.Tags);
			boneOrder[i] = i;
		}
		for (var i = 0; i < TerminalBoneSpecs.Length; i++)
		{
			var spec = TerminalBoneSpecs[i];
			var terminal = terminalBones[i] = new(RequiredJoint(spec.Previous), RequiredJoint(spec.From),
				rig.JointCount + i, spec.LengthScale);
			var bone = BoneSpecs.Length + i;
			bones[bone] = new(terminal.From, terminal.To, spec.Width, spec.Tags);
			boneOrder[bone] = bone;
		}
		for (var i = 0; i < bones.Length; i++)
		{
			var bone = bones[i];
			bones[i] = bone with { Parent = Array.FindIndex(bones, it => it.To == bone.From) };
		}
		body = RequiredJoint("Body");
		head = RequiredJoint("Head");
		Sample();
		CaptureReference();
		materialOrigin = owner.Position;
		neuralSkin = new(owner, owner.Neural, NeuralSize, NeuralOrigin, 31);
		neuralSkin.Initialize(owner.SkinMode);
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
		neuralSkin.Update(owner.SkinMode);
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

	// Each bone tapers from the width its parent ends at to its own.
	void BoneWidths(Bone bone, out float fromWidth, out float toWidth)
	{
		toWidth = bone.Width * owner.Thickness;
		fromWidth = bone.Parent >= 0 ? bones[bone.Parent].Width * owner.Thickness : toWidth;
	}

	void CaptureReference()
	{
		for (var i = 0; i < bones.Length; i++)
		{
			var bone = bones[i];
			BoneWidths(bone, out var fromWidth, out var toWidth);
			BoneQuad(projected[bone.From], projected[bone.To], fromWidth, toWidth,
				out var a, out var b, out var c, out var d);
			boneUVs[i] = new(NeuralUV(a), NeuralUV(b), NeuralUV(c), NeuralUV(d));
		}
		bodyUV = NeuralUV(projected[body]);
		headUV = NeuralUV(projected[head]);
	}

	public void Hit() => neuralHitRemaining = 0.24f;

	public void PerturbNeuralTexture(NeuralTextureActor.PerturbContext context)
	{
		if (owner.SkinMode != RigSkinMode.NCA || context.Texture != neuralSkin.Texture)
			return;
		var center = (Vector2)owner.Position + projected[body];
		if (neuralHitRemaining > 0)
			context.AddStamp(center, center, 8, owner.NeuralReactions.NeuralHitStrength);
		if (owner.NeuralReactions.NeuralJostleInterval <= 0 || owner.NeuralReactions.NeuralJostleStrength <= 0 ||
			owner.Time.SecondsF < nextJostle)
			return;
		nextJostle = owner.Time.SecondsF + owner.NeuralReactions.NeuralJostleInterval;
		var tip = projected[bones[boneOrder[(int)(owner.Time.SecondsF * 13) % boneOrder.Length]].To];
		context.AddStamp((Vector2)owner.Position + tip, (Vector2)owner.Position + tip,
			owner.NeuralReactions.NeuralJostleRadius, owner.NeuralReactions.NeuralJostleStrength);
	}

	void BuildDrawList()
	{
		drawItems.Clear();
		var costume = Assets.GetCostume(owner.CostumeId);
		Span<bool> drawn = stackalloc bool[bones.Length];
		drawn.Clear();
		foreach (var index in boneOrder)
		{
			var bone = bones[index];
			BoneWidths(bone, out var fromWidth, out var toWidth);
			drawItems.Add(new(RigDrawKind.Bone, index, false, projected[bone.From], projected[bone.To],
				fromWidth, toWidth, Color.Lerp(owner.FarColor, owner.NearColor, Math.Clamp(BoneDepth(bone) * 2 + 0.5f, 0, 1)),
				Rings<Bone>(bones, index, drawn), bone.Parent >= 0));
			AddCostumeItems(drawItems, costume, bone.Tags);
			drawn[index] = true;
		}
		drawItems.Add(new(RigDrawKind.Body, body, From: projected[body], Color: owner.CoreColor));
		drawItems.Add(new(RigDrawKind.Head, head, From: projected[head], Color: owner.NearColor));
	}

	RigSkin Skin(Surface? surface, Texture? texture, float amount, bool normals)
		=> new(surface, texture, amount, normals, NeuralSize, NeuralOrigin, materialOrigin, owner.Time.SecondsF);

	float BallRadius(in RigDrawItem item) => item.Kind == RigDrawKind.Body ? owner.BodySize : owner.HeadSize;
	Vector2 BallUV(in RigDrawItem item) => item.Kind == RigDrawKind.Body ? bodyUV : headUV;

	// Each pass rebuilds the rig's depth-ordered item list, then draws from it.
	public void Draw(Batcher batcher, Color tint, bool silhouette)
	{
		BuildDrawList();
		if (silhouette)
		{
			foreach (var item in drawItems)
				if (item.Kind == RigDrawKind.Costume)
					DrawCostume(batcher, item, default, tint, tint, default, silhouette: true);
				else if (item.Kind == RigDrawKind.Bone)
					DrawBoneSilhouette(batcher, item, tint);
				else
					batcher.Circle(item.From, BallRadius(item) + 1.5f, 14, tint);
			return;
		}

		var skin = Skin(owner.SkinMode == RigSkinMode.Surface ? Assets.GetSurface(owner.SurfaceId) : null,
			owner.SkinMode == RigSkinMode.NCA && owner.Neural.NeuralColorAmount > 0 ? neuralSkin.OutputTexture : null,
			owner.Neural.NeuralColorAmount, normals: false);
		skin.Push(batcher);
		var outline = Multiply(owner.OutlineColor, tint);
		foreach (var item in drawItems)
		{
			if (item.Kind == RigDrawKind.Costume)
			{
				DrawCostume(batcher, item, boneUVs[item.Index], tint, owner.OutlineColor, skin);
				continue;
			}
			var color = Multiply(item.Color, tint);
			if (item.Kind == RigDrawKind.Bone)
			{
				DrawBone(batcher, item, boneUVs[item.Index], color, outline, skin);
				continue;
			}
			var radius = BallRadius(item);
			batcher.Circle(item.From, radius + 1.5f, 14, outline);
			batcher.Circle(item.From, radius, 14, color);
			skin.Joint(batcher, item.From, radius, BallUV(item), color);
			if (item.Kind == RigDrawKind.Head)
			{
				var look = (owner.Game.GetFirst<Player>()?.Position - owner.Position - item.From)?.Normalized() ?? Vector2.UnitX;
				batcher.Circle(item.From + look * radius * 0.55f, 1.2f, 8, Multiply(owner.EyeColor, tint));
			}
		}
		skin.Pop(batcher);
	}

	public void DrawNormals(Batcher batcher)
	{
		BuildDrawList();
		var skin = Skin(owner.SkinMode == RigSkinMode.Surface &&
			Assets.GetSurface(owner.SurfaceId) is { HasNormal: true } found ? found : null,
			owner.SkinMode == RigSkinMode.NCA && owner.Neural.NeuralNormalAmount > 0 ? neuralSkin.OutputNormalTexture : null,
			owner.Neural.NeuralNormalAmount, normals: true);
		skin.Push(batcher);
		foreach (var item in drawItems)
		{
			if (item.Kind == RigDrawKind.Costume)
			{
				DrawCostume(batcher, item, boneUVs[item.Index], Color.White, owner.OutlineColor, skin);
				continue;
			}
			if (item.Kind == RigDrawKind.Bone)
			{
				DrawBoneNormals(batcher, item, boneUVs[item.Index], 0, 0, 0, skin);
				continue;
			}
			batcher.Circle(item.From, BallRadius(item) + 1.5f, 14, new Color(0x8080ff));
			skin.Joint(batcher, item.From, BallRadius(item), BallUV(item), Color.White);
		}
		skin.Pop(batcher);
	}

	static Vector2 NeuralUV(Vector2 position) => (position - (Vector2)NeuralOrigin) / (Vector2)NeuralSize;

	public Rect GetVisualBounds()
	{
		BuildDrawList();
		var bounds = new Rect(projected[body], Vector2.Zero);
		foreach (var item in drawItems)
		{
			if (item.Kind == RigDrawKind.Costume)
				bounds = bounds.Conflate(CostumeBounds(item));
			else if (item.Kind == RigDrawKind.Bone)
				bounds = bounds.Conflate(Rect.Between(item.From, item.To).Inflate(MathF.Max(item.FromWidth, item.ToWidth) * 0.5f + 2));
			else
				bounds = bounds.Conflate(new Rect(item.From, Vector2.Zero).Inflate(BallRadius(item) + 2));
		}
		return bounds + owner.Position;
	}

	public void Dispose()
	{
		animator?.Dispose();
		neuralSkin?.Dispose();
	}
}
