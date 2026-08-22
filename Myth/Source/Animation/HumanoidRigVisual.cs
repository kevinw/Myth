using System.Numerics;
using Foster.Framework;

namespace Myth;

sealed class HumanoidRigVisual(Actor owner) : IDisposable
{
	enum Part { Core, Cloth, Skin, Boot }

	readonly record struct BoneSpec(string From, string To, float Width, Part Part);
	readonly record struct Bone(int From, int To, float Width, Part Part);
	readonly record struct Leg(int Hip, int Knee, int Ankle, int Toe, int Tip);

	struct FootIK
	{
		public float OffsetY;
		public bool Planted;
	}

	static readonly BoneSpec[] BoneSpecs =
	[
		new("pelvis", "spine_01", 5, Part.Core),
		new("spine_01", "spine_02", 5, Part.Core),
		new("spine_02", "spine_03", 5, Part.Core),
		new("spine_03", "neck_01", 4, Part.Skin),
		new("neck_01", "Head", 3, Part.Skin),
		new("spine_03", "clavicle_l", 3, Part.Cloth),
		new("clavicle_l", "upperarm_l", 3, Part.Cloth),
		new("upperarm_l", "lowerarm_l", 3, Part.Cloth),
		new("lowerarm_l", "hand_l", 2.5f, Part.Skin),
		new("spine_03", "clavicle_r", 3, Part.Cloth),
		new("clavicle_r", "upperarm_r", 3, Part.Cloth),
		new("upperarm_r", "lowerarm_r", 3, Part.Cloth),
		new("lowerarm_r", "hand_r", 2.5f, Part.Skin),
		new("pelvis", "thigh_l", 4, Part.Cloth),
		new("thigh_l", "calf_l", 4, Part.Cloth),
		new("calf_l", "foot_l", 3, Part.Boot),
		new("foot_l", "ball_l", 2, Part.Boot),
		new("ball_l", "ball_leaf_l", 2, Part.Boot),
		new("pelvis", "thigh_r", 4, Part.Cloth),
		new("thigh_r", "calf_r", 4, Part.Cloth),
		new("calf_r", "foot_r", 3, Part.Boot),
		new("foot_r", "ball_r", 2, Part.Boot),
		new("ball_r", "ball_leaf_r", 2, Part.Boot),
	];

	public float PlaybackSpeed = 1;
	public float VisualFPS = 20;
	public float PixelsPerMeter = 25;
	public float ProjectionYaw;
	public float BootThickness = 2.5f;
	public float TrackingLean;
	public float UpperBodyHitStrength = 0.8f;
	public Vector2 Offset;
	public Vector2? LookTarget;
	public bool Decorated;
	public bool TurnWhileMoving;
	public Color FarColor = new(0x435263);
	public Color NearColor = new(0xb7cdd0);
	public Color CoreColor = new(0x708b94);
	public Color OutlineColor = new(0x171d27);
	public Color ClothColor = new(0x35263e);
	public Color ClothHighlightColor = new(0x66506f);
	public Color SkinColor = new(0x9b817a);
	public Color BootColor = new(0x17141d);
	public Color AccentColor = new(0xd92240);

	static OzzRig? sharedRig;
	OzzRig rig = null!;
	OzzAnimator animator = null!;
	Vector3[] sampledPose = [];
	Quaternion[] sampledRotations = [];
	Vector3[] ragdollPose = [];
	Vector3[] ragdollPreviousPose = [];
	Vector3[] ragdollVelocity = [];
	bool[] ragdollJoints = [];
	float[] boneLengths = [];
	Vector2[] projectedPose = [];
	float[] jointDepth = [];
	Bone[] bones = [];
	OzzJointMask upperBodyMask = null!;
	OzzRig.Clip upperBodyHitClip = null!;
	int[] boneOrder = [];
	Leg leftLeg;
	Leg rightLeg;
	FootIK leftFoot;
	FootIK rightFoot;
	int head;
	int neck;
	int chest;
	int pelvis;
	float visualAccumulator;
	float visualDelta;
	float pendingLandingSpeed;
	float layeredHitTime = -1;
	Signs visualFacing;
	Signs targetFacing;
	float visualYaw;
	float turnFromYaw;
	float turnToYaw;
	float turnTime;
	float turnAmount;
	Vector2 coatTail;
	Vector2 coatTailVelocity;
	bool ragdollReady;
	bool turnReady;
	bool turning;
	bool coatReady;
	bool poseDirty;
	Vector2 lookDirection;

