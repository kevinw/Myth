using System.Numerics;
using Foster.Framework;

namespace Myth;

[GameActor(Sprite = "player", Tags = ActorTag.Enemy, BoundsX = -12, BoundsY = -40, BoundsW = 24, BoundsH = 40,
	LivePositionEditable = true)]
public sealed class EyeLaserPuppet : RigPuppet
{
	enum Phase { None, Turning, Charge, Fire, Recover }

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

	const string ChargeTake = "EyeLaserCharge";
	const string FireTake = "EyeLaserFire";
	const string RecoverTake = "EyeLaserRecover";
	const string ChargeEffect = "eye_laser_charge";
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
	SoundInstance? sound;

	static float ChargeParticleRate => 42;
	static float ChargeParticleRadius => 18;
	static float EndpointPopInterval => 0.08f;
	static float FireGrowTime => 0.07f;
	static float FireShakeTime => 0.08f;

	public override void Added()
	{
		base.Added();
		chargeEffect = Game.Particles?.AcquireEffect(ChargeEffect) ?? -1;
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
		}

		return true;
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
		phase = Phase.None;
		beamAlpha = eyeGlow = 0;
		cooldown = MathF.Max(cooldown, nextCooldown);
		StopAndReleaseSound(ref sound, true);
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
