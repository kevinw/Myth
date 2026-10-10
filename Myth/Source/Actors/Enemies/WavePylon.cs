using System.Numerics;
using Foster.Framework;

namespace Myth;

[GameActor(Sprite = "Editor/particles", Icon = "wave-square", Tags = ActorTag.Hazard,
	BoundsX = -8, BoundsY = -8, BoundsW = 16, BoundsH = 16, Anchor = SpriteAnchor.Center,
	LivePositionEditable = true)]
public sealed class WavePylon : Actor, IPenDrawn
{
	[I(0.5f, 12)] public float Interval = 3;
	[I(0, 12)] public float PhaseOffset;
	[I(0.15f, 2)] public float ChargeDuration = 0.75f;
	[I(80, 360), InspectorGroup("Wave")] public float WaveSpeed = 100;
	[I(64, 640)] public float WaveRange = 320;
	[I(1, 8)] public float WaveThickness = 5.5f;
	[I(0, 0.4f)] public float WaveAccelerationTime = 0.12f;
	[I(0.1f, 1)] public float WaveInitialSpeed = 0.5f;
	[I(8, 320), InspectorGroup("Solid Wave")] public float SolidProbeRange = 96;
	[I(0, 12)] public float SolidInset = 3;
	[I(0, 12)] public float SolidTargetReach = 4;
	public Color AirColor = 0xff8fcf;
	public Color SolidColor = 0xd9903d;
	public string ChargeSoundEvent = "wave_pylon/charge";
	public string ReleaseSoundEvent = "wave_pylon/release";

	const string AirChargeEffect = "wave_pylon_air_charge";
	const string SolidChargeEffect = "wave_pylon_solid_charge";
	const string ChargeParameter = "Charge";
	const string MediumParameter = "Medium";

	static readonly Vector2[] SurfaceDirections =
	[
		Vector2.UnitY,
		-Vector2.UnitY,
		Vector2.UnitX,
		-Vector2.UnitX,
	];

	float cycleTime;
	float particleTimer;
	bool charging;
	int chargeEffect = -1;
	RadialWaveMedium medium;
	SurfaceHit? solidSurface;
	SoundInstance? sound;

	static float ReleaseShakeTime => 0.145f;
	static float ParticleRadius => 19;

	public WavePylon()
	{
		Sprite = null;
		Mask = Masks.Hazard;
		CollidesWithSolids = false;
		Depth = -6;
	}

	public override void Added()
	{
		base.Added();
		var interval = CycleInterval;
		cycleTime = PhaseOffset % interval;
		if (cycleTime < 0)
			cycleTime += interval;
		UpdateMedium(true);
	}

	public override void Update()
	{
		base.Update();
		UpdateMedium();
		cycleTime += Time.Delta;
		if (!charging && cycleTime >= ChargeStart)
			BeginCharge();
		if (charging)
		{
			UpdateChargeParticles();
			UpdateChargeSound();
		}
		
		if (ChargeProgress > easedChargeProgress)
			easedChargeProgress = ChargeProgress;
		else
			easedChargeProgress = Calc.Approach(easedChargeProgress, ChargeProgress, Time.Delta * 0.6f);
		if (cycleTime < CycleInterval)
			return;

		ReleaseWave();
		cycleTime %= CycleInterval;
		charging = false;
		solidSurface = null;
		if (cycleTime >= ChargeStart)
			BeginCharge();
	}

	void BeginCharge()
	{
		charging = true;
		particleTimer = 0;
		if (medium == RadialWaveMedium.Solid)
			TryFindSolidSurface(out solidSurface);
		StopAndReleaseSound(ref sound, true);
		sound = Game.CreateAudioEvent(ChargeSoundEvent, Position);
		UpdateChargeSound();
		sound?.Start();
	}

