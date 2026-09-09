using System.Numerics;
using Foster.Framework;
#if EDITOR
using LinkDotNet.StringBuilder;
using ImGuiNET;
#endif

namespace Myth;

[GameActor(Sprite = "saw", Tags = ActorTag.Enemy, LivePositionEditable = true)]
public class MagneticCrawler : Actor, INeuralTextureAgent
{
	enum State
	{
		Dormant,
		Assembling,
		Patrol,
		Notice,
		Hunting,
		Dying
	}

	struct Part
	{
		public Vector2 Position;
		public Vector2 Velocity;
		public float Angle;
		public float AngularVelocity;
		public float Phase;
		public float Detached;
	}

	struct Leg
	{
		public Vector2 Foot;
		public Vector2 From;
		public Vector2 To;
		public float Step;
		public float Delay;
		public float Lift;
		public float Contact;
	}

	public Point2 InitialSurfaceDirection = Point2.Down;
	public ContentRef<SurfaceContent> SurfaceId = new("");
	public bool ClimbsWallsAndCeilings = true;
	public bool CanWalkOnMovingPlatforms;
	[I(1, 12)] public int Health = 4;
	[I(0.5f, 2)] public float Scale = 1;
	[I(112, 180)] public float WakeRange = 136;
	[I(48, 112)] public float NoticeRange = 104;
	[I(5, 32)] public float PatrolSpeed = 15;
	[I(32, 100)] public float HuntSpeed = 48;
	public Color MetalColor = new(0x858d9a);
	public Color MetalEdgeColor = new(0x303743);
	public Color EyeColor = new(0xb20f24);
	public Color EyeGlowColor = new(0xff1838);
	[I(4, 64)] public float EyeLightRadius = 28;
	[I(0, 2)] public float EyeLightIntensity = 0.75f;
	public RigSkinMode SkinMode;
	[ActorSettings(typeof(NeuralSurfaceSettings), "Neural Surface", "Neural")]
	public readonly NeuralSurfaceSettings Neural = new()
	{
		NeuralWeightsFile = NeuralWeightsContent.MagneticCrawlerDefault,
		NeuralColorAmount = 0.42f,
		NeuralNormalAmount = 0.5f,
		NeuralFireRate = 0.55f,
		NeuralContrast = 1.2f,
		NeuralSaturation = 1.1f,
		NeuralBrightness = 0.08f,
	};
	[ActorSettings(typeof(NeuralReactionSettings), "Neural Reactions", "Neural")]
	public readonly NeuralReactionSettings NeuralReactions = new()
	{
		NeuralHitStrength = 0.65f,
		NeuralJostleInterval = 0.38f,
		NeuralJostleStrength = 0.2f,
		NeuralJostleRadius = 4,
	};
	[I(0, 1), InspectorGroup("Neural")] public float NeuralFootContactStrength = 0.32f;
	[I(1, 12)] public float NeuralFootContactRadius = 3;

	static float AssemblyDuration => 1.65f;
	static float NoticeDuration => 0.82f;
	static float HuntWindup => 0.42f;
	static float HuntBurst => 0.58f;
	static float HuntRecover => 0.34f;
	static float BodyHeight => 9f;
	static float BodySpring => 90f;
	static float BodyDamping => 12f;
	static float PartSpring => 115f;
	static float PartDamping => 14f;
	static float StepReach => 7f;
	static float StepSpeed => 7f;
	static float StepLift => 5f;
	static float NeuralFootContactDuration => 0.16f;
	static Point2 NeuralSize => new(48, 40);
	static Point2 NeuralOrigin => new(-24, -26);
	const int PlateVertexCount = 8;

	readonly SurfaceFollower surface;
	readonly Part[] parts = new Part[6];
	readonly Leg[] legs = new Leg[4];
	Rng rng = new();
	State state;
	float stateTimer;
	float stuckTimer;
	float eyeHitFlash;
	float walkSign;
	float bodyAngle;
	Vector2 bodyPosition;
	Vector2 bodyVelocity;
	bool attached;
	bool huntBurstStarted;
	int latchedParts;
	int sparkEffect = -1;
	uint visualSeed;
	float neuralHitRemaining;
	float nextNeuralJostle;
	NeuralSkinRuntime? neuralSkin;

	public override bool CastsSpriteShadow => state != State.Dormant;
	public override bool CastsReflection => state != State.Dormant;
	public override bool LightingDithered => false;
	public override string HitSoundType => "metal";

	public MagneticCrawler()
	{
		Sprite = null;
		Depth = -5;
		Facing = Signs.Positive;
		surface = new(this);
	}

	public override void Added()
	{
		visualSeed = VisualSeed();
		var half = Math.Max(4, (int)MathF.Round(6 * Scale));
		Hitbox = new(new RectInt(-half, -half, half * 2, half * 2));
		surface.ProbeRect = Hitbox.Rect;
		surface.ClimbsWallsAndCeilings = ClimbsWallsAndCeilings;
		surface.CanWalkOnMovingPlatforms = CanWalkOnMovingPlatforms;
		surface.TowardSurface = InitialSurfaceDirection == Point2.Zero ? Vector2.UnitY : InitialSurfaceDirection.Normalized();
		attached = AttachToSurface();
		bodyAngle = Calc.Angle(surface.Tangent);
		bodyPosition = DormantBodyPosition();
		InitializeParts();
		PlantAllFeet();
		sparkEffect = AcquireParticleEffect("sparks");
		SetState(State.Dormant);
		SyncNeuralSkin();
	}

