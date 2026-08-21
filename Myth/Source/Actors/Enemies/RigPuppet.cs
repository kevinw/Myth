using System.Numerics;
using Foster.Framework;

namespace Myth;

[GameActor(Sprite = "player", Tags = ActorTag.Enemy, BoundsX = -12, BoundsY = -40, BoundsW = 24, BoundsH = 40)]
public class RigPuppet : Actor
{
	enum StepTraversal { None, Up, Down }

	readonly record struct BoneSpec(string From, string To, float Width);
	readonly record struct Bone(int From, int To, float Width);
	readonly record struct Leg(int Hip, int Knee, int Ankle, int Toe);

	struct FootPlant
	{
		public Vector2 Position;
		public float Strength;
		public float Cooldown;
		public bool Planted;
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
	[I(0.1f, 3)] public float PlaybackSpeed = 1;
	[I(1, 60)] public float VisualFPS = 20;
	[I(1, 4)] public int StepUp = 2;
	[I(1, 4)] public int StepDown = 2;
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
	SampledRig.Clip recoveryClip = null!;
	SampledRig.Clip idleClip = null!;
	Vector3[] sampledPose = [];
	Vector3[] blendFromPose = [];
	Vector2[] projectedPose = [];
	float[] jointDepth = [];
	Vector2[] recoil = [];
	Vector2[] recoilVelocity = [];
	Vector2[] inheritedRecoil = [];
	Bone[] bones = [];
	int[] boneOrder = [];
	Leg leftLeg;
	Leg rightLeg;
	FootPlant leftFoot;
	FootPlant rightFoot;
	int head;
	int pelvis;
	float rigTime;
	float blendRemaining;
	StepTraversal stepTraversal;
	Vector2 stepStartOffset;
	Vector2 stepVisualOffset;
	float stepTime;
	float stepDuration;
	bool hitReacting;
	bool recovering;
	bool walking;

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
	static float RecoilSpring => 74f;
	static float RecoilDamping => 10f;
	static float RecoilInheritance => 0.58f;
	static float FootProbeAbove => 4f;
	static float FootProbeDistance => 12f;
	static float FootPlantDistance => 5f;
	static float FootReleaseDistance => 8f;
	static float FootLiftDistance => 4f;
	static float FootBlendSpeed => 12f;
	static float FootReplantDelay => 0.08f;

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
		leftLeg = RequiredLeg("l");
		rightLeg = RequiredLeg("r");
		hitClip = rig.GetClip("Hit_Knockback");
		recoveryClip = rig.GetClip("LayToIdle");
		idleClip = rig.GetClip("Zombie_Idle_Loop");
		activeClip = idleClip;
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

		walking = stepTraversal != StepTraversal.None;
		if (stepTraversal == StepTraversal.None && Game.GetFirst<Player>() is { } player &&
			MathF.Abs(player.Position.X - Position.X) > 1)
		{
			Facing = player.Position.X < Position.X ? Signs.Negative : Signs.Positive;
			if (grounded && !hitReacting && !recovering && MathF.Abs(player.Position.X - Position.X) > StopDistance)
			{
				walking = HasGroundAhead();
				if (!walking)
					TryStartStepDown((int)Facing);
			}
		}
		if (stepTraversal == StepTraversal.None && grounded)
			Velocity.X = Calc.Approach(Velocity.X, walking ? (int)Facing * WalkSpeed : 0,
				(walking ? WalkAcceleration : GroundFriction) * Time.Delta);
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
			StartClip(idleClip);
		}
		else if (!hitReacting && !recovering &&
			(walking ? LocomotionClip() : idleClip) is { } desiredClip && desiredClip != activeClip)
			StartClip(desiredClip);

		UpdateRecoil();
		UpdatePose();
	}

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
		if (VisualFPS <= 0)
			VisualFPS = 20;
		if (StepUp <= 0)
			StepUp = 2;
		if (StepDown <= 0)
			StepDown = 2;
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
		var visualTime = MathF.Floor(rigTime * VisualFPS) / VisualFPS;
		rig.Sample(activeClip, visualTime, loop: !hitReacting && !recovering, sampledPose);
		if (blendRemaining > 0)
		{
			blendRemaining = MathF.Max(0, blendRemaining - Time.Delta);
			var blend = 1 - blendRemaining / ClipBlendDuration;
			blend = blend * blend * (3 - 2 * blend);
			for (var i = 0; i < sampledPose.Length; i++)
				sampledPose[i] = Vector3.Lerp(blendFromPose[i], sampledPose[i], blend);
		}
		var angle = ProjectionYaw * MathF.PI / 180f;
		var cos = MathF.Cos(angle);
		var sin = MathF.Sin(angle);
		for (var i = 0; i < sampledPose.Length; i++)
		{
			var joint = sampledPose[i];
			projectedPose[i] = new Vector2((-joint.Z * cos + joint.X * sin) * -(int)Facing,
				-joint.Y) * PixelsPerMeter + new Vector2(0, 2.5f);
			jointDepth[i] = joint.X * cos + joint.Z * sin;
			var parent = rig.JointParents[i];
			inheritedRecoil[i] = recoil[i] + (parent >= 0 ? inheritedRecoil[parent] * RecoilInheritance : Vector2.Zero);
			projectedPose[i] += inheritedRecoil[i] + stepVisualOffset;
		}

		var grounded = Grounded();
		UpdateFoot(ref leftFoot, leftLeg, grounded);
		UpdateFoot(ref rightFoot, rightLeg, grounded);

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

	void UpdateFoot(ref FootPlant plant, Leg leg, bool grounded)
	{
		var animatedToe = Position + projectedPose[leg.Toe];
		plant.Cooldown = MathF.Max(0, plant.Cooldown - Time.Delta);

		if (!grounded || hitReacting || recovering || stepTraversal != StepTraversal.None || plant.Planted &&
			(MathF.Abs(animatedToe.X - plant.Position.X) > FootReleaseDistance ||
			animatedToe.Y < plant.Position.Y - FootLiftDistance))
		{
			if (plant.Planted)
				plant.Cooldown = FootReplantDelay;
			plant.Planted = false;
		}

		if (!plant.Planted && plant.Cooldown <= 0 && grounded)
		{
			var probeCenter = (animatedToe - Vector2.UnitY * FootProbeAbove).RoundToPoint2();
			var probe = new RectInt(probeCenter.X - 1, probeCenter.Y - 1, 2, 2);
			if (Game.TryFindSurface(this, probe, Vector2.UnitY, FootProbeDistance, out var hit) &&
				hit.Normal.Y < -0.5f && Vector2.DistanceSquared(animatedToe, hit.Point) <= FootPlantDistance * FootPlantDistance)
			{
				plant.Position = hit.Point;
				plant.Planted = true;
			}
		}

		plant.Strength = Calc.Approach(plant.Strength, plant.Planted ? 1 : 0, FootBlendSpeed * Time.Delta);
		if (plant.Strength <= 0)
			return;

		var animatedFoot = projectedPose[leg.Ankle];
		var footOffset = projectedPose[leg.Toe] - animatedFoot;
		var plantedToe = Vector2.Lerp(animatedToe, plant.Position, plant.Strength) - Position;
		SolveLeg(leg, plantedToe - footOffset);
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
			var look = new Vector2((int)Facing * 2.4f, 0.2f);
			batcher.Circle(headPosition + look, 1.1f, 8, new Color(0xd92240));
		}
	}

	public override Rect GetVisualBounds()
		=> new(Position.X - 42, Position.Y - 48, 84, 58);
}
