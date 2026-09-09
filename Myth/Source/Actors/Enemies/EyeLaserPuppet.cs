using System.Diagnostics;
using System.Numerics;
using Foster.Framework;
#if EDITOR
using ImGuiNET;
using LinkDotNet.StringBuilder;
#endif

namespace Myth;

[GameActor(Sprite = "player", Tags = ActorTag.Enemy, BoundsX = -12, BoundsY = -40, BoundsW = 24, BoundsH = 40,
	LivePositionEditable = true)]
public sealed class EyeLaserPuppet : RigPuppet, ILiveStatus
{
	public override RumbleEffect? AttackHitRumble => new(0.70f, 0.85f, 0.14f);
	public override float HitKnockback => 100f;
	static readonly HumanoidRigWireSpec[] WireLayout =
	[
		new("Head", "spine_03", -8, -0.06f, 1.05f),
		new("Head", "spine_03", 11, 0.05f),
		new("neck_01", "clavicle_l", -8, -0.04f, 0.9f),
		new("neck_01", "clavicle_r", 10, 0.04f, 0.9f),
		new("spine_03", "pelvis", 8, 0.06f, 1.1f),
	];

	//protected override IReadOnlyList<HumanoidRigWireSpec> RigWireLayout => WireLayout;

	enum Phase { None, Turning, Charge, Fire, Recover, FloatingFlash, StompTurning, Stomp, Swipe }
	enum Decision { FloatingFlash, Stomp, Walk, EyeLaser, Swipe }

	[I(24, 160), InspectorGroup("Eye Laser")] public float MinimumRange = 48;
	[InspectorGroup("Encounter")] public bool WaitForPlayerOnSamePlatform;
	[I(48, 320)] public float MaximumRange = 376;
	[I(8, 128)] public float VerticalRange = 48;
	static float Cooldown => 0.45f;
	[I(0.1f, 3)] public float SweepDuration = 0.85f;
	[I(0, 96)] public float SweepBeforePlayer = 36;
	[I(0, 96)] public float SweepBeyondPlayer = 48;
	[I(48, 480)] public float LaserRange = 300;
	[I(1, 12)] public float LaserThickness = 3;
	[I(0.02f, 0.5f)] public float LaserFadeTime = 0.12f;
	[I(4, 64)] public float LaserLightRadius = 24;
	[I(0, 3)] public float LaserLightIntensity = 1.35f;
	public Color LaserColor = 0x5fcde4;
	public ContentRef<SurfaceContent> LaserSurfaceId = new("");
	[I(4, 40), InspectorGroup("Floating Flash")] public float FloatingFlashHeight = 14;
	[I(0, 2)] public float FloatingPoseJostle = 1.3f;
	[I(0, 16)] public float FloatingWanderRadius = 8;
	[I(0.1f, 2)] public float FloatingWanderRate = 0.7f;
	[I(80, 360)] public float FloatingWaveSpeed = 200;
	[I(64, 640)] public float FloatingWaveRange = 520;
	[I(1, 8)] public float FloatingWaveThickness = 5.5f;
	[I(-2, 8)] public float FloatingWaveCollisionInset = 0.5f;
	[I(0, 0.4f)] public float FloatingWaveAccelerationTime = 0.16f;
	[I(0.1f, 1)] public float FloatingWaveInitialSpeed = 0.42f;
	public Color FloatingFlashColor = 0xffd7f2;
	[I(80, 360)] public float StompWaveSpeed = 120;
	[I(64, 640)] public float StompWaveRange = 460;
	[I(1, 8)] public float StompWaveThickness = 5.5f;
	[I(0, 0.4f)] public float StompWaveAccelerationTime = 0.12f;
	[I(0.1f, 1)] public float StompWaveInitialSpeed = 0.5f;
	[I(0, 12)] public float StompWaveTargetReach = 4;
	[I(0, 12)] public float StompWaveInset = 3;
	[I(0.1f, 0.9f)] public float StompImpactProgress = 0.84f;
	public Color StompWaveColor = 0xd9903d;
	[InspectorGroup("Swipe")] public bool SwipeFingerSpin = true;

	const string ChargeTake = "EyeLaserCharge";
	const string FireTake = "EyeLaserFire";
	const string RecoverTake = "EyeLaserRecover";
	const string FloatingFlashTake = "FloatingFlashPose";
	const string FloatingFlashLandTake = "FloatingFlashLand";
	const string StompTake = "Stomp";
	const string SwipeTake = "Swipe";
	const string DeathTake = "Death";
	const string ChargeEffect = "eye_laser_charge";
	const string FloatingFlashEffect = "floating_flash_charge";
	const string StompGatherEffect = "wave_pylon_solid_charge";
	const string HitSparkEffect = "elp_hit_sparks";
	const string GroundDustEffect = "elp_ground_dust";
	const string GreetingLine = "I need to eat your brain. Sorry.";
	const string WarmupParameter = "Warmup";
	const string FireDurationParameter = "FireDuration";

	Phase phase;
	int hitsTaken;
	int maxHealth;
	readonly LaserRay laser = new();
	Vector2 lockedTarget;
	Vector2 aimTarget;
	float sweepStartX;
	float sweepEndX;
	float cooldown;
	float fireTime;
	float beamScale;
	float beamAlpha;
	float eyeGlow;
	float particleTimer;
	float endpointPopTimer;
	int chargeEffect = -1;
	int floatingFlashEffect = -1;
	int stompGatherEffect = -1;
	int hitSparkEffect = -1;
	int groundDustEffect = -1;
	float flashParticleTimer;
	bool floating;
	uint floatingWanderSeed;
	int flashTick;
	Vector2 previousLaserEnd;
	bool laserContacting;
	bool stomped;
	bool stompHolding;
	bool stompHeld;
	bool stompLeftFoot;
	float stompHoldTime;
	float stompParticleTimer;
	float? walkTargetX;
	float walkTime;
	WeightedEnumSelector<Decision> decisions;
	float contactDamageGrace;
	float swipeStrength;
	bool greetingStarted;
	bool greeted;
	bool playerReachedPlatform;
	readonly LaserContactMark[] laserContactMarks = new LaserContactMark[8];
	int nextLaserContactMark;
	readonly Coroutine floatingFlash = new();
#if EDITOR
	readonly string?[] liveStatusLog = new string[24];
	int liveStatusLogStart;
	int liveStatusLogCount;
	int liveStatusLogVersion;
	int liveStatusDrawnLogVersion;
#endif

	struct LaserContactMark
	{
		public Vector2 Position;
		public float Remaining;
	}