	public override void Destroyed()
	{
		ReleaseParticleEffect(ref sparkEffect);
		neuralSkin?.Dispose();
	}

	public override void Update()
	{
		surface.CanWalkOnMovingPlatforms = CanWalkOnMovingPlatforms;
		SyncNeuralSkin();
		neuralHitRemaining = MathF.Max(0, neuralHitRemaining - Time.Delta);
		for (var i = 0; i < legs.Length; i++)
			legs[i].Contact = MathF.Max(0, legs[i].Contact - Time.Delta);
		Velocity = Vector2.Zero;
		base.Update();
		eyeHitFlash = Calc.Approach(eyeHitFlash, 0, Time.Delta * 4);
		stateTimer += Time.Delta;

		if (state == State.Dying)
		{
			UpdateDying();
			return;
		}

		var player = Game.GetFirst<Player>();
		switch (state)
		{
			case State.Dormant:
				UpdateDormant(player);
				break;
			case State.Assembling:
				UpdateAssembly();
				break;
			case State.Patrol:
				UpdatePatrol(player);
				break;
			case State.Notice:
				UpdateNotice(player);
				break;
			case State.Hunting:
				UpdateHunt(player);
				break;
		}

		if (state is State.Patrol or State.Notice or State.Hunting)
		{
			UpdateLegs();
			UpdateBody();
			UpdateParts();
			UpdateHitbox();
		}
	}

	void UpdateDormant(Player? player)
	{
		Mask = Masks.None;
		if (!attached)
			attached = AttachToSurface();
		bodyPosition = DormantBodyPosition();
		if (player == null || !attached || Vector2.DistanceSquared(player.Position, Position) > WakeRange * WakeRange)
			return;
		SetState(State.Assembling);
	}

	void UpdateAssembly()
	{
		var progress = Math.Clamp(stateTimer / AssemblyDuration, 0, 1);
		var eased = Ease.Cube.InOut(progress);
		bodyPosition = Vector2.Lerp(DormantBodyPosition(), ActiveBodyTarget(), eased);
		bodyAngle = Calc.Angle(surface.Tangent);
		for (var i = 0; i < parts.Length; i++)
		{
			ref var part = ref parts[i];
			var t = Math.Clamp((progress - part.Phase) / MathF.Max(0.01f, 1 - part.Phase), 0, 1);
			var target = PartTarget(i);
			var side = (i & 1) == 0 ? -1 : 1;
			part.Position = Vector2.Lerp(SourcePosition(i), target, Ease.Back.Out(t))
				- surface.TowardSurface * MathF.Sin(t * MathF.PI) * (5 + i % 3 * 2) * Scale
				+ surface.Tangent * MathF.Sin(t * MathF.PI) * side * 3 * Scale;
			part.Angle = float.Lerp(SourceAngle(i), PartTargetAngle(i), Ease.Cube.InOut(t));
			if (t >= 0.82f && (latchedParts & (1 << i)) == 0)
			{
				latchedParts |= 1 << i;
				bodyVelocity -= surface.TowardSurface * 9;
				EmitSparks(part.Position, 5);
			}
		}

		if (progress >= 0.72f)
			Mask = Masks.Enemy;
		if (progress < 1)
			return;

		PlantAllFeet();
		SetState(State.Patrol);
	}

	void UpdatePatrol(Player? player)
	{
		if (player != null && Vector2.DistanceSquared(player.Position, bodyPosition) <= NoticeRange * NoticeRange)
		{
			SetState(State.Notice);
			return;
		}
		MoveAlongSurface((int)Facing, PatrolSpeed);
	}

	void UpdateNotice(Player? player)
	{
		walkSign = 0;
		if (player != null && stateTimer > NoticeDuration * 0.38f)
			FacePlayer(player);
		if (stateTimer >= NoticeDuration)
			SetState(State.Hunting);
	}

	void UpdateHunt(Player? player)
	{
		var phase = stateTimer % HuntCycle;
		if (phase < HuntWindup)
		{
			walkSign = 0;
			huntBurstStarted = false;
			if (player != null)
				FacePlayer(player);
		}
		else if (phase < HuntWindup + HuntBurst)
		{
			if (!huntBurstStarted)
			{
				huntBurstStarted = true;
				bodyVelocity -= surface.TowardSurface * 35;
				EmitSparks(Position, 8);
			}
			MoveAlongSurface((int)Facing, HuntSpeed);
		}
		else
			walkSign = 0;
	}

	float HuntCycle => HuntWindup + HuntBurst + HuntRecover;

	void SyncNeuralSkin()
	{
		if (SkinMode != RigSkinMode.NCA)
		{
			neuralSkin?.Dispose();
			neuralSkin = null;
			return;
		}

		if (neuralSkin == null)
		{
			neuralSkin = new(this, Neural, NeuralSize, NeuralOrigin, 43);
			neuralSkin.Initialize(SkinMode);
			nextNeuralJostle = Time.SecondsF + NeuralReactions.NeuralJostleInterval * 0.5f;
		}
		else
			neuralSkin.Update(SkinMode);
	}

	uint VisualSeed()
	{
		var hash = 2166136261u;
		if (!string.IsNullOrEmpty(ActorId))
			for (var i = 0; i < ActorId.Length; i++)
			hash = unchecked((hash ^ ActorId[i]) * 16777619u);
		else
		{
			hash = unchecked((hash ^ (uint)Position.X) * 16777619u);
			hash = unchecked((hash ^ (uint)Position.Y) * 16777619u);
		}
		return Hash32.Mix(hash);
	}

