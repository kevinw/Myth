using System.Numerics;
using Foster.Framework;

namespace Myth;

[GameActor(Sprite = "player", Tags = ActorTag.Enemy, BoundsX = -12, BoundsY = -40, BoundsW = 24, BoundsH = 40,
	LivePositionEditable = true)]
public sealed class EyeLaserPuppet : RigPuppet
{
	enum Phase { None, Turning, Charge, Fire, Recover, FloatingFlash }

	[I(24, 160), InspectorGroup("Eye Laser")] public float MinimumRange = 48;
	[I(48, 320)] public float MaximumRange = 176;
	[I(8, 128)] public float VerticalRange = 48;
	[I(0.1f, 8)] public float Cooldown = 2.5f;
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
	public string SoundEvent = "lightning_trap";
	[I(0, 1), InspectorGroup("Floating Flash")] public float FloatingFlashChance = 0.55f;
	[I(4, 40)] public float FloatingFlashHeight = 14;
	[I(0, 2)] public float FloatingPoseJostle = 1.3f;
	[I(0, 16)] public float FloatingWanderRadius = 8;
	[I(0.1f, 2)] public float FloatingWanderRate = 0.7f;
	[I(80, 360)] public float FloatingWaveSpeed = 220;
	[I(64, 480)] public float FloatingWaveRange = 320;
	[I(1, 8)] public float FloatingWaveThickness = 3.5f;
	public Color FloatingFlashColor = 0xffd7f2;

	const string ChargeTake = "EyeLaserCharge";
	const string FireTake = "EyeLaserFire";
	const string RecoverTake = "EyeLaserRecover";
	const string FloatingFlashTake = "FloatingFlashPose";
	const string ChargeEffect = "eye_laser_charge";
	const string FloatingFlashEffect = "floating_flash_charge";
	const string WarmupParameter = "Warmup";
	const string FireDurationParameter = "FireDuration";

	Phase phase;
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
	SoundInstance? sound;
	float flashParticleTimer;
	bool floating;
	uint floatingWanderSeed;
	RadialWave? floatingWave;
	readonly Coroutine floatingFlash = new();

	static float ChargeParticleRate => 42;
	static float ChargeParticleRadius => 18;
	static float EndpointPopInterval => 0.08f;
	static float FireGrowTime => 0.07f;
	static float FireShakeTime => 0.08f;
	static float FlashSettleTime => 0.2f;
	static float FlashTurnTime => 0.4f;
	static float FlashTurnHoldTime => 0.15f;
	static float FlashRiseTime => 0.75f;
	static float FlashHoverHoldTime => 0.25f;
	static float FlashChargeTime => 2;
	static float FlashImpactTime => 0.1f;
	static float FlashWaveEndHoldTime => 0.15f;
	static float FlashAftershockTime => 0.25f;
	static float FlashDescentTime => 0.55f;
	static float FlashLandHoldTime => 0.15f;
	static float FlashRecoverTime => 0.35f;
	static float FlashCooldown => 4;
	static float FlashParticleRate => 54;
	static float FlashParticleRadius => 24;
	static float FlashRuffSpeed => MathF.Tau * 3.8f;

	public override void Added()
	{
		base.Added();
		chargeEffect = Game.Particles?.AcquireEffect(ChargeEffect) ?? -1;
		floatingFlashEffect = Game.Particles?.AcquireEffect(FloatingFlashEffect) ?? -1;
		ConfigureLaser();
	}

	protected override bool UpdateAbility(Player player, bool grounded)
	{
		if (phase == Phase.None)
		{
			cooldown = MathF.Max(0, cooldown - Time.Delta);
			var toPlayer = player.Position - Position;
			var distance = MathF.Abs(toPlayer.X);
			if (!grounded || distance < MinimumRange || distance > MaximumRange ||
				MathF.Abs(toPlayer.Y) > VerticalRange)
				return false;

			Facing = toPlayer.X < 0 ? Signs.Negative : Signs.Positive;
			Velocity.X = 0;
			PlayAbilityIdle();
			SetAbilityLookTarget(player.Position);
			if (cooldown > 0)
				return true;
			if (Ruff && Game.rng.Chance(FloatingFlashChance))
				BeginFloatingFlash(player);
			else
				phase = Phase.Turning;
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
		}

		return true;
	}

