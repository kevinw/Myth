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
	}

	static float WalkSpeed => 18f;
	static float Gravity => 360f;
	static float BodySpring => 82f;
	static float BodyDamping => 10f;
	static float TurnSpeed => 5.5f;
	static float StepDistance => 7f;
	static float StepSpeed => 5.5f;
	static float StepLift => 4f;
	static float LayInterval => 3.2f;
	static float LayRange => 176f;
	static float LayDuration => 1.15f;
	static float SawReleaseTime => 0.67f;
	static float BodyRadius => 10f;

	public bool ClimbsWallsAndCeilings;
	public int Health = 5;

	readonly Leg[] legs = new Leg[8];
	readonly SurfaceFollower surface;
	Vector2 bodyPosition;
	Vector2 bodyVelocity;
	float bodyAngle;
	float layCooldown = 1.2f;
	float layTime = -1;
	float stuckTime;
	bool attached;
	bool sawReleased;

	public Spider()
	{
		Sprite = null;
		Hitbox = new(new RectInt(-9, -9, 18, 18));
		Mask = Masks.Enemy;
		Depth = -5;
		Facing = Signs.Positive;
		surface = new(this);
	}

	public override void Added()
	{
		surface.ClimbsWallsAndCeilings = ClimbsWallsAndCeilings;
		attached = surface.Attach(12);
		bodyPosition = Position;
		bodyAngle = Calc.Angle(surface.Tangent);
		PlantAllFeet();
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

		UpdateBody();
		UpdateLegs();
	}

	void UpdateSurfaceMovement()
	{
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
		if (player != null && layCooldown <= 0 && ((Vector2)(player.Position - Position)).LengthSquared() < LayRange * LayRange)
		{
			layTime = 0;
			sawReleased = false;
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
			stuckTime = 0;
	}

	void UpdateBody()
	{
		var dt = MathF.Min(Time.Delta, 0.05f);
		var target = (Vector2)Position;
		bodyVelocity += (target - bodyPosition) * BodySpring * dt;
		bodyVelocity *= MathF.Exp(-BodyDamping * dt);
		bodyPosition += bodyVelocity * dt;
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
		}
	}

	void UpdateLegs()
	{
		for (var i = 0; i < legs.Length; i++)
		{
			ref var leg = ref legs[i];
			var desired = DesiredFoot(i);
			if (leg.Step < 1)
			{
				if (leg.Delay > 0)
				{
					leg.Delay -= Time.Delta;
					continue;
				}
				leg.Step = MathF.Min(1, leg.Step + StepSpeed * Time.Delta);
				var eased = Ease.Cube.InOut(leg.Step);
				leg.Foot = Vector2.Lerp(leg.From, leg.To, eased) - surface.TowardSurface * MathF.Sin(leg.Step * MathF.PI) * StepLift;
				continue;
			}

			if ((leg.Foot - desired).LengthSquared() <= StepDistance * StepDistance || !FindPurchase(desired, out var purchase))
				continue;

			leg.From = leg.Foot;
			leg.To = purchase;
			leg.Step = 0;
			leg.Delay = i * 0.025f;
		}
	}

	Vector2 DesiredFoot(int index)
	{
		var side = index < 4 ? -1 : 1;
		var pair = index & 3;
		var along = side * (12f + pair * 2.2f);
		var down = 4f + MathF.Abs(pair - 1.5f) * 2.2f;
		return bodyPosition + surface.Tangent * along + surface.TowardSurface * down;
	}

	bool FindPurchase(Vector2 desired, out Vector2 purchase)
	{
		for (var i = 0; i < 5; i++)
		{
			var spread = i switch { 1 => -0.45f, 2 => 0.45f, 3 => -0.8f, 4 => 0.8f, _ => 0 };
			var direction = (surface.TowardSurface + surface.Tangent * spread).Normalized();
			var start = desired - direction * 9;
			var probe = new RectInt((int)MathF.Round(start.X), (int)MathF.Round(start.Y), 1, 1);
			if (Game.TryFindSurface(this, probe, direction, 20, out var hit))
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
		var spawn = Position + (direction * (BodyRadius - 4)).RoundToPoint2();
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
		DrawLegs(batcher, new Color(0x171319), new Color(0xa06078));
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
		RenderSphereNormals(batcher, BodyRadius, 24);
		batcher.PopMatrix();
	}

	public override void RenderEmissive(Batcher batcher)
	{
		if (!SpriteBlinkHidden)
			DrawBody(batcher, Color.White, emissive: true, flatTint: false);
	}

	Vector2 BodyOffset => bodyPosition - (Vector2)Position - surface.TowardSurface * LayBulge * 3;

	void DrawLegs(Batcher batcher, Color outline, Color color)
	{
		for (var i = 0; i < legs.Length; i++)
		{
			var side = i < 4 ? -1 : 1;
			var pair = i & 3;
			var hip = bodyPosition + surface.Tangent * side * (4 + pair * 0.7f) + surface.TowardSurface * (pair - 1.5f) * 1.5f;
			var foot = legs[i].Foot;
			var knee = Vector2.Lerp(hip, foot, 0.48f) - surface.TowardSurface * (5 + pair) + surface.Tangent * side * 2;
			hip -= Position;
			knee -= Position;
			foot -= Position;
			batcher.Line(hip, knee, 4, outline);
			batcher.Line(knee, foot, 4, outline);
			batcher.Line(hip, knee, 2, color);
			batcher.Line(knee, foot, 2, color);
			batcher.Circle(foot, 1.5f, 8, color);
		}
	}

	void DrawBody(Batcher batcher, Color tint, bool emissive, bool flatTint)
	{
		var bulge = LayBulge;
		var scale = new Vector2(1 - bulge * 0.12f, 1 + bulge * 0.32f);
		batcher.PushMatrix(Matrix3x2.CreateScale(scale) * Matrix3x2.CreateRotation(bodyAngle) * Matrix3x2.CreateTranslation(BodyOffset));
		if (!emissive)
		{
			batcher.Circle(Vector2.Zero, BodyRadius + 1, 24, flatTint ? tint : new Color(0x171319));
			batcher.Circle(Vector2.Zero, BodyRadius, 24, flatTint ? tint : new Color(0x573443));
			if (layTime >= 0 && !sawReleased)
				batcher.Circle(new Vector2(0, 9 + bulge * 4), 2 + bulge * 3, 16, flatTint ? tint : new Color(0xc5b7a1));
		}
		DrawEyes(batcher, emissive ? new Color(0xff485f) : new Color(0xa71932), emissive);
		batcher.PopMatrix();
	}

	static void DrawEyes(Batcher batcher, Color color, bool emissive)
	{
		var center = new Vector2(0, -2);
		for (var i = 0; i < 8; i++)
		{
			var offset = Calc.AngleToVector(i / 8f * Calc.TAU) * (i % 2 == 0 ? 4f : 3f);
			batcher.Circle(center + offset, emissive ? 1.25f : 1f, 8, color);
		}
		batcher.Circle(center, emissive ? 1.7f : 1.35f, 10, color);
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
