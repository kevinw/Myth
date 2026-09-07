using System.Numerics;
using Foster.Framework;

namespace Myth;

[GameActor(Sprite = "blob", Tags = ActorTag.Enemy)]
public class Pouncer : Actor
{
	private enum States
	{
		Waiting,
		Crouching,
		Leaping,
		Recovering
	}

	[I(1, 10)] public int Health = 2;
	[I(24, 224)] public float TriggerRange = 88;
	[I(16, 128)] public float VerticalRange = 42;
	[I(0.1f, 1.5f)] public float CrouchTime = 0.35f;
	[I(0.1f, 2)] public float RecoverTime = 0.55f;
	[I(20, 180)] public float LeapSpeedX = 78;
	[I(40, 240)] public float LeapSpeedY = 120;
	[I(100, 800)] public float Gravity = 360;

	private States state = States.Waiting;
	private float stateTimer;
	private bool wasGrounded = true;

	public Pouncer()
	{
		Sprite = Assets.GetSprite("blob");
		Hitbox = new(new RectInt(-4, -8, 8, 8));
		Mask = Masks.Enemy;
		Depth = -5;
		Play("idle");
	}

	public override void Update()
	{
		base.Update();

		Velocity.Y += Gravity * Time.Delta;
		var grounded = Grounded();
		var player = Game.GetFirst(Masks.Player);

		switch (state)
		{
			case States.Waiting:
				Velocity.X = Calc.Approach(Velocity.X, 0, 500 * Time.Delta);
				if (grounded && player != null && CanSeePlayer(player))
				{
					Facing = MathF.Sign(player.Position.X - Position.X) is { } sign && sign != 0 ? sign : Facing;
					SetState(States.Crouching);
				}
				break;

			case States.Crouching:
				Velocity.X = Calc.Approach(Velocity.X, 0, 600 * Time.Delta);
				Shift.Y = MathF.Sin(stateTimer * 45) * 1;
				Squish = Vector2.Lerp(new Vector2(1.35f, 0.65f), Vector2.One, MathF.Min(1, stateTimer / CrouchTime));
				if (!grounded)
				{
					SetState(States.Leaping);
				}
				else if (stateTimer >= CrouchTime)
				{
					var direction = player != null ? MathF.Sign(player.Position.X - Position.X) : (int)Facing;
					if (direction == 0)
						direction = Facing;
					Facing = direction;
					Velocity.X = direction * LeapSpeedX;
					Velocity.Y = -LeapSpeedY;
					Squish = new Vector2(0.65f, 1.45f);
					Play("jump", restart: true);
					SetState(States.Leaping);
				}
				break;

			case States.Leaping:
				if (Velocity.X != 0)
					Facing = MathF.Sign(Velocity.X);
				if (grounded && !wasGrounded)
				{
					Game.Shake(0.025f);
					Squish = new Vector2(1.50f, 0.55f);
					Play("idle");
					SetState(States.Recovering);
				}
				break;

			case States.Recovering:
				Velocity.X = Calc.Approach(Velocity.X, 0, 450 * Time.Delta);
				if (stateTimer >= RecoverTime)
					SetState(States.Waiting);
				break;
		}

		wasGrounded = grounded;
		stateTimer += Time.Delta;
	}

	private bool CanSeePlayer(Actor player)
		=> MathF.Abs(player.Position.X - Position.X) <= TriggerRange &&
		   MathF.Abs(player.Position.Y - Position.Y) <= VerticalRange;

	private void SetState(States next)
	{
		state = next;
		stateTimer = 0;
		Shift = Vector2.Zero;
	}

	public override void OnCollideX()
	{
		base.OnCollideX();
		if (state == States.Leaping)
			SetState(States.Recovering);
	}

	internal override void OnWasHit(HitInfo hitInfo)
	{
		Health--;
		Game.HitStun(0.1f);
		if (Health <= 0)
		{
			Game.Create<Pop>(Position + new Point2(0, -4));
			Game.Destroy(this);
			return;
		}

		Velocity.X = MathF.Sign(Position.X - hitInfo.By.Position.X) * 120;
		Velocity.Y = -60;
		SetState(States.Recovering);
	}
}
