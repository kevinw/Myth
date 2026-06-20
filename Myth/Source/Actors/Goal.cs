using Foster.Framework;

namespace Myth;

public class Goal : Actor
{
	public string Name = "";

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
	public override void Render(Batcher batcher)
	{
		if (Game.Manager.playing) return;
		base.Render(batcher);
	}
	public override void RenderNormals(Batcher batcher)
	{
		if (Game.Manager.playing) return;
		base.RenderNormals(batcher);
	}
}
