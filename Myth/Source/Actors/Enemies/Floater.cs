using System.Numerics;

namespace Myth;
using Foster.Framework;

[GameActor(Tags = ActorTag.Enemy | ActorTag.Flying)]
public class Floater : Actor
{
	[I(0.05f, 1.5f)] public float LaserWarmup = 0.2f;
	[I(0.02f, 0.75f)] public float LaserGrowTime = 0.12f;
	[I(0.02f, 0.75f)] public float LaserFadeTime = 0.16f;
	[I(-1, 1)] public float LaserRotationSpeed = 0.10f;
	[I(48, 480)] public float LaserRange = 260;
	[I(1, 12)] public float LaserThickness = 3;
	[I(0.02f, 0.5f)] public float LaserPopInterval = 0.08f;
	[I(4, 64)] public float LaserLightRadius = 22;
	[I(0, 3)] public float LaserLightIntensity = 1.2f;
	[I(0, 32)] public float LaserLightHeight = 8;
	public Color LaserLightColor = 0x5fcde4;
	public ContentRef<SurfaceContent> SurfaceId = new("");

	static float FireEndTime => 3.0f; // how long the lasers stay live once they're up

	int dir;
	float laserAngle;
	float laserFadeAngle;
	float laserFadeTimer;
	float laserPopTimer;
	SoundInstance? sound;
	readonly LaserRay[] lasers = [new(), new(), new(), new()];

	// Borrowing the LightningTrap's event for now, parameters and all: Warmup ramps while the
	// lasers wind up, FireDuration runs across the window they're live.
	const string SoundEvent = "lightning_trap";
	const string WarmupParameter = "Warmup";
	const string FireDurationParameter = "FireDuration";

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
		Velocity.X = Calc.Approach(Velocity.X, 0, 250 * Time.Delta);
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
				StartFireSound();
			}
		}
		else if (state.Current == State.Firing)
		{
			if (state.Time > LaserWarmup)
			{
				laserAngle += LaserRotationSpeed * MathF.Tau * Time.Delta;
				TraceLasers(laserAngle);
				UpdateLasers(LaserVisibleScale());
			}
			UpdateSound(WarmupProgress, FireProgress);
			if (state.Time > FireEndTime)
			{
				UpdateSound(1, 1);
				StopAndReleaseSound(ref sound);
				laserFadeAngle = laserAngle;
				laserFadeTimer = LaserFadeTime;
				ChangeDir();
				state.Change(State.Floating);
				Play("idle");
			}

		}
		if (state.Current != State.Firing && laserFadeTimer > 0)
			TraceLasers(laserFadeAngle);
	}

	public override void Destroyed() => StopAndReleaseSound(ref sound, true);

	float WarmupProgress => Math.Clamp(state.Time / MathF.Max(0.0001f, LaserWarmup), 0, 1);
	float FireProgress => Math.Clamp((state.Time - LaserWarmup) / MathF.Max(0.0001f, FireEndTime - LaserWarmup), 0, 1);

	void StartFireSound()
	{
		StopAndReleaseSound(ref sound, true);
		sound = Game.CreateAudioEvent(SoundEvent, Position);
		UpdateSound(0, 0);
		sound?.Start();
	}

	void UpdateSound(float warmup, float fireDuration)
	{
		Game.SetAudioEventPosition(sound, Position);
		Game.SetAudioEventParameter(sound, WarmupParameter, warmup);
		Game.SetAudioEventParameter(sound, FireDurationParameter, fireDuration);
	}

	public override void Render(Batcher batcher)
	{

		if (state.Current == State.Firing && state.Time > LaserWarmup)
			RenderLasers(batcher, LaserVisibleScale(), 1.0f);
		else if (laserFadeTimer > 0)
			RenderLasers(batcher, laserFadeTimer / LaserFadeTime, laserFadeTimer / LaserFadeTime);

		base.Render(batcher);
	}

	public override void RenderEmissive(Batcher batcher)
	{
		if (state.Current == State.Firing && state.Time > LaserWarmup)
			RenderLasers(batcher, LaserVisibleScale(), 1.0f);
		else if (laserFadeTimer > 0)
			RenderLasers(batcher, laserFadeTimer / LaserFadeTime, laserFadeTimer / LaserFadeTime);
	}

	public override void ForEachLight(Action<Vector2, float, float, float, Color> action)
	{
		if (state.Current == State.Firing && state.Time > LaserWarmup)
			ForEachLaserEndpointLight(LaserVisibleScale(), 1.0f, action);
		else if (laserFadeTimer > 0)
		{
			var alpha = laserFadeTimer / LaserFadeTime;
			ForEachLaserEndpointLight(alpha, alpha, action);
		}
	}

	private void ForEachLaserEndpointLight(float scale, float alpha, Action<Vector2, float, float, float, Color> action)
	{
		for (var i = 0; i < 4; i ++)
		{
			action(lasers[i].ScaledEnd(scale), LaserLightRadius * alpha, LaserLightIntensity * alpha,
				LaserLightHeight, LaserLightColor);
		}
	}

	private void RenderLasers(Batcher batcher, float scale, float alpha)
	{
		for (var i = 0; i < 4; i ++)
			lasers[i].Render(batcher, Vector2.Zero, scale, alpha, Time.SecondsF);
	}

	void TraceLasers(float angle)
	{
		for (var i = 0; i < lasers.Length; i++)
		{
			var laser = lasers[i];
			laser.Range = LaserRange;
			laser.Thickness = LaserThickness;
			laser.Color = LaserLightColor;
			laser.SurfaceId = SurfaceId;
			laser.Trace(Game, Position, LaserDirection(angle, i));
		}
	}

	private void UpdateLasers(float scale)
	{
		var spawnPops = scale >= 1.0f && laserPopTimer <= 0;
		for (var i = 0; i < 4; i ++)
		{
			var laser = lasers[i];
			if (laser.FirstOverlap(Game, Masks.Player, scale) is { } player)
				Hit(player);
			if (spawnPops && laser.HitObstacle)
				SpawnLaserPop(laser.End);
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

	private void SpawnLaserPop(Vector2 position)
	{
		Game.Create<Pop>((Point2)position, pop =>
		{
			pop.Velocity = new Vector2(Game.rng.Float(-12, 12), Game.rng.Float(-12, 12));
		});
	}

	static int FloaterHealth => 2;
	int health = FloaterHealth;

	public override void OnWasHit(Actor by)
	{
		health--;
		if (health <= 0)
		{
			Game.DestroyEnemy(this);
		} else {
			var sign = MathF.Sign(Position.X - by.Position.X);
			Velocity.X = sign * 100;
		}
	}
}