	static float ClipBlendDuration => 0.2f;
	static float RagdollSpring => 160f;
	static float RagdollDamping => 11f;
	static float RagdollMaxSpeed => 10f;
	static float LandingMinSpeed => 32f;
	static float LandingInertia => 1f;
	static float LandingCompressionTime => 0.0225f;
	static int RagdollConstraintIterations => 4;
	static float TurnDuration => 0.28f;
	static float TurnStartSpeed => 2f;
	static float FootProbeAbove => 4f;
	static float FootProbeDistance => 12f;
	static float FootPlantDistance => 5f;
	static float FootIKSpeed => 40f;
	static float LegExtensionSlack => 0.4f;
	static float HeelLength => 4f;
	static float CoatSpring => 75f;
	static float CoatDamping => 10f;
	static float LayeredHitDuration => 0.42f;
	static float TrackingLeanDistance => 3f;

	public IReadOnlyList<OzzRig.Clip> Clips => rig.Clips;
	public OzzRig.Clip ActiveClip => animator.ActiveClip;
	public float Time
	{
		get => animator.Time;
		set
		{
			animator.SetTime(value);
			poseDirty = true;
		}
	}
	public bool Turning => turning;
	public bool FacingMatches => !turning && visualFacing == owner.Facing;

	public void Initialize(string clip)
	{
		if (VisualFPS <= 0)
			VisualFPS = 20;
		rig = sharedRig ??= LoadRig();
		var initialClip = rig.TryGetClip(clip, out _) ? clip : "Zombie_Idle_Loop";
		animator = new(rig, initialClip);
		sampledPose = new Vector3[rig.JointCount];
		sampledRotations = new Quaternion[rig.JointCount];
		ragdollPose = new Vector3[rig.JointCount];
		ragdollPreviousPose = new Vector3[rig.JointCount];
		ragdollVelocity = new Vector3[rig.JointCount];
		ragdollJoints = new bool[rig.JointCount];
		boneLengths = new float[BoneSpecs.Length];
		projectedPose = new Vector2[rig.JointCount];
		jointDepth = new float[rig.JointCount];
		bones = new Bone[BoneSpecs.Length];
		boneOrder = new int[bones.Length];
		for (var i = 0; i < bones.Length; i++)
		{
			var spec = BoneSpecs[i];
			bones[i] = new(RequiredJoint(spec.From), RequiredJoint(spec.To), spec.Width, spec.Part);
			ragdollJoints[bones[i].From] = true;
			ragdollJoints[bones[i].To] = true;
			boneOrder[i] = i;
		}
		head = RequiredJoint("Head");
		neck = RequiredJoint("neck_01");
		chest = RequiredJoint("spine_03");
		pelvis = RequiredJoint("pelvis");
		leftLeg = RequiredLeg("l");
		rightLeg = RequiredLeg("r");
		upperBodyMask = rig.CreateMask();
		upperBodyMask.Set("spine_01", 1, descendants: true);
		upperBodyMask.Set("spine_01", 0.25f);
		upperBodyMask.Set("spine_02", 0.6f);
		upperBodyHitClip = rig.GetClip("Hit_Knockback_Additive");
		ResetTurn();
		visualDelta = 1 / VisualFPS;
		UpdatePose(grounded: false, footIK: false);
	}

