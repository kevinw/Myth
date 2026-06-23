using System.Numerics;
using Foster.Framework;

namespace Myth;

[GameActor(Sprite = "spitter", Tags = ActorTag.Enemy)]
public class Watcher : Actor
{
	private enum States
	{
		Sleeping,
		Watching,
		Charging,
		Recovering
	}

	public int Health = 2;
	public float WakeRange = 88;
	public float ShootRange = 120;
	public float ChargeTime = 0.65f;
	public float RecoverTime = 1.20f;

	private States state = States.Sleeping;
	private float stateTimer;

	public Watcher()
	{
		Sprite = Assets.GetSprite("spitter");
		Hitbox = new(new RectInt(-6, -12, 12, 12));
		Mask = Masks.Enemy;
		Depth = -5;
		Play("idle");
	}

	public override void Update()
	{
		base.Update();

		var player = Game.GetFirst(Masks.Player);
		if (player == null)
			return;

		var toPlayer = player.Position - Position;
		var absX = MathF.Abs(toPlayer.X);
		var absY = MathF.Abs(toPlayer.Y);
		if (absX > 1)
			Facing = MathF.Sign(toPlayer.X);

		switch (state)
		{
			case States.Sleeping:
				Shift.Y = 0;
				if (absX < WakeRange && absY < 32)
					SetState(States.Watching);
				break;

			case States.Watching:
				Shift.Y = MathF.Sin(Timer * 10) * 1;
				if (absX > WakeRange + 24 || absY > 48)
					SetState(States.Sleeping);
				else if (absX < ShootRange)
					SetState(States.Charging);
				break;

			case States.Charging:
				Shift.X = MathF.Sin(Timer * 50) * 1;
				if (stateTimer >= ChargeTime)
				{
					ShootAt(player.Position);
					SetState(States.Recovering);
				}
				break;

			case States.Recovering:
				Shift = Vector2.Zero;
				if (stateTimer >= RecoverTime)
					SetState(absX < WakeRange && absY < 48 ? States.Watching : States.Sleeping);
				break;
		}

		stateTimer += Time.Delta;
	}

	private void SetState(States next)
	{
		state = next;
		stateTimer = 0;
	}

	private void ShootAt(Point2 target)
	{
		var direction = (Vector2)(target - Position);
		if (direction.LengthSquared() <= 0.01f)
			direction = new Vector2(Facing, 0);
		else
			direction = Vector2.Normalize(direction);

		Game.Create<Bullet>(Position + new Point2((int)Facing * 8, -8), bullet =>
		{
			bullet.Velocity = direction * 72;
			bullet.dissolves = true;
		});
		AudioEvent("bubble");
	}

	public override void OnWasHit(Actor by)
	{
		Health--;
		if (Health <= 0)
		{
			Game.Create<Pop>(Position + new Point2(0, -8));
			Game.Destroy(this);
			return;
		}

		SetState(States.Recovering);
		Velocity.X = MathF.Sign(Position.X - by.Position.X) * 80;
	}
}
