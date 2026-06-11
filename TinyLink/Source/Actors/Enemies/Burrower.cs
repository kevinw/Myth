using System.Numerics;
using Foster.Framework;

namespace TinyLink;

public class Burrower : Actor
{
	private enum States
	{
		Hidden,
		Emerging,
		Charging,
		Tired
	}

	public int Health = 2;
	public float TriggerRange = 72;
	public float ChargeSpeed = 95;
	public float ChargeDuration = 1.35f;
	public float TiredDuration = 0.85f;

	private States state = States.Hidden;
	private float stateTimer;
	private int chargeDirection = 1;

	public Burrower()
	{
		Sprite = Assets.GetSprite("blob");
		Hitbox = new(new RectInt(-4, -8, 8, 8));
		Mask = Masks.Enemy;
		Depth = -5;
		Play("idle");
		SetState(States.Hidden);
	}

	public override void Update()
	{
		base.Update();

		var player = Game.GetFirst(Masks.Player);
		switch (state)
		{
			case States.Hidden:
				Velocity = Vector2.Zero;
				Shift.Y = 5;
				if (player != null && MathF.Abs(player.Position.X - Position.X) < TriggerRange && player.Position.Y < Position.Y + 8)
				{
					chargeDirection = MathF.Sign(player.Position.X - Position.X) is { } sign && sign != 0 ? (int)sign : 1;
					Facing = chargeDirection;
					Squish = new Vector2(1.6f, 0.45f);
					SetState(States.Emerging);
				}
				break;

			case States.Emerging:
				Shift.Y = Calc.Approach(Shift.Y, 0, Time.Delta * 20);
				if (stateTimer >= 0.28f)
				{
					Velocity.X = chargeDirection * ChargeSpeed;
					Velocity.Y = -45;
					Squish = new Vector2(0.65f, 1.35f);
					SetState(States.Charging);
				}
				break;

			case States.Charging:
				Velocity.Y += 360 * Time.Delta;
				Facing = MathF.Sign(Velocity.X) == 0 ? Facing : MathF.Sign(Velocity.X);
				if (stateTimer >= ChargeDuration)
					SetState(States.Tired);
				break;

			case States.Tired:
				Velocity.Y += 360 * Time.Delta;
				if (Grounded())
					Velocity.X = Calc.Approach(Velocity.X, 0, 300 * Time.Delta);
				if (stateTimer >= TiredDuration)
					SetState(States.Hidden);
				break;
		}

		stateTimer += Time.Delta;
	}

	public override void OnCollideX()
	{
		base.OnCollideX();
		if (state == States.Charging)
		{
			Game.Shake(0.04f);
			SetState(States.Tired);
		}
	}

	private void SetState(States next)
	{
		state = next;
		stateTimer = 0;
		if (next == States.Hidden)
		{
			Mask = Masks.None;
			Velocity = Vector2.Zero;
			Play("idle");
		}
		else
		{
			Mask = Masks.Enemy;
		}

		if (next == States.Charging)
		{
			Play("jump");
		}
	}

	public override void OnWasHit(Actor by)
	{
		if (state == States.Hidden)
		{
			SetState(States.Emerging);
			return;
		}

		Health--;
		if (Health <= 0)
		{
			Game.Create<Pop>(Position + new Point2(0, -4));
			Game.Destroy(this);
			return;
		}

		Velocity.X = MathF.Sign(Position.X - by.Position.X) * 120;
		SetState(States.Tired);
	}
}