	void MoveAlongSurface(int direction, float speed)
	{
		walkSign = 0;
		var stepDirection = surface.Tangent * direction;
		var lookahead = (stepDirection * MathF.Max(1, speed * Time.Delta + 1)).RoundToPoint2();
		if (lookahead == Point2.Zero)
			lookahead = new Point2(Math.Sign(stepDirection.X), Math.Sign(stepDirection.Y));
		if (Game.OverlapsEnemyRoomBoundary(this, lookahead))
		{
			Facing = -Facing;
			stuckTimer = 0;
			return;
		}

		var before = Position;
		surface.Follow(direction, speed * Time.Delta);
		if (Position != before)
		{
			walkSign = direction;
			stuckTimer = 0;
			return;
		}

		stuckTimer += Time.Delta;
		if (stuckTimer > 0.2f)
		{
			if (!surface.Attach(surface.CornerProbeDistance, surface.MaxSurfaceSnapDistance) && AttachToSurface())
				PlantAllFeet();
			Facing = -Facing;
			stuckTimer = 0;
		}
	}

	bool AttachToSurface() => surface.Attach((int)MathF.Ceiling(18 * Scale));

	void FacePlayer(Player player)
	{
		var along = Vector2.Dot((Vector2)player.Position - bodyPosition, surface.Tangent);
		if (MathF.Abs(along) > 2)
			Facing = along < 0 ? Signs.Negative : Signs.Positive;
	}

	void SetState(State next)
	{
		state = next;
		stateTimer = 0;
		if (next == State.Assembling)
		{
			latchedParts = 0;
			EmitSparks(Position, 10);
		}
		if (next == State.Notice)
		{
			bodyVelocity += surface.TowardSurface * 28;
			EmitSparks(Position, 4);
		}
		if (next == State.Hunting)
			huntBurstStarted = false;
	}

	void InitializeParts()
	{
		for (var i = 0; i < parts.Length; i++)
		{
			parts[i].Position = SourcePosition(i);
			parts[i].Angle = SourceAngle(i);
			parts[i].Phase = i switch { 4 => 0.02f, 5 => 0.14f, _ => 0.22f + i * 0.085f };
		}
	}

	Vector2 DormantBodyPosition() => (Vector2)Position + surface.TowardSurface * 3 * Scale;

	Vector2 ActiveBodyTarget()
	{
		var crouch = CrouchAmount();
		var stride = state == State.Hunting && HuntPhase() is > 0 and < 1
			? surface.Tangent * (int)Facing * 2.5f * Scale
			: Vector2.Zero;
		var drift = Vector2.Zero;
		for (var i = 0; i < legs.Length; i++)
			drift += legs[i].Foot - DesiredFoot(i);
		return (Vector2)Position - surface.TowardSurface * (BodyHeight - crouch * 4) * Scale + stride + drift * 0.12f;
	}

	float CrouchAmount()
	{
		if (state == State.Notice)
			return MathF.Sin(Math.Clamp(stateTimer / NoticeDuration, 0, 1) * MathF.PI);
		if (state != State.Hunting)
			return 0;
		var phase = stateTimer % HuntCycle;
		return phase < HuntWindup ? Ease.Cube.InOut(phase / HuntWindup) : 0;
	}

	float HuntPhase()
	{
		if (state != State.Hunting)
			return -1;
		var phase = stateTimer % HuntCycle;
		return phase < HuntWindup ? 0 : phase < HuntWindup + HuntBurst ? 0.5f : 1;
	}

	void UpdateBody()
	{
		var dt = MathF.Min(Time.Delta, 0.05f);
		Spring.Step(ref bodyPosition, ref bodyVelocity, ActiveBodyTarget(), BodySpring, BodyDamping, dt);
		bodyAngle = Calc.AngleApproach(bodyAngle, Calc.Angle(surface.Tangent), 7 * dt);
	}

	void UpdateParts()
	{
		var dt = MathF.Min(Time.Delta, 0.05f);
		for (var i = 0; i < parts.Length; i++)
		{
			ref var part = ref parts[i];
			part.Detached = Calc.Approach(part.Detached, 0, dt);
			var spring = part.Detached > 0 ? 13 : PartSpring;
			var damping = part.Detached > 0 ? 3.5f : PartDamping;
			Spring.Step(ref part.Position, ref part.Velocity, PartTarget(i), spring, damping, dt);
			part.AngularVelocity += AngleDelta(part.Angle, PartTargetAngle(i)) * spring * 0.18f * dt;
			part.AngularVelocity *= MathF.Exp(-damping * dt);
			part.Angle += part.AngularVelocity * dt;
		}
	}

	Vector2 PartTarget(int index)
	{
		if (index < 4)
		{
			var hip = Hip(index);
			var foot = legs[index].Foot;
			return Vector2.Lerp(hip, foot, 0.52f) - surface.TowardSurface * (4 + index % 2 * 1.5f) * Scale;
		}

		var side = index == 4 ? -1 : 1;
		var flare = ArmorFlare();
		return bodyPosition + surface.Tangent * side * (5.5f + flare * 3.5f) * Scale
			+ surface.TowardSurface * (1 - flare * 2) * Scale;
	}

