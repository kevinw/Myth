using System.Numerics;
using System.Runtime.InteropServices;
using Foster.Framework;
using static Myth.RigDraw;

namespace Myth;

public enum RigSkinMode { Solid, Surface, NCA }
public enum RigOutlineMode { PerPiece, Silhouette }

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

	enum Part { Torso, Chest, Neck, Bicep, Forearm, Hand, Thigh, Shin, Foot }
	enum Side { None, Left, Right }
	internal enum DrawParts { All, BehindPlayer, InFrontOfPlayer }

	public IHumanoidRigAccessoryVisual? Accessory;
	readonly List<RigDrawItem> drawItems = [];

	readonly record struct BoneSpec(string From, string To, Part Part, bool Combat = false,
		Side Leg = Side.None, Side Arm = Side.None, RigTags Tags = RigTags.None);
	readonly record struct Bone(int From, int To, Part Part, bool Combat, Side Leg, Side Arm, RigTags Tags, int Parent = -1)
		: IRigBone;
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
		new("pelvis", "spine_01", Part.Torso, true, Tags: RigTags.Waist),
		new("spine_01", "spine_02", Part.Torso, true, Tags: RigTags.Trunk),
		new("spine_02", "spine_03", Part.Torso, true, Tags: RigTags.Trunk),
		new("spine_03", "neck_01", Part.Chest, true, Tags: RigTags.Trunk),
		new("neck_01", "Head", Part.Neck, true, Tags: RigTags.Neck),
		new("spine_03", "clavicle_l", Part.Bicep, true, Arm: Side.Left, Tags: RigTags.Shoulder | RigTags.Left),
		new("clavicle_l", "upperarm_l", Part.Bicep, true, Arm: Side.Left, Tags: RigTags.Shoulder | RigTags.Left),
		new("upperarm_l", "lowerarm_l", Part.Bicep, true, Arm: Side.Left, Tags: RigTags.UpperArm | RigTags.Left),
		new("lowerarm_l", "hand_l", Part.Forearm, true, Arm: Side.Left, Tags: RigTags.Forearm | RigTags.Left),
		new("hand_l", "middle_01_l", Part.Hand, true, Arm: Side.Left, Tags: RigTags.Hand | RigTags.Left),
		new("spine_03", "clavicle_r", Part.Bicep, true, Arm: Side.Right, Tags: RigTags.Shoulder | RigTags.Right),
		new("clavicle_r", "upperarm_r", Part.Bicep, true, Arm: Side.Right, Tags: RigTags.Shoulder | RigTags.Right),
		new("upperarm_r", "lowerarm_r", Part.Bicep, true, Arm: Side.Right, Tags: RigTags.UpperArm | RigTags.Right),
		new("lowerarm_r", "hand_r", Part.Forearm, true, Arm: Side.Right, Tags: RigTags.Forearm | RigTags.Right),
		new("hand_r", "middle_01_r", Part.Hand, true, Arm: Side.Right, Tags: RigTags.Hand | RigTags.Right),
		new("pelvis", "thigh_l", Part.Thigh, Leg: Side.Left, Tags: RigTags.Hip | RigTags.Left),
		new("thigh_l", "calf_l", Part.Thigh, Leg: Side.Left, Tags: RigTags.Thigh | RigTags.Left),
		new("calf_l", "foot_l", Part.Shin, Leg: Side.Left, Tags: RigTags.Shin | RigTags.Left),
		new("foot_l", "ball_l", Part.Foot, Leg: Side.Left, Tags: RigTags.Foot | RigTags.Left),
		new("ball_l", "ball_leaf_l", Part.Foot, Leg: Side.Left, Tags: RigTags.Foot | RigTags.Left),
		new("pelvis", "thigh_r", Part.Thigh, Leg: Side.Right, Tags: RigTags.Hip | RigTags.Right),
		new("thigh_r", "calf_r", Part.Thigh, Leg: Side.Right, Tags: RigTags.Thigh | RigTags.Right),
		new("calf_r", "foot_r", Part.Shin, Leg: Side.Right, Tags: RigTags.Shin | RigTags.Right),
		new("foot_r", "ball_r", Part.Foot, Leg: Side.Right, Tags: RigTags.Foot | RigTags.Right),
		new("ball_r", "ball_leaf_r", Part.Foot, Leg: Side.Right, Tags: RigTags.Foot | RigTags.Right),
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
	// Tilts just the head (its sprite, and the eyes) to aim at this, up to HeadLookMaxTurn radians off the
	// body's facing; with the target behind it, the head eases back to its pose.
	public Vector2? HeadLookTarget;
	public float HeadLookMaxTurn;
	public float HeadLookRate;
	public float HeadRoll { get; private set; } // radians, clockwise on screen
	public Vector2 HeadPosition => projectedPose[head];
	public float? YawOverride;
	// Rotates the whole figure about its torso so its arms (neck to hands) point this way on screen, by ArmAimWeight.
	public Vector2 ArmAim = Vector2.UnitX;
	public float ArmAimWeight;
	public float PoseRoll { get; private set; } // radians, clockwise on screen
	// Swings the whole figure about its left hand (the grip), radians clockwise on screen; folded into PoseRoll.
	public float GripRoll;
	// Set by the owner: degrees of forward lean on top of the take's, e.g. a run's lean-in that settles.
	public float ExtraLeanDegrees;
	// Set by the owner around a draw under its own material (a burrow's terrain cut): surfaces would escape it.
	public bool SkipSurfaces;
	public float YawOverrideWeight;
	// Set by the owner: pixels above its feet to the lowest ceiling over it; the head ducks beneath. Null: open above.
	public float? CeilingClearance;
	float duckDepth;

	OzzRig rig = null!;
	OzzAnimator animator = null!;
	Vector3[] sampledPose = [];
	Vector3[] lastSampledPose = [];
	Vector3[] inertialOffset = [];
	float inertialElapsed;
	float inertialDuration;
	bool inertialPending;
	bool inertialActive;
	bool landingHoldPending;
	// KeyEvents: a clip change or a turn keys the pose now; the stepping holds between them.
	bool keyPending;
	Signs keyFacing;
	Quaternion[] sampledRotations = [];
	Vector3[] ragdollPose = [];
	Vector3[] ragdollCarriedPose = [];
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
	OzzJointMask leftHandMask = null!, rightHandMask = null!;
	// For joints outside the ragdoll (fingers): the nearest ancestor in it, whose springing they ride along with.
	int[] ragdollCarriers = [];
	OzzJointMask rightArmMask = null!;
	OzzRig.Clip upperBodyHitClip = null!;
	NeuralSkinRuntime neuralSkin = null!;
	Sprite? headSprite;
	SculptedHead? sculptedHead;
	SculptedBody? sculptedBody;
	bool sculptedBodyDirty = true;
	readonly List<BodyPrimitive> bodyPrimitives = [];
	// A dent stays where it landed on a bone: its surface point and normal in that bone's frame (meters along, across,
	// out), so it rides the limb and keeps its side through a turn. Depth springs back to flush on flesh.
	struct BodyDent
	{
		public int Bone;
		public Vector3 Surface, Normal;
		public float Radius, Depth, Velocity, Age;
		public bool Flesh;
		public Color Color;
	}
	readonly List<BodyDent> dents = [];
	const int MaxDents = 8;
	static float DentRadius => 0.07f; // meters, at strength 1
	static float FleshDentDepth => 0.035f;
	static float MetalDentDepth => 0.025f;
	static float FleshDentSpring => 140f;
	static float FleshDentDamping => 5f;
	public float MouthOpen; // 0 closed .. 1 wide, for a sculpted head
	static float SculptedHeadMinRadius => 5f;
	bool UsesSculptedHead => (Settings.SculptedHead || Settings.HeadShapeId.Name is { Length: > 0 }) && headSprite == null &&
		HeadRadius >= SculptedHeadMinRadius;
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
	int[] leftClaws = [], rightClaws = []; // per clawed finger: its second joint, third joint and tip
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
	float appliedShoulderDrop;
	bool turnReady;
	bool turning;
	bool poseDirty;
	Vector2 lookDirection;
	float lookPoseAmount;
	bool eyeReady;
	static float VisualEyeScale => 2.0f;
	float eyeScale = 1;
	float upperBodyLeanDegrees;
	float velocityLean;
	float velocityLeanRate;
	float leanPreviousVelocityX;
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
	static float LandingHoldFullSpeed => 8f; // meters/second of landing speed that earns the whole LandingHold
	static int RagdollConstraintIterations => 4;
	static float TurnStartSpeed => 2f;
	static float FootProbeAbove => 4f;
	static float FootProbeDistance => 12f;
	static float FootPlantDistance => 5f;
	static float FootstepHeightThreshold => 0.5f;
	static float FootIKSpeed => 40f;
	static float FootPlantedSlack => 0.5f;
	// Foot constants are tuned in pixels at 25 px/m; smaller rigs shrink them so tiny legs keep the same knee bend.
	float FootScale => MathF.Min(1, Settings.PixelsPerMeter / 25);
	Vector2 FootSink => new(0, 0f * FootScale);
	static float LegExtensionSlack => 0.4f;
	static float LayeredHitDuration => 0.42f;
	static float TrackingLeanDistance => 3f;
	static float VelocityLeanMaxAngle => 0.35f;
	// Leans read input when they follow acceleration (immediate) more than velocity (which lags it).
	static float VelocityLeanFullSpeed => 280f;
	static float VelocityLeanFullAcceleration => 900f;
	static float VelocityLeanSpring => 140f;
	static float VelocityLeanDamping => 9f;
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
	static float DuckMargin => 0f; // pixels kept between the head and the ceiling
	static float DuckResponse => 14f;
	static float DuckMaxHunch => 1.15f; // radians of forward stoop before the knees take the rest
	static float DuckMaxKneeDrop => 0.5f; // × pelvis height
	static float PlantShoulderLift => 1.1f;
	static bool DrawSpineToClavicle => false;
	static float GaitHeadOffset => 1.1f;
	static float GaitShoulderOffset => 0.8f;
	static float GaitStrideOffset => 1.25f;
	static float TurnInertiaDelay => 0.35f;
	static float EyeTrackingSpeed => 8f;
	static float EyeSaccadeInterval => 0.55f;
	static float EyeSaccadeAmount => 0.12f;
	static float EyeReactionDuration => 0.18f;
	static int WireSegments => 8;
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
	public float HeadRadius => Settings.HeadSize * Settings.PixelsPerMeter;
	public bool EyeVisible => Settings.EyeSize > 0 && (headSprite == null || TryHeadSpriteEyeOffset(out _));
	public Vector2 EyeOffset => TryHeadSpriteEyeOffset(out var position)
		? position
		: projectedPose[head] + (headSprite == null ? lookDirection * 2.4f : Vector2.Zero);
	public Vector2 LeftToeWorldPosition => owner.Position + projectedPose[leftLeg.Toe];
	public Vector2 RightToeWorldPosition => owner.Position + projectedPose[rightLeg.Toe];
	public Vector2 LeftHandWorldPosition => owner.Position + projectedPose[leftPalm];
	public Vector2 RightHandWorldPosition => owner.Position + projectedPose[rightPalm];
	public Vector2 RightPalm => projectedPose[rightPalm];
	public Vector2 TorsoCenter => (projectedPose[pelvis] + projectedPose[neck]) * 0.5f;
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
		lastSampledPose = new Vector3[rig.JointCount];
		inertialOffset = new Vector3[rig.JointCount];
		sampledRotations = new Quaternion[rig.JointCount];
		ragdollPose = new Vector3[rig.JointCount];
		ragdollCarriedPose = new Vector3[rig.JointCount];
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
			bones[i] = new(RequiredJoint(spec.From), RequiredJoint(spec.To), spec.Part,
				spec.Combat, spec.Leg, spec.Arm, spec.Tags);
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
		leftClaws = ClawJoints("l");
		rightClaws = ClawJoints("r");
		rightClavicle = RequiredJoint("clavicle_r");
		pelvis = RequiredJoint("pelvis");
		leftLeg = RequiredLeg("l");
		rightLeg = RequiredLeg("r");
		upperBodyMask = rig.CreateMask();
		upperBodyMask.Set("spine_01", 1, descendants: true);
		upperBodyMask.Set("spine_01", 0.25f);
		upperBodyMask.Set("spine_02", 0.6f);
		leftHandMask = rig.CreateMask();
		leftHandMask.Set("hand_l", 1, descendants: true);
		rightHandMask = rig.CreateMask();
		rightHandMask.Set("hand_r", 1, descendants: true);
		ragdollCarriers = new int[rig.JointCount];
		for (var i = 0; i < ragdollCarriers.Length; i++)
		{
			var carrier = i;
			while (carrier >= 0 && !ragdollJoints[carrier])
				carrier = rig.JointParent(carrier);
			ragdollCarriers[i] = carrier;
		}
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

	int[] ClawJoints(string side)
	{
		string[] fingers = ["index", "middle", "ring", "thumb"];
		var joints = new int[fingers.Length * 3];
		for (var i = 0; i < fingers.Length; i++)
		{
			joints[i * 3] = RequiredJoint($"{fingers[i]}_02_{side}");
			joints[i * 3 + 1] = RequiredJoint($"{fingers[i]}_03_{side}");
			joints[i * 3 + 2] = RequiredJoint($"{fingers[i]}_04_leaf_{side}");
		}
		return joints;
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
	// Meters per second a walk clip carries the body at its natural rate; 0 for clips that don't walk.
	public float WalkGroundSpeed(string clip)
		=> rig != null && rig.TryGetClip(clip, out var found) ? rig.GroundSpeed(found, [leftLeg.Toe, rightLeg.Toe]) : 0;
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
			FootSink + Offset, Vector2.Zero);
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
		var transition = Transition(blend ? (blendDuration >= 0 ? blendDuration : -1) : 0);
		animator.Play(name, transition, restart, startTime);
	}

	// The animator's cross-fade for a clip change; with an inertial blend, none, and the switch is eased out here.
	// requested < 0 means the default.
	float Transition(float requested)
	{
		keyPending = true;
		if (requested == 0)
			return 0;
		if (Settings.InertialBlendTime <= 0)
			return requested > 0 ? requested : ClipBlendDuration;
		inertialPending = true;
		inertialDuration = requested > 0 ? requested : Settings.InertialBlendTime;
		return 0;
	}

	public void Play(RigTake take, float blendDuration = -1, bool restart = true)
	{
		SetTakeModifierTargets(take.UpperBodyLeanDegrees, take.ArmWidthScale, take.HandWidthScale);
		var clip = rig.GetClip(take.Clip);
		var start = Math.Clamp(take.Start, 0, clip.Duration);
		var end = Math.Clamp(take.End, start, clip.Duration);
		PlaybackSpeed = Math.Max(0.01f, take.Speed);
		animator.Play(take.Clip, start, end, Transition(blendDuration), restart);
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
		// At or above the frame rate (with slack for frame-time jitter): one update per frame, over its real delta.
		if (step <= owner.Time.Delta * 1.25f)
		{
			visualDelta = visualAccumulator;
			visualAccumulator = 0;
			poseDirty = false;
			UpdateTurn(grounded);
			UpdateTakeModifiers();
			animator.Advance(advance ? visualDelta * PlaybackSpeed : 0, visualDelta, loop);
			UpdatePose(grounded, footIK, seekGround);
			return;
		}
		keyPending |= owner.Facing != keyFacing;
		keyFacing = owner.Facing;
		var ticks = Math.Min((int)(visualAccumulator / step), 4);
		if (ticks <= 0 && keyPending && Settings.KeyEvents)
		{
			ticks = 1;
			visualAccumulator = step;
		}
		keyPending = false;
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
		if (speed < LandingMinSpeed)
			return;
		pendingLandingSpeed = MathF.Max(pendingLandingSpeed, speed);
		landingHoldPending = Settings.LandingHold > 0;
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

	public float FacingYaw(Signs facing)
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
		if (!turning || !Settings.TurnWhileMoving && (!grounded || MathF.Abs(owner.Velocity.X) > TurnStartSpeed))
			return;

		turnTime = MathF.Min(turnTime + visualDelta, Settings.TurnDuration);
		var progress = turnTime / Settings.TurnDuration;
		turnAmount = progress * progress * (3 - 2 * progress);
		visualYaw = float.Lerp(turnFromYaw, turnToYaw, turnAmount);
		if (turnTime < Settings.TurnDuration)
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
		ApplyInertialBlend();
		ApplyShoulderDrop();
		UpdateLookPose();
		ApplyTrackingLean();
		UpdateRagdoll();
		ProjectPose();
		ApplyLookPose();
		UpdateVelocityLean();
		ApplyUpperBodyLean();
		ApplyGaitAsymmetry();
		ApplyPlantResponse();
		ApplySquish();
		ApplyCeilingDuck();
		ApplyPoseRoll();
		UpdateSecondaryMotion();
		UpdateGaitFootstep(grounded && footIK);
		if (footIK)
		{
			UpdateFoot(ref leftFoot, leftLeg, Side.Left, grounded, seekGround);
			UpdateFoot(ref rightFoot, rightLeg, Side.Right, grounded, seekGround);
		}
		else
			ResetFeet();
		if (Settings.PixelSnap)
			for (var i = 0; i < projectedPose.Length; i++)
				projectedPose[i] = projectedPose[i].Round();
		SortBones();
		UpdateWires();
		UpdateHeadLook();
		UpdateEye();
		UpdateDents();
		sculptedBodyDirty = true;
	}

	void UpdateDents()
	{
		var span = CollectionsMarshal.AsSpan(dents);
		for (var i = 0; i < span.Length; i++)
			if (span[i].Flesh)
			{
				ref var dent = ref span[i];
				Spring.Step(ref dent.Depth, ref dent.Velocity, 0, FleshDentSpring, FleshDentDamping, visualDelta);
				dent.Age += visualDelta;
			}
		dents.RemoveAll(static dent => dent.Flesh && dent.Age > 1 && MathF.Abs(dent.Depth) < 0.002f);
	}

	// Dents the sculpted body where an attack's world rect touches it.
	public void Dent(in RectInt attack, float strength = 1)
	{
		if (UsesBodyCombat && BodyContact((Rect)(attack - owner.Position), CombatRole.Vulnerable, out var contact))
			Dent(owner.Position + contact, strength);
	}

	// Dents the sculpted body at a world point: on the surface under it, or, if it misses the silhouette, the nearest
	// surface toward the torso.
	public void Dent(Vector2 worldPoint, float strength = 1)
	{
		if (!Settings.SculptedBody || bodyPrimitives.Count == 0)
			return;
		var pieces = CollectionsMarshal.AsSpan(bodyPrimitives);
		var local = worldPoint - owner.Position;
		Vector3 hit = default;
		var found = false;
		for (var i = 0; i <= 12 && !found; i++)
			found = SculptedBody.Raycast(pieces, Vector2.Lerp(local, TorsoCenter, i / 12f), out hit);
		if (!found)
			return;
		var normal = SculptedBody.Normal(pieces, hit);
		var piece = SculptedBody.NearestPiece(pieces, hit);
		var bone = 0;
		var closest = float.MaxValue;
		for (var i = 0; i < bones.Length; i++)
		{
			var (from, to) = (ViewJoint(bones[i].From), ViewJoint(bones[i].To));
			var along = to - from;
			var t = Math.Clamp(Vector3.Dot(hit - from, along) / MathF.Max(along.LengthSquared(), 0.0001f), 0, 1);
			var distance = Vector3.DistanceSquared(hit, from + along * t);
			if (distance < closest)
				(bone, closest) = (i, distance);
		}
		var (origin, axis, across, outward) = DentFrame(bone);
		var ppm = Settings.PixelsPerMeter;
		var offset = hit - origin;
		var flesh = piece >= 0 && pieces[piece].Material == 0;
		if (dents.Count == MaxDents)
			dents.RemoveAt(0);
		dents.Add(new()
		{
			Bone = bone,
			Surface = new Vector3(Vector3.Dot(offset, axis), Vector3.Dot(offset, across), Vector3.Dot(offset, outward)) / ppm,
			Normal = new(Vector3.Dot(normal, axis), Vector3.Dot(normal, across), Vector3.Dot(normal, outward)),
			Radius = DentRadius * strength,
			Depth = (flesh ? FleshDentDepth : MetalDentDepth) * strength,
			Flesh = flesh,
			Color = piece >= 0 ? pieces[piece].Color : default,
		});
		sculptedBodyDirty = true;
	}

	// A bone's frame in view pixels. Across and outward flip with the facing mirror, so what's on a limb's front stays
	// on its front when the figure turns.
	(Vector3 Origin, Vector3 Axis, Vector3 Across, Vector3 Outward) DentFrame(int bone)
	{
		var from = ViewJoint(bones[bone].From);
		var axis = Vector3.Normalize(ViewJoint(bones[bone].To) - from);
		if (!float.IsFinite(axis.X))
			axis = Vector3.UnitY;
		var mirror = PresentationMirror() < 0 ? -1 : 1;
		var across = Vector3.Cross(axis, Vector3.UnitZ);
		across = across.LengthSquared() > 0.0001f ? Vector3.Normalize(across) * mirror : Vector3.UnitX;
		return (from, axis, across, Vector3.Cross(across, axis) * mirror);
	}

	Vector3 ViewJoint(int joint) => new(projectedPose[joint], jointDepth[joint] * Settings.PixelsPerMeter);

	// Rendered when drawn (it skips itself when nothing changed), so posed and stepped rigs stay current too.
	SculptedHead? CurrentSculptedHead()
	{
		if (!UsesSculptedHead)
			return null;
		sculptedHead ??= new(owner.Game.Manager.GraphicsDevice);
		// On a sculpted body the head shades from its own color like the body's parts; otherwise from the palette.
		var (near, core, far) = (Settings.NearColor, Settings.CoreColor, Settings.FarColor);
		if (Settings.SculptedBody)
		{
			near = Settings.HeadColor;
			far = new Color((Vector4)near * (Vector4)Settings.BodyShadow);
			core = Color.Lerp(near, far, 0.5f);
		}
		var center = owner.RenderPosition + projectedPose[head];
		Span<BodyLight> lights = stackalloc BodyLight[SculptedBody.MaxLights];
		// Head sprites count yaw the other way round.
		sculptedHead.Render(Assets.GetHeadShape(Settings.HeadShapeId), HeadRadius, -HeadViewYaw(), 0,
			HeadSpriteRoll(), MouthOpen, near, core, far, Settings.EyeSize > 0 ? Settings.AccentColor : default,
			Settings.BodyShading, new(Settings.SkinMetallic, Settings.SkinRoughness, Settings.Specular, 0),
			lights[..GatherLights(lights, center, HeadRadius)], center);
		return sculptedHead;
	}

	// At a clip switch the old pose becomes an offset on the new one, eased out with a fast start: most of the change
	// shows in the first frames (crisp), the rest settles (no pop). A switch mid-blend starts from what was shown.
	void ApplyInertialBlend()
	{
		if (inertialPending && ragdollReady)
		{
			for (var i = 0; i < sampledPose.Length; i++)
				inertialOffset[i] = lastSampledPose[i] - sampledPose[i];
			inertialElapsed = 0;
			inertialActive = true;
		}
		inertialPending = false;
		if (inertialActive)
		{
			inertialElapsed += visualDelta;
			var remaining = 1 - Math.Clamp(inertialElapsed / MathF.Max(inertialDuration, 0.0001f), 0, 1);
			var weight = remaining * remaining * remaining;
			for (var i = 0; i < sampledPose.Length; i++)
				sampledPose[i] += inertialOffset[i] * weight;
			inertialActive = remaining > 0;
		}
		sampledPose.CopyTo(lastSampledPose, 0);
	}

	// Slides both shoulder chains down the chest, so every clip wears the same proportions.
	void ApplyShoulderDrop()
	{
		// The ragdoll keeps the bone lengths it first saw; a new drop re-measures them.
		if (appliedShoulderDrop != Settings.ShoulderDrop)
			ragdollReady = false;
		appliedShoulderDrop = Settings.ShoulderDrop;
		if (appliedShoulderDrop == 0)
			return;
		var offset = Vector3.Normalize(sampledPose[spine3] - sampledPose[neck]) * appliedShoulderDrop;
		for (var i = 0; i < sampledPose.Length; i++)
			sampledPose[i] += offset * (leftArmMask[i] + rightArmMask[i]);
	}

	void UpdateHeadLook()
	{
		var desired = 0f;
		if (HeadLookTarget is { } target)
		{
			var toward = target - owner.Position - projectedPose[head];
			var facing = -MathF.Cos(RigYaw()) >= 0 ? 0 : MathF.PI;
			var turn = MathF.IEEERemainder(MathF.Atan2(toward.Y, toward.X) - facing, MathF.Tau);
			if (MathF.Abs(turn) <= MathF.PI / 2)
				desired = Math.Clamp(turn, -HeadLookMaxTurn, HeadLookMaxTurn);
		}
		HeadRoll = float.Lerp(HeadRoll, desired, 1 - MathF.Exp(-HeadLookRate * visualDelta));
	}

	// Leans into speed and acceleration: forward while running, back on a skid, whipping through a reversal.
	void UpdateVelocityLean()
	{
		var velocityX = owner.Velocity.X;
		var acceleration = (velocityX - leanPreviousVelocityX) / visualDelta;
		leanPreviousVelocityX = velocityX;
		var target = Math.Clamp(velocityX / VelocityLeanFullSpeed + acceleration / VelocityLeanFullAcceleration, -1, 1) *
			VelocityLeanMaxAngle * Settings.VelocityLean * PoseModifierWeight;
		Spring.Step(ref velocityLean, ref velocityLeanRate, target, VelocityLeanSpring, VelocityLeanDamping, visualDelta);
	}

	void ApplyUpperBodyLean()
	{
		var angle = (upperBodyLeanDegrees + ExtraLeanDegrees) * MathF.PI / 180 * (int)visualFacing + velocityLean;
		if (MathF.Abs(angle) < 0.0001f)
			return;
		var pivot = projectedPose[pelvis];
		var rotation = Matrix3x2.CreateRotation(angle);
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
			: Vector2.Transform(new Vector2(-MathF.Cos(RigYaw()), 0), Matrix3x2.CreateRotation(HeadRoll + PoseRoll));
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

	// The owner's sprite Squish, scaled about the ground; legs re-solve to their planted ankles so a squash
	// bends the knees instead of shrinking the shins.
	void ApplySquish()
	{
		var squish = Vector2.Lerp(Vector2.One, owner.Squish, Settings.SquishResponse);
		if (Vector2.DistanceSquared(squish, Vector2.One) < 0.0001f)
			return;
		var left = CaptureLeg(leftLeg);
		var right = CaptureLeg(rightLeg);
		var ground = new Vector2(projectedPose[pelvis].X, (FootSink + Offset).Y);
		for (var i = 0; i < projectedPose.Length; i++)
			projectedPose[i] = ground + (projectedPose[i] - ground) * squish;
		RestoreLeg(leftLeg, left);
		RestoreLeg(rightLeg, right);
	}

	// Measured on this frame's pose, so it lowers the head exactly as far as this clip needs: a forward stoop first,
	// then bent knees for the rest.
	void ApplyCeilingDuck()
	{
		var headTop = -(projectedPose[head].Y - HeadRadius);
		var needed = CeilingClearance is { } clearance ? MathF.Max(0, headTop - clearance + DuckMargin) : 0;
		duckDepth = float.Lerp(duckDepth, needed, 1 - MathF.Exp(-DuckResponse * visualDelta));
		if (duckDepth < 0.01f)
			return;

		var pivot = projectedPose[pelvis];
		var reach = projectedPose[head] - pivot;
		var forward = (int)visualFacing;
		var lean = MathF.Atan2(reach.X * forward, -reach.Y);
		var length = reach.Length();
		var hunch = Math.Clamp(MathF.Acos(Math.Clamp((-reach.Y - duckDepth) / length, -1, 1)) - lean, 0, DuckMaxHunch);
		var rotation = Matrix3x2.CreateRotation(hunch * forward, pivot);
		var headBefore = projectedPose[head].Y;
		for (var i = 0; i < projectedPose.Length; i++)
			if (upperBodyMask[i] > 0)
				projectedPose[i] = Vector2.Lerp(projectedPose[i], Vector2.Transform(projectedPose[i], rotation), upperBodyMask[i]);

		var crouch = Math.Clamp(duckDepth - (projectedPose[head].Y - headBefore), 0, MathF.Max(0, (FootSink.Y - pivot.Y) * DuckMaxKneeDrop));
		if (crouch <= 0)
			return;
		var left = CaptureLeg(leftLeg);
		var right = CaptureLeg(rightLeg);
		for (var i = 0; i < projectedPose.Length; i++)
			projectedPose[i].Y += crouch;
		RestoreLeg(leftLeg, left);
		RestoreLeg(rightLeg, right);
	}

	void ApplyPoseRoll()
	{
		var arms = (projectedPose[leftPalm] + projectedPose[rightPalm]) * 0.5f - projectedPose[neck];
		PoseRoll = ArmAimWeight <= 0 ? 0 :
			MathF.IEEERemainder(MathF.Atan2(ArmAim.Y, ArmAim.X) - MathF.Atan2(arms.Y, arms.X), MathF.Tau) * ArmAimWeight;
		RotatePose(PoseRoll, (projectedPose[pelvis] + projectedPose[neck]) * 0.5f);
		RotatePose(GripRoll, projectedPose[leftPalm]);
		PoseRoll += GripRoll;
	}

	void RotatePose(float angle, Vector2 pivot)
	{
		if (MathF.Abs(angle) < 0.0001f)
			return;
		var rotation = Matrix3x2.CreateRotation(angle, pivot);
		for (var i = 0; i < projectedPose.Length; i++)
			projectedPose[i] = Vector2.Transform(projectedPose[i], rotation);
	}

	readonly record struct LegCapture(Vector2 Knee, Vector2 Ankle, Vector2 Toe, Vector2 Tip, float Upper, float Lower);

	LegCapture CaptureLeg(Leg leg) => new(projectedPose[leg.Knee], projectedPose[leg.Ankle], projectedPose[leg.Toe],
		projectedPose[leg.Tip], Vector2.Distance(projectedPose[leg.Hip], projectedPose[leg.Knee]),
		Vector2.Distance(projectedPose[leg.Knee], projectedPose[leg.Ankle]));

	void RestoreLeg(Leg leg, in LegCapture capture)
	{
		SolveLeg(leg, capture.Ankle, capture.Upper, capture.Lower);
		var shift = projectedPose[leg.Ankle] - capture.Ankle;
		projectedPose[leg.Toe] = capture.Toe + shift;
		projectedPose[leg.Tip] = capture.Tip + shift;
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
		OffsetMasked(leftHandMask, leftOffset);
		OffsetMasked(rightHandMask, rightOffset);
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
			var progress = Math.Clamp((turnTime / Settings.TurnDuration - delay) / (1 - delay), 0, 1);
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
		sculptedHead?.Dispose();
		sculptedBody?.Dispose();
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
			var carrier = ragdollCarriers[i];
			var joint = i == head ? HeadPosition3D()
				: carrier < 0 ? sampledPose[i]
				: sampledPose[i] + ragdollPose[carrier] - sampledPose[carrier];
			var offset = joint - pivot;
			var weight = inertiaActive ? upperBodyMask[i] : 0;
			var jointYaw = float.Lerp(rigYaw, upperBodyYaw, weight);
			var jointTurnCos = weight <= 0 ? turnCos : weight >= 1 ? upperTurnCos : MathF.Cos(jointYaw);
			var jointTurnSin = weight <= 0 ? turnSin : weight >= 1 ? upperTurnSin : MathF.Sin(jointYaw);
			var turned = new Vector3(offset.X * jointTurnCos + offset.Z * jointTurnSin, offset.Y,
				-offset.X * jointTurnSin + offset.Z * jointTurnCos);
			projectedPose[i] = new Vector2(pivotX * mirror - turned.Z * cos + turned.X * sin, -joint.Y) *
				Settings.PixelsPerMeter + FootSink + Offset;
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
			? to + Vector3.Normalize(direction) * Settings.NeckExtension
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
			sampledPose.CopyTo(ragdollCarriedPose, 0);
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
			// Show the compressed contact pose, then hold it: the next updates freeze, harder landings longer.
			if (landingHoldPending)
				freezeRemaining = MathF.Max(freezeRemaining,
					Settings.LandingHold * Math.Clamp(impactSpeed / LandingHoldFullSpeed, 0, 1));
			landingHoldPending = false;
			pendingLandingSpeed = 0;
		}

		if (Settings.RagdollCarry > 0)
			for (var i = 0; i < ragdollPose.Length; i++)
				if (ragdollJoints[i])
					ragdollPose[i] += (sampledPose[i] - ragdollCarriedPose[i]) * Settings.RagdollCarry;
		sampledPose.CopyTo(ragdollCarriedPose, 0);

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
			var probeCenter = (probeOrigin - Vector2.UnitY * FootProbeAbove * FootScale).RoundToPoint2();
			var probe = new RectInt(probeCenter.X - 1, probeCenter.Y - 1, 2, 2);
			var terrainRelative = Settings.GaitFeetFollowTerrain && GaitActive && !seekGround;
			if (owner.Game.TryFindSurface(owner, probe, Vector2.UnitY, FootProbeDistance * FootScale, out var hit) &&
				hit.Normal.Y < -0.5f)
			{
				if (terrainRelative)
				{
					targetOffset = hit.Point.Y - owner.Position.Y;
					plant.Planted = animatedToe.Y + targetOffset >= hit.Point.Y - FootPlantedSlack;
				}
				else if (seekGround || MathF.Abs(animatedToe.Y - hit.Point.Y) <= FootPlantDistance * FootScale)
				{
					targetOffset = hit.Point.Y - animatedToe.Y;
					plant.Planted = true;
				}
			}
		}

		plant.OffsetY = Calc.Approach(plant.OffsetY, targetOffset, FootIKSpeed * FootScale * visualDelta);
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

	void SolveLeg(Leg leg, Vector2 ankleTarget) => SolveLeg(leg, ankleTarget,
		Vector2.Distance(projectedPose[leg.Hip], projectedPose[leg.Knee]),
		Vector2.Distance(projectedPose[leg.Knee], projectedPose[leg.Ankle]));

	void SolveLeg(Leg leg, Vector2 ankleTarget, float upperLength, float lowerLength)
	{
		var hip = projectedPose[leg.Hip];
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

	// Costume shades with depth like the bone under it: the palette's far/near ratio at that depth, so far-side
	// clothing darkens and shifts hue as much as the limb does. Bones on the pelvis take the flat core color.
	Color CostumeShade(Bone bone)
	{
		if (bone.From == pelvis || bone.To == pelvis)
			return Color.White;
		static float Ratio(byte far, byte near) => near == 0 ? 1 : MathF.Min(1, far / (float)near);
		var near = Settings.NearColor;
		var far = Settings.FarColor;
		return Color.Lerp(new Color(Ratio(far.R, near.R), Ratio(far.G, near.G), Ratio(far.B, near.B), 1f), Color.White,
			BoneRenderDepth(bone));
	}

	float BoneWidth(Bone bone) => Settings.PixelsPerMeter * bone.Part switch
	{
		Part.Torso => Settings.TorsoSize,
		Part.Chest => Settings.ChestSize,
		Part.Neck => Settings.NeckSize,
		Part.Bicep => Settings.BicepSize * armWidthScale,
		Part.Forearm => Settings.ForearmSize * armWidthScale,
		Part.Hand => Settings.ForearmSize * handWidthScale *
			(!scaleLeftHandOnly || bone.Arm == Side.Left ? runtimeHandWidthScale : 1),
		Part.Thigh => Settings.ThighSize,
		Part.Shin => Settings.ShinSize,
		_ => Settings.FootSize,
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

	float TaperedWidth(Bone bone, float width) => width * (1 - Math.Clamp(
		bone.Part == Part.Chest ? Settings.ChestTaper :
		bone.Part == Part.Neck ? Settings.NeckTaper :
		bone.Leg != Side.None || bone.Part is Part.Bicep or Part.Forearm or Part.Hand ? Settings.LimbTaper : 0, 0, 0.75f));

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
		neuralHeadUVRadius = new Vector2(HeadRadius) / (Vector2)NeuralSize;
	}

	public bool OverlapsCombatBounds(in RectInt worldBounds, CombatRole role = CombatRole.Vulnerable)
	{
		var bounds = (Rect)(worldBounds - owner.Position);
		if (UsesBodyCombat)
			return BodyContact(bounds, role, out _) ||
				bounds.Overlaps(new Circle(projectedPose[head], HeadRadius + Settings.CombatBoundsPadding), out _);
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
		return bounds.Overlaps(new Circle(projectedPose[head], HeadRadius + Settings.CombatBoundsPadding), out _);
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

	// A sculpted body fights with what's drawn: each piece's screen footprint, flagged per piece.
	bool UsesBodyCombat => Settings.SculptedBody && bodyPrimitives.Count > 0;

	// Whether rig-local bounds touch a piece that plays `role`; contact is the point of the body nearest the bounds'
	// center (the center itself when it's inside), where a hit lands.
	bool BodyContact(in Rect bounds, CombatRole role, out Vector2 contact)
	{
		var need = role == CombatRole.Harmful ? BodyCombat.Harmful : BodyCombat.Vulnerable;
		var center = bounds.Center;
		var (touched, nearest) = (false, float.MaxValue);
		contact = center;
		foreach (var piece in bodyPrimitives)
		{
			if (piece.Carve || (piece.Combat & need) == 0)
				continue;
			var (a, b) = (new Vector2(piece.A.X, piece.A.Y), new Vector2(piece.B.X, piece.B.Y));
			if (!FootprintOverlaps(bounds, a, b, piece.RadiusA, piece.RadiusB))
				continue;
			touched = true;
			var along = b - a;
			var t = Math.Clamp(Vector2.Dot(center - a, along) / MathF.Max(along.LengthSquared(), 0.0001f), 0, 1);
			var axis = a + along * t;
			var radius = float.Lerp(piece.RadiusA, piece.RadiusB, t);
			var outward = center - axis;
			var distance = outward.Length();
			if (distance - radius < nearest)
			{
				nearest = distance - radius;
				contact = distance <= radius ? center : axis + outward / distance * radius;
			}
		}
		return touched;
	}

	// A round cone seen from the front is a tapered capsule; the gap to a rect along it is convex in t.
	static bool FootprintOverlaps(in Rect bounds, Vector2 a, Vector2 b, float radiusA, float radiusB)
	{
		var (center, half) = (bounds.Center, bounds.Size * 0.5f);
		float Gap(float t) => Vector2.Max(Vector2.Abs(Vector2.Lerp(a, b, t) - center) - half, Vector2.Zero).Length() -
			float.Lerp(radiusA, radiusB, t);
		var (low, high) = (0f, 1f);
		for (var i = 0; i < 12; i++)
		{
			var (left, right) = (float.Lerp(low, high, 1 / 3f), float.Lerp(low, high, 2 / 3f));
			if (Gap(left) < Gap(right))
				high = right;
			else
				low = left;
		}
		return Gap((low + high) * 0.5f) <= 0;
	}

	public void DrawCombatBounds(Batcher batcher)
	{
		var color = new Color(0x39e6c5);
		if (UsesBodyCombat)
		{
			foreach (var piece in bodyPrimitives)
			{
				if (piece.Carve || piece.Combat == BodyCombat.None)
					continue;
				var tint = piece.Combat == BodyCombat.Harmful ? new Color(0xe64a39) : piece.Combat == BodyCombat.Vulnerable ? new Color(0xe6d739) : color;
				var from = owner.Position + new Vector2(piece.A.X, piece.A.Y);
				var to = owner.Position + new Vector2(piece.B.X, piece.B.Y);
				batcher.CircleLine(from, piece.RadiusA, 1, 12, tint);
				batcher.CircleLine(to, piece.RadiusB, 1, 12, tint);
				batcher.Line(from, to, 1, tint * 0.5f);
			}
			batcher.CircleLine(owner.Position + projectedPose[head], HeadRadius + Settings.CombatBoundsPadding, 1, 14, color);
			return;
		}
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
		batcher.CircleLine(owner.Position + projectedPose[head], HeadRadius + Settings.CombatBoundsPadding,
			1, 14, color);
	}

	public Rect GetVisualBounds()
	{
		var headRadius = HeadRadius + 1.4f;
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
		if (CurrentSculptedBody() is { ColorTexture: { } bodyTexture } sculpted)
			bounds = bounds.Conflate(new Rect(sculpted.Origin, new Vector2(bodyTexture.Width, bodyTexture.Height)));
		if (Assets.GetCostume(Settings.CostumeId) != null)
		{
			BuildDrawList();
			foreach (var item in drawItems)
				if (item.Kind == RigDrawKind.Costume)
					bounds = bounds.Conflate(CostumeBounds(item));
		}
		return bounds + owner.Position;
	}

	// Each pass rebuilds the rig's depth-ordered item list, then draws from it.
	public void Draw(Batcher batcher, Color tint, bool silhouette, DrawParts parts = DrawParts.All)
	{
		BuildDrawList();
		DrawItems(batcher, tint, silhouette, parts);
	}

	void BuildDrawList()
	{
		drawItems.Clear();
		var wireIndex = 0;
		var accessoryIndex = 0;
		var headAdded = false;
		var headDepth = jointDepth[head];
		if (Settings.SculptedBody)
		{
			drawItems.Add(new(RigDrawKind.Body, -1));
			drawItems.Add(new(RigDrawKind.Head, -1));
			AddItemsThrough(ref wireIndex, ref accessoryIndex, float.PositiveInfinity);
			return;
		}
		var costume = Assets.GetCostume(Settings.CostumeId);
		Span<bool> added = stackalloc bool[bones.Length];
		added.Clear();
		foreach (var boneIndex in boneOrder)
		{
			var bone = bones[boneIndex];
			if (!DrawSpineToClavicle && bone.From == spine3 && bone.Arm != Side.None)
				continue;
			var boneDepth = BoneDepth(bone);
			if (Accessory != null && !headAdded && headDepth <= boneDepth)
			{
				AddItemsThrough(ref wireIndex, ref accessoryIndex, headDepth);
				drawItems.Add(new(RigDrawKind.Head, -1, false));
				headAdded = true;
			}
			AddItemsThrough(ref wireIndex, ref accessoryIndex, boneDepth);
			BoneWidths(bone, out var fromWidth, out var toWidth);
			var rings = bone.Part == Part.Hand ? JointRings.None : Rings<Bone>(bones, boneIndex, added);
			added[boneIndex] = true;
			drawItems.Add(new(bone.Part == Part.Hand ? RigDrawKind.Hand : RigDrawKind.Bone, boneIndex,
				bone.Leg == backLeg || bone.Arm == backArm, projectedPose[bone.From], projectedPose[bone.To],
				fromWidth, toWidth, BoneColor(bone, BoneRenderDepth(bone)), rings, bone.Parent >= 0));
			AddCostumeItems(drawItems, costume, bone.Tags);
		}
		if (!headAdded)
		{
			AddItemsThrough(ref wireIndex, ref accessoryIndex, headDepth);
			drawItems.Add(new(RigDrawKind.Head, -1, false));
		}
		AddItemsThrough(ref wireIndex, ref accessoryIndex, float.PositiveInfinity);
	}

	void AddItemsThrough(ref int wireIndex, ref int accessoryIndex, float depth)
	{
		while (wireIndex < wireOrder.Length && WireDepth(wires[wireOrder[wireIndex]]) <= depth)
			drawItems.Add(new(RigDrawKind.Wire, wireOrder[wireIndex++], false));
		while (Accessory != null && accessoryIndex < Accessory.DrawItemCount &&
			Accessory.GetDrawItemDepth(accessoryIndex) <= depth)
			drawItems.Add(new(RigDrawKind.Accessory, accessoryIndex++, false));
	}

	static bool Includes(RigDrawItem item, DrawParts parts) => parts switch
	{
		DrawParts.All => true,
		DrawParts.BehindPlayer => item.BehindPlayer,
		_ => !item.BehindPlayer,
	};

	RigSkin Skin(Surface? surface, Texture? texture, float amount, bool normals)
		=> new(SkipSurfaces ? null : surface, texture, amount, normals, NeuralSize, NeuralOrigin, surfaceMaterialOrigin,
			owner.Time.SecondsF, NoSurfaces: SkipSurfaces);

	void DrawItems(Batcher batcher, Color tint, bool silhouette, DrawParts parts)
	{
		if (silhouette)
		{
			DrawSilhouette(batcher, tint, parts);
			return;
		}

		var skin = Skin(Settings.SkinMode == RigSkinMode.Surface ? Assets.GetSurface(Settings.SurfaceId) : null,
			Settings.SkinMode == RigSkinMode.NCA && Neural.NeuralColorAmount > 0 ? neuralSkin.OutputTexture : null,
			Neural.NeuralColorAmount, normals: false);
		skin.Push(batcher);
		var outline = Multiply(Settings.OutlineColor, tint);
		var rimmed = Settings.OutlineMode == RigOutlineMode.Silhouette;
		if (rimmed)
			DrawSilhouette(batcher, outline, parts);
		var frontLimbOutline = Color.Black * Settings.FrontLimbOutline * (tint.A / 255f);
		foreach (var item in drawItems)
		{
			if (!Includes(item, parts))
				continue;
			var pieceOutline = !rimmed ? outline
				: item.Kind is RigDrawKind.Bone or RigDrawKind.Hand && InFrontOfBody(bones[item.Index]) ? frontLimbOutline
				: Color.Transparent;
			switch (item.Kind)
			{
				case RigDrawKind.Costume:
					DrawCostume(batcher, item, neuralBoneUVs[item.Index], Multiply(tint, CostumeShade(bones[item.Index])),
						rimmed ? Color.Transparent : Settings.OutlineColor, skin);
					break;
				case RigDrawKind.Bone:
					DrawBone(batcher, item, neuralBoneUVs[item.Index], Multiply(item.Color, tint), pieceOutline, skin);
					break;
				case RigDrawKind.Hand:
					var color = Multiply(item.Color, tint);
					var spinAmount = HandSpinAmount(bones[item.Index]);
					DrawHand(batcher, item.From, item.To, item.FromWidth, item.ToWidth, spinAmount, 2, pieceOutline);
					DrawHand(batcher, item.From, item.To, item.FromWidth, item.ToWidth, spinAmount, 0, color);
					DrawHandPalmSkin(batcher, item, color, skin);
					break;
				case RigDrawKind.Head:
					DrawHead(batcher, tint, silhouette: false, skin, outlined: !rimmed);
					break;
				case RigDrawKind.Body:
					DrawBody(batcher, tint, silhouette: false, outlined: !rimmed);
					break;
				case RigDrawKind.Wire:
					DrawWire(batcher, item.Index, tint, silhouette: false);
					break;
				case RigDrawKind.Accessory:
					Accessory!.DrawItem(batcher, item.Index, null);
					break;
			}
		}
		skin.Pop(batcher);
	}

	// Bone and hand outline shapes go down first so the silhouette is their union; the other items follow in order.
	void DrawSilhouette(Batcher batcher, Color tint, DrawParts parts)
	{
		foreach (var item in drawItems)
		{
			if (!Includes(item, parts))
				continue;
			if (item.Kind == RigDrawKind.Costume)
				DrawCostume(batcher, item, default, tint, tint, default, silhouette: true);
			else if (item.Kind == RigDrawKind.Hand)
				DrawHand(batcher, item.From, item.To, item.FromWidth, item.ToWidth,
					HandSpinAmount(bones[item.Index]), 2, tint);
			else if (item.Kind == RigDrawKind.Bone)
				DrawBoneSilhouette(batcher, item, tint);
			else if (item.Kind == RigDrawKind.Body)
				DrawBody(batcher, tint, silhouette: true);
		}
		foreach (var item in drawItems)
		{
			if (!Includes(item, parts))
				continue;
			switch (item.Kind)
			{
				case RigDrawKind.Head:
					DrawHead(batcher, tint, silhouette: true, default);
					break;
				case RigDrawKind.Wire:
					DrawWire(batcher, item.Index, tint, silhouette: true);
					break;
				case RigDrawKind.Accessory:
					Accessory!.DrawItem(batcher, item.Index, tint);
					break;
			}
		}
	}

	void DrawBody(Batcher batcher, Color tint, bool silhouette, bool outlined = true)
	{
		if (CurrentSculptedBody() is not { ColorTexture: { } color } body)
			return;
		if (silhouette || outlined)
			batcher.Image(body.MaskTexture!, body.Origin, silhouette ? tint : Multiply(Settings.OutlineColor, tint));
		if (!silhouette)
			batcher.Image(color, body.Origin, tint);
	}

	// Re-marched once per pose step, however many passes draw it.
	SculptedBody? CurrentSculptedBody()
	{
		if (!Settings.SculptedBody)
			return null;
		sculptedBody ??= new(owner.Game.Manager.GraphicsDevice);
		if (!sculptedBodyDirty)
			return sculptedBody;
		sculptedBodyDirty = false;
		BuildBodyPrimitives();
		var ppm = Settings.PixelsPerMeter;
		var shading = new BodyShading(Settings.BodyShading, Settings.BodyShadow,
			new(Settings.SkinMetallic, Settings.SkinRoughness, Settings.Specular, 0), new(1, Settings.MetalRoughness, Settings.Specular, 0),
			0.05f * ppm, jointDepth[pelvis] * ppm, 0.25f * ppm, Settings.BodyDepthShade);
		Span<BodyLight> lights = stackalloc BodyLight[SculptedBody.MaxLights];
		sculptedBody.Render(CollectionsMarshal.AsSpan(bodyPrimitives), shading,
			lights[..GatherLights(lights, owner.RenderPosition + TorsoCenter, ppm)], owner.RenderPosition);
		return sculptedBody;
	}

	// The scene lights that reach most strongly into `reach` pixels around a world point, strongest first.
	int GatherLights(Span<BodyLight> into, Vector2 center, float reach)
	{
		if (owner.Game.View is not { } view)
			return 0;
		Span<float> scores = stackalloc float[into.Length];
		var count = 0;
		foreach (var light in view.SceneLights)
		{
			var score = light.ColorIntensity.W * Math.Clamp(1 - (Vector2.Distance(light.Position, center) - reach) / light.Radius, 0, 1);
			if (score <= 0 || count == into.Length && score <= scores[count - 1])
				continue;
			var at = Math.Min(count, into.Length - 1);
			for (; at > 0 && scores[at - 1] < score; at--)
			{
				scores[at] = scores[at - 1];
				into[at] = into[at - 1];
			}
			scores[at] = score;
			var color = light.ColorIntensity;
			into[at] = new(light.Position, light.Height, light.Radius, new Vector3(color.X, color.Y, color.Z) * color.W);
			count = Math.Min(count + 1, into.Length);
		}
		return count;
	}

	public void DrawEmissive(Batcher batcher)
	{
		if (CurrentSculptedBody() is { SpecularTexture: { } specular } body)
			batcher.Image(specular, body.Origin, Color.White);
		if (CurrentSculptedHead() is { SpecularTexture: { } headSpecular } sculpt)
			batcher.Image(headSpecular, projectedPose[head], new Vector2(sculpt.Size * 0.5f), Vector2.One, 0, Color.White);
	}

	// The fleshy robot: soft masses smooth-unioned over a torso, hard metal joints, forearms, shins, hands and feet.
	// Sizes are meters; positions come from the projected pose (so leans, IK and squish carry over) plus its depth,
	// which mirrors rather than rotates on a turn, so the front always faces the camera.
	void BuildBodyPrimitives()
	{
		const int Flesh = 0, Metal = 1;
		var ppm = Settings.PixelsPerMeter;
		var bulk = ppm * Settings.BodyBulk;
		var blend = Settings.FleshBlend * ppm;
		var machined = 0.01f * ppm;
		var muscle = 0.02f * ppm; // a tight blend, so each muscle keeps a crease where it meets the limb
		var list = bodyPrimitives;
		list.Clear();

		var side = Vector3.Normalize(V(rightClavicle) - V(leftClavicle));
		var up = Vector3.Normalize(V(neck) - V(pelvis));
		var forward = Vector3.Normalize(Vector3.Cross(up, side));
		var toes = V(leftLeg.Toe) - V(leftLeg.Ankle) + V(rightLeg.Toe) - V(rightLeg.Ankle);
		if (Vector3.Dot(forward, toes) < 0)
			forward = -forward;

		var (torso, joints) = (Settings.TorsoColor, Settings.JointColor);
		var hips = Settings.HipSize;
		Add(V(leftLeg.Hip), V(rightLeg.Hip), 0.1f * hips, 0.1f * hips, 0, joints, Metal);
		Add(V(spine1), V(spine2), 0.13f, 0.14f, blend, torso);
		Add(V(spine2), V(spine3), 0.15f, 0.17f, blend, torso);
		Add(V(leftClavicle) - up * 0.06f * bulk, V(rightClavicle) - up * 0.06f * bulk, 0.12f, 0.12f, blend, torso);
		// Pecs are flat plates from the sternum up to the armpit, not balls: they barely stand off the ribcage. A shallow
		// groove along each one's underside gives it the crisp lower edge.
		var chest = Vector3.Lerp(V(spine2), V(spine3), 0.8f);
		var pecs = Settings.ChestMass;
		if (pecs > 0)
		{
			for (var s = -1; s <= 1; s += 2)
			{
				var (inner, outer) = Pec(s);
				Add(inner, outer, 0.06f * pecs, 0.05f * pecs, muscle, Settings.ChestColor);
			}
			for (var s = -1; s <= 1; s += 2)
			{
				var (inner, outer) = Pec(s);
				var under = forward * 0.02f * bulk;
				list.Add(new(inner - up * 0.055f * bulk * pecs + under, 0.012f * bulk,
					outer - up * 0.045f * bulk * pecs + under, 0.012f * bulk, 0.01f * ppm, default, Carve: true));
			}
		}
		if (Settings.Abs > 0)
			for (var row = 0; row < 3; row++)
			{
				var center = Vector3.Lerp(V(pelvis), V(spine2), 0.35f + row * 0.25f) + forward * 0.12f * bulk;
				for (var s = -1; s <= 1; s += 2)
				{
					var ab = center + side * s * 0.045f * bulk;
					Add(ab, ab, 0.04f * Settings.Abs, 0.04f * Settings.Abs, 0.015f * ppm, Settings.AbsColor);
				}
			}
		Add(V(neck), V(head), 0.075f, 0.07f, blend, torso);
		var (arms, forearms, hands) = (Settings.ArmColor, Settings.ForearmColor, Settings.HandColor);
		var (legs, shins, feet) = (Settings.LegColor, Settings.ShinColor, Settings.FootColor);
		Limb(leftUpperArm, leftLowerArm, leftHand, leftPalm, 0.1f, 0.06f, 0.05f, 0.06f, 0.05f, arms, forearms, hands);
		Limb(rightUpperArm, rightLowerArm, rightHand, rightPalm, 0.1f, 0.06f, 0.05f, 0.06f, 0.05f, arms, forearms, hands);
		Limb(leftLeg.Hip, leftLeg.Knee, leftLeg.Ankle, leftLeg.Toe, 0.13f * hips, 0.09f * hips, 0.07f, 0.075f, 0.055f, legs, shins, null);
		Limb(rightLeg.Hip, rightLeg.Knee, rightLeg.Ankle, rightLeg.Toe, 0.13f * hips, 0.09f * hips, 0.07f, 0.075f, 0.055f, legs, shins, null);
		Boot(leftLeg);
		Boot(rightLeg);
		// The codpiece: a rounded mass below the hip joints, pushed forward and smoothed into the hips and thighs.
		if (Settings.Codpiece > 0)
		{
			var crotch = Vector3.Lerp(V(leftLeg.Hip), V(rightLeg.Hip), 0.5f) + (forward * 0.07f - up * 0.06f) * bulk;
			Add(crotch + up * 0.03f * bulk, crotch + (forward * 0.02f - up * 0.04f) * bulk,
				0.065f * Settings.Codpiece, 0.055f * Settings.Codpiece, blend, legs);
		}
		for (var s = -1; s <= 1; s += 2)
		{
			var (upperArm, lowerArm) = s < 0 ? (leftUpperArm, leftLowerArm) : (rightUpperArm, rightLowerArm);
			Muscle(V(upperArm), V(lowerArm), 0.3f, 0.7f, forward, 0.03f, 0.06f, 0.05f, arms); // biceps
			Muscle(V(upperArm), V(lowerArm), 0.15f, 0.6f, -forward, 0.03f, 0.06f, 0.045f, arms); // triceps
			var leg = s < 0 ? leftLeg : rightLeg;
			var (hip, knee, ankle) = (V(leg.Hip), V(leg.Knee), V(leg.Ankle));
			var front = Vector3.Normalize(V(leg.Toe) - ankle);
			Muscle(hip, knee, 0.25f, 0.8f, front, 0.04f, 0.085f * hips, 0.065f, legs); // quads
			Muscle(hip, knee, 0.72f, 0.9f, front * 0.5f - side * s, 0.05f, 0.045f, 0.04f, legs); // the teardrop above the knee
			Muscle(hip, knee, 0.2f, 0.75f, -front, 0.04f, 0.08f * hips, 0.06f, legs); // hamstrings
			// Each glute's inner edge rides just past the midline, so the two meet in a crease at any size.
			if (Settings.Glutes > 0)
			{
				var radius = 0.085f * Settings.Glutes;
				var midline = Vector3.Lerp(V(leftLeg.Hip), V(rightLeg.Hip), 0.5f);
				var glute = midline + (side * s * radius * 0.85f - forward * 0.07f - up * 0.03f) * bulk;
				Add(glute + up * 0.02f * bulk, glute - (up * 0.06f + forward * 0.01f) * bulk, radius, radius * 0.88f, muscle, legs);
			}
			Muscle(knee, ankle, 0.15f, 0.55f, -front, 0.05f, 0.07f, 0.045f, Settings.CalfColor); // calf, fleshed over the metal shin
		}
		// Claws ride the finger joints, so they curl with the animation's grip, and run on past the fingertips.
		if (Settings.Claws > 0)
			foreach (var claws in (ReadOnlySpan<int[]>)[leftClaws, rightClaws])
				for (var i = 0; i < claws.Length; i += 3)
				{
					var (knuckle, middle, tip) = (V(claws[i]), V(claws[i + 1]), V(claws[i + 2]));
					Add(knuckle, middle, 0.022f, 0.016f, machined, Settings.ClawColor, Metal);
					Add(middle, middle + (tip - middle) * (1 + Settings.Claws * 1.5f), 0.016f, 0.003f, machined, Settings.ClawColor, Metal);
				}
		for (var s = -1; s <= 1; s += 2)
		{
			var clavicle = s < 0 ? leftClavicle : rightClavicle;
			Add(V(neck), V(clavicle), 0.08f, 0.07f, blend, torso);
		}
		// Dents carve in last; a flesh dent springing back past flush bulges out for a moment instead.
		foreach (var dent in dents)
		{
			var (origin, axis, across, outward) = DentFrame(dent.Bone);
			var surface = origin + (axis * dent.Surface.X + across * dent.Surface.Y + outward * dent.Surface.Z) * ppm;
			var normal = axis * dent.Normal.X + across * dent.Normal.Y + outward * dent.Normal.Z;
			var (radius, depth) = (dent.Radius * ppm, dent.Depth * ppm);
			var center = depth >= 0 ? surface + normal * (radius - depth) : surface - normal * (radius + depth);
			list.Add(new(center, radius, center, radius, (dent.Flesh ? 0.02f : 0.004f) * ppm, dent.Color, dent.Flesh ? Flesh : Metal,
				Carve: depth >= 0));
		}

		// A fleshy upper segment with its mass at the root, a metal joint, a machined lower segment and end.
		void Limb(int root, int joint, int end, int tip, float mass, float upper, float lower, float jointRadius, float endRadius,
			Color upperColor, Color lowerColor, Color? endColor)
		{
			Add(V(root), V(root), mass, mass, blend, upperColor);
			Add(V(root), V(joint), upper, lower, blend, upperColor);
			Add(V(joint), V(joint), jointRadius, jointRadius, 0, joints, Metal);
			Add(V(joint), V(end), jointRadius, endRadius, machined, lowerColor, Metal);
			if (endColor is { } color)
				Add(V(end), V(tip), endRadius, endRadius * 1.1f, machined, color, Metal);
		}

		// A flat boot in the foot's own frame: the ball and toe tip lie along the sole, the heel sits on that line under
		// the ankle (pushed back by HeelLength), and a shaft runs from partway up the shin down into the heel. Each
		// piece rests its underside on the floor, a sole's thickness below the line, so it rolls and lifts with the foot.
		void Boot(Leg leg)
		{
			var (knee, ankle, ball, tip) = (V(leg.Knee), V(leg.Ankle), V(leg.Toe), V(leg.Tip));
			var sole = Vector3.Normalize(tip - ball);
			if (!float.IsFinite(sole.X))
				sole = forward;
			var under = ball + sole * Vector3.Dot(ankle - ball, sole);
			var down = Vector3.Normalize(under - ankle);
			if (!float.IsFinite(down.X))
				down = Vector3.UnitY;
			var size = Settings.BootSize;
			var floor = 0.015f * ppm;
			Vector3 OnFloor(Vector3 point, float radius) => point - down * (radius * size * bulk - floor);
			var heel = OnFloor(under - sole * Settings.HeelLength * ppm, 0.055f);
			var ballCenter = OnFloor(ball, 0.05f);
			Add(Vector3.Lerp(knee, ankle, 1 - Settings.BootHeight), heel, 0.06f * size, 0.055f * size, 0.02f * ppm, feet, Metal);
			Add(heel, ballCenter, 0.055f * size, 0.05f * size, machined, feet, Metal);
			Add(ballCenter, OnFloor(tip, 0.035f), 0.05f * size, 0.035f * size, machined, feet, Metal);
		}

		(Vector3 Inner, Vector3 Outer) Pec(int s) => (chest + (side * s * 0.045f + forward * 0.12f) * bulk,
			Vector3.Lerp(chest, V(s < 0 ? leftUpperArm : rightUpperArm), 0.65f) + forward * 0.08f * bulk);

		// A mass along part of a segment, pushed off its axis toward a side of the limb.
		void Muscle(Vector3 a, Vector3 b, float from, float to, Vector3 toward, float offset, float radiusA, float radiusB, Color color)
		{
			var axis = Vector3.Normalize(b - a);
			var push = toward - axis * Vector3.Dot(toward, axis);
			push = push.LengthSquared() > 0.000001f ? Vector3.Normalize(push) * offset * bulk : Vector3.Zero;
			Add(Vector3.Lerp(a, b, from) + push, Vector3.Lerp(a, b, to) + push, radiusA, radiusB, muscle, color);
		}

		void Add(Vector3 a, Vector3 b, float radiusA, float radiusB, float k, Color color, int material = Flesh)
			=> list.Add(new(a, radiusA * bulk, b, radiusB * bulk, k, color, material));

		Vector3 V(int joint) => new(projectedPose[joint], jointDepth[joint] * ppm);
	}

	bool InFrontOfBody(Bone bone)
		=> (bone.Arm != Side.None || bone.Leg != Side.None) && (bone.Tags & (RigTags.Shoulder | RigTags.Hip)) == 0 &&
			BoneDepth(bone) - jointDepth[pelvis] > Settings.FrontLimbDepth;

	void DrawHead(Batcher batcher, Color tint, bool silhouette, in RigSkin skin, bool outlined = true)
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
		if (CurrentSculptedHead() is { ColorTexture: { } sculpted } sculpt)
		{
			var origin = new Vector2(sculpt.Size * 0.5f);
			if (silhouette || outlined)
				batcher.Image(sculpt.MaskTexture!, position, origin, Vector2.One, 0,
					silhouette ? tint : Multiply(Settings.OutlineColor, tint));
			if (!silhouette)
				batcher.Image(sculpted, position, origin, Vector2.One, 0, tint);
			return;
		}
		if (outlined)
			batcher.Circle(position, HeadRadius + 1.4f, 14, silhouette ? tint : Multiply(Settings.OutlineColor, tint));
		batcher.Circle(position, HeadRadius, 14, silhouette ? tint : Multiply(Settings.NearColor, tint));
		skin.Circle(batcher, position, HeadRadius, neuralHeadUV, neuralHeadUVRadius,
			Multiply(Settings.NearColor, tint));
		if (!silhouette && Settings.EyeSize > 0)
			batcher.Circle(position + lookDirection * 2.4f, VisualEyeScale * Settings.EyeSize * eyeScale, 8,
				Multiply(Settings.AccentColor, tint));
	}

	public void DrawNormals(Batcher batcher, DrawParts parts = DrawParts.All)
	{
		BuildDrawList();
		DrawItemNormals(batcher, parts);
	}

	void DrawItemNormals(Batcher batcher, DrawParts parts)
	{
		var flat = new Color(0x8080ff);
		var skin = Skin(Settings.SkinMode == RigSkinMode.Surface &&
			Assets.GetSurface(Settings.SurfaceId) is { HasNormal: true } foundSurface ? foundSurface : null,
			Settings.SkinMode == RigSkinMode.NCA && Neural.NeuralNormalAmount > 0 ? neuralSkin.OutputNormalTexture : null,
			Neural.NeuralNormalAmount, normals: true);
		skin.Push(batcher);
		var phaseSeed = (uint)(animationPhase * uint.MaxValue);
		foreach (var item in drawItems)
		{
			if (!Includes(item, parts))
				continue;
			switch (item.Kind)
			{
				case RigDrawKind.Costume:
					DrawCostume(batcher, item, neuralBoneUVs[item.Index], Color.White, Settings.OutlineColor, skin);
					break;
				case RigDrawKind.Bone:
					DrawBoneNormals(batcher, item, neuralBoneUVs[item.Index], Settings.NormalRoundness,
						Settings.NormalIrregularity, phaseSeed, skin);
					break;
				case RigDrawKind.Hand:
					DrawHand(batcher, item.From, item.To, item.FromWidth, item.ToWidth,
						HandSpinAmount(bones[item.Index]), 2, flat);
					DrawHandPalmSkin(batcher, item, flat, skin);
					break;
				case RigDrawKind.Head:
					DrawHeadNormals(batcher, flat, skin);
					break;
				case RigDrawKind.Body:
					if (CurrentSculptedBody() is { NormalTexture: { } bodyNormals } body)
						batcher.Image(bodyNormals, body.Origin, Color.White);
					break;
				case RigDrawKind.Wire:
					DrawWireNormals(batcher, item.Index);
					break;
				case RigDrawKind.Accessory:
					Accessory!.DrawItem(batcher, item.Index, flat);
					break;
			}
		}
		skin.Pop(batcher);
	}

	void DrawHeadNormals(Batcher batcher, Color flat, in RigSkin skin)
	{
		if (TryGetHeadSpriteFrame(out var frame, out _))
		{
			DrawHeadSprite(batcher, frame.NormalSubtexture, Color.White);
			return;
		}

		var position = projectedPose[head];
		if (CurrentSculptedHead() is { NormalTexture: { } normals } sculpt)
		{
			batcher.Image(normals, position, new Vector2(sculpt.Size * 0.5f), Vector2.One, 0, Color.White);
			return;
		}
		if (Settings.NormalRoundness > 0)
			DrawSphereNormals(batcher, position, HeadRadius + 1.4f, 14,
				Settings.NormalRoundness, Settings.NormalIrregularity, 0x68bc21ebu);
		else
			batcher.Circle(position, HeadRadius + 1.4f, 14, flat);
		skin.Circle(batcher, position, HeadRadius, neuralHeadUV, neuralHeadUVRadius, Color.White);
	}

	bool TryGetHeadSpriteFrame(out Sprite.Frame frame, out int frameIndex)
	{
		if (headSprite is not { Frames.Count: > 0 } sprite)
		{
			frame = default;
			frameIndex = -1;
			return false;
		}

		var theta = HeadViewYaw();
		// Frames sample yaw clockwise: front, front-left, left, back-left, back, ...
		frameIndex = (int)MathF.Floor(theta / MathF.Tau * sprite.Frames.Count + 0.5f)
			% sprite.Frames.Count;
		frame = sprite.Frames[frameIndex];
		return true;
	}

	// How far the head is turned from facing the viewer, clockwise: what picks a head sprite's frame.
	float HeadViewYaw()
	{
		var cameraYaw = MathF.PI * 0.5f - Settings.ProjectionYaw * MathF.PI / 180f;
		var yaw = UpperBodyYaw();
		var cameraOpenYaw = MathF.Atan2(MathF.Sin(yaw), MathF.Abs(MathF.Cos(yaw)));
		return Calc.AngleWrap((cameraYaw - cameraOpenYaw) * PresentationMirror());
	}

	internal float ExtraHeadSpriteRoll; // MathF.PI * 0.39f;
	float ExtraHeadSpriteRollLerped;

	float HeadSpriteRoll()
	{
		var axis = projectedPose[head] - projectedPose[neck];
		var final = axis.LengthSquared() > 0.0001f ? axis.Angle() + MathF.PI * 0.5f : 0;
		final += ExtraHeadSpriteRollLerped + HeadRoll;
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

	float HandSpinAmount(Bone bone)
		=> !spinLeftHandOnly || bone.Arm == Side.Left ? Math.Clamp(fingerSpinSpeed / FingerSpinBlendSpeed, 0, 1) : 0;

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

	void DrawHandPalmSkin(Batcher batcher, in RigDrawItem item, Color color, in RigSkin skin)
	{
		var palmEnd = Vector2.Lerp(item.From, item.To, HandPalmRatio);
		var uv = neuralBoneUVs[item.Index];
		var palmUV = new BoneUV(uv.A, uv.B,
			Vector2.Lerp(uv.B, uv.C, HandPalmRatio), Vector2.Lerp(uv.A, uv.D, HandPalmRatio));
		skin.Bone(batcher, item.From, palmEnd, item.FromWidth * 0.72f, MathF.Max(item.FromWidth, item.ToWidth), palmUV, color);
	}

	static Vector2 NeuralUV(Vector2 position) => (position - (Vector2)NeuralOrigin) / (Vector2)NeuralSize;
}
