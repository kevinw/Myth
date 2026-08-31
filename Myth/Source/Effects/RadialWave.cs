using System.Numerics;
using Foster.Framework;

namespace Myth;

public enum RadialWaveMedium { Air, Solid }

public sealed class RadialWave : Actor
{
	const int SpokeCount = 192;
	const int ImpactBucketCount = 48;
	const string ImpactEffect = "radial_wave_impact";
	const string BirthEffect = "radial_wave_birth";
	const string SolidImpactEffect = "underground_wave_impact";
	const string SolidBirthEffect = "underground_wave_birth";
	const string WarmupParameter = "Warmup";
	const string FireDurationParameter = "FireDuration";

	public Actor? Source;
	public Actor? Target;
	public Func<Actor, bool>? CanHit;
	public float Speed = 220;
	public float MaxRadius = 320;
	public float Thickness = 3.5f;
	public float StartDelay;
	public float AccelerationTime = 0.16f;
	public float InitialSpeedMultiplier = 0.42f;
	public float TargetReach;
	public RadialWaveMedium Medium;
	public Color Color = new(0xff8fcf);
	public Color CoreColor = new(0xfff4fb);
	public string ImpactSoundEvent = "lightning_trap";
	public bool Finished { get; private set; }

	readonly Vector2[] directions = new Vector2[SpokeCount];
	readonly bool[] blocked = new bool[SpokeCount];
	readonly float[] terminalRadius = new float[SpokeCount];
	readonly float[] contactRemaining = new float[SpokeCount];
	readonly float[] irregularity = new float[SpokeCount];
	readonly bool[] impactBuckets = new bool[ImpactBucketCount];
	float radius;
	float travelTime;
	bool hitTarget;
	bool birthEmitted;
	int activeSpokes = SpokeCount;
	int impactEffect = -1;
	int birthEffect = -1;
	int impactsThisFrame;
	float impactRumble;
	SoundInstance? impactSound;

	static float CollisionStep => 2;
	static float ContactHoldTime => 0.16f;
	static float Irregularity => 1.6f;
	static float TrailDistance => 7;
	static float EndFadeDistance => 58;

	public RadialWave()
	{
		Sprite = null;
		Mask = Masks.None;
		CollidesWithSolids = false;
		Depth = -100;
	}

	public override void Added()
	{
		base.Added();
		var phase = Game.rng.Angle();
		var noisePhase = Game.rng.Angle();
		for (var i = 0; i < directions.Length; i++)
		{
			var angle = phase + i * MathF.Tau / directions.Length;
			directions[i] = Calc.AngleToVector(angle);
			irregularity[i] = (MathF.Sin(angle * 3 + noisePhase) +
				MathF.Sin(angle * 7 - noisePhase * 0.7f) * 0.35f) / 1.35f;
		}
	}

	public override void Update()
	{
		base.Update();
		if (Finished)
			return;

		var delta = Time.Delta;
		if (StartDelay > 0)
		{
			var held = MathF.Min(StartDelay, delta);
			StartDelay -= held;
			delta -= held;
			if (delta <= 0)
				return;
		}
		if (!birthEmitted)
		{
			birthEmitted = true;
			impactEffect = Game.Particles?.AcquireEffect(Medium == RadialWaveMedium.Solid ? SolidImpactEffect : ImpactEffect) ?? -1;
			birthEffect = Game.Particles?.AcquireEffect(Medium == RadialWaveMedium.Solid ? SolidBirthEffect : BirthEffect) ?? -1;
			if (birthEffect >= 0)
				Game.Particles?.Emit(birthEffect, Position, 24);
		}

		impactsThisFrame = 0;
		if (radius < MaxRadius && activeSpokes > 0)
		{
			var previousRadius = radius;
			travelTime += delta;
			var acceleration = AccelerationTime <= 0 ? 1 :
				Ease.Cube.Out(Math.Clamp(travelTime / AccelerationTime, 0, 1));
			var speed = Speed * float.Lerp(InitialSpeedMultiplier, 1, acceleration);
			radius = MathF.Min(radius + speed * delta, MaxRadius);
			Advance(previousRadius, radius);
		}
		var contactsAlive = UpdateContacts();
		UpdateImpactSound();
		if ((radius < MaxRadius && activeSpokes > 0) || contactsAlive)
			return;
		Finished = true;
		Game.Destroy(this);
	}