	static OzzRig LoadRig()
	{
		using var stream = Assets.Content.OpenRead("Rigs/ual2.ozzpack");
		return OzzRig.Load(stream);
	}

	int RequiredJoint(string name)
	{
		var index = rig.FindJoint(name);
		return index >= 0 ? index : throw new InvalidDataException($"UAL2 rig is missing joint '{name}'.");
	}

	Leg RequiredLeg(string side)
		=> new(RequiredJoint($"thigh_{side}"), RequiredJoint($"calf_{side}"),
			RequiredJoint($"foot_{side}"), RequiredJoint($"ball_{side}"), RequiredJoint($"ball_leaf_{side}"));

	public bool TryGetClip(string name, out OzzRig.Clip clip) => rig.TryGetClip(name, out clip);
	public OzzRig.Clip GetClip(string name) => rig.GetClip(name);

	public void Play(string name, bool blend = true, bool restart = false)
		=> animator.Play(name, blend ? ClipBlendDuration : 0, restart);

	public void Update(bool grounded, bool footIK, bool loop = true, bool advance = true)
	{
		EnsureState();
		var step = 1 / VisualFPS;
		visualAccumulator += owner.Time.Delta;
		var ticks = Math.Min((int)(visualAccumulator / step), 4);
		if (ticks <= 0)
		{
			if (!advance && poseDirty)
			{
				visualDelta = step;
				poseDirty = false;
				UpdatePose(grounded, footIK);
			}
			return;
		}

		visualAccumulator -= ticks * step;
		if (visualAccumulator >= step)
			visualAccumulator %= step;
		visualDelta = step;
		poseDirty = false;
		for (var i = 0; i < ticks; i++)
		{
			UpdateTurn(grounded);
			animator.Advance(advance ? step * PlaybackSpeed : 0, step, loop);
			UpdatePose(grounded, footIK);
		}
	}

	public void ResetTurn()
	{
		visualFacing = targetFacing = owner.Facing;
		visualYaw = FacingYaw(visualFacing);
		turnFromYaw = turnToYaw = visualYaw;
		turnTime = turnAmount = 0;
		turnReady = true;
		turning = false;
	}

	public void ResetFeet()
	{
		leftFoot = default;
		rightFoot = default;
	}

	public void Land(float speed)
	{
		if (speed >= LandingMinSpeed)
			pendingLandingSpeed = MathF.Max(pendingLandingSpeed, speed);
	}

	public void Hit(float away, bool animateUpperBody = false)
	{
		if (animateUpperBody)
			layeredHitTime = 0;
		for (var i = 0; i < ragdollVelocity.Length; i++)
		{
			if (!ragdollJoints[i])
				continue;
			var height = Math.Clamp(sampledPose[i].Y / 1.55f, 0, 1);
			var depthKick = MathF.Sign(jointDepth[i]) * 7 * height;
			ragdollVelocity[i] += RigVelocity(new(away * (18 + 52 * height) + depthKick, -15 * height));
		}
	}

	void EnsureState()
	{
		if (VisualFPS <= 0)
			VisualFPS = 20;
		if (!turnReady)
			ResetTurn();
		else if (!turning && visualFacing == owner.Facing)
		{
			targetFacing = owner.Facing;
			visualYaw = FacingYaw(visualFacing);
		}
	}

	float FacingYaw(Signs facing)
		=> facing == Signs.Negative ? 0 : ProjectionYaw >= 0 ? -MathF.PI : MathF.PI;

	void UpdateTurn(bool grounded)
	{
		if (owner.Facing != targetFacing)
		{
			targetFacing = owner.Facing;
			turnFromYaw = visualYaw;
			turnToYaw = FacingYaw(targetFacing);
			turnTime = turnAmount = 0;
			turning = true;
			ResetFeet();
		}
		if (!turning || !grounded || !TurnWhileMoving && MathF.Abs(owner.Velocity.X) > TurnStartSpeed)
			return;

		turnTime = MathF.Min(turnTime + visualDelta, TurnDuration);
		var progress = turnTime / TurnDuration;
		turnAmount = progress * progress * (3 - 2 * progress);
		visualYaw = float.Lerp(turnFromYaw, turnToYaw, turnAmount);
		if (turnTime < TurnDuration)
			return;

		visualFacing = targetFacing;
		visualYaw = FacingYaw(visualFacing);
		turnTime = turnAmount = 0;
		turning = false;
	}

