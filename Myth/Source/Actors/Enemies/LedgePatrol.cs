using System.Numerics;
using Foster.Framework;

namespace Myth;

[GameActor(Sprite = "blob", Tags = ActorTag.Enemy)]
public class LedgePatrol : Actor
{
	private enum States
	{
		Patrol,
		Alert,
		Stunned
	}

	public int Health = 2;
	public float PatrolSpeed = 34;
	public float AlertSpeed = 58;
	public float AlertRange = 72;
	public float StunnedDuration = 0.45f;

	private States state = States.Patrol;
	private float stateTimer;

	public LedgePatrol()
	{
		Sprite = Assets.GetSprite("blob");
		Hitbox = new(new RectInt(-4, -8, 8, 8));
		Mask = Masks.Enemy;
		Depth = -5;
		Facing = Signs.Positive;
		Play("idle");
	}

	public override void Update()
	{
		base.Update();

		Velocity.Y += 360 * Time.Delta;

		if (Grounded() && state != States.Stunned)
		{
			if (ShouldTurnAround())
				TurnAround();

			var player = Game.GetFirst(Masks.Player);
			var seesPlayer = player != null &&
				MathF.Sign(player.Position.X - Position.X) == Facing &&
				MathF.Abs(player.Position.X - Position.X) < AlertRange &&
				MathF.Abs(player.Position.Y - Position.Y) < 24;

			SetState(seesPlayer ? States.Alert : States.Patrol);
			Velocity.X = Facing * (state == States.Alert ? AlertSpeed : PatrolSpeed);
		}
		else if (state == States.Stunned)
		{
			Velocity.X = Calc.Approach(Velocity.X, 0, 250 * Time.Delta);
			if (stateTimer >= StunnedDuration)
				SetState(States.Patrol);
		}

		Play(state == States.Alert ? "jump" : "idle");
		stateTimer += Time.Delta;
	}

	private bool ShouldTurnAround()
	{
		var forward = new Point2((int)Facing, 0);
		var wallProbe = new Point2((int)Facing, 0);
		if (OverlapsAny(wallProbe, Masks.Solid))
			return true;

		var floorProbe = new Point2((int)Facing * 5, 2);
		if (!OverlapsAny(floorProbe, Masks.Solid))
			return true;

		var lowFloorProbe = new Point2((int)Facing * 7, 8);
		return !OverlapsAny(lowFloorProbe, Masks.Solid) && !OverlapsAny(forward + Point2.Down, Masks.Solid);
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
	}

	public override void OnCollideX()
	{
		base.OnCollideX();
		TurnAround();
	}

	public override void OnWasHit(Actor by)
	{
		Health--;
		if (Health <= 0)
		{
			Game.Create<Pop>(Position + new Point2(0, -4));
			Game.Destroy(this);
			return;
		}

		Facing = MathF.Sign(Position.X - by.Position.X) is { } sign && sign != 0 ? sign : -Facing;
		Velocity.X = Facing * 90;
		Squish = new Vector2(1.35f, 0.70f);
		SetState(States.Stunned);
	}
}
