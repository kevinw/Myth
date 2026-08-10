using System.Numerics;
using FosterGames;

namespace Myth;
using Foster.Framework;

[GameActor(Tags = ActorTag.Enemy | ActorTag.Flying)]
public class Floater : Actor
{
	public float LaserWarmup = 0.2f;
	public float LaserGrowTime = 0.12f;
	public float LaserFadeTime = 0.16f;
	public float LaserRotationSpeed = 0.10f;
	public float LaserRange = 260;
	public float LaserThickness = 3;
	static float LaserStartPadding => 5;
	static float LaserEndPadding => 2;
	public float LaserPopInterval = 0.08f;
	public float LaserLightRadius = 22;
	public float LaserLightIntensity = 1.2f;
	public float LaserLightHeight = 8;
	public Color LaserLightColor = 0x5fcde4;
	public string? SurfaceId;

	int dir;
	float laserAngle;
	float laserFadeAngle;
	float laserFadeTimer;
	float laserPopTimer;

	enum State
	{
		Floating,
		Firing
	}

	StateMachine<State> state = new(State.Floating);

	public Floater()
	{
		Hitbox = new(new RectInt(-5, -5, 10, 10));
		CollidesWithSolids = false;
		Mask = Masks.Enemy;
		Depth = -5;
		Play("idle");
		ChangeDir();
		state.Time = rng.Float(0, 1.0f);
	}

	private static Rng rng = new();

	void ChangeDir()
	{
		dir = dir >= 0 ? -1 : 1;
		Velocity = new Vector2(0, (float)dir * 2.2f);
	}

	public override void Update()
	{
		base.Update();
		state.Update(Time.Delta);
		laserFadeTimer = Calc.Approach(laserFadeTimer, 0, Time.Delta);
		laserPopTimer = Calc.Approach(laserPopTimer, 0, Time.Delta);

		if (state.Current == State.Floating)
		{
			if (state.Time > 2.5f)
			{
				Velocity = new();
				state.Change(State.Firing);
				Play("shoot", looping: false);
			}
		}
		else if (state.Current == State.Firing)
		{
			if (state.Time > LaserWarmup)
			{
				laserAngle += LaserRotationSpeed * MathF.Tau * Time.Delta;
				UpdateLasers(LaserVisibleScale());
			}
			if (state.Time > 3.0f)
			{
				laserFadeAngle = laserAngle;
				laserFadeTimer = LaserFadeTime;
				ChangeDir();
				state.Change(State.Floating);
				Play("idle");
			}

		}
	}

	public override void Render(Batcher batcher)
	{

		if (state.Current == State.Firing && state.Time > LaserWarmup)
			RenderLasers(batcher, laserAngle, LaserVisibleScale(), 1.0f);
		else if (laserFadeTimer > 0)
			RenderLasers(batcher, laserFadeAngle, laserFadeTimer / LaserFadeTime, laserFadeTimer / LaserFadeTime);

		base.Render(batcher);
	}

	public override void RenderEmissive(Batcher batcher)
	{
		if (state.Current == State.Firing && state.Time > LaserWarmup)
			RenderLasers(batcher, laserAngle, LaserVisibleScale(), 1.0f);
		else if (laserFadeTimer > 0)
			RenderLasers(batcher, laserFadeAngle, laserFadeTimer / LaserFadeTime, laserFadeTimer / LaserFadeTime);
	}

	public override void ForEachLight(Action<Vector2, float, float, float, Color> action)
	{
		if (state.Current == State.Firing && state.Time > LaserWarmup)
			ForEachLaserEndpointLight(laserAngle, LaserVisibleScale(), 1.0f, action);
		else if (laserFadeTimer > 0)
		{
			var alpha = laserFadeTimer / LaserFadeTime;
			ForEachLaserEndpointLight(laserFadeAngle, alpha, alpha, action);
		}
	}

	private void ForEachLaserEndpointLight(float angle, float scale, float alpha, Action<Vector2, float, float, float, Color> action)
	{
		var origin = (Vector2)Position;
		for (var i = 0; i < 4; i ++)
		{
			var direction = LaserDirection(angle, i);
			var end = origin + (LaserEnd(origin, direction) - origin) * scale;
			action(end, LaserLightRadius * alpha, LaserLightIntensity * alpha, LaserLightHeight, LaserLightColor);
		}
	}

