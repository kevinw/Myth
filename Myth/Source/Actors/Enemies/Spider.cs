using System.Numerics;
using Foster.Framework;

namespace Myth;

[GameActor(Sprite = "saw", Tags = ActorTag.Enemy)]
public class Spider : Actor
{
	struct Leg
	{
		public Vector2 Foot;
		public Vector2 From;
		public Vector2 To;
		public float Step;
		public float Delay;
		public float Lift;   // per-step height multiplier, randomised for variety
	}

	static float Gravity => 360f;
	static float BodySpring => 82f;
	static float BodyDamping => 10f;
	static float TurnSpeed => 5.5f;
	static float StepDistance => 7f;
	static float StepSpeed => 5.5f;
	static float StepLift => 4f;
	static float LayDuration => 1.15f;
	static float SawReleaseTime => 0.67f;
	static float BlinkDuration => 0.13f;

	// behaviour
	public bool ClimbsWallsAndCeilings;
	[I(1, 16)] public int Health = 5;
	[I(4, 64)] public float WalkSpeed = 18f;
	public bool LaysSaws = true;
	[I(0.5f, 8)] public float LayInterval = 3.2f;
	[I(32, 320)] public float LayRange = 176f;

	// size & shape
	[I(0.5f, 2)] public float Scale = 1f;
	[I(4, 24)] public float BodyRadius = 10f;
	[I(-8, 20)] public float BodyHeight = 0f;   // how high the body rides above the surface
	[I(2, 8)] public int LegPairs = 4;
	[I(0.5f, 2)] public float LegLength = 1f;
	[I(0, 2.5f)] public float LegArch = 1f;      // how tall the leg joints hump up
	[I(1, 8)] public float LegThickness = 4f;
	[I(0, 1.5f)] public float StepAnticipation = 0.85f;   // how far ahead (× step reach) feet reach when walking
	public bool Hairy = true;

	// colours
	public Color BodyColor = 0x573443;
	public Color BodyOutlineColor = 0x171319;
	public Color LegColor = 0xa06078;
	public Color LegOutlineColor = 0x171319;
	public Color EyeColor = 0xa71932;
	public Color EyeGlowColor = 0xff485f;
	public bool Blinks = true;

	Leg[] legs = System.Array.Empty<Leg>();
	readonly SurfaceFollower surface;
	Vector2 bodyPosition;
	Vector2 bodyVelocity;
	float bodyAngle;
	float layCooldown = 1.2f;
	float layTime = -1;
	float stuckTime;
	bool attached;
	bool sawReleased;
	Rng rng = new();
	float blinkTimer;
	float blinkPhase = -1;
	float walkSign;   // -1/0/+1: tangential direction actually travelled this frame

	// reach/step distances grow with both overall size and leg length
	float StepReach => StepDistance * Scale * LegLength;
	float FootLift => StepLift * Scale;

	// The body is emergent: it rests where the legs actually are, not the other way
	// round. Its ideal spot is the target Position lifted to riding height, plus the
	// average drift of the real feet from their ideal footholds — so uneven terrain,
	// lifting legs, and an anticipatory stride all push the head around organically.
	Vector2 BodyTarget()
	{
		var ideal = (Vector2)Position - surface.TowardSurface * BodyHeight * Scale;
		if (legs.Length == 0)
			return ideal;
		var drift = Vector2.Zero;
		for (var i = 0; i < legs.Length; i++)
			drift += legs[i].Foot - DesiredFoot(i);
		return ideal + drift / legs.Length;
	}

	public Spider()
	{
		Sprite = null;
		Mask = Masks.Enemy;
		Depth = -5;
		Facing = Signs.Positive;
		surface = new(this);
	}

	public override void Added()
	{
		legs = new Leg[Math.Max(1, LegPairs) * 2];

		// the surface follower probes from a ground-locked footprint (the "target"),
		// while the Hitbox is free to ride up on the emergent body
		var half = Math.Max(1, (int)MathF.Round(BodyRadius * Scale * 0.9f));
		surface.ProbeRect = new RectInt(-half, -half, half * 2, half * 2);

		surface.ClimbsWallsAndCeilings = ClimbsWallsAndCeilings;
		attached = surface.Attach((int)MathF.Ceiling(12 * Scale));
		bodyAngle = Calc.Angle(surface.Tangent);
		blinkTimer = rng.Float(1.5f, 4f);
		PlantAllFeet();
		bodyPosition = BodyTarget();
		UpdateHitbox();
	}

