using System.Numerics;
using Foster.Framework;

namespace Myth;

[GameActor(Sprite = "bramble", Tags = ActorTag.Enemy)]
public class Dropper : Actor
{
	private enum States
	{
		Hanging,
		Warning,
		Falling,
		Scuttling,
		Resetting
	}

	public int Health = 1;
	public float TriggerWidth = 24;
	public float TriggerHeight = 92;
	public float WarningTime = 0.35f;
	public float ScuttleTime = 1.4f;

	private States state = States.Hanging;
	private float stateTimer;
	private Point2 home;
	private int scuttleDirection = 1;

	public Dropper()
	{
		Sprite = Assets.GetSprite("bramble");
		Hitbox = new(new RectInt(-4, -8, 8, 8));
		Mask = Masks.Enemy;
		Depth = -5;
		CollidesWithSolids = false;
		Play("idle");
	}

	public override void Added()
	{
		home = Position;
	}

	public override void Update()
	{
		base.Update();

		var player = Game.GetFirst(Masks.Player);
		switch (state)
		{
			case States.Hanging:
				Velocity = Vector2.Zero;
				Position = home;
				Shift.Y = MathF.Sin(Timer * 8) * 1;
				if (player != null && player.Position.Y > Position.Y && player.Position.Y < Position.Y + TriggerHeight && MathF.Abs(player.Position.X - Position.X) < TriggerWidth)
				{
					scuttleDirection = MathF.Sign(player.Position.X - Position.X) >= 0 ? 1 : -1;
					Facing = scuttleDirection;
					SetState(States.Warning);
				}
				break;

			case States.Warning:
				Velocity = Vector2.Zero;
				Shift.X = MathF.Sin(Timer * 42) * 1;
				if (stateTimer >= WarningTime)
				{
					CollidesWithSolids = true;
					SetState(States.Falling);
				}
				break;

			case States.Falling:
				Shift = Vector2.Zero;
				Velocity.Y += 520 * Time.Delta;
				if (Grounded())
				{
					Velocity.X = scuttleDirection * 55;
					Velocity.Y = 0;
					SetState(States.Scuttling);
				}
				break;

			case States.Scuttling:
				Velocity.Y += 360 * Time.Delta;
				if (stateTimer >= ScuttleTime)
					SetState(States.Resetting);
				break;

			case States.Resetting:
				CollidesWithSolids = false;
				Velocity = Vector2.Zero;
				Position = (Point2)Calc.Approach((Vector2)Position, home, 80 * Time.Delta);
				if ((Position - home).LengthSquared() <= 1)
				{
					Position = home;
					SetState(States.Hanging);
				}
				break;
		}

		stateTimer += Time.Delta;
	}

	public override void OnCollideX()
	{
		base.OnCollideX();
		scuttleDirection *= -1;
		Facing = scuttleDirection;
		if (state == States.Scuttling)
			Velocity.X = scuttleDirection * 55;
	}

	private void SetState(States next)
	{
		state = next;
		stateTimer = 0;
		if (next == States.Hanging)
		{
			CollidesWithSolids = false;
			Shift = Vector2.Zero;
		}
	}

	public override void OnWasHit(Actor by)
	{
		Health--;
		if (Health <= 0)
		{
			Game.Create<Pop>(Position);
			Game.Destroy(this);
		}
	}
}