	float PartTargetAngle(int index)
	{
		if (index < 4)
			return Calc.Angle(legs[index].Foot - Hip(index));
		var side = index == 4 ? -1 : 1;
		return bodyAngle + side * ArmorFlare() * 0.72f;
	}

	float ArmorFlare()
	{
		if (state == State.Notice)
			return Ease.Back.Out(Math.Clamp(stateTimer / NoticeDuration, 0, 1));
		if (state == State.Hunting)
		{
			var phase = stateTimer % HuntCycle;
			if (phase < HuntWindup)
				return -0.45f * Ease.Cube.InOut(phase / HuntWindup);
			if (phase < HuntWindup + HuntBurst)
				return 0.65f;
		}
		return 0;
	}

	Vector2 Hip(int index)
	{
		var along = (index - 1.5f) * 3.1f * Scale;
		return bodyPosition + surface.Tangent * along + surface.TowardSurface * 1.5f * Scale;
	}

	void PlantAllFeet()
	{
		for (var i = 0; i < legs.Length; i++)
		{
			var desired = DesiredFoot(i);
			legs[i].Foot = FindPurchase(desired, out var purchase) ? purchase : desired;
			legs[i].From = legs[i].To = legs[i].Foot;
			legs[i].Step = 1;
			legs[i].Lift = 1;
		}
	}

	void UpdateLegs()
	{
		var lifting = 0;
		for (var i = 0; i < legs.Length; i++)
		{
			ref var leg = ref legs[i];
			if (leg.Step >= 1)
				continue;
			if (leg.Delay > 0)
				leg.Delay -= Time.Delta;
			else
				leg.Step = MathF.Min(1, leg.Step + StepSpeed * Time.Delta);
			var eased = Ease.Cube.InOut(leg.Step);
			leg.Foot = Vector2.Lerp(leg.From, leg.To, eased)
				- surface.TowardSurface * MathF.Sin(leg.Step * MathF.PI) * StepLift * leg.Lift * Scale;
			if (leg.Step < 1)
				lifting++;
			else
			{
				leg.Contact = NeuralFootContactDuration;
				AudioEvent("enemies/magnetic_crawler/footstep");
			}
		}

		if (walkSign == 0 || lifting >= 2)
			return;
		var pick = -1;
		var pickDrift = StepReach * StepReach * Scale * Scale;
		for (var i = 0; i < legs.Length; i++)
		{
			if (legs[i].Step < 1)
				continue;
			var drift = Vector2.DistanceSquared(legs[i].Foot, DesiredFoot(i));
			if (drift > pickDrift)
			{
				pick = i;
				pickDrift = drift;
			}
		}
		if (pick < 0)
			return;

		var desired = DesiredFoot(pick) + surface.Tangent * walkSign * StepReach * 0.8f * Scale;
		if (!FindPurchase(desired, out var purchase) && !FindPurchase(DesiredFoot(pick), out purchase))
			return;
		ref var step = ref legs[pick];
		step.From = step.Foot;
		step.To = purchase;
		step.Step = 0;
		step.Delay = (pick & 1) * 0.035f;
		step.Lift = rng.Float(0.8f, 1.35f);
	}

	Vector2 DesiredFoot(int index)
	{
		var side = index < 2 ? -1 : 1;
		var pair = index & 1;
		return (Vector2)Position + surface.Tangent * side * (5 + pair * 6) * Scale + surface.TowardSurface * 2 * Scale;
	}

	bool FindPurchase(Vector2 desired, out Vector2 purchase)
	{
		for (var i = 0; i < 5; i++)
		{
			var spread = i switch { 1 => -0.45f, 2 => 0.45f, 3 => -0.8f, 4 => 0.8f, _ => 0 };
			var direction = (surface.TowardSurface + surface.Tangent * spread).Normalized();
			var start = desired - direction * 9 * Scale;
			var probe = new RectInt((int)MathF.Round(start.X), (int)MathF.Round(start.Y), 1, 1);
			if (Game.TryFindSurface(this, probe, direction, (int)MathF.Ceiling(20 * Scale), out var hit))
			{
				purchase = hit.Point;
				return true;
			}
		}
		purchase = default;
		return false;
	}

	Vector2 SourcePosition(int index)
	{
		var offsets = (ReadOnlySpan<float>)[-13, -8, -3, 3, 8, 13];
		return (Vector2)Position + surface.Tangent * offsets[index] * Scale + surface.TowardSurface * (2 + index % 2) * Scale;
	}

	float SourceAngle(int index) => Calc.Angle(surface.Tangent) + ((index & 1) == 0 ? -0.18f : 0.18f);

	void UpdateHitbox()
	{
		var half = Math.Max(4, (int)MathF.Round(6 * Scale));
		var center = (bodyPosition - (Vector2)Position).RoundToPoint2();
		Hitbox = new(new RectInt(center.X - half, center.Y - half, half * 2, half * 2));
	}

	void EmitSparks(Vector2 position, int count)
		=> Game.Particles?.Emit(sparkEffect, position, count, Calc.Angle(-surface.TowardSurface), 1, MathF.PI * 0.8f);

