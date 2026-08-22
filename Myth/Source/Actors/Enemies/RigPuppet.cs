using System.Numerics;
using Foster.Framework;

namespace Myth;

[GameActor(Sprite = "player", Tags = ActorTag.Enemy, BoundsX = -12, BoundsY = -40, BoundsW = 24, BoundsH = 40,
	LivePositionEditable = true)]
public class RigPuppet : Actor
{
	enum StepTraversal { None, Up, Down }

	readonly record struct BoneSpec(string From, string To, float Width);
	readonly record struct Bone(int From, int To, float Width);
	readonly record struct Leg(int Hip, int Knee, int Ankle, int Toe);

	struct FootIK
	{
		public float OffsetY;
	}

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
	public bool Performance;
	[I(0.1f, 3)] public float PlaybackSpeed = 1;
	[I(1, 60)] public float VisualFPS = 20;
	[I(1, 4)] public int StepUp = 2;
	[I(1, 4)] public int StepDown = 2;
	[I(12, 40)] public float PixelsPerMeter = 25;
	[I(-180, 180)] public float ProjectionYaw;
	[I(1, 20)] public int Health = 8;
	public Color FarColor = new(0x435263);
	public Color NearColor = new(0xb7cdd0);
	public Color CoreColor = new(0x708b94);
	public Color OutlineColor = new(0x171d27);

	static SampledRig? sharedRig;
	SampledRig rig = null!;
	SampledRig.Clip activeClip = null!;
	SampledRig.Clip hitClip = null!;
	SampledRig.Clip recoveryClip = null!;
	SampledRig.Clip idleClip = null!;
	Vector3[] sampledPose = [];
	Vector3[] blendFromPose = [];
	Vector3[] ragdollPose = [];
	Vector3[] ragdollPreviousPose = [];
	Vector3[] ragdollVelocity = [];
	bool[] ragdollJoints = [];
	float[] boneLengths = [];
	Vector2[] projectedPose = [];
	float[] jointDepth = [];
	Bone[] bones = [];
	int[] boneOrder = [];
	Leg leftLeg;
	Leg rightLeg;
	FootIK leftFoot;
	FootIK rightFoot;
	int head;
	int pelvis;
	float rigTime;
	float blendRemaining;
	StepTraversal stepTraversal;
	Vector2 stepStartOffset;
	Vector2 stepVisualOffset;
	float stepTime;
	float stepDuration;
	float pendingLandingSpeed;
	Signs turnTargetFacing;
	float visualYaw;
	float turnFromYaw;
	float turnToYaw;
	float turnTime;
	float turnAmount;
	bool ragdollReady;
	bool turnReady;
	bool turning;
	bool hitReacting;
	bool recovering;
	bool walking;
#if EDITOR
	internal bool AnimationPreviewEnabled;
	internal bool AnimationPreviewPaused;
	internal bool AnimationPreviewLooping = true;
	internal bool AnimationPreviewBlend = true;
	internal bool AnimationPreviewIK = true;
	internal int AnimationPreviewClipIndex;
#endif