	void UpdatePose(bool grounded, bool footIK)
	{
		UpdateAnimationLayers();
		animator.Sample(sampledPose, sampledRotations);
		ApplyTrackingLean();
		UpdateRagdoll();
		ProjectPose();
		if (footIK)
		{
			UpdateFoot(ref leftFoot, leftLeg, grounded);
			UpdateFoot(ref rightFoot, rightLeg, grounded);
		}
		else
			ResetFeet();
		SortBones();
		UpdateCoat();
		lookDirection = LookTarget is { } target
			? (target - owner.Position - projectedPose[head]).Normalized()
			: new Vector2(-MathF.Cos(visualYaw), 0);
	}

	void UpdateAnimationLayers()
	{
		if (layeredHitTime < 0)
		{
			animator.ClearLayer();
			return;
		}

		layeredHitTime += visualDelta;
		var progress = layeredHitTime / LayeredHitDuration;
		if (progress >= 1)
		{
			layeredHitTime = -1;
			animator.ClearLayer();
			return;
		}
		animator.SetAdditive(upperBodyHitClip, MathF.Min(upperBodyHitClip.Duration, layeredHitTime * 1.6f),
			MathF.Sin(progress * MathF.PI) * UpperBodyHitStrength, upperBodyMask);
	}

	void ApplyTrackingLean()
	{
		if (TrackingLean <= 0 || LookTarget is not { } target)
			return;
		var toward = target - owner.Position;
		var screenOffset = new Vector2(Math.Clamp(toward.X / 64f, -1, 1),
			Math.Clamp(toward.Y / 96f, -0.5f, 0.5f)) * TrackingLeanDistance * TrackingLean;
		var rigOffset = RigVelocity(screenOffset);
		for (var i = 0; i < sampledPose.Length; i++)
			sampledPose[i] += rigOffset * upperBodyMask[i];
	}

	public void Dispose() => animator?.Dispose();

	void ProjectPose()
	{
		var angle = ProjectionYaw * MathF.PI / 180f;
		var cos = MathF.Cos(angle);
		var sin = MathF.Sin(angle);
		var turnCos = MathF.Cos(visualYaw);
		var turnSin = MathF.Sin(visualYaw);
		var pivot = ragdollPose[pelvis];
		var pivotX = -pivot.Z * cos + pivot.X * sin;
		var mirror = turning
			? float.Lerp(-(int)visualFacing, -(int)targetFacing, turnAmount)
			: -(int)visualFacing;
		for (var i = 0; i < sampledPose.Length; i++)
		{
			var joint = ragdollJoints[i] ? ragdollPose[i] : sampledPose[i];
			var offset = joint - pivot;
			var turned = new Vector3(offset.X * turnCos + offset.Z * turnSin, offset.Y,
				-offset.X * turnSin + offset.Z * turnCos);
			projectedPose[i] = new Vector2(pivotX * mirror - turned.Z * cos + turned.X * sin, -joint.Y) *
				PixelsPerMeter + new Vector2(0, 2.5f) + Offset;
			jointDepth[i] = joint.X * cos + joint.Z * sin;
		}
	}

