using System.Numerics;
using Foster.Framework;

namespace Myth;

[GameActor(HideFromPalette = true)]
public class PatternProjectile : Actor
{
	const int CircleSegments = 14;

	[I(1, 12)] public float Radius = 3;
	public Color Color = new(0x88e7ff);
	public Color EdgeColor = new(0x264a64);
	public ContentRef<SurfaceContent> SurfaceId = SurfaceContent.BouncerDefault;
	public Vector2 Direction = Vector2.UnitX;
	[I(5, 160)] public float Speed = 36;
	[I(0, 1)] public float StartSpeedScale = 0.25f;
	[I(0, 2)] public float SpeedRampTime = 0.45f;
	[I(0, 2)] public float LaunchDelay = 0.12f;
	[I(0, 12)] public float WobbleAmplitude = 2.5f;
	[I(0, 6)] public float WobbleFrequency = 1.6f;
	[I(0, MathF.Tau)] public float WobblePhase;
	[I(0.5f, 12)] public float LifeTime = 4;
	[I(0, 2)] public float SurfaceWorldMotionScale = 0.5f;
	[I(0, 1)] public float Glow = 0.75f; // how much of it glows unlit, over its lit color
	public bool PopOnDestroy = true;
	public bool Returnable; // a player's attack bats it back, at enemies
	public bool Returned { get; private set; }
	static float ReturnSpeed => 170f;
	static float ReturnLifeTime => 3f;
	public override string HitSoundType => "";
	public override bool CastsSpriteShadow => false;
	public override bool CastsReflection => false;

	float flightTimer;
	int sparkEffect = -1;

	public PatternProjectile()
	{
		Mask = Masks.Enemy;
		Depth = -5;
		Sprite = null;
	}

	public override void Added()
	{
		base.Added();
		var size = 2 * (int)MathF.Ceiling(Radius + 1);
		Hitbox = new(RectInt.Centered(size, size));
		sparkEffect = AcquireParticleEffect("sparks");
		DrawScale = 0.01f;
	}

	public override void Update()
	{
		if (Direction.LengthSquared() <= 0.0001f)
			Direction = Vector2.UnitX;
		else
			Direction = Vector2.Normalize(Direction);

		if (LaunchDelay > 0)
		{
			LaunchDelay -= Time.Delta;
			Velocity = Vector2.Zero;
		}
		else
		{
			flightTimer += Time.Delta;
			var speedScale = SpeedRampTime <= 0 ? 1 : float.Lerp(StartSpeedScale, 1, Math.Clamp(flightTimer / SpeedRampTime, 0, 1));
			var side = new Vector2(-Direction.Y, Direction.X);
			var wobble = MathF.Cos(flightTimer * WobbleFrequency * MathF.Tau + WobblePhase) * WobbleAmplitude * WobbleFrequency * MathF.Tau;
			Velocity = Direction * Speed * speedScale + side * wobble;
		}

		base.Update();
		if (Returned && Game.OverlapsFirstCombat(Hitbox.Rect + Position, Masks.Enemy) is { } enemy)
			Hit(enemy, Hitbox);
		if (Timer > LifeTime)
			Game.Destroy(this);
		DrawScale = Calc.Approach(DrawScale, 1.0f, Time.Delta * 1.5f);
	}

	public override void OnCollideX() => Pop();
	public override void OnCollideY() => Pop();
	public override void OnPerformHit(Actor hitting) => Pop();
	internal override void OnWasHit(HitInfo hitInfo)
	{
		if (!Returnable || Returned || hitInfo.By is not Player)
		{
			Pop();
			return;
		}
		var away = (Vector2)(Position - hitInfo.By.Position) - hitInfo.By.Hitbox.Rect.Center;
		var angle = MathF.Round(Calc.Angle(away) / (MathF.PI / 4)) * (MathF.PI / 4);
		Direction = Calc.AngleToVector(angle);
		Returned = true;
		Mask = Masks.None;
		Speed = ReturnSpeed;
		WobbleAmplitude = 0;
		LaunchDelay = 0;
		flightTimer = 0;
		LifeTime = Timer + ReturnLifeTime;
		AudioEvent("bubble");
	}

	void Pop()
	{
		if (Game.destroying.Contains(this))
			return;
		if (sparkEffect >= 0)
		{
			var impact = Velocity.LengthSquared() > 0.01f ? Velocity : Direction;
			Game.Particles?.Emit(sparkEffect, Position, 10, Calc.Angle(-impact), 0.75f, MathF.PI * 0.65f);
		}
		Game.Destroy(this);
		if (PopOnDestroy)
			Game.Create<Pop>(Position);
		AudioEvent("bubble");
	}

	public override void Destroyed() => ReleaseParticleEffect(ref sparkEffect);

	public override void Render(Batcher batcher)
	{
		batcher.Circle(Vector2.Zero, DrawRadius + 1, CircleSegments, EdgeColor);
		if (Assets.GetSurface(SurfaceId) is { } surface)
			RenderSphere(batcher, surface, 1);
		else
			batcher.Circle(Vector2.Zero, DrawRadius, CircleSegments, Color);
	}

