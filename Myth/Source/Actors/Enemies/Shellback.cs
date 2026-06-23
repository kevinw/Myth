using System.Numerics;
using Foster.Framework;

namespace Myth;

[GameActor(Sprite = "blob", Tags = ActorTag.Enemy)]
public class Shellback : Actor
{
	private enum States
	{
		Patrol,
		Charging,
		Stunned
	}

	public int Health = 3;
	public float PatrolSpeed = 24;
	public float ChargeSpeed = 92;
	public float AlertRange = 96;
	public float StunTime = 0.75f;
	public float Gravity = 360;

	private States state = States.Patrol;
	private float stateTimer;

	public Shellback()
	{
		Sprite = Assets.GetSprite("blob");
		Hitbox = new(new RectInt(-5, -8, 10, 8));
		Mask = Masks.Enemy;
		Depth = -5;
		Facing = Signs.Negative;
		Play("idle");
	}

	public override void Update()
	{
		base.Update();

		Velocity.Y += Gravity * Time.Delta;
		var player = Game.GetFirst(Masks.Player);

		switch (state)
		{
			case States.Patrol:
				if (Grounded())
				{
					if (ShouldTurnAround())
						TurnAround();

					Velocity.X = Facing * PatrolSpeed;
					if (player != null && SeesPlayer(player))
						SetState(States.Charging);
				}
				break;

			case States.Charging:
				Velocity.X = Facing * ChargeSpeed;
				Shift.X = MathF.Sin(stateTimer * 45) * 1;
				if (Grounded() && ShouldTurnAround())
				{
					Game.Shake(0.035f);
					TurnAround();
					SetState(States.Stunned);
				}
				break;

			case States.Stunned:
				Velocity.X = Calc.Approach(Velocity.X, 0, 420 * Time.Delta);
				Shift.X = MathF.Sin(stateTimer * 32) * 1;
				if (stateTimer >= StunTime)
					SetState(States.Patrol);
				break;
		}

		stateTimer += Time.Delta;
	}

	private bool SeesPlayer(Actor player)
		=> MathF.Sign(player.Position.X - Position.X) == Facing &&
		   MathF.Abs(player.Position.X - Position.X) < AlertRange &&
		   MathF.Abs(player.Position.Y - Position.Y) < 28;

	private bool ShouldTurnAround()
	{
		if (OverlapsAny(new Point2((int)Facing, 0), Masks.Solid))
			return true;

		var floorProbe = new Point2((int)Facing * 6, 3);
		return !OverlapsAny(floorProbe, Masks.Solid);
	}

	private void TurnAround()
	{
		Facing = -Facing;
		Velocity.X = 0;
		Squish = new Vector2(0.75f, 1.25f);
	}

	private void SetState(States next)
	{
		if (state == next)
			return;

		state = next;
		stateTimer = 0;
		Shift = Vector2.Zero;
	}

	public override void OnCollideX()
	{
		base.OnCollideX();
		TurnAround();
		SetState(States.Stunned);
	}

	public override void OnWasHit(Actor by)
	{
		var hitFromFront = MathF.Sign(by.Position.X - Position.X) == Facing;
		if (hitFromFront && state != States.Stunned)
		{
			Velocity.X = -Facing * 70;
			Squish = new Vector2(1.35f, 0.70f);
			SetState(States.Stunned);
			return;
		}

		Health--;
		if (Health <= 0)
		{
			Game.Create<Pop>(Position + new Point2(0, -4));
			Game.Destroy(this);
			return;
		}

		Velocity.X = MathF.Sign(Position.X - by.Position.X) * 130;
		SetState(States.Stunned);
	}
}
