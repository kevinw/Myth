using System.Numerics;
using Foster.Framework;

namespace Myth;

[GameActor(Sprite = "player", Tags = ActorTag.Enemy, BoundsX = -12, BoundsY = -40, BoundsW = 24, BoundsH = 40)]
public class RigPuppet : Actor
{
	readonly record struct BoneSpec(string From, string To, float Width);
	readonly record struct Bone(int From, int To, float Width);

	static readonly BoneSpec[] BoneSpecs =
	[
		new("pelvis", "spine_01", 5),
		new("spine_01", "spine_02", 5),
		new("spine_02", "spine_03", 5),
		new("spine_03", "neck_01", 4),
		new("neck_01", "Head", 3),
		new("spine_03", "clavicle_l", 3),
		new("clavicle_l", "upperarm_l", 3),
		new("upperarm_l", "lowerarm_l", 3),
		new("lowerarm_l", "hand_l", 2.5f),
		new("spine_03", "clavicle_r", 3),
		new("clavicle_r", "upperarm_r", 3),
		new("upperarm_r", "lowerarm_r", 3),
		new("lowerarm_r", "hand_r", 2.5f),
		new("pelvis", "thigh_l", 4),
		new("thigh_l", "calf_l", 4),
		new("calf_l", "foot_l", 3),
		new("foot_l", "ball_l", 2),
		new("pelvis", "thigh_r", 4),
		new("thigh_r", "calf_r", 4),
		new("calf_r", "foot_r", 3),
		new("foot_r", "ball_r", 2),
	];

	public string AnimationName = "Zombie_Walk_Fwd_Loop";
	[I(0.1f, 3)] public float PlaybackSpeed = 1;
	[I(12, 40)] public float PixelsPerMeter = 25;
	[I(-45, 45)] public float ProjectionYaw;
	[I(1, 20)] public int Health = 8;
	public Color FarColor = new(0x435263);
	public Color NearColor = new(0xb7cdd0);
	public Color CoreColor = new(0x708b94);
	public Color OutlineColor = new(0x171d27);

	static SampledRig? sharedRig;
	SampledRig rig = null!;
	SampledRig.Clip activeClip = null!;
	SampledRig.Clip hitClip = null!;
	Vector3[] sampledPose = [];
	Vector2[] projectedPose = [];
	float[] jointDepth = [];
	Vector2[] recoil = [];
	Vector2[] recoilVelocity = [];
	Vector2[] inheritedRecoil = [];
	Bone[] bones = [];
	int[] boneOrder = [];
	int head;
	int pelvis;
	float rigTime;
	bool hitReacting;

	static float Gravity => 360f;
	static float GroundFriction => 420f;
	static float RecoilSpring => 74f;
	static float RecoilDamping => 10f;
	static float RecoilInheritance => 0.58f;

	public RigPuppet()
	{
		Sprite = null;
		Hitbox = new(new RectInt(-6, -31, 12, 31));
		Mask = Masks.Enemy;
		Depth = -5;
		IFrameTime = 0.18f;
	}

	public override bool CastsSpriteShadow => true;
	public override bool CastsReflection => true;
	public override bool LightingDithered => false;

	public override void Added()
	{
		rig = sharedRig ??= LoadRig();
		sampledPose = new Vector3[rig.JointCount];
		projectedPose = new Vector2[rig.JointCount];
		jointDepth = new float[rig.JointCount];
		recoil = new Vector2[rig.JointCount];
		recoilVelocity = new Vector2[rig.JointCount];
		inheritedRecoil = new Vector2[rig.JointCount];
		bones = new Bone[BoneSpecs.Length];
		boneOrder = new int[bones.Length];
		for (var i = 0; i < bones.Length; i++)
		{
			var spec = BoneSpecs[i];
			bones[i] = new(RequiredJoint(spec.From), RequiredJoint(spec.To), spec.Width);
			boneOrder[i] = i;
		}
		head = RequiredJoint("Head");
		pelvis = RequiredJoint("pelvis");
		hitClip = rig.GetClip("Hit_Knockback");
		activeClip = LocomotionClip();
		UpdatePose();
	}

	static SampledRig LoadRig()
	{
		using var stream = Assets.Content.OpenRead("Rigs/ual2.mrig");
		return SampledRig.Load(stream);
	}

	int RequiredJoint(string name)
	{
		var index = rig.FindJoint(name);
		return index >= 0 ? index : throw new InvalidDataException($"UAL2 rig is missing joint '{name}'.");
	}

	SampledRig.Clip LocomotionClip()
		=> rig.TryGetClip(AnimationName, out var clip) ? clip : rig.GetClip("Zombie_Walk_Fwd_Loop");

	public override void Update()
	{
		Velocity.Y += Gravity * Time.Delta;
		base.Update();
		if (Grounded())
		{
			Velocity.X = Calc.Approach(Velocity.X, 0, GroundFriction * Time.Delta);
			if (Velocity.Y > 0)
				Velocity.Y = 0;
		}

		if (Game.GetFirst<Player>() is { } player && MathF.Abs(player.Position.X - Position.X) > 1)
			Facing = player.Position.X < Position.X ? Signs.Negative : Signs.Positive;

		rigTime += Time.Delta * PlaybackSpeed;
		if (hitReacting && rigTime >= activeClip.Duration)
		{
			hitReacting = false;
			activeClip = LocomotionClip();
			rigTime = 0;
		}
		else if (!hitReacting && LocomotionClip() != activeClip)
		{
			activeClip = LocomotionClip();
			rigTime = 0;
		}

		UpdateRecoil();
		UpdatePose();
	}