	internal override void OnWasHit(HitInfo hitInfo)
	{
		if (state is State.Dormant or State.Assembling or State.Dying)
			return;

		var by = hitInfo.By;
		AudioEvent("enemies/magnetic_crawler/hit");
		neuralHitRemaining = 0.24f;

		Health--;
		Game.HitStun(0.15f);
		if (Health <= 0)
		{
			BeginDying(by);
			return;
		}

		var index = rng.Int(parts.Length);
		ref var part = ref parts[index];
		var recoil = (part.Position - (Vector2)by.Position).Normalized(-surface.TowardSurface);
		part.Detached = 0.62f;
		part.Velocity += recoil * 95 + surface.Tangent * rng.Float(-30, 30);
		part.AngularVelocity += rng.Float(-12, 12);
		eyeHitFlash = 1;
		EmitSparks(part.Position, 12);
		SetState(State.Notice);
	}
	public override float HitKnockback => 70f;

	void BeginDying(Actor by)
	{
		SetState(State.Dying);
		Mask = Masks.None;
		var away = ((Vector2)Position - by.Position).Normalized(-surface.TowardSurface);
		for (var i = 0; i < parts.Length; i++)
		{
			var spread = surface.Tangent * (i - 2.5f) * 11;
			parts[i].Velocity = away * rng.Float(45, 90) - surface.TowardSurface * rng.Float(25, 70) + spread;
			parts[i].AngularVelocity = rng.Float(-15, 15);
		}
		bodyVelocity = away * 50 - surface.TowardSurface * 45;
		EmitSparks(bodyPosition, 30);
		Game.HitStun(0.12f);
		AudioEvent("enemies/magnetic_crawler/death");
	}

	void UpdateDying()
	{
		var dt = MathF.Min(Time.Delta, 0.05f);
		bodyVelocity += Vector2.UnitY * 180 * dt;
		bodyPosition += bodyVelocity * dt;
		for (var i = 0; i < parts.Length; i++)
		{
			parts[i].Velocity += Vector2.UnitY * 220 * dt;
			parts[i].Position += parts[i].Velocity * dt;
			parts[i].Angle += parts[i].AngularVelocity * dt;
		}
		if (stateTimer >= 0.9f)
			Game.DestroyForThisSession(this);
	}

	float AssemblyProgress => state switch
	{
		State.Dormant => 0,
		State.Assembling => Math.Clamp(stateTimer / AssemblyDuration, 0, 1),
		_ => 1
	};

	public void PerturbNeuralTexture(NeuralTextureActor.PerturbContext context)
	{
		if (SkinMode != RigSkinMode.NCA || context.Texture != neuralSkin?.Texture)
			return;
		if (neuralHitRemaining > 0)
			context.AddStamp(bodyPosition, bodyPosition, 6 * Scale, NeuralReactions.NeuralHitStrength);
		for (var i = 0; i < legs.Length; i++)
			if (legs[i].Contact > 0 && NeuralFootContactStrength > 0)
				context.AddStamp(legs[i].Foot, legs[i].Foot, NeuralFootContactRadius * Scale,
					NeuralFootContactStrength * legs[i].Contact / NeuralFootContactDuration);

		if (state is State.Dormant or State.Dying || NeuralReactions.NeuralJostleInterval <= 0 ||
			NeuralReactions.NeuralJostleStrength <= 0 || Time.SecondsF < nextNeuralJostle)
			return;
		nextNeuralJostle = Time.SecondsF + NeuralReactions.NeuralJostleInterval;
		var index = (int)(Hash32.Mix(visualSeed ^ (uint)(Time.SecondsF * 17)) % (uint)parts.Length);
		context.AddStamp(parts[index].Position, parts[index].Position,
			NeuralReactions.NeuralJostleRadius * Scale, NeuralReactions.NeuralJostleStrength);
	}

	float EyeIntensity
	{
		get
		{
			if (state is State.Dormant or State.Dying)
				return 0;
			if (state == State.Assembling)
				return Math.Clamp((AssemblyProgress - 0.76f) / 0.24f, 0, 1);
			if (state == State.Notice)
				return 0.3f + Ease.Back.Out(Math.Clamp(stateTimer / NoticeDuration, 0, 1)) * 0.9f;
			if (state == State.Hunting)
			{
				var phase = stateTimer % HuntCycle;
				return phase < HuntWindup ? 0.45f + phase / HuntWindup * 0.75f : phase < HuntWindup + HuntBurst ? 1.15f : 0.55f;
			}
			return 0.32f + MathF.Sin(Time.SecondsF * 3.7f) * 0.05f;
		}
	}

	public override void Render(Batcher batcher)
	{
		if (SpriteBlinkHidden || AssemblyProgress <= 0 && string.IsNullOrEmpty(SurfaceId.Name))
			return;
		DrawRig(batcher, Color.White, shadow: false);
	}

	public override void RenderShadow(Batcher batcher, Color color) => DrawRig(batcher, color, shadow: true);

