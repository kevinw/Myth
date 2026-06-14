using Foster.Framework;
using Myth;

public class Peter : Actor
{
	public Peter()
	{
		Sprite = Assets.GetSprite("peter");
		Mask = Masks.Enemy;
		Hitbox = new(new RectInt(-8, -20, 16, 40));
		Play("idle");
	}

}