	private void RenderLasers(Batcher batcher, float angle, float scale, float alpha)
	{
		var origin = (Vector2)Position;
		var surface = Assets.GetSurface(SurfaceId);
		for (var i = 0; i < 4; i ++)
		{
			var direction = LaserDirection(angle, i);
			var start = direction * LaserStartPadding;
			var end = (LaserEnd(origin, direction) + direction * LaserEndPadding - origin) * scale;
			if (Vector2.DistanceSquared(start, end) <= 0.001f)
				continue;
			batcher.Line(start, end, LaserThickness + 2, Color.White * 0.75f * alpha);
			if (surface != null)
				RenderLaserSurface(batcher, surface, origin, start, end, direction, alpha);
			else
				batcher.Line(start, end, LaserThickness, new Color(0x5fcde4, alpha));
			batcher.Circle(end, LaserThickness * 0.5f, 12, new Color(0x5fcde4, alpha));
		}
	}

	void RenderLaserSurface(Batcher batcher, Surface surface, Vector2 origin, Vector2 start, Vector2 end, Vector2 direction, float alpha)
	{
		var side = new Vector2(-direction.Y, direction.X) * (LaserThickness * 0.5f);
		var a = start - side;
		var b = start + side;
		var c = end + side;
		var d = end - side;
		Span<Surface.Vertex> triangles = stackalloc Surface.Vertex[]
		{
			new(a, origin + a), new(b, origin + b), new(c, origin + c),
			new(a, origin + a), new(c, origin + c), new(d, origin + d)
		};
		surface.Render(batcher, triangles, Time.SecondsF, alpha);
	}

	private void UpdateLasers(float scale)
	{
		var origin = (Vector2)Position;
		var spawnPops = scale >= 1.0f && laserPopTimer <= 0;
		for (var i = 0; i < 4; i ++)
		{
			var direction = LaserDirection(laserAngle, i);
			var end = LaserEnd(origin, direction);
			HitPlayerAlongLaser(origin, direction, origin + (end - origin) * scale);
			if (spawnPops && Vector2.DistanceSquared(origin, end) < LaserRange * LaserRange)
				SpawnLaserPop(end);
		}
		if (spawnPops)
			laserPopTimer = LaserPopInterval;
	}

	private float LaserVisibleScale()
		=> Math.Clamp((state.Time - LaserWarmup) / LaserGrowTime, 0, 1);

	private Vector2 LaserDirection(float angleOffset, int index)
	{
		var angle = angleOffset + index * MathF.Tau / 4.0f;
		return new Vector2(MathF.Cos(angle), MathF.Sin(angle));
	}

	private Vector2 LaserEnd(Vector2 origin, Vector2 direction)
	{
		var step = MathF.Max(1, LaserThickness);
		var distance = 0.0f;
		var end = origin;
		while (distance < LaserRange)
		{
			distance = MathF.Min(distance + step, LaserRange);
			var position = origin + direction * distance;
			if (Game.OverlapsRoomCollision(SampleRect(position)))
				break;
			end = position;
		}

		return end;
	}

	private void HitPlayerAlongLaser(Vector2 origin, Vector2 direction, Vector2 end)
	{
		var length = Vector2.Distance(origin, end);
		var step = MathF.Max(1, LaserThickness);
		for (var distance = 0.0f; distance <= length; distance += step)
		{
			var position = origin + direction * distance;
			if (Game.OverlapsFirst(SampleRect(position), Masks.Player) is { } player)
			{
				Hit(player);
				return;
			}
		}
	}

	private RectInt SampleRect(Vector2 position)
	{
		var size = Math.Max(1, (int)MathF.Ceiling(LaserThickness));
		return new RectInt(
			(int)MathF.Floor(position.X - size * 0.5f),
			(int)MathF.Floor(position.Y - size * 0.5f),
			size,
			size);
	}

	private void SpawnLaserPop(Vector2 position)
	{
		Game.Create<Pop>((Point2)position, pop =>
		{
			pop.Velocity = new Vector2(Game.rng.Float(-12, 12), Game.rng.Float(-12, 12));
		});
	}

	public override void OnWasHit(Actor by)
	{
		Game.DestroyEnemy(this);
	}
}