	static float ChargeParticleRate => 42;
	static float ChargeParticleRadius => 18;
	static float EndpointPopInterval => 0.08f;
	static float FireGrowTime => 0.07f;
	static float FireShakeTime => 0.08f;
	static float FlashSettleTime => 0.2f;
	static float FlashTurnTime => 0.4f;
	static float FlashTurnHoldTime => 0.15f;
	static float FlashRiseTime => 1.29f;
	static float FlashHoverHoldTime => 0.25f;
	static float FlashChargeTime => 2;
	static float FlashStillTime => 0.075f;
	static float FlashReleaseShakeTime => 0.055f;
	static float FlashReleaseHoldTime => 0.045f;
	static float FlashAftershockTime => 0.25f;
	static float FlashDescentTime => 0.55f;
	static float FlashLandAnimationLeadTime => 0.35f;
	static float FlashLandIKLeadTime => 0.28f;
	static float FlashLandAnimationBlendTime => 0.12f;
	static float FlashLandAnimationSpeed => 1;
	static float FlashLandHoldTime => 0.15f;
	static float FlashRecoverTime => 0.35f;
	static float FlashRecoverBlendTime => 0.6f;
	static float FlashParticleRate => 54;
	static float FlashParticleRadius => 24;
	static float FlashRuffSpeed => MathF.Tau * 3.8f;
	static float FlashRecoilDistance => 4;
	static float FlashRecoilTime => 0.14f;
	static float FlashPoseRecoilSpeed => 105;
	static float FlashPoseRecoilLift => 16;
	static float FlashPoseRecoilSpread => 22;
	static float LaserContactMarkTime => 0.32f;
	static float LaserIgnitionShakeTime => 0.055f;
	static float LaserIgnitionKick => 42;
	static float LaserPoseRecoilSpeed => 82;
	static float LaserPoseRecoilLift => 8;
	static float LaserPoseRecoilSpread => 12;
	static float StompHoldLead => 0.22f;
	static float StompHoldDuration => 0.35f;
	static float StompHoldMinimumSpeed => 0.05f;
	static float StompPaintRadius => 17f;
	static float StompPaintSoftness => 0.6f;
	static float StompPaintStrength => 0.47f;
	static Color StompPaintColor => Color.Black;
	static float StompGatherRadius => 18;
	static float WalkSpeedup => 1.5f;
	static float LowHealthWalkSpeedup => 3f;
	static float WalkArriveDistance => 2;
	static float RetreatChance => 0.3f;
	static float RetreatDistance => 60f;
	static float DeathPopDuration => 3f;
	// Every step of a boss this size shakes, so it has to stay well under an impact.
	static float FootstepShakeIntensity => 0.22f;
	// The death shake builds across the three seconds instead of holding flat at full.
	static float DeathShakeStartIntensity => 0.15f;
	static float DeathShakeEndIntensity => 0.7f;
	static float SwipeFingerScale => 3.6f;
	static float SwipeWindupEnd => 0.42f;
	static float SwipeRecoveryStart => 0.72f;
	static float SwipeLightRadius => 28;
	static float SwipeLightIntensity => 1.15f;
	static float SwipeLightHeight => 6;
	static Color SwipeLightColor => new(0xff8058);
	static float LowHealthThreshold => 0.35f;
	static float SpeedupFactor => 1.3f;
	static Color LowHealthSkullTint => new(0x686868);
	static float WalkGiveUpTime => 2;
	static float HitLimbMotionStrength => 1.65f;
	static int HitsPerKnockdown => 5;
	static int HitSparkCount => 25;
	static float HitSparkUpwardBias => 0.65f;
	static int StepDustCount => 8;
	static int LandingDustCount => 16;

	static float SwipeCooldown => 0.55f;
	static float StompCooldown => 1.2f;
	static float FlashCooldown => 1.5f;

	static float ContactDamageGraceTime => 1.0f;
	static float NonKnockdownContactDamageGraceTime => 0.3f;
	float StompHoldProgress => MathF.Max(0, StompImpactProgress - StompHoldLead);
	bool LowHealth => Health > 0 && Health <= maxHealth * LowHealthThreshold;

	public EyeLaserPuppet()
	{
		Rig.NormalRoundness = 0.78f;
		Rig.NormalIrregularity = 0.16f;
	}

	public override void Update()
	{
		contactDamageGrace = MathF.Max(0, contactDamageGrace - Time.Delta);
		base.Update();
		for (var i = 0; i < laserContactMarks.Length; i++)
			laserContactMarks[i].Remaining = MathF.Max(0, laserContactMarks[i].Remaining - Time.Delta);
	}

	internal override void OnEvent(string eventName)
	{
		switch (eventName) {
			case "knockback":
				AudioEvent("boss/knockback");
				break;
		}
	}

	public override void Added()
	{
		maxHealth = Health;
		base.Added();
		chargeEffect = AcquireParticleEffect(ChargeEffect);
		floatingFlashEffect = AcquireParticleEffect(FloatingFlashEffect);
		stompGatherEffect = AcquireParticleEffect(StompGatherEffect);
		hitSparkEffect = AcquireParticleEffect(HitSparkEffect);
		groundDustEffect = AcquireParticleEffect(GroundDustEffect);
		ConfigureLaser();
	}

	protected override bool UpdateAbility(Player player, bool grounded)
	{
		if (WaitForPlayerOnSamePlatform && !playerReachedPlatform)
		{
			Velocity.X = 0;
			PlayAbilityIdle();
			SetAbilityLookTarget(player.Position);
			SetAbilityLookPoseWeight(1);
			visual.ExtraHeadSpriteRoll = MathF.PI * 0.39f;
			if (!grounded || !PlayerSharesGroundSupport(player))
				return true;
			visual.ExtraHeadSpriteRoll = 0;
			playerReachedPlatform = true;
			SetAbilityLookPoseWeight(0);
			LogLiveStatus("Player landed on boss platform");
		}

		if (!greeted)
		{
			if (greetingStarted)
				greeted = true;
			else
			{
				Velocity.X = 0;
				PlayAbilityIdle();
				SetAbilityLookTarget(player.Position);
				var view = new Rect(Game.Camera, new Vector2(Game.ZoomedWidth, Game.ZoomedHeight));
				if (!view.Overlaps(GetVisualBounds()))
					return true;
				greetingStarted = true;
				GreetPlayer(GreetingLine);
				return false;
			}
		}

		if (phase == Phase.None)
		{
			cooldown = MathF.Max(0, cooldown - Time.Delta * AnimationSpeedMultiplier);
			var toPlayer = player.Position - Position;
			var distance = MathF.Abs(toPlayer.X);
			if (walkTargetX is { } walkTarget)
			{
				walkTime += Time.Delta;
				if (walkTime >= WalkGiveUpTime)
				{
					LogLiveStatus($"Walk abandoned after {walkTime:0.0}s");
					walkTargetX = null;
					cooldown = Cooldown / AnimationSpeedMultiplier;
				}
				else if (MathF.Abs(walkTarget - Position.X) > WalkArriveDistance)
					return false;
				else
				{
					LogLiveStatus($"Walk complete at {distance:0.0}px");
					walkTargetX = null;
				}
			}
			var canLaser = CanLaser(toPlayer);
			var canSwipe = CanSwipe(toPlayer);
			if (!grounded)
				return false;

			Facing = toPlayer.X < 0 ? Signs.Negative : Signs.Positive;
			Velocity.X = 0;
			PlayAbilityIdle();
			SetAbilityLookTarget(player.Position);
			if (cooldown > 0)
				return true;

			var decision = ChooseAction(distance, canLaser, canSwipe);

			// hardcode this to test a behaviour.
			// decision = Decision.Stomp;

			switch (decision)
			{
				case Decision.FloatingFlash:
					BeginFloatingFlash(player);
					break;
				case Decision.Stomp:
					phase = Phase.StompTurning;
					break;
				case Decision.Walk:
					BeginWalk(player, toPlayer.X);
					return false;
				case Decision.EyeLaser:
					phase = Phase.Turning;
					break;
				case Decision.Swipe:
					BeginSwipe();
					break;
			}
		}

		Velocity.X = 0;
		switch (phase)
		{
			case Phase.Turning:
				SetAbilityPoseModifierWeight(1);
				SetAbilityLookTarget(player.Position);
				if (!grounded || MathF.Abs(player.Position.X - Position.X) > MaximumRange * 1.25f)
				{
					CancelAttack(Cooldown * 0.25f);
					return false;
				}
				Facing = player.Position.X < Position.X ? Signs.Negative : Signs.Positive;
				if (RigFacingMatches)
					BeginCharge(player);
				break;

			case Phase.Charge:
				SetAbilityPoseModifierWeight(0);
				SetAbilityLookTarget(lockedTarget);
				eyeGlow = Calc.Lerp(0.15f, 1, Ease.Cube.In(AbilityAnimationProgress));
				if (AbilityAnimationProgress > 0.36f && AbilityAnimationProgress < 0.9f)
					EmitChargeParticles();
				UpdateSound(AbilityAnimationProgress, 0);
				if (AbilityAnimationFinished)
					BeginFire();
				break;

			case Phase.Fire:
				UpdateFire();
				break;

			case Phase.Recover:
				beamAlpha = Calc.Approach(beamAlpha, 0, Time.Delta / MathF.Max(0.0001f, LaserFadeTime));
				eyeGlow = Calc.Approach(eyeGlow, 0, Time.Delta / MathF.Max(0.0001f, LaserFadeTime));
				SetAbilityPoseModifierWeight(Ease.Cube.InOut(AbilityAnimationProgress));
				SetAbilityLookTarget(aimTarget);
				if (AbilityAnimationFinished)
					FinishAttack();
				break;

			case Phase.FloatingFlash:
				if (!floatingFlash.Update(Time.Delta))
					FinishFloatingFlash();
				break;

			case Phase.StompTurning:
				SetAbilityPoseModifierWeight(1);
				SetAbilityLookTarget(player.Position);
				if (!grounded)
				{
					CancelAttack(StompCooldown * 0.25f);
					return false;
				}
				Facing = player.Position.X < Position.X ? Signs.Negative : Signs.Positive;
				if (RigFacingMatches)
					BeginStomp();
				break;

			case Phase.Stomp:
				SetAbilityLookTarget(null);
				SetAbilityPoseModifierWeight(1);
				if (!stompHeld && AbilityAnimationProgress >= StompHoldProgress)
					BeginStompHold();
				if (stompHolding)
					UpdateStompHold();
				if (!stompHolding && !stomped && AbilityAnimationProgress >= StompImpactProgress)
					ReleaseStomp(player);
				if (AbilityAnimationFinished)
					FinishStomp();
				break;

			case Phase.Swipe:
				UpdateSwipe();
				if (AbilityAnimationFinished)
					FinishSwipe();
				break;
		}

		return true;
	}