	void UpdateRecoil()
	{
		var dt = MathF.Min(Time.Delta, 0.05f);
		var damping = MathF.Exp(-RecoilDamping * dt);
		for (var i = 0; i < recoil.Length; i++)
		{
			recoilVelocity[i] -= recoil[i] * RecoilSpring * dt;
			recoilVelocity[i] *= damping;
			recoil[i] += recoilVelocity[i] * dt;
		}
	}

	void UpdatePose()
	{
		rig.Sample(activeClip, rigTime, loop: !hitReacting, sampledPose);
		var angle = ProjectionYaw * MathF.PI / 180f;
		var cos = MathF.Cos(angle);
		var sin = MathF.Sin(angle);
		for (var i = 0; i < sampledPose.Length; i++)
		{
			var joint = sampledPose[i];
			projectedPose[i] = new Vector2((-joint.Z * cos + joint.X * sin) * (int)Facing,
				-joint.Y) * PixelsPerMeter + new Vector2(0, 2.5f);
			jointDepth[i] = joint.X * cos + joint.Z * sin;
			var parent = rig.JointParents[i];
			inheritedRecoil[i] = recoil[i] + (parent >= 0 ? inheritedRecoil[parent] * RecoilInheritance : Vector2.Zero);
			projectedPose[i] += inheritedRecoil[i];
		}

		for (var i = 1; i < boneOrder.Length; i++)
		{
			var value = boneOrder[i];
			var depth = BoneDepth(bones[value]);
			var at = i;
			while (at > 0 && BoneDepth(bones[boneOrder[at - 1]]) > depth)
			{
				boneOrder[at] = boneOrder[at - 1];
				at--;
			}
			boneOrder[at] = value;
		}
	}

	float BoneDepth(Bone bone) => (jointDepth[bone.From] + jointDepth[bone.To]) * 0.5f;

	public override void OnWasHit(Actor by)
	{
		if (Health <= 0)
			return;

		Health--;
		if (Health <= 0)
		{
			for (var i = 0; i < 3; i++)
				Game.Create<Pop>(Position + new Point2(0, -8 - i * 9));
			Game.DestroyForThisSession(this);
			return;
		}

		var away = Position.X < by.Position.X ? -1f : 1f;
		for (var i = 0; i < recoil.Length; i++)
		{
			var height = Math.Clamp(sampledPose[i].Y / 1.55f, 0, 1);
			var depthKick = MathF.Sign(jointDepth[i]) * 7 * height;
			recoilVelocity[i] += new Vector2(away * (18 + 52 * height) + depthKick, -15 * height);
		}
		Velocity += new Vector2(away * 48, -28);
		activeClip = hitClip;
		rigTime = 0;
		hitReacting = true;
	}

	public override void Render(Batcher batcher)
	{
		if (!SpriteBlinkHidden)
			DrawRig(batcher, Color.White, shadow: false);
	}

	public override void RenderShadow(Batcher batcher, Color color)
		=> DrawRig(batcher, color, shadow: true);

	public override void RenderReflection(Batcher batcher, Color color)
		=> DrawRig(batcher, color, shadow: true);

	public override void RenderNormals(Batcher batcher)
		=> DrawRig(batcher, new Color(0x8080ff), shadow: true);

	void DrawRig(Batcher batcher, Color tint, bool shadow)
	{
		for (var sortedIndex = 0; sortedIndex < boneOrder.Length; sortedIndex++)
		{
			var bone = bones[boneOrder[sortedIndex]];
			var from = projectedPose[bone.From];
			var to = projectedPose[bone.To];
			if (shadow)
			{
				batcher.Line(from, to, bone.Width + 2, tint);
				continue;
			}

			var depth = Math.Clamp(BoneDepth(bone) + 0.5f, 0, 1);
			var color = bone.From == pelvis || bone.To == pelvis ? CoreColor : Color.Lerp(FarColor, NearColor, depth);
			batcher.Line(from, to, bone.Width + 2, OutlineColor);
			batcher.Line(from, to, bone.Width, color);
			batcher.Circle(to, MathF.Max(1.2f, bone.Width * 0.42f), 8, color);
		}

		var headPosition = projectedPose[head];
		batcher.Circle(headPosition, 5.3f, 14, shadow ? tint : OutlineColor);
		batcher.Circle(headPosition, 3.9f, 14, shadow ? tint : NearColor);
		if (!shadow)
		{
			var look = new Vector2((int)Facing * 2.4f, 0.2f);
			batcher.Circle(headPosition + look, 1.1f, 8, new Color(0xd92240));
		}
	}

	public override Rect GetVisualBounds()
		=> new(Position.X - 42, Position.Y - 48, 84, 58);
}
