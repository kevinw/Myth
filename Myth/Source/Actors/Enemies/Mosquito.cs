using Foster.Framework;
using System.Numerics;

namespace Myth;

[GameActor(Tags = ActorTag.Enemy | ActorTag.Flying)]
public class Mosquito : Actor
{
	static float FireRange => 160f.T();
	static float FireIntervalMin => 2.5f.T();
	static float FireIntervalMax => 4f.T();
	static float FireProjectileSpeed => 36f.T();
	static float FireArcRadians => 1.2f.T();
	static float FireStagger => 0.055f.T();
	static float FireProjectileLaunchDelay => 0.12f.T();
	static float FireProjectileWobble => 0.174999997f.T();
	static int FireProjectileCount => 5;
	public override float HitKnockback => 20f;

	int Health = 2;
	private SoundInstance? buzz;
	float fireTimer;
	float pauseAfterFiring;
	readonly Coroutine shots = new();

	public Mosquito()
	{
		Hitbox = new(new RectInt(-5, -5, 10, 10));
		Mask = Masks.Enemy;
		Depth = -5;
		CollidesWithSolids = false;
		Play("fly");
	}

	public override void Added()
	{
		fireTimer = Game.rng.Float(FireIntervalMin, FireIntervalMax);

		if (!Game.AudioEventsEnabled) return;
		buzz = Game.CreateAudioEvent("fly/idle", Position, start: true);
	}

	public override void Update()
	{
		base.Update();

		if (Game.GetFirst(Masks.Player) is {} player)
		{
			var toPlayer = (Vector2)(player.Position - Position);
			var diff = toPlayer.X;
			var dist = MathF.Abs(diff);
			var sign = MathF.Sign(diff);

			if (dist < 100)
				Velocity.X += sign * 100 * Time.Delta;
			else
				Velocity.X = Calc.Approach(Velocity.X, 0, 100 * Time.Delta);

			if (MathF.Abs(Velocity.X) > 50)
				Velocity.X = Calc.Approach(Velocity.X, MathF.Sign(Velocity.X) * 50, 800 * Time.Delta);

			fireTimer -= Time.Delta;
			if (fireTimer <= 0 && !shots.Active && toPlayer.LengthSquared() < FireRange * FireRange)
			{
				Squish.X = 1.18f;
				shots.Start(BulletPattern.FireArcStaggered(
					Game,
					Position,
					toPlayer,
					FireProjectileCount,
					FireArcRadians,
					FireProjectileSpeed,
					FireStagger,
					static bullet =>
					{
						bullet.LaunchDelay = FireProjectileLaunchDelay;
						bullet.WobbleAmplitude = FireProjectileWobble;
					}));
				AudioEvent("bubble");
				pauseAfterFiring = 0.8f;
				fireTimer = Game.rng.Float(FireIntervalMin, FireIntervalMax);
			}
		}

		shots.Update(Time.Delta);
		Velocity.Y = MathF.Sin(Timer * 4) * 10;
		var relativeTo = Game.GetFirst(Masks.Player)?.Position;
		Game.SetAudioEventPosition(buzz, Position, relativeTo);

		if (pauseAfterFiring > 0)
		{
			pauseAfterFiring = Calc.Approach(pauseAfterFiring, 0, Time.Delta);
			Velocity = Vector2.Zero;
		}
	}

	internal override void OnWasHit(HitInfo hitInfo)
	{
		Health--;

		Game.HitStun(0.1f);

		if (Health <= 0)
		{
			AudioEvent("fly/die");
			Game.Create<Pop>(Position);
			Corpse.Spawn(this, hitInfo, pieces: 2);
			StopBuzz(true);
			Game.Destroy(this);

		}
		else
		{
			AudioEvent("fly/hurt");
			var sign = MathF.Sign(Position.X - hitInfo.By.Position.X);
			Velocity.X = sign * 140;
		}
	}

	public override void Destroyed()
	{
		shots.Stop();
		StopBuzz(true);
	}

	private void StopBuzz(bool immediate)
	{
		if (buzz == null)
			return;

		buzz.Stop(immediate);
		buzz.Release();
		buzz = null;
	}
}