	void BeginWalk(Player player, float toPlayerX)
	{
		walkTime = 0;
		if (Game.rng.Chance(RetreatChance) && TryGetRetreatTarget(player, out var retreat))
		{
			walkTargetX = retreat;
			LogLiveStatus($"Retreat {RetreatDistance:0}px from {MathF.Abs(toPlayerX):0.0}px");
			return;
		}
		var closed = Game.rng.Float(0.5f, 0.9f);
		walkTargetX = Position.X + toPlayerX * closed;
		LogLiveStatus($"Walk {closed:P0} closer: {MathF.Abs(toPlayerX):0.0}px → {MathF.Abs(toPlayerX) * (1 - closed):0.0}px");
	}

	bool TryGetRetreatTarget(Player player, out float targetX)
	{
		var away = new Point2(player.Position.X < Position.X ? (int)RetreatDistance : -(int)RetreatDistance, 0);
		targetX = Position.X + away.X;
		return !OverlapsAny(away, Masks.Solid) &&
			Game.TryFindSurface(this, Hitbox.Rect + Position + away, Vector2.UnitY, 2, out _);
	}

	bool CanLaser(Vector2 toPlayer) => MathF.Abs(toPlayer.X) >= MinimumRange &&
		MathF.Abs(toPlayer.X) <= MaximumRange && MathF.Abs(toPlayer.Y) <= VerticalRange;

	static float SwipeRange => 60;

	bool CanSwipe(Vector2 toPlayer) => toPlayer.LengthSquared() < SwipeRange * SwipeRange &&
		Vector2.Dot(toPlayer, Vector2.UnitX * (int)Facing) > 0 && RigFacingMatches;

	void SetDecisionWeights(float distance, bool canLaser, bool canSwipe)
	{
		const int DecisionMaximumMisses = 5;
		const float FloatingDecisionWeight = 1.6f;
		const float DecisionPriorityPerMiss = 0.4f;
		const float CloseStompDecisionWeight = 1.8f;
		const float FarStompDecisionWeight = 0.5f;
		const float CloseLaserDecisionWeight = 0.55f;
		const float FarLaserDecisionWeight = 2.5f;
		const float SwipeDecisionWeight = 2.4f;
		const float CloseWalkDecisionWeight = 0.9f;
		const float FarWalkDecisionWeight = 1.8f;
		const float OutOfRangeWalkDecisionWeight = 2.5f;

		var distanceBias = Calc.ClampedMap(distance, SwipeRange, MaximumRange);
		decisions.Begin(DecisionPriorityPerMiss, DecisionMaximumMisses);
		decisions.Set(Decision.FloatingFlash, FloatingDecisionWeight);
		decisions.Set(Decision.Stomp, Calc.Lerp(CloseStompDecisionWeight, FarStompDecisionWeight, distanceBias));
		decisions.Set(Decision.EyeLaser,
			canLaser ? Calc.Lerp(CloseLaserDecisionWeight, FarLaserDecisionWeight, distanceBias) : 0);
		decisions.Set(Decision.Swipe, canSwipe ? SwipeDecisionWeight : 0);
		decisions.Set(Decision.Walk, distance > MaximumRange
			? OutOfRangeWalkDecisionWeight
			: Calc.Lerp(CloseWalkDecisionWeight, FarWalkDecisionWeight, distanceBias),
			increasePriorityWhenMissed: false);
	}

	Decision ChooseAction(float distance, bool canLaser, bool canSwipe)
	{
		SetDecisionWeights(distance, canLaser, canSwipe);
		if (!decisions.TryChoose(ref Game.rng, out var decision))
			throw new InvalidOperationException("Eye laser puppet has no available action");
		LogLiveStatus($"Picked {decision} at {distance:0.0}px (weight {decisions.Weight(decision):0.00})");
		return decision;
	}

	bool PlayerSharesGroundSupport(Player player)
	{
		if (!player.Grounded() || !TryGetGroundSupport(this, out var bossSupport) ||
			!TryGetGroundSupport(player, out var playerSupport))
			return false;
		if (bossSupport.Actor != null || playerSupport.Actor != null)
			return bossSupport.Actor != null && bossSupport.Actor == playerSupport.Actor;
		if (bossSupport.Slope.HasValue || playerSupport.Slope.HasValue)
			return bossSupport.Slope.HasValue && bossSupport.Slope == playerSupport.Slope;
		return bossSupport.Rect.HasValue && bossSupport.Rect == playerSupport.Rect;
	}

	bool TryGetGroundSupport(Actor actor, out SurfaceHit support)
		=> Game.TryFindSurface(actor, actor.Hitbox.Rect + actor.Position, Vector2.UnitY, 2, out support) &&
			support.Normal.Y < -0.5f;

	protected override bool AbilityControlsMovement => floating;
	protected override bool SuppressKnockdownOnHit =>
		phase == Phase.FloatingFlash || hitsTaken % HitsPerKnockdown != HitsPerKnockdown - 1;
	protected override bool DeathIsPersistent => true;
	protected override float? WalkTargetX => walkTargetX;
	protected override float WalkSpeedMultiplier => LowHealth ? LowHealthWalkSpeedup : WalkSpeedup;
	protected override float AnimationSpeedMultiplier => LowHealth ? SpeedupFactor : 1;
	protected override float HitLimbReactionStrength => HitLimbMotionStrength;
	protected override Color RigHeadTint => LowHealth ? LowHealthSkullTint : Color.White;
	protected override bool BackArmCombatEnabled => false;
	protected override void OnFootstep(Vector2 position)
	{
		// explicitly not calling base.OnFootstep(position)
		Game.Particles?.Emit(groundDustEffect, position, StepDustCount);
		Game.Shake(0.05f, FootstepShakeIntensity);
		AudioEvent("boss/footstep", "Type", Factory.SubstanceSoundType(Game.GetSubstanceBelow(this)));
	}
	internal override bool ContactDamageEnabled => contactDamageGrace <= 0 && Health > 0;

	void BeginSwipe()
	{
		swipeStrength = 0;
		SetAbilityYaw(RigFacingYaw);
		PlayAbilityTake(SwipeTake);
		SetAbilityLookTarget(null);
		SetAbilityFingerSpin(SwipeFingerSpin, leftHandOnly: true);
		SetAbilityHandWidthScale(1, leftHandOnly: true);
		phase = Phase.Swipe;
		swipeSound = Game.CreateAudioEvent("boss/saw", RigLeftHandPosition, start: true);
	}

	void UpdateSwipe()
	{
		Game.SetAudioEventPosition(swipeSound, RigLeftHandPosition);
		SetAbilityLookTarget(null);
		SetAbilityPoseModifierWeight(1);
		SetAbilityFingerSpin(SwipeFingerSpin, leftHandOnly: true);
		var progress = AbilityAnimationProgress;
		swipeStrength = progress < SwipeWindupEnd
			? Ease.Cube.InOut(progress / SwipeWindupEnd)
			: 1 - Ease.Cube.InOut(Math.Clamp(
				(progress - SwipeRecoveryStart) / (1 - SwipeRecoveryStart), 0, 1));
		SetAbilityHandWidthScale(Calc.Lerp(1, SwipeFingerScale, swipeStrength), leftHandOnly: true);
	}

