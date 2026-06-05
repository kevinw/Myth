using Foster.Framework;

namespace TinyLink;

public class Goal : Actor
{
	public Goal()
	{
		Sprite = Assets.GetSprite("goal");
		if (Sprite != null && Sprite.Frames.Count > 0)
		{
			Hitbox = Hitbox.FromSprite(Sprite);
		}
		Mask = Masks.None;
		Depth = -100;
	}
}