	protected override bool AbilityControlsMovement => floating;
	protected override bool SuppressKnockdownOnHit => floating;

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
		var riseTarget = groundPosition - Vector2.UnitY * FloatingFlashHeight;
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
			MoveFloating(hoverCenter + FloatingWander(wanderTime));
			SetAbilityPoseJostle(FloatingPoseJostle);
			yield return 0;
		}

		flashParticleTimer = 0;
		for (var time = 0f; time < FlashChargeTime; time += Time.Delta)
		{
			var progress = Math.Clamp(time / FlashChargeTime, 0, 1);
			wanderTime += Time.Delta;
			MoveFloating(hoverCenter + FloatingWander(wanderTime));
			SetRuffSpin(FlashRuffSpeed * Ease.Cube.In(progress));
			EmitFloatingFlashParticles(progress);
			yield return 0;
		}

		SetRuffSpin(FlashRuffSpeed);
		Game.ShakeRenderTarget(FlashImpactTime);
		var wave = floatingWave = Game.Create<RadialWave>(RigEyePosition.RoundToPoint2());
		wave.Source = this;
		wave.Target = player;
		wave.CanHit = static target => target is not Player { State: Player.States.Burrow };
		wave.Speed = FloatingWaveSpeed;
		wave.MaxRadius = FloatingWaveRange;
		wave.Thickness = FloatingWaveThickness;
		wave.Color = Color.Lerp(FloatingFlashColor, new Color(0xff5fb7), 0.35f);
		wave.CoreColor = FloatingFlashColor;
		for (var time = 0f; time < FlashImpactTime; time += Time.Delta)
		{
			wanderTime += Time.Delta;
			MoveFloating(hoverCenter + FloatingWander(wanderTime));
			yield return 0;
		}
		while (!wave.Finished)
		{
			wanderTime += Time.Delta;
			MoveFloating(hoverCenter + FloatingWander(wanderTime));
			yield return 0;
		}
		floatingWave = null;
		for (var time = 0f; time < FlashWaveEndHoldTime; time += Time.Delta)
			yield return 0;

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
		MoveFloating(hoverCenter);

		var descentFrom = (Vector2)Position;
		for (var time = 0f; time < FlashDescentTime; time += Time.Delta)
		{
			var progress = Ease.Cube.InOut(Math.Clamp(time / FlashDescentTime, 0, 1));
			MoveFloating(Vector2.Lerp(descentFrom, groundPosition, progress));
			yield return 0;
		}
		MoveFloating(groundPosition);
		for (var time = 0f; !Grounded() && time < FlashDescentTime * 2; time += Time.Delta)
		{
			MovePixel(Point2.Down);
			yield return 0;
		}
		Velocity = Vector2.Zero;
		floating = false;
		yield return FlashLandHoldTime;

		PlayAbilityIdle();
		for (var time = 0f; time < FlashRecoverTime; time += Time.Delta)
		{
			SetAbilityYaw(CameraFacingYaw, 1 - Ease.Cube.InOut(Math.Clamp(time / FlashRecoverTime, 0, 1)));
			yield return 0;
		}
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
		var interval = 1 / FlashParticleRate;
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

	void FinishFloatingFlash()
	{
		floatingFlash.Stop();
		DestroyFloatingWave();
		floating = false;
		phase = Phase.None;
		cooldown = FlashCooldown;
		Velocity = Vector2.Zero;
		SetRuffSpin(0);
		SetAbilityPoseJostle(0);
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
		fireTime = 0;
		beamScale = 0;
		beamAlpha = eyeGlow = 1;
		PlayAbilityTake(FireTake, 0.06f);
		phase = Phase.Fire;
		TraceLaser(0);
		EmitChargeBurst();
	}

	void UpdateFire()
	{
		fireTime += Time.Delta;
		var progress = Math.Clamp(fireTime / MathF.Max(0.0001f, SweepDuration), 0, 1);
		beamScale = Ease.Cube.Out(Math.Clamp(fireTime / FireGrowTime, 0, 1));
		beamAlpha = eyeGlow = 1;
		SetAbilityPoseModifierWeight(0);
		TraceLaser(Ease.Cube.InOut(progress));
		if (laser.HitObstacle)
			Game.ShakeRenderTarget(FireShakeTime);
		SetAbilityLookTarget(aimTarget);
		if (laser.FirstOverlap(Game, Masks.Player, beamScale) is { } player)
			Hit(player);
		UpdateEndpointPops();
		UpdateSound(1, progress);
		if (progress >= 1)
			BeginRecover();
	}

	void TraceLaser(float progress)
	{
		aimTarget = new(float.Lerp(sweepStartX, sweepEndX, progress), lockedTarget.Y);
		ConfigureLaser();
		laser.Trace(Game, RigEyePosition, aimTarget - RigEyePosition);
	}

	void BeginRecover()
	{
		PlayAbilityTake(RecoverTake);
		phase = Phase.Recover;
		UpdateSound(1, 1);
		StopAndReleaseSound(ref sound);
	}

	void FinishAttack()
	{
		phase = Phase.None;
		beamAlpha = eyeGlow = 0;
		cooldown = Cooldown;
		SetAbilityLookTarget(null);
		SetAbilityPoseModifierWeight(1);
		StopAndReleaseSound(ref sound);
	}

	protected override void InterruptAbility() => CancelAttack(Cooldown * 0.5f);

	void CancelAttack(float nextCooldown)
	{
		floatingFlash.Stop();
		DestroyFloatingWave();
		floating = false;
		phase = Phase.None;
		beamAlpha = eyeGlow = 0;
		cooldown = MathF.Max(cooldown, nextCooldown);
		SetRuffSpin(0);
		SetAbilityPoseJostle(0);
		SetAbilityYaw(null);
		StopAndReleaseSound(ref sound, true);
	}

	void DestroyFloatingWave()
	{
		if (floatingWave != null && !Game.destroying.Contains(floatingWave))
			Game.Destroy(floatingWave);
		floatingWave = null;
	}

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
			return;
		endpointPopTimer += EndpointPopInterval;
		Game.Create<Pop>((Point2)laser.End, pop =>
			pop.Velocity = new Vector2(Game.rng.Float(-12, 12), Game.rng.Float(-18, 4)));
	}

	void StartSound()
	{
		StopAndReleaseSound(ref sound, true);
		sound = Game.CreateAudioEvent(SoundEvent, RigEyePosition);
		UpdateSound(0, 0);
		sound?.Start();
	}

	void UpdateSound(float warmup, float fireDuration)
	{
		Game.SetAudioEventPosition(sound, RigEyePosition);
		Game.SetAudioEventParameter(sound, WarmupParameter, warmup);
		Game.SetAudioEventParameter(sound, FireDurationParameter, fireDuration);
	}

	public override void Destroyed()
	{
		base.Destroyed();
		Game.Particles?.ReleaseEffect(ref chargeEffect);
		Game.Particles?.ReleaseEffect(ref floatingFlashEffect);
	}

	public override void Render(Batcher batcher)
	{
		base.Render(batcher);
		RenderLaser(batcher);
	}

	public override void RenderEmissive(Batcher batcher) => RenderLaser(batcher);

	void RenderLaser(Batcher batcher)
	{
		if (beamAlpha > 0)
			laser.Render(batcher, RigEyeOffset, beamScale, beamAlpha, Time.SecondsF);
		if (eyeGlow <= 0)
			return;
		var pulse = 0.85f + MathF.Abs(MathF.Sin(Time.SecondsF * 31)) * 0.35f;
		batcher.Circle(RigEyeOffset, (1.3f + eyeGlow * 1.8f) * pulse, 12, LaserColor * eyeGlow);
	}

	public override void ForEachLight(Action<Vector2, float, float, float, Color> action)
	{
		if (eyeGlow > 0)
			action(RigEyePosition, LaserLightRadius * eyeGlow, LaserLightIntensity * eyeGlow, 8, LaserColor);
		if (beamAlpha > 0)
			action(laser.ScaledEnd(beamScale), LaserLightRadius * beamAlpha,
				LaserLightIntensity * 0.75f * beamAlpha, 6, LaserColor);
	}

	public override Rect GetVisualBounds()
		=> beamAlpha > 0 ? base.GetVisualBounds().Conflate(laser.ScaledEnd(beamScale)).Inflate(LaserThickness + 2) :
			base.GetVisualBounds();
}
