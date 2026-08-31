using System.Numerics;
using Foster.Framework;

namespace Myth;

public enum RadialWaveMedium { Air, Solid }

public sealed class RadialWave : Actor
{
	const int SpokeCount = 192;
	const int ImpactBucketCount = 48;
	const int MaxSurfaceClusters = SpokeCount / 2;
	const int MaxSurfaceVoices = 6;
	const string ImpactEffect = "radial_wave_impact";
	const string BirthEffect = "radial_wave_birth";
	const string SolidImpactEffect = "underground_wave_impact";
	const string SolidBirthEffect = "underground_wave_birth";
	const string SurfaceTravelEvent = "radial_wave/surface_travel";
	const string WarmupParameter = "Warmup";
	const string FireDurationParameter = "FireDuration";
	const string SpeedParameter = "Speed";
	const string IntensityParameter = "Intensity";
	const string SurfaceNormalYParameter = "SurfaceNormalY";
	const string VariationParameter = "Variation";

	public Actor? Source;
	public Actor? Target;
	public Func<Actor, bool>? CanHit;
	public float Speed = 220;
	public float MaxRadius = 320;
	public float Thickness = 3.5f;
	public float CollisionInset = 0.5f;
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
	readonly bool[] preEchoBlocked = new bool[SpokeCount];
	readonly float[] preEchoTerminalRadius = new float[SpokeCount];
	readonly float[] preEchoContactRemaining = new float[SpokeCount];
	readonly float[] irregularity = new float[SpokeCount];
	readonly bool[] surfaceWake = new bool[SpokeCount];
	readonly float[] surfaceWakeRadius = new float[SpokeCount];
	readonly Vector2[] surfaceWakeNormal = new Vector2[SpokeCount];
	readonly bool[] impactBuckets = new bool[ImpactBucketCount];
	readonly SurfaceAudioCluster[] surfaceClusters = new SurfaceAudioCluster[MaxSurfaceClusters];
	readonly bool[] surfaceClusterClaimed = new bool[MaxSurfaceClusters];
	readonly SurfaceAudioVoice[] surfaceVoices = new SurfaceAudioVoice[MaxSurfaceVoices];
	float radius;
	float preEchoRadius;
	float preEchoFade;
	float travelTime;
	bool hitTarget;
	bool birthEmitted;
	int activeSpokes = SpokeCount;
	int preEchoActiveSpokes = SpokeCount;
	int impactEffect = -1;
	int birthEffect = -1;
	int impactsThisFrame;
	float impactRumble;
	float surfaceWakeParticleTimer;
	float currentSpeed;
	int surfaceWakeParticleCursor;
	SoundInstance? impactSound;

	struct SurfaceAudioCluster
	{
		public Vector2 Position;
		public Vector2 Normal;
	}

	struct SurfaceAudioVoice
	{
		public SoundInstance? Sound;
		public Vector2 Position;
		public Vector2 Normal;
		public float MissingTime;
		public float Variation;
		public bool Active;
		public bool Matched;
	}

