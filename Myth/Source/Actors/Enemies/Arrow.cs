using System.Numerics;
using Foster.Framework;

namespace Myth;

public class Arrow : Actor
{
	public Arrow()
	{
		Hitbox = new(new RectInt(-4, -4, 8, 8));
		Mask = Masks.Enemy;
		Sprite = Assets.GetSprite("arrow");
		Depth = -5;
	}

	public override void OnCollideX()
	{
		Game.Destroy(this);
	}

	public override void OnPerformHit(Actor hitting) => Pop();
	public override void OnWasHit(Actor by) => Pop();

	void Pop()
	{
		Game.HitStun(0.1f);
		Game.Destroy(this);
		Game.Create<Pop>(Position);
	}
}