	void ReleaseWave()
	{
		var origin = (Vector2)Position;
		if (medium == RadialWaveMedium.Solid)
		{
			if (!TryFindSolidSurface(out solidSurface) || solidSurface is not { } surface)
			{
				StopAndReleaseSound(ref sound);
				return;
			}
			origin = surface.Point - surface.Normal * SolidInset;
		}

		if (Game.GetFirst<Player>() is { } player)
		{
			var wave = Game.Create<RadialWave>(origin.RoundToPoint2());
			wave.Source = this;
			wave.Target = player;
			wave.Medium = medium;
			wave.CanHit = static (wave, target) => wave.Medium == RadialWaveMedium.Solid
				? target is Player p && (p.State == Player.States.Burrow || p.Grounded())
				: target is not Player { State: Player.States.Burrow };
			wave.Speed = WaveSpeed;
			wave.MaxRadius = WaveRange;
			wave.Thickness = WaveThickness;
			wave.AccelerationTime = WaveAccelerationTime;
			wave.InitialSpeedMultiplier = WaveInitialSpeed;
			wave.TargetReach = medium == RadialWaveMedium.Solid ? SolidTargetReach : 0;
			wave.Color = WaveColor;
			wave.CoreColor = Color.Lerp(WaveColor, Color.White, 0.55f);
			var inverseColor = medium == RadialWaveMedium.Solid ? AirColor : SolidColor;
			wave.SetRoomInverseProfile(inverseColor, Color.Lerp(inverseColor, Color.White, 0.55f),
				medium == RadialWaveMedium.Solid ? 0 : SolidTargetReach);
			wave.ImpactSoundEvent = "";
		}

		Game.ShakeRenderTarget(ReleaseShakeTime, 9.28f);
		Game.SetAudioEventParameter(sound, ChargeParameter, 1);
		StopAndReleaseSound(ref sound);
		Game.PlayAudioEvent(ReleaseSoundEvent, MediumParameter, (float)medium, Position);
	}

	void UpdateMedium(bool force = false)
	{
		var next = Game.OverlapsRoomCollision(this, new RectInt(Position, Point2.One))
			? RadialWaveMedium.Solid
			: RadialWaveMedium.Air;
		if (!force && medium == next)
			return;

		medium = next;
		ReleaseParticleEffect(ref chargeEffect);
		chargeEffect = AcquireParticleEffect(
			medium == RadialWaveMedium.Solid ? SolidChargeEffect : AirChargeEffect);
		solidSurface = null;
		if (charging && medium == RadialWaveMedium.Solid)
			TryFindSolidSurface(out solidSurface);
	}

	bool TryFindSolidSurface(out SurfaceHit? surface)
	{
		var probe = new RectInt(Position.X - 1, Position.Y - 1, 2, 2);
		var best = default(SurfaceHit);
		var found = false;
		for (var i = 0; i < SurfaceDirections.Length; i++)
			if (Game.TryFindSurface(this, probe, SurfaceDirections[i], SolidProbeRange, out var hit) &&
				IsRealSolid(hit) && (!found || hit.Distance < best.Distance))
			{
				best = hit;
				found = true;
			}
		surface = found ? best : null;
		return found;
	}

	bool IsRealSolid(in SurfaceHit hit)
		=> hit.Actor is { } actor ? actor.Mask.Has(Masks.Solid) :
			!Game.SurfaceIsJumpthru(hit.Point.RoundToPoint2(), -hit.Normal);

	void UpdateChargeParticles()
	{
		if (chargeEffect < 0 || Game.Particles is not { } particles)
			return;
		particleTimer -= Time.Delta;
		var interval = 1 / Calc.Lerp(18, 62, Ease.Cube.In(ChargeProgress));
		for (var emitted = 0; particleTimer <= 0 && emitted < 4; emitted++, particleTimer += interval)
		{
			var direction = Game.rng.Vec2().Normalized(Vector2.UnitX);
			var origin = (Vector2)Position + direction * Calc.Lerp(ParticleRadius, 9, ChargeProgress);
			particles.Emit(chargeEffect, origin, 1, Calc.Angle(-direction),
				Calc.Lerp(0.8f, 1.35f, ChargeProgress), 0.12f);
		}
	}

	void UpdateChargeSound()
	{
		Game.SetAudioEventPosition(sound, Position);
		Game.SetAudioEventParameter(sound, ChargeParameter, ChargeProgress);
		Game.SetAudioEventParameter(sound, MediumParameter, (float)medium);
	}

	float CycleInterval => MathF.Max(ChargeDuration + 0.05f, Interval);
	float ChargeStart => CycleInterval - MathF.Min(ChargeDuration, CycleInterval - 0.05f);
	float easedChargeProgress;
	float EasedChargeProgress => MathF.Max(easedChargeProgress, ChargeProgress);
	float ChargeProgress => charging ? Math.Clamp((cycleTime - ChargeStart) /
		MathF.Max(0.0001f, CycleInterval - ChargeStart), 0, 1) : 0;
	Color WaveColor => medium == RadialWaveMedium.Solid ? SolidColor : AirColor;