	void UpdateRagdoll()
	{
		if (!ragdollReady)
		{
			sampledPose.CopyTo(ragdollPose, 0);
			Array.Clear(ragdollVelocity);
			for (var i = 0; i < bones.Length; i++)
				boneLengths[i] = Vector3.Distance(sampledPose[bones[i].From], sampledPose[bones[i].To]);
			ragdollReady = true;
			pendingLandingSpeed = 0;
			return;
		}

		if (pendingLandingSpeed > 0)
		{
			var impactSpeed = pendingLandingSpeed / PixelsPerMeter;
			for (var i = 0; i < ragdollPose.Length; i++)
				if (ragdollJoints[i])
				{
					var height = Math.Clamp(sampledPose[i].Y / 1.55f, 0, 1);
					ragdollPose[i].Y -= impactSpeed * LandingCompressionTime * height;
					ragdollVelocity[i].Y -= impactSpeed * LandingInertia * height;
				}
			pendingLandingSpeed = 0;
		}

		for (var remaining = visualDelta; remaining > 0;)
		{
			var dt = MathF.Min(remaining, 1 / 30f);
			remaining -= dt;
			var damping = MathF.Exp(-RagdollDamping * dt);
			for (var i = 0; i < ragdollPose.Length; i++)
				if (ragdollJoints[i])
				{
					ragdollPreviousPose[i] = ragdollPose[i];
					ragdollVelocity[i] += (sampledPose[i] - ragdollPose[i]) * RagdollSpring * dt;
					ragdollVelocity[i] *= damping;
					if (ragdollVelocity[i].LengthSquared() > RagdollMaxSpeed * RagdollMaxSpeed)
						ragdollVelocity[i] = Vector3.Normalize(ragdollVelocity[i]) * RagdollMaxSpeed;
					ragdollPose[i] += ragdollVelocity[i] * dt;
				}

			for (var iteration = 0; iteration < RagdollConstraintIterations; iteration++)
				for (var i = 0; i < bones.Length; i++)
				{
					var bone = bones[i];
					var delta = ragdollPose[bone.To] - ragdollPose[bone.From];
					var length = delta.Length();
					if (length <= 0.0001f)
						continue;
					var correction = delta * ((length - boneLengths[i]) / length * 0.5f);
					ragdollPose[bone.From] += correction;
					ragdollPose[bone.To] -= correction;
				}

			for (var i = 0; i < ragdollPose.Length; i++)
				if (ragdollJoints[i])
					ragdollVelocity[i] = (ragdollPose[i] - ragdollPreviousPose[i]) / dt;
		}
	}

	Vector3 RigVelocity(Vector2 screenVelocity)
	{
		var angle = ProjectionYaw * MathF.PI / 180f + visualYaw;
		var alongProjection = screenVelocity.X / PixelsPerMeter;
		return new(MathF.Sin(angle) * alongProjection, -screenVelocity.Y / PixelsPerMeter,
			-MathF.Cos(angle) * alongProjection);
	}

	void UpdateFoot(ref FootIK plant, Leg leg, bool grounded)
	{
		var animatedToe = owner.Position + projectedPose[leg.Toe];
		var targetOffset = 0f;
		plant.Planted = false;
		if (grounded)
		{
			var probeCenter = (animatedToe - Vector2.UnitY * FootProbeAbove).RoundToPoint2();
			var probe = new RectInt(probeCenter.X - 1, probeCenter.Y - 1, 2, 2);
			if (owner.Game.TryFindSurface(owner, probe, Vector2.UnitY, FootProbeDistance, out var hit) &&
				hit.Normal.Y < -0.5f && MathF.Abs(animatedToe.Y - hit.Point.Y) <= FootPlantDistance)
			{
				targetOffset = hit.Point.Y - animatedToe.Y;
				plant.Planted = true;
			}
		}

		plant.OffsetY = Calc.Approach(plant.OffsetY, targetOffset, FootIKSpeed * visualDelta);
		if (MathF.Abs(plant.OffsetY) > 0.001f)
		{
			var oldToe = projectedPose[leg.Toe];
			var footOffset = oldToe - projectedPose[leg.Ankle];
			SolveLeg(leg, oldToe + Vector2.UnitY * plant.OffsetY - footOffset);
			projectedPose[leg.Toe] = projectedPose[leg.Ankle] + footOffset;
			projectedPose[leg.Tip] += projectedPose[leg.Toe] - oldToe;
		}
		if (plant.Planted)
			projectedPose[leg.Tip] = projectedPose[leg.Toe] + Vector2.UnitX * (int)visualFacing *
				Vector2.Distance(projectedPose[leg.Toe], projectedPose[leg.Tip]);
	}

