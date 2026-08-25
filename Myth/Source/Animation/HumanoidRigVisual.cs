using System.Numerics;
using Foster.Framework;

namespace Myth;

public enum RigSkinMode { Solid, Surface, NCA }

sealed class HumanoidRigVisual(Actor owner, HumanoidRigSettings? configuredSettings = null,
	NeuralSurfaceSettings? configuredNeural = null, HumanoidNeuralReactionSettings? configuredReactions = null) : IDisposable
{
	enum Part { Torso, Bicep, Forearm, Thigh, Other }
	enum LegSide { None, Left, Right }
	enum DrawParts { All, BehindPlayer, InFrontOfPlayer }

	readonly record struct BoneSpec(string From, string To, float Width, Part Part, bool Combat = false,
		LegSide Leg = LegSide.None);
	readonly record struct Bone(int From, int To, float Width, Part Part, bool Combat, LegSide Leg);
	readonly record struct BoneUV(Vector2 A, Vector2 B, Vector2 C, Vector2 D);
	readonly record struct Leg(int Hip, int Knee, int Ankle, int Toe, int Tip);

	struct FootIK
	{
		public float OffsetY;
		public bool Planted;
		public float ContactRemaining;
		public Vector2 ContactPosition;
	}

	struct SecondaryPoint
	{
		public Vector2 Position;
		public Vector2 Velocity;
		public bool Ready;
	}

	static readonly BoneSpec[] BoneSpecs =
	[
		new("pelvis", "spine_01", 5, Part.Torso, true),
		new("spine_01", "spine_02", 5, Part.Torso, true),
		new("spine_02", "spine_03", 5, Part.Torso, true),
		new("spine_03", "neck_01", 4, Part.Other, true),
		new("neck_01", "Head", 3, Part.Other, true),
		new("spine_03", "clavicle_l", 3, Part.Bicep, true),
		new("clavicle_l", "upperarm_l", 3, Part.Bicep, true),
		new("upperarm_l", "lowerarm_l", 3, Part.Bicep, true),
		new("lowerarm_l", "hand_l", 2.5f, Part.Forearm, true),
		new("hand_l", "middle_01_l", 2.5f, Part.Forearm, true),
		new("spine_03", "clavicle_r", 3, Part.Bicep, true),
		new("clavicle_r", "upperarm_r", 3, Part.Bicep, true),
		new("upperarm_r", "lowerarm_r", 3, Part.Bicep, true),
		new("lowerarm_r", "hand_r", 2.5f, Part.Forearm, true),
		new("hand_r", "middle_01_r", 2.5f, Part.Forearm, true),
		new("pelvis", "thigh_l", 4, Part.Thigh, Leg: LegSide.Left),
		new("thigh_l", "calf_l", 4, Part.Thigh, Leg: LegSide.Left),
		new("calf_l", "foot_l", 3, Part.Other, Leg: LegSide.Left),
		new("foot_l", "ball_l", 2, Part.Other, Leg: LegSide.Left),
		new("ball_l", "ball_leaf_l", 2, Part.Other, Leg: LegSide.Left),
		new("pelvis", "thigh_r", 4, Part.Thigh, Leg: LegSide.Right),
		new("thigh_r", "calf_r", 4, Part.Thigh, Leg: LegSide.Right),
		new("calf_r", "foot_r", 3, Part.Other, Leg: LegSide.Right),
		new("foot_r", "ball_r", 2, Part.Other, Leg: LegSide.Right),
		new("ball_r", "ball_leaf_r", 2, Part.Other, Leg: LegSide.Right),
	];

	public readonly HumanoidRigSettings Settings = configuredSettings ?? new();
	public readonly NeuralSurfaceSettings Neural = configuredNeural ?? new();
	public readonly HumanoidNeuralReactionSettings NeuralReactions = configuredReactions ?? new();
	public float PlaybackSpeed = 1;
	public float PoseModifierWeight = 1;
	public bool GaitActive;
	public Vector2 Offset;
	public Vector2? LookTarget;

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
	BoneUV[] neuralBoneUVs = [];
	Vector2 neuralHeadUV;
	Vector2 neuralHeadUVRadius;
	Vector2 surfaceMaterialOrigin;
	OzzJointMask upperBodyMask = null!;
	OzzJointMask leftArmMask = null!;
	OzzJointMask rightArmMask = null!;
	OzzRig.Clip upperBodyHitClip = null!;
	NeuralSkinRuntime neuralSkin = null!;
	int[] boneOrder = [];
	Leg leftLeg;
	Leg rightLeg;
	LegSide backLeg;
	FootIK leftFoot;
	FootIK rightFoot;
	SecondaryPoint headMotion;
	SecondaryPoint leftHandMotion;
	SecondaryPoint rightHandMotion;
	int head;
	int neck;
	int leftHand;
	int leftPalm;
	int rightHand;
	int rightPalm;
	int pelvis;
	float animationPhase;
	float gaitHeadBias;
	float gaitShoulderBias;
	float gaitStrideBias;
	float plantRemaining;
	LegSide plantedSide;
	float visualAccumulator;
	float visualDelta;
	float pendingLandingSpeed;
	float layeredHitTime = -1;
	float neuralHitRemaining;
	float neuralJostleAt;
	float neuralJostlePhase;
	Signs visualFacing;
	Signs targetFacing;
	float visualYaw;
	float turnFromYaw;
	float turnToYaw;
	float turnTime;
	float turnAmount;
	bool ragdollReady;
	bool turnReady;
	bool turning;
	bool poseDirty;
	Vector2 lookDirection;
	bool eyeReady;
	float eyeScale = 1;
	float eyeReactionRemaining;
	float eyeReactionStrength;

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
	static float LayeredHitDuration => 0.42f;
	static float TrackingLeanDistance => 3f;
	static float HeadMotionSpring => 105f;
	static float HeadMotionDamping => 16f;
	static float HeadMotionMaxOffset => 2.5f;
	static float HandMotionSpring => 65f;
	static float HandMotionDamping => 10f;
	static float HandMotionMaxOffset => 4.5f;
	static float SecondaryTeleportDistance => 24f;
	static float NeuralFootContactDuration => 0.14f;
	static float PlantResponseDuration => 0.2f;
	static float PlantCompression => 1.5f;
	static float PlantShoulderLift => 1.1f;
	static float GaitHeadOffset => 1.1f;
	static float GaitShoulderOffset => 0.8f;
	static float GaitStrideOffset => 1.25f;
	static float TurnInertiaDelay => 0.35f;
	static float EyeTrackingSpeed => 8f;
	static float EyeSaccadeInterval => 0.55f;
	static float EyeSaccadeAmount => 0.12f;
	static float EyeReactionDuration => 0.18f;
	static Point2 NeuralSize => new(64, 80);
	static Point2 NeuralOrigin => new(-32, -72);

	public IReadOnlyList<OzzRig.Clip> Clips => rig.Clips;
	public OzzRig.Clip ActiveClip => animator.ActiveClip;
	public float RangeStart => animator.RangeStart;
	public float RangeEnd => animator.RangeEnd;
	public bool AnimationFinished => animator.Finished;
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
	public Vector2 EyeOffset => projectedPose[head] + lookDirection * 2.4f;
	public Vector2 EyeWorldPosition => owner.Position + EyeOffset;

	public void Initialize(string clip)
	{
		if (Settings.VisualFPS <= 0)
			Settings.VisualFPS = 20;
		rig = RigLibrary.Get("ual2");
		var initialClip = rig.TryGetClip(clip, out _) ? clip : "Zombie_Idle_Loop";
		animator = new(rig, initialClip);
		animationPhase = SeededPhase();
		gaitHeadBias = SeededSigned(1.7f);
		gaitShoulderBias = SeededSigned(4.3f);
		gaitStrideBias = SeededSigned(8.1f);
		if (Settings.SeedAnimationPhase)
			animator.SetTime(animator.ActiveClip.Duration * animationPhase);
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
		neuralBoneUVs = new BoneUV[BoneSpecs.Length];
		boneOrder = new int[bones.Length];
		for (var i = 0; i < bones.Length; i++)
		{
			var spec = BoneSpecs[i];
			bones[i] = new(RequiredJoint(spec.From), RequiredJoint(spec.To), spec.Width, spec.Part, spec.Combat, spec.Leg);
			ragdollJoints[bones[i].From] = true;
			ragdollJoints[bones[i].To] = true;
			boneOrder[i] = i;
		}
		head = RequiredJoint("Head");
		neck = RequiredJoint("neck_01");
		leftHand = RequiredJoint("hand_l");
		leftPalm = RequiredJoint("middle_01_l");
		rightHand = RequiredJoint("hand_r");
		rightPalm = RequiredJoint("middle_01_r");
		pelvis = RequiredJoint("pelvis");
		leftLeg = RequiredLeg("l");
		rightLeg = RequiredLeg("r");
		upperBodyMask = rig.CreateMask();
		upperBodyMask.Set("spine_01", 1, descendants: true);
		upperBodyMask.Set("spine_01", 0.25f);
		upperBodyMask.Set("spine_02", 0.6f);
		leftArmMask = rig.CreateMask();
		leftArmMask.Set("clavicle_l", 1, descendants: true);
		rightArmMask = rig.CreateMask();
		rightArmMask.Set("clavicle_r", 1, descendants: true);
		upperBodyHitClip = rig.GetClip("Hit_Knockback_Additive");
		ResetTurn();
		visualDelta = 1 / Settings.VisualFPS;
		UpdatePose(grounded: false, footIK: false);
		CaptureNeuralUVs();
		surfaceMaterialOrigin = owner.Position;
		neuralJostlePhase = (owner.ActorId?.GetHashCode() ?? owner.Position.GetHashCode()) * 0.0001f;
		neuralJostleAt = owner.Time.SecondsF + NeuralReactions.NeuralJostleInterval * 0.5f;
		neuralSkin = new(owner, Neural, NeuralSize, NeuralOrigin, 65);
		neuralSkin.Initialize(Settings.SkinMode);
	}

	int RequiredJoint(string name)
	{
		var index = rig.FindJoint(name);
		return index >= 0 ? index : throw new InvalidDataException($"UAL2 rig is missing joint '{name}'.");
	}

	float SeededPhase()
	{
		var hash = 2166136261u;
		if (!string.IsNullOrEmpty(owner.ActorId))
			for (var i = 0; i < owner.ActorId.Length; i++)
				hash = unchecked((hash ^ owner.ActorId[i]) * 16777619u);
		else
		{
			hash = unchecked((hash ^ (uint)owner.Position.X) * 16777619u);
			hash = unchecked((hash ^ (uint)owner.Position.Y) * 16777619u);
		}
		return (hash & 0xffff) / 65536f;
	}

	float SeededSigned(float salt) => MathF.Sin((animationPhase + salt) * 12.9898f) * 0.5f +
		MathF.Sin((animationPhase + salt) * 31.416f) * 0.5f;

	Leg RequiredLeg(string side)
		=> new(RequiredJoint($"thigh_{side}"), RequiredJoint($"calf_{side}"),
			RequiredJoint($"foot_{side}"), RequiredJoint($"ball_{side}"), RequiredJoint($"ball_leaf_{side}"));

	public bool TryGetClip(string name, out OzzRig.Clip clip) => rig.TryGetClip(name, out clip);
	public OzzRig.Clip GetClip(string name) => rig.GetClip(name);

	public void Play(string name, bool blend = true, bool restart = false, bool seededPhase = false,
		float blendDuration = -1)
	{
		var startTime = seededPhase && !string.Equals(animator.ActiveClip.Name, name, StringComparison.OrdinalIgnoreCase)
			? rig.GetClip(name).Duration * animationPhase : 0;
		var transition = blend ? (blendDuration >= 0 ? blendDuration : ClipBlendDuration) : 0;
		animator.Play(name, transition, restart, startTime);
	}

	public void Play(RigTake take, float blendDuration = -1, bool restart = true)
	{
		var clip = rig.GetClip(take.Clip);
		var start = Math.Clamp(take.Start, 0, clip.Duration);
		var end = Math.Clamp(take.End, start, clip.Duration);
		PlaybackSpeed = Math.Max(0.01f, take.Speed);
		animator.Play(take.Clip, start, end, blendDuration >= 0 ? blendDuration : ClipBlendDuration, restart);
	}

	public void Update(bool grounded, bool footIK, bool loop = true, bool advance = true)
	{
		EnsureState();
		neuralSkin.Update(Settings.SkinMode);
		var step = 1 / Settings.VisualFPS;
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
		plantRemaining = 0;
	}

	public void Land(float speed)
	{
		if (speed >= LandingMinSpeed)
			pendingLandingSpeed = MathF.Max(pendingLandingSpeed, speed);
	}

	public void Hit(float away, bool animateUpperBody = false)
	{
		neuralHitRemaining = 0.18f;
		TriggerEyeReaction(0.45f);
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
		if (Settings.VisualFPS <= 0)
			Settings.VisualFPS = 20;
		if (!turnReady)
			ResetTurn();
		else if (!turning && visualFacing == owner.Facing)
		{
			targetFacing = owner.Facing;
			visualYaw = FacingYaw(visualFacing);
		}
	}

	float FacingYaw(Signs facing)
		=> facing == Signs.Negative ? 0 : Settings.ProjectionYaw >= 0 ? -MathF.PI : MathF.PI;

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
		if (!turning || !grounded || !Settings.TurnWhileMoving && MathF.Abs(owner.Velocity.X) > TurnStartSpeed)
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
		ApplyGaitAsymmetry();
		ApplyPlantResponse();
		UpdateSecondaryMotion();
		if (footIK)
		{
			UpdateFoot(ref leftFoot, leftLeg, LegSide.Left, grounded);
			UpdateFoot(ref rightFoot, rightLeg, LegSide.Right, grounded);
		}
		else
			ResetFeet();
		SortBones();
		UpdateEye();
	}

	void UpdateEye()
	{
		var desired = LookTarget is { } target
			? (target - owner.Position - projectedPose[head]).Normalized()
			: new Vector2(-MathF.Cos(visualYaw), 0);
		if (Settings.EyeMotion > 0)
		{
			var tick = (uint)MathF.Floor((owner.Time.SecondsF + animationPhase * EyeSaccadeInterval) /
				EyeSaccadeInterval);
			var seed = (uint)(animationPhase * uint.MaxValue);
			desired = (desired + new Vector2(HashSigned(tick ^ seed), HashSigned((tick + 1) ^ seed)) *
				EyeSaccadeAmount * Settings.EyeMotion).Normalized(desired);
		}
		if (!eyeReady || Settings.EyeMotion <= 0)
		{
			lookDirection = desired;
			eyeReady = true;
		}
		else
			lookDirection = Vector2.Lerp(lookDirection, desired,
				1 - MathF.Exp(-EyeTrackingSpeed * visualDelta)).Normalized(desired);

		if (eyeReactionRemaining > 0)
		{
			var progress = 1 - eyeReactionRemaining / EyeReactionDuration;
			eyeScale = 1 - MathF.Sin(progress * MathF.PI) * eyeReactionStrength * Settings.EyeMotion;
			eyeReactionRemaining = MathF.Max(0, eyeReactionRemaining - visualDelta);
		}
		else
		{
			eyeScale = 1;
			eyeReactionStrength = 0;
		}
	}

	static float HashSigned(uint value)
	{
		value ^= value >> 16;
		value *= 0x7feb352du;
		value ^= value >> 15;
		value *= 0x846ca68bu;
		value ^= value >> 16;
		return (value & 0xffff) / 32767.5f - 1;
	}

	void TriggerEyeReaction(float strength)
	{
		eyeReactionRemaining = EyeReactionDuration;
		eyeReactionStrength = MathF.Max(eyeReactionStrength, strength);
	}

	void ApplyGaitAsymmetry()
	{
		var amount = Settings.GaitAsymmetry * PoseModifierWeight;
		if (amount <= 0)
			return;

		projectedPose[head] += Vector2.UnitX * gaitHeadBias * GaitHeadOffset * amount;
		OffsetMasked(leftArmMask, Vector2.UnitY * gaitShoulderBias * GaitShoulderOffset * amount);
		OffsetMasked(rightArmMask, -Vector2.UnitY * gaitShoulderBias * GaitShoulderOffset * amount);
		if (!GaitActive || animator.ActiveClip.Duration <= 0)
			return;

		var stride = MathF.Sin(animator.Time / animator.ActiveClip.Duration * MathF.Tau) * gaitStrideBias *
			GaitStrideOffset * amount * (int)visualFacing;
		OffsetLeg(leftLeg, Vector2.UnitX * stride);
		OffsetLeg(rightLeg, -Vector2.UnitX * stride * 0.65f);
	}

	void ApplyPlantResponse()
	{
		if (plantRemaining <= 0)
			return;
		var progress = 1 - plantRemaining / PlantResponseDuration;
		var amount = MathF.Sin(progress * MathF.PI) * Settings.PlantResponse * PoseModifierWeight;
		plantRemaining = MathF.Max(0, plantRemaining - visualDelta);
		projectedPose[pelvis] += Vector2.UnitY * PlantCompression * amount;
		OffsetMasked(upperBodyMask, Vector2.UnitY * PlantCompression * amount);
		OffsetMasked(plantedSide == LegSide.Left ? rightArmMask : leftArmMask,
			-Vector2.UnitY * PlantShoulderLift * amount);
	}

	void OffsetMasked(OzzJointMask mask, Vector2 offset)
	{
		for (var i = 0; i < projectedPose.Length; i++)
			projectedPose[i] += offset * mask[i];
	}

	void OffsetLeg(Leg leg, Vector2 offset)
	{
		projectedPose[leg.Knee] += offset * 0.35f;
		projectedPose[leg.Ankle] += offset;
		projectedPose[leg.Toe] += offset;
		projectedPose[leg.Tip] += offset;
	}

	void UpdateSecondaryMotion()
	{
		var secondaryMotion = Settings.SecondaryMotion * PoseModifierWeight;
		var headOffset = UpdateSecondaryPoint(ref headMotion, owner.Position + projectedPose[head],
			HeadMotionSpring, HeadMotionDamping, HeadMotionMaxOffset) * secondaryMotion;
		var leftOffset = UpdateSecondaryPoint(ref leftHandMotion, owner.Position + projectedPose[leftHand],
			HandMotionSpring, HandMotionDamping, HandMotionMaxOffset) * secondaryMotion;
		var rightOffset = UpdateSecondaryPoint(ref rightHandMotion, owner.Position + projectedPose[rightHand],
			HandMotionSpring, HandMotionDamping, HandMotionMaxOffset) * secondaryMotion;
		projectedPose[neck] += headOffset * 0.25f;
		projectedPose[head] += headOffset;
		projectedPose[leftHand] += leftOffset;
		projectedPose[leftPalm] += leftOffset;
		projectedPose[rightHand] += rightOffset;
		projectedPose[rightPalm] += rightOffset;
	}

	Vector2 UpdateSecondaryPoint(ref SecondaryPoint point, Vector2 target, float spring, float damping, float maxOffset)
	{
		if (!point.Ready || Vector2.DistanceSquared(point.Position, target) > SecondaryTeleportDistance * SecondaryTeleportDistance)
		{
			point.Position = target;
			point.Velocity = Vector2.Zero;
			point.Ready = true;
			return Vector2.Zero;
		}

		var dt = MathF.Min(visualDelta, 1 / 30f);
		point.Velocity += (target - point.Position) * spring * dt;
		point.Velocity *= MathF.Exp(-damping * dt);
		point.Position += point.Velocity * dt;
		var offset = point.Position - target;
		if (offset.LengthSquared() <= maxOffset * maxOffset)
			return offset;
		var direction = Vector2.Normalize(offset);
		point.Position = target + direction * maxOffset;
		point.Velocity -= direction * MathF.Max(0, Vector2.Dot(point.Velocity, direction));
		return direction * maxOffset;
	}

	void UpdateAnimationLayers()
	{
		neuralHitRemaining = MathF.Max(0, neuralHitRemaining - visualDelta);
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
			MathF.Sin(progress * MathF.PI) * Settings.UpperBodyHitStrength, upperBodyMask);
	}

	public void PerturbNeuralTexture(NeuralTextureActor.PerturbContext context)
	{
		if (Settings.SkinMode != RigSkinMode.NCA || context.Texture != neuralSkin.Texture)
			return;
		var center = (Vector2)owner.Position + (projectedPose[pelvis] + projectedPose[head]) * 0.5f;
		if (neuralHitRemaining > 0)
			context.AddStamp(center, center, 7, NeuralReactions.NeuralHitStrength);
		AddFootContactStamp(context, leftFoot);
		AddFootContactStamp(context, rightFoot);

		var now = owner.Time.SecondsF;
		if (NeuralReactions.NeuralJostleInterval <= 0 || NeuralReactions.NeuralJostleStrength <= 0 ||
			NeuralReactions.NeuralJostleRadius <= 0 || now < neuralJostleAt)
			return;
		neuralJostleAt = now + NeuralReactions.NeuralJostleInterval;
		var phase = now * 2.3f + neuralJostlePhase;
		var offset = new Vector2(MathF.Sin(phase) * 8, MathF.Cos(phase * 0.73f) * 14);
		context.AddStamp(center + offset, center + offset, NeuralReactions.NeuralJostleRadius,
			NeuralReactions.NeuralJostleStrength);
	}

	void AddFootContactStamp(NeuralTextureActor.PerturbContext context, FootIK foot)
	{
		if (foot.ContactRemaining > 0 && NeuralReactions.NeuralFootContactStrength > 0 &&
			NeuralReactions.NeuralFootContactRadius > 0)
			context.AddStamp(foot.ContactPosition, foot.ContactPosition, NeuralReactions.NeuralFootContactRadius,
				NeuralReactions.NeuralFootContactStrength *
				Math.Clamp(foot.ContactRemaining / NeuralFootContactDuration, 0, 1));
	}

	void ApplyTrackingLean()
	{
		var amount = Settings.TrackingLean * PoseModifierWeight;
		if (amount <= 0 || LookTarget is not { } target)
			return;
		var toward = target - owner.Position;
		var screenOffset = new Vector2(Math.Clamp(toward.X / 64f, -1, 1),
			Math.Clamp(toward.Y / 96f, -0.5f, 0.5f)) * TrackingLeanDistance * amount;
		var rigOffset = RigVelocity(screenOffset);
		for (var i = 0; i < sampledPose.Length; i++)
			sampledPose[i] += rigOffset * upperBodyMask[i];
	}

	public void Dispose()
	{
		animator?.Dispose();
		neuralSkin?.Dispose();
	}

	void ProjectPose()
	{
		var angle = Settings.ProjectionYaw * MathF.PI / 180f;
		var cos = MathF.Cos(angle);
		var sin = MathF.Sin(angle);
		var upperBodyYaw = visualYaw;
		var inertiaActive = turning && Settings.TurnInertia > 0;
		if (inertiaActive)
		{
			var delay = TurnInertiaDelay * Math.Clamp(Settings.TurnInertia, 0, 1);
			var progress = Math.Clamp((turnTime / TurnDuration - delay) / (1 - delay), 0, 1);
			progress = progress * progress * (3 - 2 * progress);
			upperBodyYaw = float.Lerp(turnFromYaw, turnToYaw, progress);
		}
		var pivot = ragdollPose[pelvis];
		var pivotX = -pivot.Z * cos + pivot.X * sin;
		var mirror = turning
			? float.Lerp(-(int)visualFacing, -(int)targetFacing, turnAmount)
			: -(int)visualFacing;
		var turnCos = MathF.Cos(visualYaw);
		var turnSin = MathF.Sin(visualYaw);
		var upperTurnCos = MathF.Cos(upperBodyYaw);
		var upperTurnSin = MathF.Sin(upperBodyYaw);
		for (var i = 0; i < sampledPose.Length; i++)
		{
			var joint = ragdollJoints[i] ? ragdollPose[i] : sampledPose[i];
			var offset = joint - pivot;
			var weight = inertiaActive ? upperBodyMask[i] : 0;
			var jointYaw = float.Lerp(visualYaw, upperBodyYaw, weight);
			var jointTurnCos = weight <= 0 ? turnCos : weight >= 1 ? upperTurnCos : MathF.Cos(jointYaw);
			var jointTurnSin = weight <= 0 ? turnSin : weight >= 1 ? upperTurnSin : MathF.Sin(jointYaw);
			var turned = new Vector3(offset.X * jointTurnCos + offset.Z * jointTurnSin, offset.Y,
				-offset.X * jointTurnSin + offset.Z * jointTurnCos);
			projectedPose[i] = new Vector2(pivotX * mirror - turned.Z * cos + turned.X * sin, -joint.Y) *
				Settings.PixelsPerMeter + new Vector2(0, 2.5f) + Offset;
			jointDepth[i] = joint.X * cos + joint.Z * sin;
		}
		backLeg = LegDepth(leftLeg) < LegDepth(rightLeg) ? LegSide.Left : LegSide.Right;
	}

	float LegDepth(Leg leg)
		=> (jointDepth[leg.Hip] + jointDepth[leg.Knee] + jointDepth[leg.Ankle] +
			jointDepth[leg.Toe] + jointDepth[leg.Tip]) / 5;

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
			var impactSpeed = pendingLandingSpeed / Settings.PixelsPerMeter;
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
		var angle = Settings.ProjectionYaw * MathF.PI / 180f + visualYaw;
		var alongProjection = screenVelocity.X / Settings.PixelsPerMeter;
		return new(MathF.Sin(angle) * alongProjection, -screenVelocity.Y / Settings.PixelsPerMeter,
			-MathF.Cos(angle) * alongProjection);
	}

	void UpdateFoot(ref FootIK plant, Leg leg, LegSide side, bool grounded)
	{
		var wasPlanted = plant.Planted;
		plant.ContactRemaining = MathF.Max(0, plant.ContactRemaining - visualDelta);
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
		{
			projectedPose[leg.Tip] = projectedPose[leg.Toe] + Vector2.UnitX * (int)visualFacing *
				Vector2.Distance(projectedPose[leg.Toe], projectedPose[leg.Tip]);
			if (!wasPlanted)
			{
				plant.ContactRemaining = NeuralFootContactDuration;
				plant.ContactPosition = owner.Position + projectedPose[leg.Toe];
				plantRemaining = PlantResponseDuration;
				plantedSide = side;
				TriggerEyeReaction(0.18f);
			}
		}
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

	float BoneDepth(Bone bone) => (jointDepth[bone.From] + jointDepth[bone.To]) * 0.5f;

	Color BoneColor(Bone bone, float depth)
		=> bone.From == pelvis || bone.To == pelvis
			? Settings.CoreColor
			: Color.Lerp(Settings.FarColor, Settings.NearColor, depth);

	float BoneWidth(Bone bone) => bone.Part switch
	{
		Part.Torso => Settings.TorsoSize,
		Part.Bicep => Settings.BicepSize,
		Part.Forearm => Settings.ForearmSize,
		Part.Thigh => Settings.ThighSize,
		_ => bone.Width,
	};

	float BoneRenderDepth(Bone bone)
		=> Math.Clamp(BoneDepth(bone) * MathF.Max(0, Settings.DepthColorContrast) + 0.5f, 0, 1);

	void BoneWidths(Bone bone, out float fromWidth, out float toWidth)
	{
		var depthScale = 1 + MathF.Max(0, BoneRenderDepth(bone) - 0.5f) * 2 * Settings.DepthWidthAmount;
		fromWidth = BoneWidth(bone) * depthScale;
		toWidth = bone.Leg != LegSide.None || bone.Part is Part.Bicep or Part.Forearm
			? fromWidth * (1 - Math.Clamp(Settings.LimbTaper, 0, 0.75f))
			: fromWidth;
	}

	void CaptureNeuralUVs()
	{
		for (var i = 0; i < bones.Length; i++)
		{
			var bone = bones[i];
			BoneWidths(bone, out var fromWidth, out var toWidth);
			BoneQuad(projectedPose[bone.From], projectedPose[bone.To], fromWidth, toWidth,
				out var a, out var b, out var c, out var d);
			neuralBoneUVs[i] = new(NeuralUV(a), NeuralUV(b), NeuralUV(c), NeuralUV(d));
		}
		neuralHeadUV = NeuralUV(projectedPose[head]);
		neuralHeadUVRadius = new Vector2(Settings.HeadSize) / (Vector2)NeuralSize;
	}

	public bool OverlapsCombatBounds(in RectInt worldBounds)
	{
		var bounds = (Rect)(worldBounds - owner.Position);
		for (var i = 0; i < bones.Length; i++)
		{
			var bone = bones[i];
			BoneWidths(bone, out var fromWidth, out var toWidth);
			if (bone.Combat && CapsuleOverlaps(bounds, projectedPose[bone.From], projectedPose[bone.To],
				MathF.Max(fromWidth, toWidth) * 0.5f + Settings.CombatBoundsPadding))
				return true;
		}
		return bounds.Overlaps(new Circle(projectedPose[head], Settings.HeadSize + Settings.CombatBoundsPadding), out _);
	}

	static bool CapsuleOverlaps(in Rect bounds, Vector2 from, Vector2 to, float radius)
	{
		var segment = new Line(from, to);
		if (bounds.Overlaps(segment))
			return true;
		var radiusSquared = radius * radius;
		return segment.ClosestDistanceSquared(bounds.TopLine) <= radiusSquared ||
			segment.ClosestDistanceSquared(bounds.RightLine) <= radiusSquared ||
			segment.ClosestDistanceSquared(bounds.BottomLine) <= radiusSquared ||
			segment.ClosestDistanceSquared(bounds.LeftLine) <= radiusSquared;
	}

	public void DrawCombatBounds(Batcher batcher)
	{
		var color = new Color(0x39e6c5);
		for (var i = 0; i < bones.Length; i++)
		{
			var bone = bones[i];
			if (!bone.Combat)
				continue;
			var from = owner.Position + projectedPose[bone.From];
			var to = owner.Position + projectedPose[bone.To];
			BoneWidths(bone, out var fromWidth, out var toWidth);
			var radius = MathF.Max(fromWidth, toWidth) * 0.5f + Settings.CombatBoundsPadding;
			batcher.Line(from, to, radius * 2, color * 0.2f);
			batcher.CircleLine(from, radius, 1, 10, color);
			batcher.CircleLine(to, radius, 1, 10, color);
		}
		batcher.CircleLine(owner.Position + projectedPose[head], Settings.HeadSize + Settings.CombatBoundsPadding,
			1, 14, color);
	}

	public void Draw(Batcher batcher, Color tint, bool silhouette) => Draw(batcher, tint, silhouette, DrawParts.All);
	public void DrawBehindPlayer(Batcher batcher, Color tint, bool silhouette)
		=> Draw(batcher, tint, silhouette, DrawParts.BehindPlayer);
	public void DrawInFrontOfPlayer(Batcher batcher, Color tint, bool silhouette)
		=> Draw(batcher, tint, silhouette, DrawParts.InFrontOfPlayer);

	bool Includes(Bone bone, DrawParts parts)
		=> parts switch
		{
			DrawParts.All => true,
			DrawParts.BehindPlayer => bone.Leg == backLeg,
			_ => bone.Leg != backLeg,
		};

	void Draw(Batcher batcher, Color tint, bool silhouette, DrawParts parts)
	{
		var surface = !silhouette && Settings.SkinMode == RigSkinMode.Surface
			? Assets.GetSurface(Settings.SurfaceId) : null;
		var neuralTexture = !silhouette && Settings.SkinMode == RigSkinMode.NCA && Neural.NeuralColorAmount > 0
			? neuralSkin.OutputTexture : null;
		if (neuralTexture != null)
			batcher.PushSampler(new(TextureFilter.Nearest, TextureWrap.Clamp));
		for (var sortedIndex = 0; sortedIndex < boneOrder.Length; sortedIndex++)
		{
			var bone = bones[boneOrder[sortedIndex]];
			if (!Includes(bone, parts))
				continue;
			var from = projectedPose[bone.From];
			var to = projectedPose[bone.To];
			BoneWidths(bone, out var fromWidth, out var toWidth);
			if (silhouette)
			{
				DrawBoneQuad(batcher, from, to, fromWidth + 2, toWidth + 2, tint);
				continue;
			}

			var depth = BoneRenderDepth(bone);
			var color = BoneColor(bone, depth);
			DrawBoneQuad(batcher, from, to, fromWidth + 2, toWidth + 2, Settings.OutlineColor);
			DrawBoneQuad(batcher, from, to, fromWidth, toWidth, color);
			if (surface != null)
				DrawSurfaceBone(batcher, surface, from, to, fromWidth, toWidth,
					neuralBoneUVs[boneOrder[sortedIndex]], normals: false);
			else if (neuralTexture != null)
				DrawNeuralBone(batcher, neuralTexture, from, to, fromWidth, toWidth,
					neuralBoneUVs[boneOrder[sortedIndex]], color * Neural.NeuralColorAmount);
			batcher.Circle(to, MathF.Max(1.2f, toWidth * 0.42f), 8, color);
		}

		if (parts != DrawParts.BehindPlayer)
		{
			var headPosition = projectedPose[head];
			batcher.Circle(headPosition, Settings.HeadSize + 1.4f, 14,
				silhouette ? tint : Settings.OutlineColor);
			batcher.Circle(headPosition, Settings.HeadSize, 14, silhouette ? tint : Settings.NearColor);
			if (surface != null)
				DrawSurfaceCircle(batcher, surface, headPosition, Settings.HeadSize,
					neuralHeadUV, neuralHeadUVRadius, normals: false);
			else if (neuralTexture != null)
				DrawNeuralCircle(batcher, neuralTexture, headPosition, Settings.HeadSize,
					neuralHeadUV, neuralHeadUVRadius, Settings.NearColor * Neural.NeuralColorAmount);
			if (!silhouette)
				batcher.Circle(headPosition + lookDirection * 2.4f, 1.1f * eyeScale, 8, Settings.AccentColor);
		}
		if (neuralTexture != null)
			batcher.PopSampler();
	}

	public void DrawNormals(Batcher batcher) => DrawNormals(batcher, DrawParts.All);
	public void DrawNormalsBehindPlayer(Batcher batcher) => DrawNormals(batcher, DrawParts.BehindPlayer);
	public void DrawNormalsInFrontOfPlayer(Batcher batcher) => DrawNormals(batcher, DrawParts.InFrontOfPlayer);

	void DrawNormals(Batcher batcher, DrawParts parts)
	{
		var flat = new Color(0x8080ff);
		var surface = Settings.SkinMode == RigSkinMode.Surface &&
			Assets.GetSurface(Settings.SurfaceId) is { HasNormal: true } foundSurface
			? foundSurface : null;
		var neuralNormals = Settings.SkinMode == RigSkinMode.NCA && Neural.NeuralNormalAmount > 0
			? neuralSkin.OutputNormalTexture : null;
		if (neuralNormals != null)
			batcher.PushSampler(new(TextureFilter.Nearest, TextureWrap.Clamp));
		for (var sortedIndex = 0; sortedIndex < boneOrder.Length; sortedIndex++)
		{
			var bone = bones[boneOrder[sortedIndex]];
			if (!Includes(bone, parts))
				continue;
			var from = projectedPose[bone.From];
			var to = projectedPose[bone.To];
			BoneWidths(bone, out var fromWidth, out var toWidth);
			DrawBoneQuad(batcher, from, to, fromWidth + 2, toWidth + 2, flat);
			if (surface != null)
				DrawSurfaceBone(batcher, surface, from, to, fromWidth, toWidth,
					neuralBoneUVs[boneOrder[sortedIndex]], normals: true);
			else if (neuralNormals != null)
				DrawNeuralBone(batcher, neuralNormals, from, to, fromWidth, toWidth,
					neuralBoneUVs[boneOrder[sortedIndex]], Color.White * Neural.NeuralNormalAmount);
		}
		if (parts != DrawParts.BehindPlayer)
		{
			var headPosition = projectedPose[head];
			batcher.Circle(headPosition, Settings.HeadSize + 1.4f, 14, flat);
			if (surface != null)
				DrawSurfaceCircle(batcher, surface, headPosition, Settings.HeadSize,
					neuralHeadUV, neuralHeadUVRadius, normals: true);
			else if (neuralNormals != null)
				DrawNeuralCircle(batcher, neuralNormals, headPosition, Settings.HeadSize,
					neuralHeadUV, neuralHeadUVRadius, Color.White * Neural.NeuralNormalAmount);
		}
		if (neuralNormals != null)
			batcher.PopSampler();
	}

	static void BoneQuad(Vector2 from, Vector2 to, float fromWidth, float toWidth,
		out Vector2 a, out Vector2 b, out Vector2 c, out Vector2 d)
	{
		var along = (to - from).Normalized(Vector2.UnitY);
		var across = along.TurnLeft();
		a = from + across * fromWidth * 0.5f;
		b = from - across * fromWidth * 0.5f;
		c = to - across * toWidth * 0.5f;
		d = to + across * toWidth * 0.5f;
	}

	static void DrawBoneQuad(Batcher batcher, Vector2 from, Vector2 to, float fromWidth, float toWidth, Color color)
	{
		BoneQuad(from, to, fromWidth, toWidth, out var a, out var b, out var c, out var d);
		batcher.Quad(a, b, c, d, color);
	}

	static void DrawNeuralBone(Batcher batcher, Texture texture, Vector2 from, Vector2 to,
		float fromWidth, float toWidth, BoneUV uv, Color color)
	{
		BoneQuad(from, to, fromWidth, toWidth, out var a, out var b, out var c, out var d);
		batcher.Quad(texture, a, b, c, d, uv.A, uv.B, uv.C, uv.D, color);
	}

	void DrawSurfaceBone(Batcher batcher, Surface surface, Vector2 from, Vector2 to,
		float fromWidth, float toWidth, BoneUV uv, bool normals)
	{
		BoneQuad(from, to, fromWidth, toWidth, out var a, out var b, out var c, out var d);
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

	Vector2 SurfaceWorld(Vector2 uv)
		=> surfaceMaterialOrigin + uv * (Vector2)NeuralSize + (Vector2)NeuralOrigin;

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
}