	bool UpdateContacts()
	{
		var alive = false;
		for (var i = 0; i < contactRemaining.Length; i++)
		{
			contactRemaining[i] = MathF.Max(0, contactRemaining[i] - Time.Delta);
			alive |= contactRemaining[i] > 0;
		}
		return alive || impactRumble > 0;
	}

	void UpdateImpactSound()
	{
		impactRumble = MathF.Max(impactRumble - Time.Delta * 5,
			Math.Clamp(impactsThisFrame / 12f, 0, 1));
		if (impactsThisFrame > 0 && impactSound == null && !string.IsNullOrWhiteSpace(ImpactSoundEvent))
		{
			impactSound = Game.CreateAudioEvent(ImpactSoundEvent, Position);
			Game.SetAudioEventParameter(impactSound, WarmupParameter, 1);
			impactSound?.Start();
		}
		Game.SetAudioEventPosition(impactSound, Position);
		Game.SetAudioEventParameter(impactSound, FireDurationParameter, impactRumble);
		if (impactSound != null && impactRumble <= 0 && impactsThisFrame == 0)
			StopAndReleaseSound(ref impactSound);
	}

	void Advance(float fromRadius, float toRadius)
	{
		for (var i = 0; i < directions.Length; i++)
		{
			if (blocked[i])
				continue;
			var direction = directions[i];
			for (var distance = fromRadius; distance < toRadius;)
			{
				distance = MathF.Min(distance + CollisionStep, toRadius);
				var point = (Vector2)Position + direction * distance;
				var sample = SampleRect(point);
				if (BlocksWave(sample))
				{
					blocked[i] = true;
					terminalRadius[i] = MathF.Max(0, distance - CollisionStep);
					contactRemaining[i] = ContactHoldTime;
					activeSpokes--;
					impactsThisFrame++;
					var contact = point - direction * CollisionStep;
					var outward = Medium == RadialWaveMedium.Solid ? SolidOutwardNormal(point, direction) : -direction;
					EmitImpact(i, contact, outward);
					break;
				}
				TryHitTarget(sample);
			}
		}
	}

	bool BlocksWave(RectInt sample)
		=> Medium == RadialWaveMedium.Solid ? !IsSolid(sample) :
			Room is { } room && !room.WorldBounds.Contains(sample) || IsSolid(sample);

	bool IsSolid(RectInt sample)
		=> Room is { } room && room.WorldBounds.Contains(sample) &&
			(Game.OverlapsRoomCollision(sample) || Game.OverlapsFirst(sample, Masks.Solid) != null);

	Vector2 SolidOutwardNormal(Vector2 point, Vector2 fallback)
	{
		var distance = MathF.Max(1, Thickness * 0.5f);
		var horizontal = (IsSolid(SamplePoint(point - Vector2.UnitX * distance)) ? 1 : 0) -
			(IsSolid(SamplePoint(point + Vector2.UnitX * distance)) ? 1 : 0);
		var vertical = (IsSolid(SamplePoint(point - Vector2.UnitY * distance)) ? 1 : 0) -
			(IsSolid(SamplePoint(point + Vector2.UnitY * distance)) ? 1 : 0);
		return new Vector2(horizontal, vertical).Normalized(fallback);
	}

	static RectInt SamplePoint(Vector2 point)
		=> new((int)MathF.Floor(point.X), (int)MathF.Floor(point.Y), 1, 1);

