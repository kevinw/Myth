using Foster.Framework;

namespace Myth;

[GameActor(Tags = ActorTag.Enemy | ActorTag.Hazard, LivePositionEditable = true)]
public class Bramble : Actor
{
	public Bramble()
	{
		Hitbox = new(new RectInt(-4, -8, 8, 8));
		Mask = Masks.Hazard;
		Play("idle");
	}

	public override void OnPerformHit(Actor hitting) => Pop();
	internal override void OnWasHit(HitInfo hitInfo) => Pop();

	public void Pop()
	{
		Game.Destroy(this);
		Game.Create<Pop>(Position + new Point2(0, -4));
		AudioEvent("glass");
	}
}