	static float Gravity => 360f;
	static float GroundFriction => 420f;
	static float WalkSpeed => 22f;
	static float WalkAcceleration => 180f;
	static float StopDistance => 18f;
	static int GroundLookahead => 7;
	static float HitKnockbackSpeed => 64f;
	static float HitLiftSpeed => 28f;
	static float ClipBlendDuration => 0.2f;
	static float StepBaseDuration => 0.22f;
	static float StepDurationPerPixel => 0.016f;
	static float StepArcHeight => 3f;
	static float InterruptedStepCatchupSpeed => 80f;
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
		blendFromPose = new Vector3[rig.JointCount];
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
			bones[i] = new(RequiredJoint(spec.From), RequiredJoint(spec.To), spec.Width);
			ragdollJoints[bones[i].From] = true;
			ragdollJoints[bones[i].To] = true;
			boneOrder[i] = i;
		}
		head = RequiredJoint("Head");
		pelvis = RequiredJoint("pelvis");
		leftLeg = RequiredLeg("l");
		rightLeg = RequiredLeg("r");
		hitClip = rig.GetClip("Hit_Knockback");
		recoveryClip = rig.GetClip("LayToIdle");
		idleClip = rig.GetClip("Zombie_Idle_Loop");
		activeClip = Performance ? LocomotionClip() : idleClip;
		InitializeTurn();
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

	Leg RequiredLeg(string side)
		=> new(RequiredJoint($"thigh_{side}"), RequiredJoint($"calf_{side}"),
			RequiredJoint($"foot_{side}"), RequiredJoint($"ball_{side}"));

	SampledRig.Clip LocomotionClip()
		=> rig.TryGetClip(AnimationName, out var clip) ? clip : rig.GetClip("Zombie_Walk_Fwd_Loop");

	public override void Update()
	{
		EnsureRigState();
#if EDITOR
		if (AnimationPreviewEnabled)
		{
			UpdateAnimationPreview();
			return;
		}
#endif
		if (stepTraversal == StepTraversal.None)
			Velocity.Y += Gravity * Time.Delta;
		else
			Velocity = Vector2.Zero;
		base.Update();
		var grounded = Grounded();
		if (grounded)
		{
			if (Velocity.Y > 0)
				Velocity.Y = 0;
		}

		walking = !Performance && stepTraversal != StepTraversal.None;
		if (!Performance && stepTraversal == StepTraversal.None && Game.GetFirst<Player>() is { } player &&
			MathF.Abs(player.Position.X - Position.X) > 1)
		{
			var desiredFacing = player.Position.X < Position.X ? Signs.Negative : Signs.Positive;
			if (!turning && desiredFacing != Facing && grounded && !hitReacting && !recovering)
				BeginTurn(desiredFacing);
			if (!turning && desiredFacing == Facing && grounded && !hitReacting && !recovering &&
				MathF.Abs(player.Position.X - Position.X) > StopDistance)
			{
				walking = HasGroundAhead();
				if (!walking)
					TryStartStepDown((int)Facing);
			}
		}
		if (stepTraversal == StepTraversal.None && grounded)
			Velocity.X = Calc.Approach(Velocity.X, walking ? (int)Facing * WalkSpeed : 0,
				(walking ? WalkAcceleration : GroundFriction) * Time.Delta);
		UpdateTurn(grounded);
		UpdateStepTraversal();

		rigTime += Time.Delta * PlaybackSpeed;
		if (hitReacting && grounded && rigTime >= activeClip.Duration)
		{
			hitReacting = false;
			recovering = true;
			StartClip(recoveryClip);
		}
		else if (recovering && rigTime >= activeClip.Duration)
		{
			recovering = false;
			StartClip(Performance ? LocomotionClip() : idleClip);
		}
		else if (!hitReacting && !recovering &&
			(Performance || walking ? LocomotionClip() : idleClip) is { } desiredClip &&
			desiredClip != activeClip)
			StartClip(desiredClip);

		UpdatePose();
	}

#if EDITOR
	internal IReadOnlyList<SampledRig.Clip> AnimationClips => rig.Clips;
	internal SampledRig.Clip AnimationPreviewClip => rig.Clips[AnimationPreviewClipIndex];
	internal float AnimationPreviewTime
	{
		get => rigTime;
		set
		{
			rigTime = Math.Clamp(value, 0, AnimationPreviewClip.Duration);
			blendRemaining = 0;
		}
	}

	internal void BeginAnimationPreview()
	{
		if (AnimationPreviewEnabled)
			return;

		AnimationPreviewEnabled = true;
		AnimationPreviewClipIndex = 0;
		for (var i = 0; i < rig.Clips.Count; i++)
			if (rig.Clips[i] == activeClip)
			{
				AnimationPreviewClipIndex = i;
				break;
			}
		hitReacting = false;
		recovering = false;
		stepTraversal = StepTraversal.None;
		stepVisualOffset = Vector2.Zero;
		Velocity = Vector2.Zero;
		turning = false;
		InitializeTurn();
		leftFoot = default;
		rightFoot = default;
	}

	internal void EndAnimationPreview()
	{
		if (!AnimationPreviewEnabled)
			return;

		AnimationPreviewEnabled = false;
		AnimationPreviewPaused = false;
		StartClip(idleClip);
	}

	internal void PreviewAnimation(int index)
	{
		AnimationPreviewClipIndex = Math.Clamp(index, 0, rig.Clips.Count - 1);
		var clip = AnimationPreviewClip;
		if (clip == activeClip)
		{
			rigTime = 0;
			blendRemaining = 0;
		}
		else
			StartClip(clip, AnimationPreviewBlend);
		leftFoot = default;
		rightFoot = default;
	}

	internal void SetAnimationPreviewIK(bool enabled)
	{
		AnimationPreviewIK = enabled;
		if (!enabled)
		{
			leftFoot = default;
			rightFoot = default;
		}
	}

	void UpdateAnimationPreview()
	{
		Velocity.X = 0;
		Velocity.Y += Gravity * Time.Delta;
		base.Update();
		if (Grounded() && Velocity.Y > 0)
			Velocity.Y = 0;

		if (!AnimationPreviewPaused)
			rigTime += Time.Delta * PlaybackSpeed;
		var duration = AnimationPreviewClip.Duration;
		if (AnimationPreviewLooping)
			rigTime = ((rigTime % duration) + duration) % duration;
		else
			rigTime = Math.Clamp(rigTime, 0, duration);
		UpdatePose();
	}
