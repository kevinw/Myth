using System.Numerics;
using Foster.Framework;
#if EDITOR
using LinkDotNet.StringBuilder;
using ImGuiNET;
#endif

namespace Myth;

[GameActor(Sprite = "saw", Tags = ActorTag.Enemy, LivePositionEditable = true)]
public class MagneticCrawler : Actor, INeuralTextureAgent, IPenDrawn
{
	enum State
	{
		Patrol,
		Hunting,
		Dying,
		Dormant,          // sunk low, armor tucked, eye dark, untouchable: waiting on its siblings
		WakeUpFromDormant // rising, eye sputtering on, seams lighting up, armor unfolding -- then the hunt
	}

	struct Part
	{
		public Vector2 Position;
		public Vector2 Velocity;
		public float Angle;
		public float AngularVelocity;
		public float Detached;
		public int Damage;
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
		public Vector2 Normal, ToNormal; // the surface it stands on, and the one it's stepping to (both outward)
	}

	public Point2 InitialSurfaceDirection = Point2.Down;
	public ContentRef<SurfaceContent> SurfaceId = new("");
	public bool ClimbsWallsAndCeilings = true;
	public bool CanWalkOnMovingPlatforms;
	// Sleeps (Dormant) until every other crawler in the room is dead, then wakes and hunts.
	public bool WaitsForSiblingsToDie;
	[I(1, 12)] public int Health = 4;
	[I(0.5f, 4)] public float Scale = 1;
	[I(5, 32)] public float PatrolSpeed = 15;
	[I(32, 100)] public float HuntSpeed = 48;
	public Color MetalColor = new(0x858d9a);
	public Color MetalEdgeColor = new(0x303743);
	public Color EyeColor = new(0xb20f24);
	public Color EyeGlowColor = new(0xff1838);
	[I(4, 64)] public float EyeLightRadius = 28;
	[I(0, 2)] public float EyeLightIntensity = 0.75f;
	public RigSkinMode SkinMode;
	public ContentRef<VisualTextureContent> GritTexture = PenMaterial.DefaultTexture;
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

	static float HuntWindup => 0.12f;
	static float HuntBurst => 0.95f;
	static float HuntRecover => 0.12f;
	static float WakeDuration => 1.8f;
	static float DormantSink => 5f;
	static float RoamRadius => 96f;
	static float RouteSearchInterval => 0.5f;
	static float BodyHeight => 9f;
	static float BodySpring => 90f;
	static float BodyDamping => 12f;
	static float PartSpring => 115f;
	static float PartDamping => 14f;
	static float StepReach => 7f;
	static float StepSpeed => 7f;
	static float StepLift => 5f;
	// Shape tweakables, in unscaled pixels (everything below multiplies by Scale).
	static float UpperLeg => 7f;
	static float LowerLeg => 8f;
	static float UpperLegWidth => 3.5f;          // upper/lower widths are the limb's taper
	static float LowerLegWidth => 3f;
	static float KneeLift => 0.5f;               // 0 = knees bend straight out to the side, higher = up and away from the surface
	static float HipSpan => 9.3f;                // how far apart the outermost hips sit along the body
	static float StanceInner => 5f;              // innermost foot's distance from center...
	static float StanceSpread => 7f;             // ...and how much farther the outermost one plants
	static float BodyRadius => 6.1f;
	static Vector2 ArmorPlate => new(4.8f, 3.2f); // half-extents
	static float ArmorReach => 5.5f;             // armor plates' distance out from the body center
	static Vector2 KneePlate => new(3.2f, 1.9f);
	static float ClawLength => 3.5f;
	static float ClawSplay => 0.8f;              // how wide the claws fan while the foot is lifted
	// Getting hit: the head snaps back hardest, the armor less, the knees least (px/s, scaled), all on springs
	// gone briefly loose so they overshoot and wobble home. The walk eases down to a stagger and back up.
	static float HeadKick => 122f;
	static float ArmorKick => 36f;
	static float KneeKick => 34f;
	static float HitWobbleTime => 0.7f;
	static float HitLoosen => 0.5f;
	static float HitStagger => 0.3f;
	static float PaceDrop => 0.08f;
	static float PaceRecover => 0.45f;
	static float NeuralFootContactDuration => 0.16f;
	static Point2 NeuralSize => new(48, 40);
	static Point2 NeuralOrigin => new(-24, -26);
	const int PlateVertexCount = 8;