	void SolveLeg(Leg leg, Vector2 ankleTarget)
	{
		var hip = projectedPose[leg.Hip];
		var animatedKnee = projectedPose[leg.Knee];
		var upperLength = Vector2.Distance(hip, animatedKnee);
		var lowerLength = Vector2.Distance(animatedKnee, projectedPose[leg.Ankle]);
		var towardTarget = ankleTarget - hip;
		var distance = towardTarget.Length();
		if (distance <= 0.001f || upperLength <= 0.001f || lowerLength <= 0.001f)
			return;

		var direction = towardTarget / distance;
		distance = Math.Clamp(distance, MathF.Abs(upperLength - lowerLength) + 0.001f,
			upperLength + lowerLength - MathF.Min(LegExtensionSlack, (upperLength + lowerLength) * 0.04f));
		var along = (upperLength * upperLength - lowerLength * lowerLength + distance * distance) / (2 * distance);
		var height = MathF.Sqrt(MathF.Max(0, upperLength * upperLength - along * along));
		var perpendicular = new Vector2(-direction.Y, direction.X);
		projectedPose[leg.Knee] = hip + direction * along - perpendicular * height * (int)visualFacing;
		projectedPose[leg.Ankle] = hip + direction * distance;
	}

	void SortBones()
	{
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

	void UpdateCoat()
	{
		if (!Decorated)
			return;
		var target = projectedPose[pelvis] + Vector2.UnitY * 12 - owner.Velocity * 0.025f;
		if (!coatReady)
		{
			coatTail = target;
			coatReady = true;
		}
		for (var remaining = visualDelta; remaining > 0;)
		{
			var dt = MathF.Min(remaining, 1 / 30f);
			remaining -= dt;
			coatTailVelocity += (target - coatTail) * CoatSpring * dt;
			coatTailVelocity *= MathF.Exp(-CoatDamping * dt);
			coatTail += coatTailVelocity * dt;
		}
	}

	float BoneDepth(Bone bone) => (jointDepth[bone.From] + jointDepth[bone.To]) * 0.5f;

	Color BoneColor(Bone bone, float depth)
	{
		if (!Decorated)
			return bone.From == pelvis || bone.To == pelvis ? CoreColor : Color.Lerp(FarColor, NearColor, depth);
		return bone.Part switch
		{
			Part.Core => CoreColor,
			Part.Cloth => Color.Lerp(ClothColor, ClothHighlightColor, depth),
			Part.Skin => SkinColor,
			_ => BootColor,
		};
	}

	public void Draw(Batcher batcher, Color tint, bool silhouette)
	{
		if (Decorated)
			DrawCoat(batcher, tint, silhouette);
		for (var sortedIndex = 0; sortedIndex < boneOrder.Length; sortedIndex++)
		{
			var bone = bones[boneOrder[sortedIndex]];
			var from = projectedPose[bone.From];
			var to = projectedPose[bone.To];
			if (silhouette)
			{
				batcher.Line(from, to, bone.Width + 2, tint);
				continue;
			}

			var depth = Math.Clamp(BoneDepth(bone) + 0.5f, 0, 1);
			var color = BoneColor(bone, depth);
			batcher.Line(from, to, bone.Width + 2, OutlineColor);
			batcher.Line(from, to, bone.Width, color);
			batcher.Circle(to, MathF.Max(1.2f, bone.Width * 0.42f), 8, color);
		}
		if (jointDepth[leftLeg.Toe] < jointDepth[rightLeg.Toe])
		{
			DrawBoot(batcher, tint, silhouette, leftFoot, leftLeg);
			DrawBoot(batcher, tint, silhouette, rightFoot, rightLeg);
		}
		else
		{
			DrawBoot(batcher, tint, silhouette, rightFoot, rightLeg);
			DrawBoot(batcher, tint, silhouette, leftFoot, leftLeg);
		}

		var headPosition = projectedPose[head];
		batcher.Circle(headPosition, 5.3f, 14, silhouette ? tint : OutlineColor);
		batcher.Circle(headPosition, 3.9f, 14, silhouette ? tint : Decorated ? SkinColor : NearColor);
		if (Decorated)
			DrawHat(batcher, tint, silhouette);
		if (!silhouette)
		{
			batcher.Circle(headPosition + lookDirection * 2.4f, Decorated ? 1.35f : 1.1f, 8, AccentColor);
		}
	}

	void DrawBoot(Batcher batcher, Color tint, bool silhouette, FootIK plant, Leg leg)
	{
		var ball = projectedPose[leg.Toe];
		var forward = plant.Planted
			? Vector2.UnitX * (int)visualFacing
			: (projectedPose[leg.Tip] - ball).Normalized(Vector2.UnitX * (int)visualFacing);
		var up = forward.TurnLeft();
		if (Vector2.Dot(up, -Vector2.UnitY) < 0)
			up = -up;
		var heel = ball - forward * HeelLength;
		var toe = projectedPose[leg.Tip];
		var top = up * BootThickness;
		var color = silhouette ? tint : Decorated ? BootColor : NearColor;
		batcher.Quad(heel, toe, toe + top, heel + top, color);
		if (!silhouette)
			batcher.QuadLine(heel, toe, toe + top, heel + top, 1.5f, OutlineColor);
	}

	void DrawCoat(Batcher batcher, Color tint, bool silhouette)
	{
		var torso = projectedPose[chest];
		var hips = projectedPose[pelvis];
		var down = (hips - torso).Normalized();
		var side = new Vector2(-down.Y, down.X);
		var shoulderLeft = torso - side * 6.5f;
		var shoulderRight = torso + side * 6.5f;
		var hipLeft = hips - side * 6;
		var hipRight = hips + side * 6;
		var tailLeft = coatTail - side * 4.5f;
		var tailRight = coatTail + side * 4.5f;
		var color = silhouette ? tint : ClothColor;
		batcher.Quad(shoulderLeft, shoulderRight, hipRight, hipLeft, color);
		batcher.Quad(hipLeft, hipRight, tailRight, tailLeft, color);
		if (!silhouette)
		{
			batcher.QuadLine(shoulderLeft, shoulderRight, hipRight, hipLeft, 1.5f, OutlineColor);
			batcher.QuadLine(hipLeft, hipRight, tailRight, tailLeft, 1.5f, OutlineColor);
			batcher.Line(torso, coatTail, 1, ClothHighlightColor * 0.55f);
		}
	}

	void DrawHat(Batcher batcher, Color tint, bool silhouette)
	{
		var headPosition = projectedPose[head];
		var up = (headPosition - projectedPose[neck]).Normalized();
		var side = new Vector2(-up.Y, up.X);
		var brim = headPosition + up * 3.2f;
		var top = brim + up * 5;
		var left = brim - side * 4;
		var right = brim + side * 4;
		var color = silhouette ? tint : ClothColor;
		batcher.Quad(left, right, top + side * 2.5f, top - side * 2.5f, color);
		batcher.Line(brim - side * 6, brim + side * 6, 2.5f, silhouette ? tint : OutlineColor);
		if (!silhouette)
		{
			batcher.QuadLine(left, right, top + side * 2.5f, top - side * 2.5f, 1.5f, OutlineColor);
			batcher.Line(left, right, 1.2f, AccentColor);
		}
	}
}