#endif

	void EnsureRigState()
	{
		if (leftLeg.Hip == leftLeg.Toe || rightLeg.Hip == rightLeg.Toe)
		{
			leftLeg = RequiredLeg("l");
			rightLeg = RequiredLeg("r");
			leftFoot = default;
			rightFoot = default;
		}
		if (idleClip == null)
			idleClip = rig.GetClip("Zombie_Idle_Loop");
		if (recoveryClip == null)
			recoveryClip = rig.GetClip("LayToIdle");
		if (blendFromPose.Length != rig.JointCount)
			blendFromPose = new Vector3[rig.JointCount];
		if (ragdollPose == null || ragdollPreviousPose == null || ragdollVelocity == null ||
			ragdollJoints == null || boneLengths == null || ragdollPose.Length != rig.JointCount ||
			ragdollPreviousPose.Length != rig.JointCount || ragdollVelocity.Length != rig.JointCount ||
			ragdollJoints.Length != rig.JointCount || boneLengths.Length != bones.Length)
		{
			ragdollPose = new Vector3[rig.JointCount];
			ragdollPreviousPose = new Vector3[rig.JointCount];
			ragdollVelocity = new Vector3[rig.JointCount];
			ragdollJoints = new bool[rig.JointCount];
			boneLengths = new float[bones.Length];
			for (var i = 0; i < bones.Length; i++)
			{
				ragdollJoints[bones[i].From] = true;
				ragdollJoints[bones[i].To] = true;
			}
			ragdollReady = false;
		}
		if (VisualFPS <= 0)
			VisualFPS = 20;
		if (StepUp <= 0)
			StepUp = 2;
		if (StepDown <= 0)
			StepDown = 2;
		if (!turnReady)
			InitializeTurn();
		else if (!turning)
			visualYaw = FacingYaw(Facing);
	}

	float FacingYaw(Signs facing)
		=> facing == Signs.Negative ? 0 : ProjectionYaw >= 0 ? -MathF.PI : MathF.PI;

	void InitializeTurn()
	{
		turnTargetFacing = Facing;
		visualYaw = FacingYaw(Facing);
		turnFromYaw = turnToYaw = visualYaw;
		turnTime = 0;
		turnAmount = 0;
		turnReady = true;
	}

	void BeginTurn(Signs target)
	{
		turnTargetFacing = target;
		turnFromYaw = visualYaw;
		turnToYaw = FacingYaw(target);
		turnTime = 0;
		turnAmount = 0;
		turning = true;
		leftFoot = default;
		rightFoot = default;
	}

	void UpdateTurn(bool grounded)
	{
		if (!turning || !grounded || hitReacting || recovering || MathF.Abs(Velocity.X) > TurnStartSpeed)
			return;

		turnTime = MathF.Min(turnTime + Time.Delta, TurnDuration);
		var progress = turnTime / TurnDuration;
		var eased = progress * progress * (3 - 2 * progress);
		turnAmount = eased;
		visualYaw = float.Lerp(turnFromYaw, turnToYaw, eased);
		if (turnTime < TurnDuration)
			return;

		Facing = turnTargetFacing;
		visualYaw = FacingYaw(Facing);
		turnAmount = 0;
		turning = false;
	}

	void StartClip(SampledRig.Clip clip, bool blend = true)
	{
		if (clip == activeClip)
			return;

		if (blend && sampledPose.Length == blendFromPose.Length)
		{
			sampledPose.CopyTo(blendFromPose, 0);
			blendRemaining = ClipBlendDuration;
		}
		else
			blendRemaining = 0;
		activeClip = clip;
		rigTime = 0;
	}

	bool HasGroundAhead()
	{
		var bounds = Hitbox.Rect + Position + new Point2((int)Facing * GroundLookahead, 0);
		return Game.TryFindSurface(this, bounds, Vector2.UnitY, 2, out _);
	}

	bool TryStartStepDown(int direction)
	{
		var forward = new Point2(direction * GroundLookahead, 0);
		var bounds = Hitbox.Rect + Position + forward;
		if (!Game.TryFindSurface(this, bounds, Vector2.UnitY, StepDown * Game.TileSize + 1, out var hit))
			return false;

		var drop = (int)hit.Distance - 1;
		var step = forward + Point2.Down * drop;
		if (drop <= 1 || drop > StepDown * Game.TileSize || OverlapsAny(step, Masks.Solid))
			return false;

		BeginStep(step, StepTraversal.Down);
		return true;
	}

	public override void OnCollideX()
	{
		var direction = Math.Sign(Velocity.X);
		if (direction == 0 || !Grounded())
		{
			base.OnCollideX();
			return;
		}

		for (var rise = 1; rise <= StepUp * Game.TileSize; rise++)
		{
			var up = Point2.Up * rise;
			if (OverlapsAny(up, Masks.Solid))
				break;

			var step = up + Point2.UnitX * direction;
			if (!OverlapsAny(step, Masks.Solid) && OverlapsAny(step + Point2.Down, Masks.Solid))
			{
				for (var forward = 2; forward <= GroundLookahead; forward++)
				{
					var candidate = up + Point2.UnitX * direction * forward;
					if (OverlapsAny(candidate, Masks.Solid) || !OverlapsAny(candidate + Point2.Down, Masks.Solid))
						break;
					step = candidate;
				}
				BeginStep(step, StepTraversal.Up);
				return;
			}
		}

		base.OnCollideX();
	}

	void BeginStep(Point2 step, StepTraversal traversal)
	{
		Position += step;
		stepTraversal = traversal;
		stepStartOffset = -(Vector2)step;
		stepVisualOffset = stepStartOffset;
		stepTime = 0;
		stepDuration = StepBaseDuration + Math.Abs(step.Y) * StepDurationPerPixel;
		Velocity = Vector2.Zero;
		walking = true;
		leftFoot = default;
		rightFoot = default;
		StartClip(LocomotionClip());
	}

	void UpdateStepTraversal()
	{
		if (stepTraversal == StepTraversal.None)
		{
			stepVisualOffset = Calc.Approach(stepVisualOffset, Vector2.Zero, InterruptedStepCatchupSpeed * Time.Delta);
			return;
		}

		stepTime = MathF.Min(stepTime + Time.Delta, stepDuration);
		var progress = stepTime / stepDuration;
		var eased = progress * progress * (3 - 2 * progress);
		stepVisualOffset = stepStartOffset * (1 - eased) - Vector2.UnitY * MathF.Sin(progress * MathF.PI) * StepArcHeight;
		if (stepTime < stepDuration)
			return;

		stepTraversal = StepTraversal.None;
		stepVisualOffset = Vector2.Zero;
	}

	public override void OnCollideY()
	{
		if (Velocity.Y >= LandingMinSpeed)
			pendingLandingSpeed = MathF.Max(pendingLandingSpeed, Velocity.Y);
		base.OnCollideY();
	}

	void UpdatePose()
	{
		var visualTime = MathF.Floor(rigTime * VisualFPS) / VisualFPS;
		var loop = !hitReacting && !recovering;
#if EDITOR
		if (AnimationPreviewEnabled)
			loop = AnimationPreviewLooping;
#endif
		rig.Sample(activeClip, visualTime, loop, sampledPose);
		if (blendRemaining > 0)
		{
			blendRemaining = MathF.Max(0, blendRemaining - Time.Delta);
			var blend = 1 - blendRemaining / ClipBlendDuration;
			blend = blend * blend * (3 - 2 * blend);
			for (var i = 0; i < sampledPose.Length; i++)
				sampledPose[i] = Vector3.Lerp(blendFromPose[i], sampledPose[i], blend);
		}
		UpdateRagdoll();
		var angle = ProjectionYaw * MathF.PI / 180f;
		var cos = MathF.Cos(angle);
		var sin = MathF.Sin(angle);
		var turnCos = MathF.Cos(visualYaw);
		var turnSin = MathF.Sin(visualYaw);
		var pivot = ragdollPose[pelvis];
		var pivotX = -pivot.Z * cos + pivot.X * sin;
		var mirror = turning
			? float.Lerp(-(int)Facing, -(int)turnTargetFacing, turnAmount)
			: -(int)Facing;
		for (var i = 0; i < sampledPose.Length; i++)
		{
			var joint = ragdollJoints[i] ? ragdollPose[i] : sampledPose[i];
			var offset = joint - pivot;
			var turned = new Vector3(offset.X * turnCos + offset.Z * turnSin, offset.Y,
				-offset.X * turnSin + offset.Z * turnCos);
			projectedPose[i] = new Vector2(pivotX * mirror - turned.Z * cos + turned.X * sin, -joint.Y) * PixelsPerMeter +
				new Vector2(0, 2.5f);
			jointDepth[i] = joint.X * cos + joint.Z * sin;
			projectedPose[i] += stepVisualOffset;
		}

		if (!Performance)
		{
			var updateFeet = true;
#if EDITOR
			updateFeet = !AnimationPreviewEnabled || AnimationPreviewIK;
#endif
			if (updateFeet)
			{
				var grounded = Grounded();
				UpdateFoot(ref leftFoot, leftLeg, grounded);
				UpdateFoot(ref rightFoot, rightLeg, grounded);
			}
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

		var dt = MathF.Min(Time.Delta, 1 / 30f);
		if (dt <= 0)
			return;
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

	Vector3 RigVelocity(Vector2 screenVelocity)
	{
		var angle = ProjectionYaw * MathF.PI / 180f + visualYaw;
		var alongProjection = screenVelocity.X / PixelsPerMeter;
		return new(MathF.Sin(angle) * alongProjection, -screenVelocity.Y / PixelsPerMeter,
			-MathF.Cos(angle) * alongProjection);
	}

	void UpdateFoot(ref FootIK plant, Leg leg, bool grounded)
	{
		var animatedToe = Position + projectedPose[leg.Toe];
		var targetOffset = 0f;
		if (grounded && !hitReacting && !recovering && stepTraversal == StepTraversal.None)
		{
			var probeCenter = (animatedToe - Vector2.UnitY * FootProbeAbove).RoundToPoint2();
			var probe = new RectInt(probeCenter.X - 1, probeCenter.Y - 1, 2, 2);
			if (Game.TryFindSurface(this, probe, Vector2.UnitY, FootProbeDistance, out var hit) &&
				hit.Normal.Y < -0.5f && MathF.Abs(animatedToe.Y - hit.Point.Y) <= FootPlantDistance)
				targetOffset = hit.Point.Y - animatedToe.Y;
		}

		plant.OffsetY = Calc.Approach(plant.OffsetY, targetOffset, FootIKSpeed * Time.Delta);
		if (MathF.Abs(plant.OffsetY) <= 0.001f)
			return;

		var animatedFoot = projectedPose[leg.Ankle];
		var footOffset = projectedPose[leg.Toe] - animatedFoot;
		SolveLeg(leg, projectedPose[leg.Toe] + Vector2.UnitY * plant.OffsetY - footOffset);
		projectedPose[leg.Toe] = projectedPose[leg.Ankle] + footOffset;
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
		distance = Math.Clamp(distance, MathF.Abs(upperLength - lowerLength) + 0.001f, upperLength + lowerLength - 0.001f);
		var along = (upperLength * upperLength - lowerLength * lowerLength + distance * distance) / (2 * distance);
		var height = MathF.Sqrt(MathF.Max(0, upperLength * upperLength - along * along));
		var perpendicular = new Vector2(-direction.Y, direction.X);
		var bend = Calc.Cross(direction, animatedKnee - hip) < 0 ? -1 : 1;
		projectedPose[leg.Knee] = hip + direction * along + perpendicular * height * bend;
		projectedPose[leg.Ankle] = hip + direction * distance;
	}

	float BoneDepth(Bone bone) => (jointDepth[bone.From] + jointDepth[bone.To]) * 0.5f;

	public override void OnWasHit(Actor by)
	{
#if EDITOR
		if (AnimationPreviewEnabled)
			return;
#endif
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
		for (var i = 0; i < ragdollVelocity.Length; i++)
		{
			if (!ragdollJoints[i])
				continue;
			var height = Math.Clamp(sampledPose[i].Y / 1.55f, 0, 1);
			var depthKick = MathF.Sign(jointDepth[i]) * 7 * height;
			ragdollVelocity[i] += RigVelocity(new(away * (18 + 52 * height) + depthKick, -15 * height));
		}
		Velocity = new Vector2(away * HitKnockbackSpeed, -HitLiftSpeed);
		stepTraversal = StepTraversal.None;
		leftFoot = default;
		rightFoot = default;
		StartClip(hitClip, blend: false);
		hitReacting = true;
		recovering = false;
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
			var look = new Vector2(-MathF.Cos(visualYaw) * 2.4f, 0.2f);
			batcher.Circle(headPosition + look, 1.1f, 8, new Color(0xd92240));
		}
	}

	public override Rect GetVisualBounds()
		=> new(Position.X - 42, Position.Y - 48, 84, 58);
}