	void FinishSwipe()
	{
		StopAbilitySounds();
		swipeStrength = 0;
		SetAbilityYaw(null);
		SetAbilityFingerSpin(false);
		SetAbilityHandWidthScale(1);
		phase = Phase.None;
		cooldown = SwipeCooldown / AnimationSpeedMultiplier;
		SetAbilityLookTarget(null);
		SetAbilityPoseModifierWeight(1);
	}

	void BeginStomp()
	{
		stomped = stompHolding = stompHeld = false;
		SetAbilityPlaybackSpeedMultiplier(1);
		SetAbilityYaw(RigFacingYaw);
		LockAbilityAnimationYaw();
		PlayAbilityTake(StompTake);
		SetAbilityLookTarget(null);
		phase = Phase.Stomp;
	}

	void BeginStompHold()
	{
		stompHolding = stompHeld = true;
		stompLeftFoot = RigLeftToePosition.Y < RigRightToePosition.Y;
		stompHoldTime = stompParticleTimer = 0;
		LogLiveStatus($"Stomp gather: {(stompLeftFoot ? "left" : "right")} foot");
	}

	void UpdateStompHold()
	{
		stompHoldTime = MathF.Min(stompHoldTime + Time.Delta, StompHoldDuration);
		var progress = stompHoldTime / StompHoldDuration;
		SetAbilityPlaybackSpeedMultiplier(Calc.Lerp(StompHoldMinimumSpeed, 1,
			Ease.Cube.InOut(MathF.Abs(progress * 2 - 1))));
		EmitStompGatherParticles(progress);
		stompHolding = stompHoldTime < StompHoldDuration;
		if (!stompHolding)
			SetAbilityPlaybackSpeedMultiplier(1);
	}

	void EmitStompGatherParticles(float progress)
	{
		if (stompGatherEffect < 0 || Game.Particles is not { } particles)
			return;
		stompParticleTimer -= Time.Delta;
		var interval = 1 / Calc.Lerp(18, 62, Ease.Cube.In(progress));
		var foot = stompLeftFoot ? RigLeftToePosition : RigRightToePosition;
		for (var emitted = 0; stompParticleTimer <= 0 && emitted < 4; emitted++, stompParticleTimer += interval)
		{
			var direction = Game.rng.Vec2().Normalized(Vector2.UnitX);
			var origin = foot + direction * Calc.Lerp(StompGatherRadius, 8, progress);
			particles.Emit(stompGatherEffect, origin, 1, Calc.Angle(-direction),
				Calc.Lerp(0.8f, 1.35f, progress), 0.12f);
		}
	}

	void ReleaseStomp(Player player)
	{
		stomped = true;
		Game.Shake(0.13f);
		var toe = stompLeftFoot ? RigLeftToePosition : RigRightToePosition;
		LogLiveStatus($"Stomp impact at ({toe.X:0.0}, {toe.Y:0.0})");
		var probeCenter = (toe - Vector2.UnitY * 4).RoundToPoint2();
		var probe = new RectInt(probeCenter.X - 1, probeCenter.Y - 1, 2, 2);
		if (!Game.TryFindSurface(this, probe, Vector2.UnitY, 12, out var surface) ||
			surface.Normal.Y >= -0.5f || surface.Actor?.Mask.Has(Masks.Jumpthru) == true ||
			Game.SurfaceIsJumpthru(surface.Point.RoundToPoint2(), -surface.Normal))
			return;

		var origin = surface.Point - surface.Normal * StompWaveInset;
		var wave = Game.Create<RadialWave>(origin.RoundToPoint2());
		wave.Source = this;
		wave.Target = player;
		wave.CanHit = static (wave, target) => wave.Medium == RadialWaveMedium.Solid
			? target is Player p && (p.State == Player.States.Burrow || p.Grounded())
			: target is not Player { State: Player.States.Burrow };
		wave.Medium = RadialWaveMedium.Solid;
		wave.Speed = StompWaveSpeed;
		wave.MaxRadius = StompWaveRange;
		wave.Thickness = StompWaveThickness;
		wave.AccelerationTime = StompWaveAccelerationTime;
		wave.InitialSpeedMultiplier = StompWaveInitialSpeed;
		wave.TargetReach = StompWaveTargetReach;
		wave.NeuralPerturbStrength = 1;
		wave.Color = StompWaveColor;
		wave.CoreColor = Color.Lerp(StompWaveColor, Color.White, 0.55f);
		wave.SetRoomInverseProfile(wave.Color, wave.CoreColor, 0);
		PaintStompScorch(surface.Point);
		AudioEvent("boss/stomp");
		Game.KickRenderTarget(Vector2.UnitY * 52);
		RecoilAbilityPose(-Vector2.UnitY * 58, 18);
	}

	// The game plays a CloneRuntime() room, so this scorch lives only for the session and can
	// never reach the room file the editor saves.
	void PaintStompScorch(Vector2 world)
	{
		if (Room is not { } room || !room.WorldBounds.Contains(world))
			return;
		room.ForegroundColorField.Paint(world - (Vector2)room.WorldBounds.TopLeft,
			StompPaintRadius, StompPaintSoftness, StompPaintStrength, StompPaintColor, erase: false);
	}

	void FinishStomp()
	{
		SetAbilityPlaybackSpeedMultiplier(1);
		SetAbilityYaw(null);
		UnlockAbilityAnimationYaw();
		if (Game.rng.Chance(LowHealth ? 0.72f : 0.55f)) {
			// a chance to stomp again
			phase = Phase.StompTurning;
			LogLiveStatus("Stomping again");
			return;
		} else {
			phase = Phase.None;
			cooldown = StompCooldown / AnimationSpeedMultiplier;
			SetAbilityLookTarget(null);
			SetAbilityPoseModifierWeight(1);
		}
	}

	void BeginFloatingFlash(Player player)
	{
		phase = Phase.FloatingFlash;
		floatingWanderSeed = Game.rng.U32();
		floatingFlash.Start(FloatingFlashRoutine(player));
	}