	void DrawRig(Batcher batcher, Color tint, bool shadow)
	{
		var assembly = AssemblyProgress;
		var sourceSurface = Assets.GetSurface(SurfaceId);
		var neuralTexture = !shadow && SkinMode == RigSkinMode.NCA ? neuralSkin?.OutputTexture : null;
		if (neuralTexture != null)
			batcher.PushSampler(new(TextureFilter.Nearest, TextureWrap.Clamp));
		if (state != State.Dying && assembly > 0.55f)
			for (var i = 0; i < legs.Length; i++)
			{
				var hip = Hip(i) - Position;
				var knee = parts[i].Position - Position;
				var foot = legs[i].Foot - Position;
				var outline = shadow ? tint : MetalEdgeColor;
				var metal = shadow ? tint : MetalColor;
				batcher.Line(hip, knee, 3.5f * Scale, outline);
				batcher.Line(knee, foot, 3.5f * Scale, outline);
				batcher.Line(hip, knee, 1.5f * Scale, metal);
				batcher.Line(knee, foot, 1.5f * Scale, metal);
				DrawClaw(batcher, knee, foot, outline, metal);
			}

		for (var i = 0; i < parts.Length; i++)
			DrawPart(batcher, i, tint, shadow, sourceSurface, neuralTexture);

		if (assembly > 0.68f)
		{
			var center = bodyPosition - Position;
			batcher.Circle(center, 6.2f * Scale, 18, shadow ? tint : MetalEdgeColor);
			var coreColor = Color.Lerp(new Color(0x30343d), MetalColor, 0.16f);
			batcher.Circle(center, 4.8f * Scale, 18, shadow ? tint : coreColor);
			if (!shadow && SkinMode == RigSkinMode.Surface && sourceSurface != null)
				DrawSurfaceCircle(batcher, sourceSurface, center, 4.8f * Scale, bodyPosition);
			else if (!shadow && neuralTexture != null)
				DrawNeuralCircle(batcher, neuralTexture, center, 4.8f * Scale,
					NeuralUV(new Vector2(0, -BodyHeight)), coreColor * Neural.NeuralColorAmount);
			if (!shadow)
			{
				var seam = Calc.AngleToVector(bodyAngle + MathF.PI * 0.25f) * 4.8f * Scale;
				batcher.Line(center - seam, center - seam * 0.72f, 1, MetalColor);
				batcher.Line(center + seam * 0.72f, center + seam, 1, MetalEdgeColor);
			}
			if (!shadow && state != State.Dying)
				DrawEye(batcher, emissive: false);
		}
		if (neuralTexture != null)
			batcher.PopSampler();
	}

	void DrawPart(Batcher batcher, int index, Color tint, bool shadow, Surface? sourceSurface, Texture? neuralTexture)
	{
		var part = parts[index];
		var half = index < 4 ? new Vector2(3.8f, 2.2f) : new Vector2(5.2f, 3.4f);
		half *= Scale;
		Span<Vector2> vertices = stackalloc Vector2[PlateVertexCount];
		PartVertices(part, half, index, vertices, world: false);

		if (shadow)
		{
			DrawPolygon(batcher, vertices, tint);
			return;
		}

		var final = AssemblyProgress;
		if (state == State.Assembling)
			final = Math.Clamp((AssemblyProgress - part.Phase) / MathF.Max(0.01f, 1 - part.Phase), 0, 1);
		var plateColor = Color.Lerp(MetalColor, MetalEdgeColor, 0.05f + ShapeUnit(index, 8) * 0.12f);
		var opacity = MathF.Max(final, string.IsNullOrEmpty(SurfaceId.Name) ? 0.28f : 0);
		DrawPolygon(batcher, vertices, plateColor * opacity);
		if (sourceSurface != null && (final < 1 || SkinMode == RigSkinMode.Surface))
			DrawSurfacePlate(batcher, sourceSurface, part, half, index, vertices,
				SkinMode == RigSkinMode.Surface ? 1 : 1 - final);
		if (neuralTexture != null && final > 0)
			DrawNeuralPlate(batcher, neuralTexture, index, half, vertices,
				plateColor * (Neural.NeuralColorAmount * final));

		var edge = MetalEdgeColor * MathF.Max(final, 0.25f);
		for (var i = 0; i < vertices.Length; i++)
			batcher.Line(vertices[i], vertices[(i + 1) % vertices.Length], 1.6f * Scale, edge);
		var highlight = Color.Lerp(plateColor, Color.White, 0.28f) * final;
		batcher.Line(vertices[1], vertices[2], 0.7f * Scale, highlight);
		if (index >= 4 && final > 0.5f)
		{
			var center = PolygonCenter(vertices);
			var rivet = Calc.AngleToVector(part.Angle) * half.X * 0.38f;
			batcher.Circle(center + rivet, 0.7f * Scale, 6, edge);
		}
	}

	void PartVertices(Part part, Vector2 half, int index, Span<Vector2> vertices, bool world)
	{
		var center = world ? part.Position : part.Position - Position;
		PlateAxes(index, part.Angle, half, out var x, out var y);
		PlateVertices(index, center, x, y, vertices);
	}

	void PlateAxes(int index, float angle, Vector2 half, out Vector2 x, out Vector2 y)
	{
		half *= new Vector2(1 + ShapeSigned(index, 4) * 0.09f, 1 + ShapeSigned(index, 5) * 0.12f);
		x = Calc.AngleToVector(angle) * half.X;
		y = x.TurnLeft().Normalized() * half.Y;
	}

	void PlateVertices(int index, Vector2 center, Vector2 x, Vector2 y, Span<Vector2> vertices)
	{
		var a = 0.12f + ShapeUnit(index, 0) * 0.2f;
		var b = 0.12f + ShapeUnit(index, 1) * 0.2f;
		var c = 0.12f + ShapeUnit(index, 2) * 0.2f;
		var d = 0.12f + ShapeUnit(index, 3) * 0.2f;
		var slant = ShapeSigned(index, 6) * 0.1f;
		vertices[0] = center - x + y * (-1 + a);
		vertices[1] = center + x * (-1 + a + slant) - y;
		vertices[2] = center + x * (1 - b + slant) - y;
		vertices[3] = center + x + y * (-1 + b);
		vertices[4] = center + x + y * (1 - c);
		vertices[5] = center + x * (1 - c - slant) + y;
		vertices[6] = center + x * (-1 + d - slant) + y;
		vertices[7] = center - x + y * (1 - d);
	}

