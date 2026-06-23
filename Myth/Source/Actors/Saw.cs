using Foster.Framework;

namespace Myth;

public class Saw : WallFollowerActor
{
	public Saw()
	{
		Hitbox = new(new RectInt(-2, -2, 4, 4));
		Mask = Masks.None;
		Depth = -5;
		CollidesWithSolids = false;
	}

	protected override void AfterWallFollowerUpdate()
	{
		if (Timer > 2.5f && Time.BetweenInterval(0.05f))
			Visible = !Visible;

		if (Timer > 3.0f)
			Game.Destroy(this);
	}
}