	IEnumerator<float> FloatingFlashRoutine(Player player)
	{
		PlayAbilityIdle();
		SetAbilityLookTarget(null);
		SetAbilityPoseModifierWeight(0);
		SetAbilityYaw(CameraFacingYaw, 0);
		yield return FlashSettleTime;

		for (var time = 0f; time < FlashTurnTime; time += Time.Delta)
		{
			SetAbilityYaw(CameraFacingYaw, Ease.Cube.InOut(Math.Clamp(time / FlashTurnTime, 0, 1)));
			yield return 0;
		}
		SetAbilityYaw(CameraFacingYaw);
		yield return FlashTurnHoldTime;

		PlayAbilityTake(FloatingFlashTake, FlashRiseTime);
		floating = true;
		var groundPosition = (Vector2)Position;
		var centerOfRoomX = Room?.WorldBounds.CenterF.X + Game.rng.Float(-30f, 30f) ?? Position.X;
		var landingPosition = groundPosition;
		var riseTarget = groundPosition - Vector2.UnitY * FloatingFlashHeight;
		riseTarget.X = Calc.Approach(riseTarget.X, centerOfRoomX, Time.Delta * 225.0f);
		riseSound = Game.CreateAudioEvent("boss/start_float", Position, start: true);
		for (var time = 0f; time < FlashRiseTime; time += Time.Delta)
		{
			var progress = Ease.Cube.InOut(Math.Clamp(time / FlashRiseTime, 0, 1));
			MoveFloating(Vector2.Lerp(groundPosition, riseTarget, progress));
			SetAbilityPoseJostle(FloatingPoseJostle * progress);
			yield return 0;
		}
		MoveFloating(riseTarget);
		var hoverCenter = (Vector2)Position;
		var wanderTime = 0f;
		for (var time = 0f; time < FlashHoverHoldTime; time += Time.Delta)
		{
			wanderTime += Time.Delta;

			float influence = time / FlashHoverHoldTime;
			hoverCenter.X = Calc.Approach(hoverCenter.X, centerOfRoomX, Time.Delta * 25.0f);
			MoveFloating(hoverCenter + influence * FloatingWander(wanderTime));
			SetAbilityPoseJostle(FloatingPoseJostle);
			yield return 0;
		}

		flashParticleTimer = 0;
		flashTick = 0;
		StartSound();
		var movingChargeTime = (FlashChargeTime - FlashStillTime) / AnimationSpeedMultiplier;
		for (var time = 0f; time < movingChargeTime; time += Time.Delta)
		{
			var progress = Math.Clamp(time / FlashChargeTime, 0, 1);
			wanderTime += Time.Delta;
			var wanderAmount = Ease.Cube.Out(progress);
			if (Room != null)
				hoverCenter.X = Calc.Approach(hoverCenter.X, Room.WorldBounds.CenterF.X, Time.Delta * 25.0f);
			MoveFloating(hoverCenter + FloatingWander(wanderTime) * wanderAmount);
			SetRuffSpin(FlashRuffSpeed * Ease.Cube.In(progress));
			SetAbilityPoseJostle(FloatingPoseJostle * Calc.Lerp(1, 0.25f, 1 - wanderAmount));
			eyeGlow = Calc.Lerp(0.15f, 1, Ease.Cube.In(progress));
			EmitFloatingFlashParticles(progress);
			EmitFloatingFlashTicks(progress);
			UpdateSound(progress, 0);
			yield return 0;
		}

		for (var time = 0f; time < FlashStillTime; time += Time.Delta)
		{
			var progress = Math.Clamp(time / FlashStillTime, 0, 1);
			MoveFloating(hoverCenter);
			SetAbilityPoseJostle(FloatingPoseJostle * 0.12f);
			SetAbilityVisualOffset(Vector2.UnitY * Ease.Cube.In(progress) * 1.5f);
			eyeGlow = 1;
			UpdateSound(1, 0);
			yield return 0;
		}

		var numPulses = 1;
		if (LowHealth) {
			numPulses = Game.rng.Chance(0.5f) ? 2 : 3;
		}
		for (var pulseIndex = 0; pulseIndex < numPulses; ++pulseIndex) {
			SetRuffSpin(FlashRuffSpeed);
			var releaseDirection = (player.Position - RigEyePosition).Normalized(Vector2.UnitY);
			Game.FlashAir(FloatingFlashColor, 0.025f, 0.12f);
			Game.ShakeRenderTarget(FlashReleaseShakeTime, 0.45f);
			Game.KickRenderTarget(releaseDirection * 58);
			UpdateSound(1, 0.01f);
			StopAndReleaseSound(ref riseSound);
			AudioEvent("boss/emit_floating_wave");
			EmitFloatingFlashBurst();
			RecoilAbilityPose(-releaseDirection * FlashPoseRecoilSpeed - Vector2.UnitY * FlashPoseRecoilLift,
				FlashPoseRecoilSpread);
			SetAbilityVisualOffset(-releaseDirection * FlashRecoilDistance);
			var wave = Game.Create<RadialWave>(RigEyePosition.RoundToPoint2());
			wave.Source = this;
			wave.Target = player;
			wave.CanHit = static (wave, target) => wave.Medium == RadialWaveMedium.Solid
				? target is Player p && (p.State == Player.States.Burrow || p.Grounded())
				: target is not Player { State: Player.States.Burrow };
			wave.Speed = FloatingWaveSpeed;
			wave.MaxRadius = FloatingWaveRange;
			wave.Thickness = FloatingWaveThickness;
			wave.AccelerationTime = FloatingWaveAccelerationTime;
			wave.InitialSpeedMultiplier = FloatingWaveInitialSpeed;
			wave.NeuralPerturbStrength = 1;
			wave.Color = Color.Lerp(FloatingFlashColor, new Color(0xff5fb7), 0.35f);
			wave.CoreColor = FloatingFlashColor;
			wave.SetRoomInverseProfile(wave.Color, wave.CoreColor, StompWaveTargetReach);
			wave.StartDelay = FlashReleaseHoldTime + Time.Delta;
			yield return 0;
			SetRuffSpin(0);
			FreezeAbilityPose(FlashReleaseHoldTime);
			yield return FlashReleaseHoldTime;
			SetRuffSpin(FlashRuffSpeed * 1.22f);
			for (var time = 0f; time < FlashRecoilTime; time += Time.Delta)
			{
				var progress = Math.Clamp(time / FlashRecoilTime, 0, 1);
				SetAbilityVisualOffset(-releaseDirection * FlashRecoilDistance * (1 - Ease.Cube.Out(progress)));
				SetRuffSpin(FlashRuffSpeed * Calc.Lerp(1.22f, 1, Ease.Cube.Out(progress)));
				eyeGlow = 1 - Ease.Cube.In(progress) * 0.35f;
				yield return 0;
			}
			if (numPulses > 1)
			{
				var littleWait = Game.rng.Float(0.1f, 0.5f);
				for (var time = 0f; time < littleWait; time += Time.Delta)
				{
					yield return 0;
				}
			}
		}
		SetAbilityVisualOffset(Vector2.Zero);

		for (var time = 0f; time < FlashAftershockTime; time += Time.Delta)
		{
			var remaining = 1 - Math.Clamp(time / FlashAftershockTime, 0, 1);
			wanderTime += Time.Delta;
			MoveFloating(hoverCenter + FloatingWander(wanderTime) * remaining);
			SetAbilityPoseJostle(FloatingPoseJostle * remaining);
			SetRuffSpin(FlashRuffSpeed * remaining);
			yield return 0;
		}
		SetRuffSpin(0);
		SetAbilityPoseJostle(0);
		SetAbilityVisualOffset(Vector2.Zero);
		eyeGlow = 0;
		StopAndReleaseSound(ref sound);
		MoveFloating(hoverCenter);
		landingPosition.X = hoverCenter.X;

		var descentFrom = (Vector2)Position;
		var landingAnimationStarted = false;
		var landingIKStarted = false;
		for (var time = 0f; time < FlashDescentTime; time += Time.Delta)
		{
			if (!landingAnimationStarted && time >= MathF.Max(0, FlashDescentTime - FlashLandAnimationLeadTime))
			{
				PlayAbilityTake(FloatingFlashLandTake, FlashLandAnimationBlendTime);
				SetAbilityPlaybackSpeedMultiplier(FlashLandAnimationSpeed);
				landingAnimationStarted = true;
			}
			if (!landingIKStarted && time >= MathF.Max(0, FlashDescentTime - FlashLandIKLeadTime))
			{
				SetAbilityFootIK(true, seekGround: true);
				landingIKStarted = true;
			}
			var progress = Ease.Cube.InOut(Math.Clamp(time / FlashDescentTime, 0, 1));
			MoveFloating(Vector2.Lerp(descentFrom, landingPosition, progress));
			yield return 0;
		}
		if (!landingAnimationStarted)
		{
			PlayAbilityTake(FloatingFlashLandTake, FlashLandAnimationBlendTime);
			SetAbilityPlaybackSpeedMultiplier(FlashLandAnimationSpeed);
		}
		SetAbilityFootIK(true, seekGround: true);
		MoveFloating(landingPosition);
		for (var time = 0f; !Grounded() && time < FlashDescentTime * 2; time += Time.Delta)
		{
			MovePixel(Point2.Down);
			yield return 0;
		}
		Velocity = Vector2.Zero;
		floating = false;
		Game.Particles?.Emit(groundDustEffect, LandingDustPosition(RigLeftToePosition), LandingDustCount);
		Game.Particles?.Emit(groundDustEffect, LandingDustPosition(RigRightToePosition), LandingDustCount);
		yield return FlashLandHoldTime;

		SetAbilityPlaybackSpeedMultiplier(1);
		PlayAbilityIdle(FlashRecoverBlendTime);
		for (var time = 0f; time < FlashRecoverBlendTime; time += Time.Delta)
		{
			SetAbilityYaw(CameraFacingYaw, 1 - Ease.Cube.InOut(Math.Clamp(time / FlashRecoverTime, 0, 1)));
			yield return 0;
		}
	}

	Vector2 LandingDustPosition(Vector2 toe)
	{
		var probeCenter = new Vector2(toe.X, Position.Y - 4).RoundToPoint2();
		var probe = new RectInt(probeCenter.X - 1, probeCenter.Y - 1, 2, 2);
		return Game.TryFindSurface(this, probe, Vector2.UnitY, 12, out var surface) && surface.Normal.Y < -0.5f
			? surface.Point
			: new Vector2(toe.X, Position.Y);
	}

