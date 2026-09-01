using System.Numerics;
using Foster.Framework;

namespace Myth;

[GameActor(Sprite = "spitter", Tags = ActorTag.Enemy)]
public class WallSpitter : WallFollowerActor
{
	[I(1, 10)] public int Health = 2;
	[I(0.25f, 6)] public float ShotInterval = 2.0f;
	[I(32, 320)] public float ShotRange = 120;
	[I(20, 240)] public float BulletSpeed = 100;

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

	internal override void OnWasHit(HitInfo hitInfo)
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