	float DrawRadius => Radius * DrawScale;
	float DrawScale = 1.0f;

	public override void RenderEmissive(Batcher batcher)
	{
		if (Assets.GetSurface(SurfaceId) is { } surface)
			RenderSphere(batcher, surface, Glow);
		else
			batcher.Circle(Vector2.Zero, DrawRadius, CircleSegments, Color);
	}

	public override void RenderNormals(Batcher batcher) => RigDraw.DrawSphereNormals(batcher, Vector2.Zero, DrawRadius + 1, CircleSegments, 1, 0, 0);

	// The surface wrapped onto a ball: rings whose texture runs the arc length over the curve, bunching at the rim.
	void RenderSphere(Batcher batcher, Surface surface, float opacity)
	{
		const int Rings = 3;
		var launch = Math.Clamp(flightTimer / 0.18f, 0, 1);
		var stretch = flightTimer <= 0 ? 1 : float.Lerp(1.55f, 1, launch);
		var squash = flightTimer <= 0 ? 1 : float.Lerp(0.68f, 1, launch);
		var forward = Direction.LengthSquared() <= 0.0001f ? Vector2.UnitX : Vector2.Normalize(Direction);
		var side = new Vector2(-forward.Y, forward.X);
		var worldOrigin = (Vector2)Position * SurfaceWorldMotionScale;
		var radius = DrawRadius;
		Span<Surface.Vertex> mesh = stackalloc Surface.Vertex[CircleSegments * (Rings * 2 - 1) * 3];
		var n = 0;
		for (var ring = 0; ring < Rings; ring++)
			for (var i = 0; i < CircleSegments; i++)
			{
				var a0 = Point(ring, i);
				var b0 = Point(ring + 1, i);
				var b1 = Point(ring + 1, i + 1);
				mesh[n++] = a0; mesh[n++] = b0; mesh[n++] = b1;
				if (ring == 0)
					continue;
				mesh[n++] = a0; mesh[n++] = b1; mesh[n++] = Point(ring, i + 1);
			}
		surface.Render(batcher, mesh, Time.SecondsF, opacity);

		Surface.Vertex Point(int ring, int index)
		{
			var angle = index * MathF.Tau / CircleSegments;
			var s = ring / (float)Rings;
			var around = forward * MathF.Cos(angle) + side * MathF.Sin(angle);
			var pos = forward * (MathF.Cos(angle) * stretch) + side * (MathF.Sin(angle) * squash);
			return new(pos * (radius * s), worldOrigin + around * (radius * MathF.Asin(s)));
		}
	}

	public override void RenderShadow(Batcher batcher, Color color) { }
	public override void RenderReflection(Batcher batcher, Color color) { }
}

public static class BulletPattern
{
	public static void FireArc(Game game, Point2 origin, Vector2 direction, int count, float arcRadians, float speed, Action<PatternProjectile>? init = null)
	{
		FireArcShotData(direction, count, arcRadians, out var aim, out var start, out var step);
		for (var i = 0; i < count; i++)
			FireProjectile(game, origin, Rotate(aim, start + step * i), speed, init);
	}

	public static IEnumerator<float> FireArcStaggered(Game game, Point2 origin, Vector2 direction, int count, float arcRadians, float speed, float staggerSeconds, Action<PatternProjectile>? init = null)
	{
		FireArcShotData(direction, count, arcRadians, out var aim, out var start, out var step);
		for (var i = 0; i < count; i++)
		{
			FireProjectile(game, origin, Rotate(aim, start + step * i), speed, init);
			if (staggerSeconds > 0 && i + 1 < count)
				yield return staggerSeconds;
		}
	}

	static void FireArcShotData(Vector2 direction, int count, float arcRadians, out Vector2 aim, out float start, out float step)
	{
		if (count <= 0)
		{
			aim = Vector2.UnitX;
			start = step = 0;
			return;
		}
		if (direction.LengthSquared() <= 0.0001f)
			direction = Vector2.UnitX;
		aim = Vector2.Normalize(direction);

		start = count == 1 ? 0 : -arcRadians * 0.5f;
		step = count == 1 ? 0 : arcRadians / (count - 1);
	}

	static void FireProjectile(Game game, Point2 origin, Vector2 direction, float speed, Action<PatternProjectile>? init)
	{
		if (direction.LengthSquared() <= 0.0001f)
			direction = Vector2.UnitX;
		else
			direction = Vector2.Normalize(direction);
		game.Create<PatternProjectile>(origin, bullet =>
		{
			bullet.Direction = direction;
			bullet.Speed = speed;
			bullet.WobblePhase = origin.X * 0.17f + origin.Y * 0.11f;
			init?.Invoke(bullet);
		});
	}

	static Vector2 Rotate(Vector2 v, float radians)
	{
		var sin = MathF.Sin(radians);
		var cos = MathF.Cos(radians);
		return new Vector2(v.X * cos - v.Y * sin, v.X * sin + v.Y * cos);
	}
}
