using System.Numerics;
using Foster.Framework;

namespace Myth;

[GameActor(Sprite = "saw", Tags = ActorTag.Enemy)]
public class MagneticCrawler : Actor
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
	}

	public Point2 InitialSurfaceDirection = Point2.Down;
	public ContentRef<SurfaceContent> SurfaceId = new("");
	public bool RequiresMetal = true;
	public bool ClimbsWallsAndCeilings = true;
	[I(1, 12)] public int Health = 4;
	[I(0.5f, 2)] public float Scale = 1;
	[I(112, 180)] public float WakeRange = 136;
	[I(48, 112)] public float NoticeRange = 104;
	[I(180, 320)] public float LoseRange = 208;
	[I(5, 32)] public float PatrolSpeed = 15;
	[I(32, 100)] public float HuntSpeed = 48;
	public Color MetalColor = new(0x858d9a);
	public Color MetalEdgeColor = new(0x303743);
	public Color EyeColor = new(0xb20f24);
	public Color EyeGlowColor = new(0xff1838);
	[I(4, 64)] public float EyeLightRadius = 28;
	[I(0, 2)] public float EyeLightIntensity = 0.75f;

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

	readonly SurfaceFollower surface;
	readonly Part[] parts = new Part[6];
	readonly Leg[] legs = new Leg[4];
	Rng rng = new();
	State state;
	float stateTimer;
	float lostPlayerTimer;
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
		var half = Math.Max(4, (int)MathF.Round(6 * Scale));
		Hitbox = new(new RectInt(-half, -half, half * 2, half * 2));
		surface.ProbeRect = Hitbox.Rect;
		surface.ClimbsWallsAndCeilings = ClimbsWallsAndCeilings;
		surface.TowardSurface = InitialSurfaceDirection == Point2.Zero ? Vector2.UnitY : InitialSurfaceDirection.Normalized();
		attached = surface.Attach((int)MathF.Ceiling(18 * Scale));
		bodyAngle = Calc.Angle(surface.Tangent);
		bodyPosition = DormantBodyPosition();
		InitializeParts();
		PlantAllFeet();
		sparkEffect = Game.Particles?.AcquireEffect("sparks") ?? -1;
		SetState(State.Dormant);
	}

	public override void Destroyed() => Game.Particles?.ReleaseEffect(ref sparkEffect);

	public override void Update()
	{
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
		bodyPosition = DormantBodyPosition();
		if (player == null || !attached || Vector2.DistanceSquared(player.Position, Position) > WakeRange * WakeRange)
			return;
		if (RequiresMetal && !IsOnMetal())
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
		if (player == null || Vector2.DistanceSquared(player.Position, bodyPosition) > LoseRange * LoseRange)
			lostPlayerTimer += Time.Delta;
		else
			lostPlayerTimer = 0;
		if (lostPlayerTimer > 1.1f)
		{
			SetState(State.Patrol);
			return;
		}

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
			Facing = -Facing;
			stuckTimer = 0;
		}
	}

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
		lostPlayerTimer = 0;
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

	bool IsOnMetal()
	{
		var sample = Position + (surface.TowardSurface * 7 * Scale).RoundToPoint2();
		return Game.TryGetSubstanceAtWorldPoint(sample, out var substance)
			&& string.Equals(substance, Factory.MetalSubstance, StringComparison.OrdinalIgnoreCase);
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

		Health--;
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
			DrawPart(batcher, i, tint, shadow);

		if (assembly > 0.68f)
		{
			var center = bodyPosition - Position;
			batcher.Circle(center, 6.2f * Scale, 18, shadow ? tint : MetalEdgeColor);
			batcher.Circle(center, 4.8f * Scale, 18, shadow ? tint : new Color(0x30343d));
			if (!shadow && state != State.Dying)
				DrawEye(batcher, emissive: false);
		}
	}

	void DrawPart(Batcher batcher, int index, Color tint, bool shadow)
	{
		var part = parts[index];
		var half = index < 4 ? new Vector2(3.8f, 2.2f) : new Vector2(5.2f, 3.4f);
		half *= Scale;
		Span<Vector2> corners = stackalloc Vector2[4];
		PartCorners(part, half, corners, world: false);

		if (shadow)
		{
			batcher.Quad(corners[0], corners[1], corners[2], corners[3], tint);
			return;
		}

		var final = AssemblyProgress;
		if (state == State.Assembling)
			final = Math.Clamp((AssemblyProgress - part.Phase) / MathF.Max(0.01f, 1 - part.Phase), 0, 1);
		if (Assets.GetSurface(SurfaceId) is { } sourceSurface && final < 1)
		{
			Span<Vector2> world = stackalloc Vector2[4];
			PartCorners(part, half, world, world: true);
			Span<Surface.Vertex> triangles = stackalloc Surface.Vertex[]
			{
				new(corners[0], world[0]), new(corners[1], world[1]), new(corners[2], world[2]),
				new(corners[0], world[0]), new(corners[2], world[2]), new(corners[3], world[3])
			};
			sourceSurface.Render(batcher, triangles, Time.SecondsF, 1 - final);
		}

		var fill = MetalColor * MathF.Max(final, string.IsNullOrEmpty(SurfaceId.Name) ? 0.28f : 0);
		batcher.Quad(corners[0], corners[1], corners[2], corners[3], fill);
		batcher.Line(corners[0], corners[1], 2, MetalEdgeColor * MathF.Max(final, 0.25f));
		batcher.Line(corners[1], corners[2], 2, MetalEdgeColor * MathF.Max(final, 0.25f));
		batcher.Line(corners[2], corners[3], 2, MetalEdgeColor * MathF.Max(final, 0.25f));
		batcher.Line(corners[3], corners[0], 2, MetalEdgeColor * MathF.Max(final, 0.25f));
	}

	void PartCorners(Part part, Vector2 half, Span<Vector2> corners, bool world)
	{
		var center = world ? part.Position : part.Position - Position;
		var x = Calc.AngleToVector(part.Angle) * half.X;
		var y = new Vector2(-x.Y, x.X).Normalized() * half.Y;
		corners[0] = center - x - y;
		corners[1] = center + x - y;
		corners[2] = center + x + y;
		corners[3] = center - x + y;
	}

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
		Span<Vector2> corners = stackalloc Vector2[4];
		if (state != State.Dying)
			for (var i = 0; i < legs.Length; i++)
			{
				batcher.Line(Hip(i) - Position, parts[i].Position - Position, 3.5f * Scale, flat);
				batcher.Line(parts[i].Position - Position, legs[i].Foot - Position, 3.5f * Scale, flat);
			}
		for (var i = 0; i < parts.Length; i++)
		{
			var half = (i < 4 ? new Vector2(3.8f, 2.2f) : new Vector2(5.2f, 3.4f)) * Scale;
			PartCorners(parts[i], half, corners, world: false);
			batcher.Quad(corners[0], corners[1], corners[2], corners[3], flat);
		}
		RenderSphereNormals(batcher, bodyPosition - Position, 6.2f * Scale, 18);
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

	public override Rect GetVisualBounds() => new(Position.X - 40 * Scale, Position.Y - 40 * Scale, 80 * Scale, 80 * Scale);
}