	static float CollisionStep => 1;
	static float ContactHoldTime => 0.16f;
	static float Irregularity => 1.6f;
	static float TrailDistance => 7;
	static float HurtLineEndFadeDistance => 18;
	static float DecorativeEndFadeDistance => 58;
	static float PreEchoEndFadeDistance => 58;
	static float SurfaceWakeParticleInterval => 0.015f;
	static float NearEchoDistance => 5;
	static float FarEchoDistance => 11;
	static float SurfaceVoiceGraceTime => 0.1f;
	static float PreEchoSpeedMultiplier => 2.75f;
	static float PreEchoContactHoldTime => 0.09f;
	static float PreEchoFadeTime => 0.11f;
	static float PreEchoBandOffset => 3.5f;
	static float PreEchoBandThickness => 5;
	static float PreEchoDistortionStrength => 0.52f;

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
			preEchoFade = 1;
			impactEffect = Game.Particles?.AcquireEffect(Medium == RadialWaveMedium.Solid ? SolidImpactEffect : ImpactEffect) ?? -1;
			birthEffect = Game.Particles?.AcquireEffect(Medium == RadialWaveMedium.Solid ? SolidBirthEffect : BirthEffect) ?? -1;
			if (birthEffect >= 0)
				Game.Particles?.Emit(birthEffect, Position, 24);
		}

		impactsThisFrame = 0;
		var advanced = false;
		if (radius < MaxRadius && activeSpokes > 0)
		{
			var previousRadius = radius;
			travelTime += delta;
			var acceleration = AccelerationTime <= 0 ? 1 :
				Ease.Cube.Out(Math.Clamp(travelTime / AccelerationTime, 0, 1));
			currentSpeed = Speed * float.Lerp(InitialSpeedMultiplier, 1, acceleration);
			radius = MathF.Min(radius + currentSpeed * delta, MaxRadius);
			Advance(previousRadius, radius);
			advanced = true;
			if (Medium == RadialWaveMedium.Solid)
				UpdateSurfaceWakeParticles(delta);
		}
		UpdatePreEcho(delta);
		if (Medium == RadialWaveMedium.Solid)
		{
			if (!advanced)
				ClearSurfaceWake();
			UpdateSurfaceAudio(delta);
		}
		var contactsAlive = UpdateContacts();
		UpdateImpactSound();
		if ((radius < MaxRadius && activeSpokes > 0) || contactsAlive)
			return;
		Finished = true;
		Game.Destroy(this);
	}

	void UpdatePreEcho(float delta)
	{
		if (preEchoFade <= 0)
			return;

		if (preEchoRadius < MaxRadius && preEchoActiveSpokes > 0)
		{
			var previousRadius = preEchoRadius;
			preEchoRadius = MathF.Min(preEchoRadius + currentSpeed * PreEchoSpeedMultiplier * delta, MaxRadius);
			AdvancePreEcho(previousRadius, preEchoRadius);
		}
		if (preEchoRadius >= MaxRadius || preEchoActiveSpokes <= 0)
			preEchoFade = MathF.Max(0, preEchoFade - delta / PreEchoFadeTime);
		for (var i = 0; i < preEchoContactRemaining.Length; i++)
			preEchoContactRemaining[i] = MathF.Max(0, preEchoContactRemaining[i] - delta);
	}

	void AdvancePreEcho(float fromRadius, float toRadius)
	{
		for (var i = 0; i < directions.Length; i++)
		{
			if (preEchoBlocked[i])
				continue;
			var direction = directions[i];
			for (var distance = fromRadius; distance < toRadius;)
			{
				distance = MathF.Min(distance + CollisionStep, toRadius);
				if (!BlocksWave(SamplePoint((Vector2)Position + direction * distance)))
					continue;
				preEchoBlocked[i] = true;
				preEchoTerminalRadius[i] = CollisionRadius(distance);
				preEchoContactRemaining[i] = PreEchoContactHoldTime;
				preEchoActiveSpokes--;
				break;
			}
		}
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
			surfaceWake[i] = false;
			if (blocked[i])
				continue;
			var direction = directions[i];
			for (var distance = fromRadius; distance < toRadius;)
			{
				distance = MathF.Min(distance + CollisionStep, toRadius);
				var point = (Vector2)Position + direction * distance;
				if (BlocksWave(SamplePoint(point)))
				{
					blocked[i] = true;
					terminalRadius[i] = CollisionRadius(distance);
					contactRemaining[i] = ContactHoldTime;
					activeSpokes--;
					impactsThisFrame++;
					var contact = point - direction * CollisionStep;
					var outward = Medium == RadialWaveMedium.Solid ? SolidOutwardNormal(point, direction) : -direction;
					EmitImpact(i, contact, outward);
					break;
				}
				TryHitTarget(SampleRect(point));
			}
			if (!blocked[i] && Medium == RadialWaveMedium.Solid)
			{
				var point = (Vector2)Position + direction * toRadius;
				if (TrySolidOutwardNormal(point, MathF.Max(1, Thickness * 0.5f + 1), out var outward))
				{
					surfaceWake[i] = true;
					surfaceWakeRadius[i] = toRadius;
					surfaceWakeNormal[i] = outward;
				}
			}
		}
	}

	bool BlocksWave(RectInt sample)
		=> Medium == RadialWaveMedium.Solid ? !IsSolid(sample) :
			Room is { } room && !room.WorldBounds.Contains(sample) || IsSolid(sample);

	bool IsSolid(RectInt sample)
		=> Room is { } room && room.WorldBounds.Contains(sample) &&
			(Game.OverlapsRoomCollision(sample) || Game.OverlapsFirst(sample, Masks.Solid) != null);

	float CollisionRadius(float distance)
		=> MathF.Max(0, distance - Thickness * 0.5f - CollisionInset);

	Vector2 SolidOutwardNormal(Vector2 point, Vector2 fallback)
	{
		return TrySolidOutwardNormal(point, MathF.Max(1, Thickness * 0.5f), out var normal)
			? normal
			: fallback;
	}

	bool TrySolidOutwardNormal(Vector2 point, float distance, out Vector2 normal)
	{
		var horizontal = (IsSolid(SamplePoint(point - Vector2.UnitX * distance)) ? 1 : 0) -
			(IsSolid(SamplePoint(point + Vector2.UnitX * distance)) ? 1 : 0);
		var vertical = (IsSolid(SamplePoint(point - Vector2.UnitY * distance)) ? 1 : 0) -
			(IsSolid(SamplePoint(point + Vector2.UnitY * distance)) ? 1 : 0);
		normal = new Vector2(horizontal, vertical);
		if (normal.LengthSquared() <= 0.001f)
			return false;
		normal = Vector2.Normalize(normal);
		return true;
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

	void UpdateSurfaceWakeParticles(float delta)
	{
		if (impactEffect < 0 || Game.Particles is not { } particles)
			return;
		surfaceWakeParticleTimer -= delta;
		if (surfaceWakeParticleTimer > 0)
			return;
		surfaceWakeParticleTimer += SurfaceWakeParticleInterval;
		for (int scanned = 0, emitted = 0; scanned < surfaceWake.Length && emitted < 2; scanned++)
		{
			var spoke = surfaceWakeParticleCursor++;
			if (surfaceWakeParticleCursor >= surfaceWake.Length)
				surfaceWakeParticleCursor = 0;
			if (!surfaceWake[spoke])
				continue;
			var outward = surfaceWakeNormal[spoke];
			var point = (Vector2)Position + directions[spoke] * surfaceWakeRadius[spoke] + outward;
			particles.Emit(impactEffect, point, 1, Calc.Angle(outward), 0.7f, 0.32f);
			emitted++;
		}
	}

	void ClearSurfaceWake()
	{
		for (var i = 0; i < surfaceWake.Length; i++)
			surfaceWake[i] = false;
	}

	void UpdateSurfaceAudio(float delta)
	{
		var clusterCount = BuildSurfaceAudioClusters();
		for (var i = 0; i < surfaceClusterClaimed.Length; i++)
			surfaceClusterClaimed[i] = false;
		for (var i = 0; i < surfaceVoices.Length; i++)
			surfaceVoices[i].Matched = false;

		var matchDistance = MathF.Max(14, currentSpeed * delta * 4 + 4);
		var matchDistanceSquared = matchDistance * matchDistance;
		for (var clusterIndex = 0; clusterIndex < clusterCount; clusterIndex++)
		{
			ref var cluster = ref surfaceClusters[clusterIndex];
			var bestVoice = -1;
			var bestDistance = matchDistanceSquared;
			for (var voiceIndex = 0; voiceIndex < surfaceVoices.Length; voiceIndex++)
			{
				ref var voice = ref surfaceVoices[voiceIndex];
				if (!voice.Active || voice.Matched || Vector2.Dot(voice.Normal, cluster.Normal) < -0.25f)
					continue;
				var distance = Vector2.DistanceSquared(voice.Position, cluster.Position);
				if (distance < bestDistance)
				{
					bestDistance = distance;
					bestVoice = voiceIndex;
				}
			}
			if (bestVoice >= 0)
			{
				MatchSurfaceVoice(ref surfaceVoices[bestVoice], cluster, delta);
				surfaceClusterClaimed[clusterIndex] = true;
			}
		}

		for (var voiceIndex = 0; voiceIndex < surfaceVoices.Length; voiceIndex++)
		{
			ref var voice = ref surfaceVoices[voiceIndex];
			if (voice.Active)
				continue;
			var clusterIndex = ClosestUnclaimedCluster(clusterCount);
			if (clusterIndex < 0)
				break;
			StartSurfaceVoice(ref voice, surfaceClusters[clusterIndex]);
			surfaceClusterClaimed[clusterIndex] = true;
		}

		for (var i = 0; i < surfaceVoices.Length; i++)
		{
			ref var voice = ref surfaceVoices[i];
			if (!voice.Active)
				continue;
			if (!voice.Matched)
			{
				voice.MissingTime += delta;
				if (voice.MissingTime >= SurfaceVoiceGraceTime)
				{
					StopSurfaceVoice(ref voice);
					continue;
				}
			}
			UpdateSurfaceVoice(voice);
		}
	}

	int BuildSurfaceAudioClusters()
	{
		var breakAt = -1;
		for (var i = 0; i < surfaceWake.Length; i++)
			if (!surfaceWake[i])
			{
				breakAt = i;
				break;
			}

		var clusterCount = 0;
		var position = Vector2.Zero;
		var normal = Vector2.Zero;
		var count = 0;
		var steps = breakAt < 0 ? surfaceWake.Length : surfaceWake.Length + 1;
		for (var step = 0; step < steps; step++)
		{
			var spoke = breakAt < 0 ? step : (breakAt + step) % surfaceWake.Length;
			if (spoke >= 0 && surfaceWake[spoke])
			{
				position += (Vector2)Position + directions[spoke] * surfaceWakeRadius[spoke];
				normal += surfaceWakeNormal[spoke];
				count++;
				continue;
			}
			if (count > 0 && clusterCount < surfaceClusters.Length)
			{
				surfaceClusters[clusterCount++] = new()
				{
					Position = position / count,
					Normal = normal.Normalized(Vector2.UnitY),
				};
			}
			position = normal = Vector2.Zero;
			count = 0;
		}
		if (count > 0 && clusterCount < surfaceClusters.Length)
			surfaceClusters[clusterCount++] = new()
			{
				Position = position / count,
				Normal = normal.Normalized(Vector2.UnitY),
			};
		return clusterCount;
	}

	int ClosestUnclaimedCluster(int clusterCount)
	{
		var origin = Target?.Position ?? Position;
		var closest = -1;
		var closestDistance = float.MaxValue;
		for (var i = 0; i < clusterCount; i++)
		{
			if (surfaceClusterClaimed[i])
				continue;
			var distance = Vector2.DistanceSquared(origin, surfaceClusters[i].Position);
			if (distance < closestDistance)
			{
				closest = i;
				closestDistance = distance;
			}
		}
		return closest;
	}

	void MatchSurfaceVoice(ref SurfaceAudioVoice voice, in SurfaceAudioCluster cluster, float delta)
	{
		var blend = 1 - MathF.Exp(-delta * 24);
		voice.Position = Vector2.Lerp(voice.Position, cluster.Position, blend);
		voice.Normal = Vector2.Lerp(voice.Normal, cluster.Normal, blend).Normalized(cluster.Normal);
		voice.MissingTime = 0;
		voice.Matched = true;
	}

	void StartSurfaceVoice(ref SurfaceAudioVoice voice, in SurfaceAudioCluster cluster)
	{
		voice = new()
		{
			Sound = Game.CreateAudioEvent(SurfaceTravelEvent, cluster.Position),
			Position = cluster.Position,
			Normal = cluster.Normal,
			Variation = Game.rng.Float(),
			Active = true,
			Matched = true,
		};
		UpdateSurfaceVoice(voice);
		voice.Sound?.Start();
	}

	void UpdateSurfaceVoice(in SurfaceAudioVoice voice)
	{
		var fade = 1 - Math.Clamp(voice.MissingTime / SurfaceVoiceGraceTime, 0, 1);
		Game.SetAudioEventPosition(voice.Sound, voice.Position);
		Game.SetAudioEventParameter(voice.Sound, SpeedParameter, Math.Clamp(currentSpeed / 360, 0, 1));
		Game.SetAudioEventParameter(voice.Sound, IntensityParameter, SurfaceAudioIntensity * fade);
		Game.SetAudioEventParameter(voice.Sound, SurfaceNormalYParameter, voice.Normal.Y);
		Game.SetAudioEventParameter(voice.Sound, VariationParameter, voice.Variation);
	}

	float SurfaceAudioIntensity => MathF.Min(
		Ease.Cube.Out(Math.Clamp(radius / 18, 0, 1)),
		Ease.Cube.Out(Math.Clamp((MaxRadius - radius) / DecorativeEndFadeDistance, 0, 1)));

	static void StopSurfaceVoice(ref SurfaceAudioVoice voice, bool immediate = false)
	{
		StopAndReleaseSound(ref voice.Sound, immediate);
		voice = default;
	}

	RectInt SampleRect(Vector2 point)
	{
		var size = Math.Max(1, (int)MathF.Ceiling(Thickness));
		return new((int)MathF.Floor(point.X - size * 0.5f),
			(int)MathF.Floor(point.Y - size * 0.5f), size, size);
	}

	public override void Render(Batcher batcher) => RenderWave(batcher, false);
	public override void RenderEmissive(Batcher batcher) => RenderWave(batcher, true);
	public override bool HasWorldDistortion => !Finished && birthEmitted && preEchoRadius > 0 && preEchoFade > 0;

	public override void RenderWorldDistortion(Batcher batcher)
	{
		if (!HasWorldDistortion)
			return;
		for (var i = 0; i < directions.Length; i++)
		{
			var next = (i + 1) % directions.Length;
			var alpha = MathF.Min(PreEchoSpokeAlpha(i), PreEchoSpokeAlpha(next));
			if (alpha <= 0)
				continue;
			var strength = alpha * PreEchoDistortionStrength;
			var innerFrom = directions[i] * MathF.Max(0, PreEchoSpokeRadius(i) - PreEchoBandOffset);
			var innerTo = directions[next] * MathF.Max(0, PreEchoSpokeRadius(next) - PreEchoBandOffset);
			var outerFrom = directions[i] * (PreEchoSpokeRadius(i) + PreEchoBandOffset);
			var outerTo = directions[next] * (PreEchoSpokeRadius(next) + PreEchoBandOffset);
			batcher.Line(innerFrom, innerTo, PreEchoBandThickness,
				EncodeWorldDistortion(-directions[i], strength),
				EncodeWorldDistortion(-directions[next], strength));
			batcher.Line(outerFrom, outerTo, PreEchoBandThickness,
				EncodeWorldDistortion(directions[i], strength),
				EncodeWorldDistortion(directions[next], strength));
		}
	}

	void RenderWave(Batcher batcher, bool emissive)
	{
		if (Finished || radius <= 0)
			return;
		var birth = 1 - Ease.Cube.Out(Math.Clamp(radius / 24, 0, 1));
		var time = Time.SecondsF;
		for (var i = 0; i < directions.Length; i++)
		{
			var next = (i + 1) % directions.Length;
			var decorativeAlpha = MathF.Min(DecorativeSpokeAlpha(i), DecorativeSpokeAlpha(next));
			var hurtLineAlpha = MathF.Min(HurtLineSpokeAlpha(i), HurtLineSpokeAlpha(next));
			if (decorativeAlpha <= 0 && hurtLineAlpha <= 0)
				continue;
			var contact = blocked[i] || blocked[next];
			var interference = Interference(i, time);
			var packet = MathF.Pow(interference, 5);
			var frontFrom = directions[i] * FrontRadius(i);
			var frontTo = directions[next] * FrontRadius(next);
			var shimmer = MathF.Sin(time * 15 + i * 0.73f) * 0.65f;
			var bodyFrom = directions[i] * MathF.Max(0, SpokeRadius(i) + shimmer);
			var bodyTo = directions[next] * MathF.Max(0, SpokeRadius(next) - shimmer * 0.45f);
			var haloThickness = (Medium == RadialWaveMedium.Air ? Thickness + 1.5f : Thickness * 0.65f) +
				birth * 3 + packet * 1.8f;
			if (!emissive)
			{
				batcher.Line(bodyFrom, bodyTo, haloThickness,
					Color * (decorativeAlpha * Calc.Lerp(0.2f, 0.42f, interference)));
				batcher.Line(frontFrom, frontTo, 2.25f + packet * 0.65f,
					CoreColor * hurtLineAlpha);
			}
			else
				batcher.Line(frontFrom, frontTo, 2.1f + packet * 1.1f,
					Color * (hurtLineAlpha * Calc.Lerp(0.85f, 1, packet)));

			if (!contact)
			{
				if (Medium == RadialWaveMedium.Air)
					RenderAirWake(batcher, emissive, i, next, decorativeAlpha, time);
				else
					RenderSolidFracture(batcher, emissive, i, frontFrom, frontTo, decorativeAlpha, time);
			}

			if (packet > 0.72f && (i & 1) == 0)
				batcher.Circle((frontFrom + frontTo) * 0.5f, Calc.Lerp(0.8f, 2.2f, packet), 8,
					(emissive ? Color : CoreColor) * (decorativeAlpha * packet * (emissive ? 0.9f : 0.7f)));
		}
		if (Medium == RadialWaveMedium.Solid)
			RenderSurfaceWake(batcher, emissive);
		if (!emissive)
			RenderPreEchoFringe(batcher);
	}

	void RenderPreEchoFringe(Batcher batcher)
	{
		if (!HasWorldDistortion)
			return;
		for (var i = 0; i < directions.Length; i++)
		{
			var next = (i + 1) % directions.Length;
			var alpha = MathF.Min(PreEchoSpokeAlpha(i), PreEchoSpokeAlpha(next));
			if (alpha <= 0)
				continue;
			batcher.Line(directions[i] * PreEchoSpokeRadius(i), directions[next] * PreEchoSpokeRadius(next),
				1.2f, Color * (alpha * 0.09f));
		}
	}

	void RenderAirWake(Batcher batcher, bool emissive, int spoke, int next, float alpha, float time)
	{
		RenderEcho(batcher, emissive, spoke, next, alpha, time, NearEchoDistance, 0, 0.5f, 3.5f);
		RenderEcho(batcher, emissive, spoke, next, alpha, time, FarEchoDistance, 11, 0.3f, 2.5f);
		var cell = CellStrength(spoke, time, 3);
		if (cell < 0.66f || spoke % 7 != 0 || radius <= TrailDistance)
			return;
		var point = directions[spoke] * MathF.Max(0, SpokeRadius(spoke) - 2);
		var tangent = new Vector2(-directions[spoke].Y, directions[spoke].X);
		var tail = point - directions[spoke] * Calc.Lerp(5, 9, cell) +
			tangent * MathF.Sin(spoke * 2.1f + time * 5) * 2.5f;
		batcher.Line(point, tail, emissive ? 1.2f : 2.4f,
			(emissive ? Color : CoreColor) * (alpha * cell * (emissive ? 0.38f : 0.15f)));
	}

	void RenderEcho(Batcher batcher, bool emissive, int spoke, int next, float alpha, float time,
		float distance, int offset, float opacity, float thickness)
	{
		if (radius <= distance || CellStrength(spoke, time, offset) < 0.48f)
			return;
		var from = directions[spoke] * MathF.Max(0, SpokeRadius(spoke) - distance);
		var to = directions[next] * MathF.Max(0, SpokeRadius(next) - distance);
		batcher.Line(from, to, emissive ? thickness : thickness + 1,
			(emissive ? Color : CoreColor) * (alpha * opacity * (emissive ? 1 : 0.7f)));
	}

	void RenderSolidFracture(Batcher batcher, bool emissive, int spoke, Vector2 frontFrom,
		Vector2 frontTo, float alpha, float time)
	{
		var cell = CellStrength(spoke, time, 7);
		if (cell > 0.4f && radius > NearEchoDistance)
		{
			var from = directions[spoke] * MathF.Max(0, SpokeRadius(spoke) - NearEchoDistance);
			var to = directions[(spoke + 1) % directions.Length] *
				MathF.Max(0, SpokeRadius((spoke + 1) % directions.Length) - NearEchoDistance);
			batcher.Line(from, to, emissive ? 1.2f : 2.2f,
				(emissive ? Color : CoreColor) * (alpha * cell * (emissive ? 0.32f : 0.16f)));
		}
		if (spoke % 6 != 0 || cell < 0.58f)
			return;
		var point = (frontFrom + frontTo) * 0.5f;
		var direction = directions[spoke];
		var tangent = new Vector2(-direction.Y, direction.X) * ((spoke / 6 & 1) == 0 ? 1 : -1);
		var branch = point - direction * Calc.Lerp(4, 8, cell) + tangent * Calc.Lerp(1.5f, 4, cell);
		if (!emissive)
			batcher.Line(point, branch, 3, Color * (alpha * cell * 0.18f));
		batcher.Line(point, branch, emissive ? 1.25f : 1,
			(emissive ? Color : CoreColor) * (alpha * cell * (emissive ? 0.48f : 0.72f)));
	}

	float Interference(int spoke, float time)
	{
		var angle = spoke * MathF.Tau / directions.Length;
		return 0.5f + MathF.Sin(angle * 5 - time * 7.5f) *
			MathF.Sin(angle * 8 + time * 5.2f) * 0.5f;
	}

	static float CellStrength(int spoke, float time, int offset)
	{
		var group = (spoke + offset) / 4;
		var stable = 0.5f + MathF.Sin(group * 2.17f + offset * 0.91f) * 0.5f;
		var traveling = 0.5f + MathF.Sin(group * 0.83f - time * 6.2f + offset) * 0.5f;
		return stable * 0.55f + traveling * 0.45f;
	}

	void RenderSurfaceWake(Batcher batcher, bool emissive)
	{
		var height = MathF.Max(5, TargetReach + 2);
		for (var i = 0; i < surfaceWake.Length; i++)
		{
			if (!surfaceWake[i])
				continue;
			var alpha = DecorativeSpokeAlpha(i);
			var normal = surfaceWakeNormal[i];
			var tangent = new Vector2(-normal.Y, normal.X);
			var pulse = 0.72f + MathF.Abs(MathF.Sin(Time.SecondsF * 31 + i * 1.7f)) * 0.28f;
			var width = 2.2f + pulse * 1.4f;
			var point = directions[i] * surfaceWakeRadius[i];
			var tip = point + normal * height * pulse;
			if (!emissive)
				batcher.Triangle(point - tangent * width, point + tangent * width, tip,
					Color * (alpha * 0.48f));
			batcher.Line(point, tip, emissive ? 1.8f : 1.2f,
				(emissive ? Color : CoreColor) * (alpha * pulse));
		}
	}

	float SpokeRadius(int spoke)
		=> blocked[spoke] ? terminalRadius[spoke] :
			radius + irregularity[spoke] * Irregularity * Ease.Cube.Out(Math.Clamp(radius / 40, 0, 1));

	float FrontRadius(int spoke) => blocked[spoke] ? terminalRadius[spoke] : radius;

	float PreEchoSpokeRadius(int spoke)
		=> preEchoBlocked[spoke] ? preEchoTerminalRadius[spoke] : preEchoRadius;

	float PreEchoSpokeAlpha(int spoke)
		=> preEchoBlocked[spoke]
			? Ease.Cube.Out(preEchoContactRemaining[spoke] / PreEchoContactHoldTime)
			: MathF.Min(preEchoFade,
				Ease.Cube.Out(Math.Clamp((MaxRadius - preEchoRadius) / PreEchoEndFadeDistance, 0, 1)));

	float HurtLineSpokeAlpha(int spoke)
		=> blocked[spoke] ? Ease.Cube.Out(contactRemaining[spoke] / ContactHoldTime) :
			Ease.Cube.Out(Math.Clamp((MaxRadius - radius) / HurtLineEndFadeDistance, 0, 1));

	float DecorativeSpokeAlpha(int spoke)
		=> blocked[spoke] ? Ease.Cube.Out(contactRemaining[spoke] / ContactHoldTime) :
			Ease.Cube.Out(Math.Clamp((MaxRadius - radius) / DecorativeEndFadeDistance, 0, 1));

	public override void Destroyed()
	{
		Finished = true;
		for (var i = 0; i < surfaceVoices.Length; i++)
			StopSurfaceVoice(ref surfaceVoices[i], true);
		Game.Particles?.ReleaseEffect(ref impactEffect);
		Game.Particles?.ReleaseEffect(ref birthEffect);
		StopAndReleaseSound(ref impactSound, true);
	}

	public override void RenderNormals(Batcher batcher) { }
	public override void RenderShadow(Batcher batcher, Color color) { }
	public override void RenderReflection(Batcher batcher, Color color) { }
	public override Rect GetVisualBounds()
	{
		var visualRadius = MathF.Max(radius, preEchoRadius);
		return new Rect((Vector2)Position - Vector2.One * visualRadius, Vector2.One * visualRadius * 2)
			.Inflate(MathF.Max(5, TargetReach + 2));
	}
}
