using System.Numerics;
using Foster.Framework;

namespace Myth;

public enum RigSkinMode { Solid, Surface, NCA }

sealed class HumanoidRigVisual(Actor owner) : IDisposable
{
	enum Part { Torso, Bicep, Forearm, Thigh, Other }

	readonly record struct BoneSpec(string From, string To, float Width, Part Part);
	readonly record struct Bone(int From, int To, float Width, Part Part);
	readonly record struct BoneUV(Vector2 A, Vector2 B, Vector2 C, Vector2 D);
	readonly record struct Leg(int Hip, int Knee, int Ankle, int Toe, int Tip);

	struct FootIK
	{
		public float OffsetY;
		public bool Planted;
	}

	static readonly BoneSpec[] BoneSpecs =
	[
		new("pelvis", "spine_01", 5, Part.Torso),
		new("spine_01", "spine_02", 5, Part.Torso),
		new("spine_02", "spine_03", 5, Part.Torso),
		new("spine_03", "neck_01", 4, Part.Other),
		new("neck_01", "Head", 3, Part.Other),
		new("spine_03", "clavicle_l", 3, Part.Bicep),
		new("clavicle_l", "upperarm_l", 3, Part.Bicep),
		new("upperarm_l", "lowerarm_l", 3, Part.Bicep),
		new("lowerarm_l", "hand_l", 2.5f, Part.Forearm),
		new("hand_l", "middle_01_l", 2.5f, Part.Forearm),
		new("spine_03", "clavicle_r", 3, Part.Bicep),
		new("clavicle_r", "upperarm_r", 3, Part.Bicep),
		new("upperarm_r", "lowerarm_r", 3, Part.Bicep),
		new("lowerarm_r", "hand_r", 2.5f, Part.Forearm),
		new("hand_r", "middle_01_r", 2.5f, Part.Forearm),
		new("pelvis", "thigh_l", 4, Part.Thigh),
		new("thigh_l", "calf_l", 4, Part.Thigh),
		new("calf_l", "foot_l", 3, Part.Other),
		new("foot_l", "ball_l", 2, Part.Other),
		new("ball_l", "ball_leaf_l", 2, Part.Other),
		new("pelvis", "thigh_r", 4, Part.Thigh),
		new("thigh_r", "calf_r", 4, Part.Thigh),
		new("calf_r", "foot_r", 3, Part.Other),
		new("foot_r", "ball_r", 2, Part.Other),
		new("ball_r", "ball_leaf_r", 2, Part.Other),
	];