	readonly SurfaceFollower surface;
	const int LegCount = 6; // even: half on each side
	readonly Part[] parts = new Part[LegCount + 2]; // a knee plate per leg, then the left and right armor
	readonly Leg[] legs = new Leg[LegCount];
	readonly float[] legNear = new float[LegCount]; // 0 far side of the body .. 1 near side
	Rng rng = new();
	State state;
	float stateTimer;
	float stuckTimer;
	float eyeHitFlash;
	float hitWobble;     // 1 just after a hit, easing to 0: loosens the springs
	float stagger;       // seconds of stagger left after a hit
	float walkPace = 1;  // 0..1 scale on walking speed, ramping down into a stagger and back up out of it
	float walkSign;
	float bodyAngle;
	Vector2 bodyPosition;
	Vector2 bodyVelocity;
	bool huntBurstStarted;
	Vector2 spawnPosition;
	Vector2? walkTarget;
	float walkTargetTime;
	float routeSearchTimer;
	int sparkEffect = -1;
	uint visualSeed;
	float neuralHitRemaining;
	float nextNeuralJostle;
	NeuralSkinRuntime? neuralSkin;

	public override bool CastsSpriteShadow => true;
	public override bool CastsReflection => true;
	public override bool LightingDithered => false;
	public override string HitSoundType => "metal";

	public MagneticCrawler()
	{
		Sprite = null;
		Depth = Depths.Enemy;
		Facing = Signs.Positive;
		surface = new(this);
	}

	float randomTimeOffset;

	public override void Added()
	{
		randomTimeOffset = Game.rng.Float(0, 60f);
		visualSeed = VisualSeed();
		var half = Math.Max(4, (int)MathF.Round(6 * Scale));
		Hitbox = new(new RectInt(-half, -half, half * 2, half * 2));
		surface.ProbeRect = Hitbox.Rect;
		surface.ClimbsWallsAndCeilings = ClimbsWallsAndCeilings;
		surface.CanWalkOnMovingPlatforms = CanWalkOnMovingPlatforms;
		surface.TowardSurface = InitialSurfaceDirection == Point2.Zero ? Vector2.UnitY : InitialSurfaceDirection.Normalized();
		spawnPosition = Position;
		AttachToSurface();
		bodyAngle = Calc.Angle(surface.Tangent);
		bodyPosition = (Vector2)Position - surface.TowardSurface * BodyHeight * Scale;
		PlantAllFeet();
		InitializeParts();
		for (var i = 0; i < legNear.Length; i++)
			legNear[i] = NearTarget(i, Facing);
		UpdateHitbox();
		sparkEffect = AcquireParticleEffect("sparks");
		Mask = WaitsForSiblingsToDie ? Masks.None : Masks.Enemy;
		if (WaitsForSiblingsToDie)
			Depth = Depths.BehindPlayer; // part of the scenery behind the fight until it wakes
		SetState(WaitsForSiblingsToDie ? State.Dormant : State.Patrol);
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
		hitWobble = Calc.Approach(hitWobble, 0, Time.Delta / HitWobbleTime);
		stagger = MathF.Max(0, stagger - Time.Delta);
		for (var i = 0; i < legNear.Length; i++)
			legNear[i] = Calc.Approach(legNear[i], NearTarget(i, Facing), Time.Delta / LegSwapTime);
		walkPace = Calc.Approach(walkPace, stagger > 0 ? 0 : 1, Time.Delta / (stagger > 0 ? PaceDrop : PaceRecover));
		stateTimer += Time.Delta;

		if (state == State.Dying)
		{
			UpdateDying();
			return;
		}
		if (state is State.Dormant or State.WakeUpFromDormant)
		{
			UpdateDormant();
			UpdateLegs();
			UpdateBody();
			UpdateParts();
			UpdateHitbox();
			return;
		}

		var player = Game.GetFirst<Player>();
		var roaming = player == null || Vector2.DistanceSquared(player.Position, Position) > Game.Width * Game.Width;
		var next = roaming ? State.Patrol : State.Hunting;
		if (state != next)
		{
			walkTarget = null;
			SetState(next);
		}
		routeSearchTimer = MathF.Max(0, routeSearchTimer - Time.Delta);
		walkTargetTime -= Time.Delta;
		if (walkTarget is { } target && (walkTargetTime <= 0 || Vector2.DistanceSquared(Position, target) < 36))
			walkTarget = null;
		if (roaming)
		{
			if (walkTarget == null && routeSearchTimer <= 0)
				FindWalkTarget(spawnPosition + rng.Vec2() * rng.Float(RoamRadius), roaming: true);
			MoveAlongSurface((int)Facing, PatrolSpeed);
		}
		else
			UpdateHunt(player);

		UpdateLegs();
		UpdateBody();
		UpdateParts();
		UpdateHitbox();
		if (Environment.GetEnvironmentVariable("CRAWLER_TRACE") != null)
			Console.WriteLine($"CRAWLER {ActorId} pos={Position} state={state} roam={roaming} target={walkTarget} stuck={stuckTimer:0.00} face={(int)Facing}");
	}

