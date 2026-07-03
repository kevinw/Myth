using Foster.Framework;
using System.Numerics;

namespace Myth;

[GameActor(Tags = ActorTag.Enemy)]
public class ShieldKnight : Actor
{
	enum States
	{
		Patrol,
		Charging
	}

	int health = 2;
	States state = States.Patrol;
	static RectInt ShieldHitbox => new(4, -18, 2, 18);
	static float Gravity => 360f;

	public ShieldKnight()
	{
		Mask = Masks.Enemy;
		Depth = -5;
	}

	public override void Added()
	{
		Hitbox = Hitbox.FromSprite(Sprite);
		Play("idle");
	}

	float turnTimer = 2.0f;

	public override void Update() {
		base.Update();

		var player = Game.GetFirst<Player>();
		if (player == null) {
			Play("idle");
			return;
		}

		if (turnTimer > 0)
		{
			turnTimer -= Time.Delta;
			if (turnTimer < 0)
			{
				turnTimer = 0;
				Play("walk");
				Facing *= -1;
			}
		}
		else
		{
			Velocity.X = Facing * 10;
			var facingPlayer = Math.Sign(Position.X - player.Position.X);
			if (facingPlayer == Facing)
			{
				turnTimer = 0.9f;
				Play("idle");
				Velocity.X = 0;
			}
		}

		Velocity.Y += Gravity * Time.Delta;

	}

	public override bool BlocksAttack(Actor attacker, in RectInt attackPath)
		=> MathF.Sign(attacker.Position.X - Position.X) == Facing &&
			attackPath.Overlaps(ShieldHitbox.ScaleX((int)Facing) + Position);

	public override void OnWasHit(Actor by)
	{
		if (health <= 0) return;

		health--;
		if (health <= 0) {
			for (int y = 0; y < 2; y ++)
				Game.Create<Pop>(Position + new Point2(0, -8 + y * 12));
			Game.HitStun(0.3f);
			Game.Destroy(this);
		}
	}
}
