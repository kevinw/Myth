using System.Numerics;
using Foster.Framework;

namespace Myth;

public enum RigSkinMode { Solid, Surface, NCA }

interface IHumanoidRigAccessoryVisual
{
	int DrawItemCount { get; }
	float GetDrawItemDepth(int index);
	void DrawItem(Batcher batcher, int index, Color? tint);
}

internal sealed class HumanoidRigVisual(Actor owner, HumanoidRigSettings? configuredSettings = null,
	NeuralSurfaceSettings? configuredNeural = null, HumanoidNeuralReactionSettings? configuredReactions = null,
	HumanoidRigWireSettings? configuredWires = null,
	IReadOnlyList<HumanoidRigWireSpec>? configuredWireLayout = null) : IDisposable
{
	public readonly record struct RuffProjection(Vector3 Pivot, float PivotX, float Mirror,
		float TurnCos, float TurnSin, float CameraCos, float CameraSin, float PixelsPerMeter,
		Vector2 ScreenOffset, Vector2 Correction)
	{
		public Vector2 Project(Vector3 point, out float depth)
		{
			var offset = point - Pivot;
			var turned = new Vector3(offset.X * TurnCos + offset.Z * TurnSin, offset.Y,
				-offset.X * TurnSin + offset.Z * TurnCos);
			depth = CameraOpenDepth(point, Pivot, TurnCos, TurnSin, CameraCos, CameraSin);
			return new Vector2(PivotX * Mirror - turned.Z * CameraCos + turned.X * CameraSin, -point.Y) *
				PixelsPerMeter + ScreenOffset + Correction;
		}
	}

	public readonly record struct RuffPose(Vector3 Neck, Vector3 Head,
		Vector3 LeftUpperArm, Vector3 LeftLowerArm, Vector3 RightUpperArm, Vector3 RightLowerArm,
		RuffProjection Projection);

	// Left/right facing is a 2D presentation choice, not permission to show the rig's back.
	// Fold sorting yaw across 90 degrees while leaving screen projection free to use the full yaw.
	static float CameraOpenDepth(Vector3 point, Vector3 pivot, float turnCos, float turnSin,
		float cameraCos, float cameraSin)
	{
		var offset = point - pivot;
		turnCos = MathF.Abs(turnCos);
		var turnedPoint = pivot + new Vector3(offset.X * turnCos + offset.Z * turnSin, offset.Y,
			-offset.X * turnSin + offset.Z * turnCos);
		return turnedPoint.X * cameraCos + turnedPoint.Z * cameraSin;
	}

	enum Part { Torso, Bicep, Forearm, Hand, Thigh, Other }
	enum Side { None, Left, Right }
	enum DrawParts { All, BehindPlayer, InFrontOfPlayer }

	readonly record struct BoneSpec(string From, string To, float Width, Part Part, bool Combat = false,
		Side Leg = Side.None, Side Arm = Side.None);
	readonly record struct Bone(int From, int To, float Width, Part Part, bool Combat, Side Leg, Side Arm, int Parent = -1);
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

	readonly record struct RigWire(int From, int To, float Bend, float DepthOffset, float WidthScale, uint Seed);

	struct WireMotion
	{
		public Vector2 Control;
		public Vector2 Velocity;
		public bool Ready;
	}

	enum ImpulseGroup { Head, Torso, LeftArm, RightArm, LeftHand, RightHand, LeftLeg, RightLeg }

	struct PendingImpulse
	{
		public ImpulseGroup Group;
		public Vector2 Velocity;
		public float Delay;
		public bool Active;
	}

	static readonly BoneSpec[] BoneSpecs =
	[
		new("pelvis", "spine_01", 5, Part.Torso, true),
		new("spine_01", "spine_02", 5, Part.Torso, true),
		new("spine_02", "spine_03", 5, Part.Torso, true),
		new("spine_03", "neck_01", 4, Part.Other, true),
		new("neck_01", "Head", 3, Part.Other, true),
		new("spine_03", "clavicle_l", 3, Part.Bicep, true, Arm: Side.Left),
		new("clavicle_l", "upperarm_l", 3, Part.Bicep, true, Arm: Side.Left),
		new("upperarm_l", "lowerarm_l", 3, Part.Bicep, true, Arm: Side.Left),
		new("lowerarm_l", "hand_l", 2.5f, Part.Forearm, true, Arm: Side.Left),
		new("hand_l", "middle_01_l", 2.5f, Part.Hand, true, Arm: Side.Left),
		new("spine_03", "clavicle_r", 3, Part.Bicep, true, Arm: Side.Right),
		new("clavicle_r", "upperarm_r", 3, Part.Bicep, true, Arm: Side.Right),
		new("upperarm_r", "lowerarm_r", 3, Part.Bicep, true, Arm: Side.Right),
		new("lowerarm_r", "hand_r", 2.5f, Part.Forearm, true, Arm: Side.Right),
		new("hand_r", "middle_01_r", 2.5f, Part.Hand, true, Arm: Side.Right),
		new("pelvis", "thigh_l", 4, Part.Thigh, Leg: Side.Left),
		new("thigh_l", "calf_l", 4, Part.Thigh, Leg: Side.Left),
		new("calf_l", "foot_l", 3, Part.Other, Leg: Side.Left),
		new("foot_l", "ball_l", 2, Part.Other, Leg: Side.Left),
		new("ball_l", "ball_leaf_l", 2, Part.Other, Leg: Side.Left),
		new("pelvis", "thigh_r", 4, Part.Thigh, Leg: Side.Right),
		new("thigh_r", "calf_r", 4, Part.Thigh, Leg: Side.Right),
		new("calf_r", "foot_r", 3, Part.Other, Leg: Side.Right),
		new("foot_r", "ball_r", 2, Part.Other, Leg: Side.Right),
		new("ball_r", "ball_leaf_r", 2, Part.Other, Leg: Side.Right),
	];

	public readonly HumanoidRigSettings Settings = configuredSettings ?? new();
	public readonly NeuralSurfaceSettings Neural = configuredNeural ?? new();
	public readonly HumanoidNeuralReactionSettings NeuralReactions = configuredReactions ?? new();
	public readonly HumanoidRigWireSettings Wires = configuredWires ?? new();
	public float PlaybackSpeed = 1;
	public float PoseModifierWeight = 1;
	public Color HeadTint = Color.White;
	public bool GaitActive;
	public bool BackArmCombatEnabled = true;
	public Action<Vector2>? Footstep;
	Vector2 offset;
	public Vector2 Offset
	{
		get => offset;
		set
		{
			var delta = value - offset;
			offset = value;
			if (delta == Vector2.Zero)
				return;
			for (var i = 0; i < projectedPose.Length; i++)
				projectedPose[i] += delta;
		}
	}
	public Vector2? LookTarget;
	public float LookPoseWeight;
	public float? YawOverride;
	public float YawOverrideWeight;

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
	RigWire[] wires = [];
	WireMotion[] wireMotion = [];
	int[] wireOrder = [];
	BoneUV[] neuralBoneUVs = [];
	Vector2 neuralHeadUV;
	Vector2 neuralHeadUVRadius;
	Vector2 surfaceMaterialOrigin;
	readonly PendingImpulse[] pendingImpulses = new PendingImpulse[16];
	OzzJointMask upperBodyMask = null!;
	OzzJointMask leftArmMask = null!;
	OzzJointMask rightArmMask = null!;
	OzzRig.Clip upperBodyHitClip = null!;
	NeuralSkinRuntime neuralSkin = null!;
	Sprite? headSprite;
	int[] boneOrder = [];
	Leg leftLeg;
	Leg rightLeg;
	Side backLeg;
	Side backArm;
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
	int leftUpperArm;
	int leftLowerArm;
	int rightUpperArm;
	int rightLowerArm;
	int spine1;
	int spine2;
	int spine3;
	int leftClavicle;
	int rightClavicle;
	int pelvis;
	float animationPhase;
	float gaitHeadBias;
	float gaitShoulderBias;
	float gaitStrideBias;
	float plantRemaining;
	Side plantedSide;
	Side gaitStepSide;
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
	float lookPoseAmount;
	bool eyeReady;
	static float VisualEyeScale => 2.0f;
	float eyeScale = 1;
	float upperBodyLeanDegrees;
	float targetUpperBodyLeanDegrees;
	float armWidthScale = 1;
	float targetArmWidthScale = 1;
	float handWidthScale = 1;
	float targetHandWidthScale = 1;
	float runtimeHandWidthScale = 1;
	bool scaleLeftHandOnly;
	float eyeReactionRemaining;
	float eyeReactionStrength;
	float freezeRemaining;
	float? lockedAnimationYaw;
	float fingerSpinAngle;
	float fingerSpinSpeed;
	bool fingerSpinTarget;
	bool spinLeftHandOnly;

	static float ClipBlendDuration => 0.2f;
	static float TakeModifierResponse => 12;
	static float HandPalmRatio => 0.5f;
	static float HandFingerExtension => 0.38f;
	static float HandFingerRootSpread => 0.2f;
	static float HandFingerTipSpread => 0.38f;
	static float HandFingerWidth => 0.44f;
	static float HandFingerTipWidth => 0.22f;
	static float FingerSpinTargetSpeed => MathF.Tau * 7;
	static float FingerSpinAcceleration => MathF.Tau * 28;
	static float FingerSpinBlendSpeed => MathF.Tau;
	static float HitPoseKick => 4.5f;
	static float HitPoseSpread => 2.2f;
	static float RagdollSpring => 160f;
	static float RagdollDamping => 11f;
	static float RagdollMaxSpeed => 10f;
	static float PoseJostleRate => 0.85f;
	static float LandingMinSpeed => 32f;
	static float LandingInertia => 1f;
	static float LandingCompressionTime => 0.0225f;
	static int RagdollConstraintIterations => 4;
	static float TurnDuration => 0.28f;
	static float TurnStartSpeed => 2f;
	static float FootProbeAbove => 4f;
	static float FootProbeDistance => 12f;
	static float FootPlantDistance => 5f;
	static float FootstepHeightThreshold => 0.5f;
	static float FootIKSpeed => 40f;
	static float LegExtensionSlack => 0.4f;
	static float LayeredHitDuration => 0.42f;
	static float TrackingLeanDistance => 3f;
	static float LookPoseResponse => 6f;
	static float LookPoseOpenYaw => 12f * MathF.PI / 180f;
	static Vector2 LookPoseTargetRange => new(64, 96);
	static Vector2 LookPoseHeadOffset => new(20, 3);
	static float LookPoseNeckWeight => 0.35f;
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
	static int WireSegments => 8;
	static int NormalFacets => 3;
	static Point2 NeuralSize => new(64, 80);
	static Point2 NeuralOrigin => new(-32, -72);

	public IReadOnlyList<OzzRig.Clip> Clips => rig.Clips;
	public OzzRig.Clip ActiveClip => animator.ActiveClip;
	public float RangeStart => animator.RangeStart;
	public float RangeEnd => animator.RangeEnd;
	public bool AnimationFinished => animator.Finished;
	public float CurrentFacingYaw => FacingYaw(owner.Facing);
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
	public bool EyeVisible => Settings.EyeSize > 0 && (headSprite == null || TryHeadSpriteEyeOffset(out _));
	public Vector2 EyeOffset => TryHeadSpriteEyeOffset(out var position)
		? position
		: projectedPose[head] + (headSprite == null ? lookDirection * 2.4f : Vector2.Zero);
	public Vector2 LeftToeWorldPosition => owner.Position + projectedPose[leftLeg.Toe];
	public Vector2 RightToeWorldPosition => owner.Position + projectedPose[rightLeg.Toe];
	public Vector2 LeftHandWorldPosition => owner.Position + projectedPose[leftPalm];
	public Vector2 EyeWorldPosition => owner.Position + EyeOffset;
	public float PoseJostlePixels;

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
			bones[i] = new(RequiredJoint(spec.From), RequiredJoint(spec.To), spec.Width, spec.Part,
				spec.Combat, spec.Leg, spec.Arm);
			ragdollJoints[bones[i].From] = true;
			ragdollJoints[bones[i].To] = true;
			boneOrder[i] = i;
		}
		for (var i = 0; i < bones.Length; i++)
		{
			var bone = bones[i];
			if (bone.Part != Part.Hand && (bone.Arm != Side.None || bone.Leg != Side.None))
				bones[i] = bone with { Parent = Array.FindIndex(bones, it => it.To == bone.From && it.Arm == bone.Arm && it.Leg == bone.Leg) };
		}
		if (configuredWireLayout is { Count: > 0 })
		{
			wires = new RigWire[configuredWireLayout.Count];
			wireMotion = new WireMotion[wires.Length];
			wireOrder = new int[wires.Length];
			for (var i = 0; i < wires.Length; i++)
			{
				var spec = configuredWireLayout[i];
				wires[i] = new(RequiredJoint(spec.From), RequiredJoint(spec.To), spec.Bend,
					spec.DepthOffset, spec.WidthScale,
					Hash32.Mix((uint)(animationPhase * uint.MaxValue) ^ (uint)i * 0x9e3779b9u));
				wireOrder[i] = i;
			}
		}
		head = RequiredJoint("Head");
		neck = RequiredJoint("neck_01");
		leftHand = RequiredJoint("hand_l");
		leftPalm = RequiredJoint("middle_01_l");
		rightHand = RequiredJoint("hand_r");
		rightPalm = RequiredJoint("middle_01_r");
		leftUpperArm = RequiredJoint("upperarm_l");
		leftLowerArm = RequiredJoint("lowerarm_l");
		rightUpperArm = RequiredJoint("upperarm_r");
		rightLowerArm = RequiredJoint("lowerarm_r");
		spine1 = RequiredJoint("spine_01");
		spine2 = RequiredJoint("spine_02");
		spine3 = RequiredJoint("spine_03");
		leftClavicle = RequiredJoint("clavicle_l");
		rightClavicle = RequiredJoint("clavicle_r");
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
		headSprite = string.IsNullOrWhiteSpace(Settings.HeadSpriteName)
			? null
			: Assets.GetSprite(Settings.HeadSpriteName);
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
	public RuffPose GetRuffPose()
	{
		var angle = Settings.ProjectionYaw * MathF.PI / 180f;
		var pivot = ragdollPose[pelvis];
		var cameraCos = MathF.Cos(angle);
		var cameraSin = MathF.Sin(angle);
		var yaw = UpperBodyYaw();
		var projection = new RuffProjection(pivot, -pivot.Z * cameraCos + pivot.X * cameraSin,
			PresentationMirror(),
			MathF.Cos(yaw), MathF.Sin(yaw), cameraCos, cameraSin, Settings.PixelsPerMeter,
			new Vector2(0, 2.5f) + Offset, Vector2.Zero);
		var neck3D = ragdollPose[neck];
		var projectedNeck = projection.Project(neck3D, out _);
		projection = projection with { Correction = projectedPose[neck] - projectedNeck };
		return new(neck3D, HeadPosition3D(), ragdollPose[leftUpperArm], ragdollPose[leftLowerArm],
			ragdollPose[rightUpperArm], ragdollPose[rightLowerArm], projection);
	}

	public void Play(string name, bool blend = true, bool restart = false, bool seededPhase = false,
		float blendDuration = -1)
	{
		SetTakeModifierTargets(0, 1, 1);
		var startTime = seededPhase && !string.Equals(animator.ActiveClip.Name, name, StringComparison.OrdinalIgnoreCase)
			? rig.GetClip(name).Duration * animationPhase : 0;
		var transition = blend ? (blendDuration >= 0 ? blendDuration : ClipBlendDuration) : 0;
		animator.Play(name, transition, restart, startTime);
	}

	public void Play(RigTake take, float blendDuration = -1, bool restart = true)
	{
		SetTakeModifierTargets(take.UpperBodyLeanDegrees, take.ArmWidthScale, take.HandWidthScale);
		var clip = rig.GetClip(take.Clip);
		var start = Math.Clamp(take.Start, 0, clip.Duration);
		var end = Math.Clamp(take.End, start, clip.Duration);
		PlaybackSpeed = Math.Max(0.01f, take.Speed);
		animator.Play(take.Clip, start, end, blendDuration >= 0 ? blendDuration : ClipBlendDuration, restart);
	}

	public void Update(bool grounded, bool footIK, bool loop = true, bool advance = true, bool seekGround = false)
	{
		ExtraHeadSpriteRollLerped = Calc.Approach(ExtraHeadSpriteRollLerped, ExtraHeadSpriteRoll, owner.Time.Delta);

		EnsureState();
		neuralSkin.Update(Settings.SkinMode);
		UpdateFingerSpin(owner.Time.Delta);
		if (freezeRemaining > 0)
		{
			freezeRemaining = MathF.Max(0, freezeRemaining - owner.Time.Delta);
			visualAccumulator = 0;
			return;
		}
		var step = 1 / Settings.VisualFPS;
		visualAccumulator += owner.Time.Delta;
		var ticks = Math.Min((int)(visualAccumulator / step), 4);
		if (ticks <= 0)
		{
			if (!advance && poseDirty)
			{
				visualDelta = step;
				poseDirty = false;
				UpdatePose(grounded, footIK, seekGround);
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
			UpdateTakeModifiers();
			animator.Advance(advance ? step * PlaybackSpeed : 0, step, loop);
			UpdatePose(grounded, footIK, seekGround);
		}
	}

	public void SetFingerSpin(bool enabled, bool leftHandOnly)
	{
		fingerSpinTarget = enabled;
		if (enabled)
			spinLeftHandOnly = leftHandOnly;
	}

	public void SetHandWidthScale(float scale, bool leftHandOnly)
	{
		runtimeHandWidthScale = Math.Clamp(scale, 0.1f, 3);
		scaleLeftHandOnly = leftHandOnly;
	}

	void UpdateFingerSpin(float dt)
	{
		fingerSpinSpeed = Calc.Approach(fingerSpinSpeed,
			fingerSpinTarget ? FingerSpinTargetSpeed : 0, FingerSpinAcceleration * dt);
		fingerSpinAngle = MathF.IEEERemainder(fingerSpinAngle + fingerSpinSpeed * dt, MathF.Tau);
	}

	void SetTakeModifierTargets(float leanDegrees, float arms, float hands)
	{
		targetUpperBodyLeanDegrees = leanDegrees;
		targetArmWidthScale = Math.Clamp(arms, 0.1f, 3);
		targetHandWidthScale = Math.Clamp(hands, 0.1f, 3);
	}

	void UpdateTakeModifiers()
	{
		var amount = 1 - MathF.Exp(-TakeModifierResponse * visualDelta);
		upperBodyLeanDegrees = float.Lerp(upperBodyLeanDegrees, targetUpperBodyLeanDegrees, amount);
		armWidthScale = float.Lerp(armWidthScale, targetArmWidthScale, amount);
		handWidthScale = float.Lerp(handWidthScale, targetHandWidthScale, amount);
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
		gaitStepSide = Side.None;
	}

	public void ResetPoseDynamics()
	{
		ragdollReady = false;
		headMotion = leftHandMotion = rightHandMotion = default;
		Array.Clear(wireMotion);
		Array.Clear(pendingImpulses);
		pendingLandingSpeed = 0;
		ResetFeet();
	}

	public void Land(float speed)
	{
		if (speed >= LandingMinSpeed)
			pendingLandingSpeed = MathF.Max(pendingLandingSpeed, speed);
	}

	public void Freeze(float duration) => freezeRemaining = MathF.Max(freezeRemaining, duration);
	public void Resume() => freezeRemaining = 0;
	public void LockAnimationYaw() => lockedAnimationYaw = PelvisYaw();
	public void UnlockAnimationYaw() => lockedAnimationYaw = null;

	public void Recoil(Vector2 screenVelocity, float spread)
	{
		QueueImpulse(ImpulseGroup.Head, screenVelocity * 1.05f, 0);
		QueueImpulse(ImpulseGroup.Torso, screenVelocity * 0.55f, 0.001f);
		QueueImpulse(ImpulseGroup.LeftArm, screenVelocity * 0.72f - Vector2.UnitX * spread, 0.025f);
		QueueImpulse(ImpulseGroup.RightArm, screenVelocity * 0.72f + Vector2.UnitX * spread, 0.025f);
		QueueImpulse(ImpulseGroup.LeftHand, screenVelocity * 0.95f - Vector2.UnitX * spread * 1.5f, 0.055f);
		QueueImpulse(ImpulseGroup.RightHand, screenVelocity * 0.95f + Vector2.UnitX * spread * 1.5f, 0.055f);
		if (ragdollReady)
		{
			var lead = RigVelocity(screenVelocity) * 0.012f;
			ragdollPose[head] += lead;
			ragdollPose[neck] += lead * 0.5f;
			ragdollPose[spine3] += lead * 0.18f;
			ProjectPose();
			SortBones();
		}
	}

	void QueueImpulse(ImpulseGroup group, Vector2 velocity, float delay)
	{
		for (var i = 0; i < pendingImpulses.Length; i++)
			if (!pendingImpulses[i].Active)
			{
				pendingImpulses[i] = new() { Group = group, Velocity = velocity, Delay = delay, Active = true };
				return;
			}
	}

	public void Hit(float away, bool animateUpperBody = false)
		=> Hit(Vector2.UnitX * away, animateUpperBody: animateUpperBody);

	public void Hit(Vector2 direction, float limbStrength = 1, bool animateUpperBody = false)
	{
		neuralHitRemaining = 0.18f;
		TriggerEyeReaction(0.45f);
		if (animateUpperBody)
			layeredHitTime = 0;
		direction = direction.Normalized(Vector2.UnitX);
		for (var i = 0; i < ragdollVelocity.Length; i++)
		{
			if (!ragdollJoints[i])
				continue;
			var height = Math.Clamp(sampledPose[i].Y / 1.55f, 0, 1);
			var depthKick = MathF.Sign(jointDepth[i]) * 7 * height;
			ragdollVelocity[i] += RigVelocity(
				direction * (18 + 52 * height) + Vector2.UnitX * depthKick - Vector2.UnitY * 15 * height);
		}

		var across = direction.TurnLeft();
		QueueImpulse(ImpulseGroup.LeftArm, (direction * 54 - across * 18) * limbStrength, 0.015f);
		QueueImpulse(ImpulseGroup.RightArm, (direction * 54 + across * 18) * limbStrength, 0.015f);
		QueueImpulse(ImpulseGroup.LeftHand, (direction * 82 - across * 30) * limbStrength, 0.05f);
		QueueImpulse(ImpulseGroup.RightHand, (direction * 82 + across * 30) * limbStrength, 0.05f);
		QueueImpulse(ImpulseGroup.LeftLeg, (direction * 34 - across * 12) * limbStrength, 0.035f);
		QueueImpulse(ImpulseGroup.RightLeg, (direction * 34 + across * 12) * limbStrength, 0.035f);
		if (ragdollReady)
		{
			var kick = RigVelocity(direction * HitPoseKick * limbStrength);
			var spread = RigVelocity(across * HitPoseSpread * limbStrength);
			ragdollPose[head] += kick * 0.7f;
			ragdollPose[neck] += kick * 0.3f;
			ragdollPose[leftLowerArm] += kick * 0.65f - spread * 0.45f;
			ragdollPose[rightLowerArm] += kick * 0.65f + spread * 0.45f;
			ragdollPose[leftHand] += kick - spread;
			ragdollPose[leftPalm] += kick - spread;
			ragdollPose[rightHand] += kick + spread;
			ragdollPose[rightPalm] += kick + spread;
			ragdollPose[leftLeg.Knee] += kick * 0.18f - spread * 0.15f;
			ragdollPose[rightLeg.Knee] += kick * 0.18f + spread * 0.15f;
			ragdollPose[leftLeg.Ankle] += kick * 0.32f - spread * 0.3f;
			ragdollPose[rightLeg.Ankle] += kick * 0.32f + spread * 0.3f;
			ragdollPose[leftLeg.Toe] += kick * 0.42f - spread * 0.4f;
			ragdollPose[leftLeg.Tip] += kick * 0.42f - spread * 0.4f;
			ragdollPose[rightLeg.Toe] += kick * 0.42f + spread * 0.4f;
			ragdollPose[rightLeg.Tip] += kick * 0.42f + spread * 0.4f;
			ProjectPose();
			SortBones();
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

	float PresentationMirror()
		=> turning ? float.Lerp(-(int)visualFacing, -(int)targetFacing, turnAmount) : -(int)visualFacing;

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

	void UpdatePose(bool grounded, bool footIK, bool seekGround = false)
	{
		UpdateAnimationLayers();
		animator.Sample(sampledPose, sampledRotations);
		UpdateLookPose();
		ApplyTrackingLean();
		UpdateRagdoll();
		ProjectPose();
		ApplyLookPose();
		ApplyTakePoseModifiers();
		ApplyGaitAsymmetry();
		ApplyPlantResponse();
		UpdateSecondaryMotion();
		UpdateGaitFootstep(grounded && footIK);
		if (footIK)
		{
			UpdateFoot(ref leftFoot, leftLeg, Side.Left, grounded, seekGround);
			UpdateFoot(ref rightFoot, rightLeg, Side.Right, grounded, seekGround);
		}
		else
			ResetFeet();
		SortBones();
		UpdateWires();
		UpdateEye();
	}

	void ApplyTakePoseModifiers()
	{
		if (MathF.Abs(upperBodyLeanDegrees) < 0.001f)
			return;
		var pivot = projectedPose[pelvis];
		var rotation = Matrix3x2.CreateRotation(
			upperBodyLeanDegrees * MathF.PI / 180 * (int)visualFacing);
		for (var i = 0; i < projectedPose.Length; i++)
		{
			var weight = upperBodyMask[i];
			if (weight > 0)
				projectedPose[i] = Vector2.Lerp(projectedPose[i],
					pivot + Vector2.Transform(projectedPose[i] - pivot, rotation), weight);
		}
	}

	void UpdateEye()
	{
		var desired = LookTarget is { } target
			? (target - owner.Position - projectedPose[head]).Normalized()
			: new Vector2(-MathF.Cos(RigYaw()), 0);
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
		=> (Hash32.Mix(value) & 0xffff) / 32767.5f - 1;

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
		OffsetMasked(plantedSide == Side.Left ? rightArmMask : leftArmMask,
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
			HeadMotionSpring, HeadMotionDamping, HeadMotionMaxOffset) * secondaryMotion * Settings.HeadSecondaryMotion;
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
		Spring.Step(ref point.Position, ref point.Velocity, target, spring, damping, dt);
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

	void UpdateLookPose()
	{
		var target = LookTarget is not null ? Math.Clamp(LookPoseWeight, 0, 1) : 0;
		lookPoseAmount = float.Lerp(lookPoseAmount, target,
			1 - MathF.Exp(-LookPoseResponse * visualDelta));
	}

	void ApplyLookPose()
	{
		if (lookPoseAmount <= 0.001f || LookTarget is not { } target)
			return;
		var toward = target - owner.Position - projectedPose[head];
		var direction = Vector2.Clamp(toward / LookPoseTargetRange, -Vector2.One, Vector2.One);
		var offset = direction * LookPoseHeadOffset * lookPoseAmount;
		projectedPose[neck] += offset * LookPoseNeckWeight;
		projectedPose[head] += offset;
	}

	float UpperBodyYaw()
	{
		var yaw = visualYaw;
		if (turning && Settings.TurnInertia > 0)
		{
			var delay = TurnInertiaDelay * Math.Clamp(Settings.TurnInertia, 0, 1);
			var progress = Math.Clamp((turnTime / TurnDuration - delay) / (1 - delay), 0, 1);
			progress = progress * progress * (3 - 2 * progress);
			yaw = float.Lerp(turnFromYaw, turnToYaw, progress);
		}
		return OverrideYaw(yaw) - AnimationYawOffset;
	}

	float RigYaw() => OverrideYaw(visualYaw) - AnimationYawOffset;

	float AnimationYawOffset => lockedAnimationYaw is { } locked
		? MathF.IEEERemainder(PelvisYaw() - locked, MathF.Tau) : 0;

	float PelvisYaw()
	{
		var rotation = sampledRotations[pelvis];
		return MathF.Atan2(2 * (rotation.W * rotation.Y + rotation.X * rotation.Z),
			1 - 2 * (rotation.X * rotation.X + rotation.Y * rotation.Y));
	}

	float OverrideYaw(float yaw)
	{
		if (YawOverride is { } target)
			yaw += MathF.IEEERemainder(target - yaw, MathF.Tau) * Math.Clamp(YawOverrideWeight, 0, 1);
		var cameraYaw = MathF.PI * 0.5f - Settings.ProjectionYaw * MathF.PI / 180f;
		return yaw + Math.Clamp(MathF.IEEERemainder(cameraYaw - yaw, MathF.Tau),
			-LookPoseOpenYaw, LookPoseOpenYaw) * lookPoseAmount;
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
		var inertiaActive = turning && Settings.TurnInertia > 0;
		var upperBodyYaw = UpperBodyYaw();
		var pivot = ragdollPose[pelvis];
		var pivotX = -pivot.Z * cos + pivot.X * sin;
		var mirror = PresentationMirror();
		var rigYaw = RigYaw();
		var turnCos = MathF.Cos(rigYaw);
		var turnSin = MathF.Sin(rigYaw);
		var upperTurnCos = MathF.Cos(upperBodyYaw);
		var upperTurnSin = MathF.Sin(upperBodyYaw);
		for (var i = 0; i < sampledPose.Length; i++)
		{
			var joint = i == head ? HeadPosition3D() : ragdollJoints[i] ? ragdollPose[i] : sampledPose[i];
			var offset = joint - pivot;
			var weight = inertiaActive ? upperBodyMask[i] : 0;
			var jointYaw = float.Lerp(rigYaw, upperBodyYaw, weight);
			var jointTurnCos = weight <= 0 ? turnCos : weight >= 1 ? upperTurnCos : MathF.Cos(jointYaw);
			var jointTurnSin = weight <= 0 ? turnSin : weight >= 1 ? upperTurnSin : MathF.Sin(jointYaw);
			var turned = new Vector3(offset.X * jointTurnCos + offset.Z * jointTurnSin, offset.Y,
				-offset.X * jointTurnSin + offset.Z * jointTurnCos);
			projectedPose[i] = new Vector2(pivotX * mirror - turned.Z * cos + turned.X * sin, -joint.Y) *
				Settings.PixelsPerMeter + new Vector2(0, 2.5f) + Offset;
			jointDepth[i] = CameraOpenDepth(joint, pivot, jointTurnCos, jointTurnSin, cos, sin);
		}
		backLeg = LegDepth(leftLeg) < LegDepth(rightLeg) ? Side.Left : Side.Right;
		backArm = ArmDepth(leftClavicle, leftUpperArm, leftLowerArm, leftHand, leftPalm) <
			ArmDepth(rightClavicle, rightUpperArm, rightLowerArm, rightHand, rightPalm)
			? Side.Left : Side.Right;
	}

	Vector3 HeadPosition3D()
	{
		var from = ragdollPose[neck];
		var to = ragdollPose[head];
		var direction = to - from;
		return direction.LengthSquared() > 0.000001f
			? to + Vector3.Normalize(direction) * (Settings.NeckExtension / Settings.PixelsPerMeter)
			: to;
	}

	float LegDepth(Leg leg)
		=> (jointDepth[leg.Hip] + jointDepth[leg.Knee] + jointDepth[leg.Ankle] +
			jointDepth[leg.Toe] + jointDepth[leg.Tip]) / 5;

	float ArmDepth(int clavicle, int upperArm, int lowerArm, int hand, int palm)
		=> (jointDepth[clavicle] + jointDepth[upperArm] + jointDepth[lowerArm] +
			jointDepth[hand] + jointDepth[palm]) / 5;

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

		UpdatePendingImpulses();

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
			for (var i = 0; i < ragdollPose.Length; i++)
				if (ragdollJoints[i])
				{
					ragdollPreviousPose[i] = ragdollPose[i];
					var target = sampledPose[i] + PoseJostleOffset(i);
					Spring.Step(ref ragdollPose[i], ref ragdollVelocity[i], target,
						RagdollSpring, RagdollDamping, dt, RagdollMaxSpeed);
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

	void UpdatePendingImpulses()
	{
		for (var i = 0; i < pendingImpulses.Length; i++)
		{
			ref var impulse = ref pendingImpulses[i];
			if (!impulse.Active)
				continue;
			if (impulse.Delay > 0)
			{
				impulse.Delay -= visualDelta;
				continue;
			}
			ApplyImpulse(impulse.Group, RigVelocity(impulse.Velocity));
			impulse.Active = false;
		}
	}

	void ApplyImpulse(ImpulseGroup group, Vector3 velocity)
	{
		switch (group)
		{
			case ImpulseGroup.Head:
				ragdollVelocity[head] += velocity;
				ragdollVelocity[neck] += velocity * 0.45f;
				break;
			case ImpulseGroup.Torso:
				ragdollVelocity[spine1] += velocity * 0.35f;
				ragdollVelocity[spine2] += velocity * 0.65f;
				ragdollVelocity[spine3] += velocity;
				ragdollVelocity[neck] += velocity * 0.8f;
				ragdollVelocity[leftClavicle] += velocity;
				ragdollVelocity[rightClavicle] += velocity;
				break;
			case ImpulseGroup.LeftArm:
				ragdollVelocity[leftUpperArm] += velocity * 0.6f;
				ragdollVelocity[leftLowerArm] += velocity;
				break;
			case ImpulseGroup.RightArm:
				ragdollVelocity[rightUpperArm] += velocity * 0.6f;
				ragdollVelocity[rightLowerArm] += velocity;
				break;
			case ImpulseGroup.LeftHand:
				ragdollVelocity[leftHand] += velocity;
				ragdollVelocity[leftPalm] += velocity;
				break;
			case ImpulseGroup.RightHand:
				ragdollVelocity[rightHand] += velocity;
				ragdollVelocity[rightPalm] += velocity;
				break;
			case ImpulseGroup.LeftLeg:
				ApplyLegImpulse(leftLeg, velocity);
				break;
			case ImpulseGroup.RightLeg:
				ApplyLegImpulse(rightLeg, velocity);
				break;
		}
	}

	void ApplyLegImpulse(Leg leg, Vector3 velocity)
	{
		ragdollVelocity[leg.Knee] += velocity * 0.45f;
		ragdollVelocity[leg.Ankle] += velocity * 0.75f;
		ragdollVelocity[leg.Toe] += velocity;
		ragdollVelocity[leg.Tip] += velocity;
	}

	Vector3 PoseJostleOffset(int joint)
	{
		if (PoseJostlePixels <= 0 || joint == pelvis)
			return Vector3.Zero;
		var time = owner.Time.SecondsF * PoseJostleRate;
		var seed = unchecked((uint)joint * 0x85ebca6bu ^ (uint)(animationPhase * uint.MaxValue));
		return new Vector3(
			SmoothNoise.Signed(time, seed),
			SmoothNoise.Signed(time * 0.91f, seed ^ 0xc2b2ae35u),
			SmoothNoise.Signed(time * 1.07f, seed ^ 0x27d4eb2du)) *
			(PoseJostlePixels / Settings.PixelsPerMeter);
	}

	Vector3 RigVelocity(Vector2 screenVelocity)
	{
		var angle = Settings.ProjectionYaw * MathF.PI / 180f + RigYaw();
		var alongProjection = screenVelocity.X / Settings.PixelsPerMeter;
		return new(MathF.Sin(angle) * alongProjection, -screenVelocity.Y / Settings.PixelsPerMeter,
			-MathF.Cos(angle) * alongProjection);
	}

	void UpdateFoot(ref FootIK plant, Leg leg, Side side, bool grounded, bool seekGround)
	{
		var wasPlanted = plant.Planted;
		plant.ContactRemaining = MathF.Max(0, plant.ContactRemaining - visualDelta);
		var animatedToe = owner.Position + projectedPose[leg.Toe];
		var targetOffset = 0f;
		plant.Planted = false;
		if (grounded || seekGround)
		{
			var probeOrigin = seekGround ? new Vector2(animatedToe.X, owner.Position.Y) : animatedToe;
			var probeCenter = (probeOrigin - Vector2.UnitY * FootProbeAbove).RoundToPoint2();
			var probe = new RectInt(probeCenter.X - 1, probeCenter.Y - 1, 2, 2);
			if (owner.Game.TryFindSurface(owner, probe, Vector2.UnitY, FootProbeDistance, out var hit) &&
				hit.Normal.Y < -0.5f &&
				(seekGround || MathF.Abs(animatedToe.Y - hit.Point.Y) <= FootPlantDistance))
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

	void UpdateGaitFootstep(bool grounded)
	{
		if (!GaitActive || !grounded)
		{
			gaitStepSide = Side.None;
			return;
		}

		var height = projectedPose[leftLeg.Toe].Y - projectedPose[rightLeg.Toe].Y;
		var side = height > FootstepHeightThreshold ? Side.Left :
			height < -FootstepHeightThreshold ? Side.Right : gaitStepSide;
		if (side == Side.None || side == gaitStepSide)
			return;
		gaitStepSide = side;
		Footstep?.Invoke(owner.Position + projectedPose[side == Side.Left ? leftLeg.Toe : rightLeg.Toe]);
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

	void UpdateWires()
	{
		var scale = Settings.PixelsPerMeter / 25f;
		var maxOffset = 10 * scale;
		var teleportDistance = SecondaryTeleportDistance * scale;
		for (var i = 0; i < wires.Length; i++)
		{
			var wire = wires[i];
			var from = projectedPose[wire.From];
			var to = projectedPose[wire.To];
			var control = (from + to) * 0.5f + (to - from).Normalized(Vector2.UnitY).TurnLeft() *
				(wire.Bend * Wires.WireBendScale * scale * PresentationMirror());
			ref var motion = ref wireMotion[i];
			if (!motion.Ready || Vector2.DistanceSquared(motion.Control, control) > teleportDistance * teleportDistance)
			{
				motion.Control = control;
				motion.Velocity = Vector2.Zero;
				motion.Ready = true;
				continue;
			}
			Spring.Step(ref motion.Control, ref motion.Velocity, control,
				Wires.WireSpring, Wires.WireDamping, MathF.Min(visualDelta, 1 / 30f));
			var offset = motion.Control - control;
			if (offset.LengthSquared() > maxOffset * maxOffset)
			{
				var direction = Vector2.Normalize(offset);
				motion.Control = control + direction * maxOffset;
				motion.Velocity -= direction * MathF.Max(0, Vector2.Dot(motion.Velocity, direction));
			}
		}

		for (var i = 1; i < wireOrder.Length; i++)
		{
			var value = wireOrder[i];
			var depth = WireDepth(wires[value]);
			var at = i;
			while (at > 0 && WireDepth(wires[wireOrder[at - 1]]) > depth)
			{
				wireOrder[at] = wireOrder[at - 1];
				at--;
			}
			wireOrder[at] = value;
		}
	}

	float BoneDepth(Bone bone) => (jointDepth[bone.From] + jointDepth[bone.To]) * 0.5f;
	float WireDepth(RigWire wire) => (jointDepth[wire.From] + jointDepth[wire.To]) * 0.5f + wire.DepthOffset;

	Color BoneColor(Bone bone, float depth)
		=> bone.From == pelvis || bone.To == pelvis
			? Settings.CoreColor
			: Color.Lerp(Settings.FarColor, Settings.NearColor, depth);

	float BoneWidth(Bone bone) => bone.Part switch
	{
		Part.Torso => Settings.TorsoSize,
		Part.Bicep => Settings.BicepSize * armWidthScale,
		Part.Forearm => Settings.ForearmSize * armWidthScale,
		Part.Hand => Settings.ForearmSize * handWidthScale *
			(!scaleLeftHandOnly || bone.Arm == Side.Left ? runtimeHandWidthScale : 1),
		Part.Thigh => Settings.ThighSize,
		_ => bone.Width,
	};

	float BoneRenderDepth(Bone bone)
		=> Math.Clamp(BoneDepth(bone) * MathF.Max(0, Settings.DepthColorContrast) + 0.5f, 0, 1);

	void BoneWidths(Bone bone, out float fromWidth, out float toWidth)
	{
		fromWidth = BaseBoneWidth(bone);
		toWidth = TaperedWidth(bone, fromWidth);
		if (bone.Parent >= 0)
		{
			var parent = bones[bone.Parent];
			fromWidth = float.Lerp(fromWidth, TaperedWidth(parent, BaseBoneWidth(parent)),
				Math.Clamp(Settings.JointContinuity, 0, 1));
		}
	}

	float BaseBoneWidth(Bone bone)
		=> BoneWidth(bone) * (1 + MathF.Max(0, BoneRenderDepth(bone) - 0.5f) * 2 * Settings.DepthWidthAmount);

	float TaperedWidth(Bone bone, float width)
		=> bone.Leg != Side.None || bone.Part is Part.Bicep or Part.Forearm or Part.Hand
			? width * (1 - Math.Clamp(Settings.LimbTaper, 0, 0.75f))
			: width;

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
			if (!BackArmCombatEnabled && bone.Arm == backArm)
				continue;
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
			if (!bone.Combat || !BackArmCombatEnabled && bone.Arm == backArm)
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

	public Rect GetVisualBounds()
	{
		var headRadius = Settings.HeadSize + 1.4f;
		if (TryGetHeadSpriteFrame(out var frame, out _))
			headRadius = new Vector2(frame.Subtexture.Width, frame.Subtexture.Height).Length() *
				Settings.HeadSpriteScale * 0.5f;
		var bounds = new Rect(projectedPose[head] - Vector2.One * headRadius, Vector2.One * headRadius * 2);
		for (var i = 0; i < bones.Length; i++)
		{
			var bone = bones[i];
			BoneWidths(bone, out var fromWidth, out var toWidth);
			BoneQuad(projectedPose[bone.From], projectedPose[bone.To], fromWidth + 2, toWidth + 2,
				out var a, out var b, out var c, out var d);
			bounds = bounds.Conflate(a).Conflate(b).Conflate(c).Conflate(d);
		}
		for (var i = 0; i < wires.Length; i++)
		{
			var radius = Wires.WireWidth * wires[i].WidthScale + Wires.WireOutlineWidth + 1;
			for (var segment = 0; segment <= WireSegments; segment++)
			{
				var point = WirePoint(i, segment / (float)WireSegments);
				bounds = bounds.Conflate(point - Vector2.One * radius).Conflate(point + Vector2.One * radius);
			}
		}
		return bounds + owner.Position;
	}

	public void Draw(Batcher batcher, Color tint, bool silhouette)
		=> Draw(batcher, tint, silhouette, DrawParts.All, null);
	public void DrawWithAccessory(Batcher batcher, Color tint, bool silhouette,
		IHumanoidRigAccessoryVisual accessory)
		=> Draw(batcher, tint, silhouette, DrawParts.All, accessory);
	public void DrawBehindPlayer(Batcher batcher, Color tint, bool silhouette)
		=> Draw(batcher, tint, silhouette, DrawParts.BehindPlayer, null);
	public void DrawInFrontOfPlayer(Batcher batcher, Color tint, bool silhouette)
		=> Draw(batcher, tint, silhouette, DrawParts.InFrontOfPlayer, null);
	public void DrawInFrontOfPlayerWithAccessory(Batcher batcher, Color tint, bool silhouette,
		IHumanoidRigAccessoryVisual accessory)
		=> Draw(batcher, tint, silhouette, DrawParts.InFrontOfPlayer, accessory);

	bool Includes(Bone bone, DrawParts parts)
	{
		if (parts == DrawParts.All)
			return true;
		var behindPlayer = bone.Leg == backLeg || bone.Arm == backArm;
		return parts == DrawParts.BehindPlayer ? behindPlayer : !behindPlayer;
	}

	void Draw(Batcher batcher, Color tint, bool silhouette, DrawParts parts,
		IHumanoidRigAccessoryVisual? accessory)
	{
		var surface = !silhouette && Settings.SkinMode == RigSkinMode.Surface
			? Assets.GetSurface(Settings.SurfaceId) : null;
		var neuralTexture = !silhouette && Settings.SkinMode == RigSkinMode.NCA && Neural.NeuralColorAmount > 0
			? neuralSkin.OutputTexture : null;
		if (neuralTexture != null)
			batcher.PushSampler(new(TextureFilter.Nearest, TextureWrap.Clamp));
		var accessoryIndex = 0;
		var wireIndex = parts == DrawParts.BehindPlayer ? wireOrder.Length : 0;
		var headDrawn = parts == DrawParts.BehindPlayer;
		var headDepth = jointDepth[head];
		if (silhouette)
			DrawOutlines(batcher, tint, parts);
		var outline = Multiply(Settings.OutlineColor, tint);
		Span<bool> drawn = stackalloc bool[bones.Length];
		for (var sortedIndex = 0; sortedIndex < boneOrder.Length; sortedIndex++)
		{
			var boneIndex = boneOrder[sortedIndex];
			var bone = bones[boneIndex];
			if (!Includes(bone, parts))
				continue;
			var boneDepth = BoneDepth(bone);
			if (accessory != null && !headDrawn && headDepth <= boneDepth)
			{
				DrawWiresThrough(batcher, ref wireIndex, headDepth, tint, silhouette);
				DrawAccessoryThrough(batcher, accessory, ref accessoryIndex, headDepth,
					silhouette ? tint : null);
				DrawHead(batcher, tint, silhouette, surface, neuralTexture);
				headDrawn = true;
			}
			DrawWiresThrough(batcher, ref wireIndex, boneDepth, tint, silhouette);
			DrawAccessoryThrough(batcher, accessory, ref accessoryIndex, boneDepth,
				silhouette ? tint : null);
			if (silhouette)
				continue;

			var from = projectedPose[bone.From];
			var to = projectedPose[bone.To];
			BoneWidths(bone, out var fromWidth, out var toWidth);
			var color = Multiply(BoneColor(bone, BoneRenderDepth(bone)), tint);
			drawn[boneIndex] = true;
			if (bone.Part == Part.Hand)
			{
				var spinAmount = HandSpinAmount(bone);
				DrawHand(batcher, from, to, fromWidth, toWidth, spinAmount, 2, outline);
				DrawHand(batcher, from, to, fromWidth, toWidth, spinAmount, 0, color);
				DrawHandPalmSkin(batcher, boneIndex, from, to, fromWidth, toWidth,
					color, surface, neuralTexture, normals: false);
				continue;
			}

			// A joint's outline ring comes from whichever of its two bones draws first; both bones
			// fill a disc there, so the second covers the first's outline where they meet.
			var joinsParent = bone.Parent >= 0;
			var fromRadius = JointRadius(fromWidth);
			var toRadius = JointRadius(toWidth);
			DrawBoneQuad(batcher, from, to, fromWidth + 2, toWidth + 2, outline);
			if (joinsParent && !drawn[bone.Parent])
				batcher.Circle(from, fromRadius + 1, 14, outline);
			if (!ChildDrawn(boneIndex, drawn))
				batcher.Circle(to, toRadius + 1, 14, outline);
			DrawBoneQuad(batcher, from, to, fromWidth, toWidth, color);
			if (joinsParent)
				batcher.Circle(from, fromRadius, 14, color);
			batcher.Circle(to, toRadius, 14, color);
			var uv = neuralBoneUVs[boneIndex];
			var skinColor = color * Neural.NeuralColorAmount;
			if (surface != null)
				DrawSurfaceBone(batcher, surface, from, to, fromWidth, toWidth, uv, normals: false);
			else if (neuralTexture != null)
				DrawNeuralBone(batcher, neuralTexture, from, to, fromWidth, toWidth, uv, skinColor);
			if (joinsParent)
				DrawJointSkin(batcher, from, fromRadius, (uv.A + uv.B) * 0.5f, surface, neuralTexture, skinColor,
					normals: false);
			DrawJointSkin(batcher, to, toRadius, (uv.C + uv.D) * 0.5f, surface, neuralTexture, skinColor,
				normals: false);
		}

		if (!headDrawn)
		{
			DrawWiresThrough(batcher, ref wireIndex, headDepth, tint, silhouette);
			DrawAccessoryThrough(batcher, accessory, ref accessoryIndex, headDepth,
				silhouette ? tint : null);
			DrawHead(batcher, tint, silhouette, surface, neuralTexture);
		}
		DrawWiresThrough(batcher, ref wireIndex, float.PositiveInfinity, tint, silhouette);
		DrawAccessoryThrough(batcher, accessory, ref accessoryIndex, float.PositiveInfinity,
			silhouette ? tint : null);
		if (neuralTexture != null)
			batcher.PopSampler();
	}

	void DrawHead(Batcher batcher, Color tint, bool silhouette, Surface? surface, Texture? neuralTexture)
	{
		if (TryGetHeadSpriteFrame(out var frame, out _))
		{
			DrawHeadSprite(batcher, frame.Subtexture, silhouette ? tint : Multiply(HeadTint, tint));
			if (!silhouette && Settings.EyeSize > 0 && TryHeadSpriteEyeOffset(out var eye))
				batcher.Circle(eye, VisualEyeScale * Settings.EyeSize * eyeScale, 8,
					Multiply(Settings.AccentColor, tint));
			return;
		}

		var position = projectedPose[head];
		batcher.Circle(position, Settings.HeadSize + 1.4f, 14,
			silhouette ? tint : Multiply(Settings.OutlineColor, tint));
		batcher.Circle(position, Settings.HeadSize, 14, silhouette ? tint : Multiply(Settings.NearColor, tint));
		if (surface != null)
			DrawSurfaceCircle(batcher, surface, position, Settings.HeadSize,
				neuralHeadUV, neuralHeadUVRadius, normals: false);
		else if (neuralTexture != null)
			DrawNeuralCircle(batcher, neuralTexture, position, Settings.HeadSize,
				neuralHeadUV, neuralHeadUVRadius, Multiply(Settings.NearColor, tint) * Neural.NeuralColorAmount);
		if (!silhouette && Settings.EyeSize > 0)
			batcher.Circle(position + lookDirection * 2.4f, VisualEyeScale * Settings.EyeSize * eyeScale, 8,
				Multiply(Settings.AccentColor, tint));
	}

	static Color Multiply(Color color, Color tint) => new(
		(byte)(color.R * tint.R / 255), (byte)(color.G * tint.G / 255),
		(byte)(color.B * tint.B / 255), (byte)(color.A * tint.A / 255));

	public void DrawNormals(Batcher batcher)
		=> DrawNormals(batcher, DrawParts.All, null);
	public void DrawNormalsWithAccessory(Batcher batcher, IHumanoidRigAccessoryVisual accessory)
		=> DrawNormals(batcher, DrawParts.All, accessory);
	public void DrawNormalsBehindPlayer(Batcher batcher)
		=> DrawNormals(batcher, DrawParts.BehindPlayer, null);
	public void DrawNormalsInFrontOfPlayer(Batcher batcher)
		=> DrawNormals(batcher, DrawParts.InFrontOfPlayer, null);
	public void DrawNormalsInFrontOfPlayerWithAccessory(Batcher batcher,
		IHumanoidRigAccessoryVisual accessory)
		=> DrawNormals(batcher, DrawParts.InFrontOfPlayer, accessory);

	void DrawNormals(Batcher batcher, DrawParts parts, IHumanoidRigAccessoryVisual? accessory)
	{
		var flat = new Color(0x8080ff);
		var surface = Settings.SkinMode == RigSkinMode.Surface &&
			Assets.GetSurface(Settings.SurfaceId) is { HasNormal: true } foundSurface
			? foundSurface : null;
		var neuralNormals = Settings.SkinMode == RigSkinMode.NCA && Neural.NeuralNormalAmount > 0
			? neuralSkin.OutputNormalTexture : null;
		if (neuralNormals != null)
			batcher.PushSampler(new(TextureFilter.Nearest, TextureWrap.Clamp));
		var accessoryIndex = 0;
		var wireIndex = parts == DrawParts.BehindPlayer ? wireOrder.Length : 0;
		var headDrawn = parts == DrawParts.BehindPlayer;
		var headDepth = jointDepth[head];
		for (var sortedIndex = 0; sortedIndex < boneOrder.Length; sortedIndex++)
		{
			var bone = bones[boneOrder[sortedIndex]];
			if (!Includes(bone, parts))
				continue;
			var boneDepth = BoneDepth(bone);
			if (accessory != null && !headDrawn && headDepth <= boneDepth)
			{
				DrawWireNormalsThrough(batcher, ref wireIndex, headDepth);
				DrawAccessoryThrough(batcher, accessory, ref accessoryIndex, headDepth, flat);
				DrawHeadNormals(batcher, flat, surface, neuralNormals);
				headDrawn = true;
			}
			DrawWireNormalsThrough(batcher, ref wireIndex, boneDepth);
			DrawAccessoryThrough(batcher, accessory, ref accessoryIndex, boneDepth, flat);
			var boneIndex = boneOrder[sortedIndex];
			var from = projectedPose[bone.From];
			var to = projectedPose[bone.To];
			BoneWidths(bone, out var fromWidth, out var toWidth);
			if (bone.Part == Part.Hand)
			{
				DrawHand(batcher, from, to, fromWidth, toWidth, HandSpinAmount(bone), 2, flat);
				DrawHandPalmSkin(batcher, boneIndex, from, to, fromWidth, toWidth,
					flat, surface, neuralNormals, normals: true);
				continue;
			}
			var joinsParent = bone.Parent >= 0;
			var fromRadius = JointRadius(fromWidth);
			var toRadius = JointRadius(toWidth);
			var seed = (uint)boneIndex * 0x9e3779b9u;
			if (Settings.NormalRoundness > 0)
			{
				DrawBoneNormals(batcher, boneIndex, from, to, fromWidth + 2, toWidth + 2);
				if (joinsParent)
					DrawSphereNormals(batcher, from, fromRadius + 1, 14, Settings.NormalRoundness,
						Settings.NormalIrregularity, seed);
				DrawSphereNormals(batcher, to, toRadius + 1, 14, Settings.NormalRoundness,
					Settings.NormalIrregularity, seed);
			}
			else
			{
				DrawBoneQuad(batcher, from, to, fromWidth + 2, toWidth + 2, flat);
				if (joinsParent)
					batcher.Circle(from, fromRadius + 1, 14, flat);
				batcher.Circle(to, toRadius + 1, 14, flat);
			}
			var uv = neuralBoneUVs[boneIndex];
			var skinColor = Color.White * Neural.NeuralNormalAmount;
			if (surface != null)
				DrawSurfaceBone(batcher, surface, from, to, fromWidth, toWidth, uv, normals: true);
			else if (neuralNormals != null)
				DrawNeuralBone(batcher, neuralNormals, from, to, fromWidth, toWidth, uv, skinColor);
			if (joinsParent)
				DrawJointSkin(batcher, from, fromRadius, (uv.A + uv.B) * 0.5f, surface, neuralNormals, skinColor,
					normals: true);
			DrawJointSkin(batcher, to, toRadius, (uv.C + uv.D) * 0.5f, surface, neuralNormals, skinColor,
				normals: true);
		}
		if (!headDrawn)
		{
			DrawWireNormalsThrough(batcher, ref wireIndex, headDepth);
			DrawAccessoryThrough(batcher, accessory, ref accessoryIndex, headDepth, flat);
			DrawHeadNormals(batcher, flat, surface, neuralNormals);
		}
		DrawWireNormalsThrough(batcher, ref wireIndex, float.PositiveInfinity);
		DrawAccessoryThrough(batcher, accessory, ref accessoryIndex, float.PositiveInfinity, flat);
		if (neuralNormals != null)
			batcher.PopSampler();
	}

	void DrawHeadNormals(Batcher batcher, Color flat, Surface? surface, Texture? neuralNormals)
	{
		if (TryGetHeadSpriteFrame(out var frame, out _))
		{
			DrawHeadSprite(batcher, frame.NormalSubtexture, Color.White);
			return;
		}

		var position = projectedPose[head];
		if (Settings.NormalRoundness > 0)
			DrawSphereNormals(batcher, position, Settings.HeadSize + 1.4f, 14,
				Settings.NormalRoundness, Settings.NormalIrregularity, 0x68bc21ebu);
		else
			batcher.Circle(position, Settings.HeadSize + 1.4f, 14, flat);
		if (surface != null)
			DrawSurfaceCircle(batcher, surface, position, Settings.HeadSize,
				neuralHeadUV, neuralHeadUVRadius, normals: true);
		else if (neuralNormals != null)
			DrawNeuralCircle(batcher, neuralNormals, position, Settings.HeadSize,
				neuralHeadUV, neuralHeadUVRadius, Color.White * Neural.NeuralNormalAmount);
	}

	bool TryGetHeadSpriteFrame(out Sprite.Frame frame, out int frameIndex)
	{
		if (headSprite is not { Frames.Count: > 0 } sprite)
		{
			frame = default;
			frameIndex = -1;
			return false;
		}

		var cameraYaw = MathF.PI * 0.5f - Settings.ProjectionYaw * MathF.PI / 180f;
		var yaw = UpperBodyYaw();
		var cameraOpenYaw = MathF.Atan2(MathF.Sin(yaw), MathF.Abs(MathF.Cos(yaw)));
		var theta = Calc.AngleWrap((cameraYaw - cameraOpenYaw) * PresentationMirror());
		// Frames sample yaw clockwise: front, front-left, left, back-left, back, ...
		frameIndex = (int)MathF.Floor(theta / MathF.Tau * sprite.Frames.Count + 0.5f)
			% sprite.Frames.Count;
		frame = sprite.Frames[frameIndex];
		return true;
	}

	internal float ExtraHeadSpriteRoll; // MathF.PI * 0.39f;
	float ExtraHeadSpriteRollLerped;

	float HeadSpriteRoll()
	{
		var axis = projectedPose[head] - projectedPose[neck];
		var final = axis.LengthSquared() > 0.0001f ? axis.Angle() + MathF.PI * 0.5f : 0;
		final += ExtraHeadSpriteRollLerped;
		return final;
	}

	void DrawHeadSprite(Batcher batcher, Subtexture subtexture, Color color)
	{
		var origin = headSprite!.ResolveOrigin(SpriteAnchor.Center);
		batcher.Image(subtexture, projectedPose[head], origin,
			new Vector2(Settings.HeadSpriteScale), HeadSpriteRoll(), color);
	}

	bool TryHeadSpriteEyeOffset(out Vector2 position)
	{
		position = default;
		if (!TryGetHeadSpriteFrame(out _, out var frameIndex) ||
			headSprite!.TryGetVisibleSocket("eye", frameIndex, out var socket) == false)
			return false;

		var origin = headSprite.ResolveOrigin(SpriteAnchor.Center);
		var local = (socket.Position - origin) * Settings.HeadSpriteScale;
		position = projectedPose[head] + Vector2.Transform(local, Matrix3x2.CreateRotation(HeadSpriteRoll()));
		return true;
	}

	void DrawWiresThrough(Batcher batcher, ref int index, float depth, Color tint, bool silhouette)
	{
		while (index < wireOrder.Length && WireDepth(wires[wireOrder[index]]) <= depth)
		{
			DrawWire(batcher, wireOrder[index], tint, silhouette);
			index++;
		}
	}

	void DrawWireNormalsThrough(Batcher batcher, ref int index, float depth)
	{
		while (index < wireOrder.Length && WireDepth(wires[wireOrder[index]]) <= depth)
		{
			DrawWireNormals(batcher, wireOrder[index]);
			index++;
		}
	}

	void DrawWire(Batcher batcher, int index, Color tint, bool silhouette)
	{
		var wire = wires[index];
		var width = Wires.WireWidth * wire.WidthScale * MathF.Sqrt(Settings.PixelsPerMeter / 25f);
		var previous = WirePoint(index, 0);
		for (var segment = 1; segment <= WireSegments; segment++)
		{
			var next = WirePoint(index, segment / (float)WireSegments);
			var variation = 1 + HashSigned(wire.Seed ^ (uint)segment * 0x85ebca6bu) *
				Wires.WireIrregularity * 0.18f;
			var segmentWidth = width * variation;
			if (silhouette)
				batcher.Line(previous, next, segmentWidth + Wires.WireOutlineWidth * 2, tint);
			else
			{
				batcher.Line(previous, next, segmentWidth + Wires.WireOutlineWidth * 2,
					Multiply(Wires.WireOutlineColor, tint));
				batcher.Line(previous, next, segmentWidth, Multiply(Wires.WireColor, tint));
			}
			previous = next;
		}
	}

	void DrawWireNormals(Batcher batcher, int index)
	{
		var wire = wires[index];
		var width = Wires.WireWidth * wire.WidthScale * MathF.Sqrt(Settings.PixelsPerMeter / 25f) +
			Wires.WireOutlineWidth * 2;
		var previous = WirePoint(index, 0);
		for (var segment = 1; segment <= WireSegments; segment++)
		{
			var next = WirePoint(index, segment / (float)WireSegments);
			var variation = 1 + HashSigned(wire.Seed ^ (uint)segment * 0x85ebca6bu) *
				Wires.WireIrregularity * 0.18f;
			DrawCylinderNormals(batcher, previous, next, width * variation,
				Wires.WireNormalRoundness, Wires.WireIrregularity * 0.35f,
				wire.Seed ^ (uint)segment * 0xc2b2ae35u);
			previous = next;
		}
	}

	Vector2 WirePoint(int index, float progress)
	{
		var wire = wires[index];
		var from = projectedPose[wire.From];
		var to = projectedPose[wire.To];
		var along = (to - from).Normalized(Vector2.UnitY);
		var targetControl = (from + to) * 0.5f + along.TurnLeft() *
			(wire.Bend * Wires.WireBendScale * Settings.PixelsPerMeter / 25f * PresentationMirror());
		var control = Vector2.Lerp(targetControl, wireMotion[index].Control, Math.Clamp(Wires.WireMotion, 0, 1));
		var inverse = 1 - progress;
		var point = from * (inverse * inverse) + control * (2 * inverse * progress) + to * (progress * progress);
		var phase = (wire.Seed & 0xffff) / 65535f * MathF.Tau;
		var noise = MathF.Sin(progress * MathF.Tau * 2 + phase) * 0.65f +
			MathF.Sin(progress * MathF.Tau * 5 + phase * 1.7f) * 0.35f;
		return point + along.TurnLeft() * (noise * MathF.Sin(progress * MathF.PI) *
			Wires.WireIrregularity * Wires.WireWidth * 0.55f);
	}

	static void DrawAccessoryThrough(Batcher batcher, IHumanoidRigAccessoryVisual? accessory,
		ref int index, float depth, Color? tint)
	{
		if (accessory == null)
			return;
		while (index < accessory.DrawItemCount && accessory.GetDrawItemDepth(index) <= depth)
		{
			accessory.DrawItem(batcher, index, tint);
			index++;
		}
	}

	// Every bone's outline shape in one color: the combined silhouette used for shadows and reflections.
	void DrawOutlines(Batcher batcher, Color color, DrawParts parts)
	{
		foreach (var boneIndex in boneOrder)
		{
			var bone = bones[boneIndex];
			if (!Includes(bone, parts))
				continue;
			var from = projectedPose[bone.From];
			var to = projectedPose[bone.To];
			BoneWidths(bone, out var fromWidth, out var toWidth);
			if (bone.Part == Part.Hand)
				DrawHand(batcher, from, to, fromWidth, toWidth, HandSpinAmount(bone), 2, color);
			else
			{
				DrawBoneQuad(batcher, from, to, fromWidth + 2, toWidth + 2, color);
				batcher.Circle(to, JointRadius(toWidth) + 1, 14, color);
			}
		}
	}

	float HandSpinAmount(Bone bone)
		=> !spinLeftHandOnly || bone.Arm == Side.Left ? Math.Clamp(fingerSpinSpeed / FingerSpinBlendSpeed, 0, 1) : 0;

	static float JointRadiusScale => 0.5f;
	static float JointRadius(float width) => MathF.Max(1.2f, width * JointRadiusScale);
	bool ChildDrawn(int boneIndex, ReadOnlySpan<bool> drawn)
	{
		for (var i = 0; i < bones.Length; i++)
			if (bones[i].Parent == boneIndex && drawn[i])
				return true;
		return false;
	}

	void DrawJointSkin(Batcher batcher, Vector2 center, float radius, Vector2 uvCenter, Surface? surface,
		Texture? texture, Color color, bool normals)
	{
		var uvRadius = new Vector2(radius) / (Vector2)NeuralSize;
		if (surface != null)
			DrawSurfaceCircle(batcher, surface, center, radius, uvCenter, uvRadius, normals);
		else if (texture != null)
			DrawNeuralCircle(batcher, texture, center, radius, uvCenter, uvRadius, color);
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

	void DrawBoneNormals(Batcher batcher, int boneIndex, Vector2 from, Vector2 to,
		float fromWidth, float toWidth)
	{
		var seed = Hash32.Mix((uint)(animationPhase * uint.MaxValue) ^ (uint)boneIndex * 0x9e3779b9u);
		for (var facet = 0; facet < NormalFacets; facet++)
		{
			var start = facet / (float)NormalFacets;
			var end = (facet + 1) / (float)NormalFacets;
			DrawCylinderNormals(batcher, Vector2.Lerp(from, to, start), Vector2.Lerp(from, to, end),
				float.Lerp(fromWidth, toWidth, (start + end) * 0.5f),
				Settings.NormalRoundness, Settings.NormalIrregularity,
				seed ^ (uint)facet * 0x85ebca6bu);
		}
	}

	static void DrawCylinderNormals(Batcher batcher, Vector2 from, Vector2 to, float width,
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

	static void DrawSphereNormals(Batcher batcher, Vector2 center, float radius, int steps,
		float roundness, float irregularity, uint seed)
	{
		var centerNormal = NormalColor(new Vector2(HashSigned(seed), HashSigned(seed ^ 0x27d4eb2du)) *
			(irregularity * 0.08f));
		for (var i = 0; i < steps; i++)
		{
			var a = Calc.AngleToVector(i / (float)steps * MathF.Tau);
			var b = Calc.AngleToVector((i + 1) / (float)steps * MathF.Tau);
			var aAmount = roundness * (1 + HashSigned(seed ^ (uint)i * 0x85ebca6bu) * irregularity * 0.12f);
			var bAmount = roundness * (1 + HashSigned(seed ^ (uint)(i + 1) * 0x85ebca6bu) * irregularity * 0.12f);
			batcher.Triangle(center, center + a * radius, center + b * radius,
				centerNormal, NormalColor(a * aAmount), NormalColor(b * bAmount));
		}
	}

	static Color NormalColor(Vector2 lateral)
	{
		var length = lateral.Length();
		if (length > 0.999f)
			lateral *= 0.999f / length;
		var normal = new Vector3(lateral, MathF.Sqrt(MathF.Max(0, 1 - lateral.LengthSquared())));
		return new(normal.X * 0.5f + 0.5f, normal.Y * 0.5f + 0.5f, normal.Z * 0.5f + 0.5f, 1);
	}

	void DrawHand(Batcher batcher, Vector2 from, Vector2 to, float fromWidth, float toWidth,
		float spinAmount, float inflate, Color color)
	{
		var along = (to - from).Normalized(Vector2.UnitY);
		var across = along.TurnLeft();
		var width = MathF.Max(fromWidth, toWidth);
		var palmEnd = Vector2.Lerp(from, to, HandPalmRatio);
		var fingerTip = to + along * (width * HandFingerExtension);
		var spin = fingerSpinAngle;
		var fingerRootWidth = width * HandFingerWidth;
		var fingerTipWidth = width * HandFingerTipWidth;
		DrawBoneQuad(batcher, from, palmEnd, fromWidth * 0.72f + inflate, width + inflate, color);
		for (var side = -1; side <= 1; side += 2)
		{
			var rootOffset = across * (width * HandFingerRootSpread * side);
			var tipOffset = fingerTip - palmEnd + across * (width * HandFingerTipSpread * side);
			if (spinAmount > 0)
			{
				rootOffset = Vector2.Lerp(rootOffset, Rotate(rootOffset, spin), spinAmount);
				tipOffset = Vector2.Lerp(tipOffset, Rotate(tipOffset, spin), spinAmount);
			}
			DrawBoneQuad(batcher,
				palmEnd + rootOffset, palmEnd + tipOffset,
				fingerRootWidth + inflate, fingerTipWidth + inflate, color);
		}
	}

	static Vector2 Rotate(Vector2 value, float radians)
	{
		var cos = MathF.Cos(radians);
		var sin = MathF.Sin(radians);
		return new(value.X * cos - value.Y * sin, value.X * sin + value.Y * cos);
	}

	void DrawHandPalmSkin(Batcher batcher, int boneIndex, Vector2 from, Vector2 to,
		float fromWidth, float toWidth, Color color, Surface? surface, Texture? neuralTexture, bool normals)
	{
		if (surface == null && neuralTexture == null)
			return;
		var width = MathF.Max(fromWidth, toWidth);
		var palmEnd = Vector2.Lerp(from, to, HandPalmRatio);
		var uv = neuralBoneUVs[boneIndex];
		var palmUV = new BoneUV(uv.A, uv.B,
			Vector2.Lerp(uv.B, uv.C, HandPalmRatio), Vector2.Lerp(uv.A, uv.D, HandPalmRatio));
		if (surface != null)
			DrawSurfaceBone(batcher, surface, from, palmEnd, fromWidth * 0.72f, width, palmUV, normals);
		else
			DrawNeuralBone(batcher, neuralTexture!, from, palmEnd, fromWidth * 0.72f, width, palmUV,
				color * (normals ? Neural.NeuralNormalAmount : Neural.NeuralColorAmount));
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