	void UpdateHunt(Player? player)
	{
		var phase = stateTimer % HuntCycle;
		if (phase < HuntWindup)
		{
			huntBurstStarted = false;
			if (player != null && walkTarget == null)
				FacePlayer(player);
			MoveAlongSurface((int)Facing, PatrolSpeed);
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
			MoveAlongSurface((int)Facing, PatrolSpeed);
	}

	float HuntCycle => HuntWindup + HuntBurst + HuntRecover;

	void UpdateDormant()
	{
		if (state == State.Dormant)
		{
			if (SiblingsAlive())
				return;
			SetState(State.WakeUpFromDormant);
			Game.Shake(0.35f, 0.5f * Scale);
			EmitSparks(bodyPosition, 20);
			AudioEvent("enemies/magnetic_crawler/hit");
			return;
		}
		if (stateTimer < WakeDuration)
			return;
		Mask = Masks.Enemy;
		Depth = Depths.Enemy;
		Game.Shake(0.25f, 0.8f * Scale);
		EmitSparks(bodyPosition, 30);
		if (Game.GetFirst<Player>() is { } player)
			FacePlayer(player);
		SetState(State.Hunting);
	}

	bool SiblingsAlive()
	{
		foreach (var actor in Game.Actors)
			if (actor is MagneticCrawler { WaitsForSiblingsToDie: false, state: not State.Dying } sibling && sibling.Room == Room)
				return true;
		return false;
	}

	// 0..1 through the wake-up.
	float WakeProgress => state switch
	{
		State.Dormant => 0,
		State.WakeUpFromDormant => MathF.Min(1, stateTimer / WakeDuration),
		_ => 1
	};

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
		speed *= Ease.Cube.InOut(walkPace);
		var stepDirection = surface.Tangent * direction;
		var lookahead = (stepDirection * MathF.Max(1, speed * Time.Delta + 1)).RoundToPoint2();
		if (lookahead == Point2.Zero)
			lookahead = new Point2(Math.Sign(stepDirection.X), Math.Sign(stepDirection.Y));
		if (Game.OverlapsEnemyRoomBoundary(this, lookahead))
		{
			Facing = -Facing;
			walkTarget = null;
			stuckTimer += Time.Delta;
			SearchWhenStuck();
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
		SearchWhenStuck();
	}

	void SearchWhenStuck()
	{
		if (stuckTimer < 0.2f || routeSearchTimer > 0)
			return;
		walkTarget = null;
		var player = Game.GetFirst<Player>();
		var roaming = player == null || Vector2.DistanceSquared(player.Position, Position) > Game.Width * Game.Width;
		var focus = roaming ? spawnPosition + rng.Vec2() * rng.Float(RoamRadius) : (Vector2)player!.Position;
		if (!FindWalkTarget(focus, roaming))
		{
			// A lost attachment may need a different surface normal before either route can work.
			var previousToward = surface.TowardSurface;
			for (var i = 0; i < 4; i++)
			{
				var toward = Calc.AngleToVector(i * MathF.PI * 0.5f);
				if (!ClimbsWallsAndCeilings && toward.Y < 0.5f)
					continue;
				surface.TowardSurface = toward;
				if (surface.Attach(surface.SurfaceProbeDistance, surface.MaxSurfaceSnapDistance))
				{
					PlantAllFeet();
					break;
				}
				surface.TowardSurface = previousToward;
			}
			Facing = -Facing;
		}
		stuckTimer = 0;
	}

	bool FindWalkTarget(Vector2 focus, bool roaming)
	{
		routeSearchTimer = RouteSearchInterval;
		var origin = Position;
		var toward = surface.TowardSurface;
		var remainder = surface.Remainder;
		var movingPlatforms = surface.CanWalkOnMovingPlatforms;
		var bestScore = float.PositiveInfinity;
		var bestDirection = (int)Facing;
		Vector2? best = null;
		// Probe the same surface follower used for movement; restore all its state afterward.
		try
		{
			surface.CanWalkOnMovingPlatforms = false;
			for (var attempt = 0; attempt < 8; attempt++)
			{
				Position = origin;
				surface.TowardSurface = toward;
				surface.Remainder = Vector2.Zero;
				var direction = (attempt & 1) == 0 ? 1 : -1;
				var distance = rng.Float(24, RoamRadius);
				for (var traveled = 0f; traveled < distance; traveled += 4)
				{
					var before = Position;
					surface.Follow(direction, MathF.Min(4, distance - traveled));
					if (Position == before || Game.OverlapsEnemyRoomBoundary(this, Point2.Zero))
					{
						Position = before;
						break;
					}
				}
				if (Vector2.DistanceSquared(Position, origin) < 144 ||
					!surface.Attach(surface.CornerProbeDistance, 0))
					continue;
				var spawnDistance = Vector2.DistanceSquared(Position, spawnPosition);
				if (roaming && spawnDistance > RoamRadius * RoamRadius &&
					spawnDistance >= Vector2.DistanceSquared(origin, spawnPosition))
					continue;
				var score = Vector2.DistanceSquared(Position, focus);
				if (score >= bestScore)
					continue;
				bestScore = score;
				best = Position;
				bestDirection = direction;
			}
		}
		finally
		{
			Position = origin;
			surface.TowardSurface = toward;
			surface.Remainder = remainder;
			surface.CanWalkOnMovingPlatforms = movingPlatforms;
		}
		if (best == null)
			return false;
		walkTarget = best;
		walkTargetTime = RoamRadius / MathF.Max(1, state == State.Patrol ? PatrolSpeed : HuntSpeed) + 1;
		Facing = bestDirection;
		return true;
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
		if (next == State.Hunting)
		{
			stateTimer = HuntWindup;
			huntBurstStarted = false;
		}
	}

	void InitializeParts()
	{
		for (var i = 0; i < parts.Length; i++)
		{
			parts[i].Position = PartTarget(i);
			parts[i].Angle = PartTargetAngle(i);
		}
	}

	Vector2 ActiveBodyTarget()
	{
		var crouch = CrouchAmount();
		var stride = state == State.Hunting && HuntPhase() is > 0 and < 1
			? surface.Tangent * (int)Facing * 2.5f * Scale
			: Vector2.Zero;
		var drift = Vector2.Zero;
		for (var i = 0; i < legs.Length; i++)
			drift += legs[i].Foot - DesiredFoot(i);
		var sink = DormantSink * (1 - Ease.Back.Out(WakeProgress));
		return (Vector2)Position - surface.TowardSurface * (BodyHeight - crouch * 4 - sink) * Scale + stride + drift * 0.12f;
	}

	float CrouchAmount()
	{
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
		Spring.Step(ref bodyPosition, ref bodyVelocity, ActiveBodyTarget(), BodySpring, BodyDamping * Loosened, dt);
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
			var damping = part.Detached > 0 ? 3.5f : PartDamping * Loosened;
			Spring.Step(ref part.Position, ref part.Velocity, PartTarget(i), spring, damping, dt);
			part.AngularVelocity += AngleDelta(part.Angle, PartTargetAngle(i)) * spring * 0.18f * dt;
			part.AngularVelocity *= MathF.Exp(-damping * dt);
			part.Angle += part.AngularVelocity * dt;
		}
	}

