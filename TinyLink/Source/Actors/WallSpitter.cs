using System.Numerics;
using Foster.Framework;

namespace TinyLink;

public class WallSpitter : WallFollowerActor
{
	public int Health = 2;
	public float ShotInterval = 2.0f;
	public float ShotRange = 120;
	public float BulletSpeed = 100;

	private float shotTimer;

	public WallSpitter()
	{
		Hitbox = new(new RectInt(-5, -6, 11, 10));
		Mask = Masks.Enemy;
		Sprite = Assets.GetSprite("wall_spitter");
		Depth = -5;
		CollidesWithSolids = false;
		Speed = 26;
		Gravity = 360;
		Play("idle");
	}

	public override void Added()
	{
		base.Added();
		shotTimer = ShotInterval * 0.5f;
	}

	protected override void AfterWallFollowerUpdate()
	{
		if (IsPlaying("shoot") && IsFinishedPlaying())
			Play("idle");

		shotTimer = Calc.Approach(shotTimer, 0, Time.Delta);
		if (shotTimer > 0)
			return;

		if (Game.GetFirst(Masks.Player) is not { } player)
			return;

		var delta = player.Position - Position;
		if (delta.LengthSquared() > ShotRange * ShotRange)
			return;

		var direction = Math.Sign(delta.X);
		if (direction == 0)
			direction = (int)Facing;
		if (direction == 0)
			direction = 1;

		Facing = direction;
		Play("shoot", false, restart: true);
		shotTimer = ShotInterval;

		Game.Create<WallBullet>(Position + new Point2(direction * 8, 0), bullet =>
		{
			bullet.Facing = direction;
			bullet.Speed = BulletSpeed;
		});
		AudioEvent("bubble");
	}

	public override void OnWasHit(Actor by)
	{
		Health--;
		if (Health <= 0)
		{
			Game.Create<Pop>(Position);
			Game.Destroy(this);
			return;
		}

		Squish = new Vector2(1.35f, 0.75f);
	}
}