	Vector2 FloatingWander(float time)
	{
		var position = time * FloatingWanderRate;
		var offset = new Vector2(
			SmoothNoise.Signed(position, floatingWanderSeed),
			SmoothNoise.Signed(position * 0.83f, floatingWanderSeed ^ 0x9e3779b9u));
		offset += new Vector2(
			SmoothNoise.Signed(position * 1.91f, floatingWanderSeed ^ 0x85ebca6bu),
			SmoothNoise.Signed(position * 1.67f, floatingWanderSeed ^ 0xc2b2ae35u)) * 0.3f;
		offset *= FloatingWanderRadius / 1.3f;
		var radiusSquared = FloatingWanderRadius * FloatingWanderRadius;
		return offset.LengthSquared() > radiusSquared
			? Vector2.Normalize(offset) * FloatingWanderRadius
			: offset;
	}

	void MoveFloating(Vector2 target) => MovePixels(target.RoundToPoint2() - Position);

	void EmitFloatingFlashParticles(float progress)
	{
		if (floatingFlashEffect < 0 || Game.Particles is not { } particles)
			return;
		flashParticleTimer -= Time.Delta;
		var interval = 1 / Calc.Lerp(FlashParticleRate * 0.35f, FlashParticleRate * 1.7f, Ease.Cube.In(progress));
		for (var emitted = 0; flashParticleTimer <= 0 && emitted < 4; emitted++, flashParticleTimer += interval)
		{
			var angle = Game.rng.Angle();
			var radius = Calc.Lerp(FlashParticleRadius, 16, progress);
			if (!TryGetRuffOrbitPoint(angle, radius, out var origin) ||
				!TryGetRuffOrbitPoint(angle, 0, out var center) ||
				!TryGetRuffOrbitPoint(angle + 0.12f, radius, out var next))
				return;
			var inward = (center - origin).Normalized();
			var tangent = (next - origin).Normalized();
			var direction = (inward + tangent * Calc.Lerp(0.35f, 0.8f, progress)).Normalized(inward);
			particles.Emit(floatingFlashEffect, origin, 1, MathF.Atan2(direction.Y, direction.X),
				Calc.Lerp(0.7f, 1.3f, progress), 0.08f);
		}
	}

	void EmitFloatingFlashTicks(float progress)
	{
		ReadOnlySpan<float> thresholds = [0.72f, 0.87f, 0.95f];
		if (flashTick >= thresholds.Length || progress < thresholds[flashTick])
			return;
		flashTick++;
		if (floatingFlashEffect < 0 || Game.Particles is not { } particles)
			return;
		for (var i = 0; i < 6; i++)
		{
			var direction = Calc.AngleToVector(i * MathF.Tau / 6 + Game.rng.Float(-0.08f, 0.08f));
			particles.Emit(floatingFlashEffect, RigEyePosition + direction * 7, 1,
				Calc.Angle(-direction), 1.15f + flashTick * 0.12f, 0.08f);
		}
	}

	void EmitFloatingFlashBurst()
	{
		if (floatingFlashEffect < 0 || Game.Particles is not { } particles)
			return;
		for (var i = 0; i < 18; i++)
		{
			var direction = Calc.AngleToVector(i * MathF.Tau / 18 + Game.rng.Float(-0.05f, 0.05f));
			particles.Emit(floatingFlashEffect, RigEyePosition + direction * 3, 1,
				Calc.Angle(direction), 1.8f, 0.1f);
		}
	}

	void FinishFloatingFlash()
	{
		floatingFlash.Stop();
		floating = false;
		phase = Phase.None;
		cooldown = FlashCooldown / AnimationSpeedMultiplier;
		Velocity = Vector2.Zero;
		SetRuffSpin(0);
		SetAbilityPoseJostle(0);
		SetAbilityVisualOffset(Vector2.Zero);
		SetAbilityYaw(null);
		SetAbilityLookTarget(null);
		SetAbilityPoseModifierWeight(1);
	}

	void BeginCharge(Player player)
	{
		lockedTarget = player.Position;
		var direction = player.Position.X < Position.X ? -1 : 1;
		sweepStartX = lockedTarget.X - direction * SweepBeforePlayer;
		sweepEndX = lockedTarget.X + direction * SweepBeyondPlayer;
		aimTarget = new(sweepStartX, lockedTarget.Y);
		particleTimer = endpointPopTimer = 0;
		eyeGlow = beamAlpha = beamScale = 0;
		PlayAbilityTake(ChargeTake);
		SetAbilityLookTarget(lockedTarget);
		SetAbilityPoseModifierWeight(0);
		phase = Phase.Charge;
		StartSound();
	}

	void BeginFire()
	{
		laserEndPause = 0.3f;
		fireTime = 0;
		beamScale = 0;
		beamAlpha = eyeGlow = 1;
		PlayAbilityTake(FireTake, 0.06f);
		phase = Phase.Fire;
		TraceLaser(0);
		previousLaserEnd = laser.End;
		laserContacting = false;
		RecoilAbilityPose(-laser.Direction * LaserPoseRecoilSpeed - Vector2.UnitY * LaserPoseRecoilLift,
			LaserPoseRecoilSpread);
		SetAbilityVisualOffset(-laser.Direction * 2.5f);
		Game.ShakeRenderTarget(LaserIgnitionShakeTime, 0.35f);
		Game.KickRenderTarget(laser.Direction * LaserIgnitionKick);
		EmitChargeBurst();
	}

	SoundInstance? laserSound;

	void UpdateFire()
	{
		fireTime += Time.Delta;
		var progress = Math.Clamp(fireTime / MathF.Max(0.0001f, SweepDuration), 0, 1);
		if (progress < 1 && laserSound == null)
		{
			laserSound = Game.CreateAudioEvent("boss/eye_laser_emit", RigEyePosition, start: true);
		}
		beamScale = Ease.Cube.Out(Math.Clamp(fireTime / FireGrowTime, 0, 1));
		beamAlpha = eyeGlow = 1;
		SetAbilityPoseModifierWeight(0);
		SetAbilityVisualOffset(-laser.Direction * 2.5f *
			(1 - Ease.Cube.Out(Math.Clamp(fireTime / 0.12f, 0, 1))));
		TraceLaser(Ease.Cube.InOut(progress));
		if (laser.HitObstacle && !laserContacting)
		{
			//Game.ShakeRenderTarget(FireShakeTime * 0.3f, 0.5f);
			//Game.KickRenderTarget(laser.Direction * 22);
		}
		Game.Shake(0.1f);
		laserContacting = laser.HitObstacle;
		SetAbilityLookTarget(aimTarget);
		if (laser.FirstOverlap(Game, Masks.Player, out RectInt hitRect, beamScale) is { } player)
			Hit(player, new(hitRect));
		UpdateEndpointPops();
		UpdateSound(1, progress);
		if (progress >= 1)
		{
			if (laserEndPause > 0)
			{
				laserEndPause = Calc.Approach(laserEndPause, 0, Time.Delta);
			}
			else
			{
				StopAndReleaseSound(ref laserSound);
				BeginRecover();
			}
		}
	}

	float laserEndPause;

	void TraceLaser(float progress)
	{
		aimTarget = new(float.Lerp(sweepStartX, sweepEndX, progress), lockedTarget.Y);
		ConfigureLaser();
		laser.Trace(Game, RigEyePosition, aimTarget - RigEyePosition);
	}

	void BeginRecover()
	{
		SetAbilityVisualOffset(Vector2.Zero);
		PlayAbilityTake(RecoverTake);
		phase = Phase.Recover;
		UpdateSound(1, 1);
		StopAndReleaseSound(ref sound);
	}

	void FinishAttack()
	{
		phase = Phase.None;
		beamAlpha = eyeGlow = 0;
		cooldown = Cooldown / AnimationSpeedMultiplier;
		SetAbilityLookTarget(null);
		SetAbilityPoseModifierWeight(1);
		StopAbilitySounds();
	}

	protected override void InterruptAbility() => CancelAttack(Cooldown * 0.5f);

	void CancelAttack(float nextCooldown)
	{
		floatingFlash.Stop();
		floating = false;
		phase = Phase.None;
		beamAlpha = eyeGlow = 0;
		cooldown = MathF.Max(cooldown, nextCooldown);
		SetRuffSpin(0);
		SetAbilityPoseJostle(0);
		SetAbilityVisualOffset(Vector2.Zero);
		SetAbilityYaw(null);
		SetAbilityFingerSpin(false);
		SetAbilityHandWidthScale(1);
		swipeStrength = 0;
		UnlockAbilityAnimationYaw();
		StopAbilitySounds(true);
	}

