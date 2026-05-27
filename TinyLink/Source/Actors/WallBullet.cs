using Foster.Framework;

namespace TinyLink;

public class WallBullet : WallFollowerActor
{
	public bool dissolves = false;

	public WallBullet()
	{
		Hitbox = new(new RectInt(-4, -4, 8, 8));
		Mask = Masks.Enemy;
		Sprite = Assets.GetSprite("bullet");
		Depth = -5;
		CollidesWithSolids = false;
		Play("idle");
	}

	protected override void AfterWallFollowerUpdate()
	{
		if (Timer > 2.5f && Time.BetweenInterval(0.05f))
			Visible = !Visible;

		if (Timer > 3.0f)
			Game.Destroy(this);
	}

	public override void OnPerformHit(Actor hitting) => Pop();
	public override void OnWasHit(Actor by) => Pop();

	private void Pop()
	{
		Game.HitStun(0.1f);
		Game.Destroy(this);
		Game.Create<Pop>(Position);
		AudioEvent("bubble");
	}
}