	ContentRef<VisualTextureContent> IPenDrawn.PenTexture => PenMaterial.MachinedTexture;
	static float SolidDepth => 0.6f;
	static Color HousingColor => 0x35293e;
	static Color HousingEdgeColor => 0x574661;
	static Color DarkColor => 0x191521;

	void IPenDrawn.Draw(Pen pen)
	{
		var progress = EasedChargeProgress;
		var pulse = 0.82f + MathF.Abs(MathF.Sin(Time.SecondsF * Calc.Lerp(7, 34, progress))) * 0.18f;
		var glow = charging ? Calc.Lerp(0.18f, 1, Ease.Cube.In(progress)) * pulse : 0.12f;
		var color = WaveColor;
		var material = new Vector2(48);

		// Buried in rock, the pylon sinks back; only its reach to the surface stays at the front.
		var body = pen with { Depth = medium == RadialWaveMedium.Solid ? SolidDepth : 0 };
		body.Ball(Vector2.Zero, 8.5f, material, HousingColor, Color.Lerp(HousingEdgeColor, color, progress));
		body.GlowRing(Vector2.Zero, 8.5f, 1.5f, HousingEdgeColor * ((MathF.Sin(Time.SecondsF * CycleInterval * 2) + 1) * 0.2f));

		for (var i = 0; i < 8; i++)
		{
			var direction = Calc.AngleToVector(i * MathF.Tau / 8);
			var lit = charging && progress * 8 >= i;
			body.Groove(direction * 5.5f, direction * 8, lit ? 2 : 1, lit ? color : Color.Lerp(DarkColor, color, 0.2f));
			body.GlowLine(direction * 5.5f, direction * 8, lit ? 2 : 1, color * (lit ? glow : 0.03f));
		}

		// The cycle countdown: studs that light in turn and catch the room's light while dark.
		const int Leds = 12;
		var cycleProgress = cycleTime / CycleInterval;
		for (var i = 0; i < Leds; i++)
		{
			var done = i / (float)Leds;
			var at = Calc.AngleToVector(done * MathF.Tau - MathF.PI * 0.5f) * 5.5f;
			var lit = done <= cycleProgress;
			body.Rivet(at, lit ? Color.Lerp(HousingColor, color, 0.6f) : DarkColor);
			if (lit)
				body.Glow(at, 0.8f, color * 0.5f);
		}

		var coreRadius = Calc.Lerp(2.2f, 4.6f, Ease.Cube.In(progress));
		body.Ball(Vector2.Zero, coreRadius, material, Color.Lerp(HousingColor, color, Calc.Lerp(0.35f, 1, progress)),
			Color.Lerp(DarkColor, color, 0.5f), round: true);
		body.Glow(Vector2.Zero, coreRadius, color * glow);

		if (!charging)
			return;
		var gatheringRadius = Calc.Lerp(19, 9.5f, Ease.Cube.In(progress));
		pen.Ring(Vector2.Zero, gatheringRadius, 1, color * glow * 0.45f);
		pen.GlowRing(Vector2.Zero, gatheringRadius, 2, color * glow * 0.75f);
		if (medium != RadialWaveMedium.Solid || solidSurface is not { } surface)
			return;
		var contact = surface.Point - Position;
		pen.Stroke(Vector2.Zero, contact, 1.5f, color * glow * 0.4f);
		pen.GlowLine(Vector2.Zero, contact, 2.5f, color * glow * 0.65f);
		pen.Dot(contact, 2, color * glow);
		pen.Glow(contact, 3, color * glow);
	}

	public override void ForEachLight(Action<Vector2, float, float, float, Color> action)
	{
		//action(Position, 100, 1.0f, 16, 0xffffff);
		if (charging)
		{
			action(Position, Calc.Lerp(24, 48, ChargeProgress), Calc.Lerp(0.2f, 1.2f, ChargeProgress), 8,
				WaveColor);
		}
	}

	public override void Destroyed()
	{
		ReleaseParticleEffect(ref chargeEffect);
		StopAndReleaseSound(ref sound, true);
	}

	public override Rect GetVisualBounds()
		=> charging && medium == RadialWaveMedium.Solid && solidSurface is { } surface
			? base.GetVisualBounds().Conflate(surface.Point)
			: base.GetVisualBounds().Inflate(20);
}