	public float PlaybackSpeed = 1;
	public float VisualFPS = 20;
	public float PixelsPerMeter = 25;
	public float ProjectionYaw;
	public float HeadSize = 3.9f;
	public float TorsoSize = 5;
	public float BicepSize = 3;
	public float ForearmSize = 2.5f;
	public float ThighSize = 4;
	public RigSkinMode SkinMode = RigSkinMode.NCA;
	public string SurfaceId = "";
	public float TrackingLean;
	public float UpperBodyHitStrength = 0.8f;
	public string NeuralWeightsFile = "rock035_pair_circular_screen.bin";
	public float NeuralColorAmount = 0.25f;
	public float NeuralNormalAmount = 0.55f;
	public float NeuralFPS = 20;
	public float NeuralFireRate = 0.5f;
	public float NeuralContrast = 1f;
	public float NeuralSaturation = 1.1f;
	public float NeuralBrightness = 0.3f;
	public float NeuralHitStrength = 0.55f;
	public float NeuralJostleInterval = 0.45f;
	public float NeuralJostleStrength = 0.22f;
	public float NeuralJostleRadius = 5;
	public Vector2 Offset;
	public Vector2? LookTarget;
	public bool TurnWhileMoving;
	public Color FarColor = new(0x435263);
	public Color NearColor = new(0xb7cdd0);
	public Color CoreColor = new(0x708b94);
	public Color OutlineColor = new(0x171d27);
	public Color AccentColor = new(0xd92240);

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
	OzzRig.Clip upperBodyHitClip = null!;
	NeuralTextureActor? neuralSurface;
	string activeNeuralWeightsFile = "";
	int[] boneOrder = [];
	Leg leftLeg;
	Leg rightLeg;
	FootIK leftFoot;
	FootIK rightFoot;
	int head;
	int pelvis;
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
	static Point2 NeuralSize => new(64, 80);
	static Point2 NeuralOrigin => new(-32, -72);

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
		rig = RigLibrary.Get("ual2");
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
		neuralBoneUVs = new BoneUV[BoneSpecs.Length];
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
		CaptureNeuralUVs();
		surfaceMaterialOrigin = owner.Position;
		neuralJostlePhase = (owner.ActorId?.GetHashCode() ?? owner.Position.GetHashCode()) * 0.0001f;
		neuralJostleAt = owner.Time.SecondsF + NeuralJostleInterval * 0.5f;
		CreateNeuralSurface();
	}

	void CreateNeuralSurface()
	{
		activeNeuralWeightsFile = NeuralWeightsFile;
		neuralSurface = owner.Game.Create<NeuralTextureActor>(owner.Position + NeuralOrigin, it =>
		{
			it.Visible = false;
			it.Size = NeuralSize;
			it.SimulationSize = NeuralSize;
			it.WeightsFile = activeNeuralWeightsFile;
			it.NearestStateSampling = true;
			it.EdgeTrim = 1;
		});
		SyncNeuralSurface();
		neuralSurface.SimulateFromSeed(65);
	}

	void SyncNeuralSurface()
	{
		if (neuralSurface == null)
			return;
		neuralSurface.Position = owner.Position + NeuralOrigin;
		neuralSurface.KeepOnRoomTransition = owner.KeepOnRoomTransition;
		neuralSurface.StepsPerSecond = SkinMode == RigSkinMode.NCA ? NeuralFPS : 0;
		neuralSurface.FireRate = NeuralFireRate;
		neuralSurface.Contrast = NeuralContrast;
		neuralSurface.Saturation = NeuralSaturation;
		neuralSurface.Brightness = NeuralBrightness;
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
		if (neuralSurface != null && activeNeuralWeightsFile != NeuralWeightsFile)
		{
			owner.Game.Destroy(neuralSurface);
			neuralSurface = null;
			CreateNeuralSurface();
		}
		SyncNeuralSurface();
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
		neuralHitRemaining = 0.18f;
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
		lookDirection = LookTarget is { } target
			? (target - owner.Position - projectedPose[head]).Normalized()
			: new Vector2(-MathF.Cos(visualYaw), 0);
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
			MathF.Sin(progress * MathF.PI) * UpperBodyHitStrength, upperBodyMask);
	}

	public void PerturbNeuralTexture(NeuralTextureActor.PerturbContext context)
	{
		if (SkinMode != RigSkinMode.NCA || context.Texture != neuralSurface)
			return;
		var center = (Vector2)owner.Position + (projectedPose[pelvis] + projectedPose[head]) * 0.5f;
		if (neuralHitRemaining > 0)
			context.AddStamp(center, center, 7, NeuralHitStrength);

		var now = owner.Time.SecondsF;
		if (NeuralJostleInterval <= 0 || NeuralJostleStrength <= 0 || NeuralJostleRadius <= 0 || now < neuralJostleAt)
			return;
		neuralJostleAt = now + NeuralJostleInterval;
		var phase = now * 2.3f + neuralJostlePhase;
		var offset = new Vector2(MathF.Sin(phase) * 8, MathF.Cos(phase * 0.73f) * 14);
		context.AddStamp(center + offset, center + offset, NeuralJostleRadius, NeuralJostleStrength);
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

	public void Dispose()
	{
		animator?.Dispose();
		if (neuralSurface != null && !owner.Game.destroying.Contains(neuralSurface))
			owner.Game.Destroy(neuralSurface);
		neuralSurface = null;
	}

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

	float BoneDepth(Bone bone) => (jointDepth[bone.From] + jointDepth[bone.To]) * 0.5f;

	Color BoneColor(Bone bone, float depth)
		=> bone.From == pelvis || bone.To == pelvis ? CoreColor : Color.Lerp(FarColor, NearColor, depth);

	float BoneWidth(Bone bone) => bone.Part switch
	{
		Part.Torso => TorsoSize,
		Part.Bicep => BicepSize,
		Part.Forearm => ForearmSize,
		Part.Thigh => ThighSize,
		_ => bone.Width,
	};

	void CaptureNeuralUVs()
	{
		for (var i = 0; i < bones.Length; i++)
		{
			var bone = bones[i];
			BoneQuad(projectedPose[bone.From], projectedPose[bone.To], BoneWidth(bone),
				out var a, out var b, out var c, out var d);
			neuralBoneUVs[i] = new(NeuralUV(a), NeuralUV(b), NeuralUV(c), NeuralUV(d));
		}
		neuralHeadUV = NeuralUV(projectedPose[head]);
		neuralHeadUVRadius = new Vector2(HeadSize) / (Vector2)NeuralSize;
	}

	public void Draw(Batcher batcher, Color tint, bool silhouette)
	{
		var surface = !silhouette && SkinMode == RigSkinMode.Surface ? Assets.GetSurface(SurfaceId) : null;
		var neuralTexture = !silhouette && SkinMode == RigSkinMode.NCA && NeuralColorAmount > 0
			? neuralSurface?.OutputTexture : null;
		if (neuralTexture != null)
			batcher.PushSampler(new(TextureFilter.Nearest, TextureWrap.Clamp));
		for (var sortedIndex = 0; sortedIndex < boneOrder.Length; sortedIndex++)
		{
			var bone = bones[boneOrder[sortedIndex]];
			var from = projectedPose[bone.From];
			var to = projectedPose[bone.To];
			var width = BoneWidth(bone);
			if (silhouette)
			{
				batcher.Line(from, to, width + 2, tint);
				continue;
			}

			var depth = Math.Clamp(BoneDepth(bone) + 0.5f, 0, 1);
			var color = BoneColor(bone, depth);
			batcher.Line(from, to, width + 2, OutlineColor);
			batcher.Line(from, to, width, color);
			if (surface != null)
				DrawSurfaceBone(batcher, surface, from, to, width, neuralBoneUVs[boneOrder[sortedIndex]], normals: false);
			else if (neuralTexture != null)
				DrawNeuralBone(batcher, neuralTexture, from, to, width, neuralBoneUVs[boneOrder[sortedIndex]],
					color * NeuralColorAmount);
			batcher.Circle(to, MathF.Max(1.2f, width * 0.42f), 8, color);
		}

		var headPosition = projectedPose[head];
		batcher.Circle(headPosition, HeadSize + 1.4f, 14, silhouette ? tint : OutlineColor);
		batcher.Circle(headPosition, HeadSize, 14, silhouette ? tint : NearColor);
		if (surface != null)
			DrawSurfaceCircle(batcher, surface, headPosition, HeadSize, neuralHeadUV, neuralHeadUVRadius, normals: false);
		else if (neuralTexture != null)
		{
			DrawNeuralCircle(batcher, neuralTexture, headPosition, HeadSize, neuralHeadUV, neuralHeadUVRadius,
				NearColor * NeuralColorAmount);
			batcher.PopSampler();
		}
		if (!silhouette)
			batcher.Circle(headPosition + lookDirection * 2.4f, 1.1f, 8, AccentColor);
	}

	public void DrawNormals(Batcher batcher)
	{
		var flat = new Color(0x8080ff);
		var surface = SkinMode == RigSkinMode.Surface && Assets.GetSurface(SurfaceId) is { HasNormal: true } foundSurface
			? foundSurface : null;
		var neuralNormals = SkinMode == RigSkinMode.NCA && NeuralNormalAmount > 0
			? neuralSurface?.OutputNormalTexture : null;
		if (neuralNormals != null)
			batcher.PushSampler(new(TextureFilter.Nearest, TextureWrap.Clamp));
		for (var sortedIndex = 0; sortedIndex < boneOrder.Length; sortedIndex++)
		{
			var bone = bones[boneOrder[sortedIndex]];
			var from = projectedPose[bone.From];
			var to = projectedPose[bone.To];
			var width = BoneWidth(bone);
			batcher.Line(from, to, width + 2, flat);
			if (surface != null)
				DrawSurfaceBone(batcher, surface, from, to, width, neuralBoneUVs[boneOrder[sortedIndex]], normals: true);
			else if (neuralNormals != null)
				DrawNeuralBone(batcher, neuralNormals, from, to, width, neuralBoneUVs[boneOrder[sortedIndex]],
					Color.White * NeuralNormalAmount);
		}
		var headPosition = projectedPose[head];
		batcher.Circle(headPosition, HeadSize + 1.4f, 14, flat);
		if (surface != null)
			DrawSurfaceCircle(batcher, surface, headPosition, HeadSize, neuralHeadUV, neuralHeadUVRadius, normals: true);
		else if (neuralNormals != null)
		{
			DrawNeuralCircle(batcher, neuralNormals, headPosition, HeadSize, neuralHeadUV, neuralHeadUVRadius,
				Color.White * NeuralNormalAmount);
			batcher.PopSampler();
		}
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