	protected override void Die()
	{
		CancelAttack(0);
		AudioEvent("boss/death");
		StartScript(DeathRoutine());
	}

	IEnumerator<float> DeathRoutine()
	{
		const float DeathPopInterval = 0.12f;
		PlayTake(DeathTake);
		for (var time = 0f; time < DeathPopDuration; time += Time.Delta)
		{
			Game.Shake(0.2f, Calc.Lerp(DeathShakeStartIntensity, DeathShakeEndIntensity,
				time / DeathPopDuration));
			if (Time.OnInterval(DeathPopInterval))
				Game.Create<Pop>(RandomPointIn(GetVisualBounds()));
			yield return 0;
		}
		var bounds = GetVisualBounds();
		for (var x = 0; x < 8; x++)
			for (var y = 0; y < 4; y++)
			{
				var popPos = bounds.TopLeft + bounds.Size * new Vector2(x, y) / 2;
				popPos += (Game.rng.Vec2() - (Vector2.One * 0.5f)) * 10f;
				Game.Create<Pop>(popPos.RoundToPoint2());
			}
		Game.HitStun(0.3f);
		Game.Shake(0.5f, 3f);
		DestroyOnDeath();
	}

	Point2 RandomPointIn(Rect bounds)
		=> (bounds.TopLeft + Game.rng.Float(bounds.Width) * Vector2.UnitX +
			Game.rng.Float(bounds.Height) * Vector2.UnitY).RoundToPoint2();

	void ConfigureLaser()
	{
		laser.Range = LaserRange;
		laser.Thickness = LaserThickness;
		laser.Color = LaserColor;
		laser.SurfaceId = LaserSurfaceId;
	}

	void EmitChargeParticles()
	{
		if (chargeEffect < 0 || Game.Particles is not { } particles)
			return;
		particleTimer -= Time.Delta;
		var interval = 1 / ChargeParticleRate;
		for (var emitted = 0; particleTimer <= 0 && emitted < 3; emitted++, particleTimer += interval)
		{
			var angle = Game.rng.Float(MathF.Tau);
			var direction = new Vector2(MathF.Cos(angle), MathF.Sin(angle));
			var radius = Calc.Lerp(ChargeParticleRadius, 7, AbilityAnimationProgress);
			var origin = RigEyePosition + direction * radius;
			particles.Emit(chargeEffect, origin, 1, MathF.Atan2(-direction.Y, -direction.X), 1, 0.12f);
		}
	}

	void EmitChargeBurst()
	{
		if (chargeEffect < 0 || Game.Particles is not { } particles)
			return;
		for (var i = 0; i < 12; i++)
		{
			var angle = i * MathF.Tau / 12;
			var direction = new Vector2(MathF.Cos(angle), MathF.Sin(angle));
			particles.Emit(chargeEffect, RigEyePosition + direction * 8, 1,
				MathF.Atan2(-direction.Y, -direction.X), 1.4f, 0.08f);
		}
	}

	void UpdateEndpointPops()
	{
		endpointPopTimer -= Time.Delta;
		if (!laser.HitObstacle || endpointPopTimer > 0)
		{
			previousLaserEnd = laser.End;
			return;
		}
		endpointPopTimer += EndpointPopInterval;
		var sweep = (laser.End - previousLaserEnd).Normalized();
		var pop = Game.Create<Pop>((Point2)laser.End);
		pop.Velocity = -laser.Direction * Game.rng.Float(18, 34) +
			sweep * Game.rng.Float(10, 24) + Game.rng.Vec2() * 6;
		ref var mark = ref laserContactMarks[nextLaserContactMark++ % laserContactMarks.Length];
		mark.Position = laser.End;
		mark.Remaining = LaserContactMarkTime;
		Game.ShakeRenderTarget(0.018f, 0.12f);
		previousLaserEnd = laser.End;
	}

	SoundInstance? sound;
	SoundInstance? swipeSound;
	SoundInstance? riseSound;

	void StopAbilitySounds(bool immediate = false)
	{
		StopAndReleaseSound(ref sound, immediate);
		StopAndReleaseSound(ref laserSound, immediate);
		StopAndReleaseSound(ref swipeSound, immediate);
		StopAndReleaseSound(ref riseSound, immediate);
	}

	void StartSound()
	{
		StopAndReleaseSound(ref sound, true);
		string EyeLaserSound = "boss/eye_laser";
		sound = Game.CreateAudioEvent(EyeLaserSound, RigEyePosition);
		UpdateSound(0, 0);
		sound?.Start();
	}

	void UpdateSound(float warmup, float fireDuration)
	{
		Game.SetAudioEventPosition(sound, RigEyePosition);
		Game.SetAudioEventParameter(sound, WarmupParameter, warmup);
		Game.SetAudioEventParameter(sound, FireDurationParameter, fireDuration);
	}

	[Conditional("EDITOR")]
	void LogLiveStatus(string message)
	{
#if EDITOR
		var index = (liveStatusLogStart + liveStatusLogCount) % liveStatusLog.Length;
		if (liveStatusLogCount == liveStatusLog.Length)
		{
			index = liveStatusLogStart;
			liveStatusLogStart = (liveStatusLogStart + 1) % liveStatusLog.Length;
		}
		else
			liveStatusLogCount++;
		liveStatusLog[index] = $"{Time.SecondsF,6:0.0}s  {message}";
		liveStatusLogVersion++;
#endif
	}