	public override void Update()
	{
		surface.ClimbsWallsAndCeilings = ClimbsWallsAndCeilings;
		if (!attached)
		{
			CollidesWithSolids = true;
			Velocity.Y += Gravity * Time.Delta;
			base.Update();
			surface.TowardSurface = Vector2.UnitY;
			attached = surface.Attach(3);
			if (attached)
				Velocity = Vector2.Zero;
		}
		else
		{
			CollidesWithSolids = false;
			Velocity = Vector2.Zero;
			base.Update();
			UpdateSurfaceMovement();
		}

		UpdateLegs();
		UpdateBody();
		UpdateHitbox();
		UpdateBlink();
	}

	// the damage hitbox rides on the emergent body, offset from the ground-locked target
	void UpdateHitbox()
	{
		var half = Math.Max(1, (int)MathF.Round(BodyRadius * Scale * 0.9f));
		var c = (bodyPosition - (Vector2)Position).RoundToPoint2();
		Hitbox = new(new RectInt(c.X - half, c.Y - half, half * 2, half * 2));
	}

	void UpdateBlink()
	{
		if (!Blinks)
			return;
		if (blinkPhase >= 0)
		{
			blinkPhase += Time.Delta / BlinkDuration;
			if (blinkPhase >= 1)
			{
				blinkPhase = -1;
				// occasionally snap into a quick second blink
				blinkTimer = rng.Chance(0.25f) ? 0.12f : rng.Float(1.6f, 4.5f);
			}
		}
		else
		{
			blinkTimer -= Time.Delta;
			if (blinkTimer <= 0)
				blinkPhase = 0;
		}
	}

	// 0 = eyes open, 1 = eyes shut, smooth through the blink
	float EyeBlink => (!Blinks || blinkPhase < 0) ? 0 : MathF.Sin(blinkPhase * MathF.PI);

	void UpdateSurfaceMovement()
	{
		walkSign = 0;
		var player = Game.GetFirst<Player>();
		if (layTime >= 0)
		{
			var previous = layTime;
			layTime += Time.Delta;
			if (!sawReleased && previous < SawReleaseTime && layTime >= SawReleaseTime)
				ReleaseSaw(player);
			if (layTime >= LayDuration)
			{
				layTime = -1;
				layCooldown = LayInterval;
			}
			return;
		}

		layCooldown = Calc.Approach(layCooldown, 0, Time.Delta);
		if (LaysSaws && player != null && layCooldown <= 0 && ((Vector2)(player.Position - Position)).LengthSquared() < LayRange * LayRange)
		{
			layTime = 0;
			sawReleased = false;
			return;
		}

		// turn back at the room's edge instead of wrapping down its outer wall
		var stepDir = surface.Tangent * (int)Facing;
		var lookahead = (stepDir * MathF.Max(1f, WalkSpeed * Time.Delta + 1f)).RoundToPoint2();
		if (lookahead == Point2.Zero)
			lookahead = new Point2(Math.Sign(stepDir.X), Math.Sign(stepDir.Y));
		if (Game.OverlapsEnemyRoomBoundary(this, lookahead))
		{
			Facing = -Facing;
			stuckTime = 0;
			return;
		}

		var before = Position;
		surface.Follow((int)Facing, WalkSpeed * Time.Delta);
		if (Position == before)
		{
			stuckTime += Time.Delta;
			if (stuckTime > 0.2f)
			{
				Facing = -Facing;
				stuckTime = 0;
			}
		}
		else
		{
			stuckTime = 0;
			walkSign = (int)Facing;
		}
	}

	void UpdateBody()
	{
		var dt = MathF.Min(Time.Delta, 0.05f);
		var target = (Vector2)Position - surface.TowardSurface * BodyHeight * Scale;
		Spring.Step(ref bodyPosition, ref bodyVelocity, target, BodySpring, BodyDamping, dt);
		bodyAngle = Calc.AngleApproach(bodyAngle, Calc.Angle(surface.Tangent), TurnSpeed * dt);
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
		// advance any legs that are mid-swing, counting how many are still airborne
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
			leg.Foot = Vector2.Lerp(leg.From, leg.To, eased) - surface.TowardSurface * MathF.Sin(leg.Step * MathF.PI) * FootLift * leg.Lift;
			if (leg.Step < 1)
				lifting++;
		}

		// only a few legs swing at once; the rest stay planted for a believable gait
		var maxLifting = Math.Max(2, legs.Length / 2);
		var slots = maxLifting - lifting;

