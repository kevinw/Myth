using System.Numerics;
using Foster.Framework;

namespace Myth;

[GameActor(Tags = ActorTag.Enemy)]
public class Blob : Actor
{
	[I(1, 10)] public int Health = 3;
	[I(0.25f, 5)] public float JumpTimer = 2;
	private bool grounded = true;

	public Blob()
	{
		Hitbox = new(new RectInt(-4, -8, 8, 8));
		Mask = Masks.Enemy;
		Depth = -5;
		Play("fly");

	}

	public override void Added() {
		JumpTimer -= Game.rng.Float(0, 1);
		if (JumpTimer < 0) JumpTimer = 0;
	}

	public override void Update()
	{
		base.Update();

		Velocity.Y += 300 * Time.Delta;

		if (Grounded())
		{
			if (!grounded)
			{
				Play("idle");
				Squish = new Vector2(1.5f, 0.50f);
				AudioEvent("blob/land");
			}

			Velocity.X = Calc.Approach(Velocity.X, 0, 400 * Time.Delta);
			JumpTimer -= Time.Delta;
			grounded = true;
		}
		else
		{
			grounded = false;
		}

		if (JumpTimer <= 0)
		{
			Play("jump");
			AudioEvent("blob/jump");
			JumpTimer = Game.rng.Chance(0.4f) ? Game.rng.Float(2.4f, 2.9f) : Game.rng.Float(1.5f, 2.1f);
			if (Game.rng.Chance(0.20f))
			{
				Velocity.Y = -130;
			}
			else
			{
				Velocity.Y = -90;
			}
			Squish = new Vector2(0.5f, 1.5f);

			if (Game.GetFirst(Masks.Player) is Actor player)
			{
				var dir = MathF.Sign(player.Position.X - Position.X);
				if (dir == 0) dir = 1;

				Facing = dir;
				Velocity.X = Facing * 40;
			}
		}
	}

	internal override void OnWasHit(HitInfo hitInfo)
	{
		Health--;
		Game.HitStun(0.09f);

		if (Health <= 0)
		{
			Game.Create<Pop>(Position + new Point2(0, -4));
			Game.Destroy(this);
		}
		else
		{
			var sign = MathF.Sign(Position.X - hitInfo.By.Position.X);
			Velocity.X = sign * 120;
		}
	}
}
