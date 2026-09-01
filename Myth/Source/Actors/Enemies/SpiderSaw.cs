using Foster.Framework;

namespace Myth;

[GameActor(HideFromPalette = true)]
public class SpiderSaw : WallFollowerActor
{
	public Point2 InitialSurfaceDirection = Point2.Down;

	public SpiderSaw()
	{
		Hitbox = new(new RectInt(-3, -3, 6, 6));
		Mask = Masks.Enemy;
		Sprite = Assets.GetSprite("saw");
		Depth = -5;
		CollidesWithSolids = false;
		Speed = 72;
	}

	public override void Added()
	{
		SetInitialSurfaceDirection(InitialSurfaceDirection);
		TryUnstick(-InitialSurfaceDirection.Normalized());
		base.Added();
	}

	protected override void AfterWallFollowerUpdate()
	{
		if (Timer > 4.5f && Time.BetweenInterval(0.05f))
			Visible = !Visible;
		if (Timer > 5f)
			Game.Destroy(this);
	}

	public override void OnPerformHit(Actor hitting) => Pop();
	internal override void OnWasHit(HitInfo hitInfo) => Pop();

	void Pop()
	{
		Game.Destroy(this);
		Game.Create<Pop>(Position);
		AudioEvent("bubble");
	}
}
