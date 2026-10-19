using System.Numerics;
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

internal sealed partial class HumanoidRigVisual(Actor owner, HumanoidRigSettings? configuredSettings = null,
	NeuralSurfaceSettings? configuredNeural = null, HumanoidNeuralReactionSettings? configuredReactions = null) : IDisposable
{
	public readonly record struct RuffProjection(Vector3 Pivot, Vector3 TurnedPivot,
		float TurnCos, float TurnSin, float CameraCos, float CameraSin, float PixelsPerMeter,
		Vector2 ScreenOffset, Vector2 Correction)
	{
		public Vector2 Project(Vector3 point, out float depth)
		{
			var turned = TurnedPivot + Turn(point - Pivot, TurnCos, TurnSin);
			depth = turned.X * CameraCos + turned.Z * CameraSin;
			return new Vector2(-turned.Z * CameraCos + turned.X * CameraSin, -point.Y) *
				PixelsPerMeter + ScreenOffset + Correction;
		}
	}

	public readonly record struct RuffPose(Vector3 Neck, Vector3 Head,
		Vector3 LeftUpperArm, Vector3 LeftLowerArm, Vector3 RightUpperArm, Vector3 RightLowerArm,
		RuffProjection Projection);

	// Rotates about +Y by the angle whose cosine and sine these are.
	static Vector3 Turn(Vector3 v, float cos, float sin) => new(v.X * cos + v.Z * sin, v.Y, -v.X * sin + v.Z * cos);

	enum Part { Torso, Chest, Neck, Bicep, Forearm, Hand, Thigh, Shin, Foot }
	enum Side { None, Left, Right }
	internal enum DrawParts { All, BehindPlayer, InFrontOfPlayer }

	readonly Actor owner = owner; // a field, so the bodies can reach it
	public IHumanoidRigAccessoryVisual? Accessory;

	// How the posed skeleton becomes pixels and combat bounds: drawn bones (BoneBody) or one ray-marched SDF (SculptBody).
	// Everything here is rig-local; the head is drawn by the visual, in the body's palette.
	interface IBody : IDisposable
	{
		(Color Near, Color Core, Color Far) HeadPalette { get; }
		void Update() { }
		void Posed() { }
		void Draw(Batcher batcher, Color tint, bool silhouette, DrawParts parts);
		void DrawNormals(Batcher batcher, DrawParts parts);
		void DrawEmissive(Batcher batcher) { }
		bool Overlaps(in Rect bounds, CombatRole role);
		void DrawCombatBounds(Batcher batcher, Color color);
		Rect Bounds(Rect head);
		void Dent(in RectInt attack, float strength) { }
		void Dent(Vector2 worldPoint, float strength) { }
		void PerturbNeuralTexture(NeuralTextureActor.PerturbContext context) { }
	}

	IBody? body;
	// Swapped when the style is toggled live.
	IBody Body
	{
		get
		{
			if (body == null || body is SculptBody != (Settings.BodyStyle == RigBodyStyle.Sculpted))
			{
				body?.Dispose();
				body = Settings.BodyStyle == RigBodyStyle.Sculpted ? new SculptBody(this) : new BoneBody(this);
			}
			return body;
		}
	}

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
	public float PlaybackSpeed = 1;
	public float PoseModifierWeight = 1;
	public Color HeadTint = Color.White;
	public bool GaitActive;
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
	// Sculpted bodies only: per region, 0..1, a limb charging an attack -- its pieces swell, blend softer and run toward
	// SculptHeatColor.
	public enum SculptRegion { LeftArm, RightArm, LeftLeg, RightLeg, Torso }
	public readonly float[] SculptHeat = new float[5];
	public Color SculptHeatColor = new(0xff7a2a);
	// Rotates the whole figure about its torso so its arms (neck to hands) point this way on screen, by ArmAimWeight.
	public Vector2 ArmAim = Vector2.UnitX;
	public float ArmAimWeight;
	public float PoseRoll { get; private set; } // radians, clockwise on screen
	// Radians clockwise on screen: bends the upper body about the pelvis.
	public float TorsoRoll;
	// Swings the take's right arm about its shoulder, by ArmReachWeight: its sweep since the take began is scaled by
	// ArmArcScale and turned ArmReachRoll radians (positive is down, ahead of the figure), then flipped top to bottom by
	// ArmReachFlip.
	public float ArmReachWeight, ArmReachRoll, ArmArcScale = 1;
	public bool ArmReachFlip;
	// Folds both legs out in front, near straight, by LegPikeWeight: the thighs LegPikeRaise radians up from hanging
	// down (the far one LegPikeSpread less), the knees bent LegPikeKneeBend. Hips and up are untouched.
	public float LegPikeWeight, LegPikeRaise, LegPikeKneeBend, LegPikeSpread;
	float? armArcFrom;
	float armReachOffset; // the arm's last turn, which eases back out once the reach lets go
	static float ArmReachRelease => 15f;
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
	readonly PendingImpulse[] pendingImpulses = new PendingImpulse[16];
	OzzJointMask upperBodyMask = null!;
	OzzJointMask lowerBodyMask = null!;
	OzzJointMask leftArmMask = null!;
	OzzJointMask leftHandMask = null!, rightHandMask = null!;
	// For joints outside the ragdoll (fingers): the nearest ancestor in it, whose springing they ride along with.
	int[] ragdollCarriers = [];
	OzzJointMask rightArmMask = null!;
	OzzRig.Clip upperBodyHitClip = null!;
	Sprite? headSprite;
	SculptedHead? sculptedHead;
	public float MouthOpen; // 0 closed .. 1 wide, for a sculpted head
	public Expression Face; // a sculpted head's expression; MouthOpen opens it further
	static float SculptedHeadMinRadius => 3f;
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
	float legTime;
	float neuralHitRemaining;
	Signs visualFacing;
	Signs targetFacing;
	float visualYaw;
	float turnFromYaw;
	float turnToYaw;
	float turnTime;
	float turnAmount;
	// A clip that turns the body itself (a walking turn), while it plays: its pelvis spin is taken out of the pose and the
	// facing turn follows it instead of a timed sweep, so the feet stay where the clip planted them.
	OzzRig.Clip? turnClip;
	bool clipTurn; // the turn in progress follows turnClip
	// The playing take is mirrored (left and right swapped across the body's midline) to keep its hand toward the viewer.
	// Accessors that name a side (LeftHandWorldPosition, leftHandOnly, ...) follow the take's sides, not the bones'.
	bool mirrored;
	int[] mirrorJoint = []; // each joint's opposite (itself on the midline)
	float leftLateral; // +1 or -1: the side of the rig's X axis its left joints lie on
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
	static float FingerSpinTargetSpeed => MathF.Tau * 7;
	static float FingerSpinAcceleration => MathF.Tau * 28;
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
	static float GaitHeadOffset => 1.1f;
	static float GaitShoulderOffset => 0.8f;
	static float GaitStrideOffset => 1.25f;
	static float TurnInertiaDelay => 0.35f;
	static float EyeTrackingSpeed => 8f;
	static float EyeSaccadeInterval => 0.55f;
	static float EyeSaccadeAmount => 0.12f;
	static float EyeReactionDuration => 0.18f;

	public IReadOnlyList<OzzRig.Clip> Clips => rig.Clips;
	public OzzRig.Clip ActiveClip => animator.ActiveClip;
	public float RangeStart => animator.RangeStart;
	public float RangeEnd => animator.RangeEnd;
	public bool AnimationFinished => animator.Finished;
	// Loops over the pelvis and legs in place of the playing clip's, by weight: an air pose under a ground swing.
	public RigTake? LegTake;
	public float LegWeight;
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
	public Vector2 LeftToeWorldPosition => owner.Position + projectedPose[mirrored ? rightLeg.Toe : leftLeg.Toe];
	public Vector2 RightToeWorldPosition => owner.Position + projectedPose[mirrored ? leftLeg.Toe : rightLeg.Toe];
	public Vector2 LeftHandWorldPosition => owner.Position + projectedPose[mirrored ? rightPalm : leftPalm];
	public Vector2 RightHandWorldPosition => owner.Position + projectedPose[mirrored ? leftPalm : rightPalm];
	public Vector2 RightPalm => projectedPose[mirrored ? leftPalm : rightPalm];
	// The bones playing the take's left side.
	Side TakeLeft => mirrored ? Side.Right : Side.Left;
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
		mirrorJoint = new int[rig.JointCount];
		for (var i = 0; i < mirrorJoint.Length; i++)
		{
			var name = rig.JointNames[i];
			var opposite = name.EndsWith("_l") ? name[..^1] + "r" : name.EndsWith("_r") ? name[..^1] + "l" : name;
			mirrorJoint[i] = rig.FindJoint(opposite) is >= 0 and var found ? found : i;
		}
		rightClavicle = RequiredJoint("clavicle_r");
		pelvis = RequiredJoint("pelvis");
		leftLeg = RequiredLeg("l");
		rightLeg = RequiredLeg("r");
		upperBodyMask = rig.CreateMask();
		upperBodyMask.Set("spine_01", 1, descendants: true);
		upperBodyMask.Set("spine_01", 0.25f);
		upperBodyMask.Set("spine_02", 0.6f);
		lowerBodyMask = rig.CreateMask();
		lowerBodyMask.Set("pelvis", 1, descendants: true);
		lowerBodyMask.Set("spine_01", 0, descendants: true);
		lowerBodyMask.Set("spine_01", 0.75f);
		lowerBodyMask.Set("spine_02", 0.4f);
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
		leftLateral = MathF.Sign(sampledPose[leftClavicle].X - sampledPose[rightClavicle].X);
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
		var rigYaw = RigYaw();
		var projection = new RuffProjection(pivot, Turn(pivot, MathF.Cos(rigYaw), MathF.Sin(rigYaw)),
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
		var transition = Transition(blend ? (blendDuration >= 0 ? blendDuration : -1) : 0, mirror: false);
		animator.Play(name, transition, restart, startTime);
	}

	// The animator's cross-fade for a clip change; with an inertial blend, none, and the switch is eased out here.
	// requested < 0 means the default. Leaving a turning clip, or changing mirroring, always blends inertially: the
	// animator would cross-fade the raw clips (a spun pelvis, unswapped sides), not the pose that was shown.
	float Transition(float requested, bool mirror)
	{
		keyPending = true;
		var inertial = turnClip != null || mirror != mirrored;
		turnClip = null;
		mirrored = mirror;
		if (requested == 0)
			return 0;
		if (Settings.InertialBlendTime <= 0 && !inertial)
			return requested > 0 ? requested : ClipBlendDuration;
		inertialPending = true;
		inertialDuration = requested > 0 ? requested : Settings.InertialBlendTime > 0 ? Settings.InertialBlendTime : ClipBlendDuration;
		return 0;
	}

	public void Play(RigTake take, float blendDuration = -1, bool restart = true, bool? mirror = null)
	{
		SetTakeModifierTargets(take.UpperBodyLeanDegrees, take.ArmWidthScale, take.HandWidthScale);
		var clip = rig.GetClip(take.Clip);
		var start = Math.Clamp(take.Start, 0, clip.Duration);
		var end = Math.Clamp(take.End, start, clip.Duration);
		PlaybackSpeed = Math.Max(0.01f, take.Speed);
		mirror ??= take.NearHand != RigHand.None && (take.NearHand == RigHand.Left) != LeftNear(FacingYaw(owner.Facing));
		armArcFrom = null;
		animator.Play(take.Clip, start, end, Transition(blendDuration, mirror.Value), restart);
	}

	public void Update(bool grounded, bool footIK, bool loop = true, bool advance = true, bool seekGround = false)
	{
		ExtraHeadSpriteRollLerped = Calc.Approach(ExtraHeadSpriteRollLerped, ExtraHeadSpriteRoll, owner.Time.Delta);

		EnsureState();
		Body.Update();
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
		turning = clipTurn = false;
	}

	// Plays a take that spins the body round (its pelvis turns) for the turn the owner's facing change starts: mirrored
	// if need be so it spins the way that passes the viewer, and the turn follows its spin until the clip changes.
	public void PlayTurn(RigTake take)
	{
		var clip = rig.GetClip(take.Clip);
		var spin = rig.Yaw(clip, pelvis, Math.Min(take.End, clip.Duration)) - rig.Yaw(clip, pelvis, take.Start);
		Play(take, mirror: spin * (FacingYaw(owner.Facing) - visualYaw) < 0);
		turnClip = animator.ActiveClip;
	}

	// Whether the rig's left side is toward the viewer at this yaw.
	bool LeftNear(float yaw) => leftLateral * MathF.Cos(yaw + Settings.ProjectionYaw * MathF.PI / 180f) > 0;

	// The yaw that faces the viewer.
	float CameraYaw => MathF.PI * 0.5f - Settings.ProjectionYaw * MathF.PI / 180f;

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

	// Facing right is facing left reflected about the yaw that faces the viewer: the same three-quarter view of the
	// front, from the other side. A turn between them sweeps through the front.
	public float FacingYaw(Signs facing) => facing == Signs.Negative ? 0 : 2 * CameraYaw;

	// +1 while the figure faces left of the viewer, -1 right: which way its body's sides lie on screen.
	public float FacingSide() => MathF.IEEERemainder(RigYaw() - CameraYaw, MathF.Tau) < 0 ? 1 : -1;

	void UpdateTurn(bool grounded)
	{
		if (owner.Facing != targetFacing)
		{
			targetFacing = owner.Facing;
			turnFromYaw = visualYaw;
			turnToYaw = FacingYaw(targetFacing);
			clipTurn = turnClip != null;
			// A clip turn goes the way the clip spins, however far round that is.
			if (clipTurn && ClipSpin(animator.RangeEnd) is var spin && spin != 0)
				while ((turnToYaw - turnFromYaw) * spin < 0)
					turnToYaw += MathF.Tau * MathF.Sign(spin);
			turnTime = turnAmount = 0;
			turning = true;
			ResetFeet();
		}
		if (clipTurn)
		{
			if (turnClip != null)
				return; // ApplyClipTurn drives it
			// The clip was cut short: finish on the clock from where it got to.
			clipTurn = false;
			turnTime = turnAmount * Settings.TurnDuration;
		}
		if (!turning || !Settings.TurnWhileMoving && (!grounded || MathF.Abs(owner.Velocity.X) > TurnStartSpeed))
			return;

		turnTime = MathF.Min(turnTime + visualDelta, Settings.TurnDuration);
		var progress = turnTime / Settings.TurnDuration;
		turnAmount = progress * progress * (3 - 2 * progress);
		visualYaw = float.Lerp(turnFromYaw, turnToYaw, turnAmount);
		if (turnTime >= Settings.TurnDuration)
			FinishTurn();
	}

	void FinishTurn()
	{
		visualFacing = targetFacing;
		visualYaw = FacingYaw(visualFacing);
		turnTime = turnAmount = 0;
		turning = clipTurn = false;
	}

	// Reflects the sampled pose across the body's midline (the rig's X = 0 plane), swapping each joint with its opposite.
	void ApplyMirror()
	{
		if (!mirrored)
			return;
		for (var i = 0; i < mirrorJoint.Length; i++)
		{
			var j = mirrorJoint[i];
			if (j < i)
				continue;
			var (pose, rotation) = (sampledPose[i], sampledRotations[i]);
			(sampledPose[i], sampledRotations[i]) = (Across(sampledPose[j]), Across(sampledRotations[j]));
			(sampledPose[j], sampledRotations[j]) = (Across(pose), Across(rotation));
		}
	}

	static Vector3 Across(Vector3 p) => p with { X = -p.X };
	static Quaternion Across(Quaternion q) => new(q.X, -q.Y, -q.Z, q.W);

	// How far turnClip's pelvis has spun since the start of its range, at a clip time.
	float ClipSpin(float time) => (rig.Yaw(turnClip!, pelvis, time) - rig.Yaw(turnClip!, pelvis, animator.RangeStart)) * (mirrored ? -1 : 1);

	// Takes turnClip's pelvis spin back out of the sampled pose, and moves the facing turn along by it instead, scaled
	// to the turn the figure actually needs.
	void ApplyClipTurn()
	{
		if (turnClip == null || animator.ActiveClip != turnClip)
			return;
		var spin = ClipSpin(animator.Time);
		var (cos, sin) = (MathF.Cos(-spin), MathF.Sin(-spin));
		var unspin = Quaternion.CreateFromAxisAngle(Vector3.UnitY, -spin);
		var pivot = sampledPose[pelvis];
		for (var i = 0; i < sampledPose.Length; i++)
		{
			sampledPose[i] = pivot + Turn(sampledPose[i] - pivot, cos, sin);
			sampledRotations[i] = unspin * sampledRotations[i];
		}
		if (!clipTurn)
			return;
		var total = ClipSpin(animator.RangeEnd);
		turnAmount = MathF.Abs(total) > 0.01f ? Math.Clamp(spin / total, 0, 1) : 1;
		visualYaw = float.Lerp(turnFromYaw, turnToYaw, turnAmount);
		if (animator.Finished)
			FinishTurn();
	}

	void UpdatePose(bool grounded, bool footIK, bool seekGround = false)
	{
		UpdateAnimationLayers();
		animator.Sample(sampledPose, sampledRotations);
		ApplyMirror();
		ApplyClipTurn();
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
		RotateMasked(upperBodyMask, TorsoRoll, projectedPose[pelvis]);
		ApplyArmReach();
		ApplyLegPike(rightLeg, LegPikeRaise);
		ApplyLegPike(leftLeg, LegPikeRaise - LegPikeSpread);
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
		SimulateGarments();
		if (Settings.PixelSnap)
			for (var i = 0; i < projectedPose.Length; i++)
				projectedPose[i] = projectedPose[i].Round();
		BuildGarmentFaces();
		SortBones();
		UpdateHeadLook();
		UpdateEye();
		Body.Posed();
	}

	Vector3 ViewJoint(int joint) => new(projectedPose[joint], jointDepth[joint] * Settings.PixelsPerMeter);

	// Rendered when drawn (it skips itself when nothing changed), so posed and stepped rigs stay current too.
	SculptedHead? CurrentSculptedHead()
	{
		if (!UsesSculptedHead)
			return null;
		sculptedHead ??= new(owner.Game.Manager.GraphicsDevice);
		var (near, core, far) = Body.HeadPalette;
		var center = owner.RenderPosition + projectedPose[head];
		Span<BodyLight> lights = stackalloc BodyLight[SculptedBody.MaxLights];
		// Head sprites count yaw the other way round.
		sculptedHead.Render(Assets.GetHeadShape(Settings.HeadShapeId), HeadRadius, -HeadViewYaw(), 0,
			HeadSpriteRoll(), Face with { MouthOpen = MathF.Max(Face.MouthOpen, MouthOpen) }, near, core, far, Settings.EyeSize > 0 ? Settings.AccentColor : default,
			Settings.Sculpt.BodyShading, new(Settings.Sculpt.SkinMetallic, Settings.Sculpt.SkinRoughness, Settings.Sculpt.Specular, 0),
			lights[..GatherLights(lights, center, HeadRadius)], center, stretch: Vector2.Lerp(Vector2.One, owner.Squish, Settings.SquishResponse));
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
		var headBefore = projectedPose[head].Y;
		RotateMasked(upperBodyMask, hunch * forward, pivot);

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

	void ApplyArmReach()
	{
		// Angles ahead of the figure, so a swing reads the same facing either way.
		var forward = (int)visualFacing;
		var shoulder = projectedPose[mirrored ? leftUpperArm : rightUpperArm];
		var arm = projectedPose[mirrored ? leftPalm : rightPalm] - shoulder;
		var angle = MathF.Atan2(arm.Y, arm.X * forward);
		armArcFrom ??= angle;
		var swing = armArcFrom.Value + ArmReachRoll + ArmArcScale * MathF.IEEERemainder(angle - armArcFrom.Value, MathF.Tau);
		armReachOffset = ArmReachWeight > 0 ? forward * ArmReachWeight * ((ArmReachFlip ? -swing : swing) - angle)
			: armReachOffset * MathF.Exp(-ArmReachRelease * visualDelta);
		RotateMasked(mirrored ? leftArmMask : rightArmMask, armReachOffset, shoulder);
	}

	void ApplyLegPike(Leg leg, float raise)
	{
		if (LegPikeWeight <= 0)
			return;
		var forward = (int)visualFacing;
		var hip = projectedPose[leg.Hip];
		var thigh = projectedPose[leg.Knee] - hip;
		var shin = projectedPose[leg.Ankle] - projectedPose[leg.Knee];
		var thighFrom = MathF.Atan2(thigh.Y, thigh.X);
		var shinFrom = MathF.Atan2(shin.Y, shin.X);
		// Hanging straight down is +90° on screen; raising turns toward the facing side.
		var thighTo = MathF.PI / 2 - forward * raise;
		var shinTo = MathF.PI / 2 - forward * (raise - LegPikeKneeBend);
		var thighAngle = thighFrom + LegPikeWeight * MathF.IEEERemainder(thighTo - thighFrom, MathF.Tau);
		var shinAngle = shinFrom + LegPikeWeight * MathF.IEEERemainder(shinTo - shinFrom, MathF.Tau);

		var ankle = projectedPose[leg.Ankle];
		projectedPose[leg.Knee] = hip + Calc.AngleToVector(thighAngle, thigh.Length());
		projectedPose[leg.Ankle] = projectedPose[leg.Knee] + Calc.AngleToVector(shinAngle, shin.Length());
		var foot = Matrix3x2.CreateRotation(shinAngle - shinFrom, ankle) * Matrix3x2.CreateTranslation(projectedPose[leg.Ankle] - ankle);
		projectedPose[leg.Toe] = Vector2.Transform(projectedPose[leg.Toe], foot);
		projectedPose[leg.Tip] = Vector2.Transform(projectedPose[leg.Tip], foot);
	}

	void RotateMasked(OzzJointMask mask, float angle, Vector2 pivot)
	{
		if (MathF.Abs(angle) < 0.0001f)
			return;
		var rotation = Matrix3x2.CreateRotation(angle, pivot);
		for (var i = 0; i < projectedPose.Length; i++)
			if (mask[i] > 0)
				projectedPose[i] = Vector2.Lerp(projectedPose[i], Vector2.Transform(projectedPose[i], rotation), mask[i]);
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
		if (LegTake is { } legs && LegWeight > 0 && rig.TryGetClip(legs.Clip, out var legClip))
		{
			var end = MathF.Min(legs.End, legClip.Duration);
			legTime = legs.Start + (MathF.Max(0, legTime - legs.Start) + visualDelta * legs.Speed) % MathF.Max(0.0001f, end - legs.Start);
			animator.SetOverride(legClip, legTime, LegWeight, lowerBodyMask);
		}
		else
			animator.ClearOverride();
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
		if (turning && !clipTurn && Settings.TurnInertia > 0)
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

	float PelvisYaw() => OzzRig.Yaw(sampledRotations[pelvis]);

	float OverrideYaw(float yaw)
	{
		if (YawOverride is { } target)
			yaw += MathF.IEEERemainder(target - yaw, MathF.Tau) * Math.Clamp(YawOverrideWeight, 0, 1);
		return yaw + Math.Clamp(MathF.IEEERemainder(CameraYaw - yaw, MathF.Tau),
			-LookPoseOpenYaw, LookPoseOpenYaw) * lookPoseAmount;
	}

	public void Dispose()
	{
		animator?.Dispose();
		body?.Dispose();
		sculptedHead?.Dispose();
	}

	void ProjectPose()
	{
		var angle = Settings.ProjectionYaw * MathF.PI / 180f;
		var cos = MathF.Cos(angle);
		var sin = MathF.Sin(angle);
		var inertiaActive = turning && !clipTurn && Settings.TurnInertia > 0;
		var upperBodyYaw = UpperBodyYaw();
		var pivot = ragdollPose[pelvis];
		var rigYaw = RigYaw();
		var turnCos = MathF.Cos(rigYaw);
		var turnSin = MathF.Sin(rigYaw);
		var turnedPivot = Turn(pivot, turnCos, turnSin);
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
			var turned = turnedPivot + Turn(offset, jointTurnCos, jointTurnSin);
			projectedPose[i] = new Vector2(-turned.Z * cos + turned.X * sin, -joint.Y) *
				Settings.PixelsPerMeter + FootSink + Offset;
			jointDepth[i] = turned.X * cos + turned.Z * sin;
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

	float BoneDepth(Bone bone) => (jointDepth[bone.From] + jointDepth[bone.To]) * 0.5f;

	public bool OverlapsCombatBounds(in RectInt worldBounds, CombatRole role = CombatRole.Vulnerable)
	{
		var bounds = (Rect)(worldBounds - owner.Position);
		return Body.Overlaps(bounds, role) ||
			bounds.Overlaps(new Circle(projectedPose[head], HeadRadius + Settings.CombatBoundsPadding), out _);
	}

	public void DrawCombatBounds(Batcher batcher)
	{
		var color = new Color(0x39e6c5);
		Body.DrawCombatBounds(batcher, color);
		batcher.CircleLine(owner.Position + projectedPose[head], HeadRadius + Settings.CombatBoundsPadding,
			1, 14, color);
	}

	public Rect GetVisualBounds()
	{
		var headRadius = HeadRadius + 1.4f;
		if (TryGetHeadSpriteFrame(out var frame, out _))
			headRadius = new Vector2(frame.Subtexture.Width, frame.Subtexture.Height).Length() *
				Settings.HeadSpriteScale * 0.5f;
		return Body.Bounds(new Rect(projectedPose[head] - Vector2.One * headRadius, Vector2.One * headRadius * 2)) +
			owner.Position;
	}

	public void Draw(Batcher batcher, Color tint, bool silhouette, DrawParts parts = DrawParts.All)
		=> Body.Draw(batcher, tint, silhouette, parts);

	public void DrawNormals(Batcher batcher, DrawParts parts = DrawParts.All) => Body.DrawNormals(batcher, parts);

	// Dents a sculpted body where an attack's world rect touches it.
	public void Dent(in RectInt attack, float strength = 1) => Body.Dent(attack, strength);

	// Dents a sculpted body at a world point: on the surface under it, or, if it misses the silhouette, the nearest
	// surface toward the torso.
	public void Dent(Vector2 worldPoint, float strength = 1) => Body.Dent(worldPoint, strength);

	public void PerturbNeuralTexture(NeuralTextureActor.PerturbContext context) => Body.PerturbNeuralTexture(context);

	void AddItemsThrough(List<RigDrawItem> items, ref int accessoryIndex, float depth)
	{
		while (Accessory != null && accessoryIndex < Accessory.DrawItemCount &&
			Accessory.GetDrawItemDepth(accessoryIndex) <= depth)
			items.Add(new(RigDrawKind.Accessory, accessoryIndex++, false));
	}

	void DrawAccessories(Batcher batcher, Color? tint)
	{
		for (var i = 0; i < (Accessory?.DrawItemCount ?? 0); i++)
			Accessory!.DrawItem(batcher, i, tint);
	}

	static bool Includes(RigDrawItem item, DrawParts parts) => parts switch
	{
		DrawParts.All => true,
		DrawParts.BehindPlayer => item.BehindPlayer,
		_ => !item.BehindPlayer,
	};

	// The scene lights that reach most strongly into `reach` pixels around a world point, strongest first.
	int GatherLights(Span<BodyLight> into, Vector2 center, float reach) => GatherLights(owner.Game, into, center, reach);

	internal static int GatherLights(Game game, Span<BodyLight> into, Vector2 center, float reach)
	{
		if (game.View is not { } view)
			return 0;
		Span<float> scores = stackalloc float[into.Length];
		var count = 0;
		var flickers = view.SceneLightFlickers;
		for (var i = 0; i < flickers.Length; i++)
		{
			var light = view.SceneLights[i];
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
			// Without the flicker, so sculpts that cache on their lights don't re-march every frame (the lighting pass
			// carries the flicker); rounded, since dividing it back out isn't exact.
			var color = light.ColorIntensity;
			into[at] = new(light.Position, light.Height, MathF.Round(light.Radius / flickers[i]),
				(new Vector3(color.X, color.Y, color.Z) * color.W / flickers[i] * 256).Round() / 256);
			count = Math.Min(count + 1, into.Length);
		}
		return count;
	}

	public void DrawEmissive(Batcher batcher)
	{
		Body.DrawEmissive(batcher);
		if (CurrentSculptedHead() is { SpecularTexture: { } headSpecular } sculpt)
			batcher.Image(headSpecular, SculptedHeadTopLeft(sculpt), Color.White);
	}

	// Whole pixels, so the marched texels land 1:1 even when the pose isn't pixel-snapped.
	Vector2 SculptedHeadTopLeft(SculptedHead sculpt) => (projectedPose[head] - new Vector2(sculpt.Size / 2)).Round();

	void DrawHead(Batcher batcher, Color tint, bool silhouette, in RigSkin skin, bool outlined = true,
		(Vector2 Center, Vector2 Radius) skinUV = default)
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
			var topLeft = SculptedHeadTopLeft(sculpt);
			if (silhouette || outlined)
				batcher.Image(sculpt.MaskTexture!, topLeft, silhouette ? tint : Multiply(Settings.OutlineColor, tint));
			if (!silhouette)
				batcher.Image(sculpted, topLeft, tint);
			return;
		}
		if (outlined)
			batcher.Circle(position, HeadRadius + 1.4f, 14, silhouette ? tint : Multiply(Settings.OutlineColor, tint));
		batcher.Circle(position, HeadRadius, 14, silhouette ? tint : Multiply(Settings.Bones.NearColor, tint));
		skin.Circle(batcher, position, HeadRadius, skinUV.Center, skinUV.Radius, Multiply(Settings.Bones.NearColor, tint));
		if (!silhouette && Settings.EyeSize > 0)
			batcher.Circle(position + lookDirection * 2.4f, VisualEyeScale * Settings.EyeSize * eyeScale, 8,
				Multiply(Settings.AccentColor, tint));
	}

	void DrawHeadNormals(Batcher batcher, Color flat, in RigSkin skin, (Vector2 Center, Vector2 Radius) skinUV = default)
	{
		if (TryGetHeadSpriteFrame(out var frame, out _))
		{
			DrawHeadSprite(batcher, frame.NormalSubtexture, Color.White);
			return;
		}

		var position = projectedPose[head];
		if (CurrentSculptedHead() is { NormalTexture: { } normals } sculpt)
		{
			batcher.Image(normals, SculptedHeadTopLeft(sculpt), Color.White);
			return;
		}
		if (Settings.Bones.NormalRoundness > 0)
			DrawSphereNormals(batcher, position, HeadRadius + 1.4f, 14,
				Settings.Bones.NormalRoundness, Settings.Bones.NormalIrregularity, 0x68bc21ebu);
		else
			batcher.Circle(position, HeadRadius + 1.4f, 14, flat);
		skin.Circle(batcher, position, HeadRadius, skinUV.Center, skinUV.Radius, Color.White);
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
		return Calc.AngleWrap(CameraYaw - UpperBodyYaw());
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

}
