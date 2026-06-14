using System.Numerics;
using Foster.Framework;

namespace Myth;

public class Spitter : Actor
{
	private float timer = 0;

	public Spitter()
	{
		Hitbox = new(new RectInt(-6, -12, 12, 12));
		Mask = Masks.Enemy;
		Sprite = Assets.GetSprite("spitter");
		Depth = -5;
		timer = 1.0f;
		Play("idle");
		Facing = 1;
	}

	public override void Update()
	{
		base.Update();

		timer -= Time.Delta;
		if (timer <= 0)
		{
			Play("shoot", false);
			timer = 3.0f;

			var bullet = Game.Create<Bullet>(Position + new Point2(-8 * Facing, -8), (actor) =>
			{
				actor.Velocity = new Vector2(-40 * Facing, 0);
				actor.dissolves = true;
			});
		}

		if (IsPlaying("shoot") && IsFinishedPlaying())
			Play("idle");
	}

	public override void OnWasHit(Actor by)
	{
		Game.HitStun(0.1f);
		Game.Destroy(this);
		Game.Create<Pop>(Position + new Point2(0, -4));
	}
}