	void TryHitTarget(RectInt sample)
	{
		var targetSample = TargetReach <= 0 ? sample : sample.Inflate((int)MathF.Ceiling(TargetReach));
		if (hitTarget || Source == null || Target == null || Game.destroying.Contains(Source) ||
			Game.destroying.Contains(Target) || !Target.OverlapsCombatBounds(targetSample) || CanHit?.Invoke(Target) == false)
			return;
		hitTarget = Source.Hit(Target);
	}

	void EmitImpact(int spoke, Vector2 point, Vector2 outward)
	{
		if (impactEffect < 0)
			return;
		var bucket = spoke * impactBuckets.Length / directions.Length;
		if (impactBuckets[bucket])
			return;
		impactBuckets[bucket] = true;
		Game.Particles?.Emit(impactEffect, point, 3, Calc.Angle(outward), 1, 0.45f);
	}

	RectInt SampleRect(Vector2 point)
	{
		var size = Math.Max(1, (int)MathF.Ceiling(Thickness));
		return new((int)MathF.Floor(point.X - size * 0.5f),
			(int)MathF.Floor(point.Y - size * 0.5f), size, size);
	}

	public override void Render(Batcher batcher) => RenderWave(batcher, false);
	public override void RenderEmissive(Batcher batcher) => RenderWave(batcher, true);

	void RenderWave(Batcher batcher, bool emissive)
	{
		if (Finished || radius <= 0)
			return;
		var birth = 1 - Ease.Cube.Out(Math.Clamp(radius / 24, 0, 1));
		var thickness = Thickness + birth * 4;
		for (var i = 0; i < directions.Length; i++)
		{
			var next = (i + 1) % directions.Length;
			var alpha = MathF.Min(SpokeAlpha(i), SpokeAlpha(next));
			if (alpha <= 0)
				continue;
			var contact = blocked[i] || blocked[next];
			var from = directions[i] * SpokeRadius(i);
			var to = directions[next] * SpokeRadius(next);
			var contactPulse = contact ? MathF.Sin(alpha * MathF.PI) : 0;
			var lineThickness = thickness + contactPulse * 2.5f;
			if (!emissive)
				batcher.Line(from, to, lineThickness + 2.5f, Color * (alpha * 0.32f));
			batcher.Line(from, to, emissive ? lineThickness + 1 : lineThickness,
				(emissive ? Color : CoreColor) * alpha);
			if (!contact && radius > TrailDistance)
			{
				var trailFrom = directions[i] * (SpokeRadius(i) - TrailDistance);
				var trailTo = directions[next] * (SpokeRadius(next) - TrailDistance);
				batcher.Line(trailFrom, trailTo, MathF.Max(1, Thickness * 0.55f),
					(emissive ? Color : CoreColor) * (alpha * 0.22f));
			}
		}
	}

	float SpokeRadius(int spoke)
		=> blocked[spoke] ? terminalRadius[spoke] :
			radius + irregularity[spoke] * Irregularity * Ease.Cube.Out(Math.Clamp(radius / 40, 0, 1));

	float SpokeAlpha(int spoke)
		=> blocked[spoke] ? Ease.Cube.Out(contactRemaining[spoke] / ContactHoldTime) :
			Ease.Cube.Out(Math.Clamp((MaxRadius - radius) / EndFadeDistance, 0, 1));

	public override void Destroyed()
	{
		Finished = true;
		Game.Particles?.ReleaseEffect(ref impactEffect);
		Game.Particles?.ReleaseEffect(ref birthEffect);
		StopAndReleaseSound(ref impactSound, true);
	}

	public override void RenderNormals(Batcher batcher) { }
	public override void RenderShadow(Batcher batcher, Color color) { }
	public override void RenderReflection(Batcher batcher, Color color) { }
	public override Rect GetVisualBounds() => new(Position.X - radius, Position.Y - radius, radius * 2, radius * 2);
}
