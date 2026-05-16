using Foster.Framework;

namespace TinyLink;

public class Pop : Actor
{
	public string SpriteName = "pop";
	public string AnimationName = "pop";

	public Pop()
	{
		Depth = -20;
	}

	public override void Added()
	{
		Sprite = Assets.GetSprite(SpriteName);
		Play(AnimationName, false);
	}

	public override void Update()
	{
		base.Update();

		if (IsFinishedPlaying())
			Game.Destroy(this);
	}

	public override void RenderNormals(Batcher batcher) { }
}
