using Foster.Framework;
namespace Myth;

// A marker for where the player should start in a room, if they are first spawned there.
[GameActor(Sprite = "player_start")]
public class PlayerStart : Actor
{
	public PlayerStart()
	{
		Sprite = Assets.GetSprite("player_start");
		CollidesWithSolids = false;
	}

	public override void Render(Batcher batcher)
	{
	}

	public override void RenderShadow(Batcher batcher, Color color) { }

	public override void RenderReflection(Batcher batcher, Color color) { }

	public override void RenderNormals(Batcher _) { }

	public override void RenderEmissive(Batcher batcher)
	{
		if (!Game.Manager.playing)
			base.Render(batcher);
	}
}