	float ShapeUnit(int index, int salt)
		=> (Hash32.Mix(visualSeed ^ (uint)(index + 1) * 0x9e3779b9u ^ (uint)(salt + 1) * 0x85ebca6bu) & 0xffff) /
			65535f;

	float ShapeSigned(int index, int salt) => ShapeUnit(index, salt) * 2 - 1;

	static Vector2 PolygonCenter(ReadOnlySpan<Vector2> vertices)
	{
		var center = Vector2.Zero;
		for (var i = 0; i < vertices.Length; i++)
			center += vertices[i];
		return center / vertices.Length;
	}

	static void DrawPolygon(Batcher batcher, ReadOnlySpan<Vector2> vertices, Color color)
	{
		var center = PolygonCenter(vertices);
		for (var i = 0; i < vertices.Length; i++)
			batcher.Triangle(center, vertices[i], vertices[(i + 1) % vertices.Length], color);
	}

	void DrawSurfacePlate(Batcher batcher, Surface surfaceMaterial, Part part, Vector2 half, int index,
		ReadOnlySpan<Vector2> vertices, float opacity, bool normals = false)
	{
		Span<Vector2> world = stackalloc Vector2[PlateVertexCount];
		PartVertices(part, half, index, world, world: true);
		var center = PolygonCenter(vertices);
		var worldCenter = PolygonCenter(world);
		Span<Surface.Vertex> triangles = stackalloc Surface.Vertex[PlateVertexCount * 3];
		for (var i = 0; i < vertices.Length; i++)
		{
			var at = i * 3;
			triangles[at] = new(center, worldCenter);
			triangles[at + 1] = new(vertices[i], world[i]);
			triangles[at + 2] = new(vertices[(i + 1) % vertices.Length], world[(i + 1) % vertices.Length]);
		}
		if (normals)
			surfaceMaterial.RenderNormals(batcher, triangles, Time.SecondsF);
		else
			surfaceMaterial.Render(batcher, triangles, Time.SecondsF, opacity);
	}

	void DrawNeuralPlate(Batcher batcher, Texture texture, int index, Vector2 half,
		ReadOnlySpan<Vector2> vertices, Color color)
	{
		var materialCenter = PlateMaterialCenter(index);
		PlateAxes(index, 0, half, out var x, out var y);
		Span<Vector2> uv = stackalloc Vector2[PlateVertexCount];
		PlateVertices(index, materialCenter, x, y, uv);
		var center = PolygonCenter(vertices);
		var centerUV = NeuralUV(materialCenter);
		for (var i = 0; i < vertices.Length; i++)
			batcher.Triangle(texture, center, vertices[i], vertices[(i + 1) % vertices.Length],
				centerUV, NeuralUV(uv[i]), NeuralUV(uv[(i + 1) % vertices.Length]), color);
	}

	static Vector2 PlateMaterialCenter(int index) => index < 4
		? new Vector2((index - 1.5f) * 6.5f, -6.5f + (index & 1) * 1.5f)
		: new Vector2(index == 4 ? -6 : 6, -BodyHeight);

	void DrawSurfaceCircle(Batcher batcher, Surface surfaceMaterial, Vector2 center, float radius,
		Vector2 worldCenter, bool normals = false)
	{
		const int steps = 16;
		Span<Surface.Vertex> triangles = stackalloc Surface.Vertex[steps * 3];
		var previous = center + Vector2.UnitX * radius;
		var previousWorld = worldCenter + Vector2.UnitX * radius;
		for (var i = 1; i <= steps; i++)
		{
			var direction = Calc.AngleToVector(i / (float)steps * MathF.Tau);
			var next = center + direction * radius;
			var nextWorld = worldCenter + direction * radius;
			var at = (i - 1) * 3;
			triangles[at] = new(center, worldCenter);
			triangles[at + 1] = new(previous, previousWorld);
			triangles[at + 2] = new(next, nextWorld);
			previous = next;
			previousWorld = nextWorld;
		}
		if (normals)
			surfaceMaterial.RenderNormals(batcher, triangles, Time.SecondsF);
		else
			surfaceMaterial.Render(batcher, triangles, Time.SecondsF);
	}

	static void DrawNeuralCircle(Batcher batcher, Texture texture, Vector2 center, float radius,
		Vector2 uvCenter, Color color)
	{
		const int steps = 16;
		var uvRadius = new Vector2(radius) / (Vector2)NeuralSize;
		var previous = center + Vector2.UnitX * radius;
		var previousUV = uvCenter + Vector2.UnitX * uvRadius;
		for (var i = 1; i <= steps; i++)
		{
			var direction = Calc.AngleToVector(i / (float)steps * MathF.Tau);
			var next = center + direction * radius;
			var nextUV = uvCenter + direction * uvRadius;
			batcher.Triangle(texture, center, previous, next, uvCenter, previousUV, nextUV, color);
			previous = next;
			previousUV = nextUV;
		}
	}

	static Vector2 NeuralUV(Vector2 position) => (position - (Vector2)NeuralOrigin) / (Vector2)NeuralSize;

