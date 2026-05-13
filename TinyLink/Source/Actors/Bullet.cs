using System.Numerics;
using Foster.Framework;

namespace TinyLink;

public class Bullet : Actor
{
	public bool dissolves = false;
	const float Gravity = 130;

	public Bullet()
	{
		Hitbox = new(new RectInt(-4, -4, 8, 8));
		Mask = Masks.Enemy;
		Sprite = Assets.GetSprite("bullet");
		Depth = -5;
		Play("idle");
	}

	public override void Update()
	{
		base.Update();

		Velocity.Y += Gravity * Time.Delta;

		if (Timer > 2.5f && Time.BetweenInterval(0.05f))
			Visible = !Visible;

		if (Timer > 3.0f)
			Game.Destroy(this);
	}

	public override void OnCollideX()
	{
		if (dissolves)
		{
			Game.Create<Pop>(Position, (actor) =>
			{
				actor.SpriteName = "bullet";
				actor.AnimationName = "dissolve";
				actor.Velocity = Velocity * 0.25f;
			});
		}

		Game.Destroy(this);
	}

	public override void OnCollideY()
	{
		Velocity.Y = -60;
		AudioEvent("bubble_bounce");
	}

	public override void OnPerformHit(Actor hitting) => Pop();
	public override void OnWasHit(Actor by) => Pop();

	void Pop()
	{
		Game.HitStun(0.1f);
		Game.Destroy(this);
		Game.Create<Pop>(Position);
		AudioEvent("bubble");
	}
}
