using Foster.Framework;

namespace Myth;

[GameActor]
public class Goal : Actor
{
	public string Name = "";

	public Goal()
	{
		if (Sprite != null && Sprite.Frames.Count > 0)
		{
			Hitbox = Hitbox.FromSprite(Sprite);
		}
		Mask = Masks.None;
		Depth = -100;
	}

	bool ShouldRender => Game.Manager.CurrentRunGoalId() == ActorId;

	public override void Render(Batcher batcher)
	{
		if (!ShouldRender) return;
		base.Render(batcher);
	}

	public override void RenderNormals(Batcher batcher)
	{
		if (!ShouldRender) return;
		base.RenderNormals(batcher);
	}
}
