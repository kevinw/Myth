using Foster.Framework;

namespace Myth;

public class Pop : Actor
{
	public string SpriteName = "pop";
	public string AnimationName = "pop";

	[Flags]
	public enum PopFlags
	{
		None = 0,
		Emissive = 1,
		AnimateDuringHitStun = 2,
	}

	public PopFlags Flags = PopFlags.None;

	public Pop()
	{
		Depth = -20;
	}

	public override void Added()
	{
		Sprite = Assets.GetSprite(SpriteName);
		Play(AnimationName, false);
		if (Flags.Has(PopFlags.AnimateDuringHitStun))
		{
			Game.HitStunActors.Add(this);
		}
	}

	public override void Destroyed()
	{
		if (Flags.Has(PopFlags.AnimateDuringHitStun))
		{
			Game.HitStunActors.Remove(this);
		}
	}

	public override void Update()
	{
		base.Update();

		if (IsFinishedPlaying())
			Game.Destroy(this);
	}

	public override void RenderNormals(Batcher batcher) { }
	public override void RenderEmissive(Batcher batch) {
		if (Flags.Has(PopFlags.Emissive))
			Render(batch);
	}
	public override void RenderLightEmission(Batcher batcher) => RenderEmissive(batcher);
}