	public void LiveStatusImGui()
	{
#if EDITOR
		var value = new ValueStringBuilder(stackalloc char[128]);
		var action = phase switch
		{
			Phase.None when walkTargetX.HasValue => "Walking",
			Phase.None when cooldown > 0 => "Cooldown",
			Phase.None => "Choosing",
			Phase.Turning => "Turning for eye laser",
			Phase.Charge => "Charging eye laser",
			Phase.Fire => "Firing eye laser",
			Phase.Recover => "Recovering from eye laser",
			Phase.FloatingFlash => "Floating radial wave",
			Phase.StompTurning => "Turning for stomp",
			Phase.Stomp when stompHolding => "Holding stomp",
			Phase.Stomp => "Stomping",
			Phase.Swipe => "Swiping",
			_ => "Unknown"
		};

		ImGui.SeparatorText("State");
		if (ImGui.BeginTable("##EyeLaserPuppetState", 2, ImGuiTableFlags.SizingFixedFit | ImGuiTableFlags.RowBg))
		{
			LiveStatusRow("Action", action);
			LiveStatusRow("Phase enum", phase switch
			{
				Phase.None => nameof(Phase.None),
				Phase.Turning => nameof(Phase.Turning),
				Phase.Charge => nameof(Phase.Charge),
				Phase.Fire => nameof(Phase.Fire),
				Phase.Recover => nameof(Phase.Recover),
				Phase.FloatingFlash => nameof(Phase.FloatingFlash),
				Phase.StompTurning => nameof(Phase.StompTurning),
				Phase.Stomp => nameof(Phase.Stomp),
				Phase.Swipe => nameof(Phase.Swipe),
				_ => "Unknown"
			});

			value.Append($"({Position.X}, {Position.Y})");
			LiveStatusRow("Position", value.AsSpan());
			value.Clear();
			value.Append($"({Velocity.X:0.0}, {Velocity.Y:0.0})");
			LiveStatusRow("Velocity", value.AsSpan());
			value.Clear();
			LiveStatusRow("Grounded", Grounded() ? "Yes" : "No");
			LiveStatusRow("Facing", Facing == Signs.Negative ? "Left" : "Right");
			LiveStatusRow("Rig facing ready", RigFacingMatches ? "Yes" : "No");

			if (Game.GetFirst<Player>() is { } player)
			{
				var toPlayer = player.Position - Position;
				var horizontalDistance = MathF.Abs(toPlayer.X);
				var verticalDistance = MathF.Abs(toPlayer.Y);
				value.Append($"{horizontalDistance:0.0}px horizontal, {verticalDistance:0.0}px vertical, {toPlayer.Length():0.0}px total");
				LiveStatusRow("Player distance", value.AsSpan());
				value.Clear();
				SetDecisionWeights(horizontalDistance, CanLaser(toPlayer), CanSwipe(toPlayer));
				foreach (var choice in WeightedEnumSelector<Decision>.Choices)
				{
					var weight = decisions.Weight(choice);
					if (weight > 0)
						value.Append($"{weight / decisions.TotalWeight:P0} (weight {weight:0.00}, missed {decisions.Misses(choice)})");
					else
						value.Append("Unavailable");
					LiveStatusRow(WeightedEnumSelector<Decision>.Name(choice), value.AsSpan());
					value.Clear();
				}
			}
			else
				LiveStatusRow("Player", "Missing");

			value.Append($"{cooldown:0.00}s");
			LiveStatusRow("Cooldown", value.AsSpan());
			value.Clear();
			if (contactDamageGrace > 0)
			{
				value.Append($"Harmless for {contactDamageGrace:0.00}s");
				LiveStatusRow("Contact damage", value.AsSpan());
				value.Clear();
			}
			else
				LiveStatusRow("Contact damage", "Active");
			if (walkTargetX is { } walkTarget)
			{
				value.Append($"x={walkTarget:0.0} ({walkTarget - Position.X:+0.0;-0.0}px)");
				LiveStatusRow("Walk target", value.AsSpan());
				value.Clear();
			}
			if (phase is not Phase.None and not Phase.FloatingFlash)
			{
				value.Append($"{AbilityAnimationProgress:P0}");
				LiveStatusRow("Animation", value.AsSpan());
				value.Clear();
			}
			if (stompHolding)
			{
				value.Append($"{stompHoldTime / StompHoldDuration:P0} ({stompHoldTime:0.00}s / {StompHoldDuration:0.00}s)");
				LiveStatusRow("Stomp hold", value.AsSpan());
			}
			ImGui.EndTable();
		}
		value.Dispose();

		ImGui.SeparatorText("Decision Log");
		ImGui.SameLine();
		if (ImGui.SmallButton("Clear"))
		{
			Array.Clear(liveStatusLog);
			liveStatusLogStart = liveStatusLogCount = 0;
			liveStatusLogVersion++;
		}
		var scrollToLatest = liveStatusDrawnLogVersion != liveStatusLogVersion;
		if (ImGui.BeginChild("##EyeLaserPuppetLog", new Vector2(0, 0), ImGuiChildFlags.Borders, ImGuiWindowFlags.None))
		{
			if (liveStatusLogCount == 0)
				ImGui.TextDisabled("No decisions yet.");
			for (var i = 0; i < liveStatusLogCount; i++)
				ImGui.TextUnformatted(liveStatusLog[(liveStatusLogStart + i) % liveStatusLog.Length]);
			if (scrollToLatest)
				ImGui.SetScrollHereY(1);
		}
		ImGui.EndChild();
		liveStatusDrawnLogVersion = liveStatusLogVersion;
#endif
	}

	internal override void OnWasHurt() {
		Game.Shake(0.3f);
		hitsTaken++;
		cooldown = 0;
		AudioEvent("boss/hurt");
	}

#if EDITOR
	static void LiveStatusRow(string label, ReadOnlySpan<char> value)
	{
		ImGui.TableNextRow();
		ImGui.TableNextColumn();
		ImGui.TextDisabled(label);
		ImGui.TableNextColumn();
		ImGui.TextUnformatted(value);
	}
#endif

	internal override void OnWasHit(HitInfo hitInfo)
	{
		if (hitInfo.By is Player)
		{
			Game.KickRenderTarget(new Vector2(1f, 0.5f));
			contactDamageGrace = SuppressKnockdownOnHit
				? NonKnockdownContactDamageGraceTime : ContactDamageGraceTime;
			LogLiveStatus($"Contact damage disabled for {contactDamageGrace:0.0}s");
		}

		Point2 popPos = new();
		if (hitInfo.By.Position.X > Position.X) {
			popPos = hitInfo.AttackBox.Rect.CenterLeft;
		} else if (hitInfo.By.Position.X < Position.X) {
			popPos = hitInfo.AttackBox.Rect.CenterRight;
		} else {
			popPos = hitInfo.AttackBox.Rect.Center;
		}
		Game.Create<Pop>(popPos, (pop) =>
		{
			pop.SpriteName = "impact";
			pop.AnimationName = "star_hit";
			pop.Flags |= Pop.PopFlags.AnimateDuringHitStun;
			pop.Flags |= Pop.PopFlags.Emissive;
		});
		var away = MathF.Sign(Position.X - hitInfo.By.Position.X);
		var sparkDirection = new Vector2(away == 0 ? (int)Facing : away, -HitSparkUpwardBias).Normalized();
		Game.Particles?.Emit(hitSparkEffect, popPos, HitSparkCount, Calc.Angle(sparkDirection));
		base.OnWasHit(hitInfo);
	}

	public override void Destroyed()
	{
		base.Destroyed();
		StopAbilitySounds(true);
		ReleaseParticleEffect(ref chargeEffect);
		ReleaseParticleEffect(ref floatingFlashEffect);
		ReleaseParticleEffect(ref stompGatherEffect);
		ReleaseParticleEffect(ref hitSparkEffect);
		ReleaseParticleEffect(ref groundDustEffect);
	}

	public override void Render(Batcher batcher)
	{
		base.Render(batcher);
		RenderLaser(batcher, false);
	}

	public override void RenderEmissive(Batcher batcher) => RenderLaser(batcher, true);

	void RenderLaser(Batcher batcher, bool emissive)
	{
		var glowColor = phase == Phase.FloatingFlash ? FloatingFlashColor : LaserColor;
		if (beamAlpha > 0)
		{
			var ignition = 1 - Math.Clamp(fireTime / 0.2f, 0, 1);
			var instability = 1 + MathF.Sin(Time.SecondsF * 83) * 0.22f * ignition +
				MathF.Sin(Time.SecondsF * 31) * 0.035f;
			laser.Render(batcher, RigEyeOffset, beamScale, beamAlpha, Time.SecondsF, instability);
		}
		for (var i = 0; i < laserContactMarks.Length; i++)
		{
			var mark = laserContactMarks[i];
			if (mark.Remaining <= 0)
				continue;
			var alpha = Ease.Cube.Out(mark.Remaining / LaserContactMarkTime);
			var position = mark.Position - Position;
			if (emissive)
				batcher.Circle(position, 2.8f, 10, LaserColor * (alpha * 0.55f));
			else
			{
				batcher.Circle(position, 2.2f, 10, new Color(0x351d39) * (alpha * 0.75f));
				batcher.Circle(position, 1.1f, 8, LaserColor * (alpha * 0.7f));
			}
		}
		if (eyeGlow <= 0 || !RigEyeVisible)
			return;
		var pulse = 0.85f + MathF.Abs(MathF.Sin(Time.SecondsF * 31)) * 0.35f;
		batcher.Circle(RigEyeOffset, (1.3f + eyeGlow * 1.8f) * pulse, 12, glowColor * eyeGlow);
	}

	public override void ForEachLight(Action<Vector2, float, float, float, Color> action)
	{
		if (eyeGlow > 0 && RigEyeVisible)
			action(RigEyePosition, LaserLightRadius * eyeGlow, LaserLightIntensity * eyeGlow, 8,
				phase == Phase.FloatingFlash ? FloatingFlashColor : LaserColor);
		if (beamAlpha > 0)
			action(laser.ScaledEnd(beamScale), LaserLightRadius * beamAlpha,
				LaserLightIntensity * 0.75f * beamAlpha, 6, LaserColor);
		if (SwipeFingerSpin && swipeStrength > 0)
			action(RigLeftHandPosition, SwipeLightRadius * swipeStrength,
				SwipeLightIntensity * swipeStrength, SwipeLightHeight, SwipeLightColor);
	}

	public override Rect GetVisualBounds()
	{
		var bounds = beamAlpha > 0 ? base.GetVisualBounds().Conflate(laser.ScaledEnd(beamScale)).Inflate(LaserThickness + 2) :
			base.GetVisualBounds();
		for (var i = 0; i < laserContactMarks.Length; i++)
			if (laserContactMarks[i].Remaining > 0)
				bounds = bounds.Conflate(laserContactMarks[i].Position);
		return bounds;
	}
}