	void DrawClaw(Batcher batcher, Vector2 knee, Vector2 foot, Color outline, Color fill)
	{
		var direction = (foot - knee).Normalized(surface.TowardSurface);
		var side = new Vector2(-direction.Y, direction.X);
		batcher.Line(foot, foot + direction * 2.5f * Scale + side * 2 * Scale, 2, outline);
		batcher.Line(foot, foot + direction * 2.5f * Scale - side * 2 * Scale, 2, outline);
		batcher.Circle(foot, 1.3f * Scale, 8, fill);
	}

	void DrawEye(Batcher batcher, bool emissive)
	{
		var intensity = Math.Clamp(EyeIntensity + eyeHitFlash, 0, 1.4f);
		if (intensity <= 0)
			return;
		var center = bodyPosition - Position;
		if (Game.GetFirst<Player>() is { } player)
			center += ((Vector2)player.Position - bodyPosition).Normalized() * 1.1f * Scale;
		var color = emissive ? EyeGlowColor : EyeColor;
		var radius = (emissive ? 2.6f : 1.8f) * Scale * MathF.Min(1.2f, intensity);
		batcher.Circle(center, radius, 12, color * MathF.Min(1, intensity));
		if (!emissive)
			batcher.Circle(center - Vector2.One * 0.45f * Scale, 0.55f * Scale, 8, Color.White * MathF.Min(1, intensity));
	}

	public override void RenderEmissive(Batcher batcher)
	{
		if (!SpriteBlinkHidden)
			DrawEye(batcher, emissive: true);
	}

	public override void ForEachLight(Action<Vector2, float, float, float, Color> action)
	{
		var intensity = Math.Clamp(EyeIntensity + eyeHitFlash, 0, 1.3f);
		if (intensity > 0.05f)
			action(bodyPosition, EyeLightRadius * Scale, EyeLightIntensity * intensity, 7 * Scale, EyeGlowColor);
	}

	public override void RenderNormals(Batcher batcher)
	{
		if (state == State.Dormant || SpriteBlinkHidden)
			return;
		var flat = new Color(0x8080ff);
		var surfaceMaterial = SkinMode == RigSkinMode.Surface ? Assets.GetSurface(SurfaceId) : null;
		var neuralNormals = SkinMode == RigSkinMode.NCA ? neuralSkin?.OutputNormalTexture : null;
		if (neuralNormals != null)
			batcher.PushSampler(new(TextureFilter.Nearest, TextureWrap.Clamp));
		Span<Vector2> vertices = stackalloc Vector2[PlateVertexCount];
		if (state != State.Dying)
			for (var i = 0; i < legs.Length; i++)
			{
				batcher.Line(Hip(i) - Position, parts[i].Position - Position, 3.5f * Scale, flat);
				batcher.Line(parts[i].Position - Position, legs[i].Foot - Position, 3.5f * Scale, flat);
			}
		for (var i = 0; i < parts.Length; i++)
		{
			var half = (i < 4 ? new Vector2(3.8f, 2.2f) : new Vector2(5.2f, 3.4f)) * Scale;
			PartVertices(parts[i], half, i, vertices, world: false);
			DrawPolygon(batcher, vertices, flat);
			if (surfaceMaterial != null)
				DrawSurfacePlate(batcher, surfaceMaterial, parts[i], half, i, vertices, 1, normals: true);
			else if (neuralNormals != null)
				DrawNeuralPlate(batcher, neuralNormals, i, half, vertices, Color.White * Neural.NeuralNormalAmount);
		}
		RenderSphereNormals(batcher, bodyPosition - Position, 6.2f * Scale, 18);
		if (surfaceMaterial != null)
			DrawSurfaceCircle(batcher, surfaceMaterial, bodyPosition - Position, 4.8f * Scale, bodyPosition,
				normals: true);
		else if (neuralNormals != null)
			DrawNeuralCircle(batcher, neuralNormals, bodyPosition - Position, 4.8f * Scale,
				NeuralUV(new Vector2(0, -BodyHeight)), Color.White * Neural.NeuralNormalAmount);
		if (neuralNormals != null)
			batcher.PopSampler();
	}

	static void RenderSphereNormals(Batcher batcher, Vector2 center, float radius, int steps)
	{
		var face = new Color(0x8080ff);
		for (var i = 0; i < steps; i++)
		{
			var a = Calc.AngleToVector(i / (float)steps * Calc.TAU);
			var b = Calc.AngleToVector((i + 1) / (float)steps * Calc.TAU);
			batcher.Triangle(center, center + a * radius, center + b * radius, face, NormalColor(a), NormalColor(b));
		}
	}

	static Color NormalColor(Vector2 direction)
		=> new(direction.X * 0.5f + 0.5f, direction.Y * 0.5f + 0.5f, 0.5f, 1);

	static float AngleDelta(float from, float to)
	{
		var delta = to - from;
		return delta - MathF.Round(delta / Calc.TAU) * Calc.TAU;
	}

	public override Rect GetVisualBounds()
	{
		var bounds = new Rect(bodyPosition, Vector2.Zero);
		for (var i = 0; i < parts.Length; i++)
			bounds = bounds.Conflate(parts[i].Position);
		if (state != State.Dying && AssemblyProgress > 0.55f)
			for (var i = 0; i < legs.Length; i++)
				bounds = bounds.Conflate(legs[i].Foot);
		return bounds.Inflate(8 * Scale);
	}
	
	public void LiveStatusImGui()
	{
#if EDITOR
		ImGui.SeparatorText("State");
		var value = new ValueStringBuilder(stackalloc char[128]);
		value.Append(state.ToString());
		ImGui.Text(value.AsSpan());
#endif
	}
}