	float Loosened => 1 - HitLoosen * hitWobble;

	// parts[] holds a knee plate per leg, then the two armor plates (left, right).
	bool IsKnee(int index) => index < legs.Length;
	int ArmorSide(int index) => index == legs.Length ? -1 : 1;
	int LegSide(int index) => index < legs.Length / 2 ? -1 : 1;

	Vector2 PartTarget(int index)
	{
		if (IsKnee(index))
			return Knee(index);

		var side = ArmorSide(index);
		var flare = ArmorFlare();
		return bodyPosition + surface.Tangent * side * (ArmorReach + flare * 3.5f) * Scale
			+ surface.TowardSurface * (1 - flare * 2) * Scale;
	}

	float PartTargetAngle(int index)
	{
		if (IsKnee(index))
			return Calc.Angle(legs[index].Foot - Hip(index));
		var side = ArmorSide(index);
		return bodyAngle + side * ArmorFlare() * 0.72f;
	}

	float ArmorFlare()
	{
		if (state is State.Dormant or State.WakeUpFromDormant)
		{
			// Tucked shut while it sleeps, then unfolding past flat into a full flare as it comes to.
			var wake = WakeProgress;
			return wake < 0.6f ? -0.45f * (1 - wake / 0.6f) : 0.65f * Ease.Cube.Out((wake - 0.6f) / 0.4f);
		}
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

	// Two-bone IK: upper and lower leg keep their lengths, so the knee is pushed out to the leg's side, spider-
	// like -- folding high as the body sinks or a foot reaches in, straightening as a step stretches.
	Vector2 Knee(int index)
	{
		var (hip, foot) = (Hip(index), legs[index].Foot);
		var (upper, lower) = (UpperLeg * Scale, LowerLeg * Scale);
		var reach = foot - hip;
		var distance = Math.Clamp(reach.Length(), 0.01f, upper + lower - 0.01f);
		var along = reach.Normalized(surface.TowardSurface);
		var bend = along.TurnLeft();
		if (Vector2.Dot(bend, surface.Tangent * LegSide(index) - surface.TowardSurface * KneeLift) < 0)
			bend = -bend;
		var a = (upper * upper - lower * lower + distance * distance) / (2 * distance);
		return hip + along * a + bend * MathF.Sqrt(MathF.Max(0, upper * upper - a * a));
	}

	Vector2 Hip(int index)
	{
		var along = (index - (legs.Length - 1) * 0.5f) * HipSpan / (legs.Length - 1) * Scale;
		return bodyPosition + surface.Tangent * along + surface.TowardSurface * 1.5f * Scale;
	}

	void PlantAllFeet()
	{
		for (var i = 0; i < legs.Length; i++)
		{
			var desired = DesiredFoot(i);
			legs[i].Foot = FindPurchase(desired, out var purchase, out var normal) ? purchase : desired;
			legs[i].Normal = legs[i].ToNormal = normal;
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
				leg.Normal = leg.ToNormal;
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
		if (!FindPurchase(desired, out var purchase, out var normal) && !FindPurchase(DesiredFoot(pick), out purchase, out normal))
			return;
		ref var step = ref legs[pick];
		step.From = step.Foot;
		step.To = purchase;
		step.ToNormal = normal;
		step.Step = 0;
		step.Delay = (pick & 1) * 0.035f;
		step.Lift = rng.Float(0.8f, 1.35f);
	}

	Vector2 DesiredFoot(int index)
	{
		var perSide = legs.Length / 2;
		var reach = StanceInner + index % perSide * StanceSpread / Math.Max(1, perSide - 1);
		return (Vector2)Position + surface.Tangent * LegSide(index) * reach * Scale + surface.TowardSurface * 2 * Scale;
	}

	bool FindPurchase(Vector2 desired, out Vector2 purchase, out Vector2 normal)
	{
		normal = -surface.TowardSurface;
		for (var i = 0; i < 5; i++)
		{
			var spread = i switch { 1 => -0.45f, 2 => 0.45f, 3 => -0.8f, 4 => 0.8f, _ => 0 };
			var direction = (surface.TowardSurface + surface.Tangent * spread).Normalized();
			var start = desired - direction * 9 * Scale;
			var probe = new RectInt((int)MathF.Round(start.X), (int)MathF.Round(start.Y), 1, 1);
			if (Game.TryFindSurface(this, probe, direction, (int)MathF.Ceiling(20 * Scale), out var hit))
			{
				purchase = hit.Point;
				if (hit.Normal != Vector2.Zero)
					normal = Vector2.Dot(hit.Normal, surface.TowardSurface) > 0 ? -hit.Normal : hit.Normal;
				return true;
			}
		}
		purchase = default;
		return false;
	}

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
		if (state is State.Dying or State.Dormant or State.WakeUpFromDormant)
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

		// The whole rig snaps back from the blow, the head hardest; one part is knocked loose on top of that.
		var away = (bodyPosition - (Vector2)by.Position).Normalized(-surface.TowardSurface);
		bodyVelocity += away * HeadKick * Scale;
		for (var i = 0; i < parts.Length; i++)
			parts[i].Velocity += away * (IsKnee(i) ? KneeKick : ArmorKick) * Scale;
		(hitWobble, stagger) = (1, HitStagger);

		var index = rng.Int(parts.Length);
		ref var part = ref parts[index];
		var recoil = (part.Position - (Vector2)by.Position).Normalized(-surface.TowardSurface);
		part.Detached = 0.62f;
		part.Damage = Math.Min(part.Damage + 1, MaxDamage);
		part.Velocity += recoil * 95 + surface.Tangent * rng.Float(-30, 30);
		part.AngularVelocity += rng.Float(-12, 12);
		eyeHitFlash = 1;
		EmitSparks(part.Position, 12);
		if (state != State.Hunting) // a hit mid-hunt shouldn't restart the lunge
			SetState(State.Hunting);
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

		if (state == State.Dying || NeuralReactions.NeuralJostleInterval <= 0 ||
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
			if (state is State.Dying or State.Dormant)
				return 0;
			if (state == State.WakeUpFromDormant)
			{
				// Sputters on: flickering through the first half, then holding and swelling as it rises.
				var wake = WakeProgress;
				var flicker = Hash32.Mix(visualSeed ^ (uint)(stateTimer * 22)) % 3 != 0;
				return wake < 0.5f ? (flicker ? wake * 1.6f : 0) : 0.8f + wake * 0.4f;
			}
			if (state == State.Hunting)
			{
				var phase = stateTimer % HuntCycle;
				return phase < HuntWindup ? 0.45f + phase / HuntWindup * 0.75f : phase < HuntWindup + HuntBurst ? 1.15f : 0.55f;
			}
			return 0.82f + MathF.Sin((Time.SecondsF + randomTimeOffset) * 3.7f) * 0.05f;
		}
	}

	// Shadows, reflections, the normal buffer and the eye's glow all come from this one drawing. Shapes take
	// their size from Scale but their detail (rims, seams, rivets, teeth) in pixels, so a bigger crawler reads
	// as a more intricate one rather than a blown-up one.
	void IPenDrawn.Draw(Pen pen)
	{
		if (SpriteBlinkHidden && pen.Pass != PenPass.Silhouette)
			return;
		pen = pen with { Skin = Skin(pen.Pass), Material = PenMaterial.Get(GritTexture) };
		var center = bodyPosition - Position;
		var focus = Focus();
		var alive = state != State.Dying;

		// Back to front, each layer a little nearer: cables, the far legs, the armor, the near legs (over the
		// armor, under the head their hips sit inside), the body, the eye.
		DrawCables(At(pen, CableDepth), center);
		for (var i = 0; i < legs.Length; i++)
			if (!alive)
				DrawPart(At(pen, FarLegDepth), i, focus);
			else if (legNear[i] < 0.5f)
				DrawLeg(LegPen(pen, i), i, focus);
		for (var i = legs.Length; i < parts.Length; i++)
			DrawPart(At(pen, ArmorDepth), i, focus);
		if (alive)
			for (var i = 0; i < legs.Length; i++)
				if (legNear[i] >= 0.5f)
					DrawLeg(LegPen(pen, i), i, focus);

		var body = At(pen, BodyDepth);
		var radius = BodyRadius * Scale;
		body.Ball(center, radius, new Vector2(0, -BodyHeight) * Scale, Color.Lerp(new Color(0x30343d), MetalColor, 0.16f), MetalEdgeColor, round: true);
		var rivets = Math.Max(6, (int)(MathF.Tau * radius / RivetSpacing));
		for (var i = 0; i < rivets; i++)
			if (ShapeUnit(i, 30) < 0.7f)
				body.Rivet(center + Calc.AngleToVector(bodyAngle + i * MathF.Tau / rivets) * (radius - Pen.RimWidth - 1.2f), MetalEdgeColor);
		var seam = Calc.AngleToVector(bodyAngle + MathF.PI * 0.25f) * radius;
		body.Groove(center - seam, center - seam * 0.72f, 1, MetalEdgeColor);
		body.Groove(center + seam * 0.72f, center + seam, 1, MetalEdgeColor);

		if (alive)
			DrawEye(At(pen, 0), center, focus);
	}

	// Depth per layer (see Pen.Depth); the dormant giant sits deeper still, stepping forward out of the dark as
	// it wakes.
	static float CableDepth => 0.85f;
	static float FarLegDepth => 0.6f;
	static float ArmorDepth => 0.3f;
	static float BodyDepth => 0.1f;
	static float DormantDepth => 0.5f;
	static float LegSwapTime => 0.25f;

	Pen At(Pen pen, float layer) => pen with { Depth = MathF.Min(1, layer + DormantDepth * (1 - Ease.Cube.Out(WakeProgress))) };
	Pen LegPen(Pen pen, int index) => At(pen, FarLegDepth * (1 - legNear[index]));

	// Alternate legs walk on the near side of the body, the rest on the far side; which is which follows Facing.
	// The depth eases across a turn and the draw order swaps at the halfway point.
	static float NearTarget(int index, Signs facing) => ((index & 1) == 0) == (facing > 0) ? 1 : 0;

	// Pixel sizes for the greebles, independent of Scale.
	static float RivetSpacing => 4f;
	static float PanelInset => 2.5f;
	static float ChipDepth => 2f;
	static float ToothSize => 2f;
	static float ToothSpacing => 2.5f;
	static float ThornSpacing => 3.5f;
	static float SegmentLength => 3f;
	static int MaxDamage => 4;

	// 0..1: how locked on it is -- the eye irising down and the armor seams lighting through the windup and the
	// burst, easing off during the recovery.
	float Focus()
	{
		if (state == State.WakeUpFromDormant)
			return Ease.Cube.In(WakeProgress); // the seams light up last, as the eye comes fully on
		if (state != State.Hunting)
			return 0;
		var phase = stateTimer % HuntCycle;
		return phase < HuntWindup ? phase / HuntWindup
			: phase < HuntWindup + HuntBurst ? 1
			: 1 - (phase - HuntWindup - HuntBurst) / HuntRecover;
	}

	// The surface or NCA texture over the fills, laid out over the rest pose (NeuralSize at NeuralOrigin, scaled
	// like everything else so a material position is a real pixel).
	RigSkin Skin(PenPass pass)
	{
		var normals = pass == PenPass.Normals;
		if (pass is not (PenPass.Color or PenPass.Normals))
			return default;
		var (size, origin) = (((Vector2)NeuralSize * Scale).RoundToPoint2(), ((Vector2)NeuralOrigin * Scale).RoundToPoint2());
		if (SkinMode == RigSkinMode.Surface && Assets.GetSurface(SurfaceId) is { } material)
			return new(material, null, 1, normals, size, origin, Position, Time.SecondsF);
		if (SkinMode == RigSkinMode.NCA && neuralSkin != null)
			return new(null, normals ? neuralSkin.OutputNormalTexture : neuralSkin.OutputTexture,
				normals ? Neural.NeuralNormalAmount : Neural.NeuralColorAmount, normals, size, origin, Position, Time.SecondsF);
		return default;
	}

	// Cables and a red tendon strung from the body to the armor and the knees, behind everything: they flex as
	// the parts spring around, and fill the gaps between plates with machinery instead of background.
	void DrawCables(Pen pen, Vector2 center)
	{
		var sag = surface.TowardSurface * 1.5f * Scale;
		var cable = Color.Lerp(MetalEdgeColor, Color.Black, 0.35f);
		var tendon = Color.Lerp(EyeColor, Color.Black, 0.45f);
		var along = surface.Tangent * 1.5f * Scale;
		for (var i = legs.Length; i < parts.Length; i++)
		{
			var to = parts[i].Position - Position;
			pen.Cable(center + along, to + along, sag, 1, cable);
			pen.Cable(center - along, to - along, sag * 1.6f, 1, ArmorSide(i) < 0 ? tendon : cable);
		}
		if (state != State.Dying)
			for (var i = 0; i < legs.Length; i++)
				pen.Cable(center, parts[i].Position - Position, sag * 0.6f, 1, (i & 1) == 0 ? cable : tendon);
	}

	void DrawLeg(Pen pen, int index, float glow)
	{
		var hip = Hip(index) - Position;
		var knee = parts[index].Position - Position;
		var foot = legs[index].Foot - Position;
		var rest = new Vector2((index - (legs.Length - 1) * 0.5f) * 27f / (legs.Length - 1), -4) * Scale;
		pen.Tube(hip, knee, UpperLegWidth * Scale, rest, MetalColor, MetalEdgeColor, SegmentLength);
		pen.Tube(knee, foot, LowerLegWidth * Scale, rest + new Vector2(0, 6 * Scale), MetalColor, MetalEdgeColor, SegmentLength);
		pen.Ball(knee, 1.9f * Scale, rest, MetalColor, MetalEdgeColor);

		// Thorns along the upper leg's back, facing away from the surface it clings to.
		var across = (knee - hip).Normalized(Vector2.UnitY).TurnLeft();
		if (Vector2.Dot(across, surface.TowardSurface) > 0)
			across = -across;
		var edge = across * UpperLegWidth * 0.5f * Scale;
		pen.Teeth(hip + edge, knee + edge, across, ToothSize * 0.75f, ThornSpacing, MetalEdgeColor);

		DrawClaws(pen, index, knee, foot);
		DrawPart(pen, index, glow);
	}

	// Two claws per foot: splayed down the shin while the foot swings, flattening out along whatever it's
	// standing on (or about to land on) as it nears the ground, so a planted foot grips the surface's own angle.
	void DrawClaws(Pen pen, int index, Vector2 knee, Vector2 foot)
	{
		ref var leg = ref legs[index];
		var normal = leg.Step < 0.5f ? leg.Normal : leg.ToNormal;
		var lifted = leg.Step < 1 ? MathF.Sin(leg.Step * MathF.PI) * leg.Lift : 0;
		var flat = (1 - MathF.Min(1, lifted)) * (1 - MathF.Min(1, lifted));
		var down = (foot - knee).Normalized(-normal);
		var side = down.TurnLeft();
		var along = normal.TurnLeft();
		foreach (var k in (ReadOnlySpan<float>)[-1, 1])
		{
			var reach = Vector2.Lerp((down + side * k * ClawSplay).Normalized(), along * k, flat).Normalized() * ClawLength * Scale;
			var thick = reach.TurnLeft().Normalized();
			if (Vector2.Dot(thick, normal) < 0 && flat > 0.5f)
				thick = -thick; // lying flat, the claw's body sits on top of the surface rather than in it
			thick *= MathF.Max(Scale * (1 - flat * 0.5f), 1);
			pen.Facet(foot, foot + thick, foot + reach, MetalEdgeColor, thick.Normalized() * 0.5f);
		}
	}

	void DrawPart(Pen pen, int index, float glow)
	{
		var part = parts[index];
		var half = (IsKnee(index) ? KneePlate : ArmorPlate) * Scale;
		Span<Vector2> vertices = stackalloc Vector2[PlateVertexCount];
		Span<Vector2> material = stackalloc Vector2[PlateVertexCount];
		PlateAxes(index, part.Angle, half, out var x, out var y);
		PlateVertices(index, part.Position - Position, x, y, vertices);
		PlateAxes(index, 0, half, out var restX, out var restY);
		PlateVertices(index, PlateMaterialCenter(index) * Scale, restX, restY, material);

		// Every hit bites a chip out of a corner and leaves a crack running in from it.
		var center = Pen.Center(vertices);
		var materialCenter = Pen.Center(material);
		Span<int> chipped = stackalloc int[MaxDamage];
		for (var d = 0; d < part.Damage; d++)
		{
			var corner = chipped[d] = (int)(ShapeUnit(index, 40 + d) * PlateVertexCount) % PlateVertexCount;
			var bite = MathF.Min(0.4f, ChipDepth / MathF.Max(Vector2.Distance(vertices[corner], center), 0.01f));
			vertices[corner] = Vector2.Lerp(vertices[corner], center, bite);
			material[corner] = Vector2.Lerp(material[corner], materialCenter, bite);
		}

		pen.Plate(vertices, material, Color.Lerp(MetalColor, MetalEdgeColor, 0.05f + ShapeUnit(index, 8) * 0.12f), MetalEdgeColor);

		// Panel seams inset from some edges, lighting up as it locks on; rivet rows along the others.
		var seamColor = Color.Lerp(MetalEdgeColor, EyeGlowColor, glow * 0.6f);
		for (var i = 0; i < vertices.Length; i++)
		{
			var a = Inset(vertices[i], center, PanelInset);
			var b = Inset(vertices[(i + 1) % vertices.Length], center, PanelInset);
			if (ShapeUnit(index, 20 + i) < 0.5f)
			{
				pen.Groove(a, b, 1, seamColor);
				pen.GlowLine(a, b, 1, EyeGlowColor * glow);
			}
			else
				for (var at = RivetSpacing * 0.5f; at < Vector2.Distance(a, b); at += RivetSpacing)
					pen.Rivet(a + (b - a).Normalized() * at, MetalEdgeColor);
		}
		for (var d = 0; d < part.Damage; d++)
			pen.Groove(vertices[chipped[d]], Vector2.Lerp(vertices[chipped[d]], center, 0.55f), 1, Color.Lerp(MetalEdgeColor, Color.Black, 0.4f));
		if (IsKnee(index))
			return;

		// Armor: a serrated outer edge, and a bank of vents that grows with the plate.
		var outward = Vector2.Normalize(part.Position - bodyPosition + surface.Tangent * 0.01f);
		var best = 0;
		for (var i = 1; i < vertices.Length; i++)
			if (Vector2.Dot(Pen.Outward(vertices[i], vertices[(i + 1) % vertices.Length], center), outward) >
				Vector2.Dot(Pen.Outward(vertices[best], vertices[(best + 1) % vertices.Length], center), outward))
				best = i;
		var (from, to) = (vertices[best], vertices[(best + 1) % vertices.Length]);
		pen.Teeth(from, to, Pen.Outward(from, to, center), ToothSize, ToothSpacing, MetalEdgeColor);

		var (along, slat) = (x.Normalized(), y.Normalized() * MathF.Min(y.Length() * 0.45f, 3));
		var vents = Math.Clamp((int)(x.Length() / 3), 2, 6);
		for (var v = 0; v < vents; v++)
		{
			var at = center + along * (v - (vents - 1) * 0.5f) * 2 - outward * x.Length() * 0.2f;
			pen.Groove(at - slat, at + slat, 1, seamColor);
		}
	}

	static Vector2 Inset(Vector2 vertex, Vector2 center, float pixels)
		=> Vector2.Lerp(vertex, center, MathF.Min(0.6f, pixels / MathF.Max(Vector2.Distance(vertex, center), 0.01f)));

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

	Vector2 PlateMaterialCenter(int index) => IsKnee(index)
		? new Vector2((index - (legs.Length - 1) * 0.5f) * 19.5f / (legs.Length - 1), -6.5f + (index & 1) * 1.5f)
		: new Vector2(ArmorSide(index) * 6, -BodyHeight);

	// A camera-iris eye: blades close down to a pinhole as it locks on, so you see it focus on you before it lunges.
	void DrawEye(Pen pen, Vector2 body, float focus)
	{
		var intensity = Math.Clamp(EyeIntensity + eyeHitFlash, 0, 1.4f);
		if (intensity <= 0)
			return;
		var center = body;
		if (Game.GetFirst<Player>() is { } player)
			center += ((Vector2)player.Position - bodyPosition).Normalized() * 1.1f * Scale;
		var strength = MathF.Min(1, intensity);
		var radius = 3.4f * Scale;
		var open = radius * (0.85f - 0.6f * focus);
		pen.Dot(center, radius, EyeColor * strength);
		pen.Dot(center - Vector2.One * 0.45f * Scale, 0.55f * Scale, Color.White * strength);
		pen.Glow(center, MathF.Max(open, 1), EyeGlowColor * strength);

		var blades = Math.Clamp((int)(radius * 1.6f), 5, 10);
		var blade = Color.Lerp(MetalEdgeColor, Color.Black, 0.25f);
		for (var i = 0; i < blades; i++)
		{
			var (a, b) = (i * MathF.Tau / blades + bodyAngle, (i + 1) * MathF.Tau / blades + bodyAngle);
			var tip = center + Calc.AngleToVector(a + 1.4f) * open;
			pen.Facet(center + Calc.AngleToVector(a) * radius, center + Calc.AngleToVector(b) * radius, tip, blade,
				-Calc.AngleToVector((a + b) * 0.5f) * 0.45f);
		}
	}

	public override void ForEachLight(Action<Vector2, float, float, float, Color> action)
	{
		var intensity = Math.Clamp(EyeIntensity + eyeHitFlash, 0, 1.3f);
		if (intensity > 0.05f)
			action(bodyPosition, EyeLightRadius * Scale, EyeLightIntensity * intensity, 7 * Scale, EyeGlowColor);
	}

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
		if (state != State.Dying)
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
