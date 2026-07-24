using Foster.Framework;

namespace Myth;

[GameActor(Anchor = SpriteAnchor.TopLeft)]
public class Goal : Actor
{
	public string Name = "";

	public Goal()
	{
		Mask = Masks.None;
		Depth = -100;
	}

	public override void Added()
	{
		base.Added();
		Hitbox = Hitbox.FromSprite(Sprite, Anchor);
	}

	public bool ReachedBy(Actor actor)
		=> (Hitbox.Rect + Position).Inflate(1).Overlaps(actor.Hitbox.Rect + actor.Position);

	bool ShouldRender => Game.Manager.CurrentRunGoalId() == ActorId || Game.Editing;

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