		// reach the next foothold AHEAD of the body in the travel direction so the
		// leading legs anticipate the walk instead of being dragged along behind it
		var lead = MathF.Min(0.95f, StepAnticipation);
		var anticipation = surface.Tangent * (walkSign * lead * StepReach);
		var threshold = StepReach * StepReach;
		var emergency = threshold * (1.7f * 1.7f);

		// each pass steps the single most-overdue leg; extra passes are allowed until
		// the swing budget runs out, but a critically stretched leg always gets to step
		for (var started = 0; started < legs.Length; started++)
		{
			var pick = -1;
			var pickDrift = threshold;
			for (var i = 0; i < legs.Length; i++)
			{
				if (legs[i].Step < 1)
					continue;
				var drift = (legs[i].Foot - DesiredFoot(i)).LengthSquared();
				if (drift > pickDrift)
				{
					pickDrift = drift;
					pick = i;
				}
			}

			if (pick < 0)
				break;
			if (started >= slots && pickDrift < emergency)
				break;

			var neutral = DesiredFoot(pick);
			var jitter = surface.Tangent * rng.Float(-1.5f, 1.5f) * Scale;
			if (!FindPurchase(neutral + anticipation + jitter, out var purchase) && !FindPurchase(neutral, out purchase))
				break;

			ref var leg = ref legs[pick];
			leg.From = leg.Foot;
			leg.To = purchase;
			leg.Step = 0;
			leg.Delay = rng.Float(0f, 0.05f);
			leg.Lift = rng.Float(0.75f, 1.4f);
		}
	}

	Vector2 DesiredFoot(int index)
	{
		var perSide = Math.Max(1, LegPairs);
		var side = index < perSide ? -1 : 1;
		var pair = index % perSide;
		var center = (perSide - 1) * 0.5f;
		var reach = Scale * LegLength;
		var along = side * (12f + pair * 2.2f) * reach;
		var down = (4f + MathF.Abs(pair - center) * 2.2f) * reach;
		return (Vector2)Position + surface.Tangent * along + surface.TowardSurface * down;
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

	void ReleaseSaw(Player? player)
	{
		sawReleased = true;
		var direction = surface.TowardSurface;
		var spawn = Position + (direction * (BodyRadius - 4) * Scale).RoundToPoint2();
		var tangent = surface.Tangent;
		var facing = player == null || Vector2.Dot((Vector2)(player.Position - spawn), tangent) >= 0 ? Signs.Positive : Signs.Negative;
		var initialSurface = Axis(direction);
		Game.Create<SpiderSaw>(spawn, saw =>
		{
			saw.Facing = facing;
			saw.InitialSurfaceDirection = initialSurface;
		});
		bodyVelocity -= direction * 70;
		Squish = new(1.25f, 0.72f);
		Game.Shake(0.05f);
	}

	static Point2 Axis(Vector2 direction)
		=> MathF.Abs(direction.X) > MathF.Abs(direction.Y)
			? new Point2(Math.Sign(direction.X), 0)
			: new Point2(0, Math.Sign(direction.Y));

	float LayBulge
	{
		get
		{
			if (layTime < 0)
				return 0;
			var t = Math.Clamp(layTime / SawReleaseTime, 0, 1);
			return layTime < SawReleaseTime ? Ease.Back.Out(t) : 1 - Ease.Cube.Out(Math.Clamp((layTime - SawReleaseTime) / (LayDuration - SawReleaseTime), 0, 1));
		}
	}

	public override void Render(Batcher batcher)
	{
		if (SpriteBlinkHidden)
			return;
		DrawLegs(batcher, LegOutlineColor, LegColor);
		DrawBody(batcher, Color.White, emissive: false, flatTint: false);
	}

	public override void RenderShadow(Batcher batcher, Color color)
	{
		if (SpriteBlinkHidden)
			return;
		DrawLegs(batcher, color, color);
		DrawBody(batcher, color, emissive: false, flatTint: true);
	}

	public override void RenderNormals(Batcher batcher)
	{
		if (SpriteBlinkHidden)
			return;
		DrawLegs(batcher, new Color(0x8080ff), new Color(0x8080ff));
		var offset = BodyOffset;
		batcher.PushMatrix(Matrix3x2.CreateTranslation(offset));
		RenderSphereNormals(batcher, BodyRadius * Scale, 24);
		batcher.PopMatrix();
	}

	public override void RenderEmissive(Batcher batcher)
	{
		if (!SpriteBlinkHidden)
			DrawBody(batcher, Color.White, emissive: true, flatTint: false);
	}

	Vector2 BodyOffset => bodyPosition - (Vector2)Position - surface.TowardSurface * LayBulge * 3 * Scale;

	void DrawLegs(Batcher batcher, Color outline, Color color)
	{
		var clock = Time.SecondsF;
		var perSide = Math.Max(1, LegPairs);
		var center = (perSide - 1) * 0.5f;
		var thick = MathF.Max(1.5f, LegThickness * Scale);
		var inner = MathF.Max(1f, (LegThickness - 2) * Scale);
		for (var i = 0; i < legs.Length; i++)
		{
			var side = i < perSide ? -1 : 1;
			var pair = i % perSide;
			var hip = bodyPosition + surface.Tangent * side * (4 + pair * 0.7f) * Scale + surface.TowardSurface * (pair - center) * 1.5f * Scale;
			var foot = legs[i].Foot;
			var arch = (5 + pair) * LegArch * ArchFor(i) * Scale;
			var knee = Vector2.Lerp(hip, foot, 0.48f) - surface.TowardSurface * arch + surface.Tangent * side * 2 * Scale;
			hip -= Position;
			knee -= Position;
			foot -= Position;
			batcher.Line(hip, knee, thick, outline);
			batcher.Line(knee, foot, thick, outline);

			// spiky hairs bristling off each segment, faintly twitching
			if (Hairy)
			{
				var sway = MathF.Sin(clock * 3.2f + i * 1.3f) * 0.14f;
				DrawBristles(batcher, hip, knee, side, 3, 2.6f * Scale, sway, outline);
				DrawBristles(batcher, knee, foot, side, 3, 2.0f * Scale, sway, outline);
			}

			batcher.Line(hip, knee, inner, color);
			batcher.Line(knee, foot, inner, color);

			DrawClaw(batcher, knee, foot, Scale, outline, color);
		}
	}

	// short bristles rooted along a→b, swept back toward the hip and out to the given side
	static void DrawBristles(Batcher batcher, Vector2 a, Vector2 b, int side, int count, float length, float sway, Color color)
	{
		var dir = b - a;
		var len = dir.Length();
		if (len < 0.001f)
			return;
		dir /= len;
		var normal = new Vector2(-dir.Y, dir.X) * side;
		var outward = Rotate((normal - dir * 0.5f).Normalized(normal), sway);
		for (var i = 0; i < count; i++)
		{
			var root = Vector2.Lerp(a, b, (i + 0.5f) / count);
			batcher.Line(root, root + outward * length * (1 - i * 0.12f), 1, color);
		}
	}

	// two little talons splaying off the tip of the leg
	static void DrawClaw(Batcher batcher, Vector2 knee, Vector2 foot, float scale, Color outline, Color color)
	{
		var dir = (foot - knee).Normalized(Vector2.UnitY);
		var tipA = foot + Rotate(dir, 0.6f) * 3.4f * scale;
		var tipB = foot + Rotate(dir, -0.55f) * 2.5f * scale;
		batcher.Line(foot, tipA, MathF.Max(1, 2 * scale), outline);
		batcher.Line(foot, tipB, MathF.Max(1, 2 * scale), outline);
		batcher.Line(foot, tipA, 1, color);
		batcher.Line(foot, tipB, 1, color);
		batcher.Circle(foot, 1.6f * scale, 8, outline);
		batcher.Circle(foot, 0.9f * scale, 6, color);
	}

	static Vector2 Rotate(Vector2 v, float radians)
	{
		var c = MathF.Cos(radians);
		var s = MathF.Sin(radians);
		return new Vector2(v.X * c - v.Y * s, v.X * s + v.Y * c);
	}

	// stable per-leg multiplier (~0.75..1.65) so some legs hump up higher than others
	static float ArchFor(int index) => 0.75f + MathF.Abs(MathF.Sin(index * 2.399f)) * 0.9f;

	void DrawBody(Batcher batcher, Color tint, bool emissive, bool flatTint)
	{
		var bulge = LayBulge;
		var scale = new Vector2(1 - bulge * 0.12f, 1 + bulge * 0.32f) * Scale;
		batcher.PushMatrix(Matrix3x2.CreateScale(scale) * Matrix3x2.CreateRotation(bodyAngle) * Matrix3x2.CreateTranslation(BodyOffset));
		if (!emissive)
		{
			if (Hairy)
				DrawBodyBristles(batcher, BodyRadius, Time.SecondsF, flatTint ? tint : BodyOutlineColor);
			batcher.Circle(Vector2.Zero, BodyRadius + 1, 24, flatTint ? tint : BodyOutlineColor);
			batcher.Circle(Vector2.Zero, BodyRadius, 24, flatTint ? tint : BodyColor);
			if (layTime >= 0 && !sawReleased)
				batcher.Circle(new Vector2(0, 9 + bulge * 4), 2 + bulge * 3, 16, flatTint ? tint : new Color(0xc5b7a1));
		}
		var eye = emissive ? EyeGlowColor : EyeColor;
		DrawEyes(batcher, eye, Color.Lerp(eye, Color.White, 0.55f), emissive, EyeBlink);
		batcher.PopMatrix();
	}

	// coarse fuzz radiating off the abdomen, drawn under the body so only the tips show
	static void DrawBodyBristles(Batcher batcher, float radius, float clock, Color color)
	{
		const int count = 20;
		for (var i = 0; i < count; i++)
		{
			var angle = i / (float)count * Calc.TAU + MathF.Sin(i * 12.9f) * 0.15f;
			var dir = Calc.AngleToVector(angle);
			var length = 3.2f + MathF.Abs(MathF.Sin(i * 7.1f)) * 2.4f;
			var swept = Rotate(dir, MathF.Sin(clock * 2.4f + i * 0.7f) * 0.1f);
			var root = dir * (radius - 1.5f);
			batcher.Line(root, root + swept * length, 1, color);
		}
	}

	// jumping-spider layout: two big forward eyes plus flanking clusters, all shuttered by the blink
	static readonly (Vector2 Pos, float Size)[] EyeLayout =
	{
		(new Vector2(-1.8f, -3.6f), 2.0f),
		(new Vector2( 1.8f, -3.6f), 2.0f),
		(new Vector2(-4.1f, -4.5f), 1.15f),
		(new Vector2( 4.1f, -4.5f), 1.15f),
		(new Vector2(-5.6f, -2.5f), 0.95f),
		(new Vector2( 5.6f, -2.5f), 0.95f),
		(new Vector2(-2.9f, -1.1f), 0.85f),
		(new Vector2( 2.9f, -1.1f), 0.85f),
	};

	static void DrawEyes(Batcher batcher, Color color, Color glint, bool emissive, float blink)
	{
		var pivot = new Vector2(0, -3.1f);
		var open = 1 - blink * 0.92f;
		batcher.PushMatrix(Matrix3x2.CreateTranslation(-pivot) * Matrix3x2.CreateScale(1, open) * Matrix3x2.CreateTranslation(pivot));

		if (!emissive)
		{
			// dark sockets sink the principal eyes into the face
			var socket = Color.Lerp(color, Color.Black, 0.72f);
			batcher.Circle(EyeLayout[0].Pos, EyeLayout[0].Size + 0.9f, 12, socket);
			batcher.Circle(EyeLayout[1].Pos, EyeLayout[1].Size + 0.9f, 12, socket);
		}

		foreach (var (pos, size) in EyeLayout)
			batcher.Circle(pos, emissive ? size + 0.3f : size, 10, color);

		// wet glints on the big front eyes, gone while the lids are down
		if (blink < 0.5f)
		{
			batcher.Circle(EyeLayout[0].Pos + new Vector2(-0.6f, -0.7f), emissive ? 0.9f : 0.7f, 6, glint);
			batcher.Circle(EyeLayout[1].Pos + new Vector2(-0.6f, -0.7f), emissive ? 0.9f : 0.7f, 6, glint);
		}

		batcher.PopMatrix();
	}

	static void RenderSphereNormals(Batcher batcher, float radius, int steps)
	{
		var faceViewer = new Color(0x8080ff);
		for (var i = 0; i < steps; i++)
		{
			var d0 = Calc.AngleToVector(i / (float)steps * Calc.TAU);
			var d1 = Calc.AngleToVector((i + 1) / (float)steps * Calc.TAU);
			batcher.Triangle(Vector2.Zero, d0 * radius, d1 * radius, faceViewer, RimNormalColor(d0), RimNormalColor(d1));
		}
	}

	static Color RimNormalColor(Vector2 direction)
		=> new(direction.X * 0.5f + 0.5f, direction.Y * 0.5f + 0.5f, 0.5f, 1);

	public override void OnWasHit(Actor by)
	{
		Health--;
		if (Health <= 0)
		{
			for (var i = 0; i < 4; i++)
				Game.Create<Pop>(Position + new Point2((i & 1) * 8 - 4, i / 2 * 8 - 6));
			Game.HitStun(0.25f);
			Game.DestroyForThisSession(this);
			return;
		}

		var recoil = ((Vector2)(Position - by.Position)).Normalized(-surface.TowardSurface);
		bodyVelocity += recoil * 90;
		layCooldown = MathF.Max(layCooldown, 0.6f);
	}
}
