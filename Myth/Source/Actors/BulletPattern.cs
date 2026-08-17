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
	public string SurfaceId = "shmorg";
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
	public bool PopOnDestroy = true;
	public override string HitSoundType => "";
	public override bool CastsSpriteShadow => false;
	public override bool CastsReflection => false;

	float flightTimer;
	int sparkEffect = -1;

	public PatternProjectile()
	{
		Hitbox = new(new RectInt(-4, -4, 8, 8));
		Mask = Masks.Enemy;
		Depth = -5;
		Sprite = null;
	}

	public override void Added()
	{
		base.Added();
		sparkEffect = Game.Particles?.AcquireEffect("sparks") ?? -1;
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
		if (Timer > LifeTime)
			Game.Destroy(this);
	}

	public override void OnCollideX() => Pop();
	public override void OnCollideY() => Pop();
	public override void OnPerformHit(Actor hitting) => Pop();
	public override void OnWasHit(Actor by) => Pop();

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

	public override void Destroyed() => Game.Particles?.ReleaseEffect(ref sparkEffect);

	public override void Render(Batcher batcher)
	{
		batcher.Circle(Vector2.Zero, Radius + 1, CircleSegments, EdgeColor);
		if (Assets.GetSurface(SurfaceId) == null)
			batcher.Circle(Vector2.Zero, Radius, CircleSegments, Color);
	}

	public override void RenderEmissive(Batcher batcher)
	{
		var launch = Math.Clamp(flightTimer / 0.18f, 0, 1);
		var stretch = flightTimer <= 0 ? 1 : float.Lerp(1.55f, 1, launch);
		var squash = flightTimer <= 0 ? 1 : float.Lerp(0.68f, 1, launch);
		if (Assets.GetSurface(SurfaceId) is { } surface)
		{
			Span<Surface.Vertex> fan = stackalloc Surface.Vertex[CircleSegments * 3];
			BuildFan(fan, Radius, stretch, squash);
			surface.Render(batcher, fan, Time.SecondsF);
		}
		else
		{
			batcher.Circle(Vector2.Zero, Radius, CircleSegments, Color);
		}
	}

	void BuildFan(Span<Surface.Vertex> fan, float radius, float stretch, float squash)
	{
		var forward = Direction.LengthSquared() <= 0.0001f ? Vector2.UnitX : Vector2.Normalize(Direction);
		var side = new Vector2(-forward.Y, forward.X);
		var worldOrigin = (Vector2)Position * SurfaceWorldMotionScale;
		for (var i = 0; i < CircleSegments; i++)
		{
			var a = PointOnCircle(i);
			var b = PointOnCircle((i + 1) % CircleSegments);
			var t = i * 3;
			fan[t + 0] = new Surface.Vertex(Vector2.Zero, worldOrigin);
			fan[t + 1] = new Surface.Vertex(a, worldOrigin + a);
			fan[t + 2] = new Surface.Vertex(b, worldOrigin + b);
		}

		Vector2 PointOnCircle(int index)
		{
			var angle = index * MathF.Tau / CircleSegments;
			return forward * (MathF.Cos(angle) * radius * stretch) + side * (MathF.Sin(angle) * radius * squash);
		}
	}

	public override void RenderNormals(Batcher batcher) { }
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
